# 当前架构

本仓库保留 v1.7.1 的现有源码行为，发布整理不等于已完成重构。`src/PurpleCare/build.ps1` 编译根目录 C# 文件，将 XAML 和照片嵌入主 EXE，单独编译 `Launcher/Launcher.cs` 为兼容入口。

| 模块 | 职责 |
| --- | --- |
| `App.cs` / `MainWindow.xaml` | 启动、窗口、导航、数据绑定和确认框 |
| `Models.cs` | 扫描模型、设备策略及系统信息采集 |
| `Operations.cs` | 管理员子进程、命名管道、服务及设备操作 |
| `Audio.cs` | CoreAudio 枚举与 WASAPI 测试音 |
| `Temperature.cs` / `GpuTemperature.cs` | 系统温区、厂商数据和 WDDM GPU 温度读取 |
| `Stress.cs` / `StressUi.cs` | 独立压力测试进程、任务文件和界面 |
| `LiveMonitor.cs` | 手动实时采样与有界曲线历史 |
| `NetworkDiagnostics.cs` / `DiagnosticsUi.cs` | 主动网络探测、修复对照和结果绑定 |
| `RepairGuide.cs` / `GuideHistoryUi.cs` | 引导步骤、用户观察、历史界面 |
| `ReportHistory.cs` / `FullReport.cs` | 历史 JSON、校验、身份字段处理、离线 HTML |
| `EdgeBackup*.cs` / `BackupIntegrity.cs` | 独立手动备份、取消、预检与校验 |
| `PortablePaths.cs` | 电脑 / 账户隔离的便携数据目录 |
| `*Tests.cs` | 内嵌的模拟自检，目前仍与应用一起编译 |

设备操作从 UI 确认进入 `Operations`，按需通过 UAC 启动 worker。管理员 worker 不承担整个 UI；常规启动保持当前用户权限。诊断结果再回到界面并写入本地记录。

已知耦合包括 `MainUi` partial 之间共享字段、`Models.cs` 同时承担模型和扫描、任务各自处理取消与 busy 状态、测试随生产程序编译。重构时应逐块提取并维持外部行为，而不是同时改 UI、权限路径、数据格式和测试框架。
