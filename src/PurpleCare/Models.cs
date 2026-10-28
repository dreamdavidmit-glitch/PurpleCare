using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;

namespace PurpleCare {
 public class DeviceRow {
  public string Name {get;set;} public string Id {get;set;} public string Class {get;set;}
  public string Manufacturer {get;set;} public string Driver {get;set;} public string Service {get;set;}
  public int Code {get;set;} public bool Present {get;set;}
  public string Category {get {return DevicePolicy.Category(Class);}}
  public string State {get {return !Present ? "未连接" : Code == 0 ? "未报告故障" : Code == 22 ? "已禁用" : Code < 0 ? "未知" : "异常 · 代码 " + Code;}}
  public string RestartNote {get {return DevicePolicy.Reason(this);}}
  public override string ToString() {return Name + "  ·  " + State;}
 }
 public static class DevicePolicy {
  static readonly string[] Allowed={"MEDIA","AUDIOENDPOINT","NET","BLUETOOTH","CAMERA","IMAGE","PRINTER","PRINTQUEUE","PORTS","DISPLAY","MONITOR"};
  public static bool AllowedRestart(DeviceRow d) {return d!=null && d.Present && d.Code!=22 && Allowed.Contains((d.Class??"").ToUpperInvariant()) && ValidId(d.Id);}
  public static bool ValidId(string id) {return !String.IsNullOrWhiteSpace(id) && id.Length<2048 && System.Text.RegularExpressions.Regex.IsMatch(id,@"^[A-Za-z0-9_]+\\.+") && !id.EndsWith("\\") && !id.Any(c=>c=='"'||c=='\r'||c=='\n'||c=='*'||c=='?'||c<' ');}
  public static string Reason(DeviceRow d) {
   if(d==null)return "请选择一个设备";
   if(!d.Present)return "设备未连接，无法重启";
   if(d.Code==22)return "设备已禁用；请在设备管理器中确认是否需要启用";
   if(!AllowedRestart(d))return "系统关键设备或未适配类型；使用整机重启重新初始化";
   switch((d.Class??"").ToUpperInvariant()) {
    case "NET":return "会断开此网卡的网络；下载、通话、远程桌面可能中断";
    case "DISPLAY":case "MONITOR":return "可能短暂黑屏；某些程序需要重新打开";
    case "BLUETOOTH":return "会断开蓝牙耳机、鼠标及键盘等连接";
    case "MEDIA":case "AUDIOENDPOINT":return "会中断播放和录音；之后可能需要重新打开播放软件";
    default:return "设备会暂时离线，正在使用它的程序可能中断";
   }
  }
  public static string Category(string c) {
   switch((c??"").ToUpperInvariant()) {
    case "MEDIA":case "AUDIOENDPOINT":return "音频";
    case "NET":return "网络"; case "DISPLAY":case "MONITOR":return "显示";
    case "DISKDRIVE":case "SCSIADAPTER":case "HDC":case "VOLUME":case "STORAGE":return "存储";
    case "BLUETOOTH":return "蓝牙"; case "CAMERA":case "IMAGE":return "摄像头";
    case "HIDCLASS":case "KEYBOARD":case "MOUSE":return "输入设备";
    case "USB":return "USB"; case "PROCESSOR":return "处理器";
    case "SYSTEM":case "FIRMWARE":return "系统";
    case "PRINTER":case "PRINTQUEUE":return "打印";
    default:return String.IsNullOrEmpty(c)?"其他":c;
   }
  }
 }
 public class Fact {public string Label{get;set;} public string Value{get;set;} public Fact(){} public Fact(string l,string v){Label=l;Value=v;}}
 public class Finding {public string Level{get;set;} public string Title{get;set;} public string Detail{get;set;} public Finding(){} public Finding(string l,string t,string d){Level=l;Title=t;Detail=d;}}
 public class NetworkRow {
  public string Name{get;set;} public string Description{get;set;} public string State{get;set;}
  public string Addresses{get;set;} public string Gateways{get;set;} public string Dns{get;set;} public string Speed{get;set;}
  public override string ToString(){return Name+" · "+State;}
 }
 public class ScanReport {
  public TemperatureReport Temperatures{get;set;}
  public string UserName{get;set;} public string AccountName{get;set;}
  public string Time{get;set;} public string Computer{get;set;} public string Model{get;set;} public string Manufacturer{get;set;}
  public List<Fact> Facts=new List<Fact>(); public List<DeviceRow> Devices=new List<DeviceRow>();
  public List<EndpointRow> Endpoints=new List<EndpointRow>(); public List<NetworkRow> Networks=new List<NetworkRow>();
  public List<Finding> Findings=new List<Finding>(); public List<string> Limitations=new List<string>();
  public string AudioService{get;set;} public string EndpointService{get;set;}
  public bool DevicesReadable{get;set;} public bool AudioReadable{get;set;}
 }
 public static class Scanner {
  public static List<Dictionary<string,object>> Query(string scope,string sql) {
   var list=new List<Dictionary<string,object>>();
   var options=new EnumerationOptions {ReturnImmediately=true,Rewindable=false,Timeout=TimeSpan.FromSeconds(15)};
   using(var search=new ManagementObjectSearcher(new ManagementScope(scope),new ObjectQuery(sql),options))
   using(var items=search.Get())foreach(ManagementObject item in items)using(item){
    var row=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
    foreach(PropertyData p in item.Properties)row[p.Name]=p.Value; list.Add(row);
   }
   return list;
  }
  public static string Text(Dictionary<string,object> r,string key) {object v;return r.TryGetValue(key,out v)&&v!=null?Convert.ToString(v).Trim():"";}
  static double Number(Dictionary<string,object> r,string key) {double n;return Double.TryParse(Text(r,key),out n)?n:0;}
  static string First(List<Dictionary<string,object>> a,string key){return a.Count>0?Text(a[0],key):"未读取到";}
  static string GB(double n){return (n/1073741824.0).ToString("0.0")+" GB";}
  static void Part(ScanReport r,string name,Action f){try{f();}catch(Exception e){r.Limitations.Add(name+"："+e.Message);}}
  public static List<DeviceRow> Devices(bool drivers) {
   var rows=Query("root\\cimv2","SELECT Name,PNPDeviceID,PNPClass,Manufacturer,ConfigManagerErrorCode,Present,Service FROM Win32_PnPEntity");
   var result=rows.Where(x=>!String.IsNullOrEmpty(Text(x,"PNPDeviceID"))).Select(x=>new DeviceRow{
    Name=String.IsNullOrEmpty(Text(x,"Name"))?Text(x,"PNPDeviceID"):Text(x,"Name"), Id=Text(x,"PNPDeviceID"),Class=Text(x,"PNPClass"),Manufacturer=Text(x,"Manufacturer"),Service=Text(x,"Service"),
    Code=String.IsNullOrEmpty(Text(x,"ConfigManagerErrorCode"))?-1:(int)Number(x,"ConfigManagerErrorCode"),Present=Text(x,"Present")!="False",Driver="未提供"}).ToList();
   if(drivers)try{
    var versions=Query("root\\cimv2","SELECT DeviceID,DriverVersion FROM Win32_PnPSignedDriver").GroupBy(x=>Text(x,"DeviceID"),StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>Text(x.First(),"DriverVersion"),StringComparer.OrdinalIgnoreCase);
    foreach(var d in result){string version;if(versions.TryGetValue(d.Id,out version))d.Driver=version;}
   }catch{}
   return result.OrderBy(x=>x.Category).ThenBy(x=>x.Name).ToList();
  }
  public static string ServiceState(string name){try{using(var s=new ServiceController(name)){return s.Status.ToString();}}catch(Exception e){return "无法读取："+e.Message;}}
  public static List<NetworkRow> Networks() {
   var rows=new List<NetworkRow>();
   foreach(var n in NetworkInterface.GetAllNetworkInterfaces()){
    if(n.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
    var row=new NetworkRow{Name=n.Name,Description=n.Description,State=n.OperationalStatus==OperationalStatus.Up?"已连接":n.OperationalStatus==OperationalStatus.Down?"未连接":n.OperationalStatus==OperationalStatus.Dormant?"待机":n.OperationalStatus==OperationalStatus.NotPresent?"设备不存在":n.OperationalStatus==OperationalStatus.LowerLayerDown?"底层链路断开":"未知",Speed=n.Speed>0?(n.Speed/1000000)+" Mbps":"未知"};
    try {var ip=n.GetIPProperties();row.Addresses=String.Join(" / ",ip.UnicastAddresses.Where(x=>x.Address.AddressFamily==AddressFamily.InterNetwork).Select(x=>x.Address.ToString()));row.Gateways=String.Join(" / ",ip.GatewayAddresses.Select(x=>x.Address.ToString()));row.Dns=String.Join(" / ",ip.DnsAddresses.Select(x=>x.ToString()));}catch(Exception e){row.Addresses="无法读取："+e.Message;}
    rows.Add(row);
   } return rows.OrderByDescending(x=>x.State=="已连接").ThenBy(x=>x.Name).ToList();
  }
  public static ScanReport Scan(Action<string> progress) {
   var r=new ScanReport{Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Computer=Environment.MachineName,UserName=Environment.UserName,AccountName=System.Security.Principal.WindowsIdentity.GetCurrent().Name,Model="未读取到",Manufacturer=""};
   r.Facts.Add(new Fact("当前用户名",r.UserName));r.Facts.Add(new Fact("完整账户名",r.AccountName));
   progress("正在识别电脑名称、型号和主要硬件…");
   Part(r,"电脑信息",()=>{var a=Query("root\\cimv2","SELECT Manufacturer,Model,TotalPhysicalMemory FROM Win32_ComputerSystem");r.Model=First(a,"Model");r.Manufacturer=First(a,"Manufacturer");r.Facts.Add(new Fact("内存总量",a.Count>0?GB(Number(a[0],"TotalPhysicalMemory")):"未知"));});
   Part(r,"产品型号",()=>{var a=Query("root\\cimv2","SELECT Version FROM Win32_ComputerSystemProduct");string version=First(a,"Version");if(!String.IsNullOrWhiteSpace(version)&&version!="未读取到"&&version!="None"&&version!="Default string"&&!version.Equals(r.Model,StringComparison.OrdinalIgnoreCase))r.Model=version+" ("+r.Model+")";});
   Part(r,"系统",()=>{var a=Query("root\\cimv2","SELECT Caption,Version,OSArchitecture,FreePhysicalMemory,LastBootUpTime FROM Win32_OperatingSystem");r.Facts.Add(new Fact("操作系统",First(a,"Caption")+" · "+First(a,"Version")));r.Facts.Add(new Fact("系统架构",First(a,"OSArchitecture")));if(a.Count>0)r.Facts.Add(new Fact("可用内存",GB(Number(a[0],"FreePhysicalMemory")*1024)));});
   Part(r,"处理器",()=>{foreach(var a in Query("root\\cimv2","SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))r.Facts.Add(new Fact("处理器",Text(a,"Name")+" · "+Text(a,"NumberOfCores")+" 核 / "+Text(a,"NumberOfLogicalProcessors")+" 线程"));});
   Part(r,"主板",()=>{foreach(var a in Query("root\\cimv2","SELECT Manufacturer,Product FROM Win32_BaseBoard"))r.Facts.Add(new Fact("主板",Text(a,"Manufacturer")+" "+Text(a,"Product")));});
   Part(r,"显卡",()=>{foreach(var a in Query("root\\cimv2","SELECT Name,DriverVersion FROM Win32_VideoController"))r.Facts.Add(new Fact("显卡",Text(a,"Name")+" · 驱动 "+Text(a,"DriverVersion")));});
   Part(r,"内存条",()=>{foreach(var a in Query("root\\cimv2","SELECT DeviceLocator,Capacity,Speed,Manufacturer FROM Win32_PhysicalMemory"))r.Facts.Add(new Fact("内存 · "+Text(a,"DeviceLocator"),GB(Number(a,"Capacity"))+" · "+Text(a,"Speed")+" MT/s · "+Text(a,"Manufacturer")));});
   Part(r,"硬盘",()=>{foreach(var a in Query("root\\cimv2","SELECT Model,Size,Status FROM Win32_DiskDrive"))r.Facts.Add(new Fact("硬盘",Text(a,"Model")+" · "+GB(Number(a,"Size"))+" · 系统状态 "+Text(a,"Status")));});
   Part(r,"磁盘空间",()=>{foreach(var a in Query("root\\cimv2","SELECT DeviceID,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3")){double total=Number(a,"Size"),free=Number(a,"FreeSpace");r.Facts.Add(new Fact("分区 "+Text(a,"DeviceID"),"可用 "+GB(free)+" / "+GB(total)));if(total>0&&free/total<0.1)r.Findings.Add(new Finding("注意","分区空间不足",Text(a,"DeviceID")+" 剩余空间少于 10%；这是空间提示，不代表硬盘损坏。"));}});
   progress("正在扫描 Windows 可识别的全部设备和驱动…");
   Part(r,"设备清单",()=>{r.Devices=Devices(true);foreach(var d in r.Devices.Where(x=>x.Present&&x.Code>0&&x.Code!=22))r.Findings.Add(new Finding("异常",d.Name,"Windows 设备错误代码 "+d.Code+"。请检查连接和对应驱动；错误代码不等于物理损坏。"));var disabled=r.Devices.Count(x=>x.Present&&x.Code==22);if(disabled>0)r.Findings.Add(new Finding("信息",disabled+" 个设备已禁用","可能是用户或系统主动禁用；本程序不会自动启用它们。"));});
   progress("正在读取音频、网卡与存储健康信息…");
   r.DevicesReadable=r.Devices.Count>0;
   Part(r,"音频输出",()=>{r.Endpoints=Audio.Enumerate();r.AudioReadable=true;});
   r.AudioService=ServiceState("Audiosrv");r.EndpointService=ServiceState("AudioEndpointBuilder");
   if(r.AudioService!="Running"||r.EndpointService!="Running")r.Findings.Add(new Finding("注意","音频服务未全部运行","Windows Audio："+r.AudioService+"；Endpoint Builder："+r.EndpointService));
   Part(r,"网络连接",()=>{r.Networks=Networks();});
   Part(r,"存储健康",()=>{var a=Query("root\\Microsoft\\Windows\\Storage","SELECT FriendlyName,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk");foreach(var d in a){string health=Text(d,"HealthStatus");string label=health=="0"?"未报告异常":health=="1"?"警告":health=="2"?"不健康":"未知";r.Facts.Add(new Fact("存储健康 · "+Text(d,"FriendlyName"),label));if(health=="1"||health=="2")r.Findings.Add(new Finding("异常","存储设备报告风险",Text(d,"FriendlyName")+"："+label+"。建议先备份重要文件。"));}if(a.Count==0)r.Limitations.Add("存储健康：驱动未提供数据。");});
   Part(r,"磁盘可靠性",()=>{var a=Query("root\\Microsoft\\Windows\\Storage","SELECT DeviceId,Temperature,Wear,PowerOnHours,ReadErrorsUncorrected,WriteErrorsUncorrected FROM MSFT_StorageReliabilityCounter");foreach(var d in a){string details="温度 "+Val(d,"Temperature"," °C")+" · 磨损 "+Val(d,"Wear"," %")+" · 通电 "+Val(d,"PowerOnHours"," 小时");r.Facts.Add(new Fact("磁盘计数 · "+Text(d,"DeviceId"),details));if(Number(d,"ReadErrorsUncorrected")>0||Number(d,"WriteErrorsUncorrected")>0)r.Findings.Add(new Finding("注意","磁盘记录了不可纠正错误","设备 "+Text(d,"DeviceId")+"：读取 "+Text(d,"ReadErrorsUncorrected")+"，写入 "+Text(d,"WriteErrorsUncorrected")+"；计数为历史累计，不一定代表正在恶化。"));}if(a.Count==0)r.Limitations.Add("磁盘可靠性：没有可用计数器。");});
   Part(r,"电池",()=>{var batteries=Query("root\\cimv2","SELECT Name,EstimatedChargeRemaining,BatteryStatus FROM Win32_Battery");if(batteries.Count==0)r.Facts.Add(new Fact("电池","未检测到系统电池"));else foreach(var b in batteries)r.Facts.Add(new Fact("电池",Text(b,"Name")+" · 电量 "+Text(b,"EstimatedChargeRemaining")+"%"));if(batteries.Count>0){var full=Query("root\\wmi","SELECT InstanceName,FullChargedCapacity FROM BatteryFullChargedCapacity");var design=Query("root\\wmi","SELECT InstanceName,DesignedCapacity FROM BatteryStaticData");foreach(var b in design){var f=full.FirstOrDefault(x=>Text(x,"InstanceName")==Text(b,"InstanceName"));double cap=Number(b,"DesignedCapacity");if(f!=null&&cap>0){double pct=Number(f,"FullChargedCapacity")/cap*100;r.Facts.Add(new Fact("电池容量估算",pct.ToString("0.0")+"% · 满充容量 / 设计容量"));}}}});
   progress("正在检查最近 7 天的硬件错误记录…");
   Part(r,"WHEA 硬件日志",()=>{var q=new EventLogQuery("System",PathType.LogName,"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= 604800000]]]"){ReverseDirection=true};int count=0;using(var reader=new EventLogReader(q)){EventRecord item;while(count<200&&(item=reader.ReadEvent())!=null){using(item){count++;if(count<=5)r.Findings.Add(new Finding("注意","硬件错误日志 · 事件 "+item.Id,(item.TimeCreated.HasValue?item.TimeCreated.Value.ToString("MM-dd HH:mm"):"")+" · "+EventDescription(item)));}}}r.Facts.Add(new Fact("近 7 天 WHEA 日志",count==0?"未发现记录":(count>=200?"至少 ":"")+count+" 条"));});
   r.Limitations.Add("设备状态来自 Windows 和硬件自报。未报告故障不等于硬件完全健康。");
   r.Limitations.Add("本次未进行内存压力测试、CPU/GPU 烤机或磁盘全盘读写测试；未检测电源电气质量及物理损伤。");
   progress("正在读取温度传感器…");r.Temperatures=TemperatureSensors.Read(r.Manufacturer);
   r.Limitations.Add("温度页面显示固件提供的快照；系统温区不能代替 CPU/GPU 核心温度。风扇转速尚未接入；未提供数据不代表正常。");
   progress("扫描完成");return r;
  }
  static string Val(Dictionary<string,object> d,string key,string suffix){var v=Text(d,key);return String.IsNullOrEmpty(v)?"未提供":v+suffix;}
  static string EventDescription(EventRecord e){try{string s=e.FormatDescription()??"请在事件查看器中查看详细信息。";return s.Length>500?s.Substring(0,500)+"…":s;}catch{return "事件已记录；描述不可用。请在事件查看器中查看。";}}
 }
}
