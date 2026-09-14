using System.Reflection;
using Hacknet;
using HarmonyLib;

namespace KernelFix;

/// <summary>
/// [EN] Extension test cleanup fix.
/// The editor's "Run Verification Tests" button goes through
/// ExtensionTests.TestExtensionForRuntime, which builds a temporary OS instance
/// for the test run. new OS() assigns OS.currentInstance, but the teardown path
/// (CompleteExtensiontesting) only restores Settings and never clears that
/// static reference — the test OS stays referenced after it has been removed
/// from the ScreenManager.
/// Anything that reads OS.currentInstance while sitting in the main menu then
/// sees a stale, hidden OS. IME plugins in particular decide whether the
/// terminal is "active" from that reference, so they blackhole every main-menu
/// keystroke (menu becomes unable to accept text).
/// This fix snapshots the pre-test instance and restores it afterwards,
/// falling back to null when the snapshot is null or no longer active.
///
/// [CN] 扩展验证测试残留清理。
/// 编辑器"Run Verification Tests"按钮走 ExtensionTests.TestExtensionForRuntime，
/// 该流程会为测试创建一个临时 OS 实例。new OS() 会设置 OS.currentInstance，
/// 但清理路径（CompleteExtensiontesting）只恢复 Settings，从未清空这个静态
/// 引用 —— 测试 OS 从 ScreenManager 移除后仍被静态字段引用着。
/// 于是任何在主菜单读取 OS.currentInstance 的逻辑都会拿到一个已隐藏的旧实例。
/// 尤其是 IME 插件会据此判断"终端是否活跃"，进而在主菜单吞掉全部按键
/// （主菜单打不了字）。
/// 本修复在测试前记录原实例、测试后恢复：快照为空或已不活跃时置 null。
/// </summary>
internal static class ExtensionTestCleanupFix
{
    /// <summary>
    /// [EN] Patch ExtensionTests.CompleteExtensiontesting (called at the end of
    ///      every test flow) to clear the stale OS reference.
    /// [CN] 修补 ExtensionTests.CompleteExtensiontesting（每个测试流程结束时都会
    ///      调用），清理残留的 OS 引用。
    /// </summary>
    public static void Apply()
    {
        var harmony = KernelFix.Instance.HarmonyInstance;

        // CompleteExtensiontesting is private — resolve by name.
        // CompleteExtensiontesting 是 private —— 按名字解析。
        var mi = AccessTools.Method(typeof(Hacknet.Misc.ExtensionTests), "CompleteExtensiontesting");
        if (mi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] ExtTest: CompleteExtensiontesting not found, skipping.");
            return;
        }

        var flags = BindingFlags.Static | BindingFlags.Public;
        harmony.Patch(mi,
            prefix: new HarmonyMethod(typeof(ExtensionTestCleanupFix).GetMethod(nameof(Prefix), flags)),
            postfix: new HarmonyMethod(typeof(ExtensionTestCleanupFix).GetMethod(nameof(Postfix), flags)));

        KernelFix.Instance.Log.LogInfo("[KF] ExtTest: patched ExtensionTests.CompleteExtensiontesting");
    }

    /// <summary>
    /// [EN] Snapshot the instance that was live before the test teardown.
    /// [CN] 记录测试清理前正在使用的实例。
    /// </summary>
    public static void Prefix(out OS __state)
    {
        __state = OS.currentInstance;
    }

    /// <summary>
    /// [EN] Restore the snapshot when it is still a live screen; otherwise clear
    ///      the reference so nothing reads a hidden test OS afterwards.
    ///      Uses the ScreenManager screen list instead of GameScreen.IsActive:
    ///      RemoveScreen only unloads and drops the screen from the list without
    ///      updating its screenState, so a removed screen never receives Update
    ///      again and its state stays TransitionOn/Active forever.
    /// [CN] 快照仍在屏幕列表中时恢复；否则清空引用，避免后续读到已隐藏的测试 OS。
    ///      用 ScreenManager 屏幕列表而非 GameScreen.IsActive 判断：
    ///      RemoveScreen 只卸载并从列表移除、不更新 screenState，被移除的屏幕
    ///      不再收到 Update，状态永远停在 TransitionOn/Active。
    /// </summary>
    public static void Postfix(OS __state)
    {
        try
        {
            OS.currentInstance = (__state != null && IsScreenLive(__state)) ? __state : null;
        }
        catch
        {
            OS.currentInstance = null;
        }
    }

    /// <summary>
    /// [EN] Whether the screen is still present in its ScreenManager's screen list.
    /// [CN] 该屏幕是否仍在 ScreenManager 的屏幕列表中。
    /// </summary>
    private static bool IsScreenLive(GameScreen screen)
    {
        try
        {
            var sm = screen.ScreenManager;
            if (sm == null) return false;
            var screens = sm.GetScreens();
            for (int i = 0; i < screens.Length; i++)
            {
                if (ReferenceEquals(screens[i], screen)) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
