using System;
using System.Reflection;
using HarmonyLib;
using Hacknet;

namespace KernelFix
{
    internal static class ForkbombRamFix
    {
        private static float _accum;

        public static void Apply()
        {
            var asm = typeof(Hacknet.ExeModule).Assembly;
            var types = new[] { "Hacknet.ForkBombExe", "Hacknet.DLCTraceSlower", "Hacknet.ExtensionSequencerExe" };
            foreach (var tn in types)
            {
                var t = asm.GetType(tn);
                if (t == null) { KernelFix.Instance.Log.LogWarning($"Cannot find type {tn}"); continue; }
                var m = AccessTools.Method(t, "Update");
                if (m == null) { KernelFix.Instance.Log.LogWarning($"Cannot find {tn}.Update"); continue; }
                KernelFix.Instance.HarmonyInstance.Patch(m,
                    postfix: new HarmonyMethod(typeof(ForkbombRamFix), nameof(Postfix)));
                KernelFix.Instance.Log.LogDebug($"RAM fix applied to {tn}");
            }
        }

        public static void Postfix(object __instance, float t)
        {
            if (__instance == null) return;
            var tType = __instance.GetType();
            float rate = GetRate(tType);
            float delta = t * rate;

            if ((int)delta > 0) { _accum = 0f; return; }
            if (delta <= 0f) return;

            _accum += delta;
            int add = (int)_accum;
            if (add <= 0) return;
            _accum -= add;

            var f = AccessTools.Field(tType, "ramCost");
            if (f == null) return;
            int current = (int)f.GetValue(__instance);

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
                        if (ramAvail < add) { _accum = 0f; return; }
                    }
                }
            }

            current += add;

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


