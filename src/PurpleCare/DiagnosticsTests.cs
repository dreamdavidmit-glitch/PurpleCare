using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
namespace PurpleCare {
 public static class DiagnosticsTests {
  public static void Run(string output,List<string> checks){
   Action<bool,string> test=(ok,name)=>{if(!ok)throw new Exception(name);checks.Add("PASS: "+name);};
   var before=new ScanReport{Time="before",Computer="PRIVATE-PC",UserName="private-user",AccountName=@"PRIVATE-PC\private-user",DevicesReadable=true,AudioReadable=true,AudioService="Stopped",EndpointService="Running"};
   before.Devices.Add(new DeviceRow{Id="device-1",Name="Test sound device",Class="MEDIA",Present=true,Code=10,Driver="1"});
   var after=new ScanReport{Time="after",DevicesReadable=true,AudioReadable=true,AudioService="Running",EndpointService="Running"};after.Devices.Add(new DeviceRow{Id="device-1",Name="Test sound device",Class="MEDIA",Present=true,Code=0,Driver="1"});
   var repair=RepairComparison.Create(before,after,"restart-device","device-1","Test repair",new OperationResult{Success=true,Message="ok"});
   test(repair.Rows.Any(x=>x.Item=="Windows Audio"&&x.Before=="Stopped"&&x.After=="Running"),"repair comparison shows actual before and after services");
   test(repair.Verification.Contains("尚未验证")&&repair.CommandResult.Contains("命令执行成功"),"command success never becomes verified audio recovery");
   var missing=RepairComparison.Create(before,null,"restart-device","device-1","Test",null);test(missing.Rows[0].After=="未能读取"&&missing.CommandResult.Contains("未知"),"missing post-scan and command results remain unknown");
   test(NetworkDiagnostics.ValidHost("www.microsoft.com")&&!NetworkDiagnostics.ValidHost("https://example.com/a")&&!NetworkDiagnostics.ValidHost("user@example.com")&&!NetworkDiagnostics.ValidHost("example.com:443"),"network diagnostic target accepts domains only");
   test(!NetworkDiagnostics.UsableAddress(IPAddress.Parse("169.254.1.2"))&&!NetworkDiagnostics.UsableAddress(IPAddress.IPv6Loopback)&&NetworkDiagnostics.UsableAddress(IPAddress.Parse("2001:db8::1")),"IP classification handles APIPA and IPv6 without requiring IPv4");
   test(NetworkDiagnostics.PingResult(IPStatus.TimedOut,"192.0.2.1").Status=="未确认","ICMP timeout does not assert network failure");
   test(NetworkDiagnostics.HttpResult(403).Status=="收到限制或错误响应"&&NetworkDiagnostics.HttpResult(302).Status=="收到重定向","HTTP restrictions and redirects remain distinct from success");
   var cancel=new CancellationTokenSource();cancel.Cancel();var stopped=NetworkDiagnostics.Run("example.com",cancel.Token,null).GetAwaiter().GetResult();test(stopped.Checks.Count==0&&stopped.Summary.Contains("停止"),"cancelled network diagnostic sends no requests");cancel.Dispose();
   before.Facts.Add(new Fact("untrusted","<script>alert(1)</script>"));before.Findings.Add(new Finding("info","Test finding","private-user"));
   var bundle=new ReportBundle{Scan=before,Repairs=new List<RepairRecord>{repair},Operations=new List<string>{@"C:\Users\private-user\report.txt"},Stress=new StressReport{Started="test",Computer="PRIVATE-PC",UserName="private-user",Options=new StressOptions{Mode="cpu",Seconds=30},Status="completed",Message="Synthetic sample",ElapsedSeconds=30,Samples=new List<StressSample>{new StressSample{Seconds=0,CpuPercent=10,MemoryPercent=20},new StressSample{Seconds=30,CpuPercent=40,MemoryPercent=30}}}};
   var html=FullReport.Html(bundle,true);var json=FullReport.Json(bundle,true);
   test(!html.Contains("<script>")&&html.Contains("&lt;script&gt;"),"HTML report escapes device and finding text");
   test(!html.Contains("PRIVATE-PC")&&!html.Contains("private-user")&&!json.Contains("private-user")&&!json.Contains("PRIVATE-PC"),"identity redaction covers HTML JSON history and stress fields");
   test(before.UserName=="private-user"&&FullReport.Html(bundle,false).Contains("private-user"),"redacted export never mutates original records");
   test(html.Contains("<svg")&&html.Contains("修复前后对照")&&html.Contains("设备与驱动"),"offline HTML includes charts hardware and repair comparison");
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"synthetic-full-report.html"),html);
  }
 }
}
