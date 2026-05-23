using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.DataStructure;
using HP.Omen.Core.Model.Device.Enums;
using HP.Omen.Core.Model.Device.Models;
using Microsoft.Win32;
using static OmenSuperHub.GpuAppManager;
using static OmenSuperHub.OmenHardware;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub {
  static partial class Program {
    static ToolStripMenuItem languageMenu;

    static void InitTrayIcon() {
      trayIcon = new NotifyIcon() {
        Icon = Properties.Resources.smallfan,
        ContextMenuStrip = new ContextMenuStrip(),
        Visible = true
      };
      trayIcon.MouseClick += TrayIcon_MouseClick;

      try {
        // 读取图标配置
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            customIcon = (string)key.GetValue("CustomIcon", "original");
            // 检查是否错误配置为自定义图标
            if (customIcon == "custom" && !CheckCustomIcon()) {
              customIcon = "original";
              SaveConfig("CustomIcon");
              trayIcon.Icon = Properties.Resources.smallfan;
              UpdateCheckedState("CustomIcon", Strings.IconOriginal);
            }
          }
        }
      } catch (Exception ex) {
        Logger.Error($"Error restoring configuration: {ex.Message}");
      }

      switch (customIcon) {
        case "original": trayIcon.Icon = Properties.Resources.smallfan; break;
        case "custom": SetCustomIcon(); break;
        case "dynamic": UpdateDynamicIcon(); break;
      }

      BuildTrayMenu(trayIcon.ContextMenuStrip);

      // Initialize tooltip update timer
      tooltipUpdateTimer = new System.Timers.Timer(1000); // Set interval to 1 second (low, default)
      tooltipUpdateTimer.Elapsed += (s, e) => UpdateTooltip();
      tooltipUpdateTimer.AutoReset = true; // Ensure the timer keeps running
      tooltipUpdateTimer.Start();
    }

    static void BuildTrayMenu(ContextMenuStrip menu) {
      menu.Items.Clear();

      ToolStripMenuItem sysInfoMenu = new ToolStripMenuItem(Strings.SysInfo);
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysModelName}: {DeviceModel.OmenPlatform.DisplayName}") { Enabled = false });
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysBoardProduct}: {systemSSID}") { Enabled = false });
      // BIOS 版本
      string biosVersion = GetBiosVersion();
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysBiosVersion}: {biosVersion}") { Enabled = false });

      // CPU 完整型号
      string cpuModel = GetCpuModel();
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysCpu}: {cpuModel}") { Enabled = false });
      if (maxCPUTemp.HasValue) {
        sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysCpuTjMax}: {maxCPUTemp.Value}°C") { Enabled = false });
      }
      ToolStripMenuItem gpuPowerLimitsMenu = null;
      if (hasNVIDIAGpu) {
        // 获取所有显卡
        var allGpuNames = GetAllGpuNamesList();
        if (allGpuNames.Count == 1) {
          sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysGpu}: {allGpuNames[0]}") { Enabled = false });
        } else if (allGpuNames.Count > 1) {
          sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.SysGpuList) { Enabled = false });
          foreach (var gpuName in allGpuNames) {
            sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"    {gpuName}") { Enabled = false });
          }
        } else {
          sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.SysGpuUnknown) { Enabled = false });
        }
        if (maxGPUTemp.HasValue) {
          sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysNvidiaTjMax}: {maxGPUTemp.Value}°C") { Enabled = false });
        }
        gpuPowerLimitsMenu = new ToolStripMenuItem($"{Strings.SysNvidiaPower}: --W / --W") { Enabled = false };
        sysInfoMenu.DropDownItems.Add(gpuPowerLimitsMenu);
      }
      irSensorMenu = new ToolStripMenuItem($"{Strings.SysIRSensor}: --°C") { Enabled = false };
      ambientSensorMenu = new ToolStripMenuItem($"{Strings.SysAmbient}: --°C") { Enabled = false };
      pchSensorMenu = new ToolStripMenuItem($"{Strings.SysPCH}: --°C") { Enabled = false };
      vrSensorMenu = new ToolStripMenuItem($"{Strings.SysVR}: --°C") { Enabled = false };
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysKbType}: {GetKeyboardTypeName(kbType)}") { Enabled = false });
      sysInfoMenu.DropDownItems.Add(irSensorMenu);
      sysInfoMenu.DropDownItems.Add(ambientSensorMenu);
      sysInfoMenu.DropDownItems.Add(pchSensorMenu);
      sysInfoMenu.DropDownItems.Add(vrSensorMenu);
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.SysAdapterPower}: {GetAdapterPower()}W") { Enabled = false });

      // 订阅 DropDownOpening 和 DropDownClosed 事件来控制是否更新信息
      sysInfoMenu.DropDownOpening += (s, e) => { isSysInfoMenuOpen = true; };
      sysInfoMenu.DropDownClosed += (s, e) => { isSysInfoMenuOpen = false; };

      menu.Items.Add(sysInfoMenu);

      menu.Items.Add(new ToolStripSeparator());
      ToolStripMenuItem fanConfigMenu = new ToolStripMenuItem(Strings.FanConfig);
      fanConfigMenu.DropDownItems.Add(CreateMenuItem(Strings.FanSilentMode, "fanTableGroup", (s, e) => {
        fanTable = "silent";
        LoadFanConfig("silent.txt");
        SaveConfig("FanTable");
      }, false, Strings.FanSilentTooltip));
      fanConfigMenu.DropDownItems.Add(CreateMenuItem(Strings.FanCoolMode, "fanTableGroup", (s, e) => {
        fanTable = "cool";
        LoadFanConfig("cool.txt");
        SaveConfig("FanTable");
      }, true, Strings.FanCoolTooltip));
      fanConfigMenu.DropDownItems.Add(new ToolStripSeparator());
      ToolStripMenuItem respondSpeedMenu = new ToolStripMenuItem(Strings.FanResponseSpeed);
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem(Strings.FanRespRealtime, "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "realtime";
        respondSpeed = 1;
        SaveConfig("TempSensitivity");
      }, false));
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem(Strings.FanRespHigh, "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "high";
        respondSpeed = 0.4f;
        SaveConfig("TempSensitivity");
      }, true));
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem(Strings.FanRespMedium, "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "medium";
        respondSpeed = 0.1f;
        SaveConfig("TempSensitivity");
      }, false));
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem(Strings.FanRespLow, "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "low";
        respondSpeed = 0.04f;
        SaveConfig("TempSensitivity");
      }, false));
      fanConfigMenu.DropDownItems.Add(respondSpeedMenu);
      menu.Items.Add(fanConfigMenu);

      ToolStripMenuItem fanControlMenu = new ToolStripMenuItem(Strings.FanControl);
      if (isFanCleanSupported || isFanLegacyCleanSupported) {
        fanControlMenu.DropDownItems.Add(CreateMenuItem(Strings.CleanCreekMenuItem, null, (s, e) => {
          if (MessageBox.Show(Strings.CleanCreekConfirmMessage, Strings.CleanCreekTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK) {
            fanControlMenu.Enabled = false;
            if (isFanCleanSupported) {
              SetMaxFanSpeedOff();
              fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
              // 准备开始清洁
              Action start = () => {
                SetFanLevel(platformSettings.CleanCreekCpuFanSpeed, platformSettings.CleanCreekGpuFanSpeed, Is3FanNb, true);
              };
              Action stop = () => {
                fanControlMenu.Enabled = true;
                RestoreFanControl();  // 恢复原始转速或自动控制
              };
              // 显示进度窗体，持续时间从配置读取（单位毫秒）
              StartCleanCreekWithProgress(platformSettings.CleanCreekDuration, Strings.CleanCreekTitle, start, stop);
            } else if (isFanLegacyCleanSupported) {
              SetMaxFanSpeedOff();
              fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
              Action start = () => SetLegacyCleanCreek(true);
              Action stop = () => {
                fanControlMenu.Enabled = true;
                SetLegacyCleanCreek(false);
                RestoreFanControl();
              };
              StartCleanCreekWithProgress(platformSettings.CleanCreekDuration, Strings.CleanCreekTitle, start, stop);
            }
          }
        }, false));
        fanControlMenu.DropDownItems.Add(new ToolStripSeparator());
      }
      fanControlMenu.DropDownItems.Add(CreateMenuItem(Strings.FanAuto, "fanControlGroup", (s, e) => {
        fanControl = "auto";
        SetMaxFanSpeedOff();
        fanControlTimer.Change(0, 1000);
        SaveConfig("FanControl");
      }, true));
      fanControlMenu.DropDownItems.Add(CreateMenuItem(Strings.FanMax, "fanControlGroup", (s, e) => {
        fanControl = "max";
        SetMaxFanSpeedOn();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveConfig("FanControl");
      }, false));
      for (int speed = 1600; speed <= 6400; speed += 400) {
        int currentSpeed = speed;  // 创建一个局部变量，保存当前的 power 值
        fanControlMenu.DropDownItems.Add(CreateMenuItem(currentSpeed + " RPM", "fanControlGroup", (s, e) => {
          fanControl = currentSpeed + " RPM";
          SetMaxFanSpeedOff();
          fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
          SetFanLevel(currentSpeed / 100, currentSpeed / 100, Is3FanNb);
          SaveConfig("FanControl");
        }, false));
      }
      menu.Items.Add(fanControlMenu);

      ToolStripMenuItem performanceControlMenu = new ToolStripMenuItem(Strings.PerfControl);
      // 图形模式
      if (NvGraphicsMode == GraphicsMode.Optimus || NvGraphicsMode == GraphicsMode.Hybrid) {
        var hotSwitchItem = CreateMenuItem(Strings.HotSwitch, null, (s, e) => {
          if (LaunchDDS() != 0)
            MessageBox.Show(Strings.DdsInitFail, Strings.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }, false);
        hotSwitchItem.CheckOnClick = false;
        performanceControlMenu.DropDownItems.Add(hotSwitchItem);
      }
      ToolStripMenuItem graphicsModeControlMenu = null;
      if (!hasNVIDIAGpu && hasAMDDiscreteGpu) {
        graphicsModeControlMenu = new ToolStripMenuItem(Strings.GraphicsMode);
        var initAmdMode = AmdGpuSwitcher.GetMode();
        bool amdIsDiscrete = initAmdMode == AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_ENABLED;
        graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem(Strings.GfxDiscreteMode, "graphicsModeGroup", (s, e) => {
          AmdGpuSwitcher.SetMode(AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_ENABLED);
        }, amdIsDiscrete));
        graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem(Strings.GfxHybridMode, "graphicsModeGroup", (s, e) => {
          AmdGpuSwitcher.SetMode(AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_DISABLED);
        }, !amdIsDiscrete));
      } else {
        byte supportedGfxModes = GetSupportedGfxModes();

        if (supportedGfxModes != 0) {
          graphicsModeControlMenu = new ToolStripMenuItem(Strings.GraphicsMode);
          graphicsModeControlMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.GfxOnlyInternal) { Enabled = false });
          graphicsModeControlMenu.DropDownItems.Add(new ToolStripSeparator());
          bool supportsUMA = (supportedGfxModes & 0x01) != 0;
          bool supportsHybrid = (supportedGfxModes & 0x02) != 0;
          bool supportsDiscrete = (supportedGfxModes & 0x04) != 0;
          bool supportsDDS = (supportedGfxModes & 0x08) != 0;
          if (supportsDDS || NvGraphicsMode == GraphicsMode.Optimus) {
            graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("NVIDIA Advanced Optimus", "graphicsModeGroup", (s, e) => {
              if (SetGfxMode(GraphicsMode.Optimus))
                MessageBox.Show(Strings.GfxSwitchedTo("NVIDIA Advanced Optimus"), Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
              else {
                SetGfxMode(NvGraphicsMode);
                MessageBox.Show(Strings.GfxUnsupported, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
              }
            }, NvGraphicsMode == GraphicsMode.Optimus, "混合"));
          }
          if (supportsDiscrete || NvGraphicsMode == GraphicsMode.Discrete) {
            graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("Discrete", "graphicsModeGroup", (s, e) => {
              if (SetGfxMode(GraphicsMode.Discrete))
                MessageBox.Show(Strings.GfxSwitchedTo("Discrete"), Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
              else {
                SetGfxMode(NvGraphicsMode);
                MessageBox.Show(Strings.GfxUnsupported, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
              }
            }, NvGraphicsMode == GraphicsMode.Discrete, "仅独显"));
          }
          if (supportsUMA || NvGraphicsMode == GraphicsMode.UMA) {
            graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("UMA", "graphicsModeGroup", (s, e) => {
              if (MessageBox.Show(Strings.GfxUMAConfirm, Strings.GfxUMATitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                if (SetGfxMode(GraphicsMode.UMA))
                  MessageBox.Show(Strings.GfxSwitchedTo("UMA"), Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else {
                  SetGfxMode(NvGraphicsMode);
                  MessageBox.Show(Strings.GfxUnsupported, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
              }
            }, NvGraphicsMode == GraphicsMode.UMA, "关闭独立 GPU 并仅使用板载 GPU 以最大限度地延长电池续航时间。"));
          }
          if (supportsHybrid || NvGraphicsMode == GraphicsMode.Hybrid) {
            graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("Hybrid", "graphicsModeGroup", (s, e) => {
              if (SetGfxMode(GraphicsMode.Hybrid))
                MessageBox.Show(Strings.GfxSwitchedTo("Hybrid"), Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
              else {
                SetGfxMode(NvGraphicsMode);
                MessageBox.Show(Strings.GfxUnsupported, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
              }
            }, NvGraphicsMode == GraphicsMode.Hybrid, "混合"));
          }
        }
      }
      if (graphicsModeControlMenu != null) {
        performanceControlMenu.DropDownItems.Add(graphicsModeControlMenu);
        graphicsModeControlMenu.DropDownOpening += (s, e) => {
          if (!hasNVIDIAGpu && hasAMDDiscreteGpu) {
            var amdMode = AmdGpuSwitcher.GetMode();
            bool isDisc = amdMode == AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_ENABLED;
            UpdateCheckedState("graphicsModeGroup", isDisc ? Strings.GfxDiscreteMode : Strings.GfxHybridMode);
          } else {
            var nvMode = GetGfxMode();
            string chk;
            switch (nvMode) {
              case GraphicsMode.Discrete: chk = "Discrete"; break;
              case GraphicsMode.Optimus: chk = "NVIDIA Advanced Optimus"; break;
              case GraphicsMode.UMA: chk = "UMA"; break;
              default: chk = "Hybrid"; break;
            }
            UpdateCheckedState("graphicsModeGroup", chk);
          }
        };
      }
      if (hasNVIDIAGpu) {
        ToolStripMenuItem gpuAppsMenu = new ToolStripMenuItem(Strings.GpuAppsMenu);
        gpuAppsMenu.DropDownOpening += (s, e) => {
          gpuAppsMenu.DropDownItems.Clear();
          var apps = GetGpuApps();
          if (apps.Count == 0) {
            gpuAppsMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.GpuAppsNone) { Enabled = false });
          } else {
            foreach (var app in apps) {
              var appItem = new ToolStripMenuItem($"{app.ProcessName} (PID: {app.ProcessId})");
              appItem.Click += (sender, args) => {
                if (MessageBox.Show(Strings.GpuCloseConfirm(app.ProcessName), Strings.GpuCloseTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                  try {
                    Process.GetProcessById(app.ProcessId).Kill();
                  } catch (Exception ex) {
                    MessageBox.Show(Strings.GpuCloseError(ex.Message), Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                  }
                }
              };
              gpuAppsMenu.DropDownItems.Add(appItem);
            }
          }
        };
        performanceControlMenu.DropDownItems.Add(gpuAppsMenu);

        ToolStripMenuItem restartGpuMenu = new ToolStripMenuItem(Strings.GpuRestartMenu);
        restartGpuMenu.ToolTipText = Strings.GpuRestartTooltip;
        restartGpuMenu.Click += (s, e) => {
          if (MessageBox.Show(Strings.GpuRestartConfirm, Strings.GpuRestartTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
            RestartGpu();
          }
        };
        performanceControlMenu.DropDownItems.Add(restartGpuMenu);
      }
      performanceControlMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      //ToolStripMenuItem pl4Menu = new ToolStripMenuItem("PL4");
      //pl4Menu.DropDownItems.Add(CreateMenuItem("不设置", "pl4PowerGroup", (s, e) => {
      //  powerLimit4 = "null";
      //  SaveConfig("PL4Power");
      //}, true));
      //pl4Menu.DropDownItems.Add(CreateMenuItem("最大", "pl4PowerGroup", (s, e) => {
      //  powerLimit4 = "max";
      //  if (isTwoBytePL4) {
      //    SetPL4DoubleByte(500);
      //  } else {
      //    SetCpuPowerLimit4(254);
      //  }
      //  SaveConfig("PL4Power");
      //}, false));
      //int doubleFactor = isTwoBytePL4 ? 2 : 1;
      //for (int power = 40; power <= 240 * doubleFactor; power += 20 * doubleFactor) {
      //  int currentPower = power;
      //  pl4Menu.DropDownItems.Add(CreateMenuItem(currentPower + " W", "pl4PowerGroup", (s, e) => {
      //    powerLimit4 = currentPower + " W";
      //    if (isTwoBytePL4) {
      //      SetPL4DoubleByte((ushort)currentPower);
      //    } else {
      //      SetCpuPowerLimit4((byte)currentPower);
      //    }
      //    SaveConfig("PL4Power");
      //  }, false));
      //}
      //performanceControlMenu.DropDownItems.Add(pl4Menu);
      if (platformSettings != null && platformSettings.UnleashedModeMaxIccMax > 0) {
        ToolStripMenuItem iccMaxMenu = new ToolStripMenuItem(Strings.IccMaxMenu);
        iccMaxMenu.DropDownItems.Add(CreateMenuItem(Strings.NotSet, "iccMaxGroup", (s, e) => {
          iccMax = "null";
          SaveConfig("IccMax");
        }, true));
        for (int ampere = 150; ampere <= 350; ampere += 20) {
          int currentAmpere = ampere;
          iccMaxMenu.DropDownItems.Add(CreateMenuItem(currentAmpere + " A", "iccMaxGroup", (s, e) => {
            iccMax = currentAmpere + " A";
            SetIccMaxByWmi((decimal)currentAmpere);
            SaveConfig("IccMax");
          }, false));
        }
        performanceControlMenu.DropDownItems.Add(iccMaxMenu);
      }
      if (IsLoadLineSupported()) {
        ToolStripMenuItem acLoadLineMenu = new ToolStripMenuItem(Strings.AcLoadLineMenu);
        acLoadLineMenu.DropDownItems.Add(CreateMenuItem(Strings.NotSet, "acLoadLineGroup", (s, e) => {
          acLoadline = "null";
          SaveConfig("AcLoadLine");
        }, true));
        int maxSupportedLevel = GetLoadLineSupportLevels();
        for (int level = 1; level <= maxSupportedLevel + 5; level++) {
          int currentLevel = level;
          string displayText = (180 - 10 * currentLevel).ToString();
          acLoadLineMenu.DropDownItems.Add(CreateMenuItem(displayText, "acLoadLineGroup", (s, e) => {
            acLoadline = currentLevel.ToString();
            if (currentLevel > maxSupportedLevel) {
              trayIcon.BalloonTipTitle = Strings.AcLoadLineBalloonTitle;
              trayIcon.BalloonTipText = Strings.AcLoadLineBalloonText(maxSupportedLevel, currentLevel);
              trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
              trayIcon.ShowBalloonTip(3000);
            }
            SetLoadLine(currentLevel);
            SaveConfig("AcLoadLine");
          }, false));
        }
        performanceControlMenu.DropDownItems.Add(acLoadLineMenu);
      }
      ToolStripMenuItem cpuPowerMenu = new ToolStripMenuItem(Strings.CpuPowerMenu);
      cpuPowerMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.PerfCpuPowerTip) { Enabled = false });
      cpuPowerMenu.DropDownItems.Add(new ToolStripSeparator());
      cpuPowerMenu.DropDownItems.Add(CreateMenuItem(Strings.NotSet, "cpuPowerGroup", (s, e) => {
        cpuPower = "null";
        SaveConfig("CpuPower");
      }, true));
      cpuPowerMenu.DropDownItems.Add(CreateMenuItem(Strings.Maximum, "cpuPowerGroup", (s, e) => {
        cpuPower = "max";
        SetCpuPowerLimit(254);
        SaveConfig("CpuPower");
      }, false));
      for (int power = 10; power <= 200; power += 10) {
        int currentPower = power;  // 创建一个局部变量，保存当前的 power 值
        cpuPowerMenu.DropDownItems.Add(CreateMenuItem(power + " W", "cpuPowerGroup", (s, e) => {
          cpuPower = currentPower + " W";
          SetCpuPowerLimit((byte)currentPower);
          SaveConfig("CpuPower");
        }, false));
      }
      performanceControlMenu.DropDownItems.Add(cpuPowerMenu);

      ToolStripMenuItem gpuPowerMenu = new ToolStripMenuItem(Strings.GpuPowerControlMenu);

      ToolStripMenuItem tgpMenu = new ToolStripMenuItem("Tgp");
      tgpMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.PerfTgpTip) { Enabled = false });
      tgpMenu.DropDownItems.Add(new ToolStripSeparator());
      tgpMenu.DropDownItems.Add(CreateMenuItem(Strings.Enable, "tgpPowerGroup", (s, e) => {
        tgpPower = "on";
        SetGpuPowerState(true, ppabPower == "on", dState == "normal" ? 1 : 2);
        SaveConfig("TgpPower");
      }, true));
      tgpMenu.DropDownItems.Add(CreateMenuItem(Strings.Disable, "tgpPowerGroup", (s, e) => {
        tgpPower = "off";
        SetGpuPowerState(false, ppabPower == "on", dState == "normal" ? 1 : 2);
        SaveConfig("TgpPower");
      }, false));
      gpuPowerMenu.DropDownItems.Add(tgpMenu);

      ToolStripMenuItem ppabMenu = new ToolStripMenuItem("Ppab");
      ppabMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.PerfPpabTip) { Enabled = false });
      ppabMenu.DropDownItems.Add(new ToolStripSeparator());
      ppabMenu.DropDownItems.Add(CreateMenuItem(Strings.Enable, "ppabPowerGroup", (s, e) => {
        ppabPower = "on";
        SetGpuPowerState(tgpPower == "on", true, dState == "normal" ? 1 : 2);
        SaveConfig("PpabPower");
      }, true));
      ppabMenu.DropDownItems.Add(CreateMenuItem(Strings.Disable, "ppabPowerGroup", (s, e) => {
        ppabPower = "off";
        SetGpuPowerState(tgpPower == "on", false, dState == "normal" ? 1 : 2);
        SaveConfig("PpabPower");
      }, false));
      gpuPowerMenu.DropDownItems.Add(ppabMenu);

      if (platformSettings != null && platformSettings.TppSupport) {
        ToolStripMenuItem tppMenu = new ToolStripMenuItem(Strings.PpabPowerMenu);
        tppMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.PerfTppTip) { Enabled = false });
        tppMenu.DropDownItems.Add(new ToolStripSeparator());
        tppMenu.DropDownItems.Add(CreateMenuItem(Strings.NotSet, "tppPowerGroup", (s, e) => {
          tppPower = "null";
          SaveConfig("TppPower");
        }, true));
        tppMenu.DropDownItems.Add(CreateMenuItem(Strings.Maximum, "tppPowerGroup", (s, e) => {
          tppPower = "max";
          SetConcurrentTdp(254);
          SaveConfig("TppPower");
        }, false));
        for (int power = 20; power <= 240; power += 20) {
          int currentPower = power;
          tppMenu.DropDownItems.Add(CreateMenuItem(currentPower + " W", "tppPowerGroup", (s, e) => {
            tppPower = currentPower + " W";
            SetConcurrentTdp((byte)currentPower);
            SaveConfig("TppPower");
          }, false));
        }
        gpuPowerMenu.DropDownItems.Add(tppMenu);
      }

      ToolStripMenuItem dStateMenu = new ToolStripMenuItem(Strings.DStateSubMenu);
      dStateMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.PerfDStateTip) { Enabled = false });
      dStateMenu.DropDownItems.Add(new ToolStripSeparator());
      dStateMenu.DropDownItems.Add(CreateMenuItem(Strings.Normal, "dStateGroup", (s, e) => {
        dState = "normal";
        SetGpuPowerState(tgpPower == "on", ppabPower == "on", 1);
        SaveConfig("DState");
      }, true));
      dStateMenu.DropDownItems.Add(CreateMenuItem(Strings.LowPower, "dStateGroup", (s, e) => {
        dState = "low";
        SetGpuPowerState(tgpPower == "on", ppabPower == "on", 2);
        SaveConfig("DState");
      }, false));
      gpuPowerMenu.DropDownItems.Add(dStateMenu);

      performanceControlMenu.DropDownItems.Add(gpuPowerMenu);
      if (hasNVIDIAGpu) {
        ToolStripMenuItem gpuClockMenu = new ToolStripMenuItem(Strings.GpuClockMenu);
        gpuClockMenu.DropDownItems.Add(CreateMenuItem(Strings.Restore, "gpuClockGroup", (s, e) => {
          gpuClock = 0;
          SetGPUClockLimit(gpuClock);
          SaveConfig("GpuClock");
        }, true));
        for (int clock = 600; clock <= 1400; clock += 400) {
          int currentclock = clock;
          gpuClockMenu.DropDownItems.Add(CreateMenuItem(currentclock + " MHz", "gpuClockGroup", (s, e) => {
            gpuClock = currentclock;
            SetGPUClockLimit(gpuClock);
            SaveConfig("GpuClock");
          }, false));
        }
        for (int clock = 1550; clock <= 2000; clock += 150) {
          int currentclock = clock;
          gpuClockMenu.DropDownItems.Add(CreateMenuItem(currentclock + " MHz", "gpuClockGroup", (s, e) => {
            gpuClock = currentclock;
            SetGPUClockLimit(gpuClock);
            SaveConfig("GpuClock");
          }, false));
        }
        for (int clock = 2100; clock <= 2500; clock += 100) {
          int currentclock = clock;
          gpuClockMenu.DropDownItems.Add(CreateMenuItem(currentclock + " MHz", "gpuClockGroup", (s, e) => {
            gpuClock = currentclock;
            SetGPUClockLimit(gpuClock);
            SaveConfig("GpuClock");
          }, false));
        }
        performanceControlMenu.DropDownItems.Add(gpuClockMenu);
        ToolStripMenuItem DBMenu = new ToolStripMenuItem(Strings.DbVersionMenu);
        if (platformSettings != null && platformSettings.TppSupport) {
          DBMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.PerfDbTip) { Enabled = false });
          DBMenu.DropDownItems.Add(new ToolStripSeparator());
        }
        DBMenu.DropDownItems.Add(CreateMenuItem(Strings.DbUnlocked, "DBGroup", (s, e) => {
          string gpuModel = GetNVIDIAModel();
          if (gpuModel != null) {
            var match = Regex.Match(gpuModel, @"^\d+");
            if (match.Success && int.TryParse(match.Value, out int modelNum)) {
              if (modelNum >= 5000) {
                MessageBox.Show(Strings.DbNo50Series, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DBVersion = 2;
                countDB = 0;
                SaveConfig("DBVersion");
                UpdateCheckedState("DBGroup", Strings.DbNormal);
                return;
              }
            }
          }
          if (MessageBox.Show(Strings.PerfDbUnlockWarning, Strings.DbUnlockTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
            SetGpuPowerState(true, true);
            SetCpuPowerLimit((byte)CPULimitDB);
            DBVersion = 1;
            ChangeDBVersion(DBVersion);
            countDB = countDBInit;
            SaveConfig("DBVersion");
          }
        }, false, Strings.PerfDbUnlockTooltip));
        DBMenu.DropDownItems.Add(CreateMenuItem(Strings.DbNormal, "DBGroup", (s, e) => {
          DBVersion = 2;
          countDB = 0;
          //ChangeDBVersion(DBVersion);

          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /enable-device {deviceId}";
          ExecuteCommand(command);
          SaveConfig("DBVersion");
        }, true, Strings.PerfDbNormalTooltip));
        performanceControlMenu.DropDownItems.Add(DBMenu);
      }
      menu.Items.Add(performanceControlMenu);

      // ---- 灯光控制 ----
      if (kbType >= 0) {
        ToolStripMenuItem lightingControlMenu = new ToolStripMenuItem(Strings.LightingControl);
        lightingControlMenu.DropDownOpening += (s, e) => {
          lightingControlMenu.DropDownItems.Clear();

          if (supportDojo)
            kbControlInterface = LightingControlInterface.Dojo;

          // ---------- 四分区/单分区键盘 ----------
          if (kbType > 0) {
            ToolStripMenuItem kbMenu = new ToolStripMenuItem(Strings.LightingFourZoneKeyboard);
            kbMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.LightingBrightnessRangeTip) { Enabled = false });
            kbMenu.DropDownItems.Add(new ToolStripSeparator());
            AddLightingUI(kbMenu, LightingDevice.Keyboard, true);
            lightingControlMenu.DropDownItems.Add(kbMenu);
          } else {
            lightingControlMenu.DropDownItems.Add(CreateMenuItem(Strings.LightingOn, "lightSwitch", (s1, e1) => {
              SetZoneBrightness(LightingDevice.Keyboard, 228);
            }, GetZoneBrightness() == 228));
            lightingControlMenu.DropDownItems.Add(CreateMenuItem(Strings.LightingOff, "lightSwitch", (s1, e1) => {
              SetZoneBrightness(LightingDevice.Keyboard, 100);
            }, GetZoneBrightness() == 100));
          }

          // ---------- 灯条 ----------
          if (supportLightbar) {
            ToolStripMenuItem lbMenu = new ToolStripMenuItem(Strings.LightingLightBar);
            lbMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.LightingBrightnessRangeTip) { Enabled = false });
            lbMenu.DropDownItems.Add(new ToolStripSeparator());
            AddLightingUI(lbMenu, LightingDevice.LightBar, false);
            lightingControlMenu.DropDownItems.Add(lbMenu);
          }

          // ---------- 单键 RGB ----------
          if (kbType == NbKeyboardLightingType.RgbPerKey) {
            ToolStripMenuItem perKeyMenu = new ToolStripMenuItem(Strings.LightingPerKeyTitle);
            AddPerKeyLightingUI(perKeyMenu);
            lightingControlMenu.DropDownItems.Add(perKeyMenu);
          }

          // ---------- 状态显示 ----------
          lightingControlMenu.DropDownItems.Add(new ToolStripSeparator());
          byte brightness = GetZoneBrightness();
          var colors = GetZoneStaticColor();
          lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingBrightnessStatus}: {brightness}%") { Enabled = false });
          if (supportAni) {
            int currentAnimation = GetCurrentAnimationEffect();
            lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingAnimationStatus}: {(currentAnimation != -1 ? "ID " + currentAnimation : "无")}") { Enabled = false });
          }
          if (kbType == NbKeyboardLightingType.FourZoneWithNumpad || kbType == NbKeyboardLightingType.FourZoneWithoutNumpad) {
            if (colors != null && colors.Length == 4) {
              lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingZoneTag}1: RGB({colors[0].R},{colors[0].G},{colors[0].B})") { Enabled = false });
              lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingZoneTag}2: RGB({colors[1].R},{colors[1].G},{colors[1].B})") { Enabled = false });
              lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingZoneTag}3: RGB({colors[2].R},{colors[2].G},{colors[2].B})") { Enabled = false });
              lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingZoneTag}4: RGB({colors[3].R},{colors[3].G},{colors[3].B})") { Enabled = false });
            }
          } else if (kbType == NbKeyboardLightingType.OneZoneWithNumpad || kbType == NbKeyboardLightingType.OneZoneWithoutNumpad) {
            if (colors != null && colors.Length == 4) {
              lightingControlMenu.DropDownItems.Add(new ToolStripMenuItem($"{Strings.LightingSingleZoneColor}: RGB({colors[0].R},{colors[0].G},{colors[0].B})") { Enabled = false });
            }
          }
        };

        menu.Items.Add(lightingControlMenu);
      }

      menu.Items.Add(new ToolStripSeparator()); // Separator between groups
      ToolStripMenuItem hardwareMonitorMenu = new ToolStripMenuItem(Strings.HwMonitor);
      ToolStripMenuItem monitorCPUMenu = new ToolStripMenuItem(Strings.MonitorCpuLabel);
      monitorCPUMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorCpuOn, "monitorCPUGroup", (s, e) => {
        bool wasAllOff = !monitorCPU && !monitorGPU;
        monitorCPU = true;
        cpuTempReady = false; // 等待获取到温度后再参与风扇控制
        rawPowerCPU = 0f;     // 清除可能残留的脏功率值
        CPUPower = 0f;
        if (wasAllOff) {
          // 从全关状态重启监控进程
          tempReady = false;
          gpuTempReady = false;
          StartHardwareMonitor();
        } else {
          SetCpuMonitorState(true);
        }
        SaveConfig("MonitorCPU");
      }, true));
      monitorCPUMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorCpuOff, "monitorCPUGroup", (s, e) => {
        // 自动转速模式下禁止彻底关闭监控
        if (!monitorGPU && fanControl == "auto") {
          MessageBox.Show(Strings.MonitorAutoFanWarning, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          UpdateCheckedState("monitorCPUGroup", monitorCPU ? Strings.MonitorCpuOn : Strings.MonitorCpuOff);
          skipCheckedUpdate = true;
          return;
        }
        monitorCPU = false;
        cpuTempReady = false;
        rawPowerCPU = 0f;  // 关闭时清零，避免重新开启时读到旧值
        CPUPower = 0f;
        SetCpuMonitorState(false);
        // 若CPU和GPU均已关闭，停止监控进程
        if (!monitorCPU && !monitorGPU) {
          StopHardwareMonitor();
        }
        SaveConfig("MonitorCPU");
        // 手动更新勾选状态（因为提前 return 会跳过 CreateMenuItem 的自动勾选）
      }, false));
      hardwareMonitorMenu.DropDownItems.Add(monitorCPUMenu);
      if (hasNVIDIAGpu || hasAMDDiscreteGpu) {
        ToolStripMenuItem monitorGPUMenu = new ToolStripMenuItem(Strings.MonitorGpuLabel);
        monitorGPUMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorGpuOn, "monitorGPUGroup", (s, e) => {
          bool wasAllOff = !monitorCPU && !monitorGPU;
          monitorGPU = true;
          gpuTempReady = false; // 等待获取到温度后再参与风扇控制
          rawPowerGPU = 0f;     // 清除可能残留的脏功率值
          GPUPower = 0f;
          if (hasStopAuto)
            autoStopMonitorGPU = false;
          //重置自动开启标志
          hasStartAuto = false;
          autoStartMonitorGPU = true;
          if (wasAllOff) {
            // 从全关状态重启监控进程
            tempReady = false;
            cpuTempReady = false;
            rawPowerCPU = 0f;
            CPUPower = 0f;
            StartHardwareMonitor();
          } else {
            SetGpuMonitorState(true);
          }
          SaveConfig("MonitorGPU");
        }, true));
        monitorGPUMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorGpuOff, "monitorGPUGroup", (s, e) => {
          // 自动转速模式下禁止彻底关闭监控
          if (!monitorCPU && fanControl == "auto") {
            MessageBox.Show(Strings.MonitorAutoFanWarning, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateCheckedState("monitorGPUGroup", monitorGPU ? Strings.MonitorGpuOn : Strings.MonitorGpuOff);
            skipCheckedUpdate = true;
            return;
          }
          monitorGPU = false;
          gpuTempReady = false;
          rawPowerGPU = 0f;  // 关闭时清零，避免重新开启时读到旧值
          GPUPower = 0f;
          if (hasStartAuto)
            autoStartMonitorGPU = false;
          //重置自动关闭标志
          hasStopAuto = false;
          autoStopMonitorGPU = true;
          SetGpuMonitorState(false);
          // 若CPU和GPU均已关闭，停止监控进程
          if (!monitorCPU && !monitorGPU) {
            StopHardwareMonitor();
          }
          SaveConfig("MonitorGPU");
        }, false));
        hardwareMonitorMenu.DropDownItems.Add(monitorGPUMenu);
      }
      ToolStripMenuItem monitorFanMenu = new ToolStripMenuItem(Strings.MonitorFanLabel);
      monitorFanMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorFanOn, "monitorFanGroup", (s, e) => {
        monitorFan = true;
        SaveConfig("MonitorFan");
      }, true));
      monitorFanMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorFanOff, "monitorFanGroup", (s, e) => {
        monitorFan = false;
        SaveConfig("MonitorFan");
      }, false));
      hardwareMonitorMenu.DropDownItems.Add(monitorFanMenu);
      ToolStripMenuItem monitorRefreshMenu = new ToolStripMenuItem(Strings.MonitorRefresh);
      monitorRefreshMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorRefreshHigh, "monitorRefreshGroup", (s, e) => {
        monitorRefreshRate = "high";
        tooltipUpdateTimer.Interval = 250;
        SetMonitorInterval(250);
        SaveConfig("MonitorRefreshRate");
      }, false));
      monitorRefreshMenu.DropDownItems.Add(CreateMenuItem(Strings.MonitorRefreshLow, "monitorRefreshGroup", (s, e) => {
        monitorRefreshRate = "low";
        tooltipUpdateTimer.Interval = 1000;
        SetMonitorInterval(1000);
        SaveConfig("MonitorRefreshRate");
      }, true));
      hardwareMonitorMenu.DropDownItems.Add(monitorRefreshMenu);
      ToolStripMenuItem tempDisplayMenu = new ToolStripMenuItem(Strings.TempDisplay);
      tempDisplayMenu.DropDownItems.Add(CreateMenuItem(Strings.TempSmoothed, "tempDisplayGroup", (s, e) => {
        tempDisplayMode = "smoothed";
        SaveConfig("TempDisplayMode");
      }, true));
      tempDisplayMenu.DropDownItems.Add(CreateMenuItem(Strings.TempRaw, "tempDisplayGroup", (s, e) => {
        tempDisplayMode = "raw";
        SaveConfig("TempDisplayMode");
      }, false));
      hardwareMonitorMenu.DropDownItems.Add(tempDisplayMenu);
      menu.Items.Add(hardwareMonitorMenu);
      ToolStripMenuItem floatingBarMenu = new ToolStripMenuItem(Strings.FloatingBar);
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FloatingHide, "floatingBarGroup", (s, e) => {
        floatingBar = "off";
        CloseFloatingForm();
        SaveConfig("FloatingBar");
      }, true));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FloatingShow, "floatingBarGroup", (s, e) => {
        floatingBar = "on";
        ShowFloatingForm();
        SaveConfig("FloatingBar");
      }, false));
      floatingBarMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FontSize24, "floatingBarSizeGroup", (s, e) => {
        textSize = 24;
        UpdateFloatingText();
        SaveConfig("FloatingBarSize");
      }, false));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FontSize36, "floatingBarSizeGroup", (s, e) => {
        textSize = 36;
        UpdateFloatingText();
        SaveConfig("FloatingBarSize");
      }, false));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FontSize48, "floatingBarSizeGroup", (s, e) => {
        textSize = 48;
        UpdateFloatingText();
        SaveConfig("FloatingBarSize");
      }, true));
      floatingBarMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FloatingLocLeft, "floatingBarLocGroup", (s, e) => {
        floatingBarLoc = "left";
        UpdateFloatingText();
        SaveConfig("FloatingBarLoc");
      }, true));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem(Strings.FloatingLocRight, "floatingBarLocGroup", (s, e) => {
        floatingBarLoc = "right";
        UpdateFloatingText();
        SaveConfig("FloatingBarLoc");
      }, false));
      menu.Items.Add(floatingBarMenu);
      ToolStripMenuItem omenKeyMenu = new ToolStripMenuItem(Strings.OmenKeyMenu);
      omenKeyMenu.DropDownItems.Add(CreateMenuItem(Strings.OmenKeyDefault, "omenKeyGroup", (s, e) => {
        omenKey = "default";
        tooltipUpdateTimer.Enabled = false;
        OmenKeyOff();
        OmenKeyOn(omenKey);
        SaveConfig("OmenKey");
      }, true));
      omenKeyMenu.DropDownItems.Add(CreateMenuItem(Strings.OmenKeyToggle, "omenKeyGroup", (s, e) => {
        omenKey = "custom";
        checkFloatingTimer.Enabled = true;
        OmenKeyOff();
        OmenKeyOn(omenKey);
        SaveConfig("OmenKey");
      }, false));
      omenKeyMenu.DropDownItems.Add(CreateMenuItem(Strings.OmenKeyNone, "omenKeyGroup", (s, e) => {
        omenKey = "none";
        checkFloatingTimer.Enabled = false;
        OmenKeyOff();
        SaveConfig("OmenKey");
      }, false));
      menu.Items.Add(omenKeyMenu);

      ToolStripMenuItem settingMenu = new ToolStripMenuItem(Strings.OtherSettings);
      languageMenu = new ToolStripMenuItem(Strings.LanguageMenu);
      var langItems = new (string label, string code)[] {
        (Strings.LangSimplified,  "zh-CN"),
        (Strings.LangTraditional, "zh-TW"),
        (Strings.LangEnglish,     "en"),
      };
      foreach (var lang in langItems) {
        var localLang = lang;
        var langItem = new ToolStripMenuItem(localLang.label) {
          Tag = "languageGroup",
          Checked = (appLanguage == localLang.code)
        };
        langItem.Click += (s, e) => {
          if (appLanguage == localLang.code) return;

          appLanguage = localLang.code;
          ApplyLanguage(appLanguage);
          SaveConfig("AppLanguage");
          RefreshMenu();
        };
        languageMenu.DropDownItems.Add(langItem);
      }
      settingMenu.DropDownItems.Add(languageMenu);
      ToolStripMenuItem customIconMenu = new ToolStripMenuItem(Strings.IconMenu);
      customIconMenu.DropDownItems.Add(CreateMenuItem(Strings.IconOriginal, "customIconGroup", (s, e) => {
        customIcon = "original";
        trayIcon.Icon = Properties.Resources.smallfan;
        SaveConfig("CustomIcon");
      }, true));
      customIconMenu.DropDownItems.Add(CreateMenuItem(Strings.IconCustom, "customIconGroup", (s, e) => {
        customIcon = "custom";
        SetCustomIcon();
        SaveConfig("CustomIcon");
      }, false));
      customIconMenu.DropDownItems.Add(CreateMenuItem(Strings.IconDynamic, "customIconGroup", (s, e) => {
        customIcon = "dynamic";
        UpdateDynamicIcon();
        SaveConfig("CustomIcon");
      }, false));
      settingMenu.DropDownItems.Add(customIconMenu);
      ToolStripMenuItem dataLocalizeMenu = new ToolStripMenuItem(Strings.DataLocalize);
      dataLocalizeMenu.DropDownItems.Add(CreateMenuItem(Strings.Enable, "dataLocalizeGroup", (s, e) => {
        dataLocalize = "on";
        SaveConfig("DataLocalize");
      }, false));
      dataLocalizeMenu.DropDownItems.Add(CreateMenuItem(Strings.Disable, "dataLocalizeGroup", (s, e) => {
        dataLocalize = "off";
        SaveConfig("DataLocalize");
      }, true));
      settingMenu.DropDownItems.Add(dataLocalizeMenu);
      ToolStripMenuItem autoStartMenu = new ToolStripMenuItem(Strings.AutoStart);
      autoStartMenu.DropDownItems.Add(CreateMenuItem(Strings.Enable, "autoStartGroup", (s, e) => {
        autoStart = "on";
        AutoStartEnable();
        SaveConfig("AutoStart");
      }, false));
      autoStartMenu.DropDownItems.Add(CreateMenuItem(Strings.Disable, "autoStartGroup", (s, e) => {
        autoStart = "off";
        AutoStartDisable();
        SaveConfig("AutoStart");
      }, true));
      settingMenu.DropDownItems.Add(autoStartMenu);

      menu.Items.Add(settingMenu);

      menu.Items.Add(new ToolStripSeparator()); // Separator between groups
      menu.Items.Add(CreateMenuItem(Strings.Help, null, (s, e) => {
        HelpForm.Instance.Show();
      }, false));
      menu.Items.Add(new ToolStripSeparator()); // Separator between groups
      menu.Items.Add(CreateMenuItem(Strings.Exit, null, (s, e) => Exit(), false));
    }

    // 在合适的位置添加此方法（例如 OmenHardware 类或独立的辅助类）
    public static void StartCleanCreekWithProgress(int durationMs, string title, Action startCleanAction, Action stopCleanAction) {
      // 创建进度窗体
      Form progressForm = new Form();
      progressForm.Text = title;
      progressForm.Size = new System.Drawing.Size(300, 150);
      progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
      progressForm.ControlBox = false;
      progressForm.StartPosition = FormStartPosition.CenterScreen;

      Label lblMessage = new Label();
      lblMessage.Text = string.Format(Strings.CleanCreekProgressMessageTemplate, durationMs / 1000);
      lblMessage.Dock = DockStyle.Top;
      lblMessage.Height = 40;
      lblMessage.TextAlign = ContentAlignment.MiddleCenter;

      Button btnStop = new Button();
      btnStop.Text = Strings.CleanCreekStopButton;
      btnStop.DialogResult = DialogResult.Cancel;
      btnStop.Dock = DockStyle.Bottom;
      btnStop.Height = 35;

      progressForm.Controls.Add(lblMessage);
      progressForm.Controls.Add(btnStop);

      // 倒计时计时器
      System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
      timer.Interval = 1000; // 每秒更新一次
      DateTime startTime = DateTime.Now;
      int remainingSeconds = durationMs / 1000;
      timer.Tick += (sender, e) => {
        TimeSpan elapsed = DateTime.Now - startTime;
        int remaining = (int)(durationMs - elapsed.TotalMilliseconds) / 1000;
        if (remaining <= 0) {
          timer.Stop();
          progressForm.Close();      // 倒计时结束，关闭窗体
        } else {
          lblMessage.Text = string.Format(Strings.CleanCreekProgressMessageTemplate, remaining);
        }
      };

      // 停止按钮点击事件
      btnStop.Click += (sender, e) => {
        timer.Stop();
        progressForm.Close();          // 用户点击停止，关闭窗体
      };

      // 窗体关闭时执行停止清洁（无论是倒计时结束还是用户点击停止）
      progressForm.FormClosed += (sender, e) => {
        stopCleanAction?.Invoke();
      };

      // 开始清洁
      startCleanAction?.Invoke();

      // 启动倒计时
      timer.Start();

      // 显示模态对话框（阻止父窗体操作）
      progressForm.ShowDialog();

      // 注意：ShowDialog 会阻塞直到窗体关闭，但内部倒计时和停止按钮正常工作
    }

    static void AddLightingUI(ToolStripMenuItem parentMenu, LightingDevice device, bool isKeyboard) {
      // 当前设备使用的协议（从模块级变量读取）
      LightingControlInterface iface = isKeyboard ? kbControlInterface : lbControlInterface;

      // ── 协议选择 ───────────────────────────────────────────────────
      ToolStripMenuItem protocolMenu = new ToolStripMenuItem(Strings.LightingWmiProtocol);
      protocolMenu.DropDownItems.Add(new ToolStripMenuItem(Strings.LightingWmiTip) { Enabled = false });
      var protocols = new (string label, LightingControlInterface val)[] {
        (Strings.LightingProtocolBasic, LightingControlInterface.BasicFourZone),
        (Strings.LightingProtocolDojo, LightingControlInterface.Dojo),
      };
      foreach (var proto in protocols) {
        var localProto = proto;
        var protoItem = new ToolStripMenuItem(localProto.label) {
          Checked = (iface == localProto.val)
        };
        protoItem.Click += (sender, args) => {
          if (isKeyboard) kbControlInterface = localProto.val;
          else lbControlInterface = localProto.val;
          SaveConfig("ZoneInterface");
          foreach (ToolStripMenuItem mi in protocolMenu.DropDownItems.OfType<ToolStripMenuItem>())
            mi.Checked = (mi == protoItem);
        };
        protocolMenu.DropDownItems.Add(protoItem);
      }
      parentMenu.DropDownItems.Add(protocolMenu);
      parentMenu.DropDownItems.Add(new ToolStripSeparator());

      // ── 亮度 ────────────────────────────────────────────────────────
      ToolStripMenuItem brightnessMenu = new ToolStripMenuItem(Strings.LightingBrightness);
      byte currentBrightness = GetZoneBrightness();
      for (int b = 0; b <= 100; b += 25) {
        byte curB = (byte)b;
        var brightnessItem = new ToolStripMenuItem($"{b}%") {
          Checked = (curB == currentBrightness)
        };
        brightnessItem.Click += (sender, args) => {
          LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
          SetZoneBrightness(device, curB, ci);
          foreach (ToolStripMenuItem mi in brightnessMenu.DropDownItems.OfType<ToolStripMenuItem>())
            mi.Checked = (mi == brightnessItem);
        };
        brightnessMenu.DropDownItems.Add(brightnessItem);
      }
      var brightnessItem1 = new ToolStripMenuItem($"{128}%") {
        Checked = (currentBrightness == 128)
      };
      brightnessItem1.Click += (sender, args) => {
        LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
        SetZoneBrightness(device, 128, ci);
        foreach (ToolStripMenuItem mi in brightnessMenu.DropDownItems.OfType<ToolStripMenuItem>())
          mi.Checked = (mi == brightnessItem1);
      };
      brightnessMenu.DropDownItems.Add(brightnessItem1);
      var brightnessItem2 = new ToolStripMenuItem($"{228}%") {
        Checked = (currentBrightness == 228)
      };
      brightnessItem2.Click += (sender, args) => {
        LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
        SetZoneBrightness(device, 228, ci);
        foreach (ToolStripMenuItem mi in brightnessMenu.DropDownItems.OfType<ToolStripMenuItem>())
          mi.Checked = (mi == brightnessItem2);
      };
      brightnessMenu.DropDownItems.Add(brightnessItem2);
      parentMenu.DropDownItems.Add(brightnessMenu);

      // ── 静态颜色 ────────────────────────────────────────────────────
      ToolStripMenuItem staticColorMenu = new ToolStripMenuItem(Strings.LightingStaticColor);
      var staticColors = new (string name, byte r, byte g, byte b)[] {
        (Strings.LightingColorRed, 255, 0, 0), (Strings.LightingColorGreen, 0, 255, 0), (Strings.LightingColorBlue, 0, 0, 255),
        (Strings.LightingColorWhite, 255, 255, 255), (Strings.LightingColorCyan, 0, 255, 255), (Strings.LightingColorMagenta, 255, 0, 255), (Strings.LightingColorYellow, 255, 255, 0)
      };

      string[] zoneColorSel = isKeyboard ? zoneColorSel_Keyboard : zoneColorSel_LightBar;
      string globalColorSel = isKeyboard ? zoneGlobalColorSel_Keyboard : zoneGlobalColorSel_LightBar;

      // 全局颜色（四区同色）
      ToolStripMenuItem allZonesMenu = new ToolStripMenuItem(Strings.LightingAllZones);
      foreach (var color in staticColors) {
        var localColor = color;
        var colorItem = new ToolStripMenuItem(localColor.name) {
          Checked = (globalColorSel == localColor.name)
        };
        colorItem.Click += (sender, args) => {
          LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
          var c = System.Windows.Media.Color.FromRgb(localColor.r, localColor.g, localColor.b);
          SetZoneStaticColor(device, new List<System.Windows.Media.Color> { c, c, c, c }, GetZoneBrightness(), ci);
          if (isKeyboard) { zoneGlobalColorSel_Keyboard = localColor.name; for (int z = 0; z < 4; z++) zoneColorSel_Keyboard[z] = localColor.name; } else { zoneGlobalColorSel_LightBar = localColor.name; for (int z = 0; z < 4; z++) zoneColorSel_LightBar[z] = localColor.name; }
          SaveConfig("ZoneColor");
          foreach (ToolStripMenuItem mi in allZonesMenu.DropDownItems.OfType<ToolStripMenuItem>())
            mi.Checked = (mi == colorItem);
        };
        allZonesMenu.DropDownItems.Add(colorItem);
      }
      ToolStripMenuItem customGlobalColor = new ToolStripMenuItem(Strings.LightingCustom);
      customGlobalColor.Click += (sender, args) => {
        using (ColorDialog cd = new ColorDialog { FullOpen = true }) {
          if (cd.ShowDialog() == DialogResult.OK) {
            LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
            var c = System.Windows.Media.Color.FromRgb(cd.Color.R, cd.Color.G, cd.Color.B);
            SetZoneStaticColor(device, new List<System.Windows.Media.Color> { c, c, c, c }, GetZoneBrightness(), ci);
            if (isKeyboard) { zoneGlobalColorSel_Keyboard = null; for (int z = 0; z < 4; z++) zoneColorSel_Keyboard[z] = null; } else { zoneGlobalColorSel_LightBar = null; for (int z = 0; z < 4; z++) zoneColorSel_LightBar[z] = null; }
            SaveConfig("ZoneColor");
            foreach (ToolStripMenuItem mi in allZonesMenu.DropDownItems.OfType<ToolStripMenuItem>())
              mi.Checked = false;
          }
        }
      };
      allZonesMenu.DropDownItems.Add(new ToolStripSeparator());
      allZonesMenu.DropDownItems.Add(customGlobalColor);
      staticColorMenu.DropDownItems.Add(allZonesMenu);

      // 各分区单独颜色
      for (int i = 0; i < 4; i++) {
        int zoneIndex = i;
        ToolStripMenuItem zoneMenu = new ToolStripMenuItem($"{Strings.LightingZone} {zoneIndex + 1}");
        foreach (var color in staticColors) {
          var localColor = color;
          var zoneColorItem = new ToolStripMenuItem(localColor.name) {
            Checked = (zoneColorSel[zoneIndex] == localColor.name)
          };
          zoneColorItem.Click += (sender, args) => {
            LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
            var currentColors = GetZoneStaticColor()?.ToList();
            if (currentColors == null || currentColors.Count != 4)
              currentColors = Enumerable.Repeat(System.Windows.Media.Color.FromRgb(0, 0, 0), 4).ToList();
            currentColors[zoneIndex] = System.Windows.Media.Color.FromRgb(localColor.r, localColor.g, localColor.b);
            SetZoneStaticColor(device, currentColors, GetZoneBrightness(), ci);
            if (isKeyboard) { zoneColorSel_Keyboard[zoneIndex] = localColor.name; zoneGlobalColorSel_Keyboard = null; } else { zoneColorSel_LightBar[zoneIndex] = localColor.name; zoneGlobalColorSel_LightBar = null; }
            SaveConfig("ZoneColor");
            foreach (ToolStripMenuItem mi in zoneMenu.DropDownItems.OfType<ToolStripMenuItem>())
              mi.Checked = (mi == zoneColorItem);
          };
          zoneMenu.DropDownItems.Add(zoneColorItem);
        }
        ToolStripMenuItem customZoneColor = new ToolStripMenuItem(Strings.LightingCustom);
        customZoneColor.Click += (sender, args) => {
          using (ColorDialog cd = new ColorDialog { FullOpen = true }) {
            if (cd.ShowDialog() == DialogResult.OK) {
              LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
              var currentColors = GetZoneStaticColor()?.ToList();
              if (currentColors == null || currentColors.Count != 4)
                currentColors = Enumerable.Repeat(System.Windows.Media.Color.FromRgb(0, 0, 0), 4).ToList();
              currentColors[zoneIndex] = System.Windows.Media.Color.FromRgb(cd.Color.R, cd.Color.G, cd.Color.B);
              SetZoneStaticColor(device, currentColors, GetZoneBrightness(), ci);
              if (isKeyboard) { zoneColorSel_Keyboard[zoneIndex] = null; zoneGlobalColorSel_Keyboard = null; } else { zoneColorSel_LightBar[zoneIndex] = null; zoneGlobalColorSel_LightBar = null; }
              SaveConfig("ZoneColor");
              foreach (ToolStripMenuItem mi in zoneMenu.DropDownItems.OfType<ToolStripMenuItem>())
                mi.Checked = false;
            }
          }
        };
        zoneMenu.DropDownItems.Add(new ToolStripSeparator());
        zoneMenu.DropDownItems.Add(customZoneColor);
        staticColorMenu.DropDownItems.Add(zoneMenu);
      }
      parentMenu.DropDownItems.Add(staticColorMenu);

      // ── 动画效果 ─────────────────────────────────────────────────────
      if (supportAni) {
        ToolStripMenuItem animMenu = new ToolStripMenuItem(Strings.LightingAnimation);
        // 在菜单打开时判断协议，决定展示完整版还是简化版
        // 注意：iface 在 DropDownOpening 时已捕获，但协议可能被用户在同次打开中切换，
        // 所以动画菜单的内容在此次 AddLightingUI 调用时固定。协议切换后需重新打开菜单。
        bool isDojo = iface == LightingControlInterface.Dojo;

        // ── 效果 ──────────────────────────────────────────────────────
        ToolStripMenuItem effectMenu = new ToolStripMenuItem(Strings.LightingEffect);
        // Dojo 支持全部效果；其余协议只支持颜色循环(id=2)和呼吸(id=4)
        var anims = isDojo
        ? new (string name, byte id)[] {
            (Strings.LightingAnimColorCycle, 2), (Strings.LightingAnimStarlight, 3), (Strings.LightingAnimBreathing, 4), (Strings.LightingAnimWave, 6),
            (Strings.LightingAnimRaindrop, 7), (Strings.LightingAnimAudioPulse, 8), (Strings.LightingAnimConfetti, 9), (Strings.LightingAnimSun, 10), (Strings.LightingAnimSwipe, 11)
          }
        : new (string name, byte id)[] {
            (Strings.LightingAnimColorCycle, 2), (Strings.LightingAnimBreathing, 4)
          };
        foreach (var anim in anims) {
          var localAnim = anim;
          var animItem = new ToolStripMenuItem(localAnim.name) {
            Checked = (localAnim.id == currentAnimEffect)
          };
          animItem.Click += (sender, args) => {
            currentAnimEffect = localAnim.id;
            LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
            SetZoneAnimation(device, currentAnimEffect, currentAnimSpeed, currentAnimDirection, currentAnimTheme,
                currentAnimTheme == 4 ? GetZoneStaticColor()?.ToList() : null, GetZoneBrightness(), ci);
            foreach (ToolStripMenuItem mi in effectMenu.DropDownItems.OfType<ToolStripMenuItem>())
              mi.Checked = (mi == animItem);
          };
          effectMenu.DropDownItems.Add(animItem);
        }
        animMenu.DropDownItems.Add(effectMenu);

        // ── 速度 ──────────────────────────────────────────────────────
        // Dojo：0/1/2 直接传给硬件；非 Dojo：转换为间隔 10/5/2（在 SetZoneAnimation 内完成）
        ToolStripMenuItem speedMenu = new ToolStripMenuItem(Strings.LightingSpeed);
        var speeds = new (string name, byte val)[] { (Strings.LightingSpeedSlow, 0), (Strings.LightingSpeedMedium, 1), (Strings.LightingSpeedFast, 2) };
        foreach (var sp in speeds) {
          var localSp = sp;
          var speedItem = new ToolStripMenuItem(localSp.name) {
            Checked = (localSp.val == currentAnimSpeed)
          };
          speedItem.Click += (sender, args) => {
            currentAnimSpeed = localSp.val;
            LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
            SetZoneAnimation(device, currentAnimEffect, currentAnimSpeed, currentAnimDirection, currentAnimTheme,
                currentAnimTheme == 4 ? GetZoneStaticColor()?.ToList() : null, GetZoneBrightness(), ci);
            foreach (ToolStripMenuItem mi in speedMenu.DropDownItems.OfType<ToolStripMenuItem>())
              mi.Checked = (mi == speedItem);
          };
          speedMenu.DropDownItems.Add(speedItem);
        }
        animMenu.DropDownItems.Add(speedMenu);

        // ── 方向・主题（仅 Dojo 协议支持） ───────────────────────────
        if (isDojo) {
          ToolStripMenuItem dirMenu = new ToolStripMenuItem(Strings.LightingDirection);
          var dirs = new (string name, byte val)[] { (Strings.LightingDirLeft, 0), (Strings.LightingDirRight, 1) };
          foreach (var d in dirs) {
            var localD = d;
            var dirItem = new ToolStripMenuItem(localD.name) {
              Checked = (localD.val == currentAnimDirection)
            };
            dirItem.Click += (sender, args) => {
              currentAnimDirection = localD.val;
              LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
              SetZoneAnimation(device, currentAnimEffect, currentAnimSpeed, currentAnimDirection, currentAnimTheme,
                  currentAnimTheme == 4 ? GetZoneStaticColor()?.ToList() : null, GetZoneBrightness(), ci);
              foreach (ToolStripMenuItem mi in dirMenu.DropDownItems.OfType<ToolStripMenuItem>())
                mi.Checked = (mi == dirItem);
            };
            dirMenu.DropDownItems.Add(dirItem);
          }
          animMenu.DropDownItems.Add(dirMenu);

          ToolStripMenuItem themeMenu = new ToolStripMenuItem(Strings.LightingTheme);
          var themes = new (string name, byte val)[] {
              (Strings.LightingThemeGalaxy, 0), (Strings.LightingThemeVolcano, 1), (Strings.LightingThemeJungle, 2), (Strings.LightingThemeOcean, 3), (Strings.LightingThemeCustom, 4)
          };
          foreach (var t in themes) {
            var localT = t;
            var themeItem = new ToolStripMenuItem(localT.name) {
              Checked = (localT.val == currentAnimTheme)
            };
            themeItem.Click += (sender, args) => {
              currentAnimTheme = localT.val;
              LightingControlInterface ci = isKeyboard ? kbControlInterface : lbControlInterface;
              SetZoneAnimation(device, currentAnimEffect, currentAnimSpeed, currentAnimDirection, currentAnimTheme,
                  currentAnimTheme == 4 ? GetZoneStaticColor()?.ToList() : null, GetZoneBrightness(), ci);
              foreach (ToolStripMenuItem mi in themeMenu.DropDownItems.OfType<ToolStripMenuItem>())
                mi.Checked = (mi == themeItem);
            };
            themeMenu.DropDownItems.Add(themeItem);
          }
          animMenu.DropDownItems.Add(themeMenu);
        }

        parentMenu.DropDownItems.Add(animMenu);
      }
    }

    static void AddPerKeyLightingUI(ToolStripMenuItem parentMenu) {
      // 静态颜色预设
      var staticColorMenu = new ToolStripMenuItem(Strings.LightingPerKeyStaticColor);
      var colorPresets = new (string name, byte r, byte g, byte b)[] {
          (Strings.LightingColorRed, 255, 0, 0), (Strings.LightingColorGreen, 0, 255, 0), (Strings.LightingColorBlue, 0, 0, 255),
          (Strings.LightingColorWhite, 255, 255, 255), (Strings.LightingColorCyan, 0, 255, 255), (Strings.LightingColorMagenta, 255, 0, 255), (Strings.LightingColorYellow, 255, 255, 0)
      };
      foreach (var cp in colorPresets) {
        var localCp = cp;
        var cpItem = new ToolStripMenuItem(localCp.name) {
          Checked = (perKeyStaticColorSel == localCp.name)
        };
        cpItem.Click += (sender, args) => {
          int handle = OpenPerKeyKeyboard();
          if (handle > 0) {
            try {
              const int keyCount = 144;
              var r = new byte[keyCount]; var g = new byte[keyCount]; var b = new byte[keyCount];
              for (int i = 0; i < keyCount; i++) { r[i] = localCp.r; g[i] = localCp.g; b[i] = localCp.b; }
              SetPerKeyStaticColor(handle, r, g, b).Wait();
              StorePerKeyToFlash(handle).Wait();
              perKeyStaticColorSel = localCp.name;
              perKeyAnimationSel = null; // 切换到静态颜色，清除动画选中
              foreach (ToolStripMenuItem mi in staticColorMenu.DropDownItems.OfType<ToolStripMenuItem>())
                mi.Checked = (mi == cpItem);
            } catch (Exception ex) { Logger.Error(ex.Message); } finally { CloseDeviceAsync(handle).Wait(); }
          } else
            MessageBox.Show(Strings.KeyboardConnectFail, Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        staticColorMenu.DropDownItems.Add(cpItem);
      }
      parentMenu.DropDownItems.Add(staticColorMenu);

      // 动画效果
      var animMenu = new ToolStripMenuItem(Strings.LightingPerKeyAnimation);
      var anims = new (string name, byte eff)[] {
          (Strings.LightingAnimColorCycle, 2), (Strings.LightingAnimStarlight, 3), (Strings.LightingAnimBreathing, 4), (Strings.LightingAnimWave, 6),
          (Strings.LightingAnimRaindrop, 7), (Strings.LightingAnimAudioPulse, 8), (Strings.LightingAnimConfetti, 9), (Strings.LightingAnimSun, 10), (Strings.LightingAnimSwipe, 11)
      };
      foreach (var a in anims) {
        var localA = a;
        var animItem = new ToolStripMenuItem(localA.name) {
          Checked = (perKeyAnimationSel == localA.name)
        };
        animItem.Click += (sender, args) => {
          int handle = OpenPerKeyKeyboard();
          if (handle > 0) {
            try {
              var setting = new LightingSetting();
              byte mcuEff;
              switch (localA.eff) {
                case 2: mcuEff = 7; break;
                case 3: mcuEff = 2; break;
                case 4: mcuEff = 8; break;
                case 6: mcuEff = 10; break;
                case 7: mcuEff = 13; break;
                case 9: mcuEff = 14; break;
                case 10: mcuEff = 15; break;
                case 11: mcuEff = 16; break;
                default: mcuEff = 4; break;
              }
              setting.Effect = mcuEff;
              setting.LedSpeed = 1;
              setting.Direction = 0;
              setting.Brightness = 100;
              setting.ColorNumber = 4;
              setting.ShowMode = 0;
              SetPerKeyAnimation(handle, setting).Wait();
              perKeyAnimationSel = localA.name;
              perKeyStaticColorSel = null; // 切换到动画，清除静态颜色选中
              foreach (ToolStripMenuItem mi in animMenu.DropDownItems.OfType<ToolStripMenuItem>())
                mi.Checked = (mi == animItem);
            } catch (Exception ex) { Logger.Error(ex.Message); } finally { CloseDeviceAsync(handle).Wait(); }
          } else
            MessageBox.Show(Strings.KeyboardConnectFail, Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        animMenu.DropDownItems.Add(animItem);
      }
      parentMenu.DropDownItems.Add(animMenu);

      // 亮度
      var brightMenu = new ToolStripMenuItem(Strings.LightingPerKeyBrightness);
      for (int b = 0; b <= 100; b += 25) {
        byte curB = (byte)b;
        var brightItem = new ToolStripMenuItem($"{b}%") {
          Checked = (perKeyBrightnessSel == curB)
        };
        brightItem.Click += (sender, args) => {
          int handle = OpenPerKeyKeyboard();
          if (handle > 0) {
            try {
              SetPerKeyBrightness(handle, curB).Wait();
              perKeyBrightnessSel = curB;
              foreach (ToolStripMenuItem mi in brightMenu.DropDownItems.OfType<ToolStripMenuItem>())
                mi.Checked = (mi == brightItem);
            } catch (Exception ex) { Logger.Error(ex.Message); } finally { CloseDeviceAsync(handle).Wait(); }
          } else
            MessageBox.Show(Strings.KeyboardConnectFail, Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        brightMenu.DropDownItems.Add(brightItem);
      }
      parentMenu.DropDownItems.Add(brightMenu);
    }

    static void RefreshMenu() {
      if (trayIcon == null || trayIcon.ContextMenuStrip == null) return;
      BuildTrayMenu(trayIcon.ContextMenuStrip);
    }

    static ToolStripMenuItem CreateMenuItem(string text, string group, EventHandler action, bool isChecked, string toolTip = null) {
      var item = new ToolStripMenuItem(text) {
        Tag = group,
        Checked = isChecked, // Set initial checked state
        ToolTipText = toolTip   // 设置提示文本
      };
      item.Click += (s, e) => {
        if (item.Text == Strings.DbUnlocked) {
          string gpuModel = GetNVIDIAModel();
          if (gpuModel != null) {
            var match = Regex.Match(gpuModel, @"^\d+");
            if (match.Success && int.TryParse(match.Value, out int modelNum)) {
              if (modelNum >= 5000) {
                MessageBox.Show(Strings.DbNo50Series, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DBVersion = 2;
                countDB = 0;
                SaveConfig("DBVersion");
                UpdateCheckedState("DBGroup", Strings.DbNormal);
                return;
              }
            }
          }

          if (!powerOnline) {
            MessageBox.Show(Strings.PleaseConnectAC, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DBVersion = 2;
            countDB = 0;
            SaveConfig("DBVersion");
            UpdateCheckedState("DBGroup", Strings.DbNormal);
            return;
          }
          if (!CheckDBVersion(1)) {
            DBVersion = 2;
            countDB = 0;
            SaveConfig("DBVersion");
            UpdateCheckedState("DBGroup", Strings.DbNormal);
            return;
          }
          //if(CPUPower > CPULimitDB + 1) {
          //  MessageBox.Show(Strings.DbUnlockCpuHighWarning, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          //  DBVersion = 2;
          //  countDB = 0;
          //  SaveConfig("DBVersion");
          //  UpdateCheckedState("DBGroup", Strings.DbNormal);
          //  return;
          //}
        }
        if (item.Text == Strings.DbNormal && !CheckDBVersion(2))
          return;
        if (item.Text == Strings.IconCustom && !CheckCustomIcon())
          return;

        action(s, e); // Perform the original action
        if (group != null) {
          if (skipCheckedUpdate) {
            skipCheckedUpdate = false;
          } else {
            UpdateCheckedState(group, null, item);
          }
        }
      };
      return item;
    }

    static void UpdateCheckedState(string group, string itemText = null, ToolStripMenuItem menuItemToCheck = null) {
      if (menuItemToCheck == null) {
        // 先尝试匹配相同 group 和名称的选项，防止不同菜单组出现同名冲突（如都有“开启”/“关闭”）
        ToolStripMenuItem FindExact(ToolStripItemCollection items) {
          foreach (ToolStripMenuItem item in items.OfType<ToolStripMenuItem>()) {
            if (item.Text == itemText && string.Equals(item.Tag as string, group)) return item;
            if (item.HasDropDownItems) {
              var found = FindExact(item.DropDownItems);
              if (found != null) return found;
            }
          }
          return null;
        }

        menuItemToCheck = FindExact(trayIcon.ContextMenuStrip.Items) ?? FindMenuItem(trayIcon.ContextMenuStrip.Items, itemText);

        if (menuItemToCheck == null)
          return;
      }

      void UpdateMenuItemsCheckedState(ToolStripItemCollection items, ToolStripMenuItem clicked) {
        foreach (ToolStripMenuItem menuItem in items.OfType<ToolStripMenuItem>()) {
          // 检查是否属于同一个组
          if (menuItem.Tag as string == group) {
            menuItem.Checked = (menuItem == clicked);
          }
          // 如果当前项有子菜单，递归调用处理子菜单项
          if (menuItem.HasDropDownItems) {
            UpdateMenuItemsCheckedState(menuItem.DropDownItems, clicked);
          }
        }
      }
      // 从ContextMenuStrip的根菜单项开始递归
      UpdateMenuItemsCheckedState(trayIcon.ContextMenuStrip.Items, menuItemToCheck);
    }

    // 递归查找指定文本的菜单项
    static ToolStripMenuItem FindMenuItem(ToolStripItemCollection items, string itemText, int select = 2) {
      foreach (ToolStripMenuItem menuItem in items.OfType<ToolStripMenuItem>()) {
        if (menuItem.Text == itemText) {
          return menuItem;
        }

        if (menuItem.HasDropDownItems) {
          var foundItem = FindMenuItem(menuItem.DropDownItems, itemText);
          if (foundItem != null) {
            // 启用或禁用对应项
            if (select == 1)
              foundItem.Enabled = true;
            else if (select == 0)
              foundItem.Enabled = false;
            return foundItem;
          }
        }
      }
      return null;
    }

    /// <summary>
    /// 根据当前 appLanguage 值恢复语言菜单的勾选状态。
    /// 语言菜单项的 Tag 为 "languageGroup"，通过遍历而非文本匹配来找到对应项，
    /// 以免因多语言文本不一致导致匹配失败。
    /// </summary>
    static void RestoreLanguageChecked() {
      // 映射：语言代码 → 该语言下自己的显示文本
      string targetText;
      switch (appLanguage) {
        case "zh-TW": targetText = Strings.LangTraditional; break;
        case "en": targetText = Strings.LangEnglish; break;
        default: targetText = Strings.LangSimplified; break;
      }
      // 在菜单树中找到 languageGroup 中文本匹配的项并勾选
      void Walk(ToolStripItemCollection items) {
        foreach (ToolStripMenuItem mi in items.OfType<ToolStripMenuItem>()) {
          if ((mi.Tag as string) == "languageGroup") {
            mi.Checked = (mi.Text == targetText);
          }
          if (mi.HasDropDownItems) Walk(mi.DropDownItems);
        }
      }
      Walk(trayIcon.ContextMenuStrip.Items);
    }
  }
}
