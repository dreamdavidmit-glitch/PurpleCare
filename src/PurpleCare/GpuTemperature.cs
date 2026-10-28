using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PurpleCare {
 public static class GpuTemperature {
  // Layouts and query IDs from the Windows SDK d3dkmthk.h (x64).
  [StructLayout(LayoutKind.Sequential)] struct AdapterList {public uint Count;public IntPtr Adapters;}
  [StructLayout(LayoutKind.Sequential)] struct Query {public uint Handle;public int Type;public IntPtr Data;public uint Size;}
  [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref AdapterList list);
  [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref Query query);
  [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref uint handle);
  public static double? Celsius(uint raw){return TemperatureSensors.Normalize(raw/10.0,false);}
  public static bool Physical(uint flags){return (flags&1)!=0&&(flags&196)==0;}
  static bool Read(uint handle,int kind,IntPtr buffer,int bytes){var query=new Query{Handle=handle,Type=kind,Data=buffer,Size=(uint)bytes};return D3DKMTQueryAdapterInfo(ref query)>=0;}
  public static void Append(TemperatureReport report){
   const int count=128,stride=20;IntPtr adapters=Marshal.AllocHGlobal(count*stride);int before=report.Readings.Count;
   Marshal.Copy(new byte[count*stride],0,adapters,count*stride);
   try{
    var list=new AdapterList{Count=count,Adapters=adapters};
    if(D3DKMTEnumAdapters2(ref list)<0||list.Count>count){report.Notes.Add("GPU：Windows 显卡接口枚举失败");return;}
    var seen=new HashSet<string>();
    for(int i=0;i<list.Count;i++){
     uint handle=unchecked((uint)Marshal.ReadInt32(adapters,i*stride));if(handle==0)continue;
     IntPtr data=Marshal.AllocHGlobal(2080);
     try{
      Marshal.WriteInt32(data,0);if(!Read(handle,15,data,4)||!Physical(unchecked((uint)Marshal.ReadInt32(data))))continue;
      string key=Marshal.ReadInt32(adapters,i*stride+4)+":"+Marshal.ReadInt32(adapters,i*stride+8);if(!seen.Add(key))continue;
      Marshal.Copy(new byte[2080],0,data,2080);string name="显卡 "+(i+1);
      if(Read(handle,8,data,2080)){string value=Marshal.PtrToStringUni(data,260).TrimEnd('\0');if(!String.IsNullOrWhiteSpace(value))name=value;}
      Marshal.WriteInt32(data,0);int physical=Read(handle,30,data,4)?Marshal.ReadInt32(data):1;if(physical<1||physical>16)physical=1;
      for(int j=0;j<physical;j++){
       Marshal.Copy(new byte[64],0,data,64);Marshal.WriteInt32(data,j);
       if(!Read(handle,62,data,64))continue;
       var celsius=Celsius(unchecked((uint)Marshal.ReadInt32(data,56)));
       if(celsius.HasValue)report.Readings.Add(new TemperatureReading{Name="GPU · "+name+(physical>1?" · "+j:""),Celsius=celsius.Value,Source="Windows 显卡驱动 / WDDM 主温度传感器",Time=DateTime.Now.ToString("HH:mm:ss")});
      }
     }finally{Marshal.FreeHGlobal(data);}
    }
    if(report.Readings.Count==before)report.Notes.Add("GPU：当前驱动未提供有效温度（休眠、虚拟显卡或驱动不支持均可能导致缺失）");
   }catch(Exception e){report.Notes.Add("GPU 温度接口不可用："+e.Message);}
   finally{
    // EnumAdapters2 opens handles, including adapters that report no sensors.
    for(int i=0;i<count;i++){uint handle=unchecked((uint)Marshal.ReadInt32(adapters,i*stride));if(handle!=0)D3DKMTCloseAdapter(ref handle);}
    Marshal.FreeHGlobal(adapters);
   }
  }
 }
}
