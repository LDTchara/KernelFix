using BepInEx;
using BepInEx.Configuration;
using BepInEx.Hacknet;

namespace KernelFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class KernelFix : HacknetPlugin
    {
        public const string PluginGuid = "com.LDTchara.KernelFix";
        public const string PluginName = "KernelFix";
        public const string PluginVersion = "1.2.2";

        public static ConfigEntry<bool> EnableDPIFix;
        public static ConfigEntry<bool> EnableRamTruncationFix;
        public static ConfigEntry<bool> EnableIRCDelayFix;
        public static ConfigEntry<bool> EnableLocaleRestoreFix;
        public static ConfigEntry<bool> EnableDHSMissionFix;
        public static KernelFix Instance { get; private set; }

        public override bool Load()
        {
            Instance = this;

            EnableDPIFix = Config.Bind("General", "EnableDPIFix", true,
                "Enable high-DPI fix. Disable if fullscreen appears too small. / 启用高 DPI 修复。全屏时画面过小可关闭。");
            EnableRamTruncationFix = Config.Bind("General", "EnableRamTruncationFix", true,
                "Fix (int) truncation at high fps for ForkBomb, Sequencer, Shell, etc. / 修复高帧率下 ForkBomb 等的 (int) 截断。");
            EnableIRCDelayFix = Config.Bind("General", "EnableIRCDelayFix", true,
                "Fix SAAddIRCMessage negative-delay timestamps. Disable to restore vanilla future-message behavior. / 修复 IRC 负延迟时间戳。关闭以恢复原版的未来消息行为。");
            EnableLocaleRestoreFix = Config.Bind("General", "EnableLocaleRestoreFix", true,
                "Restore the main-game language after leaving an extension. / 退出扩展后恢复主游戏语言。");
            EnableDHSMissionFix = Config.Bind("General", "EnableDHSMissionFix", true,
                "Fix DHS (Labyrinths contract hub) stuck missions and NullReference crash after completing a contract under Pathfinder. / 修复 Pathfinder 下 DHS 合约完成后任务卡住与 NullReference 崩溃。");

            if (EnableDPIFix.Value) DpiFix.Apply();
            else Log.LogDebug("DPI fix disabled by config.");
            if (EnableRamTruncationFix.Value) RamTruncationFix.Apply();
            else Log.LogDebug("RAM truncation fix disabled by config.");
            IRCFix.Apply();
            if (EnableIRCDelayFix.Value) Log.LogDebug("IRC delay fix active.");
            else Log.LogDebug("IRC delay fix disabled by config.");
            if (EnableLocaleRestoreFix.Value) LocaleRestoreFix.Apply();
            else Log.LogDebug("Locale restore fix disabled by config.");
            if (EnableDHSMissionFix.Value) DHSMissionFix.Apply();
            else Log.LogDebug("DHS mission fix disabled by config.");
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
