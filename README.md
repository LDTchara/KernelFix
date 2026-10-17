# KernelFix

> **快速导航**： [中文版本](#中文版本) | [English Version](#english-version)

## 中文版本

KernelFix 是一款 **Hacknet** 的 **Pathfinder / BepInEx 全局插件**，修复原版 Hacknet 中遗留的一部分问题。

### ✨ 功能简介

#### 💾 高帧率 (int) 截断修复
- 修复 ForkBomb / DLCTraceSlower / Sequencer / Shell / Notes 在高刷新率屏幕（144Hz+）下 RAM 占用停止增加的问题。
- 根因：原版使用 `int num = (int)(t * RAM_CHANGE_PS)`，高帧率下 `t` 变小导致乘积被 `int` 截断为 0。
- 修复：按实例累积小数部分，积满 1 后按原版方向（升/降）补偿 `ramCost`。
- **不改变原版账本**：补偿前用「`totalRam` − 当前所有程序的 `ramCost`」**现算**真实余量；
  余量不够一步就不涨（与原版 `if (os.ramAvaliable >= num)` 等价）；不写 `os.ramAvaliable`；
  正在退场的程序（`isExiting`）一概不碰，退场动画仍由原版 `ExeModule.Update` 负责。
  这样即使多个程序同时爬升 / 中途关闭窗口，也不会把「已用内存」顶过总内存。
- 诊断：`EnableRamTruncationTrace`（默认关）可把每次 `ramCost` 变更连同 `target`/`avail` 写进日志。
- 总开关：`EnableRamTruncationFix`（默认开）。

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

#### 🌐 退出扩展恢复语言
- 原版进入扩展时会切换到扩展的语言，但退出后不会恢复主游戏语言。
- 修复：进入扩展时记住主语言，在返回扩展列表 / 返回主菜单 / 游戏内退出三条路径上自动恢复。
- 可通过 `EnableLocaleRestoreFix` 开关关闭。

#### 🗂️ DHS 任务卡住与崩溃修复
- 修复在 Pathfinder 下完成 Labyrinths DHS 合约后任务残留卡住、以及再次点击完成触发 `NullReferenceException` 的问题。
- 根因：Pathfinder 的 `AutoClearMissionsOnSingleComplete` 补丁在 `autoClearMissionsOnPlayerComplete="false"` 的节点上，完成任务后会把完整任务列表（含刚完成的任务）恢复进 `ActiveMissions`，而 `os.currentMission` 已被置空 —— 残留任务无法消失，对其他任务点"完成"即红错。
- 修复：完成成功后自动移除已完成任务并重新序列化剩余任务；`os.currentMission` 为空时自动补上被点击的任务，杜绝空引用崩溃。
- 可通过 `EnableDHSMissionFix` 开关关闭。

#### 🧹 扩展验证测试残留清理
- 修复在扩展编辑器点击 **Run Verification Tests** 后，主菜单输入框无法输入的问题。
- 根因：原版测试流程只 `RemoveScreen(测试OS)`，不做 `OS.quitGame` 式收尾 —— 残留实例仍被 `OS.currentInstance` 引用且 `HasExitedAndEnded` 仍为 `false`（“假装还活着”），任何按该状态判断的插件都会误判（如 IME 插件以为终端活跃而吞掉主菜单输入）。
- 修复：测试结束后，对已从屏幕列表移除的残留实例按 `quitGame` 语义收尾（标记 `HasExitedAndEnded = true` 并保留引用，不置 null）。
- 可通过 `EnableExtensionTestCleanupFix` 开关关闭。

#### 🌐 Pathfinder 内容路径本地化修复
- 修复 Pathfinder 下 Labyrinths DLC 部分剧情显示英文（Steam 原版正常显示中文）的问题。
- 根因：Pathfinder 用 `ContentFilePath()` 替换了原版带本地化的路径解析（如 `RunnableConditionalActions.LoadIntoOS`），而 `ContentFilePath()` 只拼接路径前缀、从不查找本地化副本 —— 于是 ActionScript、任务 goals、自定义主题等即使存在中文版也会读取英文原文件。
- 修复：为 `ContentFilePath()` 补上原版语义的本地化查询（无本地化副本时原样返回，扩展模式不受影响）。
- 可通过 `EnableContentFilePathLocaleFix` 开关关闭。

### 🧪 实验性功能（**仅供测试**，默认关闭）

> ⚠️ 下面两项**不属于常规修复**，只是为了排查/对比与帧率相关的行为而提供，**默认均关闭**。它们会改变游戏的帧率与计时表现，请只在测试时开启，测完请改回默认。两项都在配置文件的 `[Experimental]` 段。

#### 🖥️ 取消垂直同步（`DisableVSync`，默认关）
- 原版从不设置 VSync，因此始终继承 FNA 的默认行为：`SynchronizeWithVerticalRetrace = true` + `PresentationInterval.Default`，在 Windows 上会请求**自适应垂直同步**（`swapInterval = -1`，即启动日志里的 `Using EXT_swap_control_tear VSync!`），游戏内没有任何开关可改。
- 开启后：每次图形设备变更（启动、改分辨率、切换全屏）都会强制 `swapInterval = 0`，帧率不再有上限。
- **副作用**：画面会出现撕裂；帧率不再受限后，游戏里**所有按帧推进的计时都会变快**（RAM 消耗即为典型，`EnableRamTruncationFix` 补偿的正是该现象的另一面），可能导致扩展剧情/计时异常。
- 建议配合下面的限帧一起使用；驱动面板（如 NVIDIA 控制面板）的垂直同步设置可能仍然优先于本插件的请求。

#### ⏱️ 帧率限制（`EnableFpsLimit`，默认关 / `FpsLimit`，默认 60）
- 原版 `IsFixedTimeStep = false`，FNA 自身不做任何帧率限制，实际帧率由显示器的垂直同步决定。
- 开启后：主循环（`Game.Tick`）尾部会等待到目标周期，把帧率压在 `FpsLimit` 以内，用于检查与帧率相关的行为。
- `FpsLimit <= 0` 表示不限帧；未取消垂直同步时实际帧率仍受显示器约束。

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

#### 💾 High-FPS (int) Truncation Fix
- Fixes ForkBomb / DLCTraceSlower / Sequencer / Shell / Notes RAM stalling on high-refresh-rate monitors (144Hz+).
- Root cause: `int num = (int)(t * RAM_CHANGE_PS)` truncates to 0 when `t` becomes small at high frame rates.
- Fix: accumulates the fractional remainder per instance and applies the original step in the same direction.
- **Leaves vanilla bookkeeping untouched**: before compensating it recomputes the true free memory as
  `totalRam` − the sum of every current program's `ramCost`; when that is below one step it does nothing
  (equivalent to vanilla's `if (os.ramAvaliable >= num)` guard); it never writes `os.ramAvaliable`, and it
  never touches an exe that is exiting (the vanilla `ExeModule.Update` fade owns that). So even with several
  programs ramping at once — or a window being closed mid-ramp — "used RAM" cannot be pushed past the total.
- Diagnostics: `EnableRamTruncationTrace` (default off) logs every `ramCost` change with `target`/`avail`.
- Toggle the whole fix via `EnableRamTruncationFix` (default on).

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

#### 🌐 Locale Restore on Extension Exit
- Vanilla switches to the extension's language on entry but never restores the main-game locale on exit.
- Fix: remembers the pre-extension locale and restores it on all three exit paths (back to extension list, back to main menu, quit in-extension game).
- Toggle via `EnableLocaleRestoreFix`.

#### 🗂️ DHS Stuck-Mission & Crash Fix
- Fixes stuck missions in the Labyrinths DHS contract hub after completing a contract under Pathfinder, and the `NullReferenceException` when clicking Complete again.
- Root cause: Pathfinder's `AutoClearMissionsOnSingleComplete` patch — on nodes with `autoClearMissionsOnPlayerComplete="false"` — restores the full mission list (including the just-completed mission) into `ActiveMissions` after completion, while `os.currentMission` has already been nulled. The completed mission can never disappear, and clicking Complete on another mission crashes.
- Fix: after a successful completion, removes completed missions and re-serializes the survivors; when `os.currentMission` is null it is filled with the clicked mission, eliminating the null-deref crash.
- Toggle via `EnableDHSMissionFix`.

#### 🧹 Extension Verification-Test Leftover Cleanup
- Fixes the main-menu textboxes accepting no input after clicking **Run Verification Tests** in the extension editor.
- Root cause: the vanilla test flow only calls `RemoveScreen(testOS)` and never performs `OS.quitGame`'s teardown — the leftover stays referenced by `OS.currentInstance` with `HasExitedAndEnded` still `false` ("pretending to be alive"), so any plugin judging by that state is misled (IME plugins, for example, think the terminal is active and swallow every main-menu keystroke).
- Fix: after the tests, finalize any leftover already dropped from the screen list with `quitGame` semantics (set `HasExitedAndEnded = true` and keep the reference — never null it).
- Toggle via `EnableExtensionTestCleanupFix`.

#### 🌐 Pathfinder Content-Path Localization Fix
- Fixes parts of the Labyrinths DLC story showing English under Pathfinder (Steam vanilla shows Chinese correctly).
- Root cause: Pathfinder replaced vanilla's localized path resolution with `ContentFilePath()` (e.g. in `RunnableConditionalActions.LoadIntoOS`), and `ContentFilePath()` only concatenates a prefix — it never looks for a localized copy. ActionScripts, mission goal files and custom themes therefore read the English original even when a localized file exists.
- Fix: append vanilla's localized lookup to `ContentFilePath()` (returns the input unchanged when no localized copy exists; extension mode is unaffected).
- Toggle via `EnableContentFilePathLocaleFix`.

### 🧪 Experimental Features (**TEST ONLY**, off by default)

> ⚠️ These two are **not regular fixes**. They exist to probe and compare frame-rate-dependent behaviour, and **both default to off**. They change the game's frame rate and timing, so enable them only while testing and restore the defaults afterwards. Both live in the `[Experimental]` section of the config file.

#### 🖥️ Disable VSync (`DisableVSync`, default off)
- Vanilla never sets VSync and therefore always inherits FNA's default: `SynchronizeWithVerticalRetrace = true` with `PresentationInterval.Default`, which on Windows requests **adaptive VSync** (`swapInterval = -1`, the `Using EXT_swap_control_tear VSync!` startup log line). The game itself exposes no switch for it.
- When enabled: every graphics device change (startup, resolution change, fullscreen toggle) forces `swapInterval = 0`, removing the frame-rate ceiling.
- **Side effects**: screen tearing, and — with no frame-rate bound — **every frame-based timer in the game runs faster** (RAM consumption is the typical example; `EnableRamTruncationFix` compensates for the other side of the same effect), which can skew extension story/timer behaviour.
- Best paired with the limiter below. A driver-side override (e.g. the NVIDIA control panel) may still take precedence over the plugin's request.

#### ⏱️ Frame Rate Limit (`EnableFpsLimit`, default off / `FpsLimit`, default 60)
- Vanilla runs with `IsFixedTimeStep = false`, so FNA never limits the frame rate — the effective rate is whatever the display's VSync allows.
- When enabled: the main loop (`Game.Tick`) waits for the target period at the end of each frame, capping the rate at `FpsLimit`, which is useful when checking frame-rate-dependent behaviour.
- `FpsLimit <= 0` disables the limiter; without disabling VSync the effective rate stays display-bound.

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

### 📄 License

**MIT-0 (MIT No Attribution)** — see [LICENSE](LICENSE).

You may use, copy, modify, merge, publish, distribute, sublicense and/or sell copies of
this software **without keeping any attribution notice** and without any conditions.
The software is provided "as is", without warranty of any kind.

**MIT-0（MIT 无署名）**——详见 [LICENSE](LICENSE)。

你可以自由使用、复制、修改、合并、发布、分发、再许可和/或销售本软件，
**不需要保留任何署名声明**，也没有任何附加条件。本软件按「原样」提供，不附带任何担保。
