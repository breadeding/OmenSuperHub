using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Newtonsoft.Json.Linq;
using static OmenSuperHub.GpuAppManager;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  static partial class Program {
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
        if (value >= 10 && value <= 254) {
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
        if (fanTrackBar != null && rpmValue / 100 >= fanTrackBar.Minimum && rpmValue / 100 <= fanTrackBar.Maximum) {
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
          throttle = GetGpuTemperatureTarget();
          if (throttle > 50) {
            maxGPUTemp = throttle;
          }
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

    static void LoadDefaultFanConfig(string filePath) {
      // ── 1. 获取 CPU 与 GPU 的允许最高温度差 ─────────────────────
      int? tempDelta = null;
      int maxGPUT = 87;
      if (maxGPUTemp.HasValue) {
        maxGPUT = maxGPUTemp.Value;
      }
      if (maxCPUTemp.HasValue) {
        tempDelta = maxCPUTemp.Value - maxGPUT;
      }

      // ── 2. 若两个值均获取成功，则生成 silent / cool 转速表 ──────────────
      if (platformMaxFanSpeed.HasValue && maxCPUTemp.HasValue && tempDelta.HasValue) {
        int maxRpm = platformMaxFanSpeed.Value;
        int maxCpu = maxCPUTemp.Value;
        int delta = tempDelta.Value;

        List<int> cpuTempList, cpuSpeedList, gpuTempList, gpuSpeedList;

        bool isSilent = filePath.IndexOf("silent", StringComparison.OrdinalIgnoreCase) >= 0;

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

        // 写入文件
        var lines = new List<string> {
      "Fan_Table_CPU_Temperature_List=" + string.Join(",", cpuTempList),
      "Fan_Table_CPU_Fan_Speed_List="   + string.Join(",", cpuSpeedList),
      "Fan_Table_GPU_Temperature_List=" + string.Join(",", gpuTempList),
      "Fan_Table_GPU_Fan_Speed_List="   + string.Join(",", gpuSpeedList)
    };
        File.WriteAllLines(filePath, lines);

        LoadFanConfigFromLists(cpuTempList, cpuSpeedList, gpuTempList, gpuSpeedList);
        return;
      }

      // ── 3. 兜底：无法提取参数时使用硬编码默认值 ─────────────────────────
      GenerateDefaultMapping(filePath);
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

    // Generate default temperature-fan speed mapping
    static void GenerateDefaultMapping(string filePath) {
      // 硬编码默认映射（与原逻辑一致，转换为新格式）
      var cpuTempList = new List<int> { 50, 60, 85, 100 };
      var cpuSpeedList = new List<int> { 1600, 2000, 4000, 5600 };   // RPM
      var gpuTempList = new List<int> { 40, 50, 75, 90 };
      var gpuSpeedList = new List<int> { 1600, 2000, 4000, 5600 };

      var lines = new List<string>
      {
        "Fan_Table_CPU_Temperature_List=" + string.Join(",", cpuTempList),
        "Fan_Table_CPU_Fan_Speed_List=" + string.Join(",", cpuSpeedList),
        "Fan_Table_GPU_Temperature_List=" + string.Join(",", gpuTempList),
        "Fan_Table_GPU_Fan_Speed_List=" + string.Join(",", gpuSpeedList)
    };
      File.WriteAllLines(filePath, lines);

      LoadFanConfigFromLists(cpuTempList, cpuSpeedList, gpuTempList, gpuSpeedList);
    }

    static void LoadFanConfigFromLists(List<int> cpuTempList, List<int> cpuSpeedList,
                                   List<int> gpuTempList, List<int> gpuSpeedList) {
      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        GPUTempFanMap.Clear();

        for (int i = 0; i < cpuTempList.Count; i++) {
          int speedRpm = cpuSpeedList[i];
          CPUTempFanMap[cpuTempList[i]] = new List<int> { speedRpm, speedRpm }; // 双风扇同速
        }

        for (int i = 0; i < gpuTempList.Count; i++) {
          int speedRpm = gpuSpeedList[i];
          GPUTempFanMap[gpuTempList[i]] = new List<int> { speedRpm, speedRpm };
        }
      }
    }

    // Get fan speed for CPU and GPU and return the maximum
    // 使用平滑后的温度查表，保证高中低档响应速度生效；实时档下平滑温度==原始温度
    // 只有对应监控开启且温度已完成初始化时，才参与风扇转速计算
    static int GetFanSpeedForTemperature(int fanIndex) {
      if (CPUTempFanMap.Count == 0 || GPUTempFanMap.Count == 0) return 0;

      int resultSpeed = 0;

      if (monitorCPU && cpuTempReady) {
        int cpuFanSpeed = GetFanSpeedForSpecificTemperature(smoothedCPUTemp, CPUTempFanMap, fanIndex);
        resultSpeed = Math.Max(resultSpeed, cpuFanSpeed);
      }

      if (monitorGPU && gpuTempReady) {
        int gpuFanSpeed = GetFanSpeedForSpecificTemperature(smoothedGPUTemp, GPUTempFanMap, fanIndex);
        resultSpeed = Math.Max(resultSpeed, gpuFanSpeed);
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
                key.SetValue("DBVersion", DBVersion);
              }
              key.SetValue("AutoStart", autoStart);
              key.SetValue("AlreadyRead", alreadyRead);
              key.SetValue("CustomIcon", customIcon);
              key.SetValue("OmenKey", omenKey);
              if (hasNVIDIAGpu || hasAMDDiscreteGpu)
                key.SetValue("MonitorGPU", monitorGPU);
              key.SetValue("MonitorCPU", monitorCPU);
              key.SetValue("MonitorFan", monitorFan);
              key.SetValue("MonitorRefreshRate", monitorRefreshRate);
              key.SetValue("TempDisplayMode", tempDisplayMode);
              key.SetValue("FloatingBarLoc", floatingBarLoc);
              key.SetValue("FloatingBar", floatingBar);
              key.SetValue("DataLocalize", dataLocalize);
              key.SetValue("AppLanguage", appLanguage);
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
              }
              if (configName == "FanTable" || configName == "FanControl" || configName == "TempSensitivity" || configName == "CpuPower" || configName == "TgpPower" || configName == "PpabPower" || configName == "DState" || configName == "GpuClock" || configName == "TppPower" || configName == "IccMax" || configName == "AcLoadLine") {
                SavePresetToRegistry(currentPreset);
              }
            }
          }
        }
      } catch (Exception ex) {
        Logger.Error($"Error saving configuration: {ex.Message}");
      }
    }

    static void SavePresetToRegistry(string presetKey) {
      if (presetKey == "PresetAllPerformance" || presetKey == "PresetGpuPriority" || presetKey == "PresetLightUse") return;
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\OmenSuperHub\{presetKey}")) {
          if (key != null) {
            key.SetValue("FanTable", fanTable);
            key.SetValue("FanControl", fanControl);
            key.SetValue("TempSensitivity", tempSensitivity);
            key.SetValue("CpuPower", cpuPower);
            key.SetValue("TgpPower", tgpPower);
            key.SetValue("PpabPower", ppabPower);
            key.SetValue("DState", dState);
            key.SetValue("GpuClock", gpuClock);
            key.SetValue("TppPower", tppPower);
            key.SetValue("IccMax", iccMax);
            key.SetValue("AcLoadLine", acLoadline);
          }
        }
      } catch { }
    }

    static void LoadPresetFromRegistry(string presetKey) {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey($@"Software\OmenSuperHub\{presetKey}")) {
          if (key != null) {
            fanTable = (string)key.GetValue("FanTable", fanTable);
            fanControl = (string)key.GetValue("FanControl", fanControl);
            tempSensitivity = (string)key.GetValue("TempSensitivity", tempSensitivity);
            cpuPower = (string)key.GetValue("CpuPower", cpuPower);
            tgpPower = (string)key.GetValue("TgpPower", tgpPower);
            ppabPower = (string)key.GetValue("PpabPower", ppabPower);
            dState = (string)key.GetValue("DState", dState);
            gpuClock = (int)key.GetValue("GpuClock", gpuClock);
            tppPower = (string)key.GetValue("TppPower", tppPower);
            iccMax = (string)key.GetValue("IccMax", iccMax);
            acLoadline = (string)key.GetValue("AcLoadLine", acLoadline);
          }
        }
      } catch { }
    }

    static void RestoreConfig() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            currentPreset = (string)key.GetValue("CurrentPreset", platformSettings != null ? "PresetAllPerformance" : "PresetCustom1");
            presetCustom1Name = (string)key.GetValue("PresetCustom1Name", Strings.PresetCustom1);
            presetCustom2Name = (string)key.GetValue("PresetCustom2Name", Strings.PresetCustom2);
            presetCustom3Name = (string)key.GetValue("PresetCustom3Name", Strings.PresetCustom3);

            if (currentPreset != "PresetAllPerformance" && currentPreset != "PresetGpuPriority" && currentPreset != "PresetLightUse") {
              LoadPresetFromRegistry(currentPreset);
            }

            if (currentPreset == "PresetAllPerformance" || currentPreset == "PresetGpuPriority" || currentPreset == "PresetLightUse") {
              fanTable = (string)key.GetValue("FanTable", fanTable);
              fanControl = (string)key.GetValue("FanControl", "auto");
              tempSensitivity = (string)key.GetValue("TempSensitivity", "high");
              cpuPower = (string)key.GetValue("CpuPower", "null");
              tgpPower = (string)key.GetValue("TgpPower", "on");
              ppabPower = (string)key.GetValue("PpabPower", "on");
              dState = (string)key.GetValue("DState", "normal");
              gpuClock = (int)key.GetValue("GpuClock", 0);
              tppPower = (string)key.GetValue("TppPower", "null");
              iccMax = (string)key.GetValue("IccMax", "null");
              acLoadline = (string)key.GetValue("AcLoadLine", "null");
            }
              
            if (fanTable.Contains("cool")) {
              LoadFanConfig("cool.txt");
              UpdateCheckedState("fanTableGroup", Strings.FanCoolMode);
            } else if (fanTable.Contains("silent")) {
              LoadFanConfig("silent.txt");
              UpdateCheckedState("fanTableGroup", Strings.FanSilentMode);
            }

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
              if (fanTrackBar != null && rpmValue / 100 >= fanTrackBar.Minimum && rpmValue / 100 <= fanTrackBar.Maximum) {
                fanTrackBar.Value = rpmValue / 100;
              }
              UpdateCheckedState("fanControlGroup", Strings.SetFanSpeedSlider);
            }

            switch (tempSensitivity) {
              case "realtime":
                respondSpeed = 1;
                UpdateCheckedState("tempSensitivityGroup", Strings.FanRespRealtime);
                break;
              case "high":
                respondSpeed = 0.4f;
                UpdateCheckedState("tempSensitivityGroup", Strings.FanRespHigh);
                break;
              case "medium":
                respondSpeed = 0.1f;
                UpdateCheckedState("tempSensitivityGroup", Strings.FanRespMedium);
                break;
              case "low":
                respondSpeed = 0.04f;
                UpdateCheckedState("tempSensitivityGroup", Strings.FanRespLow);
                break;
            }

            // TPP 设置单独延迟 1s 应用，避免启动时与其他设置冲突
            string tppPowerSnapshot = tppPower;
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
              if (tppPowerSnapshot == "null") {
                UpdateCheckedState("tppPowerGroup", Strings.NotSet);
              } else if (tppPowerSnapshot == "max") {
                SetConcurrentTdp(254);
                if (tppTrackBar != null && tppTrackBar.Minimum <= 254 && 254 <= tppTrackBar.Maximum) {
                  tppTrackBar.Value = 254;
                }
              } else if (tppPowerSnapshot.Contains(" W")) {
                int value = int.Parse(tppPowerSnapshot.Replace(" W", "").Trim());
                if (value >= 20 && value <= 254) {
                  SetConcurrentTdp((byte)value);
                  if (tppTrackBar != null && tppTrackBar.Minimum <= value && value <= tppTrackBar.Maximum) {
                    tppTrackBar.Value = value;
                  }
                  UpdateCheckedState("tppPowerGroup", Strings.SetTppSlider);
                }
              }
            });

            //powerLimit4 = (string)key.GetValue("PL4Power", "null");
            //if (powerLimit4 == "null") {
            //  UpdateCheckedState("pl4PowerGroup", "不设置");
            //} else if (powerLimit4 == "max") {
            //  if (isTwoBytePL4) {
            //    SetPL4DoubleByte(500);
            //  } else {
            //    SetCpuPowerLimit4(254);
            //  }
            //  UpdateCheckedState("pl4PowerGroup", "最大");
            //} else if (powerLimit4.Contains(" W")) {
            //  int value = int.Parse(powerLimit4.Replace(" W", "").Trim());
            //  int doubleFactor = isTwoBytePL4 ? 2 : 1;
            //  if (value >= 40 && value <= 240 * doubleFactor) {
            //    if (isTwoBytePL4) {
            //      SetPL4DoubleByte((ushort)value);
            //    } else {
            //      SetCpuPowerLimit4((byte)value);
            //    }
            //    UpdateCheckedState("pl4PowerGroup", powerLimit4);
            //  }
            //}

            if (iccMax == "null") {
              UpdateCheckedState("iccMaxGroup", Strings.NotSet);
            } else if (iccMax.Contains(" A")) {
              if (int.TryParse(iccMax.Replace(" A", "").Trim(), out int ampVal) && ampVal >= 150 && ampVal <= 350) {
                SetIccMaxByWmi((decimal)ampVal);
                UpdateCheckedState("iccMaxGroup", iccMax);
              }
            }

            if (acLoadline == "null") {
              UpdateCheckedState("acLoadLineGroup", Strings.NotSet);
            } else if (int.TryParse(acLoadline, out int llVal) && llVal >= 1) {
              SetLoadLine(llVal);
              string llDisplay = (180 - 10 * llVal).ToString();
              UpdateCheckedState("acLoadLineGroup", llDisplay);
            }

            if (cpuPower == "null") {
              UpdateCheckedState("cpuPowerGroup", Strings.NotSet);
            } else if (cpuPower == "max") {
              SetCpuPowerLimit(254);
              if (cpuPowerTrackBar != null && 254 >= cpuPowerTrackBar.Minimum && 254 <= cpuPowerTrackBar.Maximum) {
                cpuPowerTrackBar.Value = 254;
              }
              UpdateCheckedState("cpuPowerGroup", Strings.SetCpuPowerSlider);
            } else if (cpuPower.Contains(" W")) {
              int value = int.Parse(cpuPower.Replace(" W", "").Trim());
              if (value >= 5 && value <= 254) {
                SetCpuPowerLimit((byte)value);
                if (cpuPowerTrackBar != null && value >= cpuPowerTrackBar.Minimum && value <= cpuPowerTrackBar.Maximum) {
                  cpuPowerTrackBar.Value = value;
                }
                UpdateCheckedState("cpuPowerGroup", Strings.SetCpuPowerSlider);
              }
            }

            SetGpuPowerState(tgpPower == "on", ppabPower == "on", dState == "normal" ? 1 : 2);
            UpdateCheckedState("tgpPowerGroup", tgpPower == "on" ? Strings.Enable : Strings.Disable);
            UpdateCheckedState("ppabPowerGroup", ppabPower == "on" ? Strings.Enable : Strings.Disable);
            UpdateCheckedState("dStateGroup", dState == "normal" ? Strings.Normal : Strings.LowPower);

            if (hasNVIDIAGpu) {
              if (SetGPUClockLimit(gpuClock)) {
                if (gpuClock > 0 && gpuClockTrackBar != null && gpuClockTrackBar.Minimum <= gpuClock / 10 && gpuClock / 10 <= gpuClockTrackBar.Maximum) {
                  gpuClockTrackBar.Value = gpuClock / 10;
                  UpdateCheckedState("gpuClockGroup", Strings.SetGpuClockSlider);
                } else if (gpuClock == 0) {
                  UpdateCheckedState("gpuClockGroup", Strings.Restore);
                }
              } else {
                UpdateCheckedState("gpuClockGroup", Strings.Restore);
              }

              if (DBMenu.Enabled) {
                DBVersion = (int)key.GetValue("DBVersion", 2);
                switch (DBVersion) {
                  case 1:
                    string gpuModel = GetNVIDIAModel();
                    if (gpuModel != null) {
                      var match = Regex.Match(gpuModel, @"^\d+");
                      if (match.Success && int.TryParse(match.Value, out int modelNum)) {
                        if (modelNum >= 5000) {
                          DBVersion = 2;
                          string deviceId50 = "\"ACPI\\NVDA0820\\NPCF\"";
                          string command50 = $"pnputil /enable-device {deviceId50}";
                          ExecuteCommand(command50);
                          UpdateCheckedState("DBGroup", Strings.DbNormal);
                          break;
                        }
                      }
                    }
                    DBVersion = 1;
                    SetGpuPowerState(true, true); // fallback for db state
                    SetCpuPowerLimit((byte)CPULimitDB);
                    countDB = countDBInit;
                    DBMenu.Enabled = false;
                    UpdateCheckedState("DBGroup", Strings.DbUnlocked);
                    break;
                  case 2:
                    string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
                    string command = $"pnputil /enable-device {deviceId}";
                    ExecuteCommand(command);
                    DBVersion = 2;
                    UpdateCheckedState("DBGroup", Strings.DbNormal);
                    break;
                }
              }
            }

            autoStart = (string)key.GetValue("AutoStart", "off");
            switch (autoStart) {
              case "on":
                AutoStartEnable();
                UpdateCheckedState("autoStartGroup", Strings.Enable);
                break;
              case "off":
                UpdateCheckedState("autoStartGroup", Strings.Disable);
                break;
            }

            alreadyRead = (int)key.GetValue("AlreadyRead", 0);

            customIcon = (string)key.GetValue("CustomIcon", "original");
            switch (customIcon) {
              case "original":
                trayIcon.Icon = Properties.Resources.smallfan;
                UpdateCheckedState("customIconGroup", Strings.IconOriginal);
                break;
              case "custom":
                SetCustomIcon();
                UpdateCheckedState("customIconGroup", Strings.IconCustom);
                break;
              case "dynamic":
                UpdateDynamicIcon();
                UpdateCheckedState("customIconGroup", Strings.IconDynamic);
                break;
            }

            omenKey = (string)key.GetValue("OmenKey", "default");
            switch (omenKey) {
              case "default":
                checkFloatingTimer.Enabled = false;
                OmenKeyOff();
                OmenKeyOn(omenKey);
                UpdateCheckedState("omenKeyGroup", Strings.OmenKeyDefault);
                break;
              case "custom":
                checkFloatingTimer.Enabled = true;
                OmenKeyOff();
                OmenKeyOn(omenKey);
                UpdateCheckedState("omenKeyGroup", Strings.OmenKeyToggle);
                break;
              case "none":
                checkFloatingTimer.Enabled = false;
                OmenKeyOff();
                UpdateCheckedState("omenKeyGroup", Strings.OmenKeyNone);
                break;
            }

            bool monitorCPUCache = Convert.ToBoolean(key.GetValue("MonitorCPU", true));
            monitorCPU = monitorCPUCache;
            UpdateCheckedState("monitorCPUGroup", monitorCPU ? Strings.MonitorCpuOn : Strings.MonitorCpuOff);

            if (hasNVIDIAGpu || hasAMDDiscreteGpu) {
              bool monitorGPUCache = Convert.ToBoolean(key.GetValue("MonitorGPU", true));
              monitorGPU = monitorGPUCache;
              UpdateCheckedState("monitorGPUGroup", monitorGPU ? Strings.MonitorGpuOn : Strings.MonitorGpuOff);
            } else {
              monitorGPU = false;
              UpdateCheckedState("monitorGPUGroup", monitorGPU ? Strings.MonitorGpuOn : Strings.MonitorGpuOff);
            }

            // 仅当至少一个监控开启时才启动 libre 进程；进程启动后再发送 CPU/GPU 状态
            if (monitorCPU || monitorGPU) {
              StartHardwareMonitor(); // 内部已调用 SetCpuMonitorState / SetGpuMonitorState
            }

            bool monitorFanCache = Convert.ToBoolean(key.GetValue("MonitorFan", false));
            if (monitorFanCache == true) {
              monitorFan = true;
              UpdateCheckedState("monitorFanGroup", Strings.MonitorFanOn);
            } else {
              monitorFan = false;
              UpdateCheckedState("monitorFanGroup", Strings.MonitorFanOff);
            }

            monitorRefreshRate = (string)key.GetValue("MonitorRefreshRate", "low");
            switch (monitorRefreshRate) {
              case "high":
                tooltipUpdateTimer.Interval = 250;
                SetMonitorInterval(250);
                UpdateCheckedState("monitorRefreshGroup", Strings.MonitorRefreshHigh);
                break;
              case "low":
              default:
                monitorRefreshRate = "low";
                tooltipUpdateTimer.Interval = 1000;
                SetMonitorInterval(1000);
                UpdateCheckedState("monitorRefreshGroup", Strings.MonitorRefreshLow);
                break;
            }

            tempDisplayMode = (string)key.GetValue("TempDisplayMode", "smoothed");
            if (tempDisplayMode == "raw") {
              UpdateCheckedState("tempDisplayGroup", Strings.TempRaw);
            } else {
              tempDisplayMode = "smoothed";
              UpdateCheckedState("tempDisplayGroup", Strings.TempSmoothed);
            }

            textSize = (int)key.GetValue("FloatingBarSize", 48);
            UpdateFloatingText();
            switch (textSize) {
              case 24:
                UpdateCheckedState("floatingBarSizeGroup", Strings.FontSize24);
                break;
              case 36:
                UpdateCheckedState("floatingBarSizeGroup", Strings.FontSize36);
                break;
              case 48:
                UpdateCheckedState("floatingBarSizeGroup", Strings.FontSize48);
                break;
            }

            floatingBarLoc = (string)key.GetValue("FloatingBarLoc", "left");
            UpdateFloatingText();
            if (floatingBarLoc == "left") {
              UpdateCheckedState("floatingBarLocGroup", Strings.FloatingLocLeft);
            } else {
              UpdateCheckedState("floatingBarLocGroup", Strings.FloatingLocRight);
            }

            floatingBar = (string)key.GetValue("FloatingBar", "off");
            if (floatingBar == "on") {
              ShowFloatingForm();
              UpdateCheckedState("floatingBarGroup", Strings.FloatingShow);
            } else {
              CloseFloatingForm();
              UpdateCheckedState("floatingBarGroup", Strings.FloatingHide);
            }

            dataLocalize = (string)key.GetValue("DataLocalize", "off");
            if (dataLocalize == "on") {
              UpdateCheckedState("dataLocalizeGroup", Strings.Enable);
            } else {
              UpdateCheckedState("dataLocalizeGroup", Strings.Disable);
            }

            // 恢复语言设置菜单勾选（语言本身已在 LoadLanguageSetting 中生效）
            appLanguage = (string)key.GetValue("AppLanguage", "zh-CN");
            RestoreLanguageChecked();

            // 旧版升级兼容：不存在预设键时，将当前设置迁移为自定义预设1
            if (key.GetValue("CurrentPreset") == null) {
              currentPreset = "PresetCustom1";
              SavePresetToRegistry(currentPreset);
              SaveConfig("CurrentPreset");
            }
          } else {
            // 如果注册表键不存在
            if (platformSettings != null) {
              applyPresetLogic("PresetAllPerformance");
            } else {
              applyPresetLogic("PresetCustom1");
            }
          }
        }
      } catch (Exception ex) {
        Logger.Error($"Error restoring configuration: {ex.Message}");
      }

      // 保证应用启动时如果不包含 DataLocalize 键（第一次运行或旧版升级），菜单项UI依然能被初始化选中
      if (dataLocalize == "on") {
        UpdateCheckedState("dataLocalizeGroup", Strings.Enable);
      } else {
        UpdateCheckedState("dataLocalizeGroup", Strings.Disable);
      }
    }

    static void applyPresetLogic(string targetPreset) {
      currentPreset = targetPreset;
      SaveConfig("CurrentPreset");
      // Reload preset and save values natively
      if (targetPreset == "PresetAllPerformance" || targetPreset == "PresetGpuPriority" || targetPreset == "PresetLightUse") {
        fanTable = "cool"; fanControl = "auto"; tempSensitivity = "high";
        tgpPower = "on"; ppabPower = "on"; dState = "normal";
        gpuClock = 0; iccMax = "null"; acLoadline = "null";
        int targetPL1Perf = (platformSettings?.NbPL1UpperBoundPerformance > 0) ? platformSettings.NbPL1UpperBoundPerformance : 160;
        int targetPL1Default = (platformSettings?.NbPL1UpperBoundDefault > 0) ? platformSettings.NbPL1UpperBoundDefault : 55;

        if (targetPreset == "PresetAllPerformance") {
          cpuPower = $"{targetPL1Perf} W";
          tppPower = $"{targetPL1Perf} W";
        } else if (targetPreset == "PresetGpuPriority") {
          cpuPower = $"{targetPL1Default} W";
          tppPower = $"{targetPL1Perf} W";
        } else if (targetPreset == "PresetLightUse") {
          fanTable = "silent";
          cpuPower = $"{(int)(targetPL1Default * 0.6)} W";
          if (currentPreset == "PresetLightUse" && platformSettings?.NbPL1UpperBoundDefault == null) cpuPower = "30 W";
          tppPower = "null";
          tgpPower = "off";
          ppabPower = "off";
        }
      } else {
        LoadPresetFromRegistry(targetPreset);
      }
      SaveConfig();
      RestoreConfig();
    }
  }
}
