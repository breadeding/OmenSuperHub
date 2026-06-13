using System;
using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Hardware.Motherboard;
using LibreHardwareMonitor.Interop;

namespace LibreHardwareMonitor.Hardware.Gpu;

internal sealed class NvidiaGpu : GenericGpu
{
    private readonly NvApi.NvPhysicalGpuHandle _handle;
    private readonly NvidiaML.NvmlDevice? _nvmlDevice;
    private readonly Sensor _powerUsage;
    private readonly Sensor _temperature;

    public NvidiaGpu(int adapterIndex, NvApi.NvPhysicalGpuHandle handle, NvApi.NvDisplayHandle? displayHandle, ISettings settings)
        : base(GetName(handle), new Identifier("gpu-nvidia", adapterIndex.ToString(CultureInfo.InvariantCulture)), settings)
    {
        _handle = handle;

        // 温度传感器
        _temperature = new Sensor("GPU Core", 0, SensorType.Temperature, this, settings);
        ActivateSensor(_temperature);

        // 功率传感器 (NVML)
        if (NvidiaML.Initialize())
        {
            NvApi.NvAPI_GPU_GetBusId(handle, out uint busId);
            _nvmlDevice = NvidiaML.NvmlDeviceGetHandleByPciBusId($" 0000:{busId:X2}:00.0")
                          ?? NvidiaML.NvmlDeviceGetHandleByIndex(adapterIndex);
            if (_nvmlDevice.HasValue)
            {
                _powerUsage = new Sensor("GPU Package", 0, SensorType.Power, this, settings);
                // 注意：功率传感器在 Update 中激活，这里不激活避免无值
            }
        }
    }

    public override string DeviceId => null;

    public override HardwareType HardwareType => HardwareType.GpuNvidia;

    public override void Update()
    {
        try
        {
            // 温度
            var settings = new NvApi.NvThermalSettings
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSettings>(2),
                Count = NvApi.MAX_THERMAL_SENSORS_PER_GPU
            };
            if (NvApi.NvAPI_GPU_GetThermalSettings(_handle, (int)NvApi.NvThermalTarget.All, ref settings) == NvApi.NvStatus.OK
                && settings.Count > 0)
            {
                _temperature.Value = settings.Sensor[0].CurrentTemp;
            }

            // 功率
            if (_nvmlDevice.HasValue)
            {
                int? power = NvidiaML.NvmlDeviceGetPowerUsage(_nvmlDevice.Value);
                if (power.HasValue)
                {
                    _powerUsage.Value = power.Value / 1000f;
                    ActivateSensor(_powerUsage);
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"{nameof(NvidiaGpu)}.{nameof(Update)} failed for {Name} ({Identifier}): {e}");
        }
    }

    private static string GetName(NvApi.NvPhysicalGpuHandle handle)
    {
        if (NvApi.NvAPI_GPU_GetFullName(handle, out string name) == NvApi.NvStatus.OK)
        {
            name = name.Trim();
            return name.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase) ? name : "NVIDIA " + name;
        }
        return "NVIDIA";
    }
}
