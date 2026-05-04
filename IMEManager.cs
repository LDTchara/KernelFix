using System;
using System.Runtime.InteropServices;
using System.Text;
using Hacknet;
using Hacknet.Gui;
using Microsoft.Xna.Framework;
using SDL2;

namespace HacknetIME
{
    public static class IMEManager
    {
        /// <summary>
        /// 当前正在组合的文本（输入法预览），例如“ni hao”
        /// </summary>
        public static string CompositionString { get; private set; } = "";

        private static SDL.SDL_EventFilter eventFilterDelegate;
        private static IntPtr filterUserdata = IntPtr.Zero;

        public static void Initialize()
        {
            if (eventFilterDelegate != null)
            {
                Console.WriteLine("[IMEManager] Already initialized.");
                return;
            }

            eventFilterDelegate = EventFilter;
            SDL.SDL_AddEventWatch(eventFilterDelegate, filterUserdata);
            Console.WriteLine("[IMEManager] SDL event watch added.");
        }

        public static void Dispose()
        {
            if (eventFilterDelegate != null)
            {
                SDL.SDL_DelEventWatch(eventFilterDelegate, filterUserdata);
                eventFilterDelegate = null;
                Console.WriteLine("[IMEManager] SDL event watch removed.");
            }
        }

        /// <summary>
        /// 事件过滤器回调。返回 0 阻止事件继续传播（例如阻止内置 TextInputHook 接收），返回 1 允许。
        /// </summary>
        private static int EventFilter(IntPtr userdata, IntPtr evtPtr)
        {
            SDL.SDL_Event evt = (SDL.SDL_Event)Marshal.PtrToStructure(evtPtr, typeof(SDL.SDL_Event));
            switch (evt.type)
            {
                case SDL.SDL_EventType.SDL_TEXTEDITING:
                    HandleTextEditing(evt.edit);
                    return 1; // 组合文本事件仍传递，不影响其他处理

                case SDL.SDL_EventType.SDL_TEXTINPUT:
                    HandleTextInput(evt.text);
                    // 重要：返回 0 阻止事件到达 FNA 的 TextInputEXT，避免重复输入
                    return 0;

                default:
                    return 1;
            }
        }

        private static unsafe void HandleTextEditing(SDL.SDL_TextEditingEvent edit)
        {
            // 读取 32 字节内的文本，转为字符串
            byte* textPtr = edit.text;
            int length = 0;
            while (length < 32 && textPtr[length] != 0) length++;
            CompositionString = Encoding.UTF8.GetString(textPtr, length);

            Console.WriteLine($"[IMEManager] TextEditing: \"{CompositionString}\" (start={edit.start}, length={edit.length})");
        }

        private static unsafe void HandleTextInput(SDL.SDL_TextInputEvent input)
        {
            byte* textPtr = input.text;
            int length = 0;
            while (length < 32 && textPtr[length] != 0) length++;
            string finalText = Encoding.UTF8.GetString(textPtr, length);

            Console.WriteLine($"[IMEManager] TextInput: \"{finalText}\"");
            InjectTextToTerminal(finalText);
            CompositionString = "";
        }

        /// <summary>
        /// 将最终确认的文本插入到当前终端的当前行，并更新光标位置。
        /// </summary>
        private static void InjectTextToTerminal(string text)
        {
            try
            {
                OS os = OS.currentInstance;
                if (os == null || os.terminal == null)
                {
                    Console.WriteLine("[IMEManager] No OS/terminal instance, text ignored.");
                    return;
                }

                Terminal terminal = os.terminal;
                // 安全插入到光标位置（.NET Framework 4.7.2 无 Math.Clamp）
                int pos = TextBox.cursorPosition;
                pos = Math.Max(0, Math.Min(pos, terminal.currentLine.Length));
                terminal.currentLine = terminal.currentLine.Insert(pos, text);
                TextBox.cursorPosition = pos + text.Length;

                Console.WriteLine($"[IMEManager] Injected text at pos {pos}, new cursor={TextBox.cursorPosition}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IMEManager] InjectText failed: {ex.Message}");
            }
        }
    }
}