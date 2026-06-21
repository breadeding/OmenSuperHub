using System.Globalization;

namespace OmenSuperHub {
  public sealed class HardwareMonitorSnapshot {
    public float CpuTemperature { get; private set; }
    public float CpuPower { get; private set; }
    public float GpuTemperature { get; private set; }
    public float GpuPower { get; private set; }
    public bool GotGpuPower { get; private set; }
    public float CpuFrequency { get; private set; }
    public float GpuFrequency { get; private set; }

    public static bool TryParse(string line, out HardwareMonitorSnapshot snapshot) {
      snapshot = null;
      if (string.IsNullOrWhiteSpace(line) || line.StartsWith("CRASH:"))
        return false;

      string[] parts = line.Split(';');
      if (parts.Length != 5 && parts.Length != 7)
        return false;

      float cpuTemperature;
      float cpuPower;
      float gpuTemperature;
      float gpuPower;
      float cpuFrequency = 0f;
      float gpuFrequency = 0f;
      if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out cpuTemperature))
        return false;
      if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out cpuPower))
        return false;
      if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out gpuTemperature))
        return false;
      if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out gpuPower))
        return false;
      if (parts.Length == 7) {
        if (!float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out cpuFrequency))
          return false;
        if (!float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out gpuFrequency))
          return false;
      }

      snapshot = new HardwareMonitorSnapshot {
        CpuTemperature = cpuTemperature,
        CpuPower = cpuPower,
        GpuTemperature = gpuTemperature,
        GpuPower = gpuPower,
        GotGpuPower = parts[4] == "1",
        CpuFrequency = cpuFrequency,
        GpuFrequency = gpuFrequency
      };
      return true;
    }
  }
}
