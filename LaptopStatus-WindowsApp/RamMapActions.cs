using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LaptopStatus
{
    internal static class RamMapActions
    {
        private const int SystemMemoryListInformation = 80;
        private const uint ProcessSetQuota = 0x0100;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        public static string Execute(string action)
        {
            return action switch
            {
                "working-sets" => EmptyProcessWorkingSets(),
                "system-working-set" => RunMemoryListCommand(2, "System working set dikosongkan."),
                "modified-page-list" => RunMemoryListCommand(3, "Modified page list dikosongkan."),
                "standby-list" => RunMemoryListCommand(4, "Standby list dikosongkan."),
                "priority0-standby-list" => RunMemoryListCommand(5, "Standby priority 0 dikosongkan."),
                _ => throw new ArgumentException("Aksi RAMMap tidak dikenali.")
            };
        }

        private static string EmptyProcessWorkingSets()
        {
            var succeeded = 0;
            var denied = 0;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId || process.Id <= 4) continue;
                    try
                    {
                        var handle = OpenProcess(ProcessSetQuota | ProcessQueryLimitedInformation, false, process.Id);
                        if (handle == IntPtr.Zero) { denied++; continue; }
                        try
                        {
                            if (K32EmptyWorkingSet(handle)) succeeded++;
                            else denied++;
                        }
                        finally { CloseHandle(handle); }
                    }
                    catch { denied++; }
                }
            }
            if (succeeded == 0) throw new InvalidOperationException("Windows tidak mengizinkan pengosongan working set proses.");
            return $"Working set dikosongkan untuk {succeeded} proses; {denied} proses dilindungi atau tidak tersedia.";
        }

        private static string RunMemoryListCommand(int command, string successMessage)
        {
            // SystemMemoryListInformation is the same operation family used by memory-management tools.
            // Unsupported/blocked systems return an NTSTATUS and are reported without retrying.
            var value = command;
            var status = NtSetSystemInformation(SystemMemoryListInformation, ref value, sizeof(int));
            if (status < 0)
                throw new InvalidOperationException($"Windows menolak aksi ini (NTSTATUS 0x{status:X8}). Pastikan layanan berjalan dengan izin administrator dan versi Windows mendukung operasi tersebut.");
            return successMessage;
        }

        [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool K32EmptyWorkingSet(IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, uint systemInformationLength);
    }
}
