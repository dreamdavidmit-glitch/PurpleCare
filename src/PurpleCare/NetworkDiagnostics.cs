using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace PurpleCare {
 public class NetworkCheck {public string Step{get;set;} public string Status{get;set;} public string Detail{get;set;}}
 public class NetworkDiagnosis {public string Time{get;set;} public string Host{get;set;} public string Summary{get;set;} public List<NetworkCheck> Checks=new List<NetworkCheck>();}
 public static class NetworkDiagnostics {
  public static bool ValidHost(string host){return !String.IsNullOrWhiteSpace(host)&&host.Length<=253&&host.Contains(".")&&Uri.CheckHostName(host)==UriHostNameType.Dns&&!host.Any(char.IsWhiteSpace);}
  public static bool UsableAddress(IPAddress ip){if(IPAddress.IsLoopback(ip)||ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.IPv6Any))return false;return ip.AddressFamily==AddressFamily.InterNetwork?!ip.ToString().StartsWith("169.254."):ip.AddressFamily==AddressFamily.InterNetworkV6&&!ip.IsIPv6LinkLocal&&!ip.IsIPv6Multicast;}
  public static NetworkCheck PingResult(IPStatus status,string address){return new NetworkCheck{Step="网关 · "+address,Status=status==IPStatus.Success?"收到回应":"未确认",Detail=status==IPStatus.Success?"网关回应了 ICMP 请求。":"未收到成功回应（"+status+"）。网关可能禁止 ICMP，不能据此认定断网。"};}
  public static NetworkCheck HttpResult(int code){return new NetworkCheck{Step="指定网站 HTTPS",Status=code>=200&&code<300?"收到成功响应":code>=300&&code<400?"收到重定向":"收到限制或错误响应",Detail="HTTP "+code+"；仅说明此次目标请求的结果。重定向未跟随，403/405 等可能是站点策略，不代表网卡故障。"};}
  static void Observe(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
  static void ObserveResponse(Task<WebResponse> task){task.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();else if(t.IsFaulted){var ignored=t.Exception;}});}
  public static async Task<NetworkDiagnosis> Run(string host,CancellationToken cancel,IProgress<NetworkCheck> progress){
   if(!ValidHost(host))throw new ArgumentException("请输入域名，例如 www.microsoft.com，不包含网址路径、端口或账户。");
   var r=new NetworkDiagnosis{Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Host=host};
   Action<NetworkCheck> add=x=>{r.Checks.Add(x);if(progress!=null)progress.Report(x);};
   try{
    cancel.ThrowIfCancellationRequested();
    var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&n.OperationalStatus==OperationalStatus.Up).ToArray();
    add(new NetworkCheck{Step="网卡链路",Status=adapters.Length>0?"存在已连接接口":"未发现已连接接口",Detail=String.Join("；",adapters.Select(n=>n.Name))+"。包含 VPN/虚拟接口；本诊断使用系统路由，不限定为重启页面选中的网卡。"});
    var gateways=new List<IPAddress>();var addresses=new List<IPAddress>();
    foreach(var adapter in adapters)try{var p=adapter.GetIPProperties();addresses.AddRange(p.UnicastAddresses.Select(x=>x.Address));gateways.AddRange(p.GatewayAddresses.Select(x=>x.Address).Where(x=>!x.Equals(IPAddress.Any)&&!x.Equals(IPAddress.IPv6Any)));}catch(Exception e){add(new NetworkCheck{Step="接口配置 · "+adapter.Name,Status="未能读取",Detail=e.Message});}
    add(new NetworkCheck{Step="IP 地址",Status=addresses.Any(UsableAddress)?"存在可用类型的地址":"需要检查地址配置",Detail=String.Join(" / ",addresses)+"。仅链路本地地址不证明能访问互联网；存在地址也不保证路由可用。"});
    if(gateways.Count==0)add(new NetworkCheck{Step="网关",Status="未提供默认网关",Detail="VPN、点对点或专用路由可能不显示网关，不单独判定故障。"});
    foreach(var gateway in gateways.Distinct().Take(3)){
     cancel.ThrowIfCancellationRequested();try{using(var ping=new Ping()){var reply=await ping.SendPingAsync(gateway,1500);add(PingResult(reply.Status,gateway.ToString()));}}catch(Exception e){add(new NetworkCheck{Step="网关",Status="未确认",Detail=e.Message+"；防火墙或 ICMP 限制可能影响结果。"});}
    }
    cancel.ThrowIfCancellationRequested();
    try{
     var dns=Dns.GetHostAddressesAsync(host);
     if(await Task.WhenAny(dns,Task.Delay(4000,cancel))!=dns){Observe(dns);cancel.ThrowIfCancellationRequested();add(new NetworkCheck{Step="DNS",Status="超时未确认",Detail="系统解析器 4 秒内未返回。请检查 DNS、VPN 或网络策略。"});}
     else{var ips=await dns;add(new NetworkCheck{Step="DNS",Status=ips.Length>0?"解析成功":"无地址",Detail=host+" → "+String.Join(" / ",ips.Select(x=>x.ToString()))+"；可能命中缓存。"});}
    }catch(OperationCanceledException){throw;}catch(Exception e){add(new NetworkCheck{Step="DNS",Status="解析未成功",Detail=e.Message});}
    cancel.ThrowIfCancellationRequested();
    var request=(HttpWebRequest)WebRequest.Create("https://"+host+"/");request.Method="HEAD";request.AllowAutoRedirect=false;request.Proxy=null;request.UseDefaultCredentials=false;request.CookieContainer=null;request.KeepAlive=false;
    using(cancel.Register(request.Abort))try{
     var fetch=request.GetResponseAsync();
     if(await Task.WhenAny(fetch,Task.Delay(6000,cancel))!=fetch){request.Abort();ObserveResponse(fetch);cancel.ThrowIfCancellationRequested();add(new NetworkCheck{Step="指定网站 HTTPS",Status="超时未确认",Detail="直连目标 6 秒内未返回。未使用系统 HTTP 代理；需要代理的网络可能出现此结果。"});}
     else using(var response=(HttpWebResponse)await fetch)add(HttpResult((int)response.StatusCode));
    }catch(WebException e){cancel.ThrowIfCancellationRequested();var response=e.Response as HttpWebResponse;if(response!=null)using(response)add(HttpResult((int)response.StatusCode));else add(new NetworkCheck{Step="指定网站 HTTPS",Status="未确认可访问",Detail=e.Status+"："+e.Message+"。可能涉及代理、证书、站点或防火墙，不能直接判定网卡损坏。"});}
    r.Summary="检查完成。请按各层结果定位；未更改网络设置，也未自动重启网卡。";
   }catch(OperationCanceledException){r.Summary="用户已停止；未完成的层级不能判定为正常。";}catch(Exception e){r.Summary="检查未完整完成："+e.Message;}
   return r;
  }
 }
}
