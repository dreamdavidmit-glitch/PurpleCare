using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Security.Principal;

namespace PurpleCare {
 public sealed class TemperatureReading {
  public string Name{get;set;} public double Celsius{get;set;} public string Source{get;set;} public string Time{get;set;}
  public string Display{get{return Celsius.ToString("0.0",CultureInfo.CurrentCulture)+" °C";}}
 }
 public sealed class TemperatureReport {
  public List<TemperatureReading> Readings=new List<TemperatureReading>();
  public List<string> Notes=new List<string>(); public string Time{get;set;}
 }
 public static class TemperatureSensors {
  // Firmware sentinels must remain unavailable, not become a reassuring 0 degrees.
  public static double? Normalize(object raw,bool deciKelvin){
   if(raw==null)return null;double v;
   if(!Double.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out v)||Double.IsNaN(v)||Double.IsInfinity(v)||v<=0)return null;
   if(deciKelvin)v=v/10.0-273.15;
   return v>0&&v<=150?(double?)Math.Round(v,1):null;
  }
  static string Failure(Exception e){
   var m=e as ManagementException;
   if(e is UnauthorizedAccessException||(m!=null&&m.ErrorCode==ManagementStatus.AccessDenied))return "需要管理员权限，可点击管理员读取温度";
   if(m!=null&&(m.ErrorCode==ManagementStatus.InvalidClass||m.ErrorCode==ManagementStatus.InvalidNamespace||m.ErrorCode==ManagementStatus.NotSupported))return "此电脑未提供该接口";
   return "接口未返回可用数据（"+e.Message.Trim()+"）";
  }
  static void Add(TemperatureReport r,string name,object raw,bool kelvin,string source){
   var value=Normalize(raw,kelvin);
   if(value.HasValue)r.Readings.Add(new TemperatureReading{Name=name,Celsius=value.Value,Source=source,Time=DateTime.Now.ToString("HH:mm:ss")});
   else r.Notes.Add(name+"：未提供有效温度");
  }
  public static TemperatureReport Read(string manufacturer){
   var r=new TemperatureReport{Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")};
   GpuTemperature.Append(r);
   var options=new EnumerationOptions{ReturnImmediately=true,Rewindable=false,Timeout=TimeSpan.FromSeconds(4)};
   if((manufacturer??"").IndexOf("LENOVO",StringComparison.OrdinalIgnoreCase)>=0){
    try{
     using(var query=new ManagementObjectSearcher(new ManagementScope(@"root\wmi"),new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"),options))
     using(var instances=query.Get()){
      bool found=false;
      foreach(ManagementObject item in instances)using(item){
       found=true;
       foreach(var part in new[]{"CPU","GPU"})try{
        using(var output=item.InvokeMethod("Get"+part+"Temp",(ManagementBaseObject)null,new InvokeMethodOptions{Timeout=TimeSpan.FromSeconds(4)}))
         Add(r,part+" · 联想传感器",output==null?null:output["Data"],false,"联想固件 / Get"+part+"Temp");
       }catch(Exception e){r.Notes.Add("联想 "+part+"："+Failure(e));}
      }
      if(!found)r.Notes.Add("联想 CPU/GPU：未提供传感器实例");
     }
    }catch(Exception e){r.Notes.Add("联想 CPU/GPU："+Failure(e));}
   }
   try{
    using(var query=new ManagementObjectSearcher(new ManagementScope(@"root\wmi"),new ObjectQuery("SELECT InstanceName,CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"),options))
    using(var instances=query.Get()){
     bool found=false;
     foreach(ManagementObject item in instances)using(item){found=true;Add(r,"系统温区 · "+Convert.ToString(item["InstanceName"]),item["CurrentTemperature"],true,"Windows ACPI · 未确定对应部件");}
     if(!found)r.Notes.Add("系统温区：固件未提供数据");
    }
   }catch(Exception e){r.Notes.Add("系统温区："+Failure(e));}
   r.Notes.Add("顶部重新扫描可刷新 GPU 温度，无需管理员。系统温区与部分厂商接口需要管理员补充读取。\n这是读取时的温度快照。系统温区不等同于 CPU 核心或 GPU 温度；固件可能延迟更新。压力测试未连续监测温度，不能按温度自动停止。");
   return r;
  }
 }
}
