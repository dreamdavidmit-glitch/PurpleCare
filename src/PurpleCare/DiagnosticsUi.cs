using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
namespace PurpleCare {
 public partial class MainUi {
  readonly List<RepairRecord> repairs=new List<RepairRecord>();NetworkDiagnosis networkDiagnosis;CancellationTokenSource networkCancel;
  void InitializeDiagnostics(){On("NetworkDiagnoseBtn",async()=>await DiagnoseNetwork());On("NetworkStopBtn",()=>{if(networkCancel!=null)networkCancel.Cancel();});}
  void ShowComparison(){
   Text("AudioComparison",RepairComparison.Text(repairs.LastOrDefault(r=>r.Domain=="audio")));
   Text("NetworkComparison",RepairComparison.Text(repairs.LastOrDefault(r=>r.Domain=="network")));
   Text("LatestComparison",RepairComparison.Text(repairs.LastOrDefault()));
  }
  void SaveComparisons(){ShowComparison();try{Directory.CreateDirectory(dataDir);File.WriteAllText(Path.Combine(dataDir,"repair-session-"+repairSession+".json"),Operations.Json().Serialize(repairs));}catch(Exception e){Log("无法保存修复对照："+e.Message);}}
  readonly string repairSession=Guid.NewGuid().ToString("N");
  async Task DiagnoseNetwork(){
   if(busy)return;string host=C<TextBox>("DiagnosticHost").Text.Trim();if(!NetworkDiagnostics.ValidHost(host)){Text("NetworkDiagnosticResult","请输入有效域名，例如 www.microsoft.com。");return;}
   networkCancel=new CancellationTokenSource();SetBusy(true,"网络分层诊断进行中…");C<Button>("NetworkStopBtn").IsEnabled=true;Text("NetworkDiagnosticResult","正在读取网卡、地址和路由…");
   var lines=new List<string>();var progress=new Progress<NetworkCheck>(c=>{lines.Add(c.Step+" · "+c.Status+"\n"+c.Detail);Text("NetworkDiagnosticResult",String.Join("\n\n",lines));});
   try{
    networkDiagnosis=await Task.Run(()=>NetworkDiagnostics.Run(host,networkCancel.Token,progress));
    Text("NetworkDiagnosticResult",networkDiagnosis.Time+"\n"+String.Join("\n\n",networkDiagnosis.Checks.Select(c=>c.Step+" · "+c.Status+"\n"+c.Detail))+"\n\n"+networkDiagnosis.Summary);
    Log("网络诊断："+networkDiagnosis.Host+" · "+networkDiagnosis.Summary);
    var last=repairs.LastOrDefault();if(last!=null&&last.Domain=="network"){last.Verification="已于 "+networkDiagnosis.Time+"追加系统路由诊断（目标 "+host+"）；它不限定为刚才重启的网卡，详见分层结果。";SaveComparisons();}
   }catch(Exception e){Text("NetworkDiagnosticResult","诊断未完成："+e.Message);Log("网络诊断未完成："+e.Message);}
   finally{networkCancel.Dispose();networkCancel=null;C<Button>("NetworkStopBtn").IsEnabled=false;SetBusy(false,"网络诊断结束，请按各层结果判断");}
  }
 }
}
