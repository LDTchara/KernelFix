using System.Runtime.InteropServices;

namespace KernelFix
{
    internal static class DpiFix
    {
        public static void Apply()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetProcessDPIAware();
                KernelFix.Instance.Log.LogDebug("DPI awareness set.");
            }
            else
            {
                KernelFix.Instance.Log.LogDebug("DPI fix skipped on non-Windows platform.");
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
