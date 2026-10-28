using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace PurpleCare {
 public partial class MainUi {
  GuideSnapshot guide;int guideIndex;bool bindingGuide;HistoryEnvelope selectedHistory;int historySelectionVersion;
  void HistoryText(string value){C<TextBox>("HistoryPreview").Text=value;}
  ReportBundle CurrentBundle(){CaptureGuide();return new ReportBundle{Scan=report,Stress=lastStress,Network=networkDiagnosis,Repairs=repairs,Operations=history,Guide=guide};}
  void InitializeGuideHistory(){
   C<ComboBox>("GuideKind").ItemsSource=new[]{"请选择需要排查的问题","声音无声 / 拔耳机后无声","网络不能上网","运行卡顿 / 稳定性排查"};
   C<ComboBox>("GuideObservation").ItemsSource=RepairGuide.Outcomes;
   C<ComboBox>("GuideConclusion").ItemsSource=new[]{"尚未确认最终结果","用户确认：问题已恢复","用户反馈：仍未解决","用户反馈：需要进一步检修"};
   C<ComboBox>("GuideKind").SelectedIndex=0;C<ComboBox>("GuideConclusion").SelectedIndex=0;
   int previousKind=0;C<ComboBox>("GuideKind").SelectionChanged+=(s,e)=>{if(bindingGuide)return;CaptureGuide();int kind=C<ComboBox>("GuideKind").SelectedIndex;if(guide!=null&&(guide.Steps.Any(x=>RepairGuide.Recorded(x)||!String.IsNullOrWhiteSpace(x.Note)))&&!Confirm("重新开始引导？","切换问题类型会清空当前未保存的引导记录。要保留它，请取消并先保存到历史中心。","切换并重新开始")){bindingGuide=true;C<ComboBox>("GuideKind").SelectedIndex=previousKind;bindingGuide=false;return;}previousKind=kind;if(kind<=0){guide=null;guideIndex=0;}else{guide=RepairGuide.Create(kind-1);guideIndex=0;}BindGuide();};
   C<ComboBox>("GuideObservation").SelectionChanged+=(s,e)=>{if(!bindingGuide){CaptureGuide();UpdateGuideHistoryButtons();}};
   C<ComboBox>("GuideConclusion").SelectionChanged+=(s,e)=>{if(!bindingGuide)CaptureGuide();};
   On("GuidePrevBtn",()=>{CaptureGuide();if(guideIndex>0)guideIndex--;BindGuide();});
   On("GuideNextBtn",()=>{CaptureGuide();if(guide!=null&&RepairGuide.Recorded(guide.Steps[guideIndex])&&guideIndex<guide.Steps.Count-1)guideIndex++;BindGuide();});
   On("GuideGoBtn",()=>{CaptureGuide();if(guide!=null)C<RadioButton>("Nav"+guide.Steps[guideIndex].Page).IsChecked=true;});
   On("GuideSaveBtn",async()=>await SaveCurrentHistory("维修引导记录"));
   On("HistorySaveBtn",async()=>await SaveCurrentHistory("手动保存当前报告"));On("HistoryRefreshBtn",async()=>await RefreshReportHistory());On("HistoryImportBtn",async()=>await ImportReportHistory());On("HistoryExportBtn",ExportHistory);
   C<DataGrid>("HistoryGrid").SelectionChanged+=async(s,e)=>await SelectReportHistory();Window.Closed+=(s,e)=>historySelectionVersion++;BindGuide();
  }
  void CaptureGuide(){if(bindingGuide||guide==null)return;guide.Steps[guideIndex].Observation=C<ComboBox>("GuideObservation").SelectedItem as string??RepairGuide.Outcomes[0];guide.Steps[guideIndex].Note=C<TextBox>("GuideNote").Text;guide.Conclusion=C<ComboBox>("GuideConclusion").SelectedItem as string??"尚未确认最终结果";guide.Updated=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");Text("GuideSummary",RepairGuide.Summary(guide));}
  void BindGuide(){
   bindingGuide=true;try{var step=guide==null?null:guide.Steps[guideIndex];Text("GuideStepTitle",step==null?"选择问题后开始":"步骤 "+(guideIndex+1)+" / "+guide.Steps.Count+" · "+step.Title);Text("GuideInstruction",step==null?"引导会提供检查顺序，不会自动执行修复。":step.Instruction);C<ComboBox>("GuideObservation").SelectedItem=step==null?RepairGuide.Outcomes[0]:step.Observation;C<TextBox>("GuideNote").Text=step==null?"":step.Note;C<ComboBox>("GuideConclusion").SelectedItem=guide==null?"尚未确认最终结果":guide.Conclusion;Text("GuideSummary",RepairGuide.Summary(guide));}finally{bindingGuide=false;}UpdateGuideHistoryButtons();
  }
  void UpdateGuideHistoryButtons(){
   bool ready=guide!=null&&!busy;C<Button>("GuideGoBtn").IsEnabled=ready;C<Button>("GuidePrevBtn").IsEnabled=ready&&guideIndex>0;C<Button>("GuideNextBtn").IsEnabled=ready&&guideIndex<guide.Steps.Count-1&&RepairGuide.Recorded(guide.Steps[guideIndex]);C<Button>("GuideSaveBtn").IsEnabled=ready&&report!=null;
   C<ComboBox>("GuideKind").IsEnabled=!busy;C<ComboBox>("GuideObservation").IsEnabled=ready;C<ComboBox>("GuideConclusion").IsEnabled=ready;C<TextBox>("GuideNote").IsEnabled=ready;
   C<Button>("HistorySaveBtn").IsEnabled=!busy&&report!=null;C<Button>("HistoryRefreshBtn").IsEnabled=!busy;C<Button>("HistoryImportBtn").IsEnabled=!busy;C<Button>("HistoryExportBtn").IsEnabled=!busy&&selectedHistory!=null;
  }
  async Task SaveCurrentHistory(string origin){
   if(busy||report==null)return;var bundle=CurrentBundle();bool hide=C<CheckBox>("HideReportIdentity").IsChecked==true;SetBusy(true,"正在保存当前报告快照…");
   try{await Task.Run(()=>ReportHistory.Save(ReportHistory.Root,bundle,hide,origin));Log("已保存历史报告："+origin+"。历史记录不会覆盖当前扫描状态。");Text("GuideSummary",RepairGuide.Summary(guide)+"\n已保存到本机历史报告中心。");}
   catch(Exception e){MessageBox.Show(Window,e.Message,"保存报告失败");}finally{SetBusy(false,"历史报告保存操作结束");}await RefreshReportHistory();
  }
  async Task RefreshReportHistory(){
   if(busy)return;SetBusy(true,"正在读取本机、本账户的历史报告…");
   try{var list=await Task.Run(()=>ReportHistory.List(ReportHistory.Root));selectedHistory=null;historySelectionVersion++;C<DataGrid>("HistoryGrid").ItemsSource=list.Entries;HistoryText("选择一条历史报告查看摘要。旧结果不会作为当前状态显示。");Text("HistorySummary","当前电脑与账户的历史目录："+ReportHistory.Root+"\n显示 "+list.Entries.Count+" 条"+(list.Unreadable>0?"；有 "+list.Unreadable+" 个文件无法读取":"")+(list.Limited?"；仅展示最近写入的 200 个文件":"")+"。旧版文件需通过导入入口选择。");}
   catch(Exception e){Text("HistorySummary","无法读取历史列表："+e.Message);}finally{SetBusy(false,"历史列表读取结束");}
  }
  async Task SelectReportHistory(){
   int version=++historySelectionVersion;selectedHistory=null;UpdateGuideHistoryButtons();var entry=C<DataGrid>("HistoryGrid").SelectedItem as HistoryEntry;if(entry==null)return;
   HistoryText("正在读取历史摘要…");try{var value=await Task.Run(()=>ReportHistory.Load(ReportHistory.Root,entry.Id));if(version!=historySelectionVersion)return;selectedHistory=value;HistoryText(ReportHistory.Preview(value));}catch(Exception e){if(version==historySelectionVersion)HistoryText("历史报告无法读取："+e.Message);}finally{if(version==historySelectionVersion)UpdateGuideHistoryButtons();}
  }
  async Task ImportReportHistory(){
   if(busy)return;var picker=new OpenFileDialog{Title="导入紫芯 JSON 报告",Filter="紫芯 JSON 报告|*.json",Multiselect=false};if(picker.ShowDialog(Window)!=true)return;bool hide=C<CheckBox>("HideReportIdentity").IsChecked==true;SetBusy(true,"正在导入所选报告，不替换当前扫描…");
   try{await Task.Run(()=>{var bundle=ReportHistory.Import(picker.FileName);ReportHistory.Save(ReportHistory.Root,bundle,hide,"用户导入（可能来自其他电脑）");});Log("已导入用户选择的历史报告；未覆盖当前设备状态。");}catch(Exception e){MessageBox.Show(Window,e.Message,"无法导入报告");}finally{SetBusy(false,"历史报告导入操作结束");}await RefreshReportHistory();
  }
  void ExportHistory(){
   if(busy||selectedHistory==null)return;var envelope=selectedHistory;bool hide=C<CheckBox>("HideReportIdentity").IsChecked==true;var picker=new SaveFileDialog{Title="导出选中的历史报告",Filter="HTML 历史报告|*.html|JSON 历史数据|*.json",FileName="紫芯历史报告-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".html"};if(picker.ShowDialog(Window)!=true)return;
   try{var bundle=Operations.Json().Deserialize<ReportBundle>(Operations.Json().Serialize(envelope.Bundle));bundle.HistorySavedAt=envelope.SavedAt;File.WriteAllText(picker.FileName,picker.FilterIndex==1?FullReport.Html(bundle,hide):FullReport.Json(bundle,hide),new System.Text.UTF8Encoding(false));Log("已导出选中的历史报告："+picker.FileName);}catch(Exception e){MessageBox.Show(Window,e.Message,"历史报告导出失败");}
  }
 }
}
