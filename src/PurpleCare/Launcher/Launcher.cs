using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
[assembly: AssemblyTitle("紫芯电脑助手 · 兼容启动入口")]
[assembly: AssemblyVersion("1.7.1.0")]
[assembly: AssemblyFileVersion("1.7.1.0")]
internal static class Launcher {
 [STAThread] private static int Main(){
  try {
   string target=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"紫芯电脑助手.exe");
   if(String.Equals(target,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))throw new IOException("启动入口名称不正确，请保留完整程序目录。");
   if(!File.Exists(target))throw new FileNotFoundException("未找到新版紫芯电脑助手.exe，请将启动入口和主程序放在同一目录。");
   Process.Start(new ProcessStartInfo(target){UseShellExecute=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory});return 0;
  }catch(Exception e){MessageBox.Show(e.Message,"紫芯启动入口",MessageBoxButton.OK,MessageBoxImage.Warning);return 1;}
 }
}
