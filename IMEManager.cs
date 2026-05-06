using System;
using System.Runtime.InteropServices;
using System.Text;
using Hacknet;
using Hacknet.Gui;
using SDL2;

namespace KernelFix
{
    /// <summary>
    /// SDL 事件过滤器：拦截 SDL 的文本输入事件，防止与 TSF/ImeSharp 重复。
    /// 在 TSF 模式下（UseTSF = true），最终文字由 TSFManager 回调注入，这里只负责阻止原生的 TextInputHook。
    /// </summary>
    public static class IMEManager
    {
        /// <summary>当前组合文本（输入法预览）</summary>
        public static string CompositionString { get; set; } = "";

        /// <summary>是否启用 TSF 模式（影响 SDL 最终文字处理）</summary>
        public static bool UseTSF = true;

        /// <summary>
        /// 动态判断是否应该接管输入：仅在终端已打开、输入启用且未锁定（如密码模式）时才激活。
        /// 主菜单或其他界面的 TextBox 不受影响。
        /// </summary>
        public static bool IsActive
        {
            get
            {
                var os = OS.currentInstance;
                if (os == null || os.terminal == null) return false;
                return os.inputEnabled && !os.terminal.inputLocked;
            }
        }

        private static SDL.SDL_EventFilter eventFilterDelegate;
        private static IntPtr filterUserdata = IntPtr.Zero;

        public static void Initialize()
        {
            if (eventFilterDelegate != null) return;
            eventFilterDelegate = EventFilter;
            SDL.SDL_AddEventWatch(eventFilterDelegate, filterUserdata);
            if (KernelFix.Debug) Console.WriteLine("[IMEManager] SDL event watch added.");
        }

        public static void Dispose()
        {
            if (eventFilterDelegate != null)
            {
                SDL.SDL_DelEventWatch(eventFilterDelegate, filterUserdata);
                eventFilterDelegate = null;
            }
        }

        /// <summary>
        /// SDL 事件过滤器回调。
        /// - SDL_TEXTEDITING：组合文本，TSF 模式下忽略（由 TSFManager 管理），非 TSF 模式下记录。
        /// - SDL_TEXTINPUT：最终文字，TSF 模式下直接吞掉（由 TSF 回调注入），非 TSF 模式下自己注入。
        /// </summary>
        private static int EventFilter(IntPtr userdata, IntPtr evtPtr)
        {
            var evt = (SDL.SDL_Event)Marshal.PtrToStructure(evtPtr, typeof(SDL.SDL_Event));
            switch (evt.type)
            {
                case SDL.SDL_EventType.SDL_TEXTEDITING:
                    if (!UseTSF)
                        HandleTextEditing(evt.edit);
                    return 0; // 阻止内置 TextInputHook 接收

                case SDL.SDL_EventType.SDL_TEXTINPUT:
                    if (UseTSF)
                        return 0; // TSF 模式下吞掉，避免重复
                    else
                    {
                        HandleTextInput(evt.text);
                        return 0;
                    }

                default:
                    return 1;
            }
        }

        private static unsafe void HandleTextEditing(SDL.SDL_TextEditingEvent edit)
        {
            byte* textPtr = edit.text;
            int length = 0;
            while (length < 32 && textPtr[length] != 0) length++;
            CompositionString = Encoding.UTF8.GetString(textPtr, length);
        }

        private static unsafe void HandleTextInput(SDL.SDL_TextInputEvent input)
        {
            byte* textPtr = input.text;
            int length = 0;
            while (length < 32 && textPtr[length] != 0) length++;
            string final = Encoding.UTF8.GetString(textPtr, length);
            InjectText(final);
        }

        /// <summary>
        /// 将文本注入到终端光标位置。可被 TSFManager 等外部模块调用。
        /// </summary>
        public static void InjectText(string text)
        {
            try
            {
                var os = OS.currentInstance;
                if (os?.terminal == null) return;

                Terminal terminal = os.terminal;
                int pos = Math.Max(0, Math.Min(TextBox.cursorPosition, terminal.currentLine.Length));
                terminal.currentLine = terminal.currentLine.Insert(pos, text);
                TextBox.cursorPosition = pos + text.Length;

                if (KernelFix.Debug)
                    Console.WriteLine($"[IMEManager] Injected \"{text}\" at pos {pos}, cursor={TextBox.cursorPosition}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IMEManager] InjectText failed: {ex.Message}");
            }
        }
    }
}