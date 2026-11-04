using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LaptopStatus
{
    internal sealed class PerformanceTelemetry : IDisposable
    {
        private sealed record MemoryModule(string Location, ulong CapacityBytes, uint SpeedMhz);
        private sealed record GpuDevice(string Name, ulong? DedicatedBytes, string DriverVersion);
        private sealed record Inventory(string CpuName, int Sockets, int Cores, int LogicalProcessors, uint BaseMhz,
            bool? Virtualization, ulong L1Bytes, ulong L2Bytes, ulong L3Bytes, int MemorySlots, uint MemorySpeedMhz,
            IReadOnlyList<MemoryModule> MemoryModules, IReadOnlyList<GpuDevice> Gpus);
        private sealed record NetworkSample(double DownloadBytesPerSecond, double UploadBytesPerSecond, string Adapter);

        private readonly Task<Inventory> _inventoryTask = Task.Run(ReadInventory);
        private PerformanceCounter? _cpuPerformanceCounter;
        private long? _previousReceivedBytes;
        private long? _previousSentBytes;
        private DateTime _previousNetworkSampleUtc;
        private NetworkSample _networkSample = new(0, 0, "");
        private DateTime _lastNetworkReadUtc;
        private DateTime _lastGpuMemoryReadUtc;
        private (ulong? Dedicated, ulong? Shared) _gpuMemoryUsage;

        public PerformanceTelemetry()
        {
            try
            {
                _cpuPerformanceCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                _cpuPerformanceCounter.NextValue();
            }
            catch { _cpuPerformanceCounter?.Dispose(); _cpuPerformanceCounter = null; }
        }

        public object CreateSnapshot(double cpuUsage, double ramPercent, double? gpuUsage, double? gpuTemperature,
            string cpuTemperature, string fanSpeed)
        {
            var inventory = _inventoryTask.IsCompletedSuccessfully ? _inventoryTask.Result : null;
            var info = ReadPerformanceInformation();
            var totalMemory = info is null ? 0UL : Bytes(info.Value.PhysicalTotal, info.Value.PageSize);
            var availableMemory = info is null ? 0UL : Math.Min(totalMemory, Bytes(info.Value.PhysicalAvailable, info.Value.PageSize));
            var usedMemory = totalMemory > availableMemory ? totalMemory - availableMemory : 0;
            var currentSpeedMhz = inventory?.BaseMhz > 0 ? inventory.BaseMhz : (uint?)null;
            if (inventory?.BaseMhz > 0 && _cpuPerformanceCounter is not null)
            {
                try
                {
                    var performanceRatio = _cpuPerformanceCounter.NextValue();
                    if (double.IsFinite(performanceRatio) && performanceRatio > 0)
                        currentSpeedMhz = (uint)Math.Clamp(Math.Round(inventory.BaseMhz * performanceRatio / 100d), 1, 100000);
                }
                catch { }
            }

            UpdateNetworkSample();
            if (DateTime.UtcNow - _lastGpuMemoryReadUtc >= TimeSpan.FromSeconds(10))
            {
                _lastGpuMemoryReadUtc = DateTime.UtcNow;
                _gpuMemoryUsage = ReadGpuMemoryUsage();
            }

            var gpuDedicatedTotal = inventory?.Gpus.Where(item => item.DedicatedBytes.HasValue)
                .Aggregate(0UL, (total, item) => total + item.DedicatedBytes.GetValueOrDefault()) ?? 0UL;
            var logicalProcessorCount = inventory?.LogicalProcessors is > 0 ? inventory.LogicalProcessors : Environment.ProcessorCount;
            return new
            {
                cpuModel = inventory?.CpuName ?? "",
                cpuUsage = Math.Clamp(cpuUsage, 0, 100),
                cpuSpeedMhz = currentSpeedMhz,
                cpuBaseSpeedMhz = inventory is { BaseMhz: > 0 } ? inventory.BaseMhz : (uint?)null,
                socketCount = inventory?.Sockets > 0 ? inventory.Sockets : (int?)null,
                coreCount = inventory?.Cores > 0 ? inventory.Cores : (int?)null,
                logicalProcessorCount,
                virtualization = inventory?.Virtualization,
                cpuL1CacheBytes = inventory?.L1Bytes,
                cpuL2CacheBytes = inventory?.L2Bytes,
                cpuL3CacheBytes = inventory?.L3Bytes,
                processCount = info?.ProcessCount,
                threadCount = info?.ThreadCount,
                handleCount = info?.HandleCount,
                uptimeSeconds = Math.Max(0, Environment.TickCount64 / 1000),
                ramPercent = Math.Clamp(ramPercent, 0, 100),
                ramUsedBytes = totalMemory > 0 ? usedMemory : (ulong?)null,
                ramTotalBytes = totalMemory > 0 ? totalMemory : (ulong?)null,
                ramAvailableBytes = totalMemory > 0 ? availableMemory : (ulong?)null,
                memorySlotCount = inventory?.MemoryModules.Count,
                memorySlotTotal = inventory?.MemorySlots,
                memorySpeedMhz = inventory?.MemorySpeedMhz,
                memoryModules = inventory?.MemoryModules.Select(module =>
                    $"{module.Location}: {FormatBytes(module.CapacityBytes)}{(module.SpeedMhz > 0 ? $" · {module.SpeedMhz} MT/s" : "")}").ToArray() ?? Array.Empty<string>(),
                gpuModel = inventory is { Gpus.Count: > 0 } ? string.Join("; ", inventory.Gpus.Select(item => item.Name)) : "",
                gpuCount = inventory?.Gpus.Count,
                gpuPercent = gpuUsage,
                gpuTemperatureC = gpuTemperature,
                gpuDedicatedUsedBytes = _gpuMemoryUsage.Dedicated,
                gpuDedicatedTotalBytes = gpuDedicatedTotal > 0 ? gpuDedicatedTotal : (ulong?)null,
                gpuSharedUsedBytes = _gpuMemoryUsage.Shared,
                gpuSharedTotalBytes = totalMemory > 0 ? totalMemory / 2 : (ulong?)null,
                gpuDriverVersion = inventory is { Gpus.Count: > 0 }
                    ? string.Join(" · ", inventory.Gpus.Select(item => item.DriverVersion).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                    : "",
                downloadBytesPerSecond = _networkSample.DownloadBytesPerSecond,
                uploadBytesPerSecond = _networkSample.UploadBytesPerSecond,
                networkAdapter = _networkSample.Adapter,
                cpuTemperature,
                fanSpeed
            };
        }

        private void UpdateNetworkSample()
        {
            if (DateTime.UtcNow - _lastNetworkReadUtc < TimeSpan.FromSeconds(3)) return;
            _lastNetworkReadUtc = DateTime.UtcNow;
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                        item.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                    .Select(item => (item.Name, item.Description, Stats: item.GetIPv4Statistics()))
                    .ToArray();
                var received = adapters.Sum(item => (long)Math.Max(0, item.Stats.BytesReceived));
                var sent = adapters.Sum(item => (long)Math.Max(0, item.Stats.BytesSent));
                var now = DateTime.UtcNow;
                var elapsed = _previousNetworkSampleUtc == default ? 0 : Math.Max(.1, (now - _previousNetworkSampleUtc).TotalSeconds);
                var down = _previousReceivedBytes.HasValue && received >= _previousReceivedBytes.Value ? (received - _previousReceivedBytes.Value) / elapsed : 0;
                var up = _previousSentBytes.HasValue && sent >= _previousSentBytes.Value ? (sent - _previousSentBytes.Value) / elapsed : 0;
                var names = adapters.Select(item => string.IsNullOrWhiteSpace(item.Description) ? item.Name : item.Description)
                    .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase);
                _networkSample = new NetworkSample(down, up, string.Join(" · ", names));
                _previousReceivedBytes = received;
                _previousSentBytes = sent;
                _previousNetworkSampleUtc = now;
            }
            catch { _networkSample = new NetworkSample(0, 0, ""); }
        }

        private static (ulong? Dedicated, ulong? Shared) ReadGpuMemoryUsage()
        {
            ulong dedicated = 0, shared = 0;
            var hasDedicated = false;
            var hasShared = false;
            try
            {
                var category = new PerformanceCounterCategory("GPU Adapter Memory");
                foreach (var instance in category.GetInstanceNames())
                {
                    var counters = category.GetCounters(instance);
                    try
                    {
                        foreach (var counter in counters)
                        {
                            using (counter)
                            {
                                var name = counter.CounterName;
                                if (name.Equals("Dedicated Usage", StringComparison.OrdinalIgnoreCase))
                                {
                                    dedicated += (ulong)Math.Max(0, counter.RawValue);
                                    hasDedicated = true;
                                }
                                else if (name.Equals("Shared Usage", StringComparison.OrdinalIgnoreCase))
                                {
                                    shared += (ulong)Math.Max(0, counter.RawValue);
                                    hasShared = true;
                                }
                            }
                        }
                    }
                    catch { foreach (var counter in counters) counter.Dispose(); }
                }
            }
            catch { }
            return (hasDedicated ? dedicated : null, hasShared ? shared : null);
        }

        private static Inventory ReadInventory()
        {
            var sockets = 0; var cores = 0; var logical = 0; uint baseSpeed = 0;
            var cpuName = ""; bool? virtualization = null;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, VirtualizationFirmwareEnabled FROM Win32_Processor");
                using var result = searcher.Get();
                foreach (ManagementObject item in result)
                {
                    using (item)
                    {
                        sockets++;
                        if (string.IsNullOrWhiteSpace(cpuName)) cpuName = Convert.ToString(item["Name"])?.Trim() ?? "";
                        cores += ToInt(item["NumberOfCores"]);
                        logical += ToInt(item["NumberOfLogicalProcessors"]);
                        baseSpeed = Math.Max(baseSpeed, ToUInt(item["MaxClockSpeed"]));
                        if (item["VirtualizationFirmwareEnabled"] is not null) virtualization = ToBool(item["VirtualizationFirmwareEnabled"]);
                    }
                }
            }
            catch { }
            ulong l1 = 0, l2 = 0, l3 = 0;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Level, InstalledSize FROM Win32_CacheMemory WHERE InstalledSize > 0");
                using var result = searcher.Get();
                foreach (ManagementObject item in result)
                {
                    using (item)
                    {
                        var bytes = (ulong)Math.Max(0, ToInt(item["InstalledSize"])) * 1024UL;
                        switch (ToInt(item["Level"])) { case 3: l1 += bytes; break; case 4: l2 += bytes; break; case 5: l3 += bytes; break; }
                    }
                }
            }
            catch { }
            var slots = 0; uint memorySpeed = 0; var modules = new List<MemoryModule>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                using var result = searcher.Get();
                foreach (ManagementObject item in result) { using (item) slots += ToInt(item["MemoryDevices"]); }
            }
            catch { }
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                using var result = searcher.Get();
                foreach (ManagementObject item in result)
                {
                    using (item)
                    {
                        var locator = Convert.ToString(item["DeviceLocator"])?.Trim();
                        if (string.IsNullOrWhiteSpace(locator)) locator = Convert.ToString(item["BankLabel"])?.Trim();
                        if (string.IsNullOrWhiteSpace(locator)) locator = $"Slot {modules.Count + 1}";
                        var speed = Math.Max(ToUInt(item["ConfiguredClockSpeed"]), ToUInt(item["Speed"]));
                        memorySpeed = Math.Max(memorySpeed, speed);
                        modules.Add(new MemoryModule(locator, ToULong(item["Capacity"]), speed));
                    }
                }
            }
            catch { }
            var gpus = new List<GpuDevice>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
                using var result = searcher.Get();
                foreach (ManagementObject item in result)
                {
                    using (item)
                    {
                        var name = Convert.ToString(item["Name"])?.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            gpus.Add(new GpuDevice(name, ToULong(item["AdapterRAM"]), Convert.ToString(item["DriverVersion"])?.Trim() ?? ""));
                    }
                }
            }
            catch { }
            return new Inventory(cpuName, sockets, cores, logical, baseSpeed, virtualization, l1, l2, l3, slots, memorySpeed, modules, gpus);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PerformanceInformation
        {
            public uint Size;
            public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }

        private static PerformanceInformation? ReadPerformanceInformation()
        {
            try
            {
                var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
                return GetPerformanceInfo(ref info, info.Size) ? info : null;
            }
            catch
            {
                // Performance counters are optional; a failed native read must not close the app.
                return null;
            }
        }
        private static ulong Bytes(nuint pages, nuint pageSize) => checked((ulong)pages * (ulong)pageSize);
        private static int ToInt(object? value) { try { return Convert.ToInt32(value); } catch { return 0; } }
        private static uint ToUInt(object? value) { try { return Convert.ToUInt32(value); } catch { return 0; } }
        private static ulong ToULong(object? value) { try { return Convert.ToUInt64(value); } catch { return 0; } }
        private static bool ToBool(object? value) { try { return Convert.ToBoolean(value); } catch { return false; } }
        private static string FormatBytes(ulong value)
        {
            var amount = (double)value; var unit = 0; string[] units = { "B", "KB", "MB", "GB", "TB" };
            while (amount >= 1024 && unit < units.Length - 1) { amount /= 1024; unit++; }
            return $"{amount:0.#} {units[unit]}";
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

        public void Dispose() { _cpuPerformanceCounter?.Dispose(); }
    }
}
