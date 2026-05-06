using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace OmenSuperHub {
  public static class GpuAppManager {
    public class GpuAppInfo {
      public int ProcessId { get; set; }
      public string ProcessName { get; set; }
    }

    public static List<GpuAppInfo> GetGpuApps() {
      var apps = new List<GpuAppInfo>();
      try {
        var processStartInfo = new ProcessStartInfo {
          FileName = "nvidia-smi",
          Arguments = "--query-compute-apps=pid,process_name --format=csv,noheader",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden
        };

        using (var process = new Process { StartInfo = processStartInfo }) {
          process.Start();
          string output = process.StandardOutput.ReadToEnd();
          process.WaitForExit();

          if (process.ExitCode == 0) {
            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines) {
              string[] parts = line.Split(',');
              if (parts.Length >= 2) {
                if (int.TryParse(parts[0].Trim(), out int pid)) {
                  apps.Add(new GpuAppInfo {
                    ProcessId = pid,
                    ProcessName = parts[1].Trim()
                  });
                }
              }
            }
          }
        }
      } catch { }
      return apps;
    }

    public static void ForceSleepGpu() {
      try {
        // Restart driver using pnputil
        string deviceId = "";
        var processStartInfoDisable = new ProcessStartInfo {
          FileName = "pnputil",
          Arguments = $"/disable-device {deviceId}",
          RedirectStandardOutput = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using (var p = Process.Start(processStartInfoDisable)) p.WaitForExit();

        var processStartInfoEnable = new ProcessStartInfo {
          FileName = "pnputil",
          Arguments = $"/enable-device {deviceId}",
          RedirectStandardOutput = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using (var p = Process.Start(processStartInfoEnable)) p.WaitForExit();
      } catch { }
    }

    public static float[] GetGpuPowerLimits() {
      // Returns [Current Limit, Max Limit]
      var limits = new float[2] { -2f, -2f };
      try {
        var processStartInfo = new ProcessStartInfo {
          FileName = "nvidia-smi",
          Arguments = "-q -d POWER",
          RedirectStandardOutput = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };

        using (var process = new Process { StartInfo = processStartInfo }) {
          process.Start();
          string output = process.StandardOutput.ReadToEnd();
          process.WaitForExit();

          if (process.ExitCode == 0) {
            string currentPattern = @"Current Power Limit\s+:\s+([\d.]+)\s+W";
            string maxPattern = @"Max Power Limit\s+:\s+([\d.]+)\s+W";

            var currentMatch = Regex.Match(output, currentPattern);
            var maxMatch = Regex.Match(output, maxPattern);

            if (currentMatch.Success && maxMatch.Success) {
              limits[0] = float.Parse(currentMatch.Groups[1].Value);
              limits[1] = float.Parse(maxMatch.Groups[1].Value);
            }
          }
        }
      } catch { }
      return limits;
    }
  }
}
