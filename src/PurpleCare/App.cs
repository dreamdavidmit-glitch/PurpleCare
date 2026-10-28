using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PurpleCare {
 public static class Program {
  [STAThread] public static int Main(string[] args){
   try{
    if(args.Length>0&&args[0]=="--stress-worker")return StressFiles.Worker(args);
    if(args.Length>0&&args[0]=="--worker")return Operations.Worker(args);
    if(args.Length==2&&args[0]=="--self-test")return SelfTests.Run(args[1]);
    if(args.Length==2&&args[0]=="--scan-report"){var report=Scanner.Scan(s=>{});File.WriteAllText(args[1],Operations.Json().Serialize(report),new UTF8Encoding(false));return report.Devices.Count>0?0:1;}
    if(args.Length==2&&args[0]=="--audio-probe"){var eps=Audio.Enumerate().Where(x=>x.StateCode==1).ToList();var lines=new List<string>();foreach(var ep in eps){Audio.PlayTone(ep.Id,true);lines.Add("PASS: WASAPI shared stream initialized for "+ep.Name+"; no playback started.");}if(eps.Count==0)lines.Add("No active output; audio stream could not be verified.");File.WriteAllLines(args[1],lines,new UTF8Encoding(false));return eps.Count>0?0:1;}
    RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
    var app=new Application();app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show("发生错误："+e.Exception.Message,"紫芯 · 电脑助手",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
    var ui=new MainUi();app.Run(ui.Window);return 0;
   }catch(Exception e){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),e.ToString());}catch{}MessageBox.Show(e.ToString(),"紫芯启动失败");return 1;}
  }
 }
 public partial class MainUi {
  public Window Window{get;private set;} ScanReport report; bool busy;string activePage="Home";readonly List<string> history=new List<string>();
  readonly string dataDir=PortablePaths.Root;
  T C<T>(string n) where T:FrameworkElement{return (T)Window.FindName(n);}
  void Text(string name,string value){C<TextBlock>(name).Text=value;}
  void On(string n,Action action){C<Button>(n).Click+=(s,e)=>action();}
  public MainUi(){
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
   ApplyPhotoTheme();
   InitializeStress();InitializeBackup();InitializeDiagnostics();InitializeLive();InitializeGuideHistory();
   Window.Icon=MakeIcon();
   Window.SourceInitialized+=(s,e)=>{try{var handle=new WindowInteropHelper(Window).Handle;int color=0x001A0C06;DwmSetWindowAttribute(handle,35,ref color,4);int textColor=0x00FFFFFF;DwmSetWindowAttribute(handle,36,ref textColor,4);}catch{}};
   double availableWidth=Math.Max(1,SystemParameters.WorkArea.Width-20),availableHeight=Math.Max(1,SystemParameters.WorkArea.Height-20);
   Window.MinWidth=Math.Min(760,availableWidth);Window.MinHeight=Math.Min(440,availableHeight);Window.Width=Math.Min(1220,availableWidth);Window.Height=Math.Min(860,availableHeight);
   Window.SizeChanged+=(s,e)=>UpdateLayoutMode();UpdateLayoutMode();
   foreach(var name in new[]{"Home","Stress","Audio","Network","Devices","Backup","Guide","History","Logs"}){string key=name;C<RadioButton>("Nav"+name).Checked+=(s,e)=>Navigate(key);}
   On("ScanBtn",async()=>await Scan());On("ExportBtn",Export);On("HomeAudioBtn",()=>C<RadioButton>("NavAudio").IsChecked=true);
   On("TemperatureScanBtn",()=>C<Button>("AdminScanBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
   On("AdminScanBtn",async()=>{if(busy)return;SetBusy(true,"正在请求管理员权限，仅补充读取硬件数据…");try{var result=await Operations.Elevated("scan","");if(result.Report!=null){report=result.Report;Bind();}Log(result.Message);}catch(Exception e){Log("补充扫描未完成："+e.Message);}finally{SetBusy(false,"补充扫描结束，请查看检测范围与可用数据");}});
   On("ToneBtn",async()=>await TestTone());On("RefreshAudioBtn",async()=>await RefreshAudio());
   On("RepairAudioBtn",async()=>{if(busy)return;if(Confirm("恢复音频服务","将重新启动 Windows Audio 和 Audio Endpoint Builder。\n\n正在进行的播放、录音或通话会中断。请先结束录音，完成后重新测试声音。","确认修复"))await RunOperation("audio-services","","恢复音频服务");});
   On("RestartAudioBtn",async()=>await RestartSelected(C<ComboBox>("AudioDeviceCombo").SelectedItem as DeviceRow));
   On("RestartNetworkBtn",async()=>await RestartSelected(C<ComboBox>("NetworkDeviceCombo").SelectedItem as DeviceRow));
   On("RestartDeviceBtn",async()=>await RestartSelected(C<DataGrid>("DeviceGrid").SelectedItem as DeviceRow));
   On("RescanDevicesBtn",async()=>{if(busy)return;if(Confirm("重新检测硬件连接","请求 Windows 重新枚举连接的设备。新插入的设备可能被识别并加载已安装的驱动。","开始检测"))await RunOperation("rescan","","重新检测硬件连接");});
   On("RefreshNetworkBtn",async()=>{if(busy)return;SetBusy(true,"正在读取网络连接…");try{report.Networks=await Task.Run(()=>Scanner.Networks());C<ItemsControl>("NetworksList").ItemsSource=report.Networks;Log("已刷新本地网络连接状态。未进行互联网连通性测试。");}catch(Exception e){Log(e.Message);}finally{SetBusy(false,"网络状态已刷新");}});
   On("SoundSettingsBtn",()=>Open("ms-settings:sound"));On("NetworkSettingsBtn",()=>Open("ms-settings:network-status"));On("DeviceManagerBtn",()=>Open(Path.Combine(Environment.SystemDirectory,"devmgmt.msc")));
   On("PowerBtn",()=>{if(busy)return;if(Confirm("打开 Windows 电源对话框","CPU、内存、系统盘和存储控制器不能在这里单独重启。\n\n接下来会打开 Windows 电源对话框。请先保存工作，再自行选择“重新启动”。","打开电源选项")){Type t=Type.GetTypeFromProgID("Shell.Application");object shell=Activator.CreateInstance(t);try{t.InvokeMember("ShutdownWindows",BindingFlags.InvokeMethod,null,shell,new object[0]);}finally{System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);}}});
   C<TextBox>("SearchBox").TextChanged+=(s,e)=>Filter();C<ComboBox>("CategoryCombo").SelectionChanged+=(s,e)=>Filter();C<CheckBox>("ProblemsOnly").Checked+=(s,e)=>Filter();C<CheckBox>("ProblemsOnly").Unchecked+=(s,e)=>Filter();
   C<DataGrid>("DeviceGrid").SelectionChanged+=(s,e)=>DeviceDetails();
   C<ComboBox>("OutputCombo").SelectionChanged+=(s,e)=>OutputDetails();
   C<ComboBox>("AudioDeviceCombo").SelectionChanged+=(s,e)=>AdapterDetails("Audio");C<ComboBox>("NetworkDeviceCombo").SelectionChanged+=(s,e)=>AdapterDetails("Network");
   Window.Closing+=(s,e)=>{if(networkCancel!=null){e.Cancel=true;networkCancel.Cancel();return;}if(backupRunning){e.Cancel=true;backupCancel.Cancel();return;}if(stressRunning){e.Cancel=true;RequestStressStop();return;}if(busy){e.Cancel=true;MessageBox.Show(Window,"正在读取数据或执行设备操作，请等待结果后再关闭。","操作进行中");}};
   Window.Loaded+=async(s,e)=>{try{string logPath=Path.Combine(dataDir,"operations.log");if(File.Exists(logPath)){var tail=new Queue<string>();foreach(string line in File.ReadLines(logPath)){tail.Enqueue(line);if(tail.Count>400)tail.Dequeue();}history.Add("—— 最近的历史记录 ——\r\n"+String.Join("\r\n",tail)+"\r\n—— 当前会话 ——");}}catch{}Log("紫芯电脑助手已启动。扫描不会重启设备或修改系统设置。");await Scan();RestoreStressResult();};
   UpdateButtons();
  }
  void ApplyPhotoTheme(){
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("verstappen-background.jpg")){
    if(stream==null)throw new InvalidOperationException("Background resource missing.");
    var photo=new BitmapImage();photo.BeginInit();photo.CacheOption=BitmapCacheOption.OnLoad;photo.DecodePixelWidth=2400;photo.StreamSource=stream;photo.EndInit();photo.Freeze();
    C<Image>("ThemeBackground").Source=photo;((ImageBrush)Window.FindResource("HeroPhoto")).ImageSource=photo;
   }
  }
  ImageSource MakeIcon(){var group=new DrawingGroup();using(var dc=group.Open()){dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(241,47,69)),null,new Rect(0,0,64,64),15,15);var pen=new Pen(Brushes.White,3);dc.DrawRoundedRectangle(null,pen,new Rect(18,18,28,28),4,4);for(int i=23;i<=41;i+=9){dc.DrawLine(pen,new Point(i,10),new Point(i,18));dc.DrawLine(pen,new Point(i,46),new Point(i,54));dc.DrawLine(pen,new Point(10,i),new Point(18,i));dc.DrawLine(pen,new Point(46,i),new Point(54,i));}dc.DrawRectangle(Brushes.White,null,new Rect(26,26,12,12));}return new DrawingImage(group);}
  void Navigate(string page){activePage=page;foreach(var name in new[]{"Home","Stress","Audio","Network","Devices","Backup","Guide","History","Logs"})C<FrameworkElement>(name+"Page").Visibility=name==page?Visibility.Visible:Visibility.Collapsed;
   var titles=new Dictionary<string,string>{{"Home","概览与体检"},{"Stress","压力测试"},{"Audio","声音修复"},{"Network","网络修复"},{"Devices","全部硬件"},{"Backup","Edge 资料备份"},{"Guide","维修引导"},{"History","历史报告中心"},{"Logs","操作记录"}};
   var subs=new Dictionary<string,string>{{"Home","了解这台电脑，从真实状态开始。"},{"Stress","观察负载变化，验证本次运算和内存读写。"},{"Audio","找回扬声器与耳机的声音。"},{"Network","检查连接，重新初始化指定网卡。"},{"Devices","系统可识别的设备、驱动与运行状态。"},{"Backup","确认范围与保存位置，再备份自己的浏览器资料。"},{"Guide","按步骤检查，记录实际观察。"},{"History","查看过去的报告，不改变当前设备状态。"},{"Logs","每一次检查和修复，都有迹可循。"}};
   Text("PageTitle",titles[page]);Text("PageSubtitle",subs[page]);
  }
  void SetBusy(bool value,string status){busy=value;Text("StatusText",status);C<ProgressBar>("BusyBar").Visibility=value?Visibility.Visible:Visibility.Collapsed;UpdateButtons();}
  void UpdateButtons(){
   UpdateStressButtons();UpdateBackupButtons();UpdateGuideHistoryButtons();
   foreach(var n in new[]{"ScanBtn","AdminScanBtn","TemperatureScanBtn","RepairAudioBtn","RefreshAudioBtn","RefreshNetworkBtn","RescanDevicesBtn","PowerBtn","NetworkDiagnoseBtn"})C<Button>(n).IsEnabled=!busy;
   C<Button>("RefreshNetworkBtn").IsEnabled=!busy&&report!=null;C<Button>("ExportBtn").IsEnabled=!busy&&report!=null;
   var ep=C<ComboBox>("OutputCombo").SelectedItem as EndpointRow;C<Button>("ToneBtn").IsEnabled=!busy&&ep!=null&&ep.StateCode==1;
   C<Button>("RestartAudioBtn").IsEnabled=!busy&&DevicePolicy.AllowedRestart(C<ComboBox>("AudioDeviceCombo").SelectedItem as DeviceRow);
   C<Button>("RestartNetworkBtn").IsEnabled=!busy&&DevicePolicy.AllowedRestart(C<ComboBox>("NetworkDeviceCombo").SelectedItem as DeviceRow);
   C<Button>("RestartDeviceBtn").IsEnabled=!busy&&DevicePolicy.AllowedRestart(C<DataGrid>("DeviceGrid").SelectedItem as DeviceRow);
  }
  async Task Scan(){if(busy)return;SetBusy(true,"开始只读扫描…");try{report=await Task.Run(()=>Scanner.Scan(s=>Window.Dispatcher.BeginInvoke(new Action(()=>Text("StatusText",s)))));Bind();Log("扫描完成："+report.Devices.Count+" 个系统设备；"+report.Devices.Count(x=>x.Present&&x.Code>0&&x.Code!=22)+" 个设备报告错误。" );}catch(Exception e){Log("扫描失败："+e.Message);MessageBox.Show(Window,e.Message,"扫描失败");}finally{SetBusy(false,report==null?"扫描未完成，请重试":"最近扫描 "+report.Time+" · 未执行压力测试");}}
  void Bind(){
   var temperatures=report.Temperatures??new TemperatureReport();C<ItemsControl>("TemperatureList").ItemsSource=temperatures.Readings;
   Text("TemperatureTime",temperatures.Time==null?"尚未读取温度":"读取于 "+temperatures.Time+" · 温度快照");
   Text("TemperatureNotes",(temperatures.Readings.Count==0?"当前没有可显示的温度。\n":"")+String.Join("\n",temperatures.Notes));
   report.UserName=sessionUser;report.AccountName=sessionAccount;report.Facts.RemoveAll(f=>f.Label=="当前用户名"||f.Label=="完整账户名");report.Facts.Insert(0,new Fact("完整账户名",sessionAccount));report.Facts.Insert(0,new Fact("当前用户名",sessionUser));Text("UserName",sessionUser);
   Text("ComputerName",report.Computer);Text("ModelName",report.Manufacturer+" · "+report.Model);Text("ScanTime","扫描于 "+report.Time);
   Text("DeviceCount",report.DevicesReadable?report.Devices.Count.ToString():"未知");Text("IssueCount",report.DevicesReadable?report.Devices.Count(x=>x.Present&&x.Code>0&&x.Code!=22).ToString():"未知");Text("AudioCount",report.AudioReadable?report.Endpoints.Count(x=>x.StateCode==1).ToString():"未知");
   C<ItemsControl>("FactsList").ItemsSource=report.Facts;C<ItemsControl>("FindingsList").ItemsSource=report.Findings;Text("NoFindings",report.Findings.Count==0?"本次可读取的项目未报告异常；请同时查看下方检测范围。":"");Text("LimitationsText",String.Join("\n\n",report.Limitations));
   var categories=new List<string>{"全部类别"};categories.AddRange(report.Devices.Select(x=>x.Category).Distinct().OrderBy(x=>x));var cat=C<ComboBox>("CategoryCombo");string old=cat.SelectedItem as string;cat.ItemsSource=categories;cat.SelectedItem=categories.Contains(old)?old:categories[0];
   BindAudio();BindAdapters("Audio",report.Devices.Where(x=>x.Present&&x.Class.Equals("MEDIA",StringComparison.OrdinalIgnoreCase)).ToList());BindAdapters("Network",report.Devices.Where(x=>x.Present&&x.Class.Equals("NET",StringComparison.OrdinalIgnoreCase)).ToList());
   C<ItemsControl>("NetworksList").ItemsSource=report.Networks;Filter();UpdateButtons();
  }
  void BindAdapters(string prefix,List<DeviceRow> rows){var combo=C<ComboBox>(prefix+"DeviceCombo");var old=combo.SelectedItem as DeviceRow;combo.ItemsSource=rows;combo.SelectedItem=old==null?null:rows.FirstOrDefault(x=>x.Id==old.Id);if(combo.SelectedItem==null&&rows.Count==1)combo.SelectedIndex=0;AdapterDetails(prefix);}
  void BindAudio(){var combo=C<ComboBox>("OutputCombo");var old=combo.SelectedItem as EndpointRow;var visible=report.Endpoints.Where(x=>x.StateCode!=4).OrderByDescending(x=>x.Default).ThenBy(x=>x.StateCode).ToList();combo.ItemsSource=visible;combo.SelectedItem=old==null?null:visible.FirstOrDefault(x=>x.Id==old.Id);if(combo.SelectedItem==null)combo.SelectedItem=visible.FirstOrDefault(x=>x.Default&&x.StateCode==1)??visible.FirstOrDefault(x=>x.StateCode==1);Text("AudioServicesText","Windows Audio："+report.AudioService+"\nAudio Endpoint Builder："+report.EndpointService);OutputDetails();}
  void OutputDetails(){var ep=C<ComboBox>("OutputCombo").SelectedItem as EndpointRow;Text("OutputDetails",ep==null?(report!=null&&!report.AudioReadable?"输出状态读取失败，请刷新或查看检测范围。":"未发现可用输出。可先尝试音频服务修复。"):("状态："+ep.State+"    音量："+ep.Volume+"    静音："+ep.Muted+"\n"+(ep.Muted=="是"?"此输出当前静音，请在声音设置中取消静音后再验证。":"这里显示的是系统报告；无法据此确定实际是否有声音。")));UpdateButtons();}
  void AdapterDetails(string prefix){var d=C<ComboBox>(prefix+"DeviceCombo").SelectedItem as DeviceRow;Text(prefix+"DeviceDetails",d==null?"请选择需要重启的具体设备。":"驱动 "+d.Driver+" · "+d.State+"\n"+d.Id);UpdateButtons();}
  void DeviceDetails(){var d=C<DataGrid>("DeviceGrid").SelectedItem as DeviceRow;Text("SelectedDeviceName",d==null?"选择设备查看详情":d.Name);C<TextBox>("SelectedDeviceId").Text=d==null?"":d.Id;Text("SelectedDeviceNote",DevicePolicy.Reason(d));UpdateButtons();}
  void Filter(){if(report==null)return;string query=C<TextBox>("SearchBox").Text.Trim();string category=C<ComboBox>("CategoryCombo").SelectedItem as string;bool problems=C<CheckBox>("ProblemsOnly").IsChecked==true;
   var rows=report.Devices.Where(d=>(String.IsNullOrEmpty(category)||category=="全部类别"||d.Category==category)&&(!problems||(d.Present&&d.Code>0&&d.Code!=22))&&(String.IsNullOrEmpty(query)||(d.Name+" "+d.Id+" "+d.Category+" "+d.Driver).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
   C<DataGrid>("DeviceGrid").ItemsSource=rows;Text("FooterText","已显示 "+rows.Count+" / "+report.Devices.Count+" 个系统设备 · 驱动状态不等于完整硬件健康检测");
  }
  async Task RefreshAudio(){if(busy)return;SetBusy(true,"正在读取声音输出与服务状态…");try{var eps=await Task.Run(()=>Audio.Enumerate());if(report==null)report=new ScanReport();report.Endpoints=eps;report.AudioReadable=true;report.AudioService=Scanner.ServiceState("Audiosrv");report.EndpointService=Scanner.ServiceState("AudioEndpointBuilder");BindAudio();Log("音频状态已刷新。");}catch(Exception e){Log("读取音频失败："+e.Message);}finally{SetBusy(false,"音频状态已更新；请用测试音验证实际声音");}}
  async Task TestTone(){if(busy)return;var ep=C<ComboBox>("OutputCombo").SelectedItem as EndpointRow;if(ep==null)return;SetBusy(true,"正在向 "+ep.Name+" 播放测试音…");try{await Task.Run(()=>Audio.PlayTone(ep.Id));Log("测试音已发送至："+ep.Name+"。尚未确认是否实际发声。");SetBusy(false,"测试音播放请求已完成，请确认是否听到");bool heard=Confirm("声音恢复了吗？","刚才向以下输出播放了约 1 秒测试音：\n\n"+ep.Name+"\n\n请按实际听到的结果选择；系统成功发送音频并不代表扬声器一定有声。","听到了","没听到");Text("HearingResult",heard?"用户已确认：这个输出可以发声。":"未确认发声：请检查所选输出，再继续服务或声卡修复。");var last=repairs.LastOrDefault();if(last!=null&&last.Domain=="audio"){last.Verification=DateTime.Now.ToString("HH:mm:ss")+" · 用户"+(heard?"确认所选输出可发声：":"未确认所选输出发声：")+ep.Name;SaveComparisons();}Log(heard?"用户确认听到了测试音。":"用户未确认听到测试音。");}catch(Exception e){Text("HearingResult","播放失败："+e.Message);Log("测试音失败："+e.Message);}finally{SetBusy(false,"测试结束");}}
  async Task RestartSelected(DeviceRow dev){if(busy)return;if(!DevicePolicy.AllowedRestart(dev))return;
   bool network=dev.Class.Equals("Net",StringComparison.OrdinalIgnoreCase);string title=network?"重启这张网卡？":"重启这个设备？";
   if(!Confirm(title,dev.Name+"\n\n"+dev.Id+"\n\n"+DevicePolicy.Reason(dev)+"。\n\n需要管理员授权。如果 Windows 要求重启电脑，本程序会提示，不会自动重启整机。",network?"确认断网并重启":"确认重启"))return;
   await RunOperation("restart-device",dev.Id,"重启设备："+dev.Name);
  }
  async Task RunOperation(string op,string id,string label){if(busy)return;SetBusy(true,"准备 "+label+"，可能出现管理员授权窗口…");
   ScanReport before=null;OperationResult result=null;RepairRecord comparison=null;bool attempted=false;
   try{before=await Task.Run(()=>Scanner.Scan(s=>{}));report=before;Checkpoint(label,id);Log("开始操作："+label);attempted=true;result=await Operations.Elevated(op,id);Log(result.Message);foreach(var step in result.Steps.Where(x=>!String.IsNullOrWhiteSpace(x)))Log(step);
    Text("HearingResult","设备操作后需要重新播放测试音，确认是否实际恢复。");
    SetBusy(true,"操作已返回，正在重新扫描设备状态…");await Task.Delay(1800);report=await Task.Run(()=>Scanner.Scan(s=>Window.Dispatcher.BeginInvoke(new Action(()=>Text("StatusText",s)))));
    comparison=RepairComparison.Create(before,report,op,id,label,result);repairs.Add(comparison);SaveComparisons();Bind();
    if(op=="restart-device"&&!String.IsNullOrEmpty(id)){var after=report.Devices.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase));Log("重启后设备状态："+(after==null?"暂未找到该设备":after.State));}
    if(op=="restart-device"&&report.Devices.Any(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)&&x.Class.Equals("Net",StringComparison.OrdinalIgnoreCase)))Log("网卡操作后："+String.Join("；",report.Networks.Select(x=>x.Name+"="+x.State+" / "+x.Addresses))+"。未验证互联网连通性。");
    MessageBox.Show(Window,result.Message+"\n\n详细系统结果已保存在“操作记录”。",result.Success?"操作完成 · 请验证实际功能":result.RebootRequired?"需要整机重启":"操作未完成",MessageBoxButton.OK,result.Success?MessageBoxImage.Information:MessageBoxImage.Warning);
   }catch(Exception e){Log("操作异常："+e.Message);MessageBox.Show(Window,e.Message,"操作未完成");}finally{if(attempted&&comparison==null){comparison=RepairComparison.Create(before,null,op,id,label,result);repairs.Add(comparison);SaveComparisons();}SetBusy(false,"操作结束，请查看修复对照并验证实际功能");}
  }
  void Checkpoint(string label,string id){try{Directory.CreateDirectory(dataDir);var snapshot=new{Time=DateTime.Now.ToString("o"),Operation=label,DeviceId=id,Report=report,AudioServices=new{Audiosrv=Scanner.ServiceState("Audiosrv"),EndpointBuilder=Scanner.ServiceState("AudioEndpointBuilder")}};string path=Path.Combine(dataDir,"before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");File.WriteAllText(path,Operations.Json().Serialize(snapshot),new UTF8Encoding(false));Log("已保存操作前诊断："+path);}catch(Exception e){throw new IOException("无法保存操作前记录，操作尚未执行："+e.Message);}}
  void Log(string msg){string line="["+DateTime.Now.ToString("HH:mm:ss")+"] "+msg;history.Add(line);C<TextBox>("LogText").Text=String.Join("\r\n\r\n",history);if(activePage=="Logs")C<TextBox>("LogText").ScrollToEnd();try{Directory.CreateDirectory(dataDir);File.AppendAllText(Path.Combine(dataDir,"operations.log"),DateTime.Now.ToString("yyyy-MM-dd ")+line+Environment.NewLine,new UTF8Encoding(false));}catch{}}
  void Export(){if(report==null)return;bool hide=C<CheckBox>("HideReportIdentity").IsChecked==true;var dialog=new SaveFileDialog{Title="导出完整诊断报告",Filter="HTML 离线报告|*.html|JSON 诊断数据|*.json",FileName="紫芯报告-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".html"};if(dialog.ShowDialog(Window)==true)try{var bundle=CurrentBundle();File.WriteAllText(dialog.FileName,dialog.FilterIndex==1?FullReport.Html(bundle,hide):FullReport.Json(bundle,hide),new UTF8Encoding(false));Log("已导出报告："+dialog.FileName);Text("StatusText","报告已保存；"+(hide?"已隐藏当前账户和电脑名，其余网络与设备信息仍可能敏感":"报告保留了身份信息"));}catch(Exception e){MessageBox.Show(Window,e.Message,"导出失败");}}
  void Open(string uri){try{Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}catch(Exception e){MessageBox.Show(Window,e.Message,"无法打开");}}
  [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
  void UpdateLayoutMode(){
   bool narrow=Window.Width<1080,shortWindow=Window.Height<700;
   var actions=C<WrapPanel>("HeaderActions");Grid.SetRow(actions,narrow?1:0);Grid.SetColumn(actions,narrow?0:1);Grid.SetColumnSpan(actions,narrow?2:1);
   actions.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Right;actions.Margin=new Thickness(0,narrow?12:0,0,0);
   C<StackPanel>("BrandPanel").Margin=new Thickness(12,0,0,shortWindow?12:32);
   C<Border>("BrandIcon").Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;
   C<Border>("SidebarInfo").Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;
  }
  Window CreateConfirmation(string title,string body,string yes,string no){
   double maxHeight=Math.Max(1,Math.Min(SystemParameters.WorkArea.Height-20,Window.ActualHeight>0?Window.ActualHeight-20:Window.Height-20));
   var dialog=new Window{Title=title,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=Math.Max(1,Math.Min(540,SystemParameters.WorkArea.Width-20)),MaxHeight=maxHeight,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Background=new SolidColorBrush(Color.FromRgb(15,26,46)),Foreground=new SolidColorBrush(Color.FromRgb(237,242,252)),FontFamily=Window.FontFamily,FontSize=13,ShowInTaskbar=false};
   var stack=new Grid{Margin=new Thickness(20)};stack.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});stack.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});stack.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   stack.Children.Add(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,FontSize=21,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(255,209,46)),Margin=new Thickness(0,0,0,15)});
   var scroll=new ScrollViewer{Name="ConfirmationBody",VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=new TextBlock{Text=body,TextWrapping=TextWrapping.Wrap,LineHeight=24}};Grid.SetRow(scroll,1);stack.Children.Add(scroll);
   var buttons=new WrapPanel{Name="ConfirmationActions",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};Grid.SetRow(buttons,2);
   var cancel=new Button{Content=no,Style=(Style)Window.FindResource("Secondary"),IsCancel=true,Margin=new Thickness(0,0,12,0)};cancel.Click+=(s,e)=>dialog.DialogResult=false;
   var ok=new Button{Content=yes,Style=(Style)Window.FindResource(typeof(Button))};ok.Click+=(s,e)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(ok);stack.Children.Add(buttons);dialog.Content=stack;dialog.Loaded+=(s,e)=>cancel.Focus();return dialog;
  }
  bool Confirm(string title,string body,string yes,string no="取消"){return CreateConfirmation(title,body,yes,no).ShowDialog()==true;}
 }
}
