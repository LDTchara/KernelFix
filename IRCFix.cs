using System;
using Hacknet;
using Hacknet.Daemons.Helpers;
using HarmonyLib;

namespace KernelFix
{
    internal static class IRCFix
    {
        public static void Apply()
        {
            var target = typeof(SAAddIRCMessage).GetMethod("Trigger",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (target != null)
                KernelFix.Instance.HarmonyInstance.Patch(target,
                    prefix: new HarmonyMethod(typeof(IRCFix), nameof(FixBackdate)));
        }

        private static bool FixBackdate(SAAddIRCMessage __instance, object os_obj)
        {
            if (KernelFix.EnableIRCDelayFix?.Value != true)
                return true;

            if (__instance.Delay >= 0f)
                return true;

            var os = (OS)os_obj;
            if (os == null) return true;

            var comp = Programs.getComputer(os, __instance.TargetComp);
            if (comp == null) return true;

            var irc = comp.getDaemon(typeof(IRCDaemon)) as IRCDaemon;
            var sys = irc?.System;
            if (sys == null)
            {
                var dhs = comp.getDaemon(typeof(DLCHubServer)) as DLCHubServer;
                sys = dhs?.IRCSystem;
            }
            if (sys == null) return true;

            // 修正：d = Now + (-60s) = 60s 前
            var d = DateTime.Now + TimeSpan.FromSeconds(__instance.Delay);
            sys.AddLog(__instance.Author, __instance.Message,
                d.Hour.ToString("00") + ":" + d.Minute.ToString("00"));
            return false;
        }
    }
}
