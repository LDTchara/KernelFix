using System;
using System.Collections.Generic;
using Hacknet;
using ImeSharp;
using Microsoft.Xna.Framework;
using SDL2;

namespace KernelFix
{
    /// <summary>
    /// 管理 ImeSharp 的 TSF 初始化、回调，并提供候选词数据给自绘模块。
    /// </summary>
    public static class TSFManager
    {
        /// <summary>候选词列表</summary>
        public static List<string> Candidates = new List<string>();
        /// <summary>当前高亮候选词索引</summary>
        public static int CandidateSelection = 0;
        /// <summary>是否已成功初始化</summary>
        public static bool Initialized { get; private set; } = false;

        private static IntPtr sdlWindowHandle = IntPtr.Zero;

        /// <summary>
        /// 初始化 TSF。传入 SDL 窗口句柄，内部提取原生 HWND 并调用 ImeSharp。
        /// </summary>
        /// <param name="windowHandle">SDL 窗口句柄（Game1.Window.Handle）</param>
        public static void Initialize(IntPtr windowHandle)
        {
            if (Initialized || windowHandle == IntPtr.Zero) return;
            sdlWindowHandle = windowHandle;

            // 获取原生 Win32 窗口句柄（TSF 必需）
            var wmInfo = new SDL.SDL_SysWMinfo();
            wmInfo.version.major = 2; // SDL2-CS 要求设置版本，否则调用失败
            wmInfo.version.minor = 0;

            if (SDL.SDL_GetWindowWMInfo(sdlWindowHandle, ref wmInfo) == SDL.SDL_bool.SDL_FALSE)
            {
                Console.WriteLine("[TSFManager] SDL_GetWindowWMInfo failed!");
                return;
            }

            IntPtr nativeHwnd = wmInfo.info.win.window;
            if (nativeHwnd == IntPtr.Zero)
            {
                Console.WriteLine("[TSFManager] Native HWND is null!");
                return;
            }

            try
            {
                // 让窗口获得焦点，确保 TSF 可关联
                SDL.SDL_RaiseWindow(sdlWindowHandle);
                SDL.SDL_SetWindowInputFocus(sdlWindowHandle);
                System.Threading.Thread.Sleep(100); // 给系统一点反应时间

                InputMethod.TextInputCallback = OnTextInput;
                InputMethod.TextCompositionCallback = OnTextComposition;

                // showOSImeWindow: false 阻止系统自绘候选窗（由我们自己画）
                InputMethod.Initialize(nativeHwnd, showOSImeWindow: false);
                InputMethod.Enabled = true;

                Initialized = true;
                Console.WriteLine("[TSFManager] ImeSharp TSF initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TSFManager] Initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 最终文字上屏回调。过滤控制字符，只注入可打印文本。
        /// </summary>
        private static void OnTextInput(char character)
        {
            if (char.IsControl(character) || character < 32) return;
            if (KernelFix.Debug) Console.WriteLine($"[TSFManager] TextInput: '{character}'");
            IMEManager.InjectText(character.ToString());
        }

        /// <summary>
        /// 组合文本更新及候选词列表刷新回调。
        /// </summary>
        private static void OnTextComposition(string composition, int cursorPos)
        {
            if (KernelFix.Debug)
                Console.WriteLine($"[TSFManager] Composition: \"{composition}\", cursor={cursorPos}");

            IMEManager.CompositionString = composition;

            // 更新候选词列表
            Candidates.Clear();
            CandidateSelection = InputMethod.CandidateSelection;

            for (int i = 0; i < InputMethod.CandidatePageSize; i++)
            {
                string cand = InputMethod.CandidateList[i].ToString();
                if (!string.IsNullOrEmpty(cand))
                    Candidates.Add(cand);

                if (KernelFix.Debug)
                    Console.WriteLine($"[TSFManager] Candidate[{i}] = \"{cand}\"");
            }

            if (KernelFix.Debug)
                Console.WriteLine($"[TSFManager] Candidate count: {Candidates.Count}, selection: {CandidateSelection}");
        }

        /// <summary>
        /// 每帧更新系统输入法的文本输入矩形（即使候选窗隐藏，也需要正确设置以保证光标对齐）。
        /// </summary>
        public static void UpdateTextInputRect(Rectangle terminalBounds)
        {
            if (!Initialized || sdlWindowHandle == IntPtr.Zero) return;

            // 将终端内坐标转换为屏幕坐标
            SDL.SDL_GetWindowPosition(sdlWindowHandle, out int wx, out int wy);
            int x = wx + terminalBounds.X + 3;
            int y = wy + terminalBounds.Y + terminalBounds.Height - 30;
            InputMethod.SetTextInputRect(x, y, 200, 20);
        }
    }
}