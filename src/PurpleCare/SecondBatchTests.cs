using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
namespace PurpleCare {
 public static class SecondBatchTests {
  public static void Run(string output,List<string> checks){
   Action<bool,string> test=(ok,name)=>{if(!ok)throw new Exception(name);checks.Add("PASS: "+name);};
   string dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"integrity-fixtures-"+Guid.NewGuid().ToString("N"));string source=Path.Combine(dir,"source"),target=Path.Combine(dir,"target");Directory.CreateDirectory(source);
   byte[] bytes=Enumerable.Range(0,10000).Select(x=>(byte)x).ToArray();File.WriteAllBytes(Path.Combine(source,"sample.bin"),bytes);File.WriteAllBytes(Path.Combine(source,"empty"),new byte[0]);
   var plan=BackupIntegrity.Preflight(source,target,CancellationToken.None);
   test(plan.Bytes==bytes.Length&&plan.Files==2&&plan.Available>plan.Bytes,"preflight measures synthetic files and destination capacity");
   test(!Directory.EnumerateFiles(target).Any(),"preflight write probe is removed");
   test(!BackupIntegrity.Fits(100,1,99)&&!BackupIntegrity.Fits(100,1000,1000)&&BackupIntegrity.Fits(100,1,100000000),"space check includes allocation and metadata reserve");
   var r=EdgeBackup.Copy(source,target,"test","test",CancellationToken.None,p=>{},true);
   test(r.Status=="completed"&&r.VerifiedFiles==2&&r.VerifiedBytes==bytes.Length&&r.Verification.Contains("通过"),"verified backup checks nonempty and empty destination files");
   byte[] expected;using(var sha=SHA256.Create())expected=sha.ComputeHash(bytes);
   var file=Path.Combine(r.Destination,"Default","sample.bin");File.WriteAllText(file,"deliberately changed synthetic data");
   test(!BackupIntegrity.VerifyFile(file,expected,CancellationToken.None,n=>{}),"SHA256 verification detects corrupted destination content");
   using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{BackupIntegrity.VerifyFile(file,expected,cancel.Token,n=>{});}catch(OperationCanceledException){cancelled=true;}test(cancelled,"content verification respects cancellation");}
   test(new BackupProgress{Stage="verifying",Bytes=100,TotalBytes=100,VerifyRequested=true}.Percent==50,"copy completion is only halfway when verification is selected");
   test(new BackupProgress{Stage="partial",Bytes=100,VerifiedBytes=100,TotalBytes=100,VerifyRequested=true}.Percent<100,"failed verified backup cannot display completed progress");
   var values=new List<LiveSample>();DateTime now=DateTime.Now;for(int i=0;i<200;i++)LiveHistory.Add(values,new LiveSample{Time=now.AddSeconds(i*2)});
   test(values.Count==150&&values.Last().Time-values.First().Time<=TimeSpan.FromMinutes(5),"live monitor history is bounded to 150 samples and five minutes");
   LiveHistory.Add(values,new LiveSample{Time=now.AddHours(1)});test(values.Count==1&&!values[0].Cpu.HasValue&&!values[0].Gpu.HasValue,"stale monitoring history expires and missing readings stay unknown");
  }
 }
}
