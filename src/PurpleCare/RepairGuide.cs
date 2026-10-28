using System;
using System.Collections.Generic;
using System.Linq;
namespace PurpleCare {
 public sealed class GuideStep {public string Title{get;set;} public string Instruction{get;set;} public string Page{get;set;} public string Observation{get;set;} public string Note{get;set;}}
 public sealed class GuideSnapshot {public string Symptom{get;set;} public string Started{get;set;} public string Updated{get;set;} public string Conclusion{get;set;} public List<GuideStep> Steps=new List<GuideStep>();}
 public static class RepairGuide {
  public static readonly string[] Outcomes={"尚未记录","用户观察：本步正常 / 已恢复","用户观察：仍有问题","用户观察：无法判断"};
  static GuideStep Step(string title,string text,string page){return new GuideStep{Title=title,Instruction=text,Page=page,Observation=Outcomes[0],Note=""};}
  public static GuideSnapshot Create(int kind){
   var r=new GuideSnapshot{Started=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Conclusion="尚未确认最终结果"};
   if(kind==0){r.Symptom="声音无声 / 拔耳机后无声";r.Steps.AddRange(new[]{
    Step("确认输出并播放测试音","在声音修复页选择实际扬声器或耳机，检查连接、音量和静音，再主动播放测试音。请根据实际听到的声音记录结果；有声即可结束引导。","Audio"),
    Step("按需恢复音频服务","如果仍然无声，可在声音修复页主动点击恢复音频服务，并确认中断播放、录音的影响。返回后记录结果；引导本身不会执行修复。","Audio"),
    Step("按需重启实际声卡","服务恢复后仍无声时，选择实际声卡再按需重启。HDMI/显示器音频与机身声卡可能不同，请勿随意选择设备。","Audio"),
    Step("复测并决定下一步","再次播放测试音，查看修复前后对照。如果仍无声，保留报告，再排查驱动、插孔或物理硬件；不要循环重启所有设备。","Audio")});}
   else if(kind==1){r.Symptom="网络不能上网";r.Steps.AddRange(new[]{
    Step("先做分层诊断","在网络修复页设置需要测试的域名并主动开始诊断，依次查看链路、地址、网关、DNS、HTTPS。超时或网站限制不直接等于网卡故障。","Network"),
    Step("根据失败层检查","无链路先检查连接与无线开关；地址异常检查网络配置；DNS 或网站错误需结合 VPN、代理和站点策略判断。诊断使用系统路由，不保证经过选中的网卡。","Network"),
    Step("仅在有必要时重启网卡","如果怀疑网卡或驱动卡住，可在网络修复页选择对应网卡并主动确认重启。远程控制时不要重启承载当前连接的网卡。","Network"),
    Step("再次诊断并核对","用相同目标再次诊断，查看修复对照；再实际打开需要访问的网站。即使系统命令成功，也要按真实使用结果记录。","Network")});}
   else if(kind==2){r.Symptom="运行卡顿 / 稳定性排查";r.Steps.AddRange(new[]{
    Step("查看扫描线索","在概览查看磁盘空间、设备状态、存储健康和近期 WHEA 日志。错误记录是排查线索，不直接证明某个硬件损坏。","Home"),
    Step("观察实时负载","在概览展开实时监控并主动开始，观察卡顿时的 CPU、内存和可读 GPU 温度。CPU 核心温度尚未接入；不要把温区当作 CPU 核心温度。","Home"),
    Step("按需短时压力测试","先保存工作、保证散热，再按需进行短时 CPU 或限额内存校验。已存在明显过热、异常气味或频繁断电时先停止使用并检修。该测试不代表全硬件健康。","Stress"),
    Step("保存观察与报告","结合卡顿出现时间、负载、日志和测试结果记录现象。原因不明时保留报告，避免盲目重启或更换硬件。","History")});}
   else throw new ArgumentOutOfRangeException("kind");
   r.Updated=r.Started;return r;
  }
  public static bool Recorded(GuideStep step){return step!=null&&Outcomes.Skip(1).Contains(step.Observation);}
  public static string Summary(GuideSnapshot guide){return guide==null?"尚未开始引导":guide.Symptom+" · 已记录 "+guide.Steps.Count(Recorded)+" / "+guide.Steps.Count+" 步 · "+guide.Conclusion+"（用户反馈，不等于自动诊断结论）";}
 }
}
