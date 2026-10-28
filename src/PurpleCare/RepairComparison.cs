using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace PurpleCare {
 public class ComparisonRow {public string Item{get;set;} public string Before{get;set;} public string After{get;set;}}
 public class RepairRecord {
  public string Started{get;set;} public string Finished{get;set;} public string Label{get;set;} public string Domain{get;set;}
  public string CommandResult{get;set;} public string Verification{get;set;}
  public List<ComparisonRow> Rows=new List<ComparisonRow>();
 }
 public static class RepairComparison {
  static DeviceRow Device(ScanReport r,string id){return r==null?null:r.Devices.FirstOrDefault(x=>String.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase));}
  static string State(ScanReport r,string id){var d=Device(r,id);return r==null||!r.DevicesReadable?"未能读取":d==null?"本次未枚举到":d.State+" · 驱动 "+d.Driver;}
  public static RepairRecord Create(ScanReport before,ScanReport after,string op,string id,string label,OperationResult result){
   var dev=Device(before,id)??Device(after,id);string domain=op=="audio-services"||(dev!=null&&dev.Class.Equals("MEDIA",StringComparison.OrdinalIgnoreCase))?"audio":dev!=null&&dev.Class.Equals("Net",StringComparison.OrdinalIgnoreCase)?"network":"device";
   var r=new RepairRecord{Started=before==null?"未知":before.Time,Finished=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Label=label,Domain=domain,CommandResult=result==null?"操作结果未知":(result.Success?"系统命令执行成功；":"系统命令未完全成功；")+result.Message,Verification=domain=="audio"?"尚未验证实际声音，请播放测试音并按听到的情况确认。":domain=="network"?"尚未验证联网，请主动运行网络分层诊断。":"设备状态不等于实际功能已恢复。"};
   Action<string,string,string> add=(name,b,a)=>r.Rows.Add(new ComparisonRow{Item=name,Before=b??"未知",After=a??"未知"});
   if(!String.IsNullOrEmpty(id))add(dev==null?"目标设备":dev.Name,State(before,id),State(after,id));
   if(domain=="audio"){
    add("Windows Audio",before==null?null:before.AudioService,after==null?null:after.AudioService);
    add("Audio Endpoint Builder",before==null?null:before.EndpointService,after==null?null:after.EndpointService);
    Func<ScanReport,string> outputs=s=>s==null||!s.AudioReadable?"未能读取":String.Join("；",s.Endpoints.Select(x=>x.Name+"："+x.State+" / 静音 "+x.Muted+(x.Default?" / 默认输出":"")));
    add("声音输出",outputs(before),outputs(after));
   }
   if(domain=="network"){
    Func<ScanReport,string> networks=s=>s==null?"未能读取":String.Join("；",s.Networks.Select(x=>x.Name+"："+x.State+" / IP "+x.Addresses+" / 网关 "+x.Gateways));
    add("系统网络连接（含其他网卡）",networks(before),networks(after));
   }
   if(r.Rows.Count==0)add("设备枚举数量",before==null||!before.DevicesReadable?"未能读取":before.Devices.Count.ToString(),after==null||!after.DevicesReadable?"未能读取":after.Devices.Count.ToString());
   return r;
  }
  public static string Text(RepairRecord r){if(r==null)return "尚未执行修复。";var b=new StringBuilder(r.Label+"\n修复前："+r.Started+" · 修复后："+r.Finished+"\n"+r.CommandResult+"\n验证："+r.Verification);foreach(var row in r.Rows)b.Append("\n\n"+row.Item+"\n之前："+row.Before+"\n之后："+row.After);return b.ToString();}
 }
}
