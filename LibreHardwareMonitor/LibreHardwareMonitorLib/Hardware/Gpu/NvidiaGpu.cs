using System;
using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Interop;

namespace LibreHardwareMonitor.Hardware.Gpu;

internal sealed class NvidiaGpu : GenericGpu
{
    private readonly NvApi.NvPhysicalGpuHandle _handle;
    private readonly NvidiaML.NvmlDevice? _nvmlDevice;
    private readonly Sensor _coreClock;
    private readonly Sensor _powerUsage;
    private readonly Sensor _temperature;
    private bool _firstUpdate = true;

    public NvidiaGpu(int adapterIndex, NvApi.NvPhysicalGpuHandle handle, NvApi.NvDisplayHandle? displayHandle, ISettings settings)
        : base(GetName(handle), new Identifier("gpu-nvidia", adapterIndex.ToString(CultureInfo.InvariantCulture)), settings)
    {
        _handle = handle;

        // 温度传感器
        _temperature = new Sensor("GPU Core", 0, SensorType.Temperature, this, settings);
        ActivateSensor(_temperature);

        // 频率传感器 (NVAPI)
        if (NvApi.NvAPI_GPU_GetAllClockFrequencies != null)
        {
            _coreClock = new Sensor("GPU Core", 0, SensorType.Clock, this, settings);
            ActivateSensor(_coreClock);
        }

        // 功率传感器 (NVML)
        if (NvidiaML.IsAvailable || NvidiaML.Initialize())
        {
            if (NvApi.NvAPI_GPU_GetBusId(handle, out uint busId) == NvApi.NvStatus.OK)
                _nvmlDevice = NvidiaML.NvmlDeviceGetHandleByPciBusId($" 0000:{busId:X2}:00.0") ?? NvidiaML.NvmlDeviceGetHandleByIndex(adapterIndex);
            else
                _nvmlDevice = NvidiaML.NvmlDeviceGetHandleByIndex(adapterIndex);

            if (_nvmlDevice.HasValue)
                _powerUsage = new Sensor("GPU Package", 0, SensorType.Power, this, settings);
        }

        Update();
    }

    public override string DeviceId => null;

    public override HardwareType HardwareType => HardwareType.GpuNvidia;

    /// <summary>
    /// 通过读取 GPU 性能状态判断是否休眠。
    /// GPU 活跃时返回 OK，休眠时返回非 OK（如原始错误码 -216 对应 NVAPI_GPU_NOT_POWERED），
    /// 且此调用本身不会唤醒 GPU。
    /// </summary>
    private bool IsGpuPowered()
    {
        if (NvApi.NvAPI_GPU_GetDynamicPstatesInfoEx == null)
            return true; // API 不可用时保守处理，允许读取

        var pStatesInfo = new NvApi.NvDynamicPStatesInfo
        {
            Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvDynamicPStatesInfo>(1),
            Utilizations = new NvApi.NvDynamicPState[NvApi.MAX_GPU_UTILIZATIONS]
        };

        return NvApi.NvAPI_GPU_GetDynamicPstatesInfoEx(_handle, ref pStatesInfo) == NvApi.NvStatus.OK;
    }

    public override void Update()
    {
        try
        {
            if (!_firstUpdate && !IsGpuPowered())
            {
                _temperature.Value = null;
                if (_powerUsage != null)
                    _powerUsage.Value = null;
                if (_coreClock != null)
                    _coreClock.Value = null;
                return;
            }
            else
                _firstUpdate = false;

            // 温度
            var thermalSettings = new NvApi.NvThermalSettings
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSettings>(2),
                Count = NvApi.MAX_THERMAL_SENSORS_PER_GPU
            };
            if (NvApi.NvAPI_GPU_GetThermalSettings(_handle, (int)NvApi.NvThermalTarget.All, ref thermalSettings) == NvApi.NvStatus.OK
                && thermalSettings.Count > 0)
            {
                _temperature.Value = thermalSettings.Sensor[0].CurrentTemp;
            }

            // 频率
            if (_coreClock != null)
            {
                if (TryGetCoreClock(out float coreClock))
                    _coreClock.Value = coreClock;
                else
                    _coreClock.Value = null;
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

    private bool TryGetCoreClock(out float coreClock)
    {
        coreClock = 0f;
        if (NvApi.NvAPI_GPU_GetAllClockFrequencies == null)
            return false;

        for (int version = 1; version <= 3; version++)
        {
            var clockFrequencies = new NvApi.NvGpuClockFrequencies
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClockFrequencies>(version),
                Clocks = new NvApi.NvGpuClockFrequenciesDomain[NvApi.MAX_GPU_PUBLIC_CLOCKS]
            };

            if (NvApi.NvAPI_GPU_GetAllClockFrequencies(_handle, ref clockFrequencies) != NvApi.NvStatus.OK)
                continue;

            int graphicsClockIndex = (int)NvApi.NvGpuPublicClockId.Graphics;
            if (clockFrequencies.Clocks.Length <= graphicsClockIndex)
                continue;

            var graphicsClock = clockFrequencies.Clocks[graphicsClockIndex];
            if (!graphicsClock.IsPresent || graphicsClock.Frequency == 0)
                continue;

            coreClock = graphicsClock.Frequency / 1000f;
            return true;
        }

        return false;
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
