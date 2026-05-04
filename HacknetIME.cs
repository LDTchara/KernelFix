using BepInEx;
using BepInEx.Hacknet;
using System;
using SDL2;

namespace HacknetIME
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class HacknetIME : HacknetPlugin
    {
        public const string PluginGuid = "com.example.hacknetime";
        public const string PluginName = "Hacknet IME Fix";
        public const string PluginVersion = "1.0.0";

        public static HacknetIME Instance { get; private set; }

        public override bool Load()
        {
            Instance = this;
            Console.WriteLine("[HacknetIME] Loading...");

            // 1. 设置 SDL 提示，强制显示系统输入法 UI，关闭内部编辑模式由我们自己处理组合文本
            SDL.SDL_SetHint("SDL_HINT_IME_SHOW_UI", "1");
            SDL.SDL_SetHint("SDL_HINT_IME_INTERNAL_EDITING", "0");
            Console.WriteLine("[HacknetIME] SDL hints set: IME_SHOW_UI=1, IME_INTERNAL_EDITING=0");

            // 2. 初始化 IME 管理器（注册事件过滤器）
            IMEManager.Initialize();
            Console.WriteLine("[HacknetIME] IMEManager initialized.");

            // 3. 应用 Harmony 补丁
            HarmonyInstance.PatchAll();
            Console.WriteLine("[HacknetIME] Harmony patches applied.");

            return true;
        }

        public override bool Unload()
        {
            // 移除事件过滤器
            IMEManager.Dispose();
            HarmonyInstance.UnpatchSelf();
            Console.WriteLine("[HacknetIME] Unloaded.");
            return true;
        }
    }
}