namespace OmenSuperHub {
  public sealed class PresetSettings {
    public string FanTable { get; set; } = "cool";
    public string FanControl { get; set; } = "auto";
    public string TempSensitivity { get; set; } = "high";
    public string CpuPower { get; set; } = "null";
    public int GpuCoreOverclock { get; set; } = -1;
    public int GpuMemoryOverclock { get; set; } = -1;
    public string TgpPower { get; set; } = "on";
    public string PpabPower { get; set; } = "on";
    public string DState { get; set; } = "normal";
    public int GpuClock { get; set; }
    public int MaxFrameRate { get; set; } = -1;
    public string TppPower { get; set; } = "null";
    public string IccMax { get; set; } = "null";
    public string AcLoadLine { get; set; } = "null";
    public bool MonitorCPU { get; set; } = true;
    public bool MonitorGPU { get; set; } = true;
    public bool MonitorFan { get; set; }
    public string MonitorRefreshRate { get; set; } = "low";
    public string TempDisplayMode { get; set; } = "smoothed";

    public PresetSettings Clone() {
      return (PresetSettings)MemberwiseClone();
    }
  }
}
