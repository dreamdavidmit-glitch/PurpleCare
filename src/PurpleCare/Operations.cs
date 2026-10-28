using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PurpleCare {
 public class OperationResult{public bool Success{get;set;} public bool RebootRequired{get;set;} public string Message{get;set;} public ScanReport Report{get;set;} public List<string> Steps=new List<string>();}
 public interface IAudioServices{string State(string name);IEnumerable<string> RunningDependents(string name);void Stop(string name);void Start(string name);}
 public class WindowsAudioServices:IAudioServices{
  public string State(string name){using(var s=new ServiceController(name)){s.Refresh();return s.Status.ToString();}}
  public IEnumerable<string> RunningDependents(string name){using(var s=new ServiceController(name)){var result=new List<string>();foreach(var d in s.DependentServices)using(d){if(d.Status!=ServiceControllerStatus.Stopped)result.Add(d.ServiceName);}return result;}}
  public void Stop(string name){using(var s=new ServiceController(name)){s.Refresh();if(s.Status==ServiceControllerStatus.Stopped)return;if(s.Status!=ServiceControllerStatus.StopPending)s.Stop();s.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));}}
  public void Start(string name){using(var s=new ServiceController(name)){s.Refresh();if(s.Status==ServiceControllerStatus.Running)return;if(s.Status==ServiceControllerStatus.StopPending){s.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));s.Refresh();}if(s.Status!=ServiceControllerStatus.StartPending)s.Start();s.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(20));}}
 }
 public static class Operations{
  public static bool IsAdmin(){return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}
  public static string Encode(string s){return Convert.ToBase64String(Encoding.UTF8.GetBytes(s??""));}
  public static string Decode(string s){return Encoding.UTF8.GetString(Convert.FromBase64String(s));}
  public static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=16000000,RecursionLimit=60};}
  public static OperationResult RepairAudio(IAudioServices services){
   var r=new OperationResult();bool touched=false;bool stopped=false;var errors=new List<string>();
   try{
    r.Steps.Add("修复前：Audiosrv="+services.State("Audiosrv")+"，AudioEndpointBuilder="+services.State("AudioEndpointBuilder"));
    var other=services.RunningDependents("AudioEndpointBuilder").Where(x=>!x.Equals("Audiosrv",StringComparison.OrdinalIgnoreCase)).Concat(services.RunningDependents("Audiosrv")).Distinct().ToList();
    if(other.Count>0){r.Message="存在其他运行中的依赖服务，未执行停止："+String.Join(", ",other)+"。请改用指定声卡设备重启。";return r;}
    touched=true;services.Stop("Audiosrv");r.Steps.Add("已停止 Windows Audio");services.Stop("AudioEndpointBuilder");r.Steps.Add("已停止 Audio Endpoint Builder");stopped=true;
   }catch(Exception e){errors.Add(e.Message);}
   finally{
    // Always attempt to restore both services, even when stopping one failed.
    if(touched){foreach(var name in new[]{"AudioEndpointBuilder","Audiosrv"})try{services.Start(name);r.Steps.Add("已启动 "+name);}catch(Exception e){errors.Add(name+" 启动失败："+e.Message);}}
   }
   r.Success=stopped&&errors.Count==0;r.Steps.AddRange(errors);
   r.Message=r.Success?"音频服务已重新启动。请播放测试音，确认实际声音是否恢复。":"音频服务修复未完全完成，请查看步骤记录；可能需要重启电脑。";
   return r;
  }
  public static string RestartArgs(string id){if(!DevicePolicy.ValidId(id))throw new ArgumentException("无效的设备实例 ID");return "/restart-device \""+id+"\"";}
  public static OperationResult RunCommand(string file,string args){
   var r=new OperationResult();var info=new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(var p=Process.Start(info)){var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
    if(!p.WaitForExit(90000)){r.Message="操作超时，结果尚不确定。请刷新设备状态，不要立即重复操作。";return r;}
    Task.WaitAll(stdout,stderr);r.Steps.Add(stdout.Result.Trim());if(!String.IsNullOrWhiteSpace(stderr.Result))r.Steps.Add(stderr.Result.Trim());
    r.RebootRequired=p.ExitCode==3010||p.ExitCode==1641;r.Success=p.ExitCode==0;r.Message=r.RebootRequired?"Windows 要求重启电脑才能完成操作；本程序未自动重启电脑。":r.Success?"Windows 已完成设备操作。请验证设备的实际功能。":"操作失败，返回代码 "+p.ExitCode+"。详情见操作记录。";
   }return r;
  }
  public static OperationResult Execute(string operation,string id){
   if(!IsAdmin())return new OperationResult{Message="此操作需要管理员权限。"};
   if(operation=="scan")return new OperationResult{Success=true,Message="管理员只读扫描完成。",Report=Scanner.Scan(s=>{})};
   if(operation=="audio-services")return RepairAudio(new WindowsAudioServices());
   if(operation=="rescan")return RunCommand(Path.Combine(Environment.SystemDirectory,"pnputil.exe"),"/scan-devices");
   if(operation=="restart-device"){
    if(!DevicePolicy.ValidId(id))return new OperationResult{Message="设备 ID 无效，已拒绝操作。"};
    var dev=Scanner.Devices(false).FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
    if(dev==null||!DevicePolicy.AllowedRestart(dev))return new OperationResult{Message="设备不允许单独重启："+DevicePolicy.Reason(dev)};
    var r=RunCommand(Path.Combine(Environment.SystemDirectory,"pnputil.exe"),RestartArgs(dev.Id));r.Steps.Insert(0,"目标："+dev.Name+"\n实例："+dev.Id);return r;
   }
   return new OperationResult{Message="不支持的操作，已拒绝执行。"};
  }
  public static async Task<OperationResult> Elevated(string op,string id){
   if(IsAdmin())return await Task.Run(()=>Execute(op,id));
   string pipe="PurpleCare-"+Guid.NewGuid().ToString("N");var security=new PipeSecurity();
   security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
   security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
   using(var server=new NamedPipeServerStream(pipe,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)){
    var connected=server.WaitForConnectionAsync();Process process;
    try{process=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--worker "+op+" "+Encode(String.IsNullOrEmpty(id)?"-":id)+" "+pipe){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});}
    catch(System.ComponentModel.Win32Exception e){return new OperationResult{Message=e.NativeErrorCode==1223?"已取消管理员授权，未执行修复。":e.Message};}
    using(process){if(await Task.WhenAny(connected,Task.Delay(45000))!=connected)return new OperationResult{Message="未能连接修复进程。请确认管理员授权窗口；尚未确认操作结果。"};await connected;
     using(var reader=new StreamReader(server,Encoding.UTF8)){var read=reader.ReadLineAsync();if(await Task.WhenAny(read,Task.Delay(150000))!=read)return new OperationResult{Message="等待修复结果超时，请刷新状态后查看，不要立即重复操作。"};string text=await read;if(String.IsNullOrEmpty(text))return new OperationResult{Message="修复进程未返回结果，请刷新状态。"};return Json().Deserialize<OperationResult>(text);}
    }
   }
  }
  public static int Worker(string[] args){
   if(args.Length!=4||!Regex.IsMatch(args[3],"^PurpleCare-[a-f0-9]{32}$"))return 2;
   using(var pipe=new NamedPipeClientStream(".",args[3],PipeDirection.Out)){
    try{pipe.Connect(20000);OperationResult r;try{r=Execute(args[1],Decode(args[2]));}catch(Exception e){r=new OperationResult{Message="操作失败："+e.Message};}
     using(var writer=new StreamWriter(pipe,new UTF8Encoding(false))){writer.WriteLine(Json().Serialize(r));writer.Flush();}return r.Success?0:1;
    }catch{return 3;}
   }
  }
 }
}
