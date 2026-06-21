using System;
using OmenSuperHub;

namespace OmenSuperHub.Tests {
  internal static class Program {
    private static int failures;

    private static void Main() {
      Run("OperationResult success stores message", OperationResultSuccessStoresMessage);
      Run("OperationResult failure stores message", OperationResultFailureStoresMessage);
      Run("MonitorSettings maps high interval", MonitorSettingsMapsHighInterval);
      Run("MonitorSettings normalizes unknown interval", MonitorSettingsNormalizesUnknownInterval);
      Run("HardwareMonitorSnapshot parses invariant output", HardwareMonitorSnapshotParsesInvariantOutput);
      Run("HardwareMonitorSnapshot parses frequency output", HardwareMonitorSnapshotParsesFrequencyOutput);
      Run("HardwareMonitorSnapshot rejects malformed output", HardwareMonitorSnapshotRejectsMalformedOutput);
      Run("PresetService creates extreme defaults", PresetServiceCreatesExtremeDefaults);
      Run("PresetService creates light-use defaults", PresetServiceCreatesLightUseDefaults);

      if (failures > 0) {
        Console.Error.WriteLine(failures + " test(s) failed.");
        Environment.Exit(1);
      }

      Console.WriteLine("All tests passed.");
    }

    private static void OperationResultSuccessStoresMessage() {
      OperationResult result = OperationResult.Success("ok");
      AssertTrue(result.Succeeded, "Expected success result.");
      AssertEqual("ok", result.Message, "Expected success message.");
    }

    private static void OperationResultFailureStoresMessage() {
      OperationResult result = OperationResult.Failure("bad");
      AssertTrue(!result.Succeeded, "Expected failure result.");
      AssertEqual("bad", result.Message, "Expected failure message.");
    }

    private static void MonitorSettingsMapsHighInterval() {
      var settings = new MonitorSettings { RefreshRate = "high" };
      AssertEqual(250, settings.IntervalMs, "Expected high refresh interval.");
    }

    private static void MonitorSettingsNormalizesUnknownInterval() {
      var settings = new MonitorSettings { RefreshRate = "fast" };
      AssertEqual(1000, settings.IntervalMs, "Expected fallback refresh interval.");
      AssertEqual("low", settings.NormalizedRefreshRate, "Expected normalized low refresh rate.");
    }

    private static void HardwareMonitorSnapshotParsesInvariantOutput() {
      HardwareMonitorSnapshot snapshot;
      bool parsed = HardwareMonitorSnapshot.TryParse("51.25;12.50;44.75;80.00;1", out snapshot);

      AssertTrue(parsed, "Expected snapshot to parse.");
      AssertNear(51.25f, snapshot.CpuTemperature, "Expected CPU temperature.");
      AssertNear(12.50f, snapshot.CpuPower, "Expected CPU power.");
      AssertNear(44.75f, snapshot.GpuTemperature, "Expected GPU temperature.");
      AssertNear(80.00f, snapshot.GpuPower, "Expected GPU power.");
      AssertTrue(snapshot.GotGpuPower, "Expected GPU power flag.");
    }

    private static void HardwareMonitorSnapshotParsesFrequencyOutput() {
      HardwareMonitorSnapshot snapshot;
      bool parsed = HardwareMonitorSnapshot.TryParse("51.25;12.50;44.75;80.00;1;4250.00;2100.00", out snapshot);

      AssertTrue(parsed, "Expected snapshot with frequency to parse.");
      AssertNear(4250f, snapshot.CpuFrequency, "Expected CPU frequency.");
      AssertNear(2100f, snapshot.GpuFrequency, "Expected GPU frequency.");
    }

    private static void HardwareMonitorSnapshotRejectsMalformedOutput() {
      HardwareMonitorSnapshot snapshot;
      bool parsed = HardwareMonitorSnapshot.TryParse("51.25;bad", out snapshot);

      AssertTrue(!parsed, "Expected malformed snapshot to fail.");
    }

    private static void PresetServiceCreatesExtremeDefaults() {
      PresetSettings settings = PresetService.CreateBuiltInDefaults("PresetExtreme", null);

      AssertEqual("cool", settings.FanTable, "Expected cool fan table.");
      AssertEqual("auto", settings.FanControl, "Expected auto fan control.");
      AssertEqual("160 W", settings.CpuPower, "Expected fallback CPU power.");
      AssertEqual("160 W", settings.TppPower, "Expected fallback TPP power.");
      AssertEqual(120, settings.GpuCoreOverclock, "Expected core overclock.");
      AssertEqual(400, settings.GpuMemoryOverclock, "Expected memory overclock.");
      AssertEqual(0, settings.MaxFrameRate, "Expected unlimited frame rate.");
    }

    private static void PresetServiceCreatesLightUseDefaults() {
      PresetSettings settings = PresetService.CreateBuiltInDefaults("PresetLightUse", null);

      AssertEqual("silent", settings.FanTable, "Expected silent fan table.");
      AssertEqual("33 W", settings.CpuPower, "Expected 60 percent fallback CPU power.");
      AssertEqual("null", settings.TppPower, "Expected unset TPP power.");
      AssertEqual("off", settings.TgpPower, "Expected disabled TGP.");
      AssertEqual("off", settings.PpabPower, "Expected disabled PPAB.");
      AssertEqual(60, settings.MaxFrameRate, "Expected 60 FPS limit.");
    }

    private static void Run(string name, Action test) {
      try {
        test();
        Console.WriteLine("PASS " + name);
      } catch (Exception ex) {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
      }
    }

    private static void AssertTrue(bool condition, string message) {
      if (!condition)
        throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message) {
      if (!object.Equals(expected, actual))
        throw new InvalidOperationException(message + " Expected <" + expected + "> but got <" + actual + ">.");
    }

    private static void AssertNear(float expected, float actual, string message) {
      if (Math.Abs(expected - actual) > 0.001f)
        throw new InvalidOperationException(message + " Expected <" + expected + "> but got <" + actual + ">.");
    }
  }
}
