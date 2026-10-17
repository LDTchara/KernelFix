using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Hacknet;

namespace KernelFix;

/// <summary>
/// [EN] High-refresh-rate (int)-truncation fix.
///
/// <para>
/// Vanilla exes ramp <c>ramCost</c> with <c>int num = (int)(t * rate)</c>. At high refresh
/// rates (e.g. 240 Hz, <c>t = 1/240</c>, <c>rate = 200</c>) that product is &lt; 1 and gets
/// truncated to <b>0</b>, so the RAM ramp simply never happens (and the same truncation
/// corrupts every other frame-based per-second step in those exes).
/// </para>
/// <para>
/// This Postfix accumulates the fractional remainder per instance and, once a whole
/// megabyte has been collected, applies the original step in the same direction the
/// vanilla code would have gone. It is deliberately conservative:
/// </para>
/// <list type="bullet">
///   <item>It never touches an exe whose <c>isExiting</c> is set — the vanilla
///     <c>ExeModule.Update</c> fade owns <c>ramCost</c> during the exit animation.</item>
///   <item>It never writes <c>os.ramAvaliable</c>. Vanilla recomputes that field from
///     the sum of all <c>ramCost</c> at the start of every frame; writing it here
///     would desynchronise the accounting (and a negative value would then silently
///     disable every later cap check).</item>
///   <item>If the target cannot be read, or the available RAM is unknown / smaller than
///     the step, it does nothing this frame — exactly what the vanilla
///     <c>if (os.ramAvaliable &gt;= num)</c> guard does.</item>
/// </list>
///
/// [CN] 高刷新率下 (int) 截断的修复。
///
/// <para>
/// 原版这些程序用 <c>int num = (int)(t * rate)</c> 爬 RAM。高刷新率下（例如 240Hz，
/// <c>t = 1/240</c>、<c>rate = 200</c>）乘积小于 1 被截断成 <b>0</b>，于是 RAM 永远不涨
/// （同一截断也会让这些程序里其它"每秒推进"的按帧计时变慢）。
/// </para>
/// <para>
/// 本 Postfix 按实例累积小数部分，攒够 1MB 后按原版会走的方向补上一步。刻意保持保守：
/// </para>
/// <list type="bullet">
///   <item>**不碰正在退场的程序**（<c>isExiting</c>）：退场时 <c>ramCost</c> 归原版
///     <c>ExeModule.Update</c> 的淡出逻辑管。</item>
///   <item>**从不写 <c>os.ramAvaliable</c>**：原版每帧开头都会用它重算
///     <c>totalRam - Σ ramCost</c>；这里写会打乱账本，一旦写成负值还会让后续所有
///     上限检查被静默跳过（实测出现过 2480/761 的越界报告）。</item>
///   <item>目标读不到、或可用内存未知/不够一步，这一帧就**什么都不做** —— 与原版
///     <c>if (os.ramAvaliable &gt;= num)</c> 的护栏等价。</item>
/// </list>
///
/// Covered / 覆盖:
///   ForkBombExe · DLCTraceSlower · SequencerExe ·
///   ExtensionSequencerExe · ShellExe · NotesExe
/// </summary>
internal static class RamTruncationFix
{
    // ===================================================================
    //  [EN] Per-instance accumulators — GC-safe via ConditionalWeakTable
    //  [CN] 每实例独立累积器，GC 安全
    // ===================================================================
    private static readonly ConditionalWeakTable<ExeModule, AccumState> _accums = new();

    private sealed class AccumState { public float Frac; }

    /// <summary>
    /// [EN] Diagnostic trace (off by default, <c>EnableRamTruncationTrace</c>).
    /// [CN] 诊断追踪（默认关，<c>EnableRamTruncationTrace</c>）。
    /// </summary>
    public static bool Trace;

    private static int _traceRows;

    // ===================================================================
    //  [EN] RAM change rate table (MB / sec) — same constants the vanilla
    //       Update methods use, so the compensation matches vanilla speed.
    //  [CN] 速率表（MB/秒）—— 与原版 Update 里用的常量一致，补偿速度才对得上。
    // ===================================================================
    private static readonly Dictionary<Type, float> _rates = new()
    {
        { typeof(ForkBombExe),          150f },
        { typeof(DLCTraceSlower),        200f },
        { typeof(SequencerExe),          100f },
        { typeof(ExtensionSequencerExe), 100f },
        { typeof(ShellExe),              200f },
        { typeof(NotesExe),              350f },
    };

    /// <summary>缓存各类型 <c>targetRamUse</c> 私有字段（读不到返回 null）。</summary>
    private static readonly Dictionary<Type, FieldInfo> _targetFieldCache = new();

    // ===================================================================
    //  Postfix
    // ===================================================================

    public static void Postfix(object __instance, float t)
    {
        // 直接类型访问：不再用反射读 ramCost / os / isExiting。
        // 反射读失败曾被静默当成「没有余量」处理，等于关掉上限检查。
        if (__instance is not ExeModule exe)
            return;

        Type type = exe.GetType();
        if (!_rates.TryGetValue(type, out float rate))
            return;

        float delta = t * rate;

        // [EN] Vanilla already took a step this frame → reset and stay out of the way.
        // [CN] 原版这帧已经走了一步 → 清零累积、不掺和。
        if ((int)delta > 0)
        {
            _accums.GetOrCreateValue(exe).Frac = 0;
            return;
        }
        if (delta <= 0f)
            return;

        // [EN] Exit animation: ExeModule.Update owns ramCost (baseRamCost * fade).
        // [CN] 退场动画：ramCost 归 ExeModule.Update 的淡出逻辑，别去对抗它。
        if (exe.isExiting)
            return;

        AccumState state = _accums.GetOrCreateValue(exe);
        state.Frac += delta;

        int whole = (int)state.Frac;
        if (whole <= 0)
            return;
        state.Frac -= whole;

        int current = exe.ramCost;
        int target = GetTarget(type, exe);
        // 余量直接按「totalRam − Σ 当前所有 exe 的 ramCost」现算。
        // 不能信 os.ramAvaliable：那个字段每帧只在开头重算一次，同一帧里多个程序
        // 一起涨会读到同一个旧值、一起越过上限（实测 14 个 SignalScramble 同时爬升后
        // 关闭其一 → 771/761）。现算的 Σ 覆盖了淡出中/待移除的程序，不存在滞后。
        int avail = ComputeFreeRam(exe);

        if (current < target)
        {
            // 与原版同款护栏：余量未知（-1 / os 为空）或不够一步 → 本帧不涨。
            if (avail < whole)
            {
                state.Frac = 0;
                return;
            }

            current += whole;
            if (current > target)
                current = target;
        }
        else if (current > target)
        {
            // 下降不需要余量检查（与原版一致）。
            current -= whole;
            if (current < target)
                current = target;
        }
        else
        {
            return;
        }

        if (exe.ramCost == current)
            return;

        if (Trace && _traceRows < 20000)
        {
            _traceRows++;
            try
            {
                KernelFix.Instance.Log.LogInfo(
                    $"[KF] RAM trace: {type.Name} ramCost {exe.ramCost} -> {current} "
                    + $"target={target} whole={whole} avail={avail}");
            }
            catch
            {
                // 追踪本身绝不能影响修复逻辑
            }
        }

        exe.ramCost = current;
    }

    // ===================================================================
    //  Helpers / 辅助
    // ===================================================================

    /// <summary>
    /// [EN] Authoritative free RAM: <c>totalRam - Σ ramCost</c> over the OS's current
    ///      exe list, recomputed on demand. Immune to the once-per-frame
    ///      <c>ramAvaliable</c> cache and to fade/removal timing.
    ///      Returns -1 when the OS is unavailable (caller then skips growth).
    /// [CN] 权威余量：`totalRam − Σ 当前 exe 的 ramCost`，按需现算。
    ///      不受每帧只重算一次的 <c>ramAvaliable</c> 缓存、以及淡出/移除时机影响。
    ///      OS 不可用时返回 -1（调用方据此不涨）。
    /// </summary>
    private static int ComputeFreeRam(ExeModule exe)
    {
        try
        {
            OS os = exe.os;
            if (os == null) return -1;
            int used = 0;
            var list = os.exes;
            for (int i = 0; i < list.Count; i++)
                used += list[i].ramCost;
            return os.totalRam - used;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// [EN] Read the instance's <c>targetRamUse</c> field (private on most of these
    ///      classes; <c>DLCTraceSlower</c> has no field and uses the constant 600).
    ///      Falls back to the current ramCost when unreadable, which makes the
    ///      direction check a no-op — safer than guessing.
    /// [CN] 读实例的 <c>targetRamUse</c>（多数类是私有字段；<c>DLCTraceSlower</c>
    ///      没有该字段，原版写死 600）。读不到时回退当前 ramCost，方向检查变成空操作
    ///      —— 比乱猜安全。
    /// </summary>
    private static int GetTarget(Type type, ExeModule instance)
    {
        if (type == typeof(DLCTraceSlower))
            return 600;

        if (!_targetFieldCache.TryGetValue(type, out FieldInfo fi))
        {
            fi = AccessTools.Field(type, "targetRamUse");
            _targetFieldCache[type] = fi;
        }

        if (fi != null)
        {
            try
            {
                return (int)fi.GetValue(instance);
            }
            catch
            {
                // fall through
            }
        }

        return instance.ramCost;
    }

    // ===================================================================
    //  Registration / 注册入口
    // ===================================================================

    /// <summary>
    /// [EN] Register Postfix patches for all 6 target classes.
    /// [CN] 为 6 个目标类注册 Postfix 修补。
    /// </summary>
    public static void Apply()
    {
        var harmony = KernelFix.Instance.HarmonyInstance;

        var postfixMi = typeof(RamTruncationFix).GetMethod(
            nameof(Postfix),
            BindingFlags.Static | BindingFlags.Public);

        var rows = new (Type type, string method)[]
        {
            (typeof(ForkBombExe),           "Update"),
            (typeof(DLCTraceSlower),        "Update"),
            (typeof(SequencerExe),          "UpdateRamCost"),
            (typeof(ExtensionSequencerExe), "UpdateRamCost"),
            (typeof(ShellExe),              "Update"),
            (typeof(NotesExe),              "Update"),
        };

        foreach (var (type, method) in rows)
        {
            var mi = AccessTools.Method(type, method);
            if (mi == null)
            {
                KernelFix.Instance.Log.LogWarning(
                    $"[KF] RAM: cannot find {type.Name}.{method}, skipping.");
                continue;
            }

            harmony.Patch(mi, postfix: new HarmonyMethod(postfixMi));
            KernelFix.Instance.Log.LogDebug($"[KF] RAM: patched {type.Name}.{method}");
        }
    }
}
