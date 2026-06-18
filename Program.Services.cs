using System;
using System.Linq;
using System.Windows.Forms;

namespace OmenSuperHub {
  static partial class Program {
    static readonly AppSettingsService appSettingsService = new AppSettingsService();
    static readonly HardwareControlService hardwareControlService = new HardwareControlService();
    static readonly GpuService gpuService = new GpuService();
    static readonly PresetService presetService = new PresetService(appSettingsService);
    static HardwareMonitorService hardwareMonitorService;

    static void InitializeServices() {
      if (hardwareMonitorService != null)
        return;

      hardwareMonitorService = new HardwareMonitorService(Application.ExecutablePath);
      hardwareMonitorService.SnapshotReceived += OnHardwareMonitorSnapshotReceived;
      hardwareMonitorService.ErrorReceived += (s, e) => Logger.Error("HardwareMonitor [HWMonitor ERR] " + e.Message);
    }

    static bool IsHardwareMonitorRunning() {
      return hardwareMonitorService != null && hardwareMonitorService.IsRunning;
    }

    static void OnHardwareMonitorSnapshotReceived(object sender, HardwareMonitorSnapshotEventArgs e) {
      HardwareMonitorSnapshot snapshot = e.Snapshot;
      rawTempCPU = snapshot.CpuTemperature;
      if (snapshot.CpuPower < 9999)
        rawPowerCPU = snapshot.CpuPower;
      rawTempGPU = snapshot.GpuTemperature;
      rawPowerGPU = snapshot.GpuPower;
      rawGotGPU = snapshot.GotGpuPower;

      if (!cpuTempReady) {
        smoothedCPUTemp = rawTempCPU;
        cpuTempReady = true;
      }
      if (!gpuTempReady && rawGotGPU) {
        smoothedGPUTemp = rawTempGPU;
        gpuTempReady = true;
      }
      if (!rawGotGPU) {
        gpuTempReady = false;
        GPUTemp = 40;
        GPUPower = 0;
      }

      if (!tempReady) {
        tempReady = true;
        try {
          QueryHardware();
        } catch (Exception ex) {
          Logger.Error("[UpdateTooltip] QueryHardware 异常: " + ex.Message);
        }
        UpdateFloatingText();
        UpdateTrayIconText();

        if (customIcon == "dynamic")
          UpdateDynamicIcon();
      }
    }

    static PresetSettings CapturePresetSettings() {
      return new PresetSettings {
        FanTable = fanTable,
        FanControl = fanControl,
        TempSensitivity = tempSensitivity,
        CpuPower = cpuPower,
        GpuCoreOverclock = gpuCoreOverclock,
        GpuMemoryOverclock = gpuMemoryOverclock,
        TgpPower = tgpPower,
        PpabPower = ppabPower,
        DState = dState,
        GpuClock = gpuClock,
        MaxFrameRate = maxFrameRate,
        TppPower = tppPower,
        IccMax = iccMax,
        AcLoadLine = acLoadline,
        MonitorCPU = monitorCPU,
        MonitorGPU = monitorGPU,
        MonitorFan = monitorFan,
        MonitorRefreshRate = monitorRefreshRate,
        TempDisplayMode = tempDisplayMode
      };
    }

    static void ApplyPresetSettingsToFields(PresetSettings settings, bool includeMonitorSettings) {
      if (settings == null)
        return;

      fanTable = settings.FanTable;
      fanControl = settings.FanControl;
      tempSensitivity = settings.TempSensitivity;
      cpuPower = settings.CpuPower;
      gpuCoreOverclock = settings.GpuCoreOverclock;
      gpuMemoryOverclock = settings.GpuMemoryOverclock;
      tgpPower = settings.TgpPower;
      ppabPower = settings.PpabPower;
      dState = settings.DState;
      gpuClock = settings.GpuClock;
      maxFrameRate = settings.MaxFrameRate;
      tppPower = settings.TppPower;
      iccMax = settings.IccMax;
      acLoadline = settings.AcLoadLine;

      if (!includeMonitorSettings)
        return;

      monitorCPU = settings.MonitorCPU;
      monitorGPU = hasNVIDIAGpu && settings.MonitorGPU;
      monitorFan = settings.MonitorFan;
      monitorRefreshRate = settings.MonitorRefreshRate;
      tempDisplayMode = settings.TempDisplayMode;
    }

    static void ShowGpuOperationWarning(OperationResult result) {
      if (result == null || result.Succeeded)
        return;

      MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), result.Message, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    static void ShowGpuOperationWarningAsync(OperationResult result) {
      if (result == null || result.Succeeded)
        return;

      if (uiContext != null) {
        uiContext.Post(_ => ShowGpuOperationWarning(result), null);
      } else {
        ShowGpuOperationWarning(result);
      }
    }

    static bool CheckDBVersionWithUi(int kind) {
      OperationResult result = gpuService.CheckDbVersion(kind);
      ShowGpuOperationWarning(result);
      return result.Succeeded;
    }
  }
}
