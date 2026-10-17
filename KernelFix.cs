using BepInEx;
using BepInEx.Configuration;
using BepInEx.Hacknet;

namespace KernelFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft dependency: lets BepInEx load PathfinderAPI first so ContentFilePathLocaleFix
    // can resolve Pathfinder.Util.StringExtensions during Load(). Missing Pathfinder is fine.
    // 软依赖：让 BepInEx 先加载 PathfinderAPI，使 ContentFilePathLocaleFix 在 Load() 期间
    // 能解析到 Pathfinder.Util.StringExtensions。未安装 Pathfinder 也不影响加载。
    [BepInDependency("com.Pathfinder.API", BepInDependency.DependencyFlags.SoftDependency)]
    public class KernelFix : HacknetPlugin
    {
        public const string PluginGuid = "com.LDTchara.KernelFix";
        public const string PluginName = "KernelFix";
        public const string PluginVersion = "1.2.5";

        public static ConfigEntry<bool> EnableDPIFix;
        public static ConfigEntry<bool> EnableRamTruncationFix;
        public static ConfigEntry<bool> EnableRamTruncationTrace;
        public static ConfigEntry<bool> EnableIRCDelayFix;
        public static ConfigEntry<bool> EnableLocaleRestoreFix;
        public static ConfigEntry<bool> EnableDHSMissionFix;
        public static ConfigEntry<bool> EnableExtensionTestCleanupFix;
        public static ConfigEntry<bool> EnableContentFilePathLocaleFix;

        // [EN] EXPERIMENTAL, TEST-ONLY display tweaks. Both default to off and neither
        //      is part of KernelFix's regular fix list - see DisplayTweaks.cs.
        // [CN] 实验性显示调整，仅供测试。默认均为关，且不属于 KernelFix 的常规修复
        //      清单 —— 详见 DisplayTweaks.cs。
        public static ConfigEntry<bool> DisableVSync;
        public static ConfigEntry<bool> EnableFpsLimit;
        public static ConfigEntry<int> FpsLimit;

        public static KernelFix Instance { get; private set; }

        public override bool Load()
        {
            Instance = this;

            EnableDPIFix = Config.Bind("General", "EnableDPIFix", true,
                "Enable high-DPI fix. Disable if fullscreen appears too small. / 启用高 DPI 修复。全屏时画面过小可关闭。");
            EnableRamTruncationFix = Config.Bind("General", "EnableRamTruncationFix", true,
                "Fix (int) truncation at high fps for ForkBomb, Sequencer, Shell, etc. / 修复高帧率下 ForkBomb 等的 (int) 截断。");
            EnableRamTruncationTrace = Config.Bind("General", "EnableRamTruncationTrace", false,
                "Diagnostic only: log every ramCost change made by the RAM truncation fix. / 仅诊断：记录 RAM 截断修复每次改 ramCost 的细节。");
            EnableIRCDelayFix = Config.Bind("General", "EnableIRCDelayFix", true,
                "Fix SAAddIRCMessage negative-delay timestamps. Disable to restore vanilla future-message behavior. / 修复 IRC 负延迟时间戳。关闭以恢复原版的未来消息行为。");
            EnableLocaleRestoreFix = Config.Bind("General", "EnableLocaleRestoreFix", true,
                "Restore the main-game language after leaving an extension. / 退出扩展后恢复主游戏语言。");
            EnableDHSMissionFix = Config.Bind("General", "EnableDHSMissionFix", true,
                "Fix DHS (Labyrinths contract hub) stuck missions and NullReference crash after completing a contract under Pathfinder. / 修复 Pathfinder 下 DHS 合约完成后任务卡住与 NullReference 崩溃。");
            EnableExtensionTestCleanupFix = Config.Bind("General", "EnableExtensionTestCleanupFix", true,
                "Clear the stale OS.currentInstance left behind by the editor's extension verification tests (which breaks main-menu text input in IME plugins). / 清理编辑器扩展验证测试残留的 OS.currentInstance（该残留会让 IME 插件在主菜单吞掉输入）。");
            EnableContentFilePathLocaleFix = Config.Bind("General", "EnableContentFilePathLocaleFix", true,
                "Restore localization for content paths built by Pathfinder's ContentFilePath (ActionScripts, mission goals, themes), which otherwise read the English original even when a localized file exists. / 恢复 Pathfinder 的 ContentFilePath 所构造内容路径的本地化（ActionScript、任务 goals、主题），否则即使存在本地化文件也会读取英文原文件。");

            // [EN] Experimental (test-only) display tweaks. The switches are off by
            //      default, and nothing is patched unless one of them is enabled.
            // [CN] 实验性（仅供测试）显示调整。开关默认关闭，未开启时不会添加任何补丁。
            DisableVSync = Config.Bind("Experimental", "DisableVSync", false,
                "TEST ONLY. Disable vertical sync by forcing swapInterval = 0 after every graphics device change. The game then renders as fast as it can: this removes the frame-rate ceiling (frame-based timers run faster too) and causes screen tearing. Combine with EnableFpsLimit to cap the frame rate. Off by default. / 仅供测试。取消垂直同步：每次图形设备变更后强制 swapInterval = 0，游戏将以最大速度渲染 —— 帧率不再有上限（按帧推进的计时也会变快），并出现画面撕裂。建议配合 EnableFpsLimit 限制帧率。默认关闭。");
            EnableFpsLimit = Config.Bind("Experimental", "EnableFpsLimit", false,
                "TEST ONLY. Cap the main loop at FpsLimit frames per second. Without disabling VSync the effective frame rate is still bounded by the display. Off by default. / 仅供测试。把主循环限制在 FpsLimit 帧/秒。未取消垂直同步时，实际帧率仍受显示器约束。默认关闭。");
            FpsLimit = Config.Bind("Experimental", "FpsLimit", 60,
                "TEST ONLY. Frame-rate cap in frames per second for EnableFpsLimit. Values <= 0 disable the limiter. / 仅供测试。EnableFpsLimit 的帧率上限（帧/秒）。<= 0 表示不限帧。");

            if (DisableVSync.Value || EnableFpsLimit.Value) DisplayTweaks.Apply();
            else Log.LogDebug("Experimental display tweaks disabled by config (default).");

            if (EnableDPIFix.Value) DpiFix.Apply();
            else Log.LogDebug("DPI fix disabled by config.");
            RamTruncationFix.Trace = EnableRamTruncationTrace.Value;
            if (EnableRamTruncationFix.Value) RamTruncationFix.Apply();
            else Log.LogDebug("RAM truncation fix disabled by config.");
            IRCFix.Apply();
            if (EnableIRCDelayFix.Value) Log.LogDebug("IRC delay fix active.");
            else Log.LogDebug("IRC delay fix disabled by config.");
            if (EnableLocaleRestoreFix.Value) LocaleRestoreFix.Apply();
            else Log.LogDebug("Locale restore fix disabled by config.");
            if (EnableDHSMissionFix.Value) DHSMissionFix.Apply();
            else Log.LogDebug("DHS mission fix disabled by config.");
            if (EnableExtensionTestCleanupFix.Value) ExtensionTestCleanupFix.Apply();
            else Log.LogDebug("Extension test cleanup fix disabled by config.");
            if (EnableContentFilePathLocaleFix.Value) ContentFilePathLocaleFix.ApplyWithRetry();
            else Log.LogDebug("Content path locale fix disabled by config.");
            OpenALFix.Apply();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[KernelFix] Loaded successfully.");
            Console.ResetColor();
            return true;
        }

        public override bool Unload()
        {
            HarmonyInstance.UnpatchSelf();
            Log.LogDebug("Unloaded.");
            return true;
        }
    }
}
