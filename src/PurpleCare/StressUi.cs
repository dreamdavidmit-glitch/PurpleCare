using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace PurpleCare {
 public partial class MainUi {
  bool stressRunning,stressStopRequested; string stressFolder;StressReport lastStress;
  readonly string sessionUser=Environment.UserName;
  readonly string sessionAccount=System.Security.Principal.WindowsIdentity.GetCurrent().Name;
  void InitializeStress(){
   C<ComboBox>("StressMode").ItemsSource=new[]{"CPU 运算校验","内存读写校验","CPU + 内存组合"};C<ComboBox>("StressMode").SelectedIndex=0;
   C<ComboBox>("StressDuration").ItemsSource=new[]{"30 秒","60 秒","120 秒","180 秒"};C<ComboBox>("StressDuration").SelectedIndex=1;
   C<ComboBox>("StressLevel").ItemsSource=new[]{"标准 · 半数线程间歇运行","较高 · 全部线程持续高负载"};C<ComboBox>("StressLevel").SelectedIndex=0;
   C<ComboBox>("StressMemory").ItemsSource=new[]{"128 MB","256 MB","512 MB","1024 MB"};C<ComboBox>("StressMemory").SelectedIndex=1;
   C<ComboBox>("StressMode").SelectionChanged+=(s,e)=>UpdateStressButtons();
   On("StressStartBtn",async()=>await StartStress());On("StressStopBtn",RequestStressStop);On("StressExportBtn",ExportStress);
   C<Canvas>("StressChart").SizeChanged+=(s,e)=>DrawStressChart();
   Text("UserName",sessionUser);
  }
  void RestoreStressResult(){
   try{
    string root=System.IO.Path.Combine(dataDir,"Stress");if(!Directory.Exists(root))return;
    var folder=Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();if(folder==null)return;
    var saved=StressFiles.Read(System.IO.Path.Combine(folder,"result.json"));
    if(saved==null){saved=StressFiles.Read(System.IO.Path.Combine(folder,"request.json"));if(saved!=null){saved.Status="incomplete";saved.Message="上次测试未留下完整结果，可能被关闭或中断，不能判断是否通过。";}}
    if(saved==null||saved.Options==null||saved.Options.Seconds<1||saved.Options.Seconds>180)return;
    ShowStress(saved);UpdateStressButtons();
   }catch{}
  }
  void UpdateStressButtons(){
   C<Button>("StressStartBtn").IsEnabled=!busy;
   C<Button>("StressStopBtn").IsEnabled=stressRunning&&!stressStopRequested;
   C<Button>("StressExportBtn").IsEnabled=!busy&&lastStress!=null&&lastStress.Status!="running";
   C<ComboBox>("StressMode").IsEnabled=!busy;C<ComboBox>("StressDuration").IsEnabled=!busy;
   C<ComboBox>("StressLevel").IsEnabled=!busy&&C<ComboBox>("StressMode").SelectedIndex!=1;
   C<ComboBox>("StressMemory").IsEnabled=!busy&&C<ComboBox>("StressMode").SelectedIndex!=0;
  }
  void RequestStressStop(){
   if(!stressRunning||stressStopRequested)return;
   stressStopRequested=true;try{File.WriteAllText(System.IO.Path.Combine(stressFolder,"cancel"),"stop");}catch{}
   Text("StressResultTitle","正在停止…");Text("StressResultDetail","正在释放测试内存并等待测试进程退出。");UpdateStressButtons();
  }
  async Task StartStress(){
   if(busy)return;
   int index=C<ComboBox>("StressMode").SelectedIndex;bool high=C<ComboBox>("StressLevel").SelectedIndex==1;
   var options=new StressOptions{Mode=new[]{"cpu","memory","combined"}[index],Seconds=new[]{30,60,120,180}[C<ComboBox>("StressDuration").SelectedIndex],Threads=Math.Min(64,high?Environment.ProcessorCount:Math.Max(1,Environment.ProcessorCount/2)),Duty=high?90:50,MemoryMB=new[]{128,256,512,1024}[C<ComboBox>("StressMemory").SelectedIndex]};
   if(!Confirm("开始压力测试？",C<ComboBox>("StressMode").SelectedItem+" · "+options.Seconds+" 秒\n\n测试会增加 CPU 负载、发热和内存使用，请先保存正在编辑的文件，并保持散热通畅。\n\n温度页仅提供读取时的快照，测试不能按温度自动停止。可随时点击停止；结束后不会重启设备。","开始测试"))return;
   stressStopRequested=false;stressRunning=true;
   lastStress=new StressReport{Started=DateTime.Now.ToString("o"),Computer=Environment.MachineName,UserName=sessionUser,Options=options,Status="running",Message="正在启动测试进程"};
   string id=Guid.NewGuid().ToString("N");stressFolder=StressFiles.Folder(id);SetBusy(true,"正在运行压力测试，可在压力测试页面停止");ShowStress(lastStress);Log("开始压力测试："+options.Mode+"，"+options.Seconds+" 秒。");
   try{
    Directory.CreateDirectory(stressFolder);StressFiles.Write(System.IO.Path.Combine(stressFolder,"request.json"),lastStress);
    var progress=new Progress<StressReport>(ShowStress);
    var result=await Task.Run(()=>RunStressProcess(id,options,progress));lastStress=result;ShowStress(result);
    StressFiles.Write(System.IO.Path.Combine(stressFolder,"result.json"),result);Log("压力测试："+result.Message+" 结果保存于 "+stressFolder);
   }catch(Exception e){lastStress.Status="error";lastStress.Message="测试未完成："+e.Message;ShowStress(lastStress);Log(lastStress.Message);}
   finally{stressRunning=false;SetBusy(false,"压力测试结束，请查看结果图标和曲线");}
  }
  StressReport RunStressProcess(string id,StressOptions o,IProgress<StressReport> progress){
   var initial=lastStress;string folder=StressFiles.Folder(id);StressReport latest=initial;
   string arguments=String.Join(" ",new[]{"--stress-worker",id,o.Mode,o.Seconds.ToString(),o.Threads.ToString(),o.Duty.ToString(),o.MemoryMB.ToString(),Process.GetCurrentProcess().Id.ToString()});
   var start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,arguments){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
   using(var child=Process.Start(start)){
    var deadline=Stopwatch.StartNew();Stopwatch stopping=null;bool forced=false;
    try{
     while(!child.WaitForExit(250)){
      var sample=StressFiles.Read(System.IO.Path.Combine(folder,"progress.json"));if(sample!=null){latest=sample;progress.Report(sample);}
      if(File.Exists(System.IO.Path.Combine(folder,"cancel"))&&stopping==null)stopping=Stopwatch.StartNew();
      if(deadline.Elapsed.TotalSeconds>o.Seconds+10||(stopping!=null&&stopping.Elapsed.TotalSeconds>5)){forced=true;child.Kill();child.WaitForExit(5000);break;}
     }
     var final=StressFiles.Read(System.IO.Path.Combine(folder,"result.json"));
     if(final!=null&&!forced)return final;
     latest.Status=stopping!=null?"cancelled":"error";latest.ElapsedSeconds=deadline.Elapsed.TotalSeconds;
     latest.Message=stopping!=null?"已停止测试进程，结果不构成完整测试结论。":forced?"测试进程超过时限，已终止；本次没有有效的完整结论。":"测试进程异常退出，未生成完整结果。";
     return latest;
    }finally{try{if(!child.HasExited){child.Kill();child.WaitForExit(5000);}}catch{}}
   }
  }
  void ShowStress(StressReport value){
   // Ignore queued progress from a previous state after a final result has been shown.
   if(lastStress!=null&&lastStress.Status!="running"&&value.Status=="running")return;
   lastStress=value;var sample=value.Samples.LastOrDefault();
   Text("StressCpuValue",sample==null||!sample.CpuPercent.HasValue?"—":sample.CpuPercent.Value.ToString("0")+"%");
   Text("StressMemoryValue",sample==null||!sample.MemoryPercent.HasValue?"—":sample.MemoryPercent.Value.ToString("0")+"%");
   Text("StressRemaining",value.Status=="running"?Math.Max(0,value.Options.Seconds-(int)value.ElapsedSeconds)+" 秒":value.ElapsedSeconds.ToString("0.0")+" 秒");
   Text("StressWork",value.CpuChecks+" 次 CPU 校验 · "+value.MemoryPasses+" 轮内存校验 · "+value.AllocatedMemoryMB+" MB 测试内存");
   C<ProgressBar>("StressProgress").Value=Math.Min(100,value.ElapsedSeconds/value.Options.Seconds*100);
   string icon="…",title="测试进行中",color="#FFD12E";
   if(value.Status=="completed"){icon="✓";title="完成 · 本次校验未发现错误";color="#61D9AF";}
   else if(value.Status=="cancelled"){icon="■";title="已停止 · 测试未完成";color="#B2C0D6";}
   else if(value.Status=="error"){icon="!";title="测试异常 · 请查看详情";color="#FF907A";}
   else if(value.Status=="incomplete"){icon="!";title="校验不足 · 暂无结论";color="#FFD12E";}
   Text("StressResultIcon",icon);C<TextBlock>("StressResultIcon").Foreground=(Brush)new BrushConverter().ConvertFromString(color);
   Text("StressResultTitle",stressStopRequested&&value.Status=="running"?"正在停止…":title);Text("StressResultDetail",value.Message);
   if(value.Status!="running"){
    var cpu=value.Samples.Where(s=>s.CpuPercent.HasValue).Select(s=>s.CpuPercent.Value).ToArray();var ram=value.Samples.Where(s=>s.MemoryPercent.HasValue).Select(s=>s.MemoryPercent.Value).ToArray();
    Text("StressSummary","系统 CPU 平均 "+(cpu.Length==0?"未知":cpu.Average().ToString("0.0")+"%")+" / 峰值 "+(cpu.Length==0?"未知":cpu.Max().ToString("0.0")+"%")+"；系统内存峰值 "+(ram.Length==0?"未知":ram.Max().ToString("0.0")+"%")+"。校验错误 / 测试异常计数："+value.Errors+"。");
   }else Text("StressSummary","曲线统计整台电脑的使用率，也包含其他程序的负载。");
   DrawStressChart();
   if(value.Status!="running"&&activePage=="Stress")C<ScrollViewer>("StressPage").ScrollToVerticalOffset(C<Border>("StressConfigCard").ActualHeight+14);
  }
  void DrawStressChart(){
   var canvas=C<Canvas>("StressChart");canvas.Children.Clear();double width=canvas.ActualWidth,height=canvas.ActualHeight;if(width<80||height<60)return;
   double left=35,top=12,w=width-47,h=height-38;var muted=new SolidColorBrush(Color.FromRgb(151,174,205));
   foreach(int pct in new[]{0,50,100}){double y=top+h*(1-pct/100.0);canvas.Children.Add(new Line{X1=left,X2=left+w,Y1=y,Y2=y,Stroke=new SolidColorBrush(Color.FromRgb(50,69,96)),StrokeThickness=1});var label=new TextBlock{Text=pct.ToString(),Foreground=muted,FontSize=10};Canvas.SetLeft(label,0);Canvas.SetTop(label,y-7);canvas.Children.Add(label);}
   var points=lastStress==null?new List<StressSample>():lastStress.Samples;double seconds=lastStress==null?60:Math.Max(lastStress.Options.Seconds,lastStress.ElapsedSeconds);
   for(int series=0;series<2;series++){var line=new Polyline{Stroke=series==0?new SolidColorBrush(Color.FromRgb(255,209,46)):new SolidColorBrush(Color.FromRgb(107,187,255)),StrokeThickness=2};foreach(var s in points){double? value=series==0?s.CpuPercent:s.MemoryPercent;if(value.HasValue)line.Points.Add(new Point(left+w*Math.Min(1,s.Seconds/seconds),top+h*(1-Math.Max(0,Math.Min(100,value.Value))/100)));}canvas.Children.Add(line);}
   var start=new TextBlock{Text="0 秒",Foreground=muted,FontSize=10};Canvas.SetLeft(start,left);Canvas.SetTop(start,top+h+8);canvas.Children.Add(start);var end=new TextBlock{Text=seconds.ToString("0")+" 秒",Foreground=muted,FontSize=10};Canvas.SetRight(end,0);Canvas.SetTop(end,top+h+8);canvas.Children.Add(end);
  }
  void ExportStress(){
   if(lastStress==null||stressRunning)return;
   var picker=new SaveFileDialog{Title="导出压力测试结果",Filter="HTML 图表报告|*.html|JSON 测试数据|*.json",FileName="紫芯压力测试-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".html"};
   if(picker.ShowDialog(Window)!=true)return;
   try{File.WriteAllText(picker.FileName,picker.FilterIndex==1?StressHtml.Create(lastStress):Operations.Json().Serialize(lastStress),new UTF8Encoding(false));Log("已导出压力测试报告："+picker.FileName);}catch(Exception e){MessageBox.Show(Window,e.Message,"导出失败");}
  }
 }
 public static class StressHtml {
  static string E(string text){return System.Net.WebUtility.HtmlEncode(text??"");}
  static string N(double v){return v.ToString("0.##",CultureInfo.InvariantCulture);}
  public static string Create(StressReport r){
   var svg=new StringBuilder("<svg viewBox='0 0 900 260' role='img' aria-label='CPU 和内存使用率曲线'>");
   for(int i=0;i<=100;i+=25){double y=220-2*i;svg.Append("<path d='M40 "+N(y)+" H880' stroke='#31415d'/><text x='0' y='"+N(y+4)+"' fill='#b4c5df' font-size='12'>"+i+"%</text>");}
   double seconds=Math.Max(r.Options.Seconds,r.ElapsedSeconds);
   foreach(bool cpu in new[]{true,false}){var points=new List<string>();foreach(var p in r.Samples){double? v=cpu?p.CpuPercent:p.MemoryPercent;if(v.HasValue)points.Add(N(40+840*Math.Min(1,p.Seconds/Math.Max(1,seconds)))+","+N(220-2*Math.Max(0,Math.Min(100,v.Value))));}svg.Append("<polyline fill='none' stroke='"+(cpu?"#ffd12e":"#6bbbff")+"' stroke-width='2' points='"+String.Join(" ",points)+"'/>");}
   svg.Append("<text x='40' y='250' fill='#b4c5df'>0 秒</text><text x='825' y='250' fill='#b4c5df'>"+N(seconds)+" 秒</text></svg>");
   string icon=r.Status=="completed"?"✓":r.Status=="cancelled"?"■":"!";
   return "<!doctype html><html lang='zh-CN'><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>紫芯压力测试报告</title><style>body{background:#091020;color:#eaf1ff;font:16px/1.7 system-ui;margin:40px auto;max-width:960px;padding:0 20px}.card{background:#142039;border:1px solid #304463;border-radius:14px;padding:24px;margin:18px 0}.icon{font-size:38px;color:"+(r.Status=="completed"?"#61d9af":"#ffd12e")+"}svg{width:100%;height:auto}.muted{color:#b2c3dc}</style><h1>紫芯 · 压力测试结果</h1><p>"+E(r.Computer)+" · 用户 "+E(r.UserName)+" · "+E(r.Started)+"</p><div class='card'><span class='icon'>"+icon+"</span><h2>"+E(r.Message)+"</h2><p>项目："+E(r.Options.Mode)+" · 目标时长 "+r.Options.Seconds+" 秒 · 实测 "+N(r.ElapsedSeconds)+" 秒</p><p>CPU 校验 "+r.CpuChecks+" 次 · 内存校验 "+r.MemoryPasses+" 轮 · 测试内存 "+r.AllocatedMemoryMB+" MB · 异常计数 "+r.Errors+"</p></div><div class='card'>"+svg+"<span style='color:#ffd12e'>━ CPU</span>　<span style='color:#6bbbff'>━ 内存</span><p class='muted'>系统使用率包含其他程序的负载。</p></div><p class='muted'>本结果仅适用于本次负载、时长及所分配的内存区域。未检测 GPU、磁盘、电源或全部内存；本测试未连续监测温度，不能按温度自动停止。短时校验未发现错误不代表整机完全健康。</p></html>";
  }
 }
}
