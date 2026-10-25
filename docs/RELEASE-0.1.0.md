# OpenTRCC 0.1.0

利民 2.8" / 320×240（USB `0416:5302`）小屏的非官方 Windows 上位机首个发布版。

## 下载

- 文件：`OpenTRCC-0.1.0-win-x64.zip`（73.7 MB，自包含单文件，无需安装 .NET）
- SHA-256：`5B15972EE8E82651202EA95ECD36B7D40023F6CFD03579452F462ED155B15BD1`

## 安装

1. 解压到任意目录（例如 `D:\Tools\OpenTRCC`）
2. **先关闭 TRCC**（它独占屏幕 HID 设备）
3. 双击 `OpenTRCC.exe`

## 数据位置

- 主题与导入素材：安装目录 `themes\`（安装目录不可写时回退到 `%LocalAppData%\OpenTRCC\themes`）
- 设置与日志：`%LocalAppData%\OpenTRCC`

## 亮点

- **屏幕**：HID 直连，RGB565 全帧 320×240，实测 23.2 fps 上限、推流约 21 fps
- **传感器**：系统计数器 + LibreHardwareMonitor 全量（实测 313 个）+ PresentMon（任意游戏 FPS、**每显示器 FPS**、1% low）
- **主题**：内置 11 套；背景支持纯色、渐变、图片、**GIF**、**视频**（导入时转 320×240 JPEG 帧序列）、**9 种程序化动态**（aurora / waves / particles / stars / grid / plasma / rain / ronin / sprinter）
- **编辑器**：2× 画布、拖拽、图层增删、分类型属性、传感器绑定、撤销/重做、保存即上屏
- **应用**：Fluent（WPF-UI）+ Mica、实时预览、托盘常驻、单实例、HKCU 自启、日志与诊断复制

## 已知限制

- 静态主题需 1.2 秒保活重发，否则面板 2–3 秒后回落开机 logo
- 视频背景导入耗时约等于视频时长（默认上限 30 秒 / 20 fps，帧序列约 38 MB/分钟）
- 视频素材目前以绝对路径记录，换机需重新选择（素材随主题打包在路线图上）
- CPU 温度/功耗需管理员运行（LHM ring0）；非管理员只显示 GPU 与负载
- 不做拔插重连（屏幕为机箱内直连）
- 固件升级、RGB 灯效、亮度控制仍请使用 TRCC

## 反馈

- Issues：https://github.com/YannFang2002/openTRCC/issues
- 日志：`%LocalAppData%\OpenTRCC\app.log`
