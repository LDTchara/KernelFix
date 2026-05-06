# KernelFix

> **快速导航**： [中文版本](#中文版本) | [English Version](#english-version)

## 中文版本

KernelFix 是一款 **Hacknet** 的 **Pathfinder / BepInEx 插件**，提供两大核心功能：

1. **高 DPI 显示修复** – 解决近代高分屏下分辨率拉伸、模糊的问题。
2. **原生中文输入法支持** – 在游戏终端内完美使用 Windows 自带输入法，并自绘候选词窗口。

### ✨ 功能简介

#### 🖥️ 高 DPI 修复
- 在游戏窗口创建前调用 `SetProcessDPIAware()`。
- 避免 Windows 的 DPI 虚拟化，使游戏分辨率列表与实际屏幕一致。
- 消除全屏/窗口模式下的模糊和错误缩放。

#### ⌨️ 中文输入法
- 通过 **ImeSharp** 接入 Windows **TSF (Text Services Framework)**。
- **组合文本（拼音/字母）直接显示在终端光标处**，并带有下划线。
- **候选词列表以半透明面板绘制在终端左下角**，支持长词自动适应宽度。
- 使用 **数字键 1‑9** 或 **空格键** 选词上屏。
- 兼容微软拼音、微软五笔以及绝大多数基于 TSF 的输入法。
- 提供 **调试开关**（`KernelFix.cs` 中的 `KernelFix.Debug`），设为 `true` 可在控制台输出详细的输入法状态。

### 📦 安装方法
1. 确保已安装 **Pathfinder** 框架（它自带了 BepInEx）。
2. 下载 `KernelFix.dll`。
3. 将文件放入游戏目录下的 `BepInEx/plugins/` 文件夹内。
4. 启动游戏，插件即自动生效。

> **无需额外安装 BepInEx** – Pathfinder 已包含所需运行环境。

### ⚠️ 兼容性说明
- **与 `TAXCoreCNfix` 不兼容**，两者同时加载会导致输入冲突。请在使用 KernelFix 前移除 `TAXCoreCNfix`。
- 需要 **Pathfinder** 框架，否则插件不会加载。
- 支持 **Steam** 及 **非 Steam** 版本的 Hacknet + Labyrinths。

### 🛠️ 自行编译
- 使用 Visual Studio 打开 `KernelFix.sln`（或通过 `dotnet` 命令行构建）。
- 项目引用（Harmony、BepInEx、FNA 等）指向游戏安装目录下的 `libs` 文件夹，可按需调整。
- 采用 **Costura.Fody** 将 `ImeSharp` 及其依赖嵌入 DLL，生成的 `KernelFix.dll` 即为独立文件，无需其他附加库。

---

## English Version

KernelFix is a **Pathfinder/BepInEx plugin** for **Hacknet** that delivers two major quality‑of‑life improvements:

1. **High‑DPI display fix** – resolves blurry/stretched resolution on modern high‑DPI screens.
2. **Native Chinese IME support** – enables full Windows IME integration inside the terminal, with on‑screen composition preview and candidate list.

### ✨ Features

#### 🖥️ High‑DPI Fix
- Calls `SetProcessDPIAware()` before the game window is created.
- Prevents Windows from applying DPI virtualization, so the in‑game resolution list matches your actual screen.
- No more blurry text or incorrect fullscreen scaling.

#### ⌨️ Chinese Input Method
- Uses **Windows TSF (Text Services Framework)** via **ImeSharp**.
- Displays **composition text** (pinyin) right at the cursor in the terminal.
- Shows a **self‑drawn candidate list** (e.g. 1.啊 2.阿 …) inside the terminal.
- Select candidates with **number keys (1‑9)** or **Space**.
- Works with Microsoft Pinyin, Microsoft Wubi, and most other TSF‑based IMEs.
- Includes a **debug switch** (`KernelFix.Debug = true/false` in `KernelFix.cs`) to enable/disable verbose console logging.

### 📦 Installation
1. Make sure **Pathfinder** is already installed (it comes with BepInEx).
2. Download the latest `KernelFix.dll`.
3. Place the file into `BepInEx/plugins/` (create the folder if needed).
4. Launch Hacknet – the fix applies automatically.

> **No separate BepInEx installation is required** – Pathfinder already bundles it.

### ⚠️ Compatibility
- **KernelFix is incompatible with `TAXCoreCNfix`**. The two mods hook into the same input systems and will conflict. Please remove `TAXCoreCNfix` before using KernelFix.
- Requires **Pathfinder** (the plugin won’t load without it).
- Works with both the **Steam** and **non‑Steam** versions of Hacknet + Labyrinths.

### 🛠️ Building from source
- Open `KernelFix.sln` in Visual Studio (or build with `dotnet`).
- All required libraries (Harmony, BepInEx, FNA, etc.) are referenced from the game’s `libs` folder; you can point them to your own Hacknet installation.
- The project uses **Costura.Fody** to embed `ImeSharp` and its dependencies, so the output is a single `KernelFix.dll`.