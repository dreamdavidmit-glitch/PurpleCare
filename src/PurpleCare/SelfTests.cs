using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace PurpleCare {
 public static class SelfTests {
  static void Assert(bool condition,string name,List<string> passed){if(!condition)throw new Exception("FAILED: "+name);passed.Add("PASS: "+name);}
  static DeviceRow Device(string cls,string id){return new DeviceRow{Class=cls,Id=id,Name="Test",Present=true,Code=0};}
  public static int Run(string path){var checks=new List<string>();try{
   Assert(DevicePolicy.AllowedRestart(Device("Net",@"PCI\VEN_1234&DEV_5678\1")),"network adapter permitted",checks);
   Assert(DevicePolicy.AllowedRestart(Device("MEDIA",@"HDAUDIO\FUNC_01\1")),"audio adapter permitted",checks);
   foreach(var cls in new[]{"DiskDrive","SCSIAdapter","HDC","System","Processor","USB","HIDClass","Keyboard","Volume","Unknown"})Assert(!DevicePolicy.AllowedRestart(Device(cls,@"PCI\TEST\1")),"protect "+cls,checks);
   foreach(var id in new[]{"*",@"PCI\*","PCI\\TEST\" /reboot", "PCI\\TEST\n/reboot","",null})Assert(!DevicePolicy.ValidId(id),"reject wildcard / malformed ID",checks);
   var disabled=Device("Net",@"PCI\A\B");disabled.Code=22;Assert(!DevicePolicy.AllowedRestart(disabled),"do not enable disabled adapters",checks);
   var absent=Device("Net",@"PCI\A\B");absent.Present=false;Assert(!DevicePolicy.AllowedRestart(absent),"do not restart absent adapters",checks);
   var command=Operations.RestartArgs(@"PCI\VEN_1234&DEV_5678\1");Assert(command=="/restart-device \"PCI\\VEN_1234&DEV_5678\\1\"","exact ID command, no shell expansion or wildcard",checks);
   Assert(!command.Contains("/reboot")&&!command.Contains("/force"),"no automatic reboot or force",checks);
   var good=new FakeServices();var r=Operations.RepairAudio(good);Assert(r.Success,"audio repair success",checks);Assert(String.Join(",",good.Calls)=="stop:Audiosrv,stop:AudioEndpointBuilder,start:AudioEndpointBuilder,start:Audiosrv","dependency stop/start order",checks);
   var failure=new FakeServices{FailStopBuilder=true};r=Operations.RepairAudio(failure);Assert(!r.Success&&failure.Calls.Contains("start:Audiosrv")&&failure.Calls.Contains("start:AudioEndpointBuilder"),"restore both services after stop failure",checks);
   var failStart=new FakeServices{FailStartBuilder=true};r=Operations.RepairAudio(failStart);Assert(!r.Success&&failStart.Calls.Contains("start:Audiosrv"),"still attempt Audio restoration after builder start failure",checks);
   var external=new FakeServices{ExtraDependent=true};r=Operations.RepairAudio(external);Assert(!r.Success&&external.Calls.Count==0,"do not stop unrelated dependent service",checks);
   var json=Operations.Json().Serialize(new ScanReport{Computer="测试电脑",Model="型号"});Assert(Operations.Json().Deserialize<ScanReport>(json).Model=="型号","Chinese report round trip",checks);
   using(var sha=new System.Security.Cryptography.SHA256Managed()){
    var digest=sha.ComputeHash(StressEngine.CpuData());Assert(StressEngine.CorrectHash(digest),"CPU reference hash agrees with independently computed digest",checks);digest[0]^=1;Assert(!StressEngine.CorrectHash(digest),"CPU checker detects corrupted digest",checks);
   }
   var block=new uint[4096];for(int i=0;i<block.Length;i++)block[i]=StressEngine.Pattern(i,7);Assert(StressEngine.CheckBlock(block,7),"memory pattern round trip",checks);block[2048]^=1;Assert(!StressEngine.CheckBlock(block,7),"memory checker detects corrupted cell",checks);
   Assert(StressMetrics.CpuDelta(100,200,150,300)==50,"CPU percentage excludes idle kernel time",checks);
   Assert(!StressMetrics.CpuDelta(100,200,100,200).HasValue,"missing CPU interval remains unknown",checks);
   Assert(StressMetrics.MemoryBudget(1024,256UL*1048576,8UL*1024*1048576)==0,"low memory prevents test allocation",checks);
   Assert(StressMetrics.MemoryBudget(1024,2UL*1024*1048576,8UL*1024*1048576)<=512,"memory capped at quarter available",checks);
   Assert(StressEngine.Outcome(true,0,true)=="cancelled","cancelled test never marked passed",checks);
   Assert(StressEngine.Outcome(false,0,false)=="incomplete","zero completed checks never marked passed",checks);
   Assert(StressEngine.Outcome(false,1,true)=="error","calculation errors override completion",checks);
   bool invalid=false;try{new StressOptions{Mode="cpu",Seconds=3600,Threads=1,Duty=50,MemoryMB=128}.Validate();}catch(ArgumentException){invalid=true;}Assert(invalid,"reject unbounded stress duration",checks);
   invalid=false;try{StressFiles.Folder("..\\..");}catch(ArgumentException){invalid=true;}Assert(invalid,"reject stress result path traversal",checks);
   var stopped=StressEngine.Run(new StressOptions{Mode="combined",Seconds=2,Threads=1,Duty=10,MemoryMB=16},()=>true,snapshot=>{});Assert(stopped.Status=="cancelled"&&stopped.CpuChecks==0&&stopped.MemoryPasses==0,"pre-cancelled run starts no workload",checks);
   var userReport=new ScanReport{UserName=Environment.UserName,AccountName=System.Security.Principal.WindowsIdentity.GetCurrent().Name};Assert(Operations.Json().Deserialize<ScanReport>(Operations.Json().Serialize(userReport)).UserName==Environment.UserName,"username included in exported report",checks);
   Assert(PortablePaths.Scope("PC-A","user","SID-1")!=PortablePaths.Scope("PC-B","user","SID-1"),"portable results separated across computers",checks);
   Assert(PortablePaths.Scope("PC-A","user","SID-1")!=PortablePaths.Scope("PC-A","user","SID-2"),"portable results separated across accounts",checks);
   Assert(!PortablePaths.Scope("PC/A","u:s","SID").Any(c=>Path.GetInvalidFileNameChars().Contains(c)),"portable directory names sanitized",checks);
   Assert(GpuTemperature.Celsius(635)==63.5,"WDDM deci-Celsius converted to Celsius",checks);
   Assert(GpuTemperature.Physical(3)&&!GpuTemperature.Physical(7)&&!GpuTemperature.Physical(67)&&!GpuTemperature.Physical(131),"exclude software and indirect display adapters from GPU temperatures",checks);
   Assert(!GpuTemperature.Celsius(0).HasValue&&!GpuTemperature.Celsius(UInt32.MaxValue).HasValue,"GPU missing values never shown as normal",checks);
   Assert(TemperatureSensors.Normalize(3000,true)==26.9,"ACPI decikelvin converted to Celsius",checks);
   Assert(TemperatureSensors.Normalize(63,false)==63,"Lenovo Celsius not converted twice",checks);
   foreach(object raw in new object[]{null,0,UInt32.MaxValue,Double.NaN,-1})Assert(!TemperatureSensors.Normalize(raw,false).HasValue,"invalid temperature stays unavailable",checks);
   Assert(!TemperatureSensors.Normalize(0,true).HasValue,"missing ACPI reading not shown as absolute zero",checks);
   var tempReport=new ScanReport{Temperatures=new TemperatureReport{Readings=new List<TemperatureReading>{new TemperatureReading{Name="系统温区",Celsius=48,Source="ACPI"}}}};
   Assert(Operations.Json().Deserialize<ScanReport>(Operations.Json().Serialize(tempReport)).Temperatures.Readings[0].Celsius==48,"temperature data survives elevated report transfer",checks);
   tempReport.Facts.Add(new Fact("型号","测试型号"));tempReport.Findings.Add(new Finding("注意","检查","说明"));
   var elevated=Operations.Json().Deserialize<OperationResult>(Operations.Json().Serialize(new OperationResult{Report=tempReport}));
   Assert(elevated.Report.Facts[0].Value=="测试型号"&&elevated.Report.Findings[0].Detail=="说明","populated facts and findings survive administrator report transfer",checks);
   BackupTests.Run(path,checks);
   DiagnosticsTests.Run(path,checks);
   SecondBatchTests.Run(path,checks);
   ThirdBatchTests.Run(path,checks);
   File.WriteAllLines(path,checks.Concat(new[]{"All "+checks.Count+" checks passed. No devices or services were changed."}),new UTF8Encoding(false));return 0;
  }catch(Exception e){checks.Add(e.ToString());File.WriteAllLines(path,checks,new UTF8Encoding(false));return 1;}}
  class FakeServices:IAudioServices{
   public List<string> Calls=new List<string>();public bool FailStopBuilder,FailStartBuilder,ExtraDependent;
   public string State(string name){return "Running";}
   public IEnumerable<string> RunningDependents(string name){return ExtraDependent?new[]{"UnrelatedService"}:name=="AudioEndpointBuilder"?new[]{"Audiosrv"}:new string[0];}
   public void Stop(string name){Calls.Add("stop:"+name);if(FailStopBuilder&&name=="AudioEndpointBuilder")throw new Exception("simulated stop failure");}
   public void Start(string name){Calls.Add("start:"+name);if(FailStartBuilder&&name=="AudioEndpointBuilder")throw new Exception("simulated start failure");}
  }
 }
}
