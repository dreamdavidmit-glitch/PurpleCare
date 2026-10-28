using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
namespace PurpleCare {
 public class ReportBundle {public ScanReport Scan{get;set;} public StressReport Stress{get;set;} public NetworkDiagnosis Network{get;set;} public List<RepairRecord> Repairs{get;set;} public List<string> Operations{get;set;} public GuideSnapshot Guide{get;set;} public string HistorySavedAt{get;set;}}
 public static class FullReport {
  static Func<string,string> Redactor(ReportBundle r,bool hide){
   var tokens=new[]{r.Scan==null?null:r.Scan.AccountName,r.Scan==null?null:r.Scan.UserName,r.Scan==null?null:r.Scan.Computer,r.Stress==null?null:r.Stress.UserName,r.Stress==null?null:r.Stress.Computer}.Where(s=>!String.IsNullOrEmpty(s)).Distinct().OrderByDescending(s=>s.Length).ToArray();
   return text=>{string value=text??"";if(hide)foreach(var token in tokens)value=Regex.Replace(value,Regex.Escape(token),"[已隐藏]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);return value;};
  }
  static object Scrub(object value,Func<string,string> redact){
   var text=value as string;if(text!=null)return redact(text);
   var dict=value as IDictionary<string,object>;if(dict!=null)return dict.ToDictionary(x=>x.Key,x=>Scrub(x.Value,redact));
   var list=value as object[];if(list!=null)return list.Select(x=>Scrub(x,redact)).ToArray();return value;
  }
  public static string Json(ReportBundle r,bool hide){var json=Operations.Json();return json.Serialize(Scrub(json.DeserializeObject(json.Serialize(r)),Redactor(r,hide)));}
  public static string Html(ReportBundle r,bool hide){
   var redact=Redactor(r,hide);Func<string,string> e=value=>WebUtility.HtmlEncode(redact(value));var b=new StringBuilder("<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>紫芯电脑检测报告</title><style>body{font:16px/1.65 system-ui;background:#0b1426;color:#edf2fc;max-width:1100px;margin:32px auto;padding:0 20px}section{background:#142039;border-radius:12px;padding:22px;margin:18px 0}h1,h2{color:#ffd12e}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #344764;padding:10px;text-align:left;vertical-align:top;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}.muted{color:#b3c3dc}svg{width:100%;height:auto}@media print{body,section{background:white;color:black}h1,h2{color:#333}td,th{border-color:#ccc}}</style></head><body><h1>紫芯 · 电脑检测报告</h1>");
   Action<string> section=title=>b.Append("<section><h2>"+e(title)+"</h2>");Action end=()=>b.Append("</section>");
   Action<string,string> row=(name,value)=>b.Append("<tr><th>"+e(name)+"</th><td>"+e(value)+"</td></tr>");
   b.Append("<p>导出于 "+e(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))+"</p><p class='muted'>"+(hide?"已隐藏扫描与测试记录中的用户名、账户名和电脑名。其余设备标识、IP 和诊断内容仍可能包含个人信息。":"此报告包含电脑、账户、设备及本地网络信息。")+" 所有结果只适用于记录时刻，未报告异常不等于硬件完全健康。</p>");
   if(!String.IsNullOrEmpty(r.HistorySavedAt))b.Append("<section><strong>历史报告，不代表当前电脑状态。</strong><p>保存于 "+e(r.HistorySavedAt)+"；请按下方原始扫描及测试时间解读。</p></section>");
   if(r.Guide!=null){section("维修引导 · 用户观察记录");b.Append("<p>"+e(RepairGuide.Summary(r.Guide))+"</p><p>开始："+e(r.Guide.Started)+" · 更新："+e(r.Guide.Updated)+"</p><table>");foreach(var step in r.Guide.Steps)row(step.Title,step.Observation+" · "+step.Note);b.Append("</table>");end();}
   var scan=r.Scan;
   if(scan!=null){
    section("硬件与系统");b.Append("<table>");row("扫描时间 / 来源",scan.Time+" / Windows 系统接口");row("电脑",scan.Computer);row("当前账户",scan.AccountName);row("型号",scan.Manufacturer+" "+scan.Model);foreach(var f in scan.Facts)row(f.Label,f.Value);b.Append("</table>");end();
    section("温度快照");if(scan.Temperatures==null)b.Append("未读取");else{b.Append("<p>"+e(scan.Temperatures.Time)+"</p><table>");foreach(var t in scan.Temperatures.Readings)row(t.Name,t.Display+" · "+t.Source);b.Append("</table>");foreach(var note in scan.Temperatures.Notes)b.Append("<p>"+e(note)+"</p>");}end();
    section("异常线索与检测范围");if(scan.Findings.Count==0)b.Append("<p>可读取项目未报告异常，请结合检测范围判断。</p>");foreach(var f in scan.Findings)b.Append("<p><strong>"+e(f.Title)+"</strong><br>"+e(f.Detail)+"</p>");foreach(var note in scan.Limitations)b.Append("<p class='muted'>"+e(note)+"</p>");end();
    section("设备与驱动");b.Append("<table><tr><th>设备 / 标识</th><th>类别</th><th>Windows 状态</th><th>驱动</th></tr>");foreach(var d in scan.Devices)b.Append("<tr><td>"+e(d.Name)+"<br>"+e(d.Id)+"</td><td>"+e(d.Category)+"</td><td>"+e(d.State)+"</td><td>"+e(d.Driver)+"</td></tr>");b.Append("</table>");end();
    section("声音与网络状态");b.Append("<table>");row("音频服务",scan.AudioService+" / "+scan.EndpointService);foreach(var ep in scan.Endpoints)row(ep.Name,ep.State+" / 静音 "+ep.Muted+" / 音量 "+ep.Volume);foreach(var n in scan.Networks)row(n.Name,n.State+" / IP "+n.Addresses+" / 网关 "+n.Gateways+" / DNS "+n.Dns);b.Append("</table>");end();
   }
   section("修复前后对照");if(r.Repairs==null||r.Repairs.Count==0)b.Append("<p>本次会话尚未执行修复。</p>");else foreach(var repair in r.Repairs){b.Append("<h3>"+e(repair.Label)+"</h3><p>"+e(repair.Started)+" → "+e(repair.Finished)+"</p><p>"+e(repair.CommandResult)+"</p><p>"+e(repair.Verification)+"</p><table><tr><th>项目</th><th>修复前</th><th>修复后</th></tr>");foreach(var c in repair.Rows)b.Append("<tr><td>"+e(c.Item)+"</td><td>"+e(c.Before)+"</td><td>"+e(c.After)+"</td></tr>");b.Append("</table>");}end();
   section("网络分层诊断");if(r.Network==null)b.Append("未执行主动网络诊断。");else{b.Append("<p>"+e(r.Network.Time)+" · "+e(r.Network.Host)+"</p><table>");foreach(var c in r.Network.Checks)row(c.Step,c.Status+"："+c.Detail);b.Append("</table><p>"+e(r.Network.Summary)+"</p>");}end();
   section("压力测试与曲线");var s=r.Stress;if(s==null)b.Append("未执行压力测试。");else{
    b.Append("<p>"+e(s.Started)+" · "+e(s.Status)+" · "+e(s.Message)+"</p><p>CPU 校验 "+s.CpuChecks+" 次 / 内存 "+s.MemoryPasses+" 轮 / 错误 "+s.Errors+"</p>");
    b.Append("<svg viewBox='0 0 900 260' role='img' aria-label='CPU 和内存使用率曲线'>");
    Func<double,string> number=x=>x.ToString("0.##",CultureInfo.InvariantCulture);double duration=Math.Max(1,Math.Max(s.ElapsedSeconds,s.Options==null?0:s.Options.Seconds));
    for(int i=0;i<=100;i+=25)b.Append("<path d='M40 "+(220-i*2)+" H880' stroke='#344764'/><text x='0' y='"+(224-i*2)+"' fill='#b3c3dc'>"+i+"%</text>");
    foreach(bool cpu in new[]{true,false}){var points=new List<string>();foreach(var sample in s.Samples){double? value=cpu?sample.CpuPercent:sample.MemoryPercent;if(value.HasValue&&!Double.IsNaN(value.Value)&&!Double.IsInfinity(value.Value)&&!Double.IsNaN(sample.Seconds)&&!Double.IsInfinity(sample.Seconds))points.Add(number(40+840*Math.Max(0,Math.Min(1,sample.Seconds/duration)))+","+number(220-2*Math.Max(0,Math.Min(100,value.Value))));}b.Append("<polyline fill='none' stroke='"+(cpu?"#ffd12e":"#6bbbff")+"' stroke-width='2' points='"+String.Join(" ",points)+"'/>");}
    b.Append("</svg><p>黄色：CPU；蓝色：内存。系统占用包含其他程序负载。</p><p>本次测试时长 "+number(s.ElapsedSeconds)+" 秒；仅校验选定负载和内存区域，不是整机健康证明，未连续监测温度。</p>");
   }end();
   section("本地操作记录");b.Append("<pre>"+e(String.Join("\n",r.Operations??new List<string>()))+"</pre>");end();return b.Append("</body></html>").ToString();
  }
 }
}
