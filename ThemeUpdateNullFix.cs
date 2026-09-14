using System.Reflection;
using Hacknet;
using HarmonyLib;

namespace KernelFix;

/// <summary>
/// [EN] ThemeManager.Update null guard.
/// Vanilla ThemeManager.Update dereferences OS.currentInstance without a null
/// check in its web-refresh branch:
///
///     if (framesTillWebUpdate == -1)
///     {
///         if (OS.currentInstance.connectedComp != null &amp;&amp; ...)   // NRE when null
///
/// framesTillWebUpdate starts at -1 (the method returns early), but
/// switchThemeLayout sets it to 600 whenever the theme/layout changes, after
/// which the branch is evaluated every frame (it re-arms to 0 when no web
/// server is connected). With OS.currentInstance == null — the normal state
/// while sitting in the main menu once an OS session has ended — the next
/// frame throws NullReferenceException and takes the whole game down.
///
/// This fix pins the counter to its safe value (-1) whenever there is no OS
/// instance, so the vanilla method returns early. The hex-grid update at the
/// top of Update still runs, so menu background animation is unaffected.
///
/// [CN] ThemeManager.Update 空引用防御。
/// 原版 ThemeManager.Update 的 web 刷新分支直接解引用 OS.currentInstance，
/// 没有 null 检查：
///
///     if (framesTillWebUpdate == -1)
///     {
///         if (OS.currentInstance.connectedComp != null &amp;&amp; ...)   // 为 null 时 NRE
///
/// framesTillWebUpdate 初值 -1（此时方法提前 return），但 switchThemeLayout 在
/// 主题/布局变化时会把它设为 600，此后该分支每帧都会被求值（没有 web 服务器时
/// 会重新置 0 持续检查）。当 OS.currentInstance == null —— 即一次 OS 会话结束后
/// 停留在主菜单的正常状态 —— 下一帧就抛 NullReferenceException 直接崩游戏。
///
/// 本修复在没有 OS 实例时把计数器按回安全值 -1，让原方法提前 return；
/// Update 开头的 hexGrid 更新仍会执行，主菜单背景动画不受影响。
/// </summary>
internal static class ThemeUpdateNullFix
{
    /// <summary>[EN] ThemeManager.framesTillWebUpdate (private static int). [CN] 私有静态计时器字段。</summary>
    private static FieldInfo _framesTillWebUpdateFi;

    public static void Apply()
    {
        var harmony = KernelFix.Instance.HarmonyInstance;

        _framesTillWebUpdateFi = AccessTools.Field(typeof(ThemeManager), "framesTillWebUpdate");
        if (_framesTillWebUpdateFi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] ThemeUpdate: framesTillWebUpdate not found, skipping.");
            return;
        }

        var mi = AccessTools.Method(typeof(ThemeManager), "Update", new Type[] { typeof(float) });
        if (mi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] ThemeUpdate: ThemeManager.Update(float) not found, skipping.");
            return;
        }

        harmony.Patch(mi,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(ThemeUpdateNullFix), nameof(Prefix))));

        KernelFix.Instance.Log.LogInfo("[KF] ThemeUpdate: patched ThemeManager.Update");
    }

    /// <summary>
    /// [EN] When no OS instance exists, force the web-refresh counter to its
    ///      safe value so vanilla returns before touching OS.currentInstance.
    /// [CN] 无 OS 实例时把 web 刷新计时器按回安全值，使原版在访问
    ///      OS.currentInstance 之前提前返回。
    /// </summary>
    public static void Prefix()
    {
        try
        {
            if (OS.currentInstance == null)
                _framesTillWebUpdateFi.SetValue(null, -1);
        }
        catch { }
    }
}
