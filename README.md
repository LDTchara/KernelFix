# KernelFix

> **快速导航**： [中文版本](#中文版本) | [English Version](#english-version)

## 中文版本

KernelFix 是一款 **Hacknet** 的 **Pathfinder / BepInEx 全局插件**，修复原版 Hacknet 中遗留的一部分问题。

### ✨ 功能简介

#### 💾 高帧率 RAM 截断修复
- 修复 ForkBomb / SignalScramble / ExtensionSequencer 在高刷新率屏幕（144Hz+）下 RAM 占用停止增加的问题。
- 根因：原版使用 `int num = (int)(t * RAM_CHANGE_PS)`，高帧率下 `t` 变小导致乘积被 `int` 截断为 0。
- 修复：累积小数部分，积满 1 后补偿，并检查 `ramAvaliable` 上限。
- 可在 `BepInEx/config/com.LDTchara.KernelFix.cfg` 中通过 `EnableForkbombRamFix` 开关关闭。

#### 🖥️ 高 DPI 修复
- 在游戏窗口创建前调用 `SetProcessDPIAware()`。
- 避免 Windows 的 DPI 虚拟化，使游戏分辨率列表与实际屏幕一致。
- 消除全屏/窗口模式下的模糊和错误缩放。
- 可通过 `EnableDPIFix` 开关关闭。

#### 🕒 IRC 负延迟时间戳修复
- 修复 `SAAddIRCMessage` 的 `Delay` 为负数时时间戳变为未来的 bug。
- 根因：原版 `d -= TimeSpan.FromSeconds(Delay)` 中负 Delay 变成加未来时间。
- 修复：Prefix 拦截后使用 `d += TimeSpan.FromSeconds(Delay)` 正确回填过去时间戳。
- 可通过 `EnableIRCDelayFix` 开关关闭，关闭后恢复原版未来消息行为。

#### 🔊 OpenAL 枚举兼容（Linux / macOS）
- 在非 Windows 平台上跳过 OpenAL 音频设备枚举，防止启动崩溃。
- 根因：FNA 的 `alcGetString` 在某些容器/无音频环境下挂起或崩溃。
- 无功能性损失（HN 没有音频设备切换功能）。

### 📦 安装方法
1. 确保已安装 **Pathfinder** 框架（它自带了 BepInEx）。
2. 下载 `KernelFix.dll`。
3. 将文件放入游戏目录下的 `BepInEx/plugins/` 文件夹内。
4. 启动游戏，插件即自动生效。

> **无需额外安装 BepInEx** – Pathfinder 已包含所需运行环境。

### ⚠️ 兼容性说明
- 需要 **Pathfinder** 框架，否则插件不会加载。
- 支持 **Steam** 及 **非 Steam** 版本的 Hacknet + Labyrinths。
- **中文输入法功能已迁移至 [HacknetIME](https://github.com/LDTchara/HacknetIME)**。KernelFix 不再包含 IME 输入法支持，如有需要请安装 HacknetIME。

### 🛠️ 自行编译
1. 克隆仓库：
   ```bash
   git clone https://github.com/LDTchara/KernelFix.git
   ```
2. 使用 Visual Studio 打开 `KernelFix.sln`（或通过 `dotnet` 命令行构建）：
   ```bash
   dotnet build KernelFix.sln -c Release
   ```
3. 项目引用（Harmony、BepInEx、FNA 等）指向游戏安装目录下的 `libs` 文件夹，可按需调整。

---

## English Version

KernelFix is a **Pathfinder / BepInEx global plugin** for **Hacknet** that fixes several long-standing bugs in the base game. 

### ✨ Features

#### 💾 High-FPS RAM Truncation Fix
- Fixes ForkBomb / SignalScramble / ExtensionSequencer RAM stalling on high-refresh-rate monitors (144Hz+).
- Root cause: `int num = (int)(t * RAM_CHANGE_PS)` truncates to 0 when `t` becomes small at high frame rates.
- Fix: accumulates fractional remainder, compensates when ≥ 1, and respects `ramAvaliable` cap.
- Toggle via `EnableForkbombRamFix` in `BepInEx/config/com.LDTchara.KernelFix.cfg`.

#### 🖥️ High‑DPI Fix
- Calls `SetProcessDPIAware()` before the game window is created.
- Prevents Windows DPI virtualization, matching the in‑game resolution list to the actual display.
- Eliminates blurry text and improper scaling in both fullscreen and windowed modes.
- Toggle via `EnableDPIFix`.

#### 🕒 IRC Negative Delay Fix
- Fixes `SAAddIRCMessage` timestamps becoming future times when `Delay` is negative.
- Root cause: `d -= TimeSpan.FromSeconds(Delay)` with a negative value adds time instead of subtracting.
- Fix: Prefix intercepts and uses `d += TimeSpan.FromSeconds(Delay)` to correctly backdate the timestamp.
- Toggle via `EnableIRCDelayFix` — disable to restore vanilla future-message behavior.

#### 🔊 OpenAL Enumeration Fix (Linux / macOS)
- Skips OpenAL audio device enumeration on non-Windows platforms to prevent startup crashes.
- Root cause: FNA's `alcGetString` hangs or crashes in containerized or audio-less environments.
- No functional loss (Hacknet does not have an audio device switching feature).
- Always active, no toggle needed.

### 📦 Installation
1. Make sure **Pathfinder** is installed (it bundles BepInEx).
2. Download `KernelFix.dll`.
3. Place it into `BepInEx/plugins/` inside your Hacknet directory.
4. Launch the game – the plugin loads automatically.

> **No separate BepInEx installation is required** – Pathfinder already provides it.

### ⚠️ Compatibility
- Requires **Pathfinder**; the plugin will not load without it.
- Works with both **Steam** and **non‑Steam** versions of Hacknet + Labyrinths.
- **IME support has been moved to [HacknetIME](https://github.com/LDTchara/HacknetIME).** KernelFix no longer includes IME functionality — install HacknetIME if you need it.

### 🛠️ Building from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/LDTchara/KernelFix.git
   ```
2. Open `KernelFix.sln` in Visual Studio (or build with `dotnet`):
   ```bash
   dotnet build KernelFix.sln -c Release
   ```
3. The project references (Harmony, BepInEx, FNA, etc.) point to the `libs` folder inside your Hacknet installation; adjust them if necessary.