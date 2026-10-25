# OpenTRCC — 利民 2.8" VISION ARGB（320×240）新一代上位机实施方案

| 文档信息 | 内容 |
|---|---|
| 项目 | OpenTRCC |
| 版本 | v1.1 |
| 日期 | 2026-10-24 |
| 状态 | M0 完成 · M1 通过（23.2fps）· M2 主题/渲染可用 · M3 传感器已接入 · M4 应用壳可用 |
| 目标硬件 | 利民 2.8" IPS，320×240，USB `0416:5302` |
| 目标系统 | Windows 10/11 x64 |

> **v1.1 修订（2026-10-24 本机复核）**：修正 Python 版本与 GPU 拓扑；512B 分块由"必须"降为**假设 H1**（M1 bench 验证）；TRCC 占用检测前置；1% low 定义写死；补虚拟显示适配器过滤、主题包 zip bomb 上限、WPF DPI 感知、未签名产物风险；工期由 8.5 人日重排为约 **12 人日**，**M1 定为强制 Go/No-Go**。

> **修正**：已按确认的 **2.8 英寸 IPS、320×240、4:3 横屏** 设计；之前“方形 240×240”的假设作废。该规格与 **Peerless Assassin 120 Vision ARGB** 的官方参数一致（2.8" / 320×240 / 主板 9Pin USB 2.0），最终以设备握手读到的 PM/SUB 为准。
>
> **命名**：产品、仓库、命名空间、可执行文件统一为 **OpenTRCC**。这是非官方社区项目，与利民/Thermalright 无隶属关系，About 页会放免责声明；GitHub 目前检索不到同名仓库。
>
> **一句话**：不改造 TRCC。新做一个本地优先、可自由排版的 Windows 上位机：自动识别 `0416:5302` 屏幕，30fps 驱动 320×240 面板，读取任意游戏 / 任意显示器的 FPS 与 CPU/GPU/内存/硬盘/网络数据，并用可视化编辑器把背景、数据、图表自由组合。TRCC 只保留用于固件升级。

---

## 1. 调研结论（已在本机核实）

### 1.1 你的环境

| 项目 | 实测结果 |
|---|---|
| 现有上位机 | `D:\Program Files\TRCCCAP\TRCC.exe`，版本 **2.1.6.0**（官方最新 2.1.8） |
| 屏幕 | 2.8" IPS，**320×240（4:3 横屏）**，主板 9Pin USB 2.0 供电+数据 |
| 屏幕 USB | `USB\VID_0416&PID_5302`，复合设备，bcdDevice **4.07** |
| 帧通道 | `MI_01` = **WinUSB / USBDISPLAY**（bulk，非 HID 报文） |
| 驱动 | `MI_01` 已绑定 **WINUSB**（10.0.26100.9549），无需装驱动 |
| 控制通道 | `MI_00` = HID vendor-defined（暂不使用） |
| CPU / GPU | Ryzen 7 9800X3D（8C/**8T**，SMT 已关）/ Radeon RX 6750 GRE 12GB；另有核显 `AMD Radeon(TM) Graphics` 与 `GameViewer Virtual Display Adapter`（显示相关逻辑必须过滤虚拟适配器） |
| 运行时 | .NET 9.0.1 Runtime（NETCore + WindowsDesktop）已装；**无 .NET SDK**（改用仓库内 `.dotnet/`）；Node 22.22 / Python **3.13.12**；git 2.54；仅 Windows PowerShell **5.1**（无 pwsh 7，脚本按 5.1 编写）；nuget.org / dot.net / github 可达 |
| 监控工具 | 未安装 PresentMon / HWiNFO / RTSS |
| 权限 | 当前会话非管理员；LHM 读温度/功耗需要一次 UAC；已在 Performance Log Users 组，PresentMon 可能可非提权运行 |

> 注：AS120 Vision ARGB 是 1.54" / 240×240 方形，不是这块屏；2.8" / 320×240 与 Peerless Assassin 120 Vision ARGB 规格一致，协议同属 `0416:5302` HID Type 2，因此固件层按 320×240 处理。

### 1.2 屏幕协议结论

社区逆向资料把 `0416:5302` 归类为 **Thermalright HID Type 2**，Windows TRCC 通过 `USBLCDNEW.dll + LibUsbDotNet` 走 bulk 端点。关键事实：

**握手（512 字节）**

- 写入 `EP 0x02`：`DA DB DC DD` + 8 字节 0 + `01 00 00 00` + 4 字节 0，补零到 512；
- 从 `EP 0x01` 读 512 字节；
- 校验 magic 与 `resp[12] == 0x01`；**PM = resp[5]，SUB = resp[4]**，可选序列号在 `resp[20:36]`；
- bcdDevice 4.07 存在“流式固件”分支：跳过 init、等待约 3 秒、读取 8 字节自愿回复；**先标准握手，失败再回落**，不要只信 4.07 指纹。

**320×240 的 Type 2 帧（20 字节头 + 像素数据，512 字节对齐）**

| 偏移 | RGB565 模式 | JPEG 模式 |
|---|---|---|
| 0–3 | `DA DB DC DD` | 同左 |
| 4–5 | `02 00` | 同左 |
| 6–7 | `01 00` | `00 00` |
| 8–11 | **固定 240×320**（线序是竖屏 240×320） | 实际宽高 LE |
| 12–15 | `02 00 00 00` | 同左 |
| 16–19 | payload 长度 LE | 同左 |

- **用户画布 320×240 横屏 → 顺时针旋转 90° → 线序 240×320**，正好对应头部固定值；
- RGB565 小端为主；PM=49 的机型为大端，需探测；
- **假设 H1**：帧需按 **512 字节分块**写入 `EP 0x02`（社区结论，整帧一次写会让屏幕停在开机 logo）；M1 的 `probe bench --all` 会实测 512B / 1KB / 4KB / 整帧四档，若固件接受更大块，编码器按实测上限放大，性能风险随之消失；每帧后 1ms settle 同样作为可配项验证；
- payload = 240×320×2 = **153,600 字节**；加头后 153,620 → 补齐到 **154,112 字节 = 301 个 512B 块**；
- 30fps ≈ **9,030 次写/秒、4.62MB/s**；先按 30fps 设计，实测不达标自动降 20fps；
- 静态主题不必跑满 30fps：内容不变不发帧，只在数据变化/动画时发。

**分辨率映射（公开逆向资料，P1 只做 0416:5302）**

| PM / FBL | 分辨率 | 编码 | 线序旋转 |
|---|---|---|---|
| **49 / 50 / 51 / 52 / 53 / 58** | **320×240（目标屏）** | RGB565（49 大端，其余小端） | **90°** |
| 36 / 37 | 240×240（AS120 等方形屏） | RGB565 | 0° |
| 54 | 360×360 | JPEG | 0° |
| 100 / 101 / 102 | 320×320 | RGB565 大端 | 0° |
| 114 | 1600×720 | JPEG | 90° |
| 128 | 1280×480 | JPEG | 90° |
| 192 | 1920×462 | JPEG | 90° |
| 224 / 59 | 854×480 / 640×172 | JPEG / RGB565 | 按资料表 |
| 未知 | — | — | 让用户从列表选择 |

> 仍需 **M1 硬件探针**确认：PM 具体是 49/50/51/52/53/58 中的哪一个、字节序、旋转方向。测试图会自带方向标记，用户一眼就能看出 90° 还是 270°、是否镜像。

**其他**

- 只暴露 LCD，风扇 ARGB 走主板 ARGB 接口，TRCC 也无法直接控制其灯珠；RGB 联动应走 OpenRGB（P2/P3）。
- 未在协议中发现硬件亮度命令；P1 用软件调光（RGB 乘系数），硬件亮度留待后续探测。
- 协议事实来自公开逆向文档；实现独立编写，不复制 GPL 代码。

### 1.2.1 M1 实测结果（2026-10-24，本机真实硬件）

**实测推翻了原方案的通道假设**：这块屏的数据通道不是 MI_01 WinUSB，而是 **MI_00 厂商 HID**。

| 项目 | 实测 | 影响 |
|---|---|---|
| MI_01 设备接口 | **不存在**（`pnputil /enum-interfaces` 只有 MI_00 HID 与复合设备；`USB\MS_COMP_WINUSB` 走收件箱 `winusb.inf`，未注册 `DeviceInterfaceGUIDs`；对复合设备句柄 `WinUsb_Initialize` 返回 6） | 原“WinUSB 直连 bulk”不可行；`Devices` 层改为 **HID 优先、WinUSB 兜底** |
| MI_00 HID | usage page `0xFF06`、usage `0x0001`；输出报告 **513 B**（1 字节报告 ID + 512 负载），输入 37 B，Feature 9 B | 每 512 B 协议块 = 一个 HID 输出报告，`WriteFile` 513 B |
| 握手 | 标准 512 B init 有效；**37 B 回复**含 `DA DB DC DD`、`PM=53`、`SUB=0`、status `0x01` | PM 落在预期 320×240 行；流式探测 3 s 无自愿回复，不需要回落 |
| 帧格式 | RGB565 20 B 头 + `240×320×2` 负载 + 512 对齐（301 个报告） | 与方案一致 |
| 方向/颜色 | `rotate 90` + 小端，**用户目视确认正确**（箭头朝上、TL 红在左上、四色正常） | 已固化 `device-profile.json` |
| JPEG | 同参数改 JPEG 头 → 无图像 | PM=53 固件只吃 RGB565 |
| 帧率 | 串行提交 **13.0 fps**；**pipeline=2 起 23.2 fps**（3.42 MiB/s；深度 2/4/8/16 饱和同值），实际带渲染 21.5 fps；chunk 放大无提升（报告固定 513 B） | 达到 20fps 下限；30fps 需 ~4.6 MB/s，高于 512B × 8 kHz 的 HID 理论上限（~4.1 MB/s），全帧 RGB565 到不了 30 |
| 停止发送 | 停发 2–3 s 后面板自行回落到开机 logo | 静态主题必须**持续保活发帧**，不能“内容不变就不发” |
| 独占 | TRCC 运行时以共享模式 0 独占 MI_00（`ERROR_SHARING_VIOLATION`），关闭后正常 | 启动检测并提示关闭 TRCC，不静默杀进程 |

> 设备参数：PM=53 / SUB=0 / 240×320 / RGB565 / 小端 / rot90 / chunk 512，已写入 `%LocalAppData%\OpenTRCC\device-profile.json`。
> 证据：`artifacts/probe-runs/20261024-214610`（握手 hex）、`artifacts/probe-runs/20261024-220026`（bench CSV）。

### 1.2.2 M3 实测（2026-10-24，非管理员会话）

| 来源 | 实测 | 处理 |
|---|---|---|
| 系统计数器 | CPU 15–53%、内存 47%、网卡 0–9 MB/s、前台进程名全部可用 | 无依赖、无管理员，`SystemSensorSource` |
| LibreHardwareMonitor 0.9.4 | **非管理员也能开**：GPU 温度 50–55 °C、GPU 占用/功耗、风扇 813 RPM、显存 2.5 GB 可读；**CPU 温度/功耗为 0**（需 ring0） | 0 值过滤为不可用；CPU 温度标记“需要管理员”，绝不显示 0 °C |
| PresentMon 2.6.0（已随仓库） | 非提权可跑，但会警告“部分进程无法查询”，`Application` 列为 `<unknown>` | 用 PID 反查进程名；ETW 不注入，反作弊安全 |
| 静态保活 | 内容不变时 0.9 fps 重发，60 s 面板**一直停在主题**、未回 logo | `FramePump`：内容哈希 + 1.2 s 保活，静态主题几乎不占 USB |
| **实时闭环** | `probe render --live` = 系统 + LHM + PresentMon → 绑定 → 渲染 → 上屏；Telemetry 主题实测数字随数据变化（用户确认） | 传感器→主题→面板 全链路打通；渲染 ~2 ms/帧、屏幕刷新 ~1 次/秒 |

> M3 仍缺：多 GPU 选择、每显示器 FPS（VidPnSourceId 映射）、0.1% low、LHM 的 CPU 温度需一次 UAC 验证。

### 1.3 TRCC 能力基线

| 类别 | TRCC 已有 |
|---|---|
| 屏幕 | 主题、图片、GIF、H.264/H.265 视频（MP4/MKV/MOV/AVI）、投屏 |
| 主题 | 内置/云主题、遮罩、导入导出、轮播 |
| 叠加 | 文字、传感器、日期时间、字体/颜色/缩放 |
| 监控 | CPU/GPU 温度、风扇、功耗、占用等（宣称 77+ 传感器） |
| 屏幕参数 | 16 种分辨率、0/90/180/270、亮度 |
| 多设备 | 自动检测、多设备选择 |
| RGB | 13 种灯效、分区、呼吸/彩虹/静态/波浪 |
| 音频 | 音频可视化、麦克风频谱 |
| 系统 | 托盘、开机自启、多语言、设备信息、固件升级 |

短板：UI 老旧、固定模板、无任意游戏 FPS、无 1% low/帧时间图、无真正的图层编辑器、无本地 API/插件、主题扩展困难、必须常驻管理员、对非官方屏幕协议封闭。

### 1.4 技术选型

**主方案：.NET 9 + WPF + SkiaSharp + WinUSB P/Invoke + LibreHardwareMonitorLib + PresentMon**

理由：

- 原生 Windows、无浏览器运行时，内存/CPU 占用低；
- LHM 是 Windows 硬件监控事实标准，覆盖 9800X3D 与 AMD ADL GPU 的概率最高；
- PresentMon 用 ETW，**不注入、不 Hook**，对反作弊安全，能读任意 DX9/11/12/Vulkan/OpenGL 应用；
- WinUSB 直接 P/Invoke，不依赖 libusb DLL，设备已由 TRCC 装好 WinUSB 驱动；
- WPF + SkiaSharp 可用同一套渲染器同时输出设备帧和编辑器预览，保证所见即所得。

备选/退路：

- 若 SDK 安装受限：Python 3.12 + PySide6 + pythonnet，协议/渲染/数据层设计不变；
- 不采用“直接 fork trcc-linux”：GPL、Python/Qt、仍缺 FPS，且 UI 仍是旧思路；
- 不采用 Electron：为一个 320×240 小屏引入 Chromium 不划算。

---

## 2. 产品定义

### 2.1 目标

**OpenTRCC**：把机箱里那块 2.8" 小屏变成可自由编排的实时仪表盘，而不是只能套模板的“数码相框”。它是 TRCC 的开源、可扩展、Windows 原生替代品。

### 2.2 设计原则

1. **不注入、不 Hook**：FPS 用 PresentMon ETW；硬件用 LHM 只读驱动；对游戏零侵入。
2. **本地优先**：无账号、无遥测；主题和素材都在本地；联网只用于可选的天气/更新。
3. **主题即数据**：主题是带版本号的 JSON + 资源包，可导入导出、可手改、可版本管理。
4. **失败降级**：传感器缺失显示 `N/A` 并说明原因，绝不崩溃；PresentMon 失败回退 RTSS 共享内存。
5. **低占用**：静态主题只在数据变化时发帧；动画/视频才跑满 30fps。
6. **可测试**：设备层有 mock transport；渲染有 golden image；协议有字节级单测。
7. **非官方**：不冒充利民官方软件，不修改固件，不静默接管设备。

### 2.3 P1 成功标准

- 启动后 3 秒内识别屏幕并显示测试图；用户确认“方向、颜色、比例都对”。
- 默认主题稳定显示 CPU 温度、GPU 温度/占用、当前游戏 FPS、时间；数据延迟 ≤1 秒。
- 320×240 下达到 30fps；若实测 USB 受限，20fps 为可接受下限，并明确显示实际帧率。
  **M1 实测（PM=53 / HID 513B 报告）**：串行 13fps → **写入流水线（2 个在途报告）23.2fps**，带渲染 21.5fps，已达到 20fps 下限；30fps 受 HID 带宽上限（~4.1 MB/s）限制，全帧 RGB565 不可达。P1 目标定为“稳定 21–23fps + 如实显示实际帧率”，30fps 只在局部刷新/降分辨率时再评估。
- FPS 与 PresentMon/CapFrameX 对比误差 ≤1 FPS；1% low 误差 ≤2 FPS。
- 用户能在 5 分钟内：换自己的背景图 → 加一个绑定任意传感器的文字 → 拖动/改字号/改颜色 → 保存 → 屏幕立即更新。
- 拔插 USB 后 5 秒内自动恢复；重启应用恢复上次主题。
- 30 分钟烤机：USB 错误 0，应用 CPU <3%，内存 <300MB。
- 开机自启通过计划任务以最高权限运行，**不再弹 UAC**。

---

## 3. 功能矩阵

| 能力 | TRCC | OpenTRCC P1（首个交付） | 后续 |
|---|---|---|---|
| 主题/图片/GIF | ✓ | ✓ | 视频 P2 |
| 视频播放 | ✓ | — | P2（FFmpeg LGPL） |
| 投屏 | ✓ | — | P3（Windows.Graphics.Capture） |
| 音频可视化 | ✓ | — | P2（WASAPI + FFT） |
| 文字/传感器叠加 | ✓ | ✓ | 脚本/虚拟传感器 P2 |
| 可视化图层编辑器 | — | ✓ | 多选/组件库/对齐 P2 |
| 自定义背景图 | 部分 | ✓（图片/GIF/纯色/渐变/模糊/暗化） | 视频/网页 P2 |
| CPU/GPU/内存/硬盘/网络 | ✓ | ✓（LHM + 系统 API） | 更多源 P2 |
| **任意游戏 FPS** | — | ✓（PresentMon 全局采集） | — |
| **1% low / 帧时间图** | — | ✓ | 0.1% low P2 |
| **每显示器 FPS** | — | ✓（VidPnSourceId 映射） | — |
| 按游戏自动切主题 | — | — | P2 |
| 多设备 | ✓ | — | P2 |
| 旋转/亮度 | ✓ | ✓（软件旋转/调光） | 硬件亮度 P3 |
| RGB 灯效 | ✓ | — | P2/P3（OpenRGB） |
| 云主题/市场 | ✓ | — | P2/P3 |
| 导入 TRCC 主题 | — | — | P3（研究其包格式） |
| 本地 API / Stream Deck / HA | — | — | P2 |
| 插件 SDK | — | — | P2 |
| 开机自启/托盘 | ✓ | ✓ | — |
| 多语言 | ✓ 10 种 | 中/英 | 更多 P2 |
| 固件升级 | ✓ | **不做**（引导回 TRCC） | — |
| 多协议/多品牌屏幕 | 部分 | 0416:5302 only | P3（SCSI/Bulk/LY/Type3） |

---

## 4. 总体架构

```text
┌──────────────────────────── OpenTRCC.App (WPF) ──────────────────────────────┐
│ 概览 / 屏幕 / 主题 / 编辑器 / 传感器 / 设置 / 日志   + 托盘 + 开机自启          │
│        │ 预览帧 10fps                     │ 主题编辑 / 命令                    │
│        ▼                                  ▼                                   │
│  ThemeEditor ──► ThemeStore ──► ThemeRenderer (SkiaSharp) ──► FramePump       │
│                                      ▲                            │           │
│                                      │ SensorSnapshot              │ 512B 分块 │
│  SensorAggregator ◄── LHM / PresentMon / System / Weather          ▼           │
│                                      │                     ThermalrightType2  │
│                                      └───────────────► WinUsbTransport ──► 屏幕│
└──────────────────────────────────────────────────────────────────────────────┘
```

### 4.1 工程结构

```text
OpenTRCC/
├─ OpenTRCC.sln
├─ global.json / Directory.Build.props / NuGet.config
├─ src/
│  ├─ OpenTRCC.Core/        # 主题模型、绑定表达式、Skia 渲染、RGB565/JPEG 编码
│  ├─ OpenTRCC.Devices/     # WinUSB 枚举/传输、Thermalright Type2、设备配置表
│  ├─ OpenTRCC.Sensors/     # LHM、PresentMon、系统/网络/时间、聚合与历史
│  └─ OpenTRCC.App/         # WPF 壳、托盘、编辑器、设置、日志
├─ tests/
│  ├─ OpenTRCC.Core.Tests/
│  └─ OpenTRCC.Devices.Tests/
├─ tools/OpenTRCC.Probe/    # 命令行硬件探针：list / handshake / pattern / bench / sensors / fps
├─ themes/                  # pulse / telemetry / horizon / game 四套 320×240 主题
├─ assets/fonts/            # OFL 字体 + 许可证
├─ third_party/presentmon/  # PresentMon 2.6.0 x64 + LICENSE.txt
├─ docs/                    # PROTOCOL.md / THEME_SCHEMA.md / ARCHITECTURE.md / 本文档
└─ scripts/                 # install-dotnet / build / publish / install
```

> M0 只创建 `Core`、`Devices`、`Probe` 与两个测试项目；`Sensors` 与 `App` 分别在 M3/M4 创建，避免空壳项目分散注意力。仓库内 `.dotnet/` 为本地 SDK（scripts/install-dotnet.ps1 安装），系统 `dotnet.exe` 不会自动发现它，构建一律走 `scripts/build.ps1`。

### 4.2 关键接口（公开契约）

```csharp
public interface ILcdTransport : IAsyncDisposable {
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct);
}

public interface ILcdDevice {
    DeviceProfile Profile { get; }
    Task ConnectAsync(CancellationToken ct);
    Task SendFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken ct);
}

public interface ISensorSource {
    string SourceId { get; }
    IReadOnlyList<SensorDescriptor> Discover();
    void Poll(SensorSnapshotBuilder builder);
}

public interface IFpsSource {
    event Action<FpsSample>? Sample;
    Task StartAsync(CancellationToken ct);
}

public interface IThemeRenderer {
    SKBitmap Render(ThemeDocument theme, SensorSnapshot snapshot, TimeSpan elapsed);
}
```

### 4.3 数据/线程模型

| 线程 | 频率 | 职责 |
|---|---|---|
| 传感器线程 | 1 Hz | LHM 全量更新、系统数据、历史环形缓冲 |
| FPS 读取线程 | 连续 | 异步读 PresentMon stdout，按行解析 CSV |
| 渲染线程 | 30 fps（可降） | 生成 `SKBitmap` → 编码 → `Channel<Frame>` |
| USB 写线程 | 跟随 | 消费容量 1 的 channel，满则丢最旧帧，保证低延迟 |
| UI 线程 | 10 fps 预览 | 共享同一张帧，不额外占用 USB |

`SensorSnapshot` 为不可变记录；UI、渲染、日志都读同一份快照，避免锁竞争。

### 4.4 对外数据契约

- **主题 schema v1**：JSON Schema 随包发布；`schemaVersion` 做迁移；未知字段保留。
- **设置 schema v1**：`%LocalAppData%\OpenTRCC\settings.json`，原子写入。
- **主题包 `.ottheme`**：zip，含 `theme.json`、`assets/`、`preview.png`；导入时校验路径穿越、大小、可执行文件。
- **传感器命名空间**：`cpu.temp.package`、`gpu.temp.hotspot`、`fps.current`、`net.down`… 稳定、小写、点分层级。
- **本地 API（P2）**：`127.0.0.1:59420` + Bearer token，REST + WebSocket；P1 不开放端口。

---

## 5. 设备层设计

### 5.1 枚举与打开

- `SetupDiGetClassDevs(GUID_DEVINTERFACE_USB_DEVICE)` / `CM_Get_Device_Interface_List` 找 `VID_0416&PID_5302&MI_01`；
- `CreateFile` 独占打开（共享模式 0）；`ERROR_SHARING_VIOLATION` 立即判定为“被 TRCC 或其他程序占用”，打印占用进程名与 PID，**不静默杀进程**；
- `WinUsb_Initialize` → `WinUsb_QueryPipe` 找 `0x02 OUT` / `0x01 IN`；
- `WinUsb_SetPipePolicy(PIPE_TRANSFER_TIMEOUT)` 读 5s、写 1s；
- 不安装驱动：复用 TRCC 已装好的 WinUSB；若检测到未绑定，提供签名 INF + `pnputil` 安装向导（管理员一次）。

### 5.2 握手与识别

1. 标准握手（512B init → 回复）：HID 实际只回 **37B**（含 magic/PM/SUB/status），WinUSB 才回 512B，解析器按 `len>=6 && magic` 与 `len>=20 && resp[12]==0x01` 分别接受；解析 PM/SUB、可选序列号。
2. 若失败且 bcdDevice=4.07：跳过 init，等待 3s，读 8 字节短回复；成功则按流式固件处理。
3. PM→FBL→`DeviceProfile`；目标屏预期落在 320×240 行（PM 49/50/51/52/53/58）。
4. 未知 PM 时弹“选择你的屏幕分辨率”，写入 `device-profile.json`。
5. 保存 `PM/SUB/FBL/分辨率/编码/字节序/旋转` 到设备页，便于排障。

### 5.3 发帧

- `FrameEncoder`：320×240 画布 → 顺时针 90° → 240×320 → RGB565 LE/BE 或 JPEG → 20 字节头 → 512 对齐；
- 每 512 字节一次 `WinUsb_WritePipe`，短写立即判失败；
- 每帧后 1ms settle；帧率自适应：若单帧写超过预算，自动降到 20/15fps；
- 发送队列容量 1：渲染跟不上时丢旧帧，不累积延迟；
- 静态主题：内容哈希不变则不发帧；每 5 秒发一次 keep-alive（可配置）；
- 断线：写失败 3 次 → 关闭句柄 → 每 2 秒重连 → 恢复当前主题；
- 退出：可选“保留最后一帧 / 黑屏 / 关闭背光（若后续找到命令）”。

### 5.4 硬件探针（M1，必须先做）

`OpenTRCC.Probe` 提供：

```text
probe list                                             # 枚举接口、驱动、bcdDevice、占用状态
probe handshake [--mode auto|standard|streamed]        # 标准握手，失败回落流式；打印注释过的 512B hex
probe pattern   [--rotate 90|270|0|180] [--byte-order le|be]
                [--flip none|h|v|hv] [--mode rgb565|jpeg]
                [--chunk-size 512] [--seconds 10] [--save-png]
probe bench     [--all] [--chunk-size 512|1024|4096|full]
                [--settle-ms 0|1] [--seconds 10]       # --all 跑四档 chunk × 两档 settle
probe render    [--theme themes/pulse-320x240.json] [--png <路径>]
                [--push] [--seconds 10]                # 渲染主题；--push 直接上屏
```

每次运行在 `artifacts/probe-runs/<yyyyMMdd-HHmmss>/` 落盘 `handshake.hex`、`pattern-<配置>.png`、`bench.csv`（fps / B/s / p50·p95 单帧 ms / 短写 / 超时）与 `device-profile.json` 快照；退出码：`0` 正常、`2` 未找到设备、`3` 被占用、`4` 需提权、`5` 握手失败、`6` 写失败。
公共参数：`--index`、`--interface <子串>`、`--transport auto|hid|winusb`、`--report-id <n>`（HID 报告 ID，默认 0）。

测试图：四象限 + 大号 `OpenTRCC` + 当前模式/分辨率/PM/SUB + 移动竖条 + 顶部“↑”方向标记，用来同时验证方向、镜像、颜色、比例和刷新。用户确认哪种组合正常后，把结果固化为 `DeviceProfile`。

**M1 是 Go/No-Go 关卡**：屏幕不亮就不进入 UI 开发，先排查驱动占用/固件分支/字节序/旋转。

---

## 6. 数据层设计

### 6.1 传感器抽象

`SensorDescriptor`：`Id`、显示名、单位、类型（温度/占用/功耗/频率/风扇/吞吐/文本）、来源、精度、是否可用。

`SensorSnapshot`：`Timestamp`、`IReadOnlyDictionary<string, SensorValue>`、每个源的健康状态。

历史：每个数值传感器保留 300 点（1Hz，5 分钟）；FPS 保留 60 秒（10Hz）。

### 6.2 CPU / GPU / 主板 / 存储

**主源：LibreHardwareMonitorLib 0.9.6（需管理员）**

- CPU：包温（AMD Tctl/Tdie）、每核占用/频率、封装功耗、电压；
- GPU：核心/热点/显存温度、核心占用、显存占用/总量、核心/显存频率、风扇转速/百分比、功耗、PCIe 吞吐；
- 主板：VRM/芯片组温度、风扇转速（视主板支持）；
- 内存：占用、时序/温度（视支持）；
- 存储：温度、占用、读写吞吐（视支持）。

**降级源（非管理员或 LHM 被拦）**

- CPU 占用/内存/网络：Win32 / `NetworkInterface` / `GlobalMemoryStatusEx`；
- GPU 占用：Windows “GPU Engine” 性能计数器；
- GPU 温度/功耗：AMD ADL 用户态接口（P2 可补），或 HWiNFO 共享内存（若用户运行 HWiNFO）；
- 温度读不到就显示 `N/A`，并注明“需要管理员/驱动被占用”，不伪造数据。

**多 GPU**：LHM 列出所有适配器，**必须先过滤虚拟/远程显示适配器**（`GameViewer Virtual Display Adapter`、`Idd*`、`RDP*`、`Parsec` 等），只保留 PCI 总线枚举到的适配器；本机实测有 3 个适配器（6750 GRE / 核显 / 虚拟屏），不过滤会导致“默认独显”和每显示器 FPS 映射错乱。默认选独显（按 PCI 总线号/名称），UI 可切换；iGPU 单独显示；SMT 已关的机器每核只有 8 条传感器，界面不要按 16 线程布局。

### 6.3 FPS：任意游戏、任意显示器

**主源：PresentMon 2.6.0 x64（MIT，随包分发；已实测非提权可跑）**

```text
PresentMon-2.6.0-x64.exe --output_stdout --no_console_stats
  --session_name OpenTRCC-FPS --write_display_metadata
```

- 不指定 `--process_id` 即采集所有进程；
- `--write_display_metadata` 输出 `VidPnSourceId`，用 `QueryDisplayConfig` 映射到显示器，实现“每块屏的 FPS”；
- 逐行解析 CSV，按列名取 `ProcessID / SwapChainAddress / VidPnSourceId / MsBetweenPresents / CPUStartTime`；
- 同一进程多 swapchain 去重（取帧数最多或选中显示器的那条），避免叠加层重复计数；
- 前台应用优先：`GetForegroundWindow` → PID；不在忽略列表且有帧就用它；
- 忽略列表：`dwm`、`explorer`、`SearchHost`、`ShellExperienceHost`、`ApplicationFrameHost`（可配置）；
- 支持“固定应用”模式：用户从进程列表选一个，主题绑定 `fps.pinned`；
- **1% low 定义（写死）**：30 秒滑动窗口内的全部帧时间排序取 P99，`FPS = 1000 / P99`；0.1% low 同理（P2）。注意 CapFrameX 一类工具按“每秒取分位再平均”，两种算法可差 2–5 FPS，验收时必须用同一算法对拍；Probe 会把原始帧时间 CSV 落盘供离线比对；
- 2 秒无帧标记 stale，显示 `—`，不显示 0；
- PresentMon 失败（权限/会话冲突）→ 回退 RTSS 共享内存；仍失败则显示不可用原因；
- **不做 DLL 注入**，不碰游戏进程，反作弊安全。

**权限**：已在 Performance Log Users 组；若新登录令牌仍未启用，应用会在需要时提示“以管理员重启”。PresentMon 作为子进程继承权限。

### 6.4 其他数据

- 时间/日期/开机时长/电池；
- 网络：按网卡的上/下行速率，自动忽略虚拟网卡；
- 天气（P2）：Open-Meteo，按城市缓存 10 分钟，可选；
- 媒体（P2）：Windows SMTC 当前播放；
- 自定义数据（P2）：HTTP JSON、WMI、性能计数器、虚拟传感器表达式；
- 脚本（P3）：Jint 沙箱，默认关闭。

---

## 7. 主题与渲染系统

### 7.1 主题 JSON 示例（320×240）

> **编码**：主题 JSON 必须是 **UTF-8**（带不带 BOM 均可）。用 ANSI/GBK 保存会把 `°`、`·` 写成乱码，渲染成豆腐块（本项目实测踩过）。

```json
{
  "schemaVersion": 1,
  "id": "pulse-320x240",
  "name": "Pulse",
  "canvas": {
    "width": 320,
    "height": 240,
    "background": {
      "type": "gradient",
      "colors": ["#0B1020", "#1B2A4A"],
      "angle": 135
    }
  },
  "layers": [
    {
      "type": "ring",
      "id": "cpu-ring",
      "x": 24, "y": 34, "w": 150, "h": 150,
      "value": "cpu.temp.package",
      "min": 20, "max": 100,
      "thickness": 11,
      "track": "#1F2937",
      "fill": ["#5EEAD4", "#F59E0B", "#EF4444"],
      "rules": [
        { "when": "cpu.temp.package >= 85", "fill": "#EF4444" }
      ]
    },
    {
      "type": "text",
      "id": "cpu-temp",
      "x": 24, "y": 84, "w": 150, "h": 46,
      "text": "{cpu.temp.package:0}°",
      "font": { "family": "Bahnschrift", "size": 42, "weight": 600 },
      "color": "#FFFFFF",
      "align": "center"
    },
    {
      "type": "text",
      "id": "fps",
      "x": 196, "y": 42, "w": 108, "h": 44,
      "text": "{fps.current:0}",
      "font": { "family": "Inter", "size": 38, "weight": 700 },
      "color": "#5EEAD4",
      "align": "right"
    },
    {
      "type": "text",
      "id": "fps-label",
      "x": 196, "y": 88, "w": 108, "h": 20,
      "text": "FPS · 1% {fps.low1:0}",
      "font": { "family": "Inter", "size": 12, "weight": 500 },
      "color": "#94A3B8",
      "align": "right"
    },
    {
      "type": "bar",
      "id": "gpu-load",
      "x": 196, "y": 132, "w": 108, "h": 8,
      "value": "gpu.load.core",
      "min": 0, "max": 100,
      "track": "#1F2937",
      "fill": "#F59E0B",
      "radius": 4
    },
    {
      "type": "sparkline",
      "id": "frame-graph",
      "x": 24, "y": 198, "w": 280, "h": 26,
      "source": "fps.current",
      "samples": 60,
      "min": 0, "max": 144,
      "stroke": "#5EEAD4",
      "lineWidth": 1.5
    }
  ]
}
```

### 7.2 图层类型（P1）

| 类型 | 能力 |
|---|---|
| `text` | 混合文本 + `{sensor:format}`、字体/字重/字距/对齐/阴影 |
| `bar` | 水平/垂直、圆角、渐变、轨道色、阈值换色 |
| `ring` | 圆弧仪表、起止角、圆头、渐变、阈值换色 |
| `sparkline` | 历史曲线、自动量程、平滑、填充 |
| `image` | PNG/JPG/WebP/SVG（P2）、fit/contain/stretch/tile、着色 |
| `clock` | 日期/时间格式、时区 |
| `group` | 相对坐标组合，便于复用组件 |

背景：纯色、线性/径向渐变、图片（cover/contain/tile + 模糊 + 暗化 + 着色）、GIF（逐帧解码缓存）。

### 7.3 绑定、规则、动画

- 文本 token：`{sensor.id}`、`{sensor.id:0.0}`、`{time:HH:mm}`；
- 规则：`when` 支持 `>= <= == != && || !` 和括号，首个匹配生效；可控制颜色、可见性、透明度；
- 动画：数值缓动（默认 300ms）、脉冲、滚动字幕、无；P1 只做缓动+脉冲；
- 缺失传感器：渲染 `--`，编辑器里标红并给出“该传感器不可用”的原因。

### 7.4 渲染与编码流水线

1. 取最新 `SensorSnapshot`；
2. `SkiaThemeRenderer` 在 **320×240** 画布上绘制（抗锯齿、圆角、阴影、渐变）；
3. 线序变换：用户旋转 + **90° 顺时针**（面板固定），得到 240×320；
4. `Rgb565Encoder`：BGRA → 小端/大端 RGB565；或 `SKImage.Encode(Jpeg)`（仅 JPEG 面板/探测通过时）；
5. 20 字节头 + 512 对齐（301 块）；
6. 写入队列。

优化：

- 背景/静态图层缓存为 `SKImage`，只重绘动态层；
- 内容哈希不变则跳过 USB；
- 复用 `SKBitmap`/`byte[]`，避免每帧 GC；
- **写入流水线**：HID 输出报告保持 2 个在途 overlapped 请求（`HidTransport.PipelineDepth`），实测 13 → 23fps；深度 ≥2 后饱和，无需再调大；
- **RGB565 量化**：深色（如 `#0B1020`）在 565 下每通道只剩 5–6 bit，视觉上接近纯黑；编辑器与 `probe render` 的预览必须按 565 量化显示，否则“预览好看、上屏发黑”；
- 320×240 单帧渲染目标 <1.5ms，编码 <1.5ms。

---

## 8. 编辑器与 UI

### 8.1 主界面

| 页面 | 内容 |
|---|---|
| 概览 | 设备卡片 + 实时预览（4:3）+ 连接状态 + CPU/GPU/FPS 卡片 + 快捷主题切换 |
| 屏幕 | 分辨率、PM/SUB/FBL、固件、USB 路径、测试图、旋转、亮度、目标帧率、黑屏 |
| 主题 | 主题库缩略图、当前主题、导入/导出、复制、删除 |
| 编辑器 | 画布 + 图层树 + 属性面板 + 数据绑定选择器 |
| 传感器 | 全部传感器树、实时值、来源、健康状态、复制 ID |
| 设置 | 开机自启、托盘、语言、PresentMon 状态、LHM 驱动状态、主题目录、日志级别 |
| 日志 | 实时日志、USB 十六进制调试开关、导出诊断包 |

### 8.2 编辑器交互

- 画布按 2×/3× 缩放，显示安全区、网格、中心线；设备预览保持 4:3；
- 拖拽移动、八向手柄缩放、方向键微调（Shift=10px）、吸附网格/边缘/中心；
- 图层面板：新增、删除、复制、重排、锁定、隐藏；
- 属性面板：位置/尺寸、文本/绑定、字体/颜色、圆角/阴影、阈值规则、动画；
- 数据绑定选择器：搜索传感器树，显示当前值，点击插入；
- 背景：文件选择（不依赖拖放，避免管理员窗口 UIPI 限制）、fit/暗化/模糊；
- 预览：默认用样例数据，可切“实时数据”；可开关“同步到屏幕”；
- 撤销/重做：对主题 JSON 做快照栈（主题很小，简单可靠）；
- 保存：校验 schema → 原子写入 → 热重载渲染器；
- 内置主题只读，“复制后编辑”。

### 8.3 首次运行向导

1. 检测设备（找不到时给出：换线、关 TRCC、装 WinUSB 驱动三步）；
2. 握手 + 显示测试图，问“方向和颜色对吗”；不对则切换旋转/字节序/模式重试；
3. 扫描传感器，显示哪些可用、哪些需要管理员；
4. 选一套 starter 主题，实时预览；
5. 询问是否创建“开机自启（计划任务，最高权限，无 UAC 提示）”；
6. 完成。

### 8.4 视觉风格

- 深色 Fluent（WPF-UI 4.3.0），Mica/亚克力背景，8px 网格；
- 强调色跟随主题主色；中文用微软雅黑 UI，英文/数字用 Inter/Bahnschrift；
- 设备预览带窄边框，状态用 chip；动画 150–200ms，不浮夸；
- 托盘：显示/隐藏、当前主题、暂停发帧、黑屏、退出；
- 主窗口声明 **PerMonitorV2** DPI 感知；编辑器画布按 DIP 计算 2×/3× 缩放，避免高 DPI 下错位。

### 8.5 权限与自启

- 默认 `asInvoker` 启动；检测到未提权时进入“基础模式”（设备/网络/部分 FPS 可用），顶部提示“启用完整传感器”按钮；
- 点击后 `runas` 重启自身；传感器、PresentMon、LHM 在提权进程内运行；
- 首次提权时可选创建计划任务（`schtasks /rl highest /sc onlogon`），之后开机无 UAC；
- 不静默杀 TRCC；检测到设备被占用时提示关闭，或由用户确认后“接管”。

---

## 9. 可靠性、性能、安全

### 9.1 指标（320×240）

| 指标 | 目标 |
|---|---|
| 首帧 | <3s（已装驱动） |
| 稳定帧率 | 30fps ±1；最低可接受 20fps |
| 静态主题 CPU | <0.5% |
| 30fps 动态主题 CPU | <3%（9800X3D） |
| 内存 | <300MB |
| 断线恢复 | <5s |
| 30 分钟烤机 | 0 短写/超时，句柄数不增长 |

### 9.2 失败模式与处理

| 失败 | 检测 | 处理 |
|---|---|---|
| 设备未找到 | 枚举为空 | 向导三步排查；提供驱动安装 |
| 设备被 TRCC 占用 | `CreateFile` 共享冲突 | 提示关闭 TRCC；用户确认后可接管 |
| 握手无响应 | 5s 超时 | 标准/流式两种握手都试；记录 hex |
| 方向/镜像不对 | 用户确认测试图异常 | 切换 90°/270°、flip、字节序 |
| 颜色偏色/花屏 | 测试图异常 | 切换 LE/BE、RGB565/JPEG、检查行对齐 |
| 写入短写/超时 | 返回值校验 | 关闭重连；连续失败降级帧率 |
| USB 拔出 | 写异常 | 2s 重连循环；保留主题 |
| LHM 无权限/被拦 | 异常/空传感器 | 基础模式 + 提示；回退系统计数器 |
| GPU 传感器缺失 | 能力探测 | 显示 N/A；P2 接 ADL/HWiNFO |
| PresentMon 权限失败 | stderr/退出码 | 提示提权；回退 RTSS；显示原因 |
| PresentMon 会话冲突 | 退出码 | 唯一 session 名；重试；不杀别的采集器 |
| 前台应用识别错 | 无帧/非游戏 | 固定应用模式 + 忽略列表 |
| 主题资源缺失 | 加载校验 | 回退内置主题 + 校验报告 |
| 主题包恶意路径 | zip 校验 | 拒绝 `..`/绝对路径/可执行文件/超大文件 |
| 主题包 zip bomb | 解压总量/压缩比/文件数超限 | 拒绝导入并报告（总量 ≤200MB、压缩比 ≤100、文件数 ≤2000） |
| TRCC 正在运行 | `CreateFile` 共享冲突 | 打印 PID 与“关闭 TRCC 后重试”，退出码 3；不杀进程 |
| 固件拒绝 >512B 写入 | bench 写错 / 屏停 logo | 记录 chunk 上限并回落 512B，按实测帧率降档 |
| WinUSB 访问被拒 | `CreateFile` 错误 5 | 提示“需一次 UAC”后重试（预期不发生，实测已绑 WinUSB） |
| 停发后回落 logo | 面板 2–3 s 无帧 | 静态主题保持低频保活发帧（如 2–5 fps），不可“不变就不发” |

### 9.3 稳定性

- 单实例 Mutex；第二次启动激活已有窗口；
- 全局异常捕获 + 崩溃日志 + “安全模式”（禁用主题/传感器逐个排查）；
- 设置/主题原子写入，保留上一份备份；
- USB/传感器/PresentMon 三个看门狗，独立重启；
- 退出时优雅停止 PresentMon 会话，避免残留 ETW session。

### 9.4 安全与合规

- 无网络账号、无遥测；天气/更新必须显式开启；
- 本地 API 只绑 `127.0.0.1` + token（P2）；
- 主题包不执行代码；P3 脚本必须沙箱 + 显式授权；
- 许可证：本项目 MIT；LHM（MPL-2.0）、SkiaSharp（MIT）、PresentMon（MIT）、WPF-UI（MIT）保留 NOTICE；字体只用 OFL/Apache；FFmpeg 用 LGPL 动态链接（P2）；
- 协议实现为独立编写，不复制 GPL 代码；逆向仅用于个人互操作，注意厂商 EULA 风险；
- 名称/免责声明：OpenTRCC 与 Thermalright 无隶属关系；不使用其 logo/官方素材；About 页明确“非官方”。
- 发布物未做 Authenticode 签名，首次运行会触发 SmartScreen / 杀软误报：发布脚本必须写明操作步骤，M6 预留签名位，不把误报当 bug 处理。

---

## 10. 实施计划（P1）

| 阶段 | 内容 | 通过条件 |
|---|---|---|
| **M0 工具链**（0.5d） | 本地 `.dotnet` SDK 9.0.x、sln、5 个项目（Core/Devices/Probe/2×Tests）、中央包管理、build/probe 脚本；空 WPF 窗口推迟到 M4 | `dotnet build -c Release` 0 error；`dotnet test` 全绿 |
| **M1 硬件探针**（1.5d） | HID 枚举/独占/占用检测（WinUSB 兜底）、Type2 握手（标准 + 流式回落）、320×240 Profile、Probe（list/handshake/pattern/bench）、chunk/报告尺寸实验、写入流水线、证据落盘 | **Go/No-Go**：方向/颜色确认 ✅（PM=53、rot90、小端）；**实测 23.2fps（pipeline=2）**，达到 20fps 下限、未到 30fps → 通过 |
| **M2 渲染核心**（2d） | 主题模型/JSON Schema、Skia 渲染器、text/bar/ring/image/sparkline 五类图层、RGB565/JPEG 编码、90° 线序、golden test | 四套 320×240 主题导出 PNG 符合设计；帧长度/头正确。**已完成**：模型/JSON 往返、五类图层、绑定表达式、`probe render` 上屏、**pulse/telemetry/horizon/game 四套主题**、`--live` 实时闭环；实测端到端 **21.5fps**（含渲染） |
| **M3 数据与 FPS**（2d） | LHM 源、PresentMon 源、系统源、聚合/历史、前台应用/1% low/每显示器、虚拟适配器过滤 | 与 HWiNFO/任务管理器对拍在容差内。**已接入**：系统源、LHM（GPU 可用、CPU 温度需管理员）、PresentMon 2.6.0、聚合/历史、保活 `FramePump`（静态 0.9 fps 不回 logo）；待补多 GPU/每显示器映射 |
| **M4 应用壳与实时链路**（1.5d） | WPF-UI 壳、概览/屏幕/传感器/设置/日志、预览、托盘、自启、单实例、PerMonitorV2 | 启动即上屏；托盘可用。**已完成**：四页壳（概览/传感器/设置/日志）、实时预览、主题切换、启停、托盘常驻、单实例、HKCU 自启、PerMonitorV2、app.log 与诊断复制；**传感器页实测 313 个传感器**（LHM 全量 + 系统 + PresentMon，可搜索）。按用户要求**不做拔插重连**（直连机箱） |
| **M5 编辑器**（3d） | 画布、图层树、属性、绑定选择器、背景、规则、撤销/重做、导入导出（含 zip bomb 校验）、四套主题 | 用户 5 分钟完成“自定义背景 + 自定义数据”验收。**MVP 已可用**：2× 画布（自动 fit）、点击选中、拖拽移动、图层增删/复制/上下移、通用属性（X/Y/W/H/透明度）+ 分类型属性（文本/字号/颜色/对齐、值/范围/粗细、采样数、图片 fit）、传感器绑定插入、JSON 快照撤销/重做、保存并实时推屏；**背景编辑已完成**：类型/颜色/角度/图片或 GIF 或视频/程序化动态（9 种预设 + 速度，含剪影人物 ronin/sprinter）/适应/暗化；GIF 用 WPF 解码；视频导入时用 MediaPlayer（MF）逐帧抓取并转成 320×240 JPEG 帧序列（默认 20fps、最长 30s），播放只解 JPEG；未转码的视频仍走实时抓帧兜底。统一成 `IBackgroundFrameProvider`。**待补**：缩放手柄、颜色选择器、规则编辑、导入导出、保存到用户目录（当前写应用目录） |
| **M6 打磨与交付**（1.5d） | 性能、异常、中英、发布/安装脚本、文档、30 分钟烤机 | 验收清单全绿 |

**合计约 12 人日**（原 8.5 人日偏乐观）。M1 未通过不得进入 M2；M5 若超期，先把 `sparkline/clock/group` 推到 P2。

**P2（TRCC 完整对齐）**：视频/GIF 背景增强、投屏、音频可视化、多设备、按游戏切主题、OpenRGB、云主题、TRCC 主题导入研究、更多语言、本地 API/插件。

**P3（超越）**：多协议（Type 3/SCSI/Bulk/LY）、手机远程预览、主题市场、脚本沙箱、AI 主题生成（可选）。

---

## 11. 测试与验收

### 11.1 单元测试

- 握手包/帧头字节级比对（RGB565/JPEG、长度、512 对齐）；
- 320×240：payload 153,600、总帧 154,112、301 块；
- 90° 线序旋转后的像素位置（四角标记）；
- LE/BE RGB565 已知颜色（红/绿/蓝/灰/渐变）；
- `PM→FBL→分辨率/编码/旋转` 全表；
- 主题 JSON 往返、schema 校验、迁移；
- 绑定 token/格式化/规则/缺失传感器；
- FPS 聚合：60fps 合成帧、1% low、重复 swapchain、stale；
- 历史环形缓冲、设置原子写入；
- chunk 大小参数化与末块零填、短写检测；
- `DeviceProfile` JSON 往返、原子写、坏 JSON 回退；
- 握手解析：坏 magic、`resp[12]≠1`、8 字节流式短回复、序列号偏移。

### 11.2 集成测试

- Mock transport：握手 → 多帧 → 校验分块顺序/数量/丢帧策略；
- 模拟拔出/写失败 → 重连；
- 渲染 golden image（320×240，阈值比较，避免字体差异导致误报）；
- 编辑器命令：增删改/撤销重做/序列化 diff。

### 11.3 硬件测试

- `Probe`：list / handshake / pattern / bench；
- 用户目视确认方向、颜色、镜像、比例、刷新；
- 实际游戏 FPS 对比（PresentMon CSV / CapFrameX，若有）；
- 拔插、休眠唤醒、TRCC 占用、重启恢复。

### 11.4 烤机与验收

- 30 分钟 30fps，记录 CPU/内存/句柄/USB 错误；
- 8 小时静态主题，内存增长 <20MB；
- 通过第 2.3 节全部成功标准。

> 当前会话非管理员。M1 设备探针可非提权完成；M3 的 LHM/PresentMon 完整验证需要一次 UAC（或先用合成 CSV/非提权源验证解析逻辑）。

---

## 12. 风险、假设与不做的事

| 风险 | 影响 | 对策 |
|---|---|---|
| 旋转方向/字节序因 PM 不同而变化 | 花屏/倒置 | M1 逐组合探测（90/270、LE/BE、JPEG），测试图自带方向标记 |
| 4.07 固件存在流式分支 | 标准握手失败 | 双握手策略 + 3s settle + 短回复解析 |
| 30fps 下 9k 次 512B 写入达到 USB/固件上限 | 掉帧 | 基准测试；自动降 20fps；静态主题按需发帧 |
| TRCC 独占设备 | 无法打开 | 明确提示；用户确认后接管；不静默杀进程 |
| LHM 对 9800X3D/RX 6750 GRE 覆盖不全 | 缺温度/功耗 | 能力探测；系统计数器/ADL/HWiNFO 回退；显示 N/A |
| PresentMon 权限/控制台延迟/会话冲突 | FPS 不可用或延迟 | 提权提示；唯一 session；RTSS 回退；P2 换 Service API |
| 范围过大 | 交付延期 | P1 严格按验收清单；视频/投屏/音频/RGB 放 P2 |
| 逆向/EULA/许可证 | 法律风险 | 独立实现、不复制 GPL、保留 NOTICE、仅个人互操作 |
| 管理员权限/UAC | 体验差 | 基础模式 + 一次提权 + 计划任务自启 |
| 名称含 TRCC 商标 | 被误解为官方 | About/README 免责声明；不使用官方 logo；仓库注明非官方 |

**假设**

- 目标屏为 320×240 横屏，PM 落在 49/50/51/52/53/58；若不是，探针会读出来并按表切换；
- Windows 10/11 x64；设备固件保持现状；TRCC 可继续用于固件升级；
- 用户接受首次 UAC；屏幕未被其他程序永久占用；
- 30fps 是目标，20fps 是可接受下限。

**明确不做**

- 固件刷写/降级（变砖风险，交回 TRCC）；
- 内核级反作弊绕过、游戏注入；
- GPU 风扇/电压控制（安全风险）；
- P1 的云市场/手机 App/多品牌协议。

---

## 13. 交付物

1. `OpenTRCC/` 完整源码（Core/Devices/Sensors/App/Tests/Probe）；
2. 可运行的 `artifacts/OpenTRCC-<version>-win-x64/`（框架依赖或自包含）；
3. 四套 320×240 内置主题 + 主题 schema + 编辑器；
4. `OpenTRCC.Probe.exe` 硬件诊断工具 + `artifacts/probe-runs/` 证据包（handshake hex / bench CSV / pattern PNG / device-profile）；
5. `docs/PROTOCOL.md`、`docs/THEME_SCHEMA.md`、`docs/ARCHITECTURE.md`、README（安装/排障/主题制作/免责声明）；
6. 安装/卸载脚本（复制、开始菜单、计划任务、可选开机自启）；
7. 测试报告：单测、golden image、硬件探针输出、30 分钟烤机数据。

---

## 参考资料

- [Thermalright 官方 TRCC 下载页（2.1.8，兼容型号列表）](https://www.thermalright.com/support/download/)
- [Peerless Assassin 120 Vision ARGB BLACK 官方规格（2.8" / 320×240 / USB 2.0 9Pin）](https://www.thermalright.com/tw/product/peerless-assassin-120-vision-argb-black/)
- [ComputerBase：Peerless Assassin 120 Vision 2.8" 320×240 报道](https://www.computerbase.de/news/kuehlung/peerless-assassin-120-vision-thermalright-baut-display-kuehler-black-rgb-und-max.95949/)
- [trcc-linux：Thermalright 设备/协议社区逆向项目](https://github.com/Lexonight1/thermalright-trcc-linux)
- [trcc-linux 设备列表（0416:5302 = HID Type 2，含 320×240）](https://github.com/Lexonight1/thermalright-trcc-linux/blob/main/doc/REFERENCE_DEVICES.md)
- [Intel PresentMon（ETW 帧采集，MIT）](https://github.com/GameTechDev/PresentMon)
- [PresentMon 控制台参数（全进程采集、显示器元数据）](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)
- [LibreHardwareMonitor（CPU/GPU/主板传感器）](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

---

*本文档由 OpenTRCC 方案整理，版本 v1.1，2026-10-24。*
