using System;
using Microsoft.Win32;

namespace OmenSuperHub {
  public sealed class PresetService {
    private readonly AppSettingsService _settingsService;

    public PresetService(AppSettingsService settingsService) {
      _settingsService = settingsService;
    }

    public PresetSettings LoadPresetFields(string presetKey, PresetSettings defaults, bool hasNvidiaGpu) {
      PresetSettings result = defaults == null ? new PresetSettings() : defaults.Clone();

      try {
        if (AppSettingsService.IsBuiltInPreset(presetKey)) {
          using (RegistryKey key = _settingsService.OpenRootKey()) {
            if (key == null)
              return result;

            ReadHardwareFields(key, result);
          }
        } else {
          using (RegistryKey key = _settingsService.OpenPresetKey(presetKey)) {
            if (key == null)
              return result;

            ReadHardwareFields(key, result);
            ReadMonitorFields(key, result, hasNvidiaGpu);
          }
        }
      } catch (Exception ex) {
        Logger.Error("LoadPresetFields(" + presetKey + "): " + ex.Message);
      }

      return result;
    }

    public static PresetSettings CreateBuiltInDefaults(string presetKey, object platformSettings) {
      int targetPL1Perf = GetPlatformInt(platformSettings, "NbPL1UpperBoundPerformance", 160);
      int targetPL1Default = GetPlatformInt(platformSettings, "NbPL1UpperBoundDefault", 55);

      var settings = new PresetSettings {
        FanTable = "cool",
        FanControl = "auto",
        TempSensitivity = "high",
        TgpPower = "on",
        PpabPower = "on",
        DState = "normal",
        GpuCoreOverclock = 120,
        GpuMemoryOverclock = presetKey == "PresetExtreme" ? 400 : 0,
        GpuClock = 0,
        IccMax = "null",
        AcLoadLine = "null"
      };

      switch (presetKey) {
        case "PresetExtreme":
          settings.CpuPower = targetPL1Perf + " W";
          settings.TppPower = targetPL1Perf + " W";
          settings.MaxFrameRate = 0;
          break;
        case "PresetGpuPriority":
          settings.CpuPower = targetPL1Default + " W";
          settings.TppPower = targetPL1Perf + " W";
          settings.MaxFrameRate = 0;
          break;
        case "PresetLightUse":
          settings.FanTable = "silent";
          settings.CpuPower = (int)(targetPL1Default * 0.6) + " W";
          settings.TppPower = "null";
          settings.TgpPower = "off";
          settings.PpabPower = "off";
          settings.MaxFrameRate = 60;
          break;
      }

      return settings;
    }

    public static float GetRespondSpeed(string tempSensitivity) {
      switch (tempSensitivity) {
        case "realtime":
          return 1f;
        case "high":
          return 0.4f;
        case "medium":
          return 0.1f;
        case "low":
          return 0.04f;
        default:
          return 0.4f;
      }
    }

    public static int GetGpuPowerDState(string dState) {
      return dState == "normal" ? 1 : 2;
    }

    private static void ReadHardwareFields(RegistryKey key, PresetSettings settings) {
      settings.FanTable = AppSettingsService.GetString(key, "FanTable", settings.FanTable);
      settings.FanControl = AppSettingsService.GetString(key, "FanControl", settings.FanControl);
      settings.TempSensitivity = AppSettingsService.GetString(key, "TempSensitivity", settings.TempSensitivity);
      settings.CpuPower = AppSettingsService.GetString(key, "CpuPower", settings.CpuPower);
      settings.GpuCoreOverclock = AppSettingsService.GetInt(key, "GpuCoreOverclock", settings.GpuCoreOverclock);
      settings.GpuMemoryOverclock = AppSettingsService.GetInt(key, "GpuMemoryOverclock", settings.GpuMemoryOverclock);
      settings.TgpPower = AppSettingsService.GetString(key, "TgpPower", settings.TgpPower);
      settings.PpabPower = AppSettingsService.GetString(key, "PpabPower", settings.PpabPower);
      settings.DState = AppSettingsService.GetString(key, "DState", settings.DState);
      settings.GpuClock = AppSettingsService.GetInt(key, "GpuClock", settings.GpuClock);
      settings.MaxFrameRate = AppSettingsService.GetInt(key, "MaxFrameRate", settings.MaxFrameRate);
      settings.TppPower = AppSettingsService.GetString(key, "TppPower", settings.TppPower);
      settings.IccMax = AppSettingsService.GetString(key, "IccMax", settings.IccMax);
      settings.AcLoadLine = AppSettingsService.GetString(key, "AcLoadLine", settings.AcLoadLine);
    }

    private static void ReadMonitorFields(RegistryKey key, PresetSettings settings, bool hasNvidiaGpu) {
      settings.MonitorCPU = AppSettingsService.GetBool(key, "MonitorCPU", settings.MonitorCPU);
      settings.MonitorGPU = hasNvidiaGpu ? AppSettingsService.GetBool(key, "MonitorGPU", settings.MonitorGPU) : false;
      settings.MonitorFan = AppSettingsService.GetBool(key, "MonitorFan", settings.MonitorFan);
      settings.MonitorRefreshRate = AppSettingsService.GetString(key, "MonitorRefreshRate", settings.MonitorRefreshRate);
      settings.TempDisplayMode = AppSettingsService.GetString(key, "TempDisplayMode", settings.TempDisplayMode);
    }

    private static int GetPlatformInt(object platformSettings, string propertyName, int fallback) {
      if (platformSettings == null)
        return fallback;

      var property = platformSettings.GetType().GetProperty(propertyName);
      if (property == null)
        return fallback;

      object value = property.GetValue(platformSettings, null);
      try {
        int numericValue = Convert.ToInt32(value);
        if (numericValue > 0)
          return numericValue;
      } catch {
      }

      return fallback;
    }
  }
}
