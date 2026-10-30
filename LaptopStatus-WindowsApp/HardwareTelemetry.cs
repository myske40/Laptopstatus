using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LaptopStatus
{
    internal sealed record GpuSnapshot(string Model, double? UsagePercent, double? TemperatureC);

    internal sealed record HardwareSnapshot(
        double? CpuTemperatureC,
        double? FanRpm,
        string CpuModel,
        IReadOnlyList<GpuSnapshot> Gpus)
    {
        public string GpuModel => string.Join("; ", Gpus.Select(gpu => gpu.Model));
        public double? GpuUsagePercent => Gpus.Select(gpu => gpu.UsagePercent).FirstOrDefault(value => value.HasValue);
        public double? GpuTemperatureC => Gpus.Select(gpu => gpu.TemperatureC).FirstOrDefault(value => value.HasValue);
    }

    internal sealed class HardwareTelemetry : IDisposable
    {
        private readonly Computer _computer = new()
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true
        };

        public HardwareTelemetry()
        {
            try { _computer.Open(); } catch { }
        }

        public HardwareSnapshot Read()
        {
            var cpuTemperatures = new List<SensorReading>();
            var gpus = new List<GpuSnapshot>();
            double? fanRpm = null;
            var cpuModel = "";

            foreach (var hardware in _computer.Hardware)
            {
                UpdateHardware(hardware);

                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    if (string.IsNullOrWhiteSpace(cpuModel)) cpuModel = hardware.Name?.Trim() ?? "";
                    CollectCpuTemperatures(hardware, cpuTemperatures);
                    fanRpm ??= FindFanRpm(hardware);
                    continue;
                }

                if (IsGpu(hardware.HardwareType))
                {
                    var loads = new List<SensorReading>();
                    var temperatures = new List<SensorReading>();
                    CollectGpu(hardware, loads, temperatures);
                    fanRpm ??= FindFanRpm(hardware);
                    var model = hardware.Name?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(model))
                    {
                        gpus.Add(new GpuSnapshot(model,
                            SelectPreferred(loads, allowZero: true, maximum: 100),
                            SelectTemperature(temperatures, maximumCelsius: 130)));
                    }
                    continue;
                }

                fanRpm ??= FindFanRpm(hardware);
            }

            return new HardwareSnapshot(
                SelectTemperature(cpuTemperatures, maximumCelsius: 115),
                fanRpm,
                cpuModel,
                gpus);
        }

        private static bool IsGpu(HardwareType type) =>
            type is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;

        private static void UpdateHardware(IHardware hardware)
        {
            try { hardware.Update(); } catch { }
            foreach (var child in hardware.SubHardware) UpdateHardware(child);
        }

        private static void CollectCpuTemperatures(IHardware hardware, List<SensorReading> temperatures)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || sensor.Value is not float raw || !float.IsFinite(raw)) continue;
                if (IsPlausibleTemperature(raw, 115)) temperatures.Add(new SensorReading(TemperaturePriority(sensor.Name ?? ""), raw));
            }
            foreach (var child in hardware.SubHardware) CollectCpuTemperatures(child, temperatures);
        }

        private static void CollectGpu(IHardware hardware, List<SensorReading> loads, List<SensorReading> temperatures)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is not float raw || !float.IsFinite(raw)) continue;
                var name = sensor.Name ?? string.Empty;
                if (sensor.SensorType == SensorType.Load && raw is >= 0 and <= 100)
                    loads.Add(new SensorReading(LoadPriority(name), raw));
                else if (sensor.SensorType == SensorType.Temperature && IsPlausibleTemperature(raw, 130))
                    temperatures.Add(new SensorReading(TemperaturePriority(name), raw));
            }
            foreach (var child in hardware.SubHardware) CollectGpu(child, loads, temperatures);
        }

        private static double? FindFanRpm(IHardware hardware)
        {
            foreach (var sensor in hardware.Sensors)
                if (sensor.SensorType == SensorType.Fan && sensor.Value is float raw && float.IsFinite(raw) && raw is > 0 and < 100000)
                    return raw;
            foreach (var child in hardware.SubHardware)
                if (FindFanRpm(child) is double rpm) return rpm;
            return null;
        }

        private static double? SelectTemperature(List<SensorReading> candidates, double maximumCelsius) =>
            SelectPreferred(candidates, allowZero: false, maximum: maximumCelsius);

        private static double? SelectPreferred(List<SensorReading> candidates, bool allowZero, double maximum)
        {
            var candidate = candidates
                .Where(reading => reading.Value <= maximum && (allowZero ? reading.Value >= 0 : reading.Value >= 5))
                .OrderByDescending(reading => reading.Priority)
                .ThenByDescending(reading => reading.Value)
                .Select(reading => (double?)reading.Value)
                .FirstOrDefault();
            return candidate;
        }

        private static bool IsPlausibleTemperature(double value, double maximumCelsius) =>
            value is >= 5 && value <= maximumCelsius;

        private static int TemperaturePriority(string name)
        {
            if (name.Contains("package", StringComparison.OrdinalIgnoreCase)) return 100;
            if (name.Contains("tctl", StringComparison.OrdinalIgnoreCase)) return 95;
            if (name.Contains("tdie", StringComparison.OrdinalIgnoreCase)) return 90;
            if (name.Contains("die", StringComparison.OrdinalIgnoreCase)) return 85;
            if (name.Contains("core max", StringComparison.OrdinalIgnoreCase)) return 80;
            if (name.Contains("ccd", StringComparison.OrdinalIgnoreCase)) return 75;
            if (name.Contains("core", StringComparison.OrdinalIgnoreCase)) return 50;
            return 10;
        }

        private static int LoadPriority(string name)
        {
            if (name.Contains("core", StringComparison.OrdinalIgnoreCase)) return 100;
            if (name.Contains("d3d", StringComparison.OrdinalIgnoreCase)) return 90;
            if (name.Contains("memory", StringComparison.OrdinalIgnoreCase)) return 20;
            return 50;
        }

        private readonly record struct SensorReading(int Priority, double Value);

        public void Dispose()
        {
            try { _computer.Close(); } catch { }
        }
    }
}
