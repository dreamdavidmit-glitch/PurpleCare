using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PurpleCare {
 public static class ThirdBatchTests {
  public static void Run(string output,List<string> checks){
   Action<bool,string> test=(ok,name)=>{if(!ok)throw new Exception(name);checks.Add("PASS: "+name);};
   var guide=RepairGuide.Create(0);test(guide.Steps.Count==4&&guide.Steps.All(x=>!RepairGuide.Recorded(x)),"guide begins with unverified observations");
   guide.Steps[0].Observation=RepairGuide.Outcomes[1];guide.Conclusion="用户确认：问题已恢复";test(RepairGuide.Summary(guide).Contains("1 / 4")&&RepairGuide.Summary(guide).Contains("用户反馈"),"early guide resolution stays explicitly user-reported");
   test(Enumerable.Range(0,3).SelectMany(i=>RepairGuide.Create(i).Steps).All(x=>new[]{"Audio","Network","Home","Stress","History"}.Contains(x.Page)),"guide targets existing pages without executing operations");
   string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"history-fixtures-"+Guid.NewGuid().ToString("N"));
   var bundle=new ReportBundle{Scan=new ScanReport{Time="2001-01-01 01:02:03",Model="Synthetic model",Computer="fixture-PC",UserName="fixture-user",AccountName=@"fixture-PC\fixture-user"},Guide=guide};
   guide.Steps[0].Note="<script>test</script> fixture-user";string id=ReportHistory.Save(root,bundle,true,"synthetic test"),second=ReportHistory.Save(root,bundle,false,"synthetic repeat");
   test(id!=second&&ReportHistory.List(root).Entries.Count==2,"history preserves multiple immutable snapshots");
   var saved=ReportHistory.Load(root,id);test(saved.Bundle.Scan.UserName=="[已隐藏]"&&!saved.Bundle.Guide.Steps[0].Note.Contains("fixture-user")&&bundle.Scan.UserName=="fixture-user","history save redacts guide notes without changing current report");
   guide.Steps[0].Note="changed later";test(saved.Bundle.Guide.Steps[0].Note.Contains("script")&&ReportHistory.Load(root,id).Bundle.Guide.Steps[0].Note.Contains("script"),"later guide edits cannot alter saved history");
   test(ReportHistory.Preview(saved).Contains("不是当前扫描状态")&&ReportHistory.Preview(saved).Contains("2001-01-01"),"history preview retains original time and historical label");
   var imported=ReportHistory.Import(ReportHistory.PathFor(root,id));test(imported.Scan.Time==bundle.Scan.Time,"history envelope import preserves original scan time");
   string legacy=Path.Combine(root,"legacy-input.txt");File.WriteAllText(legacy,Operations.Json().Serialize(new{Report=bundle.Scan,Operations=new[]{"old record"}}));test(ReportHistory.Import(legacy).Scan.Model=="Synthetic model","legacy Report JSON can be explicitly imported");
   string bad=Path.Combine(root,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(bad,"{broken");var list=ReportHistory.List(root);test(list.Unreadable==1&&list.Entries.Count==2,"one corrupt history file does not hide valid records");
   ReportHistory.Save(Path.Combine(root,"other-scope"),bundle,false,"other scope");test(ReportHistory.List(root).Entries.Count==2,"history listing does not recurse into other report scopes");
   var damaged=new ReportBundle{Scan=new ScanReport{Time="test",Model="test"},Guide=new GuideSnapshot{Steps=null}};bool invalid=false;try{ReportHistory.Validate(damaged);}catch(InvalidDataException){invalid=true;}test(invalid,"malformed nested history data is rejected before display");
   bool rejected=false;try{ReportHistory.PathFor(root,"..\\escape");}catch(ArgumentException){rejected=true;}test(rejected,"history IDs reject directory traversal");
   string unknown=Path.Combine(root,"unknown.txt");File.WriteAllText(unknown,"{\"unrelated\":true}");rejected=false;try{ReportHistory.Import(unknown);}catch(InvalidDataException){rejected=true;}test(rejected,"unrelated JSON is not accepted as a diagnostic report");
   saved.Bundle.HistorySavedAt=saved.SavedAt;string html=FullReport.Html(saved.Bundle,true);test(html.Contains("历史报告，不代表当前电脑状态")&&html.Contains("维修引导")&&!html.Contains("<script>"),"history HTML labels old results and escapes guide notes");
  }
 }
}
