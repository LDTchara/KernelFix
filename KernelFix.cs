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
        public const string PluginVersion = "1.1.0";

        public static ConfigEntry<bool> EnableDPIFix;
        public static ConfigEntry<bool> EnableForkbombRamFix;
        public static ConfigEntry<bool> EnableIRCDelayFix;
        public static KernelFix Instance { get; private set; }

        public override bool Load()
        {
            Instance = this;

            EnableDPIFix = Config.Bind("General", "EnableDPIFix", true,
                "Enable high-DPI fix. Disable if fullscreen appears too small. / 启用高 DPI 修复。全屏时画面过小可关闭。");
            EnableForkbombRamFix = Config.Bind("General", "EnableForkbombRamFix", true,
                "Enable high-refresh RAM truncation fix for ForkBomb / SignalScramble / ExtensionSequencer. / 启用高帧率 RAM 截断修复。若想恢复原版行为时可关闭。");
            EnableIRCDelayFix = Config.Bind("General", "EnableIRCDelayFix", true,
                "Fix SAAddIRCMessage negative-delay timestamps. Disable to restore vanilla future-message behavior. / 修复 IRC 负延迟时间戳。关闭以恢复原版的未来消息行为。");
            if (EnableDPIFix.Value) DpiFix.Apply();
            else Log.LogDebug("DPI fix disabled by config.");
            if (EnableForkbombRamFix.Value) ForkbombRamFix.Apply();
            else Log.LogDebug("Forkbomb RAM fix disabled by config.");
            IRCFix.Apply();
            if (EnableIRCDelayFix.Value) Log.LogDebug("IRC delay fix active.");
            else Log.LogDebug("IRC delay fix disabled by config.");
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
