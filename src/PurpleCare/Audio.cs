using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace PurpleCare {
 public class EndpointRow {
  public string Id{get;set;} public string Name{get;set;} public uint StateCode{get;set;}
  public bool Default{get;set;} public string Volume{get;set;} public string Muted{get;set;}
  public string State {get{return StateCode==1?"可用":StateCode==2?"已禁用":StateCode==4?"设备不存在":StateCode==8?"未插入":"未知";}}
  public override string ToString(){return (Default?"默认 · ":"")+Name+"  ["+State+"]";}
 }
 public static class Audio {
  static void Check(int hr){Marshal.ThrowExceptionForHR(hr);}
  static void Release(object o){if(o!=null&&Marshal.IsComObject(o))Marshal.ReleaseComObject(o);}
  public static List<EndpointRow> Enumerate(){
   var result=new List<EndpointRow>();IMMDeviceEnumerator en=null;IMMDeviceCollection col=null;IMMDevice def=null;
   try{
    en=(IMMDeviceEnumerator)new MMDeviceEnumerator();string defaultId="";
    if(en.GetDefaultAudioEndpoint(0,0,out def)==0&&def!=null)Check(def.GetId(out defaultId));
    Check(en.EnumAudioEndpoints(0,15,out col));uint count;Check(col.GetCount(out count));
    for(uint i=0;i<count;i++){IMMDevice dev=null;IPropertyStore props=null;object vol=null;
     try{Check(col.Item(i,out dev));string id;uint state;Check(dev.GetId(out id));Check(dev.GetState(out state));
      Check(dev.OpenPropertyStore(0,out props));var key=new PropertyKey{fmtid=new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),pid=14};PropVariant value;
      Check(props.GetValue(ref key,out value));string name="音频输出";try{if(value.vt==31)name=Marshal.PtrToStringUni(value.ptr);}finally{PropVariantClear(ref value);}
      var row=new EndpointRow{Id=id,Name=name,Default=id==defaultId,StateCode=state,Volume="未提供",Muted="未知"};
      if(state==1){try{Guid iid=typeof(IAudioEndpointVolume).GUID;Check(dev.Activate(ref iid,23,IntPtr.Zero,out vol));var v=(IAudioEndpointVolume)vol;float level;bool mute;Check(v.GetMasterVolumeLevelScalar(out level));Check(v.GetMute(out mute));row.Volume=(level*100).ToString("0")+"%";row.Muted=mute?"是":"否";}catch{}}
      result.Add(row);
     }finally{Release(vol);Release(props);Release(dev);}
    }
   }finally{Release(def);Release(col);Release(en);}return result;
  }
  // Explicit output endpoint. No changes to volume, mute, default device or recording settings.
  public static void PlayTone(string endpointId,bool probeOnly=false){
   IMMDeviceEnumerator en=null;IMMDevice dev=null;object clientObj=null,renderObj=null;IntPtr format=IntPtr.Zero;IAudioClient client=null;bool started=false;
   try{
    en=(IMMDeviceEnumerator)new MMDeviceEnumerator();Check(en.GetDevice(endpointId,out dev));Guid cid=typeof(IAudioClient).GUID;
    Check(dev.Activate(ref cid,23,IntPtr.Zero,out clientObj));client=(IAudioClient)clientObj;Check(client.GetMixFormat(out format));
    int tag=(ushort)Marshal.ReadInt16(format,0),channels=(ushort)Marshal.ReadInt16(format,2),rate=Marshal.ReadInt32(format,4),align=(ushort)Marshal.ReadInt16(format,12),bits=(ushort)Marshal.ReadInt16(format,14);
    bool floating=tag==3;if(tag==65534){Guid subtype=(Guid)Marshal.PtrToStructure(IntPtr.Add(format,24),typeof(Guid));floating=subtype==new Guid("00000003-0000-0010-8000-00aa00389b71");if(!floating&&subtype!=new Guid("00000001-0000-0010-8000-00aa00389b71"))throw new Exception("此输出格式暂不支持测试音。");}
    else if(tag!=1&&tag!=3)throw new Exception("此输出格式暂不支持测试音。");
    if(channels<1||channels>32||rate<8000||rate>384000||align!=channels*(bits/8)||(floating?bits!=32:bits!=16&&bits!=24&&bits!=32))throw new Exception("不支持的音频混合格式。");
    Guid session=Guid.Empty;Check(client.Initialize(0,0,1000000,0,format,ref session));uint capacity;Check(client.GetBufferSize(out capacity));
    Guid rid=typeof(IAudioRenderClient).GUID;Check(client.GetService(ref rid,out renderObj));var render=(IAudioRenderClient)renderObj;
    if(probeOnly)return;
    long position=0,total=(long)(rate*1.2);Check(client.Start());started=true;
    while(position<total){uint padding;Check(client.GetCurrentPadding(out padding));uint frames=(uint)Math.Min((long)capacity-padding,total-position);if(frames==0){Thread.Sleep(8);continue;}
     IntPtr buffer;Check(render.GetBuffer(frames,out buffer));var data=new byte[checked((int)frames*align)];
     for(int f=0;f<frames;f++){double t=(position+f)/(double)rate;double fade=Math.Min(1,Math.Min(t/0.04,(1.2-t)/0.06));double sample=0.12*Math.Max(0,fade)*Math.Sin(2*Math.PI*(t<0.6?440:660)*t);
      for(int c=0;c<channels;c++){int offset=f*align+c*(bits/8);if(floating){var bytes=BitConverter.GetBytes((float)sample);Buffer.BlockCopy(bytes,0,data,offset,4);}else{long value=(long)(sample*(bits==16?32767.0:bits==24?8388607.0:2147483647.0));for(int b=0;b<bits/8;b++)data[offset+b]=(byte)((value>>(8*b))&255);}}
     }
     Marshal.Copy(data,0,buffer,data.Length);Check(render.ReleaseBuffer(frames,0));position+=frames;Thread.Sleep(8);
    }
    Thread.Sleep(180);
   }finally{if(started&&client!=null)client.Stop();if(format!=IntPtr.Zero)Marshal.FreeCoTaskMem(format);Release(renderObj);Release(clientObj);Release(dev);Release(en);}
  }
  [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropVariant p);
 }
 [StructLayout(LayoutKind.Sequential)]struct PropertyKey{public Guid fmtid;public uint pid;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]public ushort vt;[FieldOffset(8)]public IntPtr ptr;}
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class MMDeviceEnumerator{}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceEnumerator{
  [PreserveSig]int EnumAudioEndpoints(int flow,uint states,out IMMDeviceCollection devices);
  [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
  [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IMMDevice device);
  [PreserveSig]int RegisterEndpointNotificationCallback(IntPtr client);[PreserveSig]int UnregisterEndpointNotificationCallback(IntPtr client);
 }
 [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceCollection{
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out IMMDevice device);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDevice{
  [PreserveSig]int Activate(ref Guid iid,uint ctx,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)]out object obj);
  [PreserveSig]int OpenPropertyStore(uint access,out IPropertyStore props);
  [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetState(out uint state);
 }
 [ComImport,Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore{
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);
  [PreserveSig]int SetValue(ref PropertyKey key,ref PropVariant value);[PreserveSig]int Commit();
 }
 [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IAudioEndpointVolume{
  [PreserveSig]int RegisterControlChangeNotify(IntPtr notify);[PreserveSig]int UnregisterControlChangeNotify(IntPtr notify);
  [PreserveSig]int GetChannelCount(out uint count);[PreserveSig]int SetMasterVolumeLevel(float level,ref Guid context);
  [PreserveSig]int SetMasterVolumeLevelScalar(float level,ref Guid context);[PreserveSig]int GetMasterVolumeLevel(out float level);
  [PreserveSig]int GetMasterVolumeLevelScalar(out float level);[PreserveSig]int SetChannelVolumeLevel(uint channel,float level,ref Guid context);
  [PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float level,ref Guid context);[PreserveSig]int GetChannelVolumeLevel(uint channel,out float level);
  [PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float level);[PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);
  [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
 }
 [ComImport,Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IAudioClient{
  [PreserveSig]int Initialize(int mode,uint flags,long duration,long periodicity,IntPtr format,ref Guid session);
  [PreserveSig]int GetBufferSize(out uint frames);[PreserveSig]int GetStreamLatency(out long latency);[PreserveSig]int GetCurrentPadding(out uint padding);
  [PreserveSig]int IsFormatSupported(int mode,IntPtr format,out IntPtr closest);[PreserveSig]int GetMixFormat(out IntPtr format);
  [PreserveSig]int GetDevicePeriod(out long normal,out long minimum);[PreserveSig]int Start();[PreserveSig]int Stop();[PreserveSig]int Reset();[PreserveSig]int SetEventHandle(IntPtr handle);
  [PreserveSig]int GetService(ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)]out object obj);
 }
 [ComImport,Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IAudioRenderClient{
  [PreserveSig]int GetBuffer(uint frames,out IntPtr data);[PreserveSig]int ReleaseBuffer(uint frames,uint flags);
 }
}
