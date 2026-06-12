using HarmonyLib;
using System;
using System.Reflection;

namespace KernelFix
{
    /// <summary>高帧率 RAM 截断修复 — 仅在原版无法增加时补齐</summary>
    internal static class RamFixPatches
    {
        private static float _accum = 0f;

        /// <summary>原方法运行后，检查是否需要补齐 truncation 损失</summary>
        public static void Postfix(object __instance, float t)
        {
            if (__instance == null) return;
            var tType = __instance.GetType();
            float rate = GetRate(tType);
            float delta = t * rate;

            // 只有原版 (int)delta == 0 时才需要修正
            if ((int)delta > 0) { _accum = 0f; return; }
            if (delta <= 0f) return;

            _accum += delta;
            int add = (int)_accum;
            if (add <= 0) return;
            _accum -= add;

            var f = AccessTools.Field(tType, "ramCost");
            if (f == null) return;
            int current = (int)f.GetValue(__instance);
            // 检查可用内存是否足够
            var osField = AccessTools.Field(tType, "os");
            if (osField != null)
            {
                var os = osField.GetValue(__instance);
                if (os != null)
                {
                    var ramField = AccessTools.Field(os.GetType(), "ramAvaliable");
                    if (ramField != null)
                    {
                        int ramAvail = (int)ramField.GetValue(os);
                        if (ramAvail < add)
                        {
                            // 可用内存不足，不清零（os.runCommand("Completed") 后原版会处理）
                            // 但至少不能超
                            _accum = 0f;
                            return;
                        }
                    }
                }
            }
            current += add;

            // 限制不超过目标值
            string tn = tType.Name;
            if (tn == "DLCTraceSlower")
            {
                if (current > 600) current = 600;
            }
            else
            {
                var tf = AccessTools.Field(tType, "targetRamUse");
                if (tf != null)
                {
                    int target = (int)tf.GetValue(__instance);
                    if (current > target) current = target;
                }
            }

            f.SetValue(__instance, current);
        }

        private static float GetRate(Type t)
        {
            var rf = AccessTools.Field(t, "RAM_CHANGE_PS");
            if (rf != null) return (float)rf.GetValue(null);
            if (t.Name == "ForkBombExe") return 150f;
            if (t.Name == "DLCTraceSlower") return 200f;
            if (t.Name == "ExtensionSequencerExe") return 100f;
            return 100f;
        }
    }
}
