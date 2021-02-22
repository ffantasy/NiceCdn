using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace NiceCdn
{
    class Config {
        public string CfWork { get; set; }
        public string TestFile { get; set; }
        public int TestDuration { get; set; }
        public int GoalSpeed { get; set; }
        public IpRange IpRange { get; set; }
    }

    class IpRange
    {
        public string Prefix { get; set; }
        public int Begin3 { get; set; } = 1;
        public int End3 { get; set; } = 254;
        public int Begin4 { get; set; } = 1;
        public int End4 { get; set; } = 254;
    }

    class Program
    {
        static readonly string HOST_FILE = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
        static readonly string SERVER_NAME = "nicecdn";

        static private Config cfg;
        static private ConcurrentDictionary<string, int> retDic = new ConcurrentDictionary<string, int>();

        public static async Task Main(string[] args)
        {
            var fs = new FileStream("nicecdn.json", FileMode.Open);
            cfg = await JsonSerializer.DeserializeAsync<Config>(fs, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip });

            Console.WriteLine($"try test {cfg.IpRange.Prefix} ...");
            await DownloadFromRangeAsync(cfg.IpRange);

            Console.WriteLine("OK. Press enter to exit.");
            Console.Beep();
            Console.ReadLine();
        }

        static async Task<PingReply> PingAsync(string target)
        {
            var pingSender = new Ping();
            var options = new PingOptions();
            options.DontFragment = true;
            var buf = new byte[32];
            Array.Fill(buf, (byte)0);
            PingReply reply = null;
            try
            {
                var ip = IPAddress.Parse(target);
                reply = await pingSender.SendPingAsync(ip, 3000, buf, options);
                if (reply.Status != IPStatus.Success)
                {
                    //Console.WriteLine(reply.Status);
                    return null;
                }
                if (reply.RoundtripTime > 200)
                {
                    //Console.WriteLine("exceed 260");
                    return null;
                }

                //Console.WriteLine("ok");
                return reply;
            }
            catch
            {
                return null;
            }
        }

        static async Task applyHostAsync(string ip)
        {
            var hostContent = await File.ReadAllTextAsync(HOST_FILE);

            Regex reg = new Regex($@"^.*? {SERVER_NAME}\r\n$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            hostContent = reg.Replace(hostContent, string.Empty);

            hostContent += $"{ip} {SERVER_NAME}\r\n";
            await File.WriteAllTextAsync(HOST_FILE, hostContent);
        }

        static async Task PingFromRangeAsync(IpRange ipRange)
        {
            Console.WriteLine("This will take several minutes, please wait ...");
            var i = new Random().Next(ipRange.Begin3, ipRange.End3);

            var cts = new CancellationTokenSource();
            var tasks = new List<Task>();
            var result = new List<bool>();

            var hasChange = false;
            for (int j = ipRange.Begin4; j <= ipRange.End4; j++)
            {
                var ip = $"{ipRange.Prefix}.{i}.{j}";
                //var a = await CheckAvailableAsync(ip);
                //continue;

                tasks.Add(Task.Run(async () =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var ret = await TestPingAsync(ip);
                    if (ret)
                    {
                        try
                        {
                            await applyHostAsync(ip);
                            hasChange = true;
                        }
                        catch
                        {
                        }
                        cts.Cancel();
                    }
                }, cts.Token));
            }
            await Task.WhenAll(tasks.ToArray());
            if (!hasChange)
            {
                await DownloadFromRangeAsync(ipRange);
                return;
            }

            Console.WriteLine("Server ip switched.");
        }

        static async Task DownloadFromRangeAsync(IpRange ipRange)
        {
            Console.WriteLine("This will take several minutes, please wait ...");
            var begin3 = new Random().Next(ipRange.Begin3, ipRange.End3);

            var cts = new CancellationTokenSource();
            var tasks = new List<Task>();
            KeyValuePair<string, int> maxItem;
            while (true)
            {
                var end4 = ipRange.Begin4 + 10;
                end4 = end4 > ipRange.End4 ? ipRange.End4 : end4;
                for (; ipRange.Begin4 <= end4; ipRange.Begin4++)
                {
                    var ip = $"{ipRange.Prefix}.{begin3}.{ipRange.Begin4}";
                    tasks.Add(Task.Run(async () =>
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        try
                        {
                            var ret = await TestDownloadAsync(ip, cfg.CfWork, cfg.TestFile, cfg.TestDuration);
                            retDic.TryAdd(ip, ret);
                        }
                        catch
                        {
                        }
                    }, cts.Token));
                }
                await Task.WhenAll(tasks.ToArray());
                tasks.Clear();

                maxItem = retDic.OrderByDescending(kvp => kvp.Value).First();
                retDic.Clear();
                if (maxItem.Value >= cfg.GoalSpeed)
                {
                    Console.WriteLine($"nice ip: {maxItem.Key} speed: {maxItem.Value}KB");
                    await applyHostAsync(maxItem.Key);
                    return;
                }
                if (end4 >= 254) break;
            }

            await DownloadFromRangeAsync(ipRange);
        }

        static async Task<bool> TestPingAsync(string ip) {
            for (int c = 0; c < 2; c++)
            {
                var failedCount = 0;
                var continueFailed = 0;
                for (int i = 0; i < 50; i++)
                {
                    var ret = await PingAsync(ip);
                    if (ret == null)
                    {
                        failedCount++;
                        continueFailed++;
                    }
                    else
                    {
                        continueFailed = 0;
                    }
                    if (failedCount >= 5 || continueFailed >= 2) return false;
                }
            }
            return true;
        }

        static async Task<int> TestDownloadAsync(string ip, string worker, string url, int durationSec)
        {
            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Add("Host", worker);
            hc.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.141 Safari/537.36");
            var s = await hc.GetStreamAsync($"https://{ip}/{HttpUtility.UrlEncode(url)}");

            var begin = DateTime.Now;
            var total = 0;
            var span = 0d;
            while (true)
            {
                var buf = new byte[1024 * 1024];
                var count = await s.ReadAsync(buf);
                total += count;
                if (count == 0) break;

                span = (DateTime.Now - begin).TotalSeconds;
                if (span >= durationSec) break;
            }

            hc.Dispose();
            var ret = (int)Math.Round(total / span / 1024);
            return ret;
        }
    }
}