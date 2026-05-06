using System;
using Hacknet;
using Hacknet.Gui;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace KernelFix
{
    // ═══════════════════════════════════════════════════════
    // 0. 拦截 getFilteredKeys，防止英文字母通过该路径进入终端
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(GuiData), "getFilteredKeys")]
    internal class Patch_GetFilteredKeys
    {
        static bool Prefix(ref char[] __result)
        {
            if (IMEManager.IsActive)
            {
                // 清空 TextInputHook 缓冲区，避免残余字符
                GuiData.TextInputHook?.clearBuffer();
                __result = new char[0];
                return false; // 跳过原方法
            }
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════
    // 1. 键盘过滤：保留控制键的长按/重复特性，阻止字符直接输入
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(TextBox), "getFilteredStringInput")]
    internal class Patch_DisableNormalInput
    {
        static bool Prefix(string s, KeyboardState input, KeyboardState lastInput, ref string __result)
        {
            if (!IMEManager.IsActive) return true; // 未激活时走原逻辑

            Keys[] pressedKeys = input.GetPressedKeys();
            bool hasCharKey = false;

            // 第一步：检查是否有任何非控制键被按下
            foreach (Keys key in pressedKeys)
            {
                if (lastInput.IsKeyDown(key)) continue;
                if (!IsControlKey(key))
                {
                    hasCharKey = true;
                    break;
                }
            }

            // 第二步：如果有字符键，我们接管所有键盘处理（包括控制键）
            if (hasCharKey)
            {
                foreach (Keys key in pressedKeys)
                {
                    if (lastInput.IsKeyDown(key)) continue;
                    HandleControlKey(key, ref s);
                }
                __result = s;
                return false; // 跳过原方法，避免字符被直接添加
            }

            // 第三步：全部是控制键，交给原方法处理（保留长按、自动重复等功能）
            return true;
        }

        // 判断是否为控制键（不会产生可打印字符的按键）
        private static bool IsControlKey(Keys key)
        {
            return key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right ||
                   key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown ||
                   key == Keys.Back || key == Keys.Delete || key == Keys.Enter || key == Keys.Escape ||
                   key == Keys.Tab || key == Keys.LeftShift || key == Keys.RightShift ||
                   key == Keys.LeftControl || key == Keys.RightControl ||
                   key == Keys.LeftAlt || key == Keys.RightAlt;
        }

        // 处理控制键（当有字符键按下时，我们也手动实现控制键逻辑）
        private static void HandleControlKey(Keys key, ref string s)
        {
            switch (key)
            {
                case Keys.Tab: TextBox.TabWasPresed = true; break;
                case Keys.Up: TextBox.UpWasPresed = true; break;
                case Keys.Down: TextBox.DownWasPresed = true; break;
                case Keys.Left: TextBox.cursorPosition = Math.Max(0, TextBox.cursorPosition - 1); break;
                case Keys.Right: TextBox.cursorPosition = Math.Min(s.Length, TextBox.cursorPosition + 1); break;
                case Keys.Home: TextBox.cursorPosition = 0; break;
                case Keys.End: TextBox.cursorPosition = s.Length; break;

                case Keys.Back:
                    if (s.Length > 0 && TextBox.cursorPosition > 0)
                    {
                        s = s.Remove(TextBox.cursorPosition - 1, 1);
                        TextBox.cursorPosition--;
                    }
                    break;

                case Keys.Delete:
                    if (s.Length > 0 && TextBox.cursorPosition < s.Length)
                        s = s.Remove(TextBox.cursorPosition, 1);
                    break;

                    // Enter、Escape 等由 Terminal 的其他逻辑处理，此处不拦截
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // 2. 绘制组合文本（光标跟随 + 下划线）
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Terminal), "Draw")]
    internal class Patch_DrawComposition
    {
        static void Postfix(Terminal __instance, float t)
        {
            if (__instance.os?.inputEnabled != true) return;
            string comp = IMEManager.CompositionString;
            if (string.IsNullOrEmpty(comp)) return;

            SpriteFont font = GuiData.tinyfont;
            float charHeight = GuiData.ActiveFontConfig.tinyFontCharHeight;

            // 输入行 Y 坐标（与 doGui 完全一致）
            int num = -4;
            int lineY = (int)((__instance.bounds.Y + __instance.bounds.Height - 16) - charHeight - num) + num;

            // 从光标位置获取已输入字符的宽度
            string before = (TextBox.cursorPosition < __instance.currentLine.Length)
                ? __instance.currentLine.Substring(0, TextBox.cursorPosition)
                : __instance.currentLine;
            float promptWidth = font.MeasureString(__instance.prompt).X;
            float textWidth = promptWidth + font.MeasureString(before).X;

            Vector2 pos = new Vector2(__instance.bounds.X + 3 + Terminal.PROMPT_OFFSET + textWidth, lineY);

            // 绘制组合文本（白色）
            GuiData.spriteBatch.DrawString(font, comp, pos, Color.White);
            // 下划线
            var measure = font.MeasureString(comp);
            GuiData.spriteBatch.Draw(Utils.white, new Rectangle((int)pos.X, (int)(pos.Y + charHeight), (int)measure.X, 1), Color.White);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 3. 绘制候选词列表（自适应宽度 + 半透明背景）
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Terminal), "Draw")]
    internal class Patch_DrawCandidates
    {
        static void Postfix(Terminal __instance, float t)
        {
            try
            {
                var cands = TSFManager.Candidates;
                if (cands == null || cands.Count == 0) return;
                if (__instance.os?.inputEnabled != true) return;

                SpriteFont font = GuiData.tinyfont;
                float charHeight = GuiData.ActiveFontConfig.tinyFontCharHeight;

                /*
                 * 【候选框位置微调说明】
                 * 
                 * 以下两个变量控制候选框左上角的位置：
                 *   xOffset : 相对于终端左边界的偏移（单位：像素）
                 *   yOffset : 相对于终端底边界的向上偏移（单位：像素）
                 * 
                 * 当前设定：
                 *   - 水平方向：离终端左边界 10 像素
                 *   - 垂直方向：紧贴终端底部，向上偏移 20 像素作为边距，再减去候选词列表总高度
                 * 
                 * 如需调整位置，只需修改 xOffset 和 yOffset 后面的数字即可。
                 */

                // 计算候选词列表总宽度（动态适应长词）
                float maxWidth = 0;
                foreach (var c in cands)
                {
                    string text = (cands.IndexOf(c) + 1) + "." + c;
                    float w = font.MeasureString(text).X;
                    if (w > maxWidth) maxWidth = w;
                }
                float listWidth = maxWidth + 20; // 左右各留10像素边距

                // 候选框起点坐标（可根据需要调整）
                int xOffset = 10;                      // 水平偏移
                int yOffset = 32;                      // 底边距
                int x = __instance.bounds.X + xOffset;
                int listHeight = (int)(charHeight * cands.Count);
                int yBase = __instance.bounds.Y + __instance.bounds.Height - yOffset - listHeight;

                // 绘制半透明背景（黑色，透明度 180）
                Rectangle bgRect = new Rectangle(x - 5, yBase - 5, (int)listWidth, listHeight + 10);
                GuiData.spriteBatch.Draw(Utils.white, bgRect, new Color(0, 0, 0, 180));

                // 逐条绘制候选词
                for (int i = 0; i < cands.Count; i++)
                {
                    string text = (i + 1) + "." + cands[i];
                    Color col = (i == TSFManager.CandidateSelection) ? Color.Yellow : Color.White;
                    Vector2 pos = new Vector2(x, yBase + i * charHeight);
                    GuiData.spriteBatch.DrawString(font, text, pos, col);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Patch_DrawCandidates] Error: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // 4. 候选词选择（数字键1-9、空格）
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Terminal), "doGui")]
    internal class Patch_CandidateSelection
    {
        static void Postfix(Terminal __instance)
        {
            if (!IMEManager.IsActive || TSFManager.Candidates.Count == 0) return;

            KeyboardState ks = GuiData.getKeyboadState();
            KeyboardState lastKs = GuiData.getLastKeyboadState();

            // 数字键 1-9 选择候选词
            for (int i = 0; i < Math.Min(9, TSFManager.Candidates.Count); i++)
            {
                if (ks.IsKeyDown(Keys.D1 + i) && lastKs.IsKeyUp(Keys.D1 + i))
                {
                    IMEManager.InjectText(TSFManager.Candidates[i]);
                    ClearCandidates();
                    return;
                }
            }

            // 空格键选择当前高亮候选词
            if (ks.IsKeyDown(Keys.Space) && lastKs.IsKeyUp(Keys.Space))
            {
                int sel = TSFManager.CandidateSelection;
                if (sel >= 0 && sel < TSFManager.Candidates.Count)
                {
                    IMEManager.InjectText(TSFManager.Candidates[sel]);
                    ClearCandidates();
                }
            }
        }

        private static void ClearCandidates()
        {
            TSFManager.Candidates.Clear();
            TSFManager.CandidateSelection = 0;
            IMEManager.CompositionString = "";
        }
    }

    // ═══════════════════════════════════════════════════════
    // 5. 延迟初始化 TSF（等待第一个 Update 帧，确保窗口完全就绪）
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Game1), "Update")]
    internal class Patch_DelayedTSFInit
    {
        static bool tsfinited;
        static void Prefix(Game1 __instance)
        {
            if (tsfinited) return;
            if (__instance.Window?.Handle != IntPtr.Zero)
            {
                TSFManager.Initialize(__instance.Window.Handle);
                tsfinited = true;
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // 6. 每帧更新 TSF 文本输入矩形（供 ImeSharp 内部使用）
    // ═══════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Terminal), "doGui")]
    internal class Patch_UpdateTSFRect
    {
        static void Postfix(Terminal __instance)
        {
            if (__instance.os?.inputEnabled == true)
                TSFManager.UpdateTextInputRect(__instance.bounds);
        }
    }
}