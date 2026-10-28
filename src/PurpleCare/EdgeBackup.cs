using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PurpleCare {
 public sealed class BackupResult {
  public string Status{get;set;} public string Destination{get;set;} public string Source{get;set;}
  public string Started{get;set;} public string Finished{get;set;}
  public long Files{get;set;} public long Bytes{get;set;} public int Failed{get;set;}
  public long TotalBytes{get;set;}
  public bool VerifyRequested{get;set;} public long VerifiedBytes{get;set;} public long VerifiedFiles{get;set;} public string Verification{get;set;}
  public List<string> Errors=new List<string>();
 }
 public sealed class BackupProgress {
  public string Stage{get;set;} public long Bytes{get;set;} public long TotalBytes{get;set;} public long Files{get;set;}
  public bool VerifyRequested{get;set;} public long VerifiedBytes{get;set;} public double BytesPerSecond{get;set;} public double? RemainingSeconds{get;set;}
  public double Percent{get{return Stage=="completed"?100:TotalBytes>0?Math.Min(99,Math.Max(0,(Bytes+(double)VerifiedBytes)*100.0/(TotalBytes*(VerifyRequested?2.0:1.0)))):0;}}
 }
 public static class EdgeBackup {
  public static string SourcePath{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Microsoft\Edge\User Data\Default");}}
  public static string DestinationRoot{get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Edge");}}
  static string Canonical(string path){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
  public static bool Nested(string path,string parent){return (Canonical(path)+Path.DirectorySeparatorChar).StartsWith(Canonical(parent)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
  public static bool Link(string path){return (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;}
  static void CheckParents(string path){for(var dir=new DirectoryInfo(path);dir!=null;dir=dir.Parent)if(dir.Exists&&Link(dir.FullName))throw new IOException("不通过目录链接读取或保存备份："+dir.FullName);}
  public static void Validate(string source,string destination){
   if(!Directory.Exists(source))throw new DirectoryNotFoundException("未找到 Edge 资料目录："+source);
   if(Nested(source,destination)||Nested(destination,source))throw new IOException("备份来源和目标目录不能重叠。");
   CheckParents(source);CheckParents(destination);
  }
  static string Safe(string name){return new string(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).Take(40).ToArray());}
  public static string NewFolder(string root,string machine,string user){return Path.Combine(root,"Edge备份-"+Safe(machine)+"-"+Safe(user)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".partial");}
  static void Error(BackupResult result,string path,Exception error){result.Failed++;if(result.Errors.Count<200)result.Errors.Add(path+"："+error.Message);}
  // This generic copier is exercised only against synthetic fixtures in self-tests.
  public static BackupResult Copy(string source,string root,string machine,string user,CancellationToken cancel,Action<BackupProgress> progress,bool verify=false){
   cancel.ThrowIfCancellationRequested();Validate(source,root);
   var result=new BackupResult{Status="incomplete",Source=source,Started=DateTime.Now.ToString("o"),Destination=NewFolder(root,machine,user),VerifyRequested=verify,Verification=verify?"校验未完成":"未选择内容校验"};
   string data=Path.Combine(result.Destination,"Default");
   try{
    progress(new BackupProgress{Stage="counting"});
    result.TotalBytes=BackupIntegrity.Preflight(source,root,cancel).Bytes;
    Directory.CreateDirectory(result.Destination);
    var watch=Stopwatch.StartNew();long last=-250;
    Action<string> notify=stage=>{if(watch.ElapsedMilliseconds-last>=150){last=watch.ElapsedMilliseconds;double work=result.Bytes+(double)result.VerifiedBytes,rate=watch.Elapsed.TotalSeconds>=0.5?work/watch.Elapsed.TotalSeconds:0;progress(new BackupProgress{Stage=stage,Bytes=result.Bytes,TotalBytes=result.TotalBytes,Files=result.Files,VerifyRequested=verify,VerifiedBytes=result.VerifiedBytes,BytesPerSecond=rate,RemainingSeconds=rate>0?(double?)Math.Max(0,(result.TotalBytes*(verify?2.0:1.0)-work)/rate):null});}};
    CopyDirectory(source,data,result,cancel,notify);
    cancel.ThrowIfCancellationRequested();result.Status=result.Failed==0?"completed":"partial";
   }catch(OperationCanceledException){result.Status="cancelled";}
   catch(Exception e){result.Status="failed";Error(result,source,e);}
   result.Finished=DateTime.Now.ToString("o");
   if(verify)result.Verification=result.Status=="completed"&&result.VerifiedFiles==result.Files?"SHA-256 内容校验通过":"校验未完整通过，请查看失败项目；不要视为完整备份";
   if(result.Status=="completed"){
    string complete=result.Destination.Substring(0,result.Destination.Length-".partial".Length);
    try{Directory.Move(result.Destination,complete);result.Destination=complete;}
    catch(Exception e){result.Status="partial";Error(result,result.Destination,e);}
   }
   try{if(Directory.Exists(result.Destination))File.WriteAllText(Path.Combine(result.Destination,"backup-result.json"),Operations.Json().Serialize(result),new UTF8Encoding(false));}
   catch(Exception e){result.Status="partial";Error(result,"无法保存备份结果说明",e);}
   progress(new BackupProgress{Stage=result.Status,Bytes=result.Bytes,TotalBytes=result.TotalBytes,Files=result.Files,VerifyRequested=verify,VerifiedBytes=result.VerifiedBytes});
   return result;
  }
  static long CountBytes(string directory,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();if(Link(directory))return 0;long bytes=0;
   foreach(string entry in Directory.EnumerateFileSystemEntries(directory)){
    cancel.ThrowIfCancellationRequested();var attr=File.GetAttributes(entry);if((attr&FileAttributes.ReparsePoint)!=0)continue;
    bytes=checked(bytes+((attr&FileAttributes.Directory)!=0?CountBytes(entry,cancel):new FileInfo(entry).Length));
   }
   return bytes;
  }
  static void CopyDirectory(string source,string target,BackupResult result,CancellationToken cancel,Action<string> progress){
   cancel.ThrowIfCancellationRequested();
   if(Link(source)){Error(result,source,new IOException("跳过目录链接"));return;}
   Directory.CreateDirectory(target);
   foreach(string entry in Directory.EnumerateFileSystemEntries(source)){
    cancel.ThrowIfCancellationRequested();
    try{
     FileAttributes attr=File.GetAttributes(entry);
     if((attr&FileAttributes.ReparsePoint)!=0){Error(result,entry,new IOException("跳过文件或目录链接"));continue;}
     string output=Path.Combine(target,Path.GetFileName(entry));
     if((attr&FileAttributes.Directory)!=0){CopyDirectory(entry,output,result,cancel,progress);continue;}
     using(var input=new FileStream(entry,FileMode.Open,FileAccess.Read,FileShare.Read))
     using(var hash=result.VerifyRequested?SHA256.Create():null){
      using(var dest=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
       byte[] buffer=new byte[1024*1024];int read;
       while((read=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();dest.Write(buffer,0,read);if(hash!=null)hash.TransformBlock(buffer,0,read,null,0);result.Bytes+=read;progress("copying");}
       dest.Flush(true);
      }
      result.Files++;
      if(hash!=null){hash.TransformFinalBlock(new byte[0],0,0);if(!BackupIntegrity.VerifyFile(output,hash.Hash,cancel,count=>{result.VerifiedBytes+=count;progress("verifying");}))throw new IOException("SHA-256 不一致：目标文件与复制时读取的源内容不符");result.VerifiedFiles++;}
     }
     progress("copying");
    }catch(OperationCanceledException){throw;}
    catch(Exception e){Error(result,entry,e);}
   }
  }
  static List<Process> OwnedEdge(){
   string sid=WindowsIdentity.GetCurrent().User.Value;int session=Process.GetCurrentProcess().SessionId;var owned=new List<Process>();
   foreach(var process in Process.GetProcessesByName("msedge")){
    bool keep=false;
    try{
     if(process.HasExited||process.SessionId!=session)continue;
     using(var item=new ManagementObject(@"root\cimv2:Win32_Process.Handle='"+process.Id+"'"))
     using(var owner=item.InvokeMethod("GetOwnerSid",null,null))keep=owner!=null&&Convert.ToUInt32(owner["ReturnValue"])==0&&String.Equals(Convert.ToString(owner["Sid"]),sid,StringComparison.OrdinalIgnoreCase);
     if(keep)owned.Add(process);
    }catch(ManagementException){if(!process.HasExited)throw;}
    finally{if(!keep)process.Dispose();}
   }
   return owned;
  }
  public static void CloseCurrentUsersEdge(CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();
   var processes=OwnedEdge();try{foreach(var p in processes){cancel.ThrowIfCancellationRequested();if(!p.HasExited)p.CloseMainWindow();}}finally{foreach(var p in processes)p.Dispose();}
   if(cancel.WaitHandle.WaitOne(2000))cancel.ThrowIfCancellationRequested();
   processes=OwnedEdge();try{foreach(var p in processes){cancel.ThrowIfCancellationRequested();if(!p.HasExited){p.Kill();p.WaitForExit(3000);}}}finally{foreach(var p in processes)p.Dispose();}
   if(cancel.WaitHandle.WaitOne(2000))cancel.ThrowIfCancellationRequested();
   processes=OwnedEdge();try{if(processes.Any(p=>!p.HasExited))throw new IOException("Edge 仍在运行，请自行关闭后重试。");}finally{foreach(var p in processes)p.Dispose();}
  }
 }
}
