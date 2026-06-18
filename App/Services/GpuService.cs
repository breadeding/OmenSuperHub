namespace OmenSuperHub {
  public sealed class GpuService {
    public OperationResult RestartGpu() {
      return GpuAppManager.RestartGpuWithResult();
    }

    public OperationResult CheckDbVersion(int kind) {
      return GpuAppManager.CheckDBVersionWithResult(kind);
    }

    public void SetCoreClockOffset(int offsetMHz) {
      GpuAppManager.SetCoreClockOffset(offsetMHz);
    }

    public void SetMemoryClockOffset(int offsetMHz) {
      GpuAppManager.SetMemoryClockOffset(offsetMHz);
    }

    public void SetGpuClockLimit(int frequencyMHz) {
      GpuAppManager.SetGPUClockLimit(frequencyMHz);
    }

    public void ResetGpuClockLimit() {
      GpuAppManager.SetGPUClockReset();
    }
  }
}
