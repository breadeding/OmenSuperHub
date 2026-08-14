using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OmenSuperHub {
  public static class GpuSleepManager {
    const string PreferencesPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string TransactionsPath = @"Software\OmenSuperHub\GpuPreferenceTransactions";
    static Guid displayClassGuid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");

    public enum DevicePowerState { Unknown, D0, D1, D2, D3 }
    public sealed class GpuDeviceInfo {
      public string Name, InstanceId;
      public bool IsEnabled, IsPhysical, IsNvidia, HasCurrentDisplayMode;
      public DevicePowerState PowerState;
    }
    public sealed class GpuProcessInfo {
      public int ProcessId;
      public string ProcessName, ExecutablePath, UsageType, PreferenceRawValue, PreferenceDisplay;
      public bool IsSystemProcess, CanConfigurePreference, PreferenceEntryExisted;
    }
    public sealed class PreferenceEntry {
      public string ExecutablePath, ProcessName, OriginalValue, AppliedValue;
      public bool OriginalEntryExisted;
    }
    public sealed class PreferenceTransaction {
      public string Id;
      public readonly List<PreferenceEntry> Entries = new List<PreferenceEntry>();
    }
    public sealed class RestoreResult {
      public bool Success;
      public string Error;
      public readonly List<string> RestoredPaths = new List<string>();
      public readonly List<string> ConflictedPaths = new List<string>();
    }

    public static List<GpuDeviceInfo> GetDisplayAdapters() {
      var result = new List<GpuDeviceInfo>();
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
        using (var objects = searcher.Get()) {
          foreach (ManagementObject item in objects) {
            string id = item["PNPDeviceID"]?.ToString() ?? "";
            string name = item["Name"]?.ToString() ?? "";
            int error = -1;
            if (item["ConfigManagerErrorCode"] != null) int.TryParse(item["ConfigManagerErrorCode"].ToString(), out error);
            result.Add(new GpuDeviceInfo {
              Name = name,
              InstanceId = id,
              IsEnabled = error == 0,
              IsPhysical = id.StartsWith("PCI" + (char)92, StringComparison.OrdinalIgnoreCase) && !ContainsAny(name, "Microsoft", "Remote", "Virtual"),
              IsNvidia = id.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0 || ContainsAny(name, "NVIDIA"),
              HasCurrentDisplayMode = item["CurrentHorizontalResolution"] != null,
              PowerState = GetDevicePowerState(id)
            });
          }
        }
      } catch (Exception ex) { Logger.Error("GPU enumeration failed: " + ex.Message); }
      return result;
    }

    public static bool TryGetNvidiaSleepTarget(out GpuDeviceInfo target, out string reason) {
      target = null;
      reason = null;
      List<GpuDeviceInfo> enabled = GetDisplayAdapters().Where(g => g.IsPhysical && g.IsEnabled).ToList();
      if (enabled.Count < 2) { reason = Strings.GpuSleepNeedsSecondGpu; return false; }
      target = enabled.FirstOrDefault(g => g.IsNvidia);
      if (target == null) { reason = Strings.GpuSleepTargetNotFound; return false; }
      if (target.HasCurrentDisplayMode) { reason = Strings.GpuSleepTargetHasActiveDisplay; return false; }
      string targetId = target.InstanceId;
      if (!enabled.Any(g => !Same(g.InstanceId, targetId) && g.HasCurrentDisplayMode)) {
        reason = Strings.GpuSleepNoActiveFallbackGpu;
        return false;
      }
      return true;
    }

    public static List<GpuProcessInfo> GetNvidiaProcesses() {
      var found = new Dictionary<int, GpuProcessInfo>();
      try {
        var command = GpuAppManager.ExecuteCommand("nvidia-smi");
        if (command.ExitCode == 0) {
          var rx = new Regex(@"^\|\s*\d+\s+\S+\s+\S+\s+(?<pid>\d+)\s+(?<type>[A-Z+]+)\s+(?<name>.+?)\s+(?:N/A|\d+MiB)\s*\|\s*$", RegexOptions.IgnoreCase);
          foreach (string line in command.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            Match m = rx.Match(line);
            if (m.Success && int.TryParse(m.Groups["pid"].Value, out int pid)) AddProcess(found, pid, m.Groups["name"].Value.Trim(), m.Groups["type"].Value);
          }
        }
      } catch (Exception ex) { Logger.Error("NVIDIA process query failed: " + ex.Message); }
      try {
        var command = GpuAppManager.ExecuteCommand("nvidia-smi --query-compute-apps=pid,process_name --format=csv,noheader");
        if (command.ExitCode == 0) foreach (string line in command.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
          int comma = line.IndexOf(',');
          if (comma > 0 && int.TryParse(line.Substring(0, comma).Trim(), out int pid)) AddProcess(found, pid, line.Substring(comma + 1).Trim(), "C");
        }
      } catch { }
      foreach (GpuProcessInfo process in found.Values) PopulateProcess(process);
      return found.Values.OrderBy(p => p.IsSystemProcess).ThenBy(p => p.ProcessName).ToList();
    }

    public static string ApplyPowerSavingPreferences(IEnumerable<GpuProcessInfo> processes) {
      List<GpuProcessInfo> selected = processes.Where(p => p != null && p.CanConfigurePreference)
          .GroupBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
      if (selected.Count == 0) return null;
      string id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
      using (RegistryKey transaction = Registry.CurrentUser.CreateSubKey(TransactionsPath + (char)92 + id))
      using (RegistryKey preferences = Registry.CurrentUser.CreateSubKey(PreferencesPath)) {
        transaction.SetValue("CreatedUtc", DateTime.UtcNow.ToString("O"));
        int index = 0;
        foreach (GpuProcessInfo process in selected) {
          object originalObject = preferences.GetValue(process.ExecutablePath, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
          string original = originalObject?.ToString() ?? "";
          string applied = SetPreference(original, 1);
          using (RegistryKey entry = transaction.CreateSubKey((index++).ToString())) {
            entry.SetValue("Path", process.ExecutablePath);
            entry.SetValue("Name", process.ProcessName ?? "");
            entry.SetValue("Existed", originalObject != null ? 1 : 0);
            entry.SetValue("Original", original);
            entry.SetValue("Applied", applied);
          }
          preferences.SetValue(process.ExecutablePath, applied, RegistryValueKind.String);
        }
      }
      return id;
    }

    public static PreferenceTransaction LoadTransaction(string id) {
      if (string.IsNullOrWhiteSpace(id)) return null;
      using (RegistryKey transaction = Registry.CurrentUser.OpenSubKey(TransactionsPath + (char)92 + id)) {
        if (transaction == null) return null;
        var result = new PreferenceTransaction { Id = id };
        foreach (string child in transaction.GetSubKeyNames().OrderBy(n => n)) using (RegistryKey entry = transaction.OpenSubKey(child)) {
          result.Entries.Add(new PreferenceEntry {
            ExecutablePath = entry.GetValue("Path", "").ToString(),
            ProcessName = entry.GetValue("Name", "").ToString(),
            OriginalEntryExisted = Convert.ToInt32(entry.GetValue("Existed", 0)) != 0,
            OriginalValue = entry.GetValue("Original", "").ToString(),
            AppliedValue = entry.GetValue("Applied", "").ToString()
          });
        }
        return result;
      }
    }

    public static string GetLatestTransactionId() {
      using (RegistryKey root = Registry.CurrentUser.OpenSubKey(TransactionsPath)) return root?.GetSubKeyNames().OrderByDescending(n => n).FirstOrDefault();
    }

    public static RestoreResult RestorePreferences(string id, bool overwriteConflicts) {
      var result = new RestoreResult();
      try {
        PreferenceTransaction transaction = LoadTransaction(id);
        if (transaction == null) { result.Error = Strings.GpuPreferenceBackupMissing; return result; }
        using (RegistryKey preferences = Registry.CurrentUser.CreateSubKey(PreferencesPath)) {
          if (!overwriteConflicts) foreach (PreferenceEntry entry in transaction.Entries) {
            string current = preferences.GetValue(entry.ExecutablePath, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            if (!Same(current, entry.AppliedValue)) result.ConflictedPaths.Add(entry.ExecutablePath);
          }
          if (result.ConflictedPaths.Count > 0) return result;
          foreach (PreferenceEntry entry in transaction.Entries) {
            if (entry.OriginalEntryExisted) preferences.SetValue(entry.ExecutablePath, entry.OriginalValue ?? "", RegistryValueKind.String);
            else preferences.DeleteValue(entry.ExecutablePath, false);
            result.RestoredPaths.Add(entry.ExecutablePath);
          }
        }
        if (result.ConflictedPaths.Count == 0 || overwriteConflicts) {
          Registry.CurrentUser.DeleteSubKeyTree(TransactionsPath + (char)92 + id, false);
          result.Success = true;
        }
      } catch (Exception ex) { result.Error = ex.Message; Logger.Error("GPU preference restore failed: " + ex.Message); }
      return result;
    }

    public static void RestartApplications(IEnumerable<GpuProcessInfo> applications, bool force) {
      foreach (GpuProcessInfo app in applications.GroupBy(p => p.ExecutablePath ?? p.ProcessName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())) {
        try {
          string processName = (app.ProcessName ?? "").Replace(".exe", "").Trim();
          if (Same(processName, "dwm")) {
            Process dwm = app.ProcessId > 0 ? Process.GetProcessById(app.ProcessId) : Process.GetProcessesByName("dwm").FirstOrDefault();
            dwm?.Kill();
            continue;
          }
          foreach (Process process in Process.GetProcessesByName(processName)) {
            bool closed = false;
            try { if (process.CloseMainWindow()) closed = process.WaitForExit(3000); } catch { }
            if (!closed && force) try { process.Kill(); process.WaitForExit(3000); } catch { }
          }
          if (Same(processName, "explorer")) Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe")) { UseShellExecute = true });
          else if (!string.IsNullOrWhiteSpace(app.ExecutablePath) && System.IO.File.Exists(app.ExecutablePath)) Process.Start(new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(app.ExecutablePath) });
        } catch (Exception ex) { Logger.Error("Application restart failed for " + app.ProcessName + ": " + ex.Message); }
      }
    }

    public static DevicePowerState GetDevicePowerState(string instanceId) {
      if (string.IsNullOrWhiteSpace(instanceId)) return DevicePowerState.Unknown;
      IntPtr set = SetupDiGetClassDevs(ref displayClassGuid, null, IntPtr.Zero, 2);
      if (set == new IntPtr(-1)) return DevicePowerState.Unknown;
      try {
        uint index = 0;
        var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
        while (SetupDiEnumDeviceInfo(set, index++, ref data)) {
          var id = new StringBuilder(512);
          if (!SetupDiGetDeviceInstanceId(set, ref data, id, id.Capacity, out _) || !Same(id.ToString(), instanceId)) continue;
          byte[] buffer = new byte[128];
          if (!SetupDiGetDeviceRegistryProperty(set, ref data, 0x1E, out _, buffer, (uint)buffer.Length, out uint needed) || needed < 8) return DevicePowerState.Unknown;
          int state = BitConverter.ToInt32(buffer, 4);
          return state >= 1 && state <= 4 ? (DevicePowerState)state : DevicePowerState.Unknown;
        }
      } finally { SetupDiDestroyDeviceInfoList(set); }
      return DevicePowerState.Unknown;
    }

    static void AddProcess(Dictionary<int, GpuProcessInfo> found, int pid, string name, string usage) {
      if (!found.TryGetValue(pid, out GpuProcessInfo process)) found[pid] = new GpuProcessInfo { ProcessId = pid, ProcessName = name, UsageType = usage };
      else if ((process.UsageType ?? "").IndexOf(usage, StringComparison.OrdinalIgnoreCase) < 0) process.UsageType += "+" + usage;
    }

    static void PopulateProcess(GpuProcessInfo info) {
      try { using (Process process = Process.GetProcessById(info.ProcessId)) { info.ProcessName = process.ProcessName + ".exe"; try { info.ExecutablePath = process.MainModule?.FileName; } catch { } } } catch { }
      string name = (info.ProcessName ?? "").ToLowerInvariant();
      info.IsSystemProcess = name == "dwm.exe" || name == "explorer.exe" || name == "csrss.exe" || name == "winlogon.exe" || name == "system";
      info.CanConfigurePreference = !string.IsNullOrWhiteSpace(info.ExecutablePath) && name != "csrss.exe" && name != "winlogon.exe" && name != "system";
      using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PreferencesPath)) {
        object raw = key?.GetValue(info.ExecutablePath ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        info.PreferenceEntryExisted = raw != null;
        info.PreferenceRawValue = raw?.ToString() ?? "";
      }
      Match match = Regex.Match(info.PreferenceRawValue, @"(?:^|;)\s*GpuPreference=(\d+)", RegexOptions.IgnoreCase);
      info.PreferenceDisplay = !match.Success ? Strings.GpuPreferenceSystemDefault : match.Groups[1].Value == "1" ? Strings.GpuPreferencePowerSaving : match.Groups[1].Value == "2" ? Strings.GpuPreferenceHighPerformance : Strings.GpuPreferenceSystemDefault;
    }

    static string SetPreference(string raw, int preference) {
      string value = Regex.Replace(raw ?? "", @"(?:^|;)\s*GpuPreference=\d+;?", ";", RegexOptions.IgnoreCase).Trim(';', ' ');
      if (value.Length > 0) value += ";";
      return value + "GpuPreference=" + preference + ";";
    }
    static bool ContainsAny(string value, params string[] terms) { return terms.Any(t => (value ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0); }
    static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
  }
}
