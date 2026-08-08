using System.Reflection;
using HarmonyLib;
using Hacknet;
using Hacknet.Extensions;
using Hacknet.Localization;
using Hacknet.Screens;

namespace KernelFix;

/// <summary>
/// [EN] QOL: restore the main-game locale when leaving an extension.
/// Vanilla switches to the extension's language on entry (ActivateExtensionPage
/// → LocaleActivator.ActivateLocale(info.Language)) but never restores it on exit —
/// only the save-load path (OS.LanguageCreatedIn) recovers it.
///
/// Fix: remember the pre-extension locale on entry, restore it at both exits:
///   A) ExtensionsMenuScreen.ExitExtensionsScreen  (back from the extension picker)
///   B) OS.quitGame()                               (quit in-extension game; the
///      only exit point for an OS session)
///
/// [CN] QOL：退出扩展时恢复主游戏语言。原版进入扩展时会切换到扩展语言
/// （ActivateExtensionPage → ActivateLocale(info.Language)），但退出时不恢复——
/// 只有读档路径（OS.LanguageCreatedIn）会恢复。
///
/// 修复：进入时记住主语言，两条退出路径都恢复：
///   A) ExtensionsMenuScreen.ExitExtensionsScreen（扩展选择菜单返回）
///   B) OS.quitGame()（扩展游戏内退出；OS 会话结束的唯一出口）
/// </summary>
internal static class LocaleRestoreFix
{
    // 进入扩展前的主游戏语言；null 表示从未进入过扩展
    private static string _previousLocale = null;

    // "Back to Extension List" 检测：缓存字段 + 上一帧详情页状态
    private static readonly FieldInfo _infoToShowFi =
        AccessTools.Field(typeof(ExtensionsMenuScreen), "ExtensionInfoToShow");
    private static bool _showingDetail = false;

    public static void Apply()
    {
        var harmony = KernelFix.Instance.HarmonyInstance;

        // ---- 保存点：进入扩展（ActivateExtensionPage 是语言切换源头）----
        var activate = AccessTools.Method(typeof(ExtensionsMenuScreen), "ActivateExtensionPage");
        if (activate == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] Locale: cannot find ActivateExtensionPage, fix skipped.");
            return;
        }
        harmony.Patch(activate,
            prefix: new HarmonyMethod(
                typeof(LocaleRestoreFix).GetMethod(
                    nameof(SaveLocalePrefix),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));

        // ---- 恢复点 A：扩展菜单内返回主菜单 ----
        var exitScreen = AccessTools.Method(typeof(ExtensionsMenuScreen), "ExitExtensionsScreen");
        if (exitScreen != null)
        {
            harmony.Patch(exitScreen,
                postfix: new HarmonyMethod(
                    typeof(LocaleRestoreFix).GetMethod(
                        nameof(RestoreLocalePostfix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
        }

        // ---- 恢复点 B：扩展游戏内点 X 退出（OS 会话结束）----
        // 挂 OS.quitGame 而非 MainMenu.resetOS：resetOS 在"开始新游戏"委托里
        // 也会被调用（MainMenu.cs StartNewGameForUsernameAndPass），会把
        // _previousLocale 提前消耗掉导致真正退出时失效。
        // quitGame 只在确认退出时触发，是结束 OS 会话的唯一出口。
        var quitGame = AccessTools.Method(typeof(OS), "quitGame");
        if (quitGame != null)
        {
            harmony.Patch(quitGame,
                postfix: new HarmonyMethod(
                    typeof(LocaleRestoreFix).GetMethod(
                        nameof(RestoreLocalePostfix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
        }

        // ---- 恢复点 C：详情页点 "Back to Extension List" 返回列表 ----
        // 原版该按钮是内联代码（直接 ExtensionInfoToShow = null），没有独立方法可挂，
        // 用 Draw 的 postfix 检测状态变化（详情页 → 列表）。
        var draw = AccessTools.Method(typeof(ExtensionsMenuScreen), "Draw");
        if (draw != null && _infoToShowFi != null)
        {
            harmony.Patch(draw,
                postfix: new HarmonyMethod(
                    typeof(LocaleRestoreFix).GetMethod(
                        nameof(CheckBackToListPostfix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
        }

        // ---- 防污染：扩展会话期间写 Settings.txt 时用主语言 ----
        // 原版 writeStatusFile 会把当前 Settings.ActiveLocale 写入 Settings.txt 的
        // defaultLocale（SettingsLoader.cs:174）。扩展期间 ActiveLocale 是扩展语言，
        // 玩家在扩展里保存（SaveFileManager → writeStatusFile）就会污染磁盘，
        // 重启后语言被读回扩展语言。prefix 临时换成主语言写盘，postfix 恢复。
        var writeStatus = AccessTools.Method(typeof(SettingsLoader), "writeStatusFile");
        if (writeStatus != null)
        {
            harmony.Patch(writeStatus,
                prefix: new HarmonyMethod(
                    typeof(LocaleRestoreFix).GetMethod(
                        nameof(ProtectWritePrefix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                postfix: new HarmonyMethod(
                    typeof(LocaleRestoreFix).GetMethod(
                        nameof(ProtectWritePostfix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
        }

        KernelFix.Instance.Log.LogDebug("[KF] Locale restore fix applied.");
    }

    /// <summary>
    /// [EN] Run before ActivateExtensionPage: remember the main-game locale.
    ///      Only save once — the player cannot change language while inside the
    ///      extension picker or an extension session, so the first save is the
    ///      correct main-game locale.
    /// [CN] 在 ActivateExtensionPage 之前执行：记住主游戏语言。只保存一次——
    ///      进入扩展菜单/扩展会话期间玩家无法改语言，首次保存即主语言。
    /// </summary>
    private static void SaveLocalePrefix()
    {
        if (_previousLocale == null)
            _previousLocale = Settings.ActiveLocale;
        _showingDetail = true;
    }

    /// <summary>
    /// [EN] After each Draw: if we were showing an extension detail page and now
    ///      ExtensionInfoToShow became null, the player pressed "Back to Extension
    ///      List" — restore the main locale (still inside the picker).
    /// [CN] 每帧 Draw 之后：若上一帧在扩展详情页而现在 ExtensionInfoToShow 变 null，
    ///      说明玩家点了 "Back to Extension List"，恢复主语言（仍在扩展菜单内）。
    /// </summary>
    private static void CheckBackToListPostfix(ExtensionsMenuScreen __instance)
    {
        bool nowShowing = _infoToShowFi.GetValue(__instance) != null;

        if (_showingDetail && !nowShowing)
            RestoreLocalePostfix();

        _showingDetail = nowShowing;
    }

    /// <summary>
    /// [EN] Restore the main-game locale after leaving an extension.
    ///      LocaleActivator.ActivateLocale also reloads terms + font config, so
    ///      a single call fully resets the locale state.
    /// [CN] 退出扩展后恢复主游戏语言。ActivateLocale 会重载词条与字体配置，
    ///      一次调用即可完整复位语言状态。
    /// </summary>
    private static void RestoreLocalePostfix()
    {
        if (_previousLocale == null)
            return;

        LocaleActivator.ActivateLocale(_previousLocale, Game1.getSingleton().Content);
        _previousLocale = null;
        _showingDetail = false;
    }

    /// <summary>
    /// [EN] Before SettingsLoader.writeStatusFile: if we are inside an extension
    ///      session (_previousLocale != null), temporarily swap ActiveLocale to the
    ///      main-game locale so Settings.txt's defaultLocale is not polluted with
    ///      the extension's language (which would otherwise be read back on restart).
    /// [CN] 在 SettingsLoader.writeStatusFile 之前：若处于扩展会话
    ///      （_previousLocale != null），临时把 ActiveLocale 换成主语言，
    ///      避免 Settings.txt 的 defaultLocale 被扩展语言污染（重启时被读回）。
    /// </summary>
    private static void ProtectWritePrefix(out string __state)
    {
        __state = Settings.ActiveLocale;
        if (_previousLocale != null)
            Settings.ActiveLocale = _previousLocale;
    }

    /// <summary>
    /// [EN] Restore the extension locale after writeStatusFile finished.
    /// [CN] writeStatusFile 写盘完成后恢复扩展语言。
    /// </summary>
    private static void ProtectWritePostfix(string __state)
    {
        Settings.ActiveLocale = __state;
    }
}
