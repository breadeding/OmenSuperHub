using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OmenSuperHub {
  public static class GpuAppManager {
    public class GpuAppInfo {
      public int ProcessId { get; set; }
      public string ProcessName { get; set; }
    }

    public static List<GpuAppInfo> GetGpuApps() {
      var apps = new List<GpuAppInfo>();
      try {
        // 直接构建命令字符串
        string command = "nvidia-smi --query-compute-apps=pid,process_name --format=csv,noheader";
        ProcessResult result = ExecuteCommand(command);

        if (result.ExitCode == 0) {
          string[] lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
          foreach (string line in lines) {
            string[] parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out int pid)) {
              apps.Add(new GpuAppInfo {
                ProcessId = pid,
                ProcessName = parts[1].Trim()
              });
            }
          }
        }
      } catch { }
      return apps;
    }

    public static void RestartGpu() {
      try {
        // 1. WMI 查询获取 NVIDIA 显卡的 PNPDeviceID（保持不变）
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

        if (string.IsNullOrEmpty(instanceId)) {
          MessageBox.Show("未找到描述包含 NVIDIA 的显示适配器！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return;
        }

        // 2. 通过 ExecuteCommand 执行 pnputil 重启设备
        string command = $"pnputil /restart-device \"{instanceId}\"";
        ProcessResult result = ExecuteCommand(command);

        // 可选：根据结果给出提示
        if (result.ExitCode != 0) {
          MessageBox.Show($"重启显卡失败！错误信息：{result.Error}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
      } catch {
        MessageBox.Show("重启显卡失败！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    // 获取显卡数字代号
    public static string GetNVIDIAModel() {
      // 执行 nvidia-smi 命令并获取输出
      var result = ExecuteCommand("nvidia-smi --query-gpu=name --format=csv");

      // 检查命令是否成功执行
      if (result.ExitCode == 0) {

        string gpuModel;

        string output = result.Output;

        string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string modelName = null;
        // 检查是否有至少两行
        if (lines.Length > 1) {
          modelName = lines[1]; // 返回第二行
        }

        // 定义正则表达式以匹配第一个以数字开头的部分
        string pattern = @"\b(\d[\w\d\-]*)\b";

        // 查找第一个匹配项
        var match = Regex.Match(output, pattern);
        if (match.Success) {
          gpuModel = match.Groups[1].Value; // 返回匹配到的代号部分
          //if(modelName != null)
          //  MessageBox.Show($"显卡型号为：{gpuModel}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          //Console.WriteLine($"First GPU Model Code: {gpuModel}");
          return gpuModel;
        } else {
          Logger.Error("GPU model code not found.");
        }
      } else {
        Logger.Error($"Error executing command: {result.Error}");
      }

      return null;
    }

    public static float[] GetGpuPowerLimits() {
      // Returns [Current Limit, Max Limit]
      var limits = new float[2] { -2f, -2f };
      try {
        ProcessResult result = ExecuteCommand("nvidia-smi -q -d POWER");

        if (result.ExitCode == 0) {
          string currentPattern = @"Current Power Limit\s+:\s+([\d.]+)\s+W";
          string maxPattern = @"Max Power Limit\s+:\s+([\d.]+)\s+W";

          var currentMatch = Regex.Match(result.Output, currentPattern);
          var maxMatch = Regex.Match(result.Output, maxPattern);

          if (currentMatch.Success && maxMatch.Success) {
            limits[0] = float.Parse(currentMatch.Groups[1].Value);
            limits[1] = float.Parse(maxMatch.Groups[1].Value);
          }
        }
      } catch { }
      return limits;
    }

    // 设置显卡频率限制
    public static bool SetGPUClockLimit(int freq) {
      if (freq < 210) {
        ExecuteCommand("nvidia-smi --reset-gpu-clocks");
        return false;
      } else {
        ExecuteCommand("nvidia-smi --lock-gpu-clocks=0," + freq);
        return true;
      }
    }

    public static int GetGpuTemperatureTarget() {
      int limit = -2;
      try {
        ProcessResult result = ExecuteCommand("nvidia-smi -q -d TEMPERATURE");
        if (result.ExitCode == 0) {
          // 匹配形如 "GPU Target Temperature               : 87 C"
          string targetPattern = @"GPU Target Temperature\s+:\s+(\d+)\s+C";
          var targetMatch = Regex.Match(result.Output, targetPattern);
          if (targetMatch.Success) {
            limit = int.Parse(targetMatch.Groups[1].Value);
          }
        }
      } catch { }
      return limit;
    }

    public static bool CheckDBVersion(int kind) {
      ProcessResult result = ExecuteCommand("nvidia-smi");

      if (result.ExitCode == 0) {
        string pattern = @"Driver Version:\s*(\d+\.\d+)";
        Match match = Regex.Match(result.Output, pattern);
        string version = match.Success ? match.Groups[1].Value : null;

        if (version != null) {
          Version v1 = new Version(version);
          Version v2 = new Version("537.42");
          //if(kind == 2)
          //  v2 = new Version("555.99");
          if (v1.CompareTo(v2) >= 0) {
            //MessageBox.Show("当前显卡驱动：" + version, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
          } else {
            MessageBox.Show("请安装新版显卡驱动", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
          }
        } else {
          MessageBox.Show($"无法找到 NVIDIA 显卡驱动版本", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return false;
        }
      } else {
        MessageBox.Show($"查询显卡驱动失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
      }
    }

    public static void ChangeDBVersion(int kind) {
      string infFileName = "nvpcf.inf";
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;

      // 提取资源中的nvpcf文件到当前目录
      string extractedInfFilePath = Path.Combine(currentPath, "nvpcf.inf");
      string extractedSysFilePath = Path.Combine(currentPath, "nvpcf.sys");
      string extractedCatFilePath = Path.Combine(currentPath, "nvpcf.CAT");

      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_inf.inf", extractedInfFilePath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_sys.sys", extractedSysFilePath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_cat.CAT", extractedCatFilePath);

      string targetVersion = "08/28/2023 31.0.15.3730";
      string driverFile = Path.Combine(currentPath, "nvpcf.inf");
      //if (kind == 2) {
      //  targetVersion = "03/02/2024, 32.0.15.5546";
      //  driverFile = Path.Combine(currentPath, "nvpcf.inf_560.70", "nvpcf.inf");
      //}

      bool hasVersion = false;

      //string tempFilePath = Path.Combine(Path.GetTempPath(), "pnputil_output.txt");
      //string command = $"pnputil /enum-drivers > \"{tempFilePath}\"";
      //ExecuteCommand(command);
      //string output = File.ReadAllText(tempFilePath);
      //// 读取驱动程序列表文件
      //var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

      string command = "pnputil /enum-drivers";
      var result = ExecuteCommand(command);
      string output = result.Output;

      // 读取驱动程序列表文件
      var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      //try {
      //  File.WriteAllLines(Path.Combine(currentPath, "driver.txt"), lines);
      //} catch (Exception ex) {
      //  Console.WriteLine($"Error: {ex.Message}");
      //}

      // 记录需要删除的 Published Name
      var namesToDelete = new List<string>();
      for (int i = 0; i < lines.Length; i++) {
        if (lines[i].Contains($":      {infFileName}")) {
          // 记录上一行的 Published Name
          if (i > 0 && lines[i - 1].Contains(":")) {
            string publishedName = lines[i - 1].Split(':')[1].Trim();

            // 记录 +4 行的 Driver Version
            if (i + 4 < lines.Length && lines[i + 4].Contains(":")) {
              string driverVersion = lines[i + 4].Split(':')[1].Trim();

              if (driverVersion != targetVersion) {
                //Console.WriteLine("发现其他版本: " + driverVersion);
                namesToDelete.Add(publishedName);
              } else {
                hasVersion = true;
                //Console.WriteLine("已经存在所需版本!");
              }
            }
          }
        }
      }

      if (!hasVersion) {
        ExecuteCommand($"pnputil /add-driver \"{driverFile}\" /install /force");
        //Console.WriteLine("成功更改DB版本!");
      }

      if (namesToDelete.Count > 0) {
        //Console.WriteLine("找到需要删除的驱动程序包:");
        foreach (var name in namesToDelete) {
          //Console.WriteLine($"删除驱动程序包: {name}");
          ExecuteCommand($"pnputil /delete-driver \"{name}\" /uninstall /force");
        }
      } else {
        //Console.WriteLine("没有需要删除的驱动程序包.");
      }

      // 清理临时文件
      //File.Delete(driversListFile);

      // 删除提取的nvpcf文件
      DeleteExtractedFiles(extractedInfFilePath);
      DeleteExtractedFiles(extractedSysFilePath);
      DeleteExtractedFiles(extractedCatFilePath);

      //Console.WriteLine("操作完成.");
    }

    static void ExtractResourceToFile(string resourceName, string outputFilePath) {
      using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)) {
        if (resourceStream != null) {
          using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Create)) {
            resourceStream.CopyTo(fileStream);
          }
          //Logger.Info($"资源文件已提取到: {outputFilePath}");
        } else {
          Logger.Error($"无法找到资源: {resourceName}");
        }
      }
    }

    static void DeleteExtractedFiles(string filePath) {
      // 删除提取的文件
      if (File.Exists(filePath)) {
        File.Delete(filePath);
        //Console.WriteLine($"删除临时文件:{filePath}");
      }
    }

    public static ProcessResult ExecuteCommand(string command) {
      var processStartInfo = new ProcessStartInfo {
        FileName = "cmd.exe",
        Arguments = $"/c {command}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
      };

      using (var process = new Process { StartInfo = processStartInfo }) {
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult {
          ExitCode = process.ExitCode,
          Output = output,
          Error = error
        };
      }
    }

    public class ProcessResult {
      public int ExitCode { get; set; }
      public string Output { get; set; }
      public string Error { get; set; }
    }
  }
}
