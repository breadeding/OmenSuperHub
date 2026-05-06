using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using HidSharp.Utility;

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

    public static void RestartGpu() {
      try {
        // 1. 获取 NVIDIA 显卡的设备实例 ID (PNPDeviceID)
        string instanceId = null;
        string query = "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Display'";
        using (var searcher = new System.Management.ManagementObjectSearcher(query)) {
          foreach (System.Management.ManagementObject device in searcher.Get()) {
            string description = device["Description"]?.ToString();
            if (!string.IsNullOrEmpty(description) &&
                description.IndexOf("nvidia", StringComparison.OrdinalIgnoreCase) >= 0) {
              instanceId = device["PNPDeviceID"]?.ToString();
              break;
            }
          }
        }
        if (string.IsNullOrEmpty(instanceId))
          MessageBox.Show($"未找到描述包含 NVIDIA 的显示适配器！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        // 2. 执行 pnputil /restart-device
        var processStartInfo = new ProcessStartInfo {
          FileName = "pnputil",
          Arguments = $"/restart-device \"{instanceId}\"",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };

        using (var process = new Process { StartInfo = processStartInfo }) {
          process.Start();
          string output = process.StandardOutput.ReadToEnd();
          string error = process.StandardError.ReadToEnd();
          process.WaitForExit();
        }
      } catch {
        MessageBox.Show($"重启显卡失败！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
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
