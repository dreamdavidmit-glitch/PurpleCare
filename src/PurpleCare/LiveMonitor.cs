using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace PurpleCare {
 public sealed class LiveSample {public DateTime Time{get;set;} public double? Cpu{get;set;} public double? Memory{get;set;} public double? Gpu{get;set;} public string Note{get;set;}}
 public sealed class LiveSampler {
  long idle,total;bool previous;
  public LiveSample Read(){
   var s=new LiveSample{Time=DateTime.Now};var notes=new List<string>();long i,t;
   if(StressMetrics.Cpu(out i,out t)){if(previous)s.Cpu=StressMetrics.CpuDelta(idle,total,i,t);idle=i;total=t;previous=true;}else{previous=false;notes.Add("CPU 占用未读取到");}
   try{s.Memory=StressMetrics.Memory().Load;}catch{notes.Add("内存占用未读取到");}
   var gpu=new TemperatureReport();GpuTemperature.Append(gpu);if(gpu.Readings.Count>0)s.Gpu=gpu.Readings.Max(x=>x.Celsius);else notes.Add("GPU 温度未提供");
   s.Note=String.Join("；",notes);return s;
  }
 }
 public static class LiveHistory {
  public static void Add(List<LiveSample> values,LiveSample sample){values.Add(sample);values.RemoveAll(x=>x.Time<sample.Time.AddMinutes(-5));while(values.Count>150)values.RemoveAt(0);}
 }
 public partial class MainUi {
  readonly List<LiveSample> liveValues=new List<LiveSample>();DispatcherTimer liveTimer;LiveSampler liveSampler;bool liveReading;int liveGeneration;
  void InitializeLive(){
   liveTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};liveTimer.Tick+=async(s,e)=>await ReadLive();
   On("LiveStartBtn",async()=>{liveGeneration++;liveSampler=new LiveSampler();liveValues.Clear();DrawLive();liveTimer.Start();C<Button>("LiveStartBtn").IsEnabled=false;C<Button>("LiveStopBtn").IsEnabled=true;Text("LiveStatus","正在读取… CPU 占用需两个采样点。");await ReadLive();});
   On("LiveStopBtn",StopLive);Window.Closed+=(s,e)=>StopLive();C<Canvas>("LiveUsageChart").SizeChanged+=(s,e)=>DrawLive();C<Canvas>("LiveTemperatureChart").SizeChanged+=(s,e)=>DrawLive();
  }
  void StopLive(){liveGeneration++;liveTimer.Stop();C<Button>("LiveStartBtn").IsEnabled=true;C<Button>("LiveStopBtn").IsEnabled=false;Text("LiveStatus","监控已停止，显示的是停止前的历史数据。");}
  async Task ReadLive(){
   if(liveReading||!liveTimer.IsEnabled)return;liveReading=true;int generation=liveGeneration;var sampler=liveSampler;
   try{var sample=await Task.Run(()=>sampler.Read());if(generation!=liveGeneration||!liveTimer.IsEnabled)return;LiveHistory.Add(liveValues,sample);
    Text("LiveCpu",sample.Cpu.HasValue?sample.Cpu.Value.ToString("0.0")+"%":"等待下一次采样");Text("LiveMemory",sample.Memory.HasValue?sample.Memory.Value.ToString("0.0")+"%":"未读取到");Text("LiveGpu",sample.Gpu.HasValue?sample.Gpu.Value.ToString("0.0")+" °C":"未提供");
    Text("LiveStatus","更新于 "+sample.Time.ToString("HH:mm:ss")+" · 最近 5 分钟 / "+liveValues.Count+" 个样本"+(String.IsNullOrEmpty(sample.Note)?"":" · "+sample.Note));DrawLive();
   }catch(Exception e){if(generation==liveGeneration){Text("LiveCpu","未读取到");Text("LiveMemory","未读取到");Text("LiveGpu","未读取到");LiveHistory.Add(liveValues,new LiveSample{Time=DateTime.Now});DrawLive();Text("LiveStatus","本次读取失败："+e.Message);}}
   finally{liveReading=false;}
  }
  void DrawLive(){DrawLiveChart(C<Canvas>("LiveUsageChart"),false);DrawLiveChart(C<Canvas>("LiveTemperatureChart"),true);}
  void DrawLiveChart(Canvas canvas,bool temperature){
   canvas.Children.Clear();double w=canvas.ActualWidth-55,h=canvas.ActualHeight-35;if(w<=0||h<=0)return;double max=temperature?150:100;
   foreach(double tick in new[]{0,max/2,max}){double y=10+h*(1-tick/max);canvas.Children.Add(new Line{X1=40,X2=40+w,Y1=y,Y2=y,Stroke=new SolidColorBrush(Color.FromRgb(50,69,96))});var label=new TextBlock{Text=tick+(temperature?"°":"%"),Foreground=Brushes.LightGray,FontSize=10};Canvas.SetTop(label,y-7);canvas.Children.Add(label);}
   DateTime end=liveValues.Count>0?liveValues.Last().Time:DateTime.Now,start=liveValues.Count>1?liveValues.First().Time:end.AddSeconds(-2);double duration=Math.Max(2,(end-start).TotalSeconds);
   for(int series=0;series<(temperature?1:2);series++){
    var color=temperature?Brushes.Orange:series==0?Brushes.Gold:Brushes.LightSkyBlue;Polyline line=null;
    foreach(var sample in liveValues){double? v=temperature?sample.Gpu:series==0?sample.Cpu:sample.Memory;if(!v.HasValue){line=null;continue;}if(line==null){line=new Polyline{Stroke=color,StrokeThickness=2};canvas.Children.Add(line);}line.Points.Add(new Point(40+w*(sample.Time-start).TotalSeconds/duration,10+h*(1-Math.Max(0,Math.Min(max,v.Value))/max)));}
   }
   var text=new TextBlock{Text=liveValues.Count>1?liveValues.First().Time.ToString("HH:mm:ss")+" → "+end.ToString("HH:mm:ss"):"等待采样",FontSize=10,Foreground=Brushes.LightGray};Canvas.SetLeft(text,40);Canvas.SetTop(text,h+15);canvas.Children.Add(text);
  }
 }
}
