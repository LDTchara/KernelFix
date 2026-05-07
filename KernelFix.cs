using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Hacknet;

namespace KernelFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class KernelFix : HacknetPlugin
    {
        public const string PluginGuid = "com.LDTchara.KernelFix";
        public const string PluginName = "KernelFix";
        public const string PluginVersion = "1.0.2";

        /// <summary>
        /// 调试开关。设为 true 后会在控制台输出详细的输入法状态信息。
        /// </summary>
        public static bool Debug = false;

        public static KernelFix Instance { get; private set; }

        public override bool Load()
        {
            // 1. 在窗口创建前设置 DPI 感知，修复高 DPI 缩放导致的分辨率异常
            SetProcessDPIAware();
            Log.LogDebug("DPI awareness set.");

            // 2. 初始化 SDL 事件过滤器（用于拦截组合文本/最终文字，避免重复）
            IMEManager.Initialize();

            // 3. 应用所有 Harmony 补丁（键盘过滤、自绘候选框、TSF 初始化等）
            HarmonyInstance.PatchAll();
            Log.LogDebug("Patches applied.");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[KernelFix] Loaded successfully.");
            Console.ResetColor();
            return true;
        }

        public override bool Unload()
        {
            IMEManager.Dispose();
            HarmonyInstance.UnpatchSelf();
            Log.LogDebug("Unloaded.");
            return true;
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}