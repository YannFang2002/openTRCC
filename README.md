# OpenTRCC

<img src="assets/icon/opentrcc.png" width="96" alt="OpenTRCC icon">

利民 2.8" / 320×240（USB `0416:5302`）小屏的非官方 Windows 上位机。
把机箱里那块屏做成可自由编排的实时仪表盘：主题、传感器、任意游戏 FPS。

> 非官方社区项目，与 Thermalright / 利民无隶属关系。不改造原厂 TRCC，固件升级仍用 TRCC。

## 现在能做什么

- **屏幕**：HID 通道直连（513 字节报告），RGB565 全帧 320×240，实测 23.2fps 上限、实际推流约 21fps
- **主题**：`themes/*.json`（内置 10 套），text / bar / ring / sparkline / image 五类图层；背景支持纯色/渐变/图片/GIF/视频/**程序化动态**（aurora / waves / particles / stars / grid / plasma / rain / ronin / sprinter）
- **数据**：系统计数器、LibreHardwareMonitor（**全量传感器，实测 313 个**：逐核温度/占用、GPU、主板、内存、风扇…）、PresentMon 2.6.0（任意应用 FPS + 1% low）
- **编辑器**：2× 画布、拖拽移动、图层增删、分类型属性、传感器绑定、撤销/重做、保存即上屏；背景支持纯色/渐变/图片/**GIF/视频**（视频导入时转成 320×240 JPEG 帧序列，播放只解 JPEG）——概览（实时预览/主题/启停）、传感器（全量列表 + 搜索）、设置（来源状态/路径/自启/诊断）、日志（实时 tail）；托盘常驻、单实例、HKCU 自启
- **诊断**：`OpenTRCC.Probe` 命令行（握手 / 测试图 / bench / 渲染 / 传感器）

## 下载与安装

从 [Releases](https://github.com/YannFang2002/openTRCC/releases) 下载 `OpenTRCC-<version>-win-x64.zip`：

1. 解压到任意目录（例如 `D:\Tools\OpenTRCC`）
2. **先关闭 TRCC**（它独占屏幕的 HID 设备）
3. 双击 `OpenTRCC.exe`

压缩包是自包含单文件版本，不需要安装 .NET 运行时。首次启动会在 `%LocalAppData%\OpenTRCC` 创建日志和设置。

## CPU 温度与 PawnIO

CPU 温度/功耗通过 LibreHardwareMonitor 0.9.7 + **PawnIO** 驱动读取（不再是会被安全软件拦截的 WinRing0）：

1. 安装 [PawnIO](https://pawnio.eu/)（官方签名版，安装后 `C:\Program Files\PawnIO`）
2. 以管理员身份运行 OpenTRCC（设置页有「以管理员身份重启」；或启用计划任务 `OpenTRCC_Startup`）
3. 启动时应用会自动执行 `sc start PawnIO`

没有 PawnIO 或非管理员时，GPU 温度/占用仍然可用，CPU 温度显示 `—`。

## 环境

- Windows 10/11 x64
- 仓库内自带 .NET SDK（`.dotnet/`，9.0.319），无需系统安装
- 首次使用前 **关闭 TRCC**（它独占 HID 设备）

## 构建与运行

```powershell
# 安装仓库内 SDK（首次）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-dotnet.ps1

# 构建 + 测试
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Configuration Release -Test

# 启动桌面应用
.\src\OpenTRCC.App\bin\Release\net9.0-windows\OpenTRCC.exe
```

## 数据位置

- 用户主题与导入素材：安装目录 `themes\`（不可写时回退到 `%LocalAppData%\OpenTRCC\themes`）
- 设置/日志：`%LocalAppData%\OpenTRCC`

## 发布打包

```powershell
# 自包含单文件（默认，约 73 MB 压缩包）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1 -Version 0.1.0

# 依赖系统 .NET 9 桌面运行时（体积小很多）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1 -Version 0.1.0 -FrameworkDependent
```

产物在 `artifacts\OpenTRCC-<version>-win-x64.zip`。

## Probe 常用命令

```powershell
$probe = 'tools\OpenTRCC.Probe\bin\Release\net9.0-windows\OpenTRCC.Probe.dll'
.\.dotnet\dotnet.exe $probe list                     # 枚举设备/占用状态
.\.dotnet\dotnet.exe $probe handshake                # 握手，打印 PM/SUB
.\.dotnet\dotnet.exe $probe pattern --rotate 90      # 测试图
.\.dotnet\dotnet.exe $probe bench --all --seconds 5  # 帧率基准
.\.dotnet\dotnet.exe $probe render --theme themes\game-320x240.json --live --push
.\.dotnet\dotnet.exe $probe sensors --lhm --seconds 8
```

## 目录

```text
src/OpenTRCC.Core       主题模型、Skia 渲染、绑定表达式、RGB565/帧组装
src/OpenTRCC.Devices    HID/WinUSB 传输、握手、Profile、帧流控
src/OpenTRCC.Sensors    系统计数器、LHM、PresentMon、聚合与历史
src/OpenTRCC.App        WPF 应用壳（窗口/预览/托盘/自启）
tools/OpenTRCC.Probe    硬件与主题诊断 CLI
themes/                 内置 11 套主题（含程序化动态背景）
assets/icon             应用图标（PNG + 多尺寸 ICO）
docs/RELEASE-0.1.0.md   发布说明
third_party/presentmon  PresentMon 2.6.0（MIT）+ LICENSE
```

## 已知限制

- 静态主题需 1.2s 保活重发，否则面板 2–3s 后回落开机 logo
- CPU 温度/功耗需要管理员（LHM ring0）；非管理员只显示 GPU 与负载
- 深色渐变在 RGB565 下会量化，已加 4×4 Bayer 抖动
- 应用日志：`%LocalAppData%\OpenTRCC\app.log`；设备配置：`%LocalAppData%\OpenTRCC\device-profile.json`
