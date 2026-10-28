using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace PurpleCare {
 public sealed class HistoryEnvelope {public int Schema{get;set;} public string SavedAt{get;set;} public string Origin{get;set;} public bool IdentityHidden{get;set;} public ReportBundle Bundle{get;set;}}
 public sealed class HistoryEntry {public string Id{get;set;} public string SavedAt{get;set;} public string ScanTime{get;set;} public string Model{get;set;} public string Origin{get;set;} public string Privacy{get;set;}}
 public sealed class HistoryListing {public List<HistoryEntry> Entries=new List<HistoryEntry>();public int Unreadable{get;set;}public bool Limited{get;set;}}
 public static class ReportHistory {
  public static string Root{get{return Path.Combine(PortablePaths.Root,"Reports");}}
  public static string PathFor(string root,string id){Guid value;if(!Guid.TryParseExact(id,"N",out value))throw new ArgumentException("无效报告编号");return Path.Combine(root,id+".json");}
  static string ReadText(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("不读取文件链接");if(new FileInfo(path).Length>16*1024*1024)throw new IOException("报告超过 16 MB，未加载");return File.ReadAllText(path);}
  public static void Validate(ReportBundle b){
   if(b==null||b.Scan==null||String.IsNullOrWhiteSpace(b.Scan.Time)||String.IsNullOrWhiteSpace(b.Scan.Model)||b.Scan.Facts==null||b.Scan.Devices==null||b.Scan.Networks==null||b.Scan.Endpoints==null||b.Scan.Findings==null||b.Scan.Limitations==null)throw new InvalidDataException("不是可识别的紫芯完整诊断报告");
   if(b.Scan.Facts.Any(x=>x==null)||b.Scan.Devices.Any(x=>x==null)||b.Scan.Networks.Any(x=>x==null)||b.Scan.Endpoints.Any(x=>x==null)||b.Scan.Findings.Any(x=>x==null))throw new InvalidDataException("报告含有损坏的数据项");
   if(b.Guide!=null&&(b.Guide.Steps==null||b.Guide.Steps.Any(x=>x==null)))throw new InvalidDataException("引导记录不完整");
   if(b.Repairs!=null&&b.Repairs.Any(x=>x==null||x.Rows==null||x.Rows.Any(y=>y==null)))throw new InvalidDataException("修复对照记录不完整");
   if(b.Network!=null&&(b.Network.Checks==null||b.Network.Checks.Any(x=>x==null)))throw new InvalidDataException("网络诊断记录不完整");
   if(b.Stress!=null&&(b.Stress.Samples==null||b.Stress.Samples.Any(x=>x==null)))throw new InvalidDataException("压力测试采样不完整");
   if(b.Scan.Temperatures!=null&&(b.Scan.Temperatures.Readings==null||b.Scan.Temperatures.Notes==null||b.Scan.Temperatures.Readings.Any(x=>x==null)))throw new InvalidDataException("温度记录不完整");
  }
  public static ReportBundle Import(string path){
   string text=ReadText(path);var json=Operations.Json();var map=json.DeserializeObject(text) as Dictionary<string,object>;
   if(map==null)throw new InvalidDataException("报告格式不正确");ReportBundle bundle;
   if(map.ContainsKey("Schema")){var envelope=json.Deserialize<HistoryEnvelope>(text);if(envelope.Schema!=1)throw new InvalidDataException("不支持的历史报告版本");bundle=envelope.Bundle;}
   else if(map.ContainsKey("Scan"))bundle=json.Deserialize<ReportBundle>(text);
   else if(map.ContainsKey("Report")){bundle=new ReportBundle{Scan=json.ConvertToType<ScanReport>(map["Report"])};if(map.ContainsKey("StressTest"))bundle.Stress=json.ConvertToType<StressReport>(map["StressTest"]);if(map.ContainsKey("Operations"))bundle.Operations=json.ConvertToType<List<string>>(map["Operations"]);}
   else throw new InvalidDataException("请选择紫芯导出的 JSON 报告，不支持 HTML 或其他资料文件");
   Validate(bundle);return bundle;
  }
  public static string Save(string root,ReportBundle bundle,bool hide,string origin){
   Validate(bundle);Directory.CreateDirectory(root);if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("历史目录不能是文件链接");
   var copy=Operations.Json().Deserialize<ReportBundle>(FullReport.Json(bundle,hide));var envelope=new HistoryEnvelope{Schema=1,SavedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Origin=origin,IdentityHidden=hide,Bundle=copy};
   string text=Operations.Json().Serialize(envelope);if(Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new IOException("报告超过 16 MB，未保存");
   string id=Guid.NewGuid().ToString("N"),path=PathFor(root,id),temp=path+".tmp";
   using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=new UTF8Encoding(false).GetBytes(text);file.Write(bytes,0,bytes.Length);file.Flush(true);}File.Move(temp,path);return id;
  }
  public static HistoryEnvelope Load(string root,string id){var envelope=Operations.Json().Deserialize<HistoryEnvelope>(ReadText(PathFor(root,id)));if(envelope==null||envelope.Schema!=1)throw new InvalidDataException("历史报告格式不支持");Validate(envelope.Bundle);return envelope;}
  public static HistoryListing List(string root){
   var list=new HistoryListing();if(!Directory.Exists(root))return list;if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("历史目录不能是文件链接");
   var paths=Directory.EnumerateFiles(root,"*.json",SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(201).ToArray();list.Limited=paths.Length>200;
   foreach(var path in paths.Take(200))try{string id=Path.GetFileNameWithoutExtension(path);var value=Load(root,id);list.Entries.Add(new HistoryEntry{Id=id,SavedAt=value.SavedAt,ScanTime=value.Bundle.Scan.Time,Model=value.Bundle.Scan.Model,Origin=value.Origin,Privacy=value.IdentityHidden?"已隐藏身份":"保留身份"});}catch{list.Unreadable++;}
   return list;
  }
  public static string Preview(HistoryEnvelope h){var b=h.Bundle;return "历史记录 · 不是当前扫描状态\n保存于："+h.SavedAt+"\n原扫描时间："+b.Scan.Time+"\n来源："+h.Origin+"\n电脑："+b.Scan.Computer+"\n型号："+b.Scan.Model+"\n设备："+b.Scan.Devices.Count+" 个\n异常线索："+b.Scan.Findings.Count+" 条\n修复记录："+(b.Repairs==null?0:b.Repairs.Count)+" 条\n压力测试："+(b.Stress==null?"未记录":b.Stress.Started+" / "+b.Stress.Status)+"\n网络诊断："+(b.Network==null?"未记录":b.Network.Time+" / "+b.Network.Summary)+"\n维修引导："+RepairGuide.Summary(b.Guide)+"\n\n"+String.Join("\n",b.Scan.Findings.Select(x=>x.Title+"："+x.Detail));}
 }
}
