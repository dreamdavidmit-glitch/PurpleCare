using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PurpleCare {
 public static class BackupTests {
  public static void Run(string output,List<string> checks){
   Action<bool,string> test=(ok,name)=>{if(!ok)throw new Exception(name);checks.Add("PASS: "+name);};
   string fixture=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"backup-fixtures-"+Guid.NewGuid().ToString("N"));
   string source=Path.Combine(fixture,"synthetic-input"),dest=Path.Combine(fixture,"synthetic-output");
   Directory.CreateDirectory(Path.Combine(source,"nested"));File.WriteAllText(Path.Combine(source,"example.txt"),"synthetic example only");File.WriteAllBytes(Path.Combine(source,"nested","sample.bin"),new byte[]{0,1,2,255});
   var updates=new List<BackupProgress>();var result=EdgeBackup.Copy(source,dest,"test-PC","test-user",CancellationToken.None,updates.Add);
   test(result.Status=="completed"&&result.Files==2,"synthetic backup completes with correct file count");
   test(File.ReadAllBytes(Path.Combine(result.Destination,"Default","nested","sample.bin")).SequenceEqual(new byte[]{0,1,2,255}),"Default backup preserves nested bytes");
   test(updates.First().Stage=="counting"&&updates.Last().Stage=="completed"&&updates.Last().Percent==100&&result.TotalBytes==result.Bytes,"progress counts real bytes and completes at 100 percent");
   test(updates.Where(p=>p.Stage!="completed").All(p=>p.Percent<100),"unfinished progress never claims 100 percent");
   test(EdgeBackup.SourcePath.EndsWith(@"Microsoft\Edge\User Data\Default",StringComparison.OrdinalIgnoreCase),"production source is limited to Default");
   test(!Directory.Exists(Path.Combine(result.Destination,"User Data")),"export does not include the User Data parent directory");
   test(File.ReadAllText(Path.Combine(source,"example.txt"))=="synthetic example only","backup leaves source intact");
   test(File.Exists(Path.Combine(result.Destination,"backup-result.json"))&&!result.Destination.EndsWith(".partial"),"successful backup has manifest and completion directory");
   var second=EdgeBackup.Copy(source,dest,"test-PC","test-user",CancellationToken.None,s=>{});
   test(second.Destination!=result.Destination&&Directory.Exists(result.Destination),"repeated backups never overwrite earlier results");
   bool refused=false;try{EdgeBackup.Validate(source,Path.Combine(source,"backup"));}catch(IOException){refused=true;}test(refused,"reject recursive backup destination inside source");
   var cancel=new CancellationTokenSource();cancel.Cancel();string cancelledRoot=Path.Combine(fixture,"cancelled");
   refused=false;try{EdgeBackup.Copy(source,cancelledRoot,"test","test",cancel.Token,s=>{});}catch(OperationCanceledException){refused=true;}test(refused&&!Directory.Exists(cancelledRoot),"pre-cancelled backup writes no files");cancel.Dispose();
   using(var locked=new FileStream(Path.Combine(source,"example.txt"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
    var partial=EdgeBackup.Copy(source,dest,"test-PC","test-user",CancellationToken.None,s=>{});
    test(partial.Status=="partial"&&partial.Failed==1&&partial.Destination.EndsWith(".partial"),"locked file produces partial status instead of success");
   }
   test(EdgeBackup.DestinationRoot==Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Edge"),"backup destination follows executable location without fixed drive");
  }
 }
}
