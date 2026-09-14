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
/// sees a stale OS that still claims to be alive (HasExitedAndEnded == false),
/// because only OS.quitGame ever sets that flag. IME plugins in particular use
/// it to decide whether the terminal is "active", so they blackhole every
/// main-menu keystroke (menu becomes unable to accept text).
/// This fix snapshots the pre-test instance and, afterwards, mirrors what
/// quitGame does for a leftover: mark it exited and keep the reference.
///
/// [CN] 扩展验证测试残留清理。
/// 编辑器"Run Verification Tests"按钮走 ExtensionTests.TestExtensionForRuntime，
/// 该流程会为测试创建一个临时 OS 实例。new OS() 会设置 OS.currentInstance，
/// 但清理路径（CompleteExtensiontesting）只恢复 Settings，从未按原版语义收尾
/// —— 测试 OS 从 ScreenManager 移除后仍被静态字段引用着，且
/// HasExitedAndEnded 仍为 false（只有 OS.quitGame 会置 true），
/// 于是在主菜单读取它的逻辑（尤其是 IME 插件判断"终端是否活跃"）会误判，
/// 吞掉主菜单全部按键（主菜单打不了字）。
/// 本修复在测试前记录原实例，测试后按 quitGame 的方式收尾：
/// 标记为已退出并保留引用。
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
    /// [EN] Mirror OS.quitGame's teardown for the leftover instance: mark it as
    ///      exited and KEEP the reference instead of nulling it.
    ///      Vanilla relies on an implicit invariant — OS.currentInstance stays
    ///      non-null while the game runs (the menu keeps the last, already
    ///      exited OS around) and OS.HasExitedAndEnded tells readers it is dead.
    ///      ThemeManager.Update dereferences OS.currentInstance without a null
    ///      check on that assumption, and any consumer (e.g. IME plugins) is
    ///      expected to test HasExitedAndEnded. Clearing the reference instead
    ///      breaks that invariant and crashes the menu.
    /// [CN] 对遗留实例采用与 OS.quitGame 一致的收尾：标记为「已退出」并**保留引用**，
    ///      而不是置 null。
    ///      原版依赖一个隐含约定 —— 运行期间 OS.currentInstance 保持非 null
    ///      （回主菜单时保留上一个已退出的 OS），由 OS.HasExitedAndEnded 告知
    ///      读取方「它已经死了」。ThemeManager.Update 正是基于这个假设裸解引用
    ///      OS.currentInstance，而各种读取方（如 IME 插件）应当检查
    ///      HasExitedAndEnded。直接清空引用会破坏该约定并使主菜单崩溃。
    /// </summary>
    public static void Postfix(OS __state)
    {
        try
        {
            // Leftover instance (already dropped from the screen list): mark it
            // as exited, exactly like quitGame does.
            // 遗留实例（已从屏幕列表移除）：像 quitGame 那样标记为已退出。
            if (__state != null && !IsScreenLive(__state))
                __state.HasExitedAndEnded = true;

            // Keep the reference — the original never nulls it on quit.
            // 保留引用 —— 原版退出时从不置 null。
            OS.currentInstance = __state;
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
