using BepInEx;
using BepInEx.Configuration;
using BepInEx.Hacknet;
using System;
using System.Runtime.InteropServices;

namespace KernelFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class KernelFix : HacknetPlugin
    {
        public const string PluginGuid = "com.LDTchara.KernelFix";
        public const string PluginName = "KernelFix";
        public const string PluginVersion = "1.0.3";

        /// <summary>
        /// 调试开关。设为 true 后会在控制台输出详细的输入法状态信息。
        /// </summary>
        public static bool Debug = false;

        /// <summary>配置项：是否启用 DPI 修复</summary>
        public static ConfigEntry<bool> EnableDPIFix;

        /// <summary>配置项：是否启用中文输入法</summary>
        public static ConfigEntry<bool> EnableIME;

        public static KernelFix Instance { get; private set; }

        public override bool Load()
        {
            // 绑定配置文件（自动生成在 BepInEx/config/com.LDTchara.KernelFix.cfg）
            EnableDPIFix = Config.Bind("General",      // 配置节
                "EnableDPIFix",                         // 配置键
                true,                                   // 默认值
                "是否启用高 DPI 修复。如果你在全屏模式下觉得画面过小，可以关闭此项。");

            EnableIME = Config.Bind("General",
                "EnableIME",
                true,
                "是否启用中文输入法支持（候选框、组合文本等）。");

            // 1. DPI 修复（根据配置决定是否执行）
            if (EnableDPIFix.Value)
            {
                SetProcessDPIAware();
                Log.LogDebug("DPI awareness set.");
            }
            else
            {
                Log.LogDebug("DPI fix disabled by config.");
            }

            // 2. 输入法功能（根据配置决定是否初始化）
            if (EnableIME.Value)
            {
                IMEManager.Initialize();
                HarmonyInstance.PatchAll();
                Log.LogDebug("IME enabled, patches applied.");
            }
            else
            {
                Log.LogDebug("IME disabled by config.");
            }
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