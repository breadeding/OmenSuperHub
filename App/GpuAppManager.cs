using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

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
          MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.DeviceNotFound, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return;
        }

        // 2. 通过 ExecuteCommand 执行 pnputil 重启设备
        string command = $"pnputil /restart-device \"{instanceId}\"";
        ProcessResult result = ExecuteCommand(command);

        // 可选：根据结果给出提示
        if (result.ExitCode != 0) {
          MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), $"{Strings.RestartGPUFailed} {Strings.Error}：{result.Error}", Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
      } catch {
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.RestartGPUFailed, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    /// <summary>
    /// 获取所有显卡名称列表（跳过 Microsoft 基本显示适配器）
    /// </summary>
    public static List<string> GetAllGpuNamesList() {
      var gpuNames = new List<string>();
      try {
        // 增加查询 PNPDeviceID 字段
        using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, PNPDeviceID FROM Win32_VideoController"))
        using (var collection = searcher.Get()) {
          foreach (ManagementObject obj in collection) {
            string name = obj["Name"]?.ToString() ?? "";
            string compatibility = obj["AdapterCompatibility"]?.ToString() ?? "";
            string pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";

            // 1. 过滤物理硬件特征：必须是 PCI 设备（排除 ROOT\ 等虚拟根设备）
            if (!pnpDeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
              continue;

            // 2. 过滤微软基础渲染/远程桌面代理
            if (name.Contains("Microsoft") || compatibility.Contains("Microsoft"))
              continue;

            // 3. 常见的虚拟显卡黑名单关键字（双重保险）
            if (name.Contains("Idd") || name.Contains("Virtual") || name.Contains("spacedesk"))
              continue;

            if (!string.IsNullOrWhiteSpace(name))
              gpuNames.Add(name.Trim());
          }
        }
      } catch (Exception ex) {
        Logger.Error($"GetAllGpuNamesList 异常: {ex.Message}");
      }
      return gpuNames.Distinct().ToList(); // 去重，防止核显独显重复上报
    }

    /// <summary>
    /// 通过 nvidia-smi -L 获取第一个 NVIDIA 显卡的型号名称
    /// </summary>
    public static string GetGpuModelFromNvidiaSmi() {
      var result = ExecuteCommand("nvidia-smi -L");
      if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        return null;

      // 输出格式示例：GPU 0: NVIDIA GeForce RTX 4060 Laptop GPU (UUID: ...)
      // 提取 "GPU 0: " 之后，左括号之前的内容
      var output = result.Output.Trim();
      var colonIndex = output.IndexOf(':');
      if (colonIndex == -1) return null;

      var afterColon = output.Substring(colonIndex + 1).TrimStart();
      var parenIndex = afterColon.IndexOf('(');
      if (parenIndex != -1)
        afterColon = afterColon.Substring(0, parenIndex).TrimEnd();

      return string.IsNullOrEmpty(afterColon) ? null : afterColon;
    }

    /// <summary>
    /// 通过 WMI 枚举所有 NVIDIA 显卡信息（不依赖 nvidia-smi，UMA 模式下仍可检测到硬件）。
    /// </summary>
    public static List<(string Name, int ModelNum)> GetNvidiaGpuInfoList() {
      var result = new List<(string Name, int ModelNum)>();
      try {
        using (var searcher = new ManagementObjectSearcher(
            "SELECT Name FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'")) {
          foreach (ManagementObject obj in searcher.Get()) {
            string name = obj["Name"]?.ToString() ?? "";
            // 从名称中提取第一段纯数字（如 "RTX 4060" → 4060，"RTX 5080" → 5080）
            var m = Regex.Match(name, @"\b(\d{3,})\b");
            int modelNum = m.Success ? int.Parse(m.Value) : -1;
            result.Add((name, modelNum));
          }
        }
        
      } catch (Exception ex) {
        Logger.Error($"WMI GPU query failed: {ex.Message}");
      }
      return result;
    }

    /// <summary>是否存在 NVIDIA 独显。</summary>
    public static bool HasNvidiaGpu() {
      using (RegistryKey key =
          Registry.LocalMachine.OpenSubKey(
          @"SYSTEM\CurrentControlSet\Enum\PCI")) {
        foreach (string device in key.GetSubKeyNames()) {
          if (!device.StartsWith("VEN_10DE",
              StringComparison.OrdinalIgnoreCase))
            continue;

          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// 是否为 50 系及以上显卡。
    /// 若检测不到显卡，返回 true。
    /// </summary>
    public static bool IsAbove50Series() {
      var gpus = GetNvidiaGpuInfoList();
      if (gpus.Count == 0) return true;   // 检测不到时保守处理
      return gpus.All(g => g.ModelNum >= 5000);
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
    public static void SetGPUClockLimit(int freq) {
      ExecuteCommand("nvidia-smi --lock-gpu-clocks=0," + freq);
    }

    public static void SetGPUClockReset() {
      ExecuteCommand("nvidia-smi --reset-gpu-clocks");
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
        // 直接匹配第一行的 NVIDIA-SMI 版本号
        string pattern = @"NVIDIA-SMI\s+(\d+\.\d+)";
        Match match = Regex.Match(result.Output, pattern);
        string version = match.Success ? match.Groups[1].Value : null;

        if (version != null) {
          Version v1 = new Version(version);
          Version v2 = new Version("537.42");
          Version v3 = new Version("610.47");
          //if(kind == 2)
          //  v2 = new Version("555.99");
          if (v1 >= v2 && v1 < v3) {
            return true;
          } else {
            MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.DriverNotAllow + version, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
          }
        } else {
          MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.DriverNotFound, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return false;
        }
      } else {
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.CheckDriverFailed, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    public static void ChangeDBState(bool State) {
      if (State) {
        ExecuteCommand($"pnputil /enable-device \"ACPI\\NVDA0820\\NPCF\"");
      } else {
        ExecuteCommand($"pnputil /disable-device \"ACPI\\NVDA0820\\NPCF\"");
      }
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
