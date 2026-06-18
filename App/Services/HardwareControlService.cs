using System.Collections.Generic;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  public sealed class HardwareControlService {
    public void SetMaxFanSpeedOn() {
      OmenHardware.SetMaxFanSpeedOn();
    }

    public void SetMaxFanSpeedOff() {
      OmenHardware.SetMaxFanSpeedOff();
    }

    public void SetFanLevel(int fanSpeed1, int fanSpeed2, bool fan3 = false, bool fanClean = false) {
      OmenHardware.SetFanLevel(fanSpeed1, fanSpeed2, fan3, fanClean);
    }

    public bool GetFanCount(out bool ocp, out bool otp) {
      return OmenHardware.GetFanCount(out ocp, out otp);
    }

    public float GetFittingTemperature() {
      return OmenHardware.GetFittingTemperature();
    }

    public void SetGpuPowerState(bool enableTgp, bool enablePpab, int dState = 1, int gps = 0) {
      OmenHardware.SetGpuPowerState(enableTgp, enablePpab, dState, gps);
    }

    public void SetCpuPowerLimit(byte value) {
      OmenHardware.SetCpuPowerLimit(value);
    }

    public void SetConcurrentTdp(byte value) {
      OmenHardware.SetConcurrentTdp(value);
    }

    public void SetIccMaxByWmi(decimal iccMaxAmpere) {
      OmenHardware.SetIccMaxByWmi(iccMaxAmpere);
    }

    public void SetLoadLine(int level) {
      OmenHardware.SetLoadLine(level);
    }

    public void SetUnleashMode() {
      OmenHardware.SetUnleashMode();
    }

    public List<int> GetFanLevel() {
      return OmenHardware.GetFanLevel();
    }
  }
}
