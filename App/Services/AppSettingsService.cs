using Microsoft.Win32;

namespace OmenSuperHub {
  public sealed class AppSettingsService {
    public const string RootPath = @"Software\OmenSuperHub";

    public RegistryKey OpenRootKey(bool writable = false) {
      return Registry.CurrentUser.OpenSubKey(RootPath, writable);
    }

    public RegistryKey CreateRootKey() {
      return Registry.CurrentUser.CreateSubKey(RootPath);
    }

    public RegistryKey OpenPresetKey(string presetKey, bool writable = false) {
      return Registry.CurrentUser.OpenSubKey(RootPath + "\\" + presetKey, writable);
    }

    public RegistryKey CreatePresetKey(string presetKey) {
      return Registry.CurrentUser.CreateSubKey(RootPath + "\\" + presetKey);
    }

    public static bool IsBuiltInPreset(string presetKey) {
      return presetKey == "PresetExtreme" || presetKey == "PresetGpuPriority" || presetKey == "PresetLightUse";
    }

    public static string GetString(RegistryKey key, string name, string defaultValue) {
      if (key == null)
        return defaultValue;
      return (string)key.GetValue(name, defaultValue);
    }

    public static int GetInt(RegistryKey key, string name, int defaultValue) {
      if (key == null)
        return defaultValue;
      return (int)key.GetValue(name, defaultValue);
    }

    public static bool GetBool(RegistryKey key, string name, bool defaultValue) {
      if (key == null)
        return defaultValue;
      return System.Convert.ToBoolean(key.GetValue(name, defaultValue));
    }
  }
}
