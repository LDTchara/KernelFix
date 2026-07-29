using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Hacknet;

namespace KernelFix;

/// <summary>
/// [EN] High-refresh-rate (int) truncation fix.
/// Runs as a Harmony Postfix after every targeted Update() call. When the
/// original code computes (int)(t * rate) and gets 0 at high framerates,
/// this Postfix accumulates the fractional remainder across frames and
/// compensates ramCost in the correct direction (increase / decrease)
/// once enough has been collected. Also enforces ramAvaliable caps.
///
/// [CN] 高刷新率下 (int) 截断的修复。以 Harmony Postfix 形式运行在
/// 每个目标 Update() 之后。原版 (int)(t * rate) 在高帧率下结果为 0
/// 时，此 Postfix 跨帧累积小数部分，攒够后按方向（升/降）补偿
/// ramCost，并遵守 ramAvaliable 上限。
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
    private static readonly ConditionalWeakTable<object, AccumState> _accums = new();
    private sealed class AccumState { public float Frac; }

    // ===================================================================
    //  [EN] Cached reflection handles
    //  [CN] 缓存的反射句柄
    // ===================================================================

    // ramCost is public in ExeModule, accessible via typeof directly
    private static readonly FieldInfo _ramCostFi =
        typeof(ExeModule).GetField("ramCost",
            BindingFlags.Instance | BindingFlags.Public);

    // os/ramAvaliable use AccessTools.Field (same as ForkbombRamFix did) —
    // typeof(X).GetField fails under ConfuserEx protection.
    private static readonly Dictionary<Type, FieldInfo> _osFieldCache = new();
    private static readonly Dictionary<Type, FieldInfo> _ramFieldCache = new();

    // ===================================================================
    //  [EN] RAM change rate table (pixels / sec)
    //  [CN] 速率表（像素/秒）
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

    // ===================================================================
    //  Postfix
    // ===================================================================

    public static void Postfix(object __instance, float t)
    {
        if (__instance == null) return;

        var type = __instance.GetType();
        if (!_rates.TryGetValue(type, out float rate))
            return;

        float delta = t * rate;

        // [EN] Original handled this frame → reset accumulator
        // [CN] 原版已处理 → 重置累积器
        if ((int)delta > 0)
        {
            _accums.GetOrCreateValue(__instance).Frac = 0;
            return;
        }
        if (delta <= 0f)
            return;

        var state = _accums.GetOrCreateValue(__instance);
        state.Frac += delta;

        int whole = (int)state.Frac;
        if (whole <= 0)
            return;
        state.Frac -= whole;

        int current = (int)_ramCostFi.GetValue(__instance);
        int target = GetTarget(type, __instance);

        // Direction check / 方向判断
        if (current < target)
        {
            // Increase / 上升
            int ramAvail = ReadAvailableRam(__instance);
            if (ramAvail >= 0 && ramAvail < whole)
            {
                state.Frac = 0;
                return;
            }

            // [EN] Synchronously deduct from ramAvaliable so that multiple
            //      Postfix calls within the same frame see correct remaining RAM.
            // [CN] 同步扣除 ramAvaliable，同一帧内多个 Postfix 看到正确余量
            if (ramAvail >= 0)
                ConsumeRam(__instance, whole);

            current += whole;
            if (current > target) current = target;
        }
        else if (current > target)
        {
            // Decrease / 下降
            current -= whole;
            if (current < target) current = target;
        }
        // current == target → do nothing, keep accumulator
        // current == target → 不动，保留累积器

        _ramCostFi.SetValue(__instance, current);
    }

    // ===================================================================
    //  Helpers / 辅助
    // ===================================================================

    /// <summary>
    /// [EN] Deduct `amount` from os.ramAvaliable to synchronize cross-Exe RAM tracking.
    /// [CN] 从 os.ramAvaliable 扣除 amount，同步跨 Exe 的 RAM 追踪。
    /// </summary>
    private static void ConsumeRam(object exeInstance, int amount)
    {
        try
        {
            var exeType = exeInstance.GetType();
            if (!_osFieldCache.TryGetValue(exeType, out var osFi))
            {
                osFi = AccessTools.Field(exeType, "os");
                _osFieldCache[exeType] = osFi;
            }
            if (osFi == null) return;
            var os = osFi.GetValue(exeInstance);
            if (os == null) return;

            var osType = os.GetType();
            if (!_ramFieldCache.TryGetValue(osType, out var ramFi))
            {
                ramFi = AccessTools.Field(osType, "ramAvaliable");
                _ramFieldCache[osType] = ramFi;
            }
            if (ramFi == null) return;

            int cur = (int)ramFi.GetValue(os);
            ramFi.SetValue(os, cur - amount);
        }
        catch { }
    }

    /// <summary>
    /// [EN] Read os.ramAvaliable. Returns -1 on failure (skip check).
    /// [CN] 读 os.ramAvaliable，失败返回 -1（不做检查）。
    /// </summary>
    private static int ReadAvailableRam(object exeInstance)
    {
        try
        {
            var exeType = exeInstance.GetType();
            if (!_osFieldCache.TryGetValue(exeType, out var osFi))
            {
                osFi = AccessTools.Field(exeType, "os");
                _osFieldCache[exeType] = osFi;
            }
            if (osFi == null) return -1;

            var os = osFi.GetValue(exeInstance);
            if (os == null) return -1;

            var osType = os.GetType();
            if (!_ramFieldCache.TryGetValue(osType, out var ramFi))
            {
                ramFi = AccessTools.Field(osType, "ramAvaliable");
                _ramFieldCache[osType] = ramFi;
            }
            if (ramFi == null) return -1;

            return (int)ramFi.GetValue(os);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// [EN] Get ramCost target for the given type/instance.
    ///      Returns current value if field not found (no-op branch).
    /// [CN] 获取 ramCost 目标值，未知时返回当前值（不走方向分支）。
    /// </summary>
    private static int GetTarget(Type type, object instance)
    {
        if (type == typeof(DLCTraceSlower))
            return 600;

        var fi = AccessTools.Field(type, "targetRamUse");
        if (fi != null)
            return (int)fi.GetValue(instance);

        // Field not found → fallback to current → current == target → no-op
        return (int)_ramCostFi.GetValue(instance);
    }

    // ===================================================================
    //  Registration / 注册入口
    // ===================================================================

    /// <summary>
    /// [EN] Register Postfix patches for all 6 target classes.
    ///      Skips classes where targetRamUse can't be read.
    /// [CN] 为 6 个目标类注册 Postfix 修补。targetRamUse 不可读的类跳过。
    /// </summary>
    public static void Apply()
    {
        var harmony = KernelFix.Instance.HarmonyInstance;

        var postfixMi = typeof(RamTruncationFix).GetMethod(
            nameof(Postfix),
            BindingFlags.Static | BindingFlags.Public);

        var rows = new (Type type, string method, bool needsTarget)[]
        {
            (typeof(ForkBombExe),          "Update",        true),
            (typeof(DLCTraceSlower),        "Update",        false), // hardcoded 600
            (typeof(SequencerExe),          "UpdateRamCost", true),
            (typeof(ExtensionSequencerExe), "UpdateRamCost", true),
            (typeof(ShellExe),              "Update",        true),
            (typeof(NotesExe),              "Update",        true),
        };

        foreach (var (type, method, needsTarget) in rows)
        {
            if (needsTarget)
            {
                var testFi = AccessTools.Field(type, "targetRamUse");
                if (testFi == null)
                {
                    KernelFix.Instance.Log.LogWarning(
                        $"[KF] RAM: {type.Name}.targetRamUse unreadable, skipping.");
                    continue;
                }
            }

            var mi = AccessTools.Method(type, method);
            if (mi == null)
            {
                KernelFix.Instance.Log.LogWarning(
                    $"[KF] RAM: cannot find {type.Name}.{method}, skipping.");
                continue;
            }

            harmony.Patch(mi,
                postfix: new HarmonyMethod(postfixMi));

            KernelFix.Instance.Log.LogDebug(
                $"[KF] RAM: patched {type.Name}.{method}");
        }
    }
}
