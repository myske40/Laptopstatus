using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

namespace LaptopStatus
{
    internal static class ProcessKillHelper
    {
        private static readonly string[] ProtectedNames =
        {
            "system", "idle", "csrss", "wininit", "winlogon", "smss", "services", "lsass",
            "registry", "memory compression", "secure system", "svchost", "laptopstatus"
        };

        public static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool CanTerminate(Process process, out string reason)
        {
            try
            {
                if (process.Id <= 4 || process.Id == Environment.ProcessId)
                {
                    reason = "proses inti Windows atau layanan LaptopStatus dilindungi.";
                    return false;
                }
                if (ProtectedNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    reason = "proses inti Windows tidak dapat dihentikan dari aplikasi ini.";
                    return false;
                }
                reason = "";
                return true;
            }
            catch
            {
                reason = "informasi proses tidak dapat diperiksa.";
                return false;
            }
        }

    }
}
