using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Hp.Bridge.Client.SDKs.PerformanceControl.DataStructure;
using HP.Omen.Core.Common.NVidiaApi;
using HP.Omen.Core.Model.Device.Models;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using static OmenSuperHub.GpuAppManager;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  static partial class Program {
    private static List<int> frameRateMap = new List<int>();

    private static void InitFrameRateMap() {
      frameRateMap.Clear();

      frameRateMap.Add(0); // index 0 = 0

      for (int v = 1; v <= 5; v += 1)
        frameRateMap.Add(v);

      for (int v = 5; v <= 60; v += 5)
        frameRateMap.Add(v);

      for (int v = 60; v <= 240; v += 10)
        frameRateMap.Add(v);

      for (int v = 240; v <= 1000; v += 20)
        frameRateMap.Add(v);
    }

    private static int IndexToFrameRate(int index) {
      if (index < 0) return 0;
      if (index >= frameRateMap.Count) return frameRateMap[frameRateMap.Count - 1];
      return frameRateMap[index];
    }

    private static int FrameRateToIndex(int value) {
      int bestIndex = 0;
      int bestDiff = int.MaxValue;

      for (int i = 0; i < frameRateMap.Count; i++) {
        int diff = Math.Abs(frameRateMap[i] - value);
        if (diff < bestDiff) {
          bestDiff = diff;
          bestIndex = i;
        }
      }

      return bestIndex;
    }

    /// <summary>
    /// 获取当前系统 UI 语言对应的语言代码（zh-CN / zh-TW / en）
    /// 若无法匹配，返回 "en"
    /// </summary>
    private static string GetSystemLanguage() {
      string cultureName = CultureInfo.CurrentUICulture.Name;

      // 简体中文：zh-CN, zh-Hans, zh-SG 等
      if (cultureName.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
          cultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
          cultureName.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)) {
        return "zh-CN";
      }
      // 繁体中文：zh-TW, zh-HK, zh-MO, zh-Hant 等
      if (cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
          cultureName.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
          cultureName.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
          cultureName.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)) {
        return "zh-TW";
      }
      // 英语：en, en-US, en-GB 等
      if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase)) {
        return "en";
      }
      // 其他语言默认英语
      return "en";
    }

    /// <summary>
    /// 从注册表加载设备信息缓存（deviceDisplayName / cycleNumber / deviceType / supportDojo）及 alreadyRead。
    /// 若注册表中无对应设备信息项，则从 DeviceModel 获取后写入注册表供后续启动使用。
    /// </summary>
    static void LoadDeviceInfoFromRegistry() {
      // ── 设备信息（缓存命中则直接用，否则从 DeviceModel 获取）────────────────
      bool needFetch = false;
      // ── alreadyRead（无论设备信息是否缓存都要读）────────────────────────────
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            alreadyRead = (int)key.GetValue("AlreadyRead", 0);
            string cachedName = key.GetValue("DeviceDisplayName") as string;
            object cachedCycle = key.GetValue("CycleNumber");
            string cachedType = key.GetValue("DeviceType") as string;
            object cachedDojo = key.GetValue("SupportDojo");
            string cachedSSID = key.GetValue("SystemSSID") as string;
            string cachedSku = key.GetValue("Sku") as string;

            if (!string.IsNullOrEmpty(cachedName)
              && cachedCycle != null
              && !string.IsNullOrEmpty(cachedType)
              && cachedDojo != null
              && !string.IsNullOrEmpty(cachedSSID)
              && !string.IsNullOrEmpty(cachedSku)) {
              deviceDisplayName = cachedName;
              cycleNumber = (int)cachedCycle;
              supportDojo = ((int)cachedDojo) != 0;
              systemSSID = cachedSSID;
              sku = cachedSku;
              if (Enum.TryParse<DeviceEnums.DeviceType>(cachedType, out var parsedType))
                deviceType = parsedType;
              else {
                needFetch = true;
                Logger.Error($"LoadDeviceInfoFromRegistry: 无法解析 DeviceType={cachedType}，将重新从 DeviceModel 获取");
              }
            } else {
              needFetch = true;
            }
          } else {
            needFetch = true; // 全新安装，注册表键不存在
          }
        }
      } catch (Exception ex) {
        needFetch = true;
        Logger.Error($"LoadDeviceInfoFromRegistry: 读取设备信息失败: {ex.Message}");
      }

      if (needFetch)
        FetchAndCacheDeviceInfo();
      else
        System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => FetchAndCacheDeviceInfo());
    }

    /// <summary>
    /// 从 DeviceModel 获取设备信息并写入注册表缓存。
    /// </summary>
    static void FetchAndCacheDeviceInfo() {
      try {
        deviceDisplayName = DeviceModel.OmenPlatform.DisplayName;
        cycleNumber = DeviceModel.GetCycleNumber(DeviceModel.OmenPlatform.ProductNum.FirstOrDefault((SSIDInfo x) => x.SSID.Equals(DeviceModel.ThisSystemID)).Cycle);
        deviceType = DeviceModel.OmenPlatform.Name;
        supportDojo = DeviceModel.OmenPlatform.Feature.Contains("DojoLighting");
        systemSSID = DeviceModel.ThisSystemID;
        sku = PerformanceControlHelper.GetPlatformSku(isInit: true);
        SaveDeviceInfoToRegistry();
      } catch (Exception ex) {
        Logger.Error($"FetchAndCacheDeviceInfo 失败: {ex.Message}");
      }
    }

    /// <summary>
    /// 将设备信息写入注册表缓存。
    /// </summary>
    static void SaveDeviceInfoToRegistry() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub", writable: true)) {
          if (key == null) {
            Logger.Info($"全新安装，暂不保存键值。");
            return;
          }
          key.SetValue("DeviceDisplayName", deviceDisplayName, RegistryValueKind.String);
          key.SetValue("CycleNumber", cycleNumber, RegistryValueKind.DWord);
          key.SetValue("DeviceType", deviceType.ToString(), RegistryValueKind.String);
          key.SetValue("SupportDojo", supportDojo ? 1 : 0, RegistryValueKind.DWord);
          key.SetValue("SystemSSID", systemSSID, RegistryValueKind.String);
          key.SetValue("Sku", sku, RegistryValueKind.String);
        }
      } catch (Exception ex) {
        Logger.Error($"SaveDeviceInfoToRegistry 失败: {ex.Message}");
      }
    }

    static void LoadLanguageSetting() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            // 注册表中有保存的语言 → 使用保存的值
            string savedLang = (string)key.GetValue("AppLanguage", null);
            if (!string.IsNullOrEmpty(savedLang)) {
              appLanguage = savedLang;
            } else {
              // 首次运行，无注册表值 → 使用系统语言
              appLanguage = GetSystemLanguage();
              // 可选：立即将系统语言写入注册表，避免下次启动再检测
              SaveConfig("AppLanguage");
            }
          } else {
            // 注册表键不存在（全新安装）→ 使用系统语言
            appLanguage = GetSystemLanguage();
          }
        }
      } catch { }
      ApplyLanguage(appLanguage);
    }

    static void ApplyLanguage(string lang) {
      switch (lang) {
        case "zh-TW": Strings.Current = AppLanguage.TraditionalChinese; break;
        case "en": Strings.Current = AppLanguage.English; break;
        default: Strings.Current = AppLanguage.SimplifiedChinese; break;
      }
    }

    // 任务计划程序
    static void AutoStartEnable() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string exePath = Path.Combine(currentPath, "OmenSuperHub.exe");

      using (TaskService ts = new TaskService()) {

        // ── 任务一：系统启动时以 SYSTEM 账户启动 ──────────────────────────
        TaskDefinition tdBoot = ts.NewTask();
        tdBoot.RegistrationInfo.Description = "Start OmenSuperHub at system boot";
        tdBoot.Principal.RunLevel = TaskRunLevel.Highest;
        tdBoot.Principal.UserId = "SYSTEM";
        tdBoot.Principal.LogonType = TaskLogonType.ServiceAccount;

        tdBoot.Actions.Add(new ExecAction(exePath, null, null));

        BootTrigger bootTrigger = new BootTrigger();
        // bootTrigger.Delay = TimeSpan.FromSeconds(10); // 可选：延迟启动
        tdBoot.Triggers.Add(bootTrigger);

        tdBoot.Settings.DisallowStartIfOnBatteries = false;
        tdBoot.Settings.StopIfGoingOnBatteries = false;
        tdBoot.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        tdBoot.Settings.AllowHardTerminate = false;

        ts.RootFolder.RegisterTaskDefinition(@"OmenSuperHub", tdBoot);
        //Console.WriteLine("任务一已创建：系统启动时运行。");

        // ── 任务二：用户登录时重启────────────────────────
        TaskDefinition tdLogon = ts.NewTask();
        tdLogon.RegistrationInfo.Description = "Restart OmenSuperHub at user logon";
        tdLogon.Principal.RunLevel = TaskRunLevel.Highest;

        tdLogon.Actions.Add(new ExecAction(
          exePath,
          "--relaunch",  // 传入参数，触发静默重启逻辑
          null
        ));

        LogonTrigger logonTrigger = new LogonTrigger();
        try {
          string currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
          if (!string.IsNullOrWhiteSpace(currentUser))
            logonTrigger.UserId = currentUser;
        } catch { }
        tdLogon.Triggers.Add(logonTrigger);

        tdLogon.Settings.Hidden = true; // 任务本身也隐藏
        tdLogon.Settings.DisallowStartIfOnBatteries = false;
        tdLogon.Settings.StopIfGoingOnBatteries = false;
        tdLogon.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        tdLogon.Settings.AllowHardTerminate = false;

        ts.RootFolder.RegisterTaskDefinition(@"OmenSuperHub_Logon", tdLogon);
        //Console.WriteLine("任务二已创建：用户登录时重启。");
      }

      CleanUpAndRemoveTasks();
    }

    static void AutoStartDisable() {
      using (TaskService ts = new TaskService()) {
        string[] taskNames = { "OmenSuperHub", "OmenSuperHub_Logon" };

        foreach (string taskName in taskNames) {
          Task existingTask = ts.FindTask(taskName);
          if (existingTask != null) {
            ts.RootFolder.DeleteTask(taskName);
            //Console.WriteLine($"任务 {taskName} 已删除。");
          } else {
            //Console.WriteLine($"任务 {taskName} 不存在，无需删除。");
          }
        }
      }
    }

    // 清理旧版自启
    public static void CleanUpAndRemoveTasks() {
      // 目标文件夹和文件定义
      string targetFolder = @"C:\Program Files\OmenSuperHub";
      string taskName = "Omen Boot";
      string file1 = @"C:\Windows\SysWOW64\silent.txt";
      string file2 = @"C:\Windows\SysWOW64\cool.txt";

      // 删除目标文件夹及其内容
      if (Directory.Exists(targetFolder)) {
        string command = $"rd /s /q \"{targetFolder}\"";
        var result = ExecuteCommand(command);
        //Console.WriteLine(result.Output);
      } else {
        //Console.WriteLine("旧文件夹不存在");
      }

      // 删除 file1
      if (File.Exists(file1)) {
        string command = $"del /f /q \"{file1}\"";
        var result = ExecuteCommand(command);
        //Console.WriteLine($"文件已删除: {file1}");
        //Console.WriteLine(result.Output);
      } else {
        //Console.WriteLine($"文件不存在: {file1}");
      }

      // 删除 file2
      if (File.Exists(file2)) {
        string command = $"del /f /q \"{file2}\"";
        var result = ExecuteCommand(command);
        //Console.WriteLine($"文件已删除: {file2}");
        //Console.WriteLine(result.Output);
      } else {
        //Console.WriteLine($"文件不存在: {file2}");
      }

      // 检查并删除计划任务
      string taskQueryCommand = $"schtasks /query /tn \"{taskName}\"";
      var taskQueryResult = ExecuteCommand(taskQueryCommand);
      if (taskQueryResult.ExitCode == 0) {
        string deleteTaskCommand = $"schtasks /delete /tn \"{taskName}\" /f";
        var deleteTaskResult = ExecuteCommand(deleteTaskCommand);
        //Console.WriteLine("已成功删除计划任务 \"Omen Boot\"。");
        //Console.WriteLine(deleteTaskResult.Output);
      } else {
        //Console.WriteLine($"计划任务 \"{taskName}\" 不存在。");
      }

      // 从注册表中删除开机自启项
      string regDeleteCommand = @"reg delete ""HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"" /v ""OmenSuperHub"" /f";
      var regDeleteResult = ExecuteCommand(regDeleteCommand);
      //Console.WriteLine("成功取消开机自启");
      //Console.WriteLine(regDeleteResult.Output);
    }

    static void RestoreCPUPower() {
      // 恢复CPU功耗设定
      if (cpuPower.Contains(" W")) {
        int value = int.Parse(cpuPower.Replace(" W", "").Trim());
        if (isCPUPowerControlSupported && value >= 10 && value <= 254) {
          SetCpuPowerLimit((byte)value);
        }
      }
    }

    static void RestorePowerConfig() {
      SetUnleashMode();
      System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
        RestoreCPUPower();
        SetGpuPowerState(tgpPower == "on", ppabPower == "on", dState == "normal" ? 1 : 2);
        if (tppPower.Contains(" W")) {
          int value = int.Parse(tppPower.Replace(" W", "").Trim());
          if (value >= 20 && value <= 254) {
            SetConcurrentTdp((byte)value);
          }
        }
      });
    }

    static void RestoreFanControl() {
      if (fanControl == "auto") {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(0, 1000);
        UpdateCheckedState("fanControlGroup", Strings.FanAuto);
      } else if (fanControl.Contains("max")) {
        SetMaxFanSpeedOn();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        UpdateCheckedState("fanControlGroup", Strings.FanMax);
      } else if (fanControl.Contains(" RPM")) {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int rpmValue = int.Parse(fanControl.Replace(" RPM", "").Trim());
        SetFanLevel(rpmValue / 100, rpmValue / 100, Is3FanNb);
        if (fanTrackBar != null) {
          fanTrackBar.Value = rpmValue / 100;
        }
        UpdateCheckedState("fanControlGroup", Strings.SetFanSpeedSlider);
      }
    }

    static void InitMaxTemp() {
      maxCPUTemp = null;
      if (platformSettings != null) {
        int throttle = platformSettings.temperatureThrottlingPerformance;
        if (throttle > 0) {
          maxCPUTemp = throttle;
        }
        if (hasNVIDIAGpu) {
          System.Threading.Tasks.Task.Run(() => {
            throttle = GetGpuTemperatureTarget();
            if (throttle > 50) {
              maxGPUTemp = throttle;
            }
          });
        }
      }
    }

    // 从 platformSettings 提取平台最大转速，独立于风扇配置加载，启动时调用一次
    static void InitPlatformMaxFanSpeed() {
      if (platformSettings == null) return;
      int? maxFanSpeed = null;
      var candidates = new[] {
        platformSettings.SwFanControlCustomDefault,
        platformSettings.SwFanControlCustomPerformance,
        platformSettings.SwFanControlCustomUnleashed
      };
      foreach (var fanCustom in candidates) {
        if (fanCustom?.FanTable == null) continue;
        var cpuSpeeds = fanCustom.FanTable.Fan_Table_CPU_Fan_Speed_List;
        var gpuSpeeds = fanCustom.FanTable.Fan_Table_GPU_Fan_Speed_List;
        if (cpuSpeeds != null)
          foreach (var v in cpuSpeeds)
            if (!maxFanSpeed.HasValue || v > maxFanSpeed.Value) maxFanSpeed = v;
        if (gpuSpeeds != null)
          foreach (var v in gpuSpeeds)
            if (!maxFanSpeed.HasValue || v > maxFanSpeed.Value) maxFanSpeed = v;
      }
      if (maxFanSpeed.HasValue)
        platformMaxFanSpeed = maxFanSpeed.Value * 100;
    }

    static FanCurveProfile CreateDefaultFanCurveProfile(bool isSilent) {
      int? tempDelta = null;
      int maxGPUT = 87;
      if (maxGPUTemp.HasValue) {
        maxGPUT = maxGPUTemp.Value;
      }
      if (maxCPUTemp.HasValue) {
        tempDelta = maxCPUTemp.Value - maxGPUT;
      }

      if (platformMaxFanSpeed.HasValue && maxCPUTemp.HasValue && tempDelta.HasValue) {
        int maxRpm = platformMaxFanSpeed.Value;
        int maxCpu = maxCPUTemp.Value;
        int delta = tempDelta.Value;

        List<int> cpuTempList, cpuSpeedList, gpuTempList, gpuSpeedList;

        if (isSilent) {
          // silent: cpu30/gpu20 → 0RPM, 60℃ → maxRpm/3, 87℃ → maxRpm*2/3, maxTemp → maxRpm
          cpuTempList = new List<int> { 30, 60, 87, maxCpu };
          cpuSpeedList = new List<int> { 0, maxRpm / 3, maxRpm * 2 / 3, maxRpm - maxRpm / 10 };
          gpuTempList = new List<int> { 30 - delta, 60 - delta, 87 - delta, maxGPUT };
          gpuSpeedList = new List<int> { 0, maxRpm / 3, maxRpm * 2 / 3, maxRpm - maxRpm / 10 };
        } else {
          // cool: cpu45/gpu35 → maxRpm/4, (maxTemp-5)℃ → maxRpm
          cpuTempList = new List<int> { 45, maxCpu - 5, maxCpu };
          cpuSpeedList = new List<int> { maxRpm / 4, maxRpm, maxRpm + maxRpm / 10 };
          gpuTempList = new List<int> { 45 - delta, maxGPUT - 5, maxGPUT };
          gpuSpeedList = new List<int> { maxRpm / 4, maxRpm, maxRpm + maxRpm / 10 };
        }

        return new FanCurveProfile(
            NormalizeDefaultFanCurve(cpuTempList, cpuSpeedList, maxCpu),
            NormalizeDefaultFanCurve(gpuTempList, gpuSpeedList, maxGPUT));
      }

      int fallbackCpuMaximum = maxCPUTemp ?? 100;
      int fallbackGpuMaximum = maxGPUTemp ?? 90;
      return new FanCurveProfile(
          NormalizeDefaultFanCurve(
              new List<int> { 50, 60, 85, 100 },
              new List<int> { 1600, 2000, 4000, 5600 },
              fallbackCpuMaximum),
          NormalizeDefaultFanCurve(
              new List<int> { 40, 50, 75, 90 },
              new List<int> { 1600, 2000, 4000, 5600 },
              fallbackGpuMaximum));
    }

    static IEnumerable<FanCurvePoint> NormalizeDefaultFanCurve(
        IList<int> temperatures,
        IList<int> fanSpeeds,
        int temperatureMaximum) {
      var points = temperatures
          .Select((temperature, index) => new FanCurvePoint(
              Math.Max(0, Math.Min(temperatureMaximum, temperature)),
              Math.Max(0, fanSpeeds[index])))
          .GroupBy(point => point.Temperature)
          .Select(group => group.Last())
          .OrderBy(point => point.Temperature)
          .ToList();

      if (points.Count < 2) {
        int finalFanSpeed = fanSpeeds.Count > 0 ? Math.Max(0, fanSpeeds[fanSpeeds.Count - 1]) : 5600;
        points.Clear();
        points.Add(new FanCurvePoint(0, 0));
        points.Add(new FanCurvePoint(Math.Max(1, temperatureMaximum), finalFanSpeed));
      }
      return points;
    }

    static void LoadDefaultFanConfig(string filePath) {
      // 只有包含 "silent" 的路径才使用静音模板，否则使用冷静模板（cool / custom 等）
      bool useSilentDefaults = filePath.IndexOf("silent", StringComparison.OrdinalIgnoreCase) >= 0;
      FanCurveProfile profile = CreateDefaultFanCurveProfile(useSilentDefaults);
      profile.Save(filePath);
      LoadFanConfigFromLists(
          profile.CpuPoints.Select(point => point.Temperature).ToList(),
          profile.CpuPoints.Select(point => point.FanSpeed).ToList(),
          profile.GpuPoints.Select(point => point.Temperature).ToList(),
          profile.GpuPoints.Select(point => point.FanSpeed).ToList());
    }

    static void LoadFanConfig(string filePath) {
      string absoluteFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
      if (!File.Exists(absoluteFilePath)) {
        //Logger.Info($"{absoluteFilePath} not found.");
        LoadDefaultFanConfig(absoluteFilePath);
        return;
      }

      string[] allLines = File.ReadAllLines(absoluteFilePath);
      if (allLines.Length == 0) {
        LoadDefaultFanConfig(absoluteFilePath);
        return;
      }

      // 判断文件格式：若第一行包含'='则视为新格式，否则为旧CSV格式
      bool isNewFormat = allLines[0].Contains('=');

      if (isNewFormat) {
        var cpuTempList = new List<int>();
        var cpuSpeedList = new List<int>();
        var gpuTempList = new List<int>();
        var gpuSpeedList = new List<int>();

        foreach (string line in allLines) {
          if (string.IsNullOrWhiteSpace(line)) continue;
          int eqIdx = line.IndexOf('=');
          if (eqIdx < 0) continue;
          string key = line.Substring(0, eqIdx).Trim();
          string valueStr = line.Substring(eqIdx + 1).Trim();
          var values = valueStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => int.Parse(s.Trim()))
                               .ToList();

          switch (key) {
            case "Fan_Table_CPU_Temperature_List":
              cpuTempList = values;
              break;
            case "Fan_Table_CPU_Fan_Speed_List":
              cpuSpeedList = values;
              break;
            case "Fan_Table_GPU_Temperature_List":
              gpuTempList = values;
              break;
            case "Fan_Table_GPU_Fan_Speed_List":
              gpuSpeedList = values;
              break;
          }
        }

        // 校验数据完整性
        if (cpuTempList.Count == 0 || cpuSpeedList.Count == 0 ||
            gpuTempList.Count == 0 || gpuSpeedList.Count == 0 ||
            cpuTempList.Count != cpuSpeedList.Count ||
            gpuTempList.Count != gpuSpeedList.Count) {
          Logger.Error($"{absoluteFilePath} invalid new format, regenerating.");
          LoadDefaultFanConfig(absoluteFilePath);
          return;
        }

        LoadFanConfigFromLists(cpuTempList, cpuSpeedList, gpuTempList, gpuSpeedList);
      } else {
        // 旧格式：CPU,Fan1,Fan2,GPU,Fan1,Fan2 多行
        var cpuTempList = new List<int>();
        var cpuSpeedList = new List<int>();
        var gpuTempList = new List<int>();
        var gpuSpeedList = new List<int>();

        try {
          for (int i = 1; i < allLines.Length; i++) // 跳过标题行
          {
            var parts = allLines[i].Split(',');
            if (parts.Length < 6) continue;
            int cpuTemp = int.Parse(parts[0].Trim());
            int cpuFan1 = int.Parse(parts[1].Trim()); // 我们取Fan1作为统一速度
            int gpuTemp = int.Parse(parts[3].Trim());
            int gpuFan1 = int.Parse(parts[4].Trim());

            cpuTempList.Add(cpuTemp);
            cpuSpeedList.Add(cpuFan1);
            gpuTempList.Add(gpuTemp);
            gpuSpeedList.Add(gpuFan1);
          }
        } catch {
          Logger.Error($"{absoluteFilePath} parse error, regenerating.");
          LoadDefaultFanConfig(absoluteFilePath);
          return;
        }

        if (cpuTempList.Count == 0 || gpuTempList.Count == 0) {
          LoadDefaultFanConfig(absoluteFilePath);
          return;
        }

        // 将旧格式转换为新格式并覆盖写入
        var newLines = new List<string>
        {
            "Fan_Table_CPU_Temperature_List=" + string.Join(",", cpuTempList),
            "Fan_Table_CPU_Fan_Speed_List=" + string.Join(",", cpuSpeedList),
            "Fan_Table_GPU_Temperature_List=" + string.Join(",", gpuTempList),
            "Fan_Table_GPU_Fan_Speed_List=" + string.Join(",", gpuSpeedList)
        };
        File.WriteAllLines(absoluteFilePath, newLines);

        LoadFanConfigFromLists(cpuTempList, cpuSpeedList, gpuTempList, gpuSpeedList);
      }
    }

    static void LoadFanConfigFromLists(List<int> cpuTempList, List<int> cpuSpeedList,
                                   List<int> gpuTempList, List<int> gpuSpeedList) {
      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        GPUTempFanMap.Clear();

        for (int i = 0; i < cpuTempList.Count; i++) {
          int speedRpm = cpuSpeedList[i];
          CPUTempFanMap[cpuTempList[i]] = speedRpm; // 双风扇同速
        }

        for (int i = 0; i < gpuTempList.Count; i++) {
          int speedRpm = gpuSpeedList[i];
          GPUTempFanMap[gpuTempList[i]] = speedRpm;
        }
      }
    }

    // Get fan speed for CPU and GPU and return the maximum
    // 使用平滑后的温度查表，保证高中低档响应速度生效；实时档下平滑温度==原始温度
    // 只有对应监控开启且温度已完成初始化时，才参与风扇转速计算
    static int GetFanSpeedForTemperature() {
      if (CPUTempFanMap.Count == 0 || GPUTempFanMap.Count == 0) return 0;

      // 首次获取到真实温度数据前不进行转速控制，fanControlTimer处理-100将直接return
      int resultSpeed = -100;

      if (tempReady && monitorCPU && cpuTempReady) {
        int cpuFanSpeed = GetFanSpeedForSpecificTemperature(smoothedCPUTemp, CPUTempFanMap);
        resultSpeed = Math.Max(resultSpeed, cpuFanSpeed);
      }

      if (tempReady && monitorGPU && gpuTempReady) {
        int gpuFanSpeed = GetFanSpeedForSpecificTemperature(smoothedGPUTemp, GPUTempFanMap);
        resultSpeed = Math.Max(resultSpeed, gpuFanSpeed);
      }

      // 获取不到温度时使用传感器温度备用
      if (monitorCPU && !monitorGPU) {
        if (CPUPower == 0 && isAmbientSensorSupported) {
          resultSpeed = GetFanSpeedForSpecificTemperature(GetFittingTemperature(), CPUTempFanMap);
        }
      }

      return resultSpeed;
    }

    static void SaveConfig(string configName = null) {
      // 内置预设下调整设置时，不再强制切换到 Custom1，直接保存注册表（不关联任何预设子键）
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            if (configName == null) {
              key.SetValue("CurrentPreset", currentPreset);
              key.SetValue("PresetCustom1Name", presetCustom1Name);
              key.SetValue("PresetCustom2Name", presetCustom2Name);
              key.SetValue("PresetCustom3Name", presetCustom3Name);
              key.SetValue("FanTable", fanTable);
              key.SetValue("FanControl", fanControl);
              key.SetValue("TempSensitivity", tempSensitivity);
              key.SetValue("CpuPower", cpuPower);
              key.SetValue("TgpPower", tgpPower);
              key.SetValue("PpabPower", ppabPower);
              key.SetValue("DState", dState);
              if (hasNVIDIAGpu) {
                key.SetValue("GpuClock", gpuClock);
                key.SetValue("MaxFrameRate", maxFrameRate);
                key.SetValue("DBVersion", DBVersion);
              }
              key.SetValue("AutoStart", autoStart);
              key.SetValue("AlreadyRead", alreadyRead);
              key.SetValue("CustomIcon", customIcon);
              key.SetValue("OmenKey", omenKey);
              key.SetValue("OmenKeyAppPath", omenKeyAppPath);
              key.SetValue("OmenKeyAppName", omenKeyAppName);
              key.SetValue("OmenKeyShortcut", omenKeyShortcut);
              key.SetValue("OmenKeyPresetCandidates", omenKeyPresetCandidates);
              if (hasNVIDIAGpu)
                key.SetValue("MonitorGPU", monitorGPU);
              key.SetValue("MonitorCPU", monitorCPU);
              key.SetValue("MonitorFan", monitorFan);
              key.SetValue("MonitorRefreshRate", monitorRefreshRate);
              key.SetValue("TempDisplayMode", tempDisplayMode);
              key.SetValue("FloatingBarLoc", floatingBarLoc);
              key.SetValue("FloatingBar", floatingBar);
              key.SetValue("FloatingBarScreen", floatingBarScreen);
              key.SetValue("DataLocalize", dataLocalize);
              key.SetValue("AppLanguage", appLanguage);
              key.SetValue("AutoFanProtect", autoFanProtect);
              key.SetValue("TppPower", tppPower);
              //key.SetValue("PL4Power", powerLimit4);
              key.SetValue("IccMax", iccMax);
              key.SetValue("AcLoadLine", acLoadline);
              SavePresetToRegistry(currentPreset);
            } else {
              switch (configName) {
                case "CurrentPreset":
                  key.SetValue("CurrentPreset", currentPreset);
                  break;
                case "PresetCustom1Name":
                  key.SetValue("PresetCustom1Name", presetCustom1Name);
                  break;
                case "PresetCustom2Name":
                  key.SetValue("PresetCustom2Name", presetCustom2Name);
                  break;
                case "PresetCustom3Name":
                  key.SetValue("PresetCustom3Name", presetCustom3Name);
                  break;
                case "FanTable":
                  key.SetValue("FanTable", fanTable);
                  break;
                case "FanControl":
                  key.SetValue("FanControl", fanControl);
                  break;
                case "TempSensitivity":
                  key.SetValue("TempSensitivity", tempSensitivity);
                  break;
                case "CpuPower":
                  key.SetValue("CpuPower", cpuPower);
                  break;
                case "TgpPower":
                  key.SetValue("TgpPower", tgpPower);
                  break;
                case "PpabPower":
                  key.SetValue("PpabPower", ppabPower);
                  break;
                case "DState":
                  key.SetValue("DState", dState);
                  break;
                case "GpuClock":
                  key.SetValue("GpuClock", gpuClock);
                  break;
                case "MaxFrameRate":
                  key.SetValue("MaxFrameRate", maxFrameRate);
                  break;
                case "DBVersion":
                  key.SetValue("DBVersion", DBVersion);
                  break;
                case "AutoStart":
                  key.SetValue("AutoStart", autoStart);
                  break;
                case "AlreadyRead":
                  key.SetValue("AlreadyRead", alreadyRead);
                  break;
                case "CustomIcon":
                  key.SetValue("CustomIcon", customIcon);
                  break;
                case "OmenKey":
                  key.SetValue("OmenKey", omenKey);
                  break;
                case "OmenKeyAppPath":
                  key.SetValue("OmenKeyAppPath", omenKeyAppPath);
                  break;
                case "OmenKeyAppName":
                  key.SetValue("OmenKeyAppName", omenKeyAppName);
                  break;
                case "OmenKeyShortcut":
                  key.SetValue("OmenKeyShortcut", omenKeyShortcut);
                  break;
                case "OmenKeyPresetCandidates":
                  key.SetValue("OmenKeyPresetCandidates", omenKeyPresetCandidates);
                  break;
                case "MonitorGPU":
                  key.SetValue("MonitorGPU", monitorGPU);
                  break;
                case "MonitorCPU":
                  key.SetValue("MonitorCPU", monitorCPU);
                  break;
                case "MonitorFan":
                  key.SetValue("MonitorFan", monitorFan);
                  break;
                case "MonitorRefreshRate":
                  key.SetValue("MonitorRefreshRate", monitorRefreshRate);
                  break;
                case "TempDisplayMode":
                  key.SetValue("TempDisplayMode", tempDisplayMode);
                  break;
                case "FloatingBarSize":
                  key.SetValue("FloatingBarSize", textSize);
                  break;
                case "FloatingBarLoc":
                  key.SetValue("FloatingBarLoc", floatingBarLoc);
                  break;
                case "FloatingBarScreen":
                  key.SetValue("FloatingBarScreen", floatingBarScreen);
                  break;
                case "FloatingBar":
                  key.SetValue("FloatingBar", floatingBar);
                  break;
                case "DataLocalize":
                  key.SetValue("DataLocalize", dataLocalize);
                  break;
                case "TppPower":
                  key.SetValue("TppPower", tppPower);
                  break;
                case "PL4Power":
                  //key.SetValue("PL4Power", powerLimit4);
                  break;
                case "IccMax":
                  key.SetValue("IccMax", iccMax);
                  break;
                case "AcLoadLine":
                  key.SetValue("AcLoadLine", acLoadline);
                  break;
                case "AppLanguage":
                  key.SetValue("AppLanguage", appLanguage);
                  break;
                case "AutoFanProtect":
                  key.SetValue("AutoFanProtect", autoFanProtect);
                  break;
              }
              if (configName == "FanTable" || configName == "FanControl" || configName == "TempSensitivity" || configName == "CpuPower" || configName == "TgpPower" || configName == "PpabPower" || configName == "DState" || configName == "GpuClock" || configName == "MaxFrameRate" || configName == "TppPower" || configName == "IccMax" || configName == "AcLoadLine" ||
                  configName == "MonitorCPU" || configName == "MonitorGPU" || configName == "MonitorFan" || configName == "MonitorRefreshRate" || configName == "TempDisplayMode") {
                SavePresetToRegistry(currentPreset);
              }
            }
          }
        }
      } catch (Exception ex) {
        Logger.Error($"Error saving configuration: {ex.Message}");
      }
    }

    /// <summary>
    /// 将指定预设的字段值加载到内存变量。
    /// 内置预设直接从主注册表键读取；自定义预设从各自子键读取。
    /// 不执行任何硬件操作，不更新 UI。
    /// </summary>
    static void LoadPresetFields(string presetKey) {
      try {
        if (presetKey == "PresetExtreme" || presetKey == "PresetGpuPriority" || presetKey == "PresetLightUse") {
          using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
            if (key == null) return;
            fanTable = (string)key.GetValue("FanTable", fanTable);
            fanControl = (string)key.GetValue("FanControl", "auto");
            tempSensitivity = (string)key.GetValue("TempSensitivity", "high");
            cpuPower = (string)key.GetValue("CpuPower", "null");
            tgpPower = (string)key.GetValue("TgpPower", "on");
            ppabPower = (string)key.GetValue("PpabPower", "on");
            dState = (string)key.GetValue("DState", "normal");
            gpuClock = (int)key.GetValue("GpuClock", 0);
            maxFrameRate = (int)key.GetValue("MaxFrameRate", -1);
            tppPower = (string)key.GetValue("TppPower", "null");
            iccMax = (string)key.GetValue("IccMax", "null");
            acLoadline = (string)key.GetValue("AcLoadLine", "null");
          }
        } else {
          // 自定义预设：从子键读取
          using (RegistryKey key = Registry.CurrentUser.OpenSubKey($@"Software\OmenSuperHub\{presetKey}")) {
            if (key == null) return;
            fanTable = (string)key.GetValue("FanTable", fanTable);
            fanControl = (string)key.GetValue("FanControl", fanControl);
            tempSensitivity = (string)key.GetValue("TempSensitivity", tempSensitivity);
            cpuPower = (string)key.GetValue("CpuPower", cpuPower);
            tgpPower = (string)key.GetValue("TgpPower", tgpPower);
            ppabPower = (string)key.GetValue("PpabPower", ppabPower);
            dState = (string)key.GetValue("DState", dState);
            gpuClock = (int)key.GetValue("GpuClock", gpuClock);
            maxFrameRate = (int)key.GetValue("MaxFrameRate", maxFrameRate);
            tppPower = (string)key.GetValue("TppPower", tppPower);
            iccMax = (string)key.GetValue("IccMax", iccMax);
            acLoadline = (string)key.GetValue("AcLoadLine", acLoadline);
            monitorCPU = Convert.ToBoolean(key.GetValue("MonitorCPU", monitorCPU));
            if (hasNVIDIAGpu)
              monitorGPU = Convert.ToBoolean(key.GetValue("MonitorGPU", monitorGPU));
            monitorFan = Convert.ToBoolean(key.GetValue("MonitorFan", monitorFan));
            monitorRefreshRate = (string)key.GetValue("MonitorRefreshRate", monitorRefreshRate);
            tempDisplayMode = (string)key.GetValue("TempDisplayMode", tempDisplayMode);
          }
        }
      } catch (Exception ex) {
        Logger.Error($"LoadPresetFields({presetKey}): {ex.Message}");
      }
    }

    /// <summary>
    /// 将当前内存中的预设字段应用到硬件，并同步更新菜单勾选状态。
    /// 不读写注册表，可以在启动恢复和运行时切换预设时复用。
    /// </summary>
    static void ApplyPresetSettings(string presetKey) {
      // 自定义预设特有字段：监控项、温度显示模式等
      if (presetKey == "Restore" || presetKey == "PresetCustom1" || presetKey == "PresetCustom2" || presetKey == "PresetCustom3") {
        if (presetKey == "Restore") {
          try {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
              // Restore时已经判断过key
              if (key != null) {
                // 硬件监控：内置预设从主键读取，自定义预设已由 LoadPresetFields 覆盖
                if (currentPreset == "PresetExtreme" || currentPreset == "PresetGpuPriority" || currentPreset == "PresetLightUse") {
                  monitorCPU = Convert.ToBoolean(key.GetValue("MonitorCPU", true));
                  if (hasNVIDIAGpu)
                    monitorGPU = Convert.ToBoolean(key.GetValue("MonitorGPU", true));
                  else
                    monitorGPU = false;
                  monitorFan = Convert.ToBoolean(key.GetValue("MonitorFan", false));
                  monitorRefreshRate = (string)key.GetValue("MonitorRefreshRate", "low");
                  tempDisplayMode = (string)key.GetValue("TempDisplayMode", "smoothed");
                }
              }
            }
          } catch (Exception ex) {
            Logger.Error($"RestoreConfig: {ex.Message}");
          }
        }

        UpdateCheckedState("monitorCPUGroup", monitorCPU ? Strings.MonitorCpuOn : Strings.MonitorCpuOff);
        UpdateCheckedState("monitorGPUGroup", monitorGPU ? Strings.MonitorGpuOn : Strings.MonitorGpuOff);
        UpdateCheckedState("monitorFanGroup", monitorFan ? Strings.MonitorFanOn : Strings.MonitorFanOff);

        bool wasMonitorRunning = hwMonitorProcess != null && !hwMonitorProcess.HasExited;
        if (monitorCPU || monitorGPU) {
          if (!wasMonitorRunning) {
            cpuTempReady = gpuTempReady = tempReady = false;
            StartHardwareMonitor();
          } else {
            if (!monitorCPU) { cpuTempReady = false; rawPowerCPU = 0f; CPUPower = 0f; }
            if (!monitorGPU) { gpuTempReady = false; rawPowerGPU = 0f; GPUPower = 0f; }
            SetCpuMonitorState(monitorCPU);
            SetGpuMonitorState(monitorGPU);
          }
        } else {
          if (wasMonitorRunning) {
            cpuTempReady = gpuTempReady = tempReady = false;
            StopHardwareMonitor();
          }
        }

        switch (monitorRefreshRate) {
          case "high":
            tooltipUpdateTimer.Interval = 250; SetMonitorInterval(250);
            UpdateCheckedState("monitorRefreshGroup", Strings.MonitorRefreshHigh);
            break;
          default:
            monitorRefreshRate = "low";
            tooltipUpdateTimer.Interval = 1000; SetMonitorInterval(1000);
            UpdateCheckedState("monitorRefreshGroup", Strings.MonitorRefreshLow);
            break;
        }

        UpdateCheckedState("tempDisplayGroup", tempDisplayMode == "raw" ? Strings.TempRaw : Strings.TempSmoothed);
        if (tempDisplayMode != "raw") tempDisplayMode = "smoothed";
      }

      // 风扇曲线
      if (fanTable.Contains("cool")) {
        LoadFanConfig("cool.txt");
        UpdateCheckedState("fanTableGroup", Strings.FanCoolMode);
      } else if (fanTable.Contains("silent")) {
        LoadFanConfig("silent.txt");
        UpdateCheckedState("fanTableGroup", Strings.FanSilentMode);
      } else if (fanTable.Contains("custom")) {
        LoadFanConfig("custom.txt");
        UpdateCheckedState("fanTableGroup", Strings.FanCustomMode);
      }

      // 风扇控制模式
      if (fanControl == "auto") {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(0, 1000);
        UpdateCheckedState("fanControlGroup", Strings.FanAuto);
      } else if (fanControl.Contains("max")) {
        SetMaxFanSpeedOn();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        UpdateCheckedState("fanControlGroup", Strings.FanMax);
      } else if (fanControl.Contains(" RPM")) {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int rpmValue = int.Parse(fanControl.Replace(" RPM", "").Trim());
        SetFanLevel(rpmValue / 100, rpmValue / 100, Is3FanNb);
        if (fanTrackBar != null) fanTrackBar.Value = rpmValue / 100;
        UpdateCheckedState("fanControlGroup", Strings.SetFanSpeedSlider);
      }

      // 风扇响应速度
      switch (tempSensitivity) {
        case "realtime": respondSpeed = 1; UpdateCheckedState("tempSensitivityGroup", Strings.FanRespRealtime); break;
        case "high": respondSpeed = 0.4f; UpdateCheckedState("tempSensitivityGroup", Strings.FanRespHigh); break;
        case "medium": respondSpeed = 0.1f; UpdateCheckedState("tempSensitivityGroup", Strings.FanRespMedium); break;
        case "low": respondSpeed = 0.04f; UpdateCheckedState("tempSensitivityGroup", Strings.FanRespLow); break;
      }

      // CPU 功耗
      if (isCPUPowerControlSupported) {
        if (cpuPower == "null") {
          UpdateCheckedState("cpuPowerGroup", Strings.NotSet);
        } else if (cpuPower == "max") {
          SetCpuPowerLimit(254);
          if (cpuPowerTrackBar != null) cpuPowerTrackBar.Value = 254;
          UpdateCheckedState("cpuPowerGroup", Strings.SetCpuPowerSlider);
        } else if (cpuPower.Contains(" W")) {
          int value = int.Parse(cpuPower.Replace(" W", "").Trim());
          if (value >= 5 && value <= 254) {
            SetCpuPowerLimit((byte)value);
            if (cpuPowerTrackBar != null) cpuPowerTrackBar.Value = value;
            UpdateCheckedState("cpuPowerGroup", Strings.SetCpuPowerSlider);
          }
        }
      }

      // GPU 电源状态
      SetGpuPowerState(tgpPower == "on", ppabPower == "on", dState == "normal" ? 1 : 2);
      UpdateCheckedState("tgpPowerGroup", tgpPower == "on" ? Strings.Enable : Strings.Disable);
      UpdateCheckedState("ppabPowerGroup", ppabPower == "on" ? Strings.Enable : Strings.Disable);
      UpdateCheckedState("dStateGroup", dState == "normal" ? Strings.Standard : Strings.LowPower);

      // NVIDIA 专属
      if (hasNVIDIAGpu) {
        if (gpuClockTrackBar != null) {
          if (gpuClock < gpuClockTrackBar.Minimum * 10) {
            System.Threading.Tasks.Task.Run(() => SetGPUClockReset());
            UpdateCheckedState("gpuClockGroup", Strings.Unlimited);
          } else {
            System.Threading.Tasks.Task.Run(() => SetGPUClockLimit(gpuClock));
            gpuClockTrackBar.Value = gpuClock / 10;
            UpdateCheckedState("gpuClockGroup", Strings.SetGpuClockSlider);
          }
        }

        if (maxFrameRateTrackBar != null) {
          if (maxFrameRate >= 0) {
            System.Threading.Tasks.Task.Run(() => NvApiWrapper.NVAPI_SetMaxFrameRate(maxFrameRate));
            maxFrameRateTrackBar.Value = FrameRateToIndex(maxFrameRate);
            UpdateCheckedState("maxFrameRateGroup", Strings.SetMaxFrameRateSlider);
          } else {
            maxFrameRateTrackBar.Value = FrameRateToIndex(NvApiWrapper.NVAPI_GetMaxFrameRate());
            UpdateCheckedState("maxFrameRateGroup", Strings.NotSet);
          }
        }
      }

      // IccMax
      if (iccMax == "null") {
        UpdateCheckedState("iccMaxGroup", Strings.NotSet);
      } else if (iccMax.Contains(" A")) {
        if (int.TryParse(iccMax.Replace(" A", "").Trim(), out int ampVal) && ampVal >= 150 && ampVal <= 350) {
          SetIccMaxByWmi((decimal)ampVal);
          UpdateCheckedState("iccMaxGroup", iccMax);
        }
      }

      // AC Loadline
      if (acLoadline == "null") {
        UpdateCheckedState("acLoadLineGroup", Strings.NotSet);
      } else if (int.TryParse(acLoadline, out int llVal) && llVal >= 1) {
        SetLoadLine(llVal);
        UpdateCheckedState("acLoadLineGroup", (180 - 10 * llVal).ToString());
      }

      // TPP 延迟 1s 应用，避免与其他设置冲突
      string tppSnapshot = tppPower;
      System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
        if (tppSnapshot == "null") {
          UpdateCheckedState("tppPowerGroup", Strings.NotSet);
        } else if (tppSnapshot == "max") {
          SetConcurrentTdp(254);
          if (tppTrackBar != null) tppTrackBar.Value = 254;
        } else if (tppSnapshot.Contains(" W")) {
          int value = int.Parse(tppSnapshot.Replace(" W", "").Trim());
          if (value >= 20 && value <= 254) {
            SetConcurrentTdp((byte)value);
            if (tppTrackBar != null) tppTrackBar.Value = value;
            UpdateCheckedState("tppPowerGroup", Strings.SetTppSlider);
          }
        }
      });
    }

    /// <summary>
    /// 切换预设时调用。设置内置预设的默认字段值（或从注册表读取自定义预设），
    /// 保存到注册表，然后应用到硬件。
    /// </summary>
    static void applyPresetLogic(string targetPreset) {
      currentPreset = targetPreset;

      if (targetPreset == "PresetExtreme" || targetPreset == "PresetGpuPriority" || targetPreset == "PresetLightUse") {
        // 内置预设：先写入默认值，再走通用保存路径
        int targetPL1Perf = (platformSettings?.NbPL1UpperBoundPerformance > 0) ? platformSettings.NbPL1UpperBoundPerformance : 160;
        int targetPL1Default = (platformSettings?.NbPL1UpperBoundDefault > 0) ? platformSettings.NbPL1UpperBoundDefault : 55;

        fanTable = "cool"; fanControl = "auto"; tempSensitivity = "high";
        tgpPower = "on"; ppabPower = "on"; dState = "normal";
        gpuClock = 0; iccMax = "null"; acLoadline = "null";

        switch (targetPreset) {
          case "PresetExtreme":
            cpuPower = $"{targetPL1Perf} W";
            tppPower = $"{targetPL1Perf} W";
            maxFrameRate = 0;
            break;
          case "PresetGpuPriority":
            cpuPower = $"{targetPL1Default} W";
            tppPower = $"{targetPL1Perf} W";
            maxFrameRate = 0;
            break;
          case "PresetLightUse":
            fanTable = "silent";
            cpuPower = $"{(int)(targetPL1Default * 0.6)} W";
            tppPower = "null";
            tgpPower = "off"; ppabPower = "off";
            maxFrameRate = 60;
            break;
        }
      } else {
        // 自定义预设：从注册表读取
        LoadPresetFields(targetPreset);
      }

      SaveConfig();                                          // 持久化到注册表
      var item = FindMenuItemByName(trayIcon.ContextMenuStrip.Items, currentPreset);
      if (item != null)
        UpdateCheckedState("presetsGroup", null, item);
      ApplyPresetSettings(targetPreset);                                 // 应用到硬件 + 刷新其余菜单
      UpdateTrayIconText();
    }

    /// <summary>
    /// 启动时从注册表还原全部配置，包含预设字段和所有非预设配置项。
    /// 仅在程序初始化阶段调用一次。
    /// </summary>
    static void RestoreConfig() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key == null) {
            // 全新安装，无注册表键：应用默认预设
            applyPresetLogic(platformSettings != null ? "PresetExtreme" : "PresetCustom1");
            return;
          }

          // ── 预设字段 ─────────────────────────────────────────────────────────
          currentPreset = (string)key.GetValue("CurrentPreset", platformSettings != null ? "PresetExtreme" : "PresetCustom1");
          presetCustom1Name = (string)key.GetValue("PresetCustom1Name", Strings.PresetCustom1);
          presetCustom2Name = (string)key.GetValue("PresetCustom2Name", Strings.PresetCustom2);
          presetCustom3Name = (string)key.GetValue("PresetCustom3Name", Strings.PresetCustom3);

          // 旧版升级兼容：不存在 CurrentPreset 键时迁移
          if (key.GetValue("CurrentPreset") == null) {
            currentPreset = "PresetCustom1";
            SavePresetToRegistry(currentPreset);
            SaveConfig("CurrentPreset");
          }

          // 内置预设：先按预设逻辑写入默认字段，再用注册表已保存的值覆盖
          // 自定义预设：直接从子键读取
          if (currentPreset == "PresetExtreme" || currentPreset == "PresetGpuPriority" || currentPreset == "PresetLightUse") {
            int targetPL1Perf = (platformSettings?.NbPL1UpperBoundPerformance > 0) ? platformSettings.NbPL1UpperBoundPerformance : 160;
            int targetPL1Default = (platformSettings?.NbPL1UpperBoundDefault > 0) ? platformSettings.NbPL1UpperBoundDefault : 55;
            fanTable = "cool"; fanControl = "auto"; tempSensitivity = "high";
            tgpPower = "on"; ppabPower = "on"; dState = "normal";
            gpuClock = 0; iccMax = "null"; acLoadline = "null";
            switch (currentPreset) {
              case "PresetExtreme":
                cpuPower = $"{targetPL1Perf} W"; tppPower = $"{targetPL1Perf} W"; maxFrameRate = 0;
                break;
              case "PresetGpuPriority":
                cpuPower = $"{targetPL1Default} W"; tppPower = $"{targetPL1Perf} W"; maxFrameRate = 0;
                break;
              case "PresetLightUse":
                fanTable = "silent";
                cpuPower = $"{(int)(targetPL1Default * 0.6)} W"; tppPower = "null";
                tgpPower = "off"; ppabPower = "off"; maxFrameRate = 60;
                break;
            }
            // 用注册表中已保存的值覆盖（上次修改过的字段会被保留）
            fanTable = (string)key.GetValue("FanTable", fanTable);
            fanControl = (string)key.GetValue("FanControl", fanControl);
            tempSensitivity = (string)key.GetValue("TempSensitivity", tempSensitivity);
            cpuPower = (string)key.GetValue("CpuPower", cpuPower);
            tgpPower = (string)key.GetValue("TgpPower", tgpPower);
            ppabPower = (string)key.GetValue("PpabPower", ppabPower);
            dState = (string)key.GetValue("DState", dState);
            gpuClock = (int)key.GetValue("GpuClock", gpuClock);
            maxFrameRate = (int)key.GetValue("MaxFrameRate", maxFrameRate);
            tppPower = (string)key.GetValue("TppPower", tppPower);
            iccMax = (string)key.GetValue("IccMax", iccMax);
            acLoadline = (string)key.GetValue("AcLoadLine", acLoadline);
          } else {
            LoadPresetFields(currentPreset);
          }
          
          var item = FindMenuItemByName(trayIcon.ContextMenuStrip.Items, currentPreset);
          if (item != null)
            UpdateCheckedState("presetsGroup", null, item);
          ApplyPresetSettings("Restore");
          
          // ── DB 版本（仅启动时处理）────────────────────────────────────────────
          if (hasNVIDIAGpu && performanceControlMenu.Enabled) {
            DBVersion = (int)key.GetValue("DBVersion", 2);
            switch (DBVersion) {
              case 1:
                if (IsAbove50Series() || !powerOnline || !CheckDBVersion(1)) {
                  DBVersion = 2;
                  ChangeDBState(true);
                  UpdateCheckedState("DBGroup", Strings.DbNormal);
                } else {
                  countDB = countDBInit + 60;
                  // 启用DB驱动
                  ChangeDBState(true);
                  SetGpuPowerState(true, true);
                  performanceControlMenu.Enabled = false;
                  performanceControlMenu.ToolTipText = Strings.UnavailableReasonTip(countDB + 1);
                  UpdateCheckedState("DBGroup", Strings.DbUnlocked);
                }
                break;
              case 2:
                ChangeDBState(true);
                UpdateCheckedState("DBGroup", Strings.DbNormal);
                break;
            }
          }

          // ── 非预设配置项 ──────────────────────────────────────────────────────
          autoStart = (string)key.GetValue("AutoStart", "off");
          if (autoStart == "on") {
            System.Threading.Tasks.Task.Run(() => AutoStartEnable());
            UpdateCheckedState("autoStartGroup", Strings.Enable);
          } else {
            UpdateCheckedState("autoStartGroup", Strings.Disable);
          }
          
          alreadyRead = (int)key.GetValue("AlreadyRead", 0);

          customIcon = (string)key.GetValue("CustomIcon", "original");
          switch (customIcon) {
            case "original": trayIcon.Icon = Properties.Resources.smallfan; UpdateCheckedState("customIconGroup", Strings.IconOriginal); break;
            case "custom": SetCustomIcon(); UpdateCheckedState("customIconGroup", Strings.IconCustom); break;
            case "dynamic": UpdateDynamicIcon(); UpdateCheckedState("customIconGroup", Strings.IconDynamic); break;
          }

          omenKey = (string)key.GetValue("OmenKey", OmenKeyActions.Default);
          omenKeyAppPath = (string)key.GetValue("OmenKeyAppPath", "");
          omenKeyAppName = (string)key.GetValue("OmenKeyAppName", "");
          omenKeyShortcut = (string)key.GetValue("OmenKeyShortcut", "");
          omenKeyPresetCandidates = (string)key.GetValue("OmenKeyPresetCandidates", GetDefaultOmenKeyPresetCandidates());
          GetOmenKeyPresetCandidateKeys();
          RestoreOmenKeyAction();
          
          textSize = (int)key.GetValue("FloatingBarSize", 40);
          if (textSizeTrackBar != null) textSizeTrackBar.Value = textSize / 4;

          floatingBarLoc = (string)key.GetValue("FloatingBarLoc", "left");
          UpdateCheckedState("floatingBarLocGroup", floatingBarLoc == "left" ? Strings.FloatingLocLeft : Strings.FloatingLocRight);

          floatingBarScreen = (string)key.GetValue("FloatingBarScreen", "");
          floatingBar = (string)key.GetValue("FloatingBar", "off");
          if (floatingBar == "on") {
            uiContext.Post(_ => {
              ShowFloatingForm();
            }, null);
            UpdateCheckedState("floatingBarGroup", Strings.FloatingShow);
          } else {
            uiContext.Post(_ => {
              CloseFloatingForm();
            }, null);
            UpdateCheckedState("floatingBarGroup", Strings.FloatingHide);
          }

          dataLocalize = (string)key.GetValue("DataLocalize", "off");
          UpdateCheckedState("dataLocalizeGroup", dataLocalize == "on" ? Strings.Enable : Strings.Disable);

          autoFanProtect = (string)key.GetValue("AutoFanProtect", "on");
          UpdateCheckedState("autoFanProtectGroup", autoFanProtect == "on" ? Strings.FanAutoProtectOn : Strings.FanAutoProtectOff);

          appLanguage = (string)key.GetValue("AppLanguage", "zh-CN");
          RestoreLanguageChecked();
        }
      } catch (Exception ex) {
        Logger.Error($"RestoreConfig: {ex.Message}");
      }
    }

    /// <summary>
    /// 将自定义预设的当前字段值持久化到注册表子键。
    /// 内置预设不需要单独子键，直接跳过。
    /// </summary>
    static void SavePresetToRegistry(string presetKey) {
      if (presetKey == "PresetExtreme" || presetKey == "PresetGpuPriority" || presetKey == "PresetLightUse") return;
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\OmenSuperHub\{presetKey}")) {
          if (key == null) return;
          key.SetValue("FanTable", fanTable);
          key.SetValue("FanControl", fanControl);
          key.SetValue("TempSensitivity", tempSensitivity);
          key.SetValue("CpuPower", cpuPower);
          key.SetValue("TgpPower", tgpPower);
          key.SetValue("PpabPower", ppabPower);
          key.SetValue("DState", dState);
          key.SetValue("GpuClock", gpuClock);
          key.SetValue("MaxFrameRate", maxFrameRate);
          key.SetValue("TppPower", tppPower);
          key.SetValue("IccMax", iccMax);
          key.SetValue("AcLoadLine", acLoadline);
          key.SetValue("MonitorCPU", monitorCPU);
          if (hasNVIDIAGpu)
            key.SetValue("MonitorGPU", monitorGPU);
          key.SetValue("MonitorFan", monitorFan);
          key.SetValue("MonitorRefreshRate", monitorRefreshRate);
          key.SetValue("TempDisplayMode", tempDisplayMode);
        }
      } catch { }
    }
  }
}
