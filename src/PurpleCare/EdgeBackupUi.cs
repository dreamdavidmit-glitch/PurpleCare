using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PurpleCare {
 public partial class MainUi {
  CancellationTokenSource backupCancel;bool backupRunning;
  void InitializeBackup(){
   Text("BackupUser",System.Security.Principal.WindowsIdentity.GetCurrent().Name);
   Text("BackupSource",EdgeBackup.SourcePath);Text("BackupDestination",EdgeBackup.DestinationRoot);
   On("BackupStartBtn",async()=>await StartEdgeBackup());
   On("BackupCancelBtn",()=>{if(backupCancel!=null){backupCancel.Cancel();Text("BackupStatus","正在停止… 已复制的部分文件会保留，并标记为未完成。");}});
   C<CheckBox>("BackupConsent").Checked+=(s,e)=>UpdateBackupButtons();C<CheckBox>("BackupConsent").Unchecked+=(s,e)=>UpdateBackupButtons();
   UpdateBackupButtons();
  }
  void UpdateBackupButtons(){C<Button>("BackupStartBtn").IsEnabled=!busy&&C<CheckBox>("BackupConsent").IsChecked==true;C<Button>("BackupCancelBtn").IsEnabled=backupRunning;C<CheckBox>("BackupVerify").IsEnabled=!busy;}
  async Task StartEdgeBackup(){
   if(busy||C<CheckBox>("BackupConsent").IsChecked!=true)return;
   string source=EdgeBackup.SourcePath,destination=EdgeBackup.DestinationRoot;bool verify=C<CheckBox>("BackupVerify").IsChecked==true;
   if(!Confirm("确认备份 Edge Default", "当前账户："+System.Security.Principal.WindowsIdentity.GetCurrent().Name+"\n\n来源："+source+"\n保存到："+destination+"\n\n仅复制 Default 目录，包含该配置文件内的书签、浏览记录、扩展、缓存、密码数据库、Cookie 和登录会话等敏感资料。不包含其他配置文件或上级目录文件。\n\n会先关闭当前账户、当前会话的 Edge；2 秒后强制结束仍在运行的 Edge。未保存内容可能丢失，请先保存工作。\n\n文件仅复制到本地目标，不解密、不上传。Default 目录不是完整的 Edge 迁移备份。\n内容校验："+(verify?"启用 SHA-256（增加读取耗时）":"未启用"),"关闭 Edge 并备份"))return;
   backupCancel=new CancellationTokenSource();backupRunning=true;SetBusy(true,"正在执行用户确认的 Edge 备份…");
   Text("BackupStatus","正在预检查文件大小、目标盘空间和写入权限；此时尚未关闭 Edge…");
   C<ProgressBar>("BackupProgressBar").Value=0;C<ProgressBar>("BackupProgressBar").IsIndeterminate=true;Text("BackupPercent","准备中…");
   var progress=new Progress<BackupProgress>(p=>{
    var bar=C<ProgressBar>("BackupProgressBar");bar.IsIndeterminate=p.Stage=="counting";bar.Value=p.Percent;
    Text("BackupPercent",p.Stage=="counting"?"正在统计 Default 大小…":p.Percent.ToString("0.0")+"% · "+(p.Bytes/1048576.0).ToString("0.0")+" / "+(p.TotalBytes/1048576.0).ToString("0.0")+" MB");
    if(p.Stage=="copying"||p.Stage=="verifying")Text("BackupStatus",(p.Stage=="verifying"?"正在回读校验":"正在复制 Default")+" · 已复制 "+p.Files+" 个文件 · 平均处理 "+(p.BytesPerSecond/1048576.0).ToString("0.0")+" MB/s · 预计剩余 "+(p.RemainingSeconds.HasValue?(p.RemainingSeconds.Value<60?Math.Ceiling(p.RemainingSeconds.Value).ToString("0")+" 秒":Math.Ceiling(p.RemainingSeconds.Value/60).ToString("0")+" 分钟"):"计算中")+(p.VerifyRequested?"\n已校验回读 "+(p.VerifiedBytes/1048576.0).ToString("0.0")+" MB；进度包含复制和校验":""));
   });
   try{
    var token=backupCancel.Token;
    var plan=await Task.Run(()=>BackupIntegrity.Preflight(source,destination,token));Text("BackupStatus","预检查通过："+plan.Files+" 个文件 / "+(plan.Bytes/1048576.0).ToString("0.0")+" MB，目标可用 "+(plan.Available/1048576.0).ToString("0.0")+" MB。正在关闭当前账户的 Edge…");
    var result=await Task.Run(()=>{EdgeBackup.Validate(source,destination);EdgeBackup.CloseCurrentUsersEdge(token);return EdgeBackup.Copy(source,destination,Environment.MachineName,Environment.UserName,token,p=>((IProgress<BackupProgress>)progress).Report(p),verify);});
    string state=result.Status=="completed"?"备份完成":result.Status=="cancelled"?"备份已取消":"备份未完整完成";
    string summary=state+" · "+result.Files+" 个文件 · "+(result.Bytes/1048576.0).ToString("0.0")+" MB\n保存位置："+result.Destination;
    if(result.Failed>0)summary+="\n有 "+result.Failed+" 项未完成；详见目录内 backup-result.json。请关闭 Edge、检查 U 盘空间后重试。";
    summary+="\n"+result.Verification;if(!Directory.Exists(result.Destination))summary+="\n该计划目录未创建，没有生成备份。";Text("BackupStatus",summary);Log(summary);
   }catch(OperationCanceledException){Text("BackupStatus","已取消备份。浏览器如果已经关闭，需要自行重新打开。");Log("用户取消了 Edge 备份。");}
   catch(Exception e){Text("BackupStatus","备份未完成："+e.Message);Log("Edge 备份未完成："+e.Message);}
   finally{C<ProgressBar>("BackupProgressBar").IsIndeterminate=false;backupRunning=false;backupCancel.Dispose();backupCancel=null;C<CheckBox>("BackupConsent").IsChecked=false;SetBusy(false,"Edge 备份操作结束，请查看备份页面结果");}
  }
 }
}
