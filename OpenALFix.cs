using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace KernelFix
{
    internal static class OpenALFix
    {
        public static void Apply()
        {
            try
            {
                var hAsm = typeof(Hacknet.ExeModule).Assembly;
                var fnaRef = hAsm.GetReferencedAssemblies().FirstOrDefault(a => a.Name == "FNA");
                if (fnaRef == null) { KernelFix.Instance.Log.LogDebug("FNA not in references."); return; }

                var fnaLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "FNA");
                if (fnaLoaded == null) { KernelFix.Instance.Log.LogDebug("FNA not loaded."); return; }

                var oalType = fnaLoaded.GetType("Microsoft.Xna.Framework.Audio.OpenALDevice");
                if (oalType == null) { KernelFix.Instance.Log.LogDebug("OpenALDevice not found."); return; }

                var getDev = AccessTools.Method(oalType, "GetDevices");
                if (getDev != null)
                {
                    KernelFix.Instance.HarmonyInstance.Patch(getDev,
                        prefix: new HarmonyMethod(typeof(GetDevicesPrefix), "Prefix"));
                    KernelFix.Instance.Log.LogDebug("GetDevices patch applied.");
                }

                var getCap = AccessTools.Method(oalType, "GetCaptureDevices");
                if (getCap != null)
                {
                    KernelFix.Instance.HarmonyInstance.Patch(getCap,
                        prefix: new HarmonyMethod(typeof(GetCaptureDevicesPrefix), "Prefix"));
                    KernelFix.Instance.Log.LogDebug("GetCaptureDevices patch applied.");
                }
                else
                    KernelFix.Instance.Log.LogDebug("GetCaptureDevices not found.");
            }
            catch (Exception ex)
            {
                KernelFix.Instance.Log.LogWarning($"OpenAL fix failed: {ex.Message}");
            }
        }
    }

    internal static class GetDevicesPrefix
    {
        public static bool Prefix(ref object __result)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
            __result = new System.Collections.ObjectModel.ReadOnlyCollection<Microsoft.Xna.Framework.Audio.RendererDetail>(
                new System.Collections.Generic.List<Microsoft.Xna.Framework.Audio.RendererDetail>());
            Console.WriteLine("[OpenAL] Skipping GetDevices on non-Windows.");
            return false;
        }
    }

    internal static class GetCaptureDevicesPrefix
    {
        public static bool Prefix(ref object __result)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
            __result = new System.Collections.ObjectModel.ReadOnlyCollection<Microsoft.Xna.Framework.Audio.RendererDetail>(
                new System.Collections.Generic.List<Microsoft.Xna.Framework.Audio.RendererDetail>());
            Console.WriteLine("[OpenAL] Skipping GetCaptureDevices on non-Windows.");
            return false;
        }
    }
}
