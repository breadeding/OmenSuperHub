using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static OmenSuperHub.GpuAppManager;

namespace OmenSuperHub {
  static partial class Program {
    static bool gpuSleepOperationRunning;
    static volatile bool gpuSleepGuardActive;

    static bool ShouldBlockBackgroundGpuQueries {
      get { return gpuSleepGuardActive || !monitorGPU; }
    }

    static async void StartGpuSleepAttempt() {
      if (gpuSleepOperationRunning) return;
      if (string.Equals(NvGraphicsMode.ToString(), "Discrete", StringComparison.OrdinalIgnoreCase)) {
        MessageBox.Show(Strings.GpuSleepDiscreteModeBlocked, Strings.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }
      if (!GpuSleepManager.TryGetNvidiaSleepTarget(out GpuSleepManager.GpuDeviceInfo target, out string reason)) {
        MessageBox.Show(reason, Strings.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }
      if (MessageBox.Show(Strings.GpuSleepConfirm, Strings.GpuSleepMenu, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

      gpuSleepOperationRunning = true;
      bool oldMonitorGpu = monitorGPU;
      bool oldSleepGuard = gpuSleepGuardActive;
      try {
        await DisableGpuMonitoringForSleep();
        bool slept = await RestartAndWaitForD3(target);
        if (slept) { FinishGpuSleepSuccess(); return; }

        List<GpuSleepManager.GpuProcessInfo> processes = await Task.Run(() => GpuSleepManager.GetNvidiaProcesses());
        if (!processes.Any(p => p.CanConfigurePreference)) {
          MessageBox.Show(Strings.GpuSleepFailedNoProcesses, Strings.GpuSleepMenu, MessageBoxButtons.OK, MessageBoxIcon.Information);
          RestoreGpuMonitoringAfterCancelledAttempt(oldMonitorGpu, oldSleepGuard);
          return;
        }

        using (var repair = new GpuSleepRepairForm(processes)) {
          if (repair.ShowDialog() != DialogResult.OK) {
            RestoreGpuMonitoringAfterCancelledAttempt(oldMonitorGpu, oldSleepGuard);
            return;
          }
        }

        slept = await RestartAndWaitForD3(target);
        if (slept) FinishGpuSleepSuccess();
        else {
          List<GpuSleepManager.GpuProcessInfo> remaining = await Task.Run(() => GpuSleepManager.GetNvidiaProcesses());
          string details = remaining.Count == 0 ? "" : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, remaining.Select(p => $"{p.ProcessName} (PID {p.ProcessId}, {p.UsageType})"));
          MessageBox.Show(Strings.GpuSleepStillFailed + details, Strings.GpuSleepMenu, MessageBoxButtons.OK, MessageBoxIcon.Information);
          RestoreGpuMonitoringAfterCancelledAttempt(oldMonitorGpu, oldSleepGuard);
        }
      } catch (Exception ex) {
        Logger.Error("GPU sleep attempt failed: " + ex);
        MessageBox.Show(ex.Message, Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        RestoreGpuMonitoringAfterCancelledAttempt(oldMonitorGpu, oldSleepGuard);
      } finally {
        gpuSleepOperationRunning = false;
      }
    }

    static async Task DisableGpuMonitoringForSleep() {
      gpuSleepGuardActive = true;
      monitorGPU = false;
      gpuTempReady = false;
      rawGotGPU = false;
      rawPowerGPU = rawFrequencyGPU = GPUPower = GPUFrequency = 0f;
      bool stopped = await Task.Run(() => StopHardwareMonitorAndWait(5000));
      if (!stopped) throw new InvalidOperationException(Strings.GpuSleepMonitorStopFailed);
      if (monitorCPU) StartHardwareMonitor();
      UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOff);
    }

    static async Task<bool> RestartAndWaitForD3(GpuSleepManager.GpuDeviceInfo target) {
      ProcessResult result = await Task.Run(() => RestartGpu(target.InstanceId));
      if (result.ExitCode != 0) throw new InvalidOperationException(Strings.GpuSleepRestartFailed(result.Error));
      await Task.Delay(3000);
      for (int i = 0; i < 8; i++) {
        GpuSleepManager.DevicePowerState state = await Task.Run(() => GpuSleepManager.GetDevicePowerState(target.InstanceId));
        if (state == GpuSleepManager.DevicePowerState.D3) return true;
        await Task.Delay(2000);
      }
      return false;
    }

    static void FinishGpuSleepSuccess() {
      SaveConfig("MonitorGPU");
      MessageBox.Show(Strings.GpuSleepSucceeded, Strings.GpuSleepMenu, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void RestoreGpuMonitoringAfterCancelledAttempt(bool oldValue, bool oldGuardValue) {
      gpuSleepGuardActive = oldGuardValue;
      if (!oldValue) return;
      monitorGPU = true;
      gpuTempReady = false;
      SetGpuMonitorState(true);
      UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOn);
    }

    static void RestoreLatestGpuPreferences() {
      string id = GpuSleepManager.GetLatestTransactionId();
      if (string.IsNullOrEmpty(id)) { MessageBox.Show(Strings.GpuPreferenceBackupMissing, Strings.Hint); return; }
      GpuSleepManager.PreferenceTransaction transaction = GpuSleepManager.LoadTransaction(id);
      GpuSleepManager.RestoreResult result = GpuSleepManager.RestorePreferences(id, false);
      if (result.ConflictedPaths.Count > 0 && MessageBox.Show(Strings.GpuPreferenceConflict, Strings.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        result = GpuSleepManager.RestorePreferences(id, true);
      if (!result.Success) { MessageBox.Show(result.Error ?? Strings.GpuPreferenceRestoreFailed, Strings.Error); return; }
      MessageBox.Show(Strings.GpuPreferenceRestoredRestartRequired, Strings.Hint);
      if (transaction != null && MessageBox.Show(Strings.GpuSleepRestartWarning, Strings.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
        var apps = transaction.Entries.Select(e => new GpuSleepManager.GpuProcessInfo { ProcessName = e.ProcessName, ExecutablePath = e.ExecutablePath }).ToList();
        GpuSleepManager.RestartApplications(apps, true);
      }
    }
  }
}
