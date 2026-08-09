using System.Collections;
using System.Reflection;
using Hacknet;
using HarmonyLib;

namespace KernelFix;

/// <summary>
/// [EN] DHS (DLCHubServer, Labyrinths contract hub) mission cleanup fix.
/// Pathfinder's BaseGameFixes.AutoClearMissionsOnSingleComplete injects IL that,
/// when AutoClearMissionsOnSingleComplete == false, restores the FULL mission
/// list (including the just-completed mission) into ActiveMissions after a
/// successful completion. Consequences:
///   1. The completed mission stays in the list forever (stuck, no Complete
///      button since DrawMissionPanel early-returns on IsComplete).
///   2. Clicking Complete on another un-claimed mission throws
///      NullReferenceException because os.currentMission was already nulled by
///      the completion path (it is only ever set by PlayerAcceptMission).
/// This fix: (Prefix) supplies the clicked mission as os.currentMission when it
/// is null; (Postfix) after a successful completion with AutoClear == false,
/// removes completed missions from ActiveMissions and re-serializes the
/// survivors so memory and disk stay consistent.
///
/// [CN] DHS（DLCHubServer，Labyrinths 合约中心）任务清理修复。
/// Pathfinder 的 BaseGameFixes.AutoClearMissionsOnSingleComplete 注入 IL：当
/// AutoClearMissionsOnSingleComplete == false 时，完成一次任务后会把完整任务
/// 列表（含刚完成的任务）恢复回 ActiveMissions。后果：
///   1. 已完成任务永远残留在列表里卡住（DrawMissionPanel 对 IsComplete 提前
///      return，连完成按钮都不显示）。
///   2. 对列表中其他未接取任务点"完成"会抛 NullReferenceException —— 因为
///      os.currentMission 在完成路径已被置 null（它只在 PlayerAcceptMission
///      里被设置）。
/// 本修复：（Prefix）当 os.currentMission 为 null 时用被点击的任务补上；
/// （Postfix）AutoClear == false 且完成成功后，从 ActiveMissions 移除已完成
/// 任务并重新序列化剩余任务，保证内存与磁盘一致。
/// </summary>
internal static class DHSMissionFix
{
    // ===================================================================
    //  [EN] Cached reflection handles (resolved once in Apply)
    //  [CN] 缓存的反射句柄（Apply 中解析一次）
    // ===================================================================
    private static Type _hubType;
    private static FieldInfo _autoClearFi;      // DLCHubServer.AutoClearMissionsOnSingleComplete
    private static FieldInfo _activeFi;         // DLCHubServer.ActiveMissions
    private static MethodInfo _reSerializeMi;   // DLCHubServer.ReSerializeActiveMissions
    private static FieldInfo _osFi;             // Daemon.os
    private static FieldInfo _currentMissionFi; // OS.currentMission
    private static FieldInfo _isCompleteFi;     // ClaimableMission.IsComplete
    private static FieldInfo _missionFi;        // ClaimableMission.Mission

    // ===================================================================
    //  Registration / 注册入口
    // ===================================================================

    /// <summary>
    /// [EN] Resolve reflection handles and patch PlayerAttemptCompleteMission.
    ///      Skips gracefully when the DLC types are unavailable.
    /// [CN] 解析反射句柄并修补 PlayerAttemptCompleteMission。
    ///      DLC 类型不可用时优雅跳过。
    /// </summary>
    public static void Apply()
    {
        var harmony = KernelFix.Instance.HarmonyInstance;

        // TypeByName instead of typeof: DLCHubServer is a DLC class that may not
        // be referenced at compile time in every environment.
        // 用 TypeByName 而非 typeof：DLCHubServer 是 DLC 类，某些环境下编译期不可引用。
        _hubType = AccessTools.TypeByName("Hacknet.DLCHubServer");
        if (_hubType == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] DHS: DLCHubServer type not found, skipping.");
            return;
        }

        _autoClearFi = AccessTools.Field(_hubType, "AutoClearMissionsOnSingleComplete");
        _activeFi = AccessTools.Field(_hubType, "ActiveMissions");
        _reSerializeMi = AccessTools.Method(_hubType, "ReSerializeActiveMissions");
        _osFi = AccessTools.Field(_hubType, "os");
        if (_autoClearFi == null || _activeFi == null || _reSerializeMi == null || _osFi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] DHS: DLCHubServer reflection failed, skipping.");
            return;
        }

        _currentMissionFi = AccessTools.Field(typeof(OS), "currentMission");
        if (_currentMissionFi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] DHS: OS.currentMission not found, skipping.");
            return;
        }

        // ClaimableMission is a nested type: "Outer+Inner" name
        // ClaimableMission 是嵌套类型，类型名用 "外层+内层"
        var claimType = AccessTools.TypeByName("Hacknet.DLCHubServer+ClaimableMission");
        if (claimType != null)
        {
            _isCompleteFi = AccessTools.Field(claimType, "IsComplete");
            _missionFi = AccessTools.Field(claimType, "Mission");
        }
        if (_isCompleteFi == null || _missionFi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] DHS: ClaimableMission fields not found, skipping.");
            return;
        }

        var mi = AccessTools.Method(_hubType, "PlayerAttemptCompleteMission");
        if (mi == null)
        {
            KernelFix.Instance.Log.LogWarning("[KF] DHS: PlayerAttemptCompleteMission not found, skipping.");
            return;
        }

        harmony.Patch(mi,
            prefix: new HarmonyMethod(typeof(DHSMissionFix).GetMethod(
                nameof(Prefix), BindingFlags.Static | BindingFlags.Public)),
            postfix: new HarmonyMethod(typeof(DHSMissionFix).GetMethod(
                nameof(Postfix), BindingFlags.Static | BindingFlags.Public)));

        KernelFix.Instance.Log.LogDebug("[KF] DHS: patched DLCHubServer.PlayerAttemptCompleteMission");
    }

    // ===================================================================
    //  Prefix / 前置防御
    // ===================================================================

    /// <summary>
    /// [EN] Null-deref defense: os.currentMission is only set by
    ///      PlayerAcceptMission and nulled on completion. When the player
    ///      clicks Complete on a mission while no mission is active (e.g. the
    ///      remaining list entries after a Pathfinder-restored completion),
    ///      vanilla crashes at os.currentMission.isComplete(...). Supply the
    ///      clicked mission so the check targets the right contract.
    /// [CN] 空引用防御：os.currentMission 只在 PlayerAcceptMission 设置、完成时
    ///      置 null。当玩家在无进行中任务时（例如 Pathfinder 恢复列表后的残留
    ///      条目）点击完成，原版会在 os.currentMission.isComplete(...) 崩溃。
    ///      补上被点击的任务，让检查指向正确的合约。
    /// </summary>
    public static void Prefix(object __instance, object mission)
    {
        if (__instance == null || mission == null) return;
        try
        {
            var os = _osFi.GetValue(__instance);
            if (os == null) return;
            if (_currentMissionFi.GetValue(os) != null) return;

            var m = _missionFi.GetValue(mission);
            if (m == null) return;
            _currentMissionFi.SetValue(os, m);
        }
        catch { }
    }

    // ===================================================================
    //  Postfix / 完成后的清理
    // ===================================================================

    /// <summary>
    /// [EN] After a successful completion with AutoClearMissionsOnSingleComplete
    ///      == false, Pathfinder restored the full mission list. Drop the
    ///      completed entries from ActiveMissions and re-serialize the rest so
    ///      the next ReadActiveMissions (node re-entry) sees the same state.
    ///      When AutoClear == true, vanilla already cleared the list — no-op.
    /// [CN] 当 AutoClearMissionsOnSingleComplete == false 且完成成功后，
    ///      Pathfinder 已恢复完整任务列表。从 ActiveMissions 移除已完成条目，
    ///      并重新序列化剩余任务，保证下次进入节点（ReadActiveMissions）读到
    ///      的状态一致。AutoClear == true 时原版已清空列表 —— 无需处理。
    /// </summary>
    public static void Postfix(object __instance, bool __result)
    {
        if (__instance == null || !__result) return;
        try
        {
            if ((bool)_autoClearFi.GetValue(__instance)) return;

            var list = _activeFi.GetValue(__instance) as IList;
            if (list == null || list.Count == 0) return;

            // Remove completed missions (backwards so indices stay valid)
            // 倒序移除已完成任务，保证索引有效
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var cm = list[i];
                if (cm == null) continue;
                if ((bool)_isCompleteFi.GetValue(cm))
                    list.RemoveAt(i);
            }

            // Persist the surviving missions to disk (contracts folder), because
            // vanilla cleared missionFolder.files during completion.
            // 把剩余任务写回磁盘（contracts 目录）—— 完成时原版已清空该目录。
            _reSerializeMi.Invoke(__instance, null);
        }
        catch { }
    }
}
