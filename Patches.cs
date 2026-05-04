using Hacknet;
using Hacknet.Gui;
using Hacknet.Input;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL2;
using System;

namespace HacknetIME
{
    /// <summary>
    /// 全屏模式补丁：当用户开启全屏时，强制切换为无边框窗口（防止独占全屏屏蔽 IME）
    /// </summary>
    [HarmonyPatch(typeof(Game1), "setNewGraphics")]
    internal class Patch_FullscreenBorderless
    {
        static bool Prefix(Game1 __instance)
        {
            // 只处理全屏切换
            if (!__instance.graphics.IsFullScreen)
            {
                Console.WriteLine("[Patch_FullscreenBorderless] Windowed mode, no change.");
                return true; // 执行原方法
            }

            Console.WriteLine("[Patch_FullscreenBorderless] Fullscreen requested, converting to borderless window...");

            IntPtr window = __instance.Window.Handle;
            // 关闭真全屏
            SDL.SDL_SetWindowFullscreen(window, 0);
            // 去掉边框
            SDL.SDL_SetWindowBordered(window, SDL.SDL_bool.SDL_FALSE);

            int displayIndex = SDL.SDL_GetWindowDisplayIndex(window);
            if (displayIndex < 0)
            {
                Console.WriteLine("[Patch_FullscreenBorderless] Could not get display index, aborting.");
                return false;
            }

            SDL.SDL_Rect bounds;
            if (SDL.SDL_GetDisplayBounds(displayIndex, out bounds) < 0)
            {
                Console.WriteLine("[Patch_FullscreenBorderless] Could not get display bounds, aborting.");
                return false;
            }

            // 设置窗口尺寸和位置为桌面全屏
            SDL.SDL_SetWindowSize(window, bounds.w, bounds.h);
            SDL.SDL_SetWindowPosition(window, bounds.x, bounds.y);
            __instance.Window.IsBorderlessEXT = true;

            // 调整游戏后备缓冲区为桌面分辨率（如果希望保留用户自选分辨率，可改为用户选择的，但可能会拉伸）
            __instance.graphics.PreferredBackBufferWidth = bounds.w;
            __instance.graphics.PreferredBackBufferHeight = bounds.h;
            __instance.graphics.ApplyChanges();

            Console.WriteLine($"[Patch_FullscreenBorderless] Borderless window set: {bounds.w}x{bounds.h} at ({bounds.x},{bounds.y})");

            // 手动恢复必须的内容加载和屏幕管理（借用原方法部分逻辑）
            __instance.LoadGraphicsContent();
            GuiData.spriteBatch = __instance.sman.SpriteBatch;

            // 如果当前没有屏幕，加载初始屏幕
            if (__instance.sman.GetScreens().Length == 0)
            {
                Console.WriteLine("[Patch_FullscreenBorderless] No screens, loading initial.");
                __instance.LoadInitialScreens();
            }

            // 跳过原方法，因为我们已经完成了职责
            return false;
        }
    }

    /// <summary>
    /// 动态更新 IME 候选窗位置，紧贴终端输入行
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "doGui")]
    internal class Patch_UpdateImeRect
    {
        static void Postfix(Terminal __instance)
        {
            try
            {
                if (__instance.os == null || !__instance.os.inputEnabled)
                    return;

                float charHeight = GuiData.ActiveFontConfig.tinyFontCharHeight;
                int x = __instance.bounds.X + 3 + (int)Terminal.PROMPT_OFFSET;
                int y = (int)(__instance.bounds.Y + __instance.bounds.Height - charHeight * 2);
                int w = __instance.bounds.Width - 6;
                int h = (int)(charHeight * 2);

                SDL.SDL_Rect rect = new() { x = x, y = y, w = w, h = h };
                SDL.SDL_SetTextInputRect(ref rect);

                // 调试输出（频率较高时可注释掉）
                // Console.WriteLine($"[Patch_UpdateImeRect] Rect: ({x},{y},{w},{h})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Patch_UpdateImeRect] Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 在终端绘制组合文本（输入法预览），通常带下划线或高亮
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "Draw")]
    internal class Patch_DrawComposition
    {
        static void Postfix(Terminal __instance, float t)
        {
            try
            {
                if (__instance.os == null || !__instance.os.inputEnabled)
                    return;

                var comp = IMEManager.CompositionString;
                if (string.IsNullOrEmpty(comp)) return;

                SpriteFont font = GuiData.tinyfont;
                // 简单放置在提示符之后（更精确的位置需要计算当前光标偏移）
                float promptWidth = font.MeasureString(__instance.prompt).X;
                Vector2 pos = new Vector2(
                    __instance.bounds.X + 3 + Terminal.PROMPT_OFFSET + promptWidth,
                    __instance.bounds.Y + __instance.bounds.Height - GuiData.ActiveFontConfig.tinyFontCharHeight * 3
                );
                GuiData.spriteBatch.DrawString(font, comp, pos, Color.White);
                // 可选下划线效果
                // GuiData.spriteBatch.Draw(Utils.white, new Rectangle((int)pos.X, (int)(pos.Y + charHeight), (int)font.MeasureString(comp).X, 1), Color.White);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Patch_DrawComposition] Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 可选：禁用内置 TextInputHook 的最终文本处理，防止与我们的过滤器冲突（其实过滤器返回0已经阻止了）
    /// 但以防万一，再加一层保护：清空缓冲区
    /// </summary>
    [HarmonyPatch(typeof(TextInputHook), "OnTextInput")]
    internal class Patch_DisableTextInputHook
    {
        static bool Prefix()
        {
            // 如果我们自己的 IME 活跃，就跳过原始方法
            if (IMEManager.CompositionString != null) // 始终活跃
            {
                Console.WriteLine("[Patch_DisableTextInputHook] Skipping original OnTextInput.");
                return false;
            }
            return true;
        }
    }
}