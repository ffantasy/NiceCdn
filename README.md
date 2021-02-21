# NiceCdn
## 用于查找最佳的cloudflare服务器IP，适用于v2ray+ws+cdn的FQ方案。

## 使用说明
1. 安装dotnet core3.1运行时：https://dotnet.microsoft.com/download/dotnet/3.1
2. 下载文件并解压：https://github.com/ffantasy/NiceCdn/releases
3. 运行NiceCdn.exe，耐性等待查找到合适服务器后会有beep提示音，然后关闭窗口。
4. 修改原有的v2ray的配置：服务器地址修改为nicecdn，伪装域名修改为原有的域名地址。
5. 最好重启一下v2ray服务。

## 配置说明
可以根据需要修改nicecdn.json配置文件：
+ cfWork: cloudflare的worker服务，用于测试反向代理下载速度，可以保持不变，也可以自建，worker代码如下：
```javascript
    addEventListener(
      "fetch",event => {
        let url=event.request.url;
        let pos=url.lastIndexOf('/');
        let req=decodeURIComponent(url.substr(pos+1));
        url=new URL(req);

        let request=new Request(url,event.request);
        console.log(event.request);
        event.respondWith(
          fetch(request)
        )
      }
    )
```
+ testFile: 用户下载测试的文件，vps服务商一般都提供了用于测速的下载文件，可以自行选择。也可以在自己的vps服务器放个文件用于测试。
+ testDuration: 下载持续时间，默认10秒，即下载10秒钟，计算10秒的平均下载速度，时间越长越测试速度越精确，但需要耗费更长时间。
+ goalSpeed：期望能达到的下载速度，不宜设置太高，否则可能长时间无法找到合适服务器。
+ ipRange: cloudflare服务器IP段。程序将在设定的IP段内查找能达到目标下载速度的服务器：在begin3-end3内随机选择一个，然后并发10个请求依次请求begin4-end4内的地址。如果没有符合的服务器，将在begin3-end3内再次随机选择一个。服务器IP段可以查看cloudflare官方公布的地址，还有网友整理推荐的IP段。
   + prefix：固定IP段的前两位。
   + begin3：IP段第三位的开始地址。
   + end3：IP段第三位的结束地址。
   + begin4：IP段第四位的开始地址。
   + end4：IP段第四位的结束地址。
   
## 原理
v2ray+ws+cdn的方案可以拯救被封IP的主机，也能加速龟速服务器，还能有效防封。本着能省则省的原则，必然使用免费的cf服务，然而cf服务器对墙内不友好，直接使用cf服务返回的服务器可能速度并不理想，那么就需要自己去测试符合自己线路的服务器地址。

手工测试大量的IP地址是不现实的，必须依靠程序实现。一般方案是使用ping的方法去测试速度，然而实际情况会发现ping的结果很好，实际速度却只有龟速，基本无法使用。为了获得真实可用的连接速度，应当使用下载的方式测试连接速度。

本程序在找到合适的IP地址后，将在hosts文件中创建一条nicecdn的记录，并指向查找到的IP地址。修改v2ray的服务器地址为nicecdn，即指向查找到的IP地址。以后重新查找新的IP地址，都只需要重启v2ray服务器即可。

只要能找到适合的cdn服务器，龟速服务器也有救。可以实现全免费的FQ方案：Heroku+v2ray+ws+cf，无需花钱，无需注册域名，即可快乐FQ。

