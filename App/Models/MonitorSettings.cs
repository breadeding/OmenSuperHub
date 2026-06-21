namespace OmenSuperHub {
  public sealed class MonitorSettings {
    public bool CpuEnabled { get; set; }
    public bool GpuEnabled { get; set; }
    public bool FanEnabled { get; set; }
    public string RefreshRate { get; set; } = "low";
    public string TemperatureDisplayMode { get; set; } = "smoothed";

    public string NormalizedRefreshRate {
      get { return RefreshRate == "high" ? "high" : "low"; }
    }

    public int IntervalMs {
      get { return NormalizedRefreshRate == "high" ? 250 : 1000; }
    }
  }
}
