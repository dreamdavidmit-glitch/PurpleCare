using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PurpleCare {
 public sealed class StressOptions {
  public string Mode{get;set;} public int Seconds{get;set;} public int Threads{get;set;} public int Duty{get;set;} public int MemoryMB{get;set;}
  public void Validate(){
   if(Mode!="cpu"&&Mode!="memory"&&Mode!="combined")throw new ArgumentException("无效测试类型");
   if(Seconds<2||Seconds>180)throw new ArgumentException("测试时长须为 2–180 秒");
   if(Threads<1||Threads>Math.Min(64,Environment.ProcessorCount))throw new ArgumentException("测试线程数超出范围");
   if(Duty<10||Duty>90)throw new ArgumentException("占空比须为 10–90%");
   if(MemoryMB<16||MemoryMB>1024)throw new ArgumentException("测试内存须为 16–1024 MB");
  }
  public bool Cpu{get{return Mode!="memory";}} public bool Memory{get{return Mode!="cpu";}}
 }
 public sealed class StressSample {
  public double Seconds{get;set;} public double? CpuPercent{get;set;} public double? WorkerCpuPercent{get;set;} public double? MemoryPercent{get;set;}
  public long CpuChecks{get;set;} public long MemoryPasses{get;set;} public long Errors{get;set;}
 }
 public sealed class StressReport {
  public string Started{get;set;} public string Computer{get;set;} public string UserName{get;set;}
  public StressOptions Options{get;set;} public string Status{get;set;} public string Message{get;set;}
  public int AllocatedMemoryMB{get;set;} public double ElapsedSeconds{get;set;} public long CpuChecks{get;set;} public long MemoryPasses{get;set;} public long Errors{get;set;}
  public List<StressSample> Samples=new List<StressSample>();
 }
 public static class StressMetrics {
  [StructLayout(LayoutKind.Sequential)] public struct MemoryState {
   public uint Length,Load; public ulong Total,Available,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,AvailableExtended;
  }
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryState state);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
  public static MemoryState Memory(){var s=new MemoryState();s.Length=(uint)Marshal.SizeOf(typeof(MemoryState));if(!GlobalMemoryStatusEx(ref s))throw new System.ComponentModel.Win32Exception();return s;}
  public static bool Cpu(out long idle,out long total){long kernel,user;bool ok=GetSystemTimes(out idle,out kernel,out user);total=kernel+user;return ok;}
  public static double? CpuDelta(long idle0,long total0,long idle,long total){long dt=total-total0,di=idle-idle0;return dt<=0||di<0?(double?)null:Math.Max(0,Math.Min(100,100.0*(dt-di)/dt));}
  public static int MemoryBudget(int requested,ulong available,ulong total){
   ulong reserve=Math.Max(512UL*1024*1024,total/10);
   if(available<=reserve)return 0;
   ulong allowed=Math.Min(available/4,available-reserve)/1048576;
   return (int)Math.Min((ulong)Math.Min(requested,1024),allowed)/16*16;
  }
 }
 public static class StressEngine {
  static readonly byte[] Expected=Enumerable.Range(0,32).Select(i=>Convert.ToByte("e11360251d1173650cdcd20f111d8f1ca2e412f572e8b36a4dc067121c1799b8".Substring(i*2,2),16)).ToArray();
  public static byte[] CpuData(){var b=new byte[32768];for(int i=0;i<b.Length;i++)b[i]=(byte)i;return b;}
  public static bool CorrectHash(byte[] hash){return hash!=null&&hash.SequenceEqual(Expected);}
  public static uint Pattern(int index,uint pass){unchecked{return ((uint)index*2654435761u)^(pass*2246822519u)^0xA5A55A5Au;}}
  public static bool CheckBlock(uint[] block,uint pass){for(int i=0;i<block.Length;i++)if(block[i]!=Pattern(i,pass))return false;return true;}
  public static string Outcome(bool cancelled,long errors,bool completedWork){return errors>0?"error":cancelled?"cancelled":completedWork?"completed":"incomplete";}
  public static StressReport Run(StressOptions o,Func<bool> shouldStop,Action<StressReport> publish){
   o.Validate();var r=new StressReport{Started=DateTime.Now.ToString("o"),Computer=Environment.MachineName,UserName=Environment.UserName,Options=o,Status="running",Message="测试进行中"};
   var timer=Stopwatch.StartNew();var tasks=new List<Task>();var failures=new System.Collections.Concurrent.ConcurrentQueue<string>();
   long checks=0,passes=0,errors=0;bool cancelled=false;var cancel=new CancellationTokenSource();var token=cancel.Token;
   try{
    if(shouldStop()){r.Status="cancelled";r.Message="测试已停止";return r;}
    if(o.Memory){var memory=StressMetrics.Memory();r.AllocatedMemoryMB=StressMetrics.MemoryBudget(o.MemoryMB,memory.Available,memory.Total);if(r.AllocatedMemoryMB<16)throw new InvalidOperationException("可用内存不足，未开始压力测试。");}
    Action<Action> start=work=>tasks.Add(Task.Factory.StartNew(()=>{try{Thread.CurrentThread.Priority=ThreadPriority.BelowNormal;work();}catch(OperationCanceledException){}catch(Exception e){Interlocked.Increment(ref errors);failures.Enqueue(e.Message);cancel.Cancel();}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default));
    if(o.Cpu)for(int t=0;t<o.Threads;t++)start(()=>{
     var data=CpuData();using(var sha=new SHA256Managed())while(!token.IsCancellationRequested){var slice=Stopwatch.StartNew();do{token.ThrowIfCancellationRequested();if(!CorrectHash(sha.ComputeHash(data)))throw new InvalidOperationException("CPU 计算校验不一致，测试已停止。");Interlocked.Increment(ref checks);}while(slice.ElapsedMilliseconds<o.Duty&&!token.IsCancellationRequested);int wait=100-(int)slice.ElapsedMilliseconds;if(wait>0)token.WaitHandle.WaitOne(wait);}
    });
    if(o.Memory)start(()=>{
     // Separate 16 MB blocks keep allocation bounded and make cancellation responsive.
     var blocks=new List<uint[]>();for(int mb=0;mb<r.AllocatedMemoryMB;mb+=16){token.ThrowIfCancellationRequested();blocks.Add(new uint[4*1024*1024]);}
     uint pass=0;while(!token.IsCancellationRequested){pass++;foreach(var block in blocks){for(int i=0;i<block.Length;i++){if((i&4095)==0)token.ThrowIfCancellationRequested();block[i]=Pattern(i,pass);}}
      foreach(var block in blocks)for(int i=0;i<block.Length;i++){if((i&4095)==0)token.ThrowIfCancellationRequested();if(block[i]!=Pattern(i,pass))throw new InvalidOperationException("内存读写校验不一致，测试已停止。");}
      Interlocked.Increment(ref passes);token.WaitHandle.WaitOne(10);
     }
    });
    long idle0,total0;bool cpuOk=StressMetrics.Cpu(out idle0,out total0);double previousTime=timer.Elapsed.TotalSeconds;TimeSpan previousCpu;
    using(var process=Process.GetCurrentProcess()){previousCpu=process.TotalProcessorTime;
     while(timer.Elapsed.TotalSeconds<o.Seconds&&!cancel.IsCancellationRequested){
      if(shouldStop()){cancelled=true;cancel.Cancel();break;}
      token.WaitHandle.WaitOne(250);
      if(timer.Elapsed.TotalSeconds-previousTime<0.45&&timer.Elapsed.TotalSeconds<o.Seconds)continue;
      double now=timer.Elapsed.TotalSeconds;long idle,total;bool ok=StressMetrics.Cpu(out idle,out total);var mem=StressMetrics.Memory();var cpu=process.TotalProcessorTime;
      r.Samples.Add(new StressSample{Seconds=now,CpuPercent=ok&&cpuOk?StressMetrics.CpuDelta(idle0,total0,idle,total):null,WorkerCpuPercent=Math.Min(100,Math.Max(0,(cpu-previousCpu).TotalSeconds/Math.Max(0.01,now-previousTime)/Environment.ProcessorCount*100)),MemoryPercent=mem.Total>0?(double?)(100.0*(mem.Total-mem.Available)/mem.Total):null,CpuChecks=Interlocked.Read(ref checks),MemoryPasses=Interlocked.Read(ref passes),Errors=Interlocked.Read(ref errors)});
      idle0=idle;total0=total;cpuOk=ok;previousTime=now;previousCpu=cpu;r.ElapsedSeconds=now;r.CpuChecks=Interlocked.Read(ref checks);r.MemoryPasses=Interlocked.Read(ref passes);r.Errors=Interlocked.Read(ref errors);publish(r);
      if(mem.Available<Math.Max(256UL*1048576,mem.Total/20)){failures.Enqueue("系统可用内存过低，已提前停止。");Interlocked.Increment(ref errors);cancel.Cancel();}
     }
    }
   }catch(Exception e){Interlocked.Increment(ref errors);failures.Enqueue(e.Message);}
   finally{cancel.Cancel();try{Task.WaitAll(tasks.ToArray());}catch(Exception e){Interlocked.Increment(ref errors);failures.Enqueue(e.Message);}cancel.Dispose();timer.Stop();r.ElapsedSeconds=timer.Elapsed.TotalSeconds;r.CpuChecks=Interlocked.Read(ref checks);r.MemoryPasses=Interlocked.Read(ref passes);r.Errors=Interlocked.Read(ref errors);}
   bool enough=(!o.Cpu||r.CpuChecks>0)&&(!o.Memory||r.MemoryPasses>0);
   r.Status=Outcome(cancelled,r.Errors,enough);
   r.Message=r.Status=="completed"?"测试完成，本次负载未发现计算或读写校验错误。":r.Status=="cancelled"?"用户已停止测试，结果不构成完整测试结论。":r.Status=="incomplete"?"测试时间内未完成至少一轮校验，不能判断结果。":String.Join("；",failures.ToArray());
   publish(r);return r;
  }
 }
 public static class StressFiles {
  public static string Folder(string id){Guid g;if(!Guid.TryParseExact(id,"N",out g))throw new ArgumentException("无效测试编号");return Path.Combine(PortablePaths.Root,"Stress",id);}
  public static void Write(string path,object value){string temp=path+".tmp";File.WriteAllText(temp,Operations.Json().Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public static StressReport Read(string path){try{return Operations.Json().Deserialize<StressReport>(File.ReadAllText(path));}catch{return null;}}
  public static int Worker(string[] args){
   if(args.Length!=8)return 2;
   try{
    var o=new StressOptions{Mode=args[2],Seconds=Int32.Parse(args[3]),Threads=Int32.Parse(args[4]),Duty=Int32.Parse(args[5]),MemoryMB=Int32.Parse(args[6])};o.Validate();
    int ownerId=Int32.Parse(args[7]);using(var owner=Process.GetProcessById(ownerId)){
     // An orphaned test exits promptly; the deadline also remains enforced in the worker.
     string folder=Folder(args[1]);Directory.CreateDirectory(folder);try{Process.GetCurrentProcess().PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
     Func<bool> stop=()=>{try{return owner.HasExited||File.Exists(Path.Combine(folder,"cancel"));}catch{return true;}};
     var result=StressEngine.Run(o,stop,r=>Write(Path.Combine(folder,"progress.json"),r));Write(Path.Combine(folder,"result.json"),result);return result.Status=="completed"?0:result.Status=="cancelled"?3:1;
    }
   }catch{return 2;}
  }
 }
}
