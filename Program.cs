using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.TaskScheduler;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Drawing;
using OpenComputer = OpenHardwareMonitor.Hardware.Computer;
using OpenIHardware = OpenHardwareMonitor.Hardware.IHardware;
using OpenHardwareType = OpenHardwareMonitor.Hardware.HardwareType;
using OpenISensor = OpenHardwareMonitor.Hardware.ISensor;
using OpenSensorType = OpenHardwareMonitor.Hardware.SensorType;
using LibreComputer = LibreHardwareMonitor.Hardware.Computer;
using LibreIHardware = LibreHardwareMonitor.Hardware.IHardware;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreISensor = LibreHardwareMonitor.Hardware.ISensor;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;
using static OmenSuperHub.OmenHardware;
using System.IO.Pipes;

namespace OmenSuperHub {
  static class Program {
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    static float CPUTemp = 50;
    static float GPUTemp = 40;
    static float CPUPower = 0;
    static float GPUPower = 0;
    static int DBVersion = 2, countDB = 0, countDBInit = 5, CPULimitDB = 25;
    static int textSize = 48;
    static int countRestore = 0, gpuClock = 0;
    static int alreadyRead = 0, alreadyReadCode = 1000;
    static string fanTable = "silent", fanMode = "performance", fanControl = "auto", tempSensitivity = "high", cpuPower = "max", gpuPower = "max", autoStart = "off", customIcon = "original", floatingBar = "off", floatingBarLoc = "left", omenKey = "default";
    static OpenComputer openComputer = new OpenComputer() { CPUEnabled = true };
    static LibreComputer libreComputer = new LibreComputer() { IsCpuEnabled = true, IsGpuEnabled = true };
    static bool openLib = true, monitorGPU = true, monitorFan = true, isConnectedToNVIDIA = true, powerOnline = true, checkFloating = false;
    static List<int> fanSpeedNow = new List<int> { 20, 23 };
    static float respondSpeed = 0.4f;
    private static Dictionary<float, List<int>> CPUTempFanMap = new Dictionary<float, List<int>>();
    private static Dictionary<float, List<int>> GPUTempFanMap = new Dictionary<float, List<int>>();
    static System.Threading.Timer fanControlTimer;
    static System.Timers.Timer tooltipUpdateTimer; // Timer for updating tooltip
    static System.Windows.Forms.Timer checkFloatingTimer, optimiseTimer;
    static NotifyIcon trayIcon;
    static FloatingForm floatingForm;

    [STAThread]
    static void Main(string[] args) {
      bool isNewInstance;
      using (Mutex mutex = new Mutex(true, "MyUniqueAppMutex", out isNewInstance)) {
        if (!isNewInstance) {
          return;
        }

        if (Environment.OSVersion.Version.Major >= 6) {
          SetProcessDPIAware();
        }

        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        powerOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        monitorQuery();

        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        // Initialize tray icon
        InitTrayIcon();

        // Initialize HardwareMonitorLib
        openComputer.Open();
        libreComputer.Open();

        optimiseTimer = new System.Windows.Forms.Timer();
        optimiseTimer.Interval = 30000;
        optimiseTimer.Tick += (s, e) => optimiseSchedule();
        optimiseTimer.Start();

        // Main loop to query CPU and GPU temperature every second
        fanControlTimer = new System.Threading.Timer((e) => {
          int fanSpeed1 = GetFanSpeedForTemperature(0) / 100;
          int fanSpeed2 = GetFanSpeedForTemperature(1) / 100;
          if (fanSpeed1 != fanSpeedNow[0] || fanSpeed2 != fanSpeedNow[1]) {
            SetFanLevel(fanSpeed1, fanSpeed2);
          }
        }, null, 100, 1000);

        getOmenKeyTask();
        checkFloatingTimer = new System.Windows.Forms.Timer();
        checkFloatingTimer.Interval = 100;
        checkFloatingTimer.Tick += (s, e) => HandleFloatingBarToggle();
        checkFloatingTimer.Start();

        // Restore last setting
        RestoreConfig();

        if (alreadyRead != alreadyReadCode) {
          HelpForm.Instance.Show();
          alreadyRead = alreadyReadCode;
          SaveConfig("AlreadyRead");
        }

        SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerChange);

        Application.Run();
      }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE {
      [MarshalAs(UnmanagedType.U4)]
      public int cb;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
      public string DeviceName;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string DeviceString;
      [MarshalAs(UnmanagedType.U4)]
      public DisplayDeviceStateFlags StateFlags;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string DeviceID;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string DeviceKey;
    }

    [Flags()]
    public enum DisplayDeviceStateFlags : int {
      /// <summary>The device is part of the desktop.</summary>
      AttachedToDesktop = 0x1,
      MultiDriver = 0x2,
      /// <summary>The device is part of the desktop.</summary>
      PrimaryDevice = 0x4,
      /// <summary>Represents a pseudo device used to mirror application drawing for remoting or other purposes.</summary>
      MirroringDriver = 0x8,
      /// <summary>The device is VGA compatible.</summary>
      VGACompatible = 0x10,
      /// <summary>The device is removable; it cannot be the primary display.</summary>
      Removable = 0x20,
      /// <summary>The device has more display devices.</summary>
      ModesPruned = 0x8000000,
      Remote = 0x4000000,
      Disconnect = 0x2000000
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(
        string lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    // 判断独显未工作条件
    private static void monitorQuery() {
      if (Screen.AllScreens.Length != 1)
        return;
      DISPLAY_DEVICE d = new DISPLAY_DEVICE();
      d.cb = Marshal.SizeOf(d);
      uint deviceNum = 0;

      while (EnumDisplayDevices(null, deviceNum, ref d, 0)) {
        if (d.StateFlags.HasFlag(DisplayDeviceStateFlags.AttachedToDesktop)) {
          if (d.DeviceString.Contains("Intel") || d.DeviceString.Contains("AMD")) {
            isConnectedToNVIDIA = false;
            return;
          }
        }
        deviceNum++;
      }

      isConnectedToNVIDIA = true;
    }

    private static void optimiseSchedule() {
      //定时通信避免功耗锁定
      GetFanCount();
      //更新显示器连接到显卡状态
      monitorQuery();
      GC.Collect();
    }

    static void OnPowerChange(object s, PowerModeChangedEventArgs e) {
      // 休眠重新启动
      if (e.Mode == PowerModes.Resume) {
        // GetFanCount
        SendOmenBiosWmi(0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);

        tooltipUpdateTimer.Start();
        countRestore = 3;
      }

      // 检查电源模式是否发生变化
      if (e.Mode == PowerModes.StatusChange) {
        // 获取当前电源连接状态
        var powerStatus = SystemInformation.PowerStatus;
        if (powerStatus.PowerLineStatus == PowerLineStatus.Online) {
          Console.WriteLine("笔记本已连接到电源。");
          powerOnline = true;
        } else {
          Console.WriteLine("笔记本未连接到电源。");
          powerOnline = false;
        }
      }
    }

    // 任务计划程序
    static void AutoStartEnable() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;

      using (TaskService ts = new TaskService()) {
        TaskDefinition td = ts.NewTask();
        td.RegistrationInfo.Description = "Start OmenSuperHub with admin rights";
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Actions.Add(new ExecAction(Path.Combine(currentPath, "OmenSuperHub.exe"), null, null));

        // 设置触发器：在用户登录时触发
        LogonTrigger logonTrigger = new LogonTrigger();
        //logonTrigger.Delay = TimeSpan.FromSeconds(10); // 延迟 10 秒
        td.Triggers.Add(logonTrigger);

        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        td.Settings.AllowHardTerminate = false;

        ts.RootFolder.RegisterTaskDefinition(@"OmenSuperHub", td);
        Console.WriteLine("任务已创建。");
      }

      CleanUpAndRemoveTasks();
    }

    static void AutoStartDisable() {
      using (TaskService ts = new TaskService()) {
        // 检查任务是否存在
        Task existingTask = ts.FindTask("OmenSuperHub");

        if (existingTask != null) {
          // 删除任务
          ts.RootFolder.DeleteTask("OmenSuperHub");
          Console.WriteLine("任务已删除。");
        } else {
          Console.WriteLine("任务不存在，无需删除。");
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
        Console.WriteLine(result.Output);
      } else {
        Console.WriteLine("旧文件夹不存在");
      }

      // 删除 file1
      if (File.Exists(file1)) {
        string command = $"del /f /q \"{file1}\"";
        var result = ExecuteCommand(command);
        Console.WriteLine($"文件已删除: {file1}");
        Console.WriteLine(result.Output);
      } else {
        Console.WriteLine($"文件不存在: {file1}");
      }

      // 删除 file2
      if (File.Exists(file2)) {
        string command = $"del /f /q \"{file2}\"";
        var result = ExecuteCommand(command);
        Console.WriteLine($"文件已删除: {file2}");
        Console.WriteLine(result.Output);
      } else {
        Console.WriteLine($"文件不存在: {file2}");
      }

      // 检查并删除计划任务
      string taskQueryCommand = $"schtasks /query /tn \"{taskName}\"";
      var taskQueryResult = ExecuteCommand(taskQueryCommand);
      if (taskQueryResult.ExitCode == 0) {
        string deleteTaskCommand = $"schtasks /delete /tn \"{taskName}\" /f";
        var deleteTaskResult = ExecuteCommand(deleteTaskCommand);
        Console.WriteLine("已成功删除计划任务 \"Omen Boot\"。");
        Console.WriteLine(deleteTaskResult.Output);
      } else {
        Console.WriteLine($"计划任务 \"{taskName}\" 不存在。");
      }

      // 从注册表中删除开机自启项
      string regDeleteCommand = @"reg delete ""HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"" /v ""OmenSuperHub"" /f";
      var regDeleteResult = ExecuteCommand(regDeleteCommand);
      Console.WriteLine("成功取消开机自启");
      Console.WriteLine(regDeleteResult.Output);
    }

    // Initialize tray icon
    private static void InitTrayIcon() {
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
              UpdateCheckedState("CustomIcon", "原版");
            }
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error restoring configuration: {ex.Message}");
      }

      trayIcon = new NotifyIcon() {
        // Icon = SystemIcons.Application,
        Icon = Properties.Resources.smallfan,
        ContextMenuStrip = new ContextMenuStrip(),
        Visible = true
      };

      trayIcon.MouseClick += TrayIcon_MouseClick;

      switch (customIcon) {
        case "original":
          trayIcon.Icon = Properties.Resources.smallfan;
          break;
        case "custom":
          SetCustomIcon();
          break;
        case "dynamic":
          GenerateDynamicIcon((int)CPUTemp);
          break;
      }

      trayIcon.ContextMenuStrip.Items.Add(CreateMenuItem("关于OSH", null, (s, e) => {
        HelpForm.Instance.Show();
      }, false));

      trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
      ToolStripMenuItem fanConfigMenu = new ToolStripMenuItem("风扇配置");
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("安静模式", "fanTableGroup", (s, e) => {
        fanTable = "silent";
        LoadFanConfig("silent.txt");
        SaveConfig("FanTable");
      }, true));
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("降温模式", "fanTableGroup", (s, e) => {
        fanTable = "cool";
        LoadFanConfig("cool.txt");
        SaveConfig("FanTable");
      }, false));
      fanConfigMenu.DropDownItems.Add(new ToolStripSeparator());
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("实时", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "realtime";
        respondSpeed = 1;
        SaveConfig("TempSensitivity");
      }, false));
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("高", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "high";
        respondSpeed = 0.4f;
        SaveConfig("TempSensitivity");
      }, true));
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("中", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "medium";
        respondSpeed = 0.1f;
        SaveConfig("TempSensitivity");
      }, false));
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("低", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "low";
        respondSpeed = 0.04f;
        SaveConfig("TempSensitivity");
      }, false));
      trayIcon.ContextMenuStrip.Items.Add(fanConfigMenu);

      ToolStripMenuItem fanControlMenu = new ToolStripMenuItem("风扇控制");
      fanControlMenu.DropDownItems.Add(CreateMenuItem("自动", "fanControlGroup", (s, e) => {
        fanControl = "auto";
        SetMaxFanSpeedOff();
        fanControlTimer.Change(0, 1000);
        SaveConfig("FanControl");
      }, true));
      fanControlMenu.DropDownItems.Add(CreateMenuItem("最大风扇", "fanControlGroup", (s, e) => {
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
          SetFanLevel(currentSpeed / 100, currentSpeed / 100);
          SaveConfig("FanControl");
        }, false));
      }
      trayIcon.ContextMenuStrip.Items.Add(fanControlMenu);

      ToolStripMenuItem performanceControlMenu = new ToolStripMenuItem("性能控制");
      performanceControlMenu.DropDownItems.Add(CreateMenuItem("狂暴模式", "fanModeGroup", (s, e) => {
        fanMode = "performance";
        SetFanMode(0x31);
        SaveConfig("FanMode");
        // 恢复CPU功耗设定
        RestoreCPUPower();
      }, true));
      performanceControlMenu.DropDownItems.Add(CreateMenuItem("平衡模式", "fanModeGroup", (s, e) => {
        fanMode = "default";
        SetFanMode(0x30);
        SaveConfig("FanMode");
        // 恢复CPU功耗设定
        RestoreCPUPower();
      }, false));
      performanceControlMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      performanceControlMenu.DropDownItems.Add(CreateMenuItem("CTGP开+DB开", "gpuPowerGroup", (s, e) => {
        gpuPower = "max";
        SetMaxGpuPower();
        SaveConfig("GpuPower");
      }, true));
      performanceControlMenu.DropDownItems.Add(CreateMenuItem("CTGP开+DB关", "gpuPowerGroup", (s, e) => {
        gpuPower = "med";
        SetMedGpuPower();
        SaveConfig("GpuPower");
      }, false));
      performanceControlMenu.DropDownItems.Add(CreateMenuItem("CTGP关+DB关", "gpuPowerGroup", (s, e) => {
        gpuPower = "min";
        SetMinGpuPower();
        SaveConfig("GpuPower");
      }, false));
      performanceControlMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      ToolStripMenuItem DBMenu = new ToolStripMenuItem("切换DB版本");
      DBMenu.DropDownItems.Add(CreateMenuItem("解锁版本", "DBGroup", (s, e) => {
        SetFanMode(0x31);
        SetMaxGpuPower();
        SetCpuPowerLimit((byte)CPULimitDB);
        DBVersion = 1;
        ChangeDBVersion(DBVersion);
        countDB = countDBInit;
        SaveConfig("DBVersion");
      }, false));
      DBMenu.DropDownItems.Add(CreateMenuItem("普通版本", "DBGroup", (s, e) => {
        DBVersion = 2;
        countDB = 0;
        //ChangeDBVersion(DBVersion);

        string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
        string command = $"pnputil /enable-device {deviceId}";
        ExecuteCommand(command);
        SaveConfig("DBVersion");
      }, true));
      performanceControlMenu.DropDownItems.Add(DBMenu);
      ToolStripMenuItem cpuPowerMenu = new ToolStripMenuItem("CPU功率");
      cpuPowerMenu.DropDownItems.Add(CreateMenuItem("最大", "cpuPowerGroup", (s, e) => {
        cpuPower = "max";
        SetCpuPowerLimit(254);
        SaveConfig("CpuPower");
      }, true));
      for (int power = 20; power <= 120; power += 10) {
        int currentPower = power;  // 创建一个局部变量，保存当前的 power 值
        cpuPowerMenu.DropDownItems.Add(CreateMenuItem(power + " W", "cpuPowerGroup", (s, e) => {
          cpuPower = currentPower + " W";
          SetCpuPowerLimit((byte)currentPower);
          SaveConfig("CpuPower");
        }, false));
      }
      performanceControlMenu.DropDownItems.Add(cpuPowerMenu);
      ToolStripMenuItem gpuClockMenu = new ToolStripMenuItem("GPU频率限制");
      gpuClockMenu.DropDownItems.Add(CreateMenuItem("还原", "gpuClockGroup", (s, e) => {
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
      trayIcon.ContextMenuStrip.Items.Add(performanceControlMenu);

      trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator()); // Separator between groups
      ToolStripMenuItem hardwareMonitorMenu = new ToolStripMenuItem("硬件监控");
      ToolStripMenuItem monitorGPUMenu = new ToolStripMenuItem("GPU");
      monitorGPUMenu.DropDownItems.Add(CreateMenuItem("开启GPU监控", "monitorGPUGroup", (s, e) => {
        monitorGPU = true;
        if (hasStopAuto)
          autoStopMonitorGPU = false;
        //重置自动开启标志
        hasStartAuto = false;
        autoStartMonitorGPU = true;
        libreComputer.IsGpuEnabled = true;
        SaveConfig("MonitorGPU");
      }, true));
      monitorGPUMenu.DropDownItems.Add(CreateMenuItem("关闭GPU监控", "monitorGPUGroup", (s, e) => {
        monitorGPU = false;
        if (hasStartAuto)
          autoStartMonitorGPU = false;
        //重置自动关闭标志
        hasStopAuto = false;
        autoStopMonitorGPU = true;
        libreComputer.IsGpuEnabled = false;
        SaveConfig("MonitorGPU");
      }, false));
      hardwareMonitorMenu.DropDownItems.Add(monitorGPUMenu);
      ToolStripMenuItem monitorFanMenu = new ToolStripMenuItem("风扇");
      monitorFanMenu.DropDownItems.Add(CreateMenuItem("开启风扇监控", "monitorFanGroup", (s, e) => {
        monitorFan = true;
        SaveConfig("MonitorFan");
      }, true));
      monitorFanMenu.DropDownItems.Add(CreateMenuItem("关闭风扇监控", "monitorFanGroup", (s, e) => {
        monitorFan = false;
        SaveConfig("MonitorFan");
      }, false));
      hardwareMonitorMenu.DropDownItems.Add(monitorFanMenu);
      trayIcon.ContextMenuStrip.Items.Add(hardwareMonitorMenu);
      ToolStripMenuItem floatingBarMenu = new ToolStripMenuItem("浮窗显示");
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("关闭浮窗", "floatingBarGroup", (s, e) => {
        floatingBar = "off";
        CloseFloatingForm();
        SaveConfig("FloatingBar");
      }, true));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("显示浮窗", "floatingBarGroup", (s, e) => {
        floatingBar = "on";
        ShowFloatingForm();
        SaveConfig("FloatingBar");
      }, false));
      floatingBarMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("24号", "floatingBarSizeGroup", (s, e) => {
        textSize = 24;
        UpdateFloatingText();
        SaveConfig("FloatingBarSize");
      }, false));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("36号", "floatingBarSizeGroup", (s, e) => {
        textSize = 36;
        UpdateFloatingText();
        SaveConfig("FloatingBarSize");
      }, false));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("48号", "floatingBarSizeGroup", (s, e) => {
        textSize = 48;
        UpdateFloatingText();
        SaveConfig("FloatingBarSize");
      }, true));
      floatingBarMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("左上角", "floatingBarLocGroup", (s, e) => {
        floatingBarLoc = "left";
        UpdateFloatingText();
        SaveConfig("FloatingBarLoc");
      }, true));
      floatingBarMenu.DropDownItems.Add(CreateMenuItem("右上角", "floatingBarLocGroup", (s, e) => {
        floatingBarLoc = "right";
        UpdateFloatingText();
        SaveConfig("FloatingBarLoc");
      }, false));
      trayIcon.ContextMenuStrip.Items.Add(floatingBarMenu);
      ToolStripMenuItem omenKeyMenu = new ToolStripMenuItem("Omen键");
      omenKeyMenu.DropDownItems.Add(CreateMenuItem("默认", "omenKeyGroup", (s, e) => {
        omenKey = "default";
        tooltipUpdateTimer.Enabled = false;
        OmenKeyOff();
        OmenKeyOn(omenKey);
        SaveConfig("OmenKey");
      }, true));
      omenKeyMenu.DropDownItems.Add(CreateMenuItem("切换浮窗显示", "omenKeyGroup", (s, e) => {
        omenKey = "custom";
        checkFloatingTimer.Enabled = true;
        OmenKeyOff();
        OmenKeyOn(omenKey);
        SaveConfig("OmenKey");
      }, false));
      omenKeyMenu.DropDownItems.Add(CreateMenuItem("取消绑定", "omenKeyGroup", (s, e) => {
        omenKey = "none";
        checkFloatingTimer.Enabled = false;
        OmenKeyOff();
        SaveConfig("OmenKey");
      }, false));
      trayIcon.ContextMenuStrip.Items.Add(omenKeyMenu);
      ToolStripMenuItem settingMenu = new ToolStripMenuItem("其他设置");
      ToolStripMenuItem customIconMenu = new ToolStripMenuItem("图标");
      customIconMenu.DropDownItems.Add(CreateMenuItem("原版", "customIconGroup", (s, e) => {
        customIcon = "original";
        trayIcon.Icon = Properties.Resources.smallfan;
        SaveConfig("CustomIcon");
      }, true));
      customIconMenu.DropDownItems.Add(CreateMenuItem("自定义图标", "customIconGroup", (s, e) => {
        customIcon = "custom";
        SetCustomIcon();
        SaveConfig("CustomIcon");
      }, false));
      customIconMenu.DropDownItems.Add(CreateMenuItem("动态图标", "customIconGroup", (s, e) => {
        customIcon = "dynamic";
        GenerateDynamicIcon((int)CPUTemp);
        SaveConfig("CustomIcon");
      }, false));
      settingMenu.DropDownItems.Add(customIconMenu);
      ToolStripMenuItem autoStartMenu = new ToolStripMenuItem("开机自启");
      autoStartMenu.DropDownItems.Add(CreateMenuItem("开启", "autoStartGroup", (s, e) => {
        autoStart = "on";
        AutoStartEnable();
        SaveConfig("AutoStart");
      }, false));
      autoStartMenu.DropDownItems.Add(CreateMenuItem("关闭", "autoStartGroup", (s, e) => {
        autoStart = "off";
        AutoStartDisable();
        SaveConfig("AutoStart");
      }, true));
      settingMenu.DropDownItems.Add(autoStartMenu);
      trayIcon.ContextMenuStrip.Items.Add(settingMenu);

      trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator()); // Separator between groups
      trayIcon.ContextMenuStrip.Items.Add(CreateMenuItem("退出", null, (s, e) => Exit(), false));

      // Initialize tooltip update timer
      tooltipUpdateTimer = new System.Timers.Timer(1000); // Set interval to 1 second
      tooltipUpdateTimer.Elapsed += (s, e) => UpdateTooltip();
      tooltipUpdateTimer.AutoReset = true; // Ensure the timer keeps running
      tooltipUpdateTimer.Start();
    }

    private static void RestoreCPUPower() {
      // 恢复CPU功耗设定
      if (cpuPower == "max") {
        SetCpuPowerLimit(254);
      } else if (cpuPower.Contains(" W")) {
        int value = int.Parse(cpuPower.Replace(" W", "").Trim());
        if (value > 10 && value <= 254) {
          SetCpuPowerLimit((byte)value);
        }
      }
    }

    private static void TrayIcon_MouseClick(object sender, MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) {
        //MainForm.Instance.Show();
        //MainForm.Instance.TopMost = true;
        //MainForm.Instance.TopMost = false;
      }
    }

    private static bool CheckCustomIcon() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string iconPath = Path.Combine(currentPath, "custom.ico");
      // 检查图标文件是否存在
      if (File.Exists(iconPath)) {
        return true;
      } else {
        MessageBox.Show("不存在自定义图标custom.ico", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
      }
    }

    private static void SetCustomIcon() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string iconPath = Path.Combine(currentPath, "custom.ico");
      // 检查图标文件是否存在
      if (File.Exists(iconPath)) {
        trayIcon.Icon = new Icon(iconPath);
      } else {
        MessageBox.Show("不存在自定义图标custom.ico", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    extern static bool DestroyIcon(IntPtr handle);
    private static void GenerateDynamicIcon(int number) {
      using (Bitmap bitmap = new Bitmap(128, 128)) {
        using (Graphics graphics = Graphics.FromImage(bitmap)) {
          graphics.Clear(Color.Transparent); // 清除背景
          graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; // 设置文本渲染模式为抗锯齿

          string text = number.ToString("00");

          Font font = new Font("Arial", 52, FontStyle.Bold);
          // 计算文本的大小
          SizeF textSize = graphics.MeasureString(text, font);

          // 计算绘制位置，使文本居中
          float x = (bitmap.Width - textSize.Width) / 2;
          float y = (bitmap.Height - textSize.Height) / 8; // 改为居中

          // 绘制居中的数字
          graphics.DrawString(text, font, Brushes.Tan, new PointF(x, y));

          IntPtr hIcon = bitmap.GetHicon(); // 获取 HICON 句柄
          trayIcon.Icon = Icon.FromHandle(hIcon); // 转换为Icon对象

          // 销毁图标句柄
          DestroyIcon(hIcon);
        }
      }
    }

    // 获取显卡数字代号
    private static string GetNVIDIAModel() {
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
          Console.WriteLine($"First GPU Model Code: {gpuModel}");
          return gpuModel;
        } else {
          Console.WriteLine("GPU model code not found.");
        }
      } else {
        Console.WriteLine($"Error executing command: {result.Error}");
      }

      return null;
    }

    // 设置显卡频率限制
    private static bool SetGPUClockLimit(int freq) {
      if (freq < 210) {
        ExecuteCommand("nvidia-smi --reset-gpu-clocks");
        return false;
      } else {
        ExecuteCommand("nvidia-smi --lock-gpu-clocks=0," + freq);
        return true;
      }
    }

    // 判断是否为最大显卡功耗并得到当前显卡功耗限制
    private static float GPUPowerLimits() {
      // state为“当前显卡功耗限制”或“显卡功耗限制已锁定”
      string output = ExecuteCommand("nvidia-smi -q -d POWER").Output;
      // 定义正则表达式模式以提取当前功率限制和最大功率限制
      string currentPowerLimitPattern = @"Current Power Limit\s+:\s+([\d.]+)\s+W";
      string maxPowerLimitPattern = @"Max Power Limit\s+:\s+([\d.]+)\s+W";

      // 查找当前功率限制和最大功率限制的匹配项
      var currentPowerLimitMatch = Regex.Match(output, currentPowerLimitPattern);
      var maxPowerLimitMatch = Regex.Match(output, maxPowerLimitPattern);

      // 检查匹配是否成功
      if (currentPowerLimitMatch.Success && maxPowerLimitMatch.Success) {
        // 提取值并转换为浮点数
        float currentPowerLimit = float.Parse(currentPowerLimitMatch.Groups[1].Value);
        float maxPowerLimit = float.Parse(maxPowerLimitMatch.Groups[1].Value);

        // 比较值并返回结果
        if (Math.Abs(currentPowerLimit - maxPowerLimit) < 1f) // 对于浮点数比较的容差
          return -currentPowerLimit;

        else {
          return currentPowerLimit;
        }
      } else {
        // 无法找到所有所需的功率限制
        Console.WriteLine("Error: Unable to find both power limits in the output.");
        return -2;
      }
    }

    private static bool CheckDBVersion(int kind) {
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

    private static void ChangeDBVersion(int kind) {
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
                Console.WriteLine("发现其他版本: " + driverVersion);
                namesToDelete.Add(publishedName);
              } else {
                hasVersion = true;
                Console.WriteLine("已经存在所需版本!");
              }
            }
          }
        }
      }

      if (!hasVersion) {
        ExecuteCommand($"pnputil /add-driver \"{driverFile}\" /install /force");
        Console.WriteLine("成功更改DB版本!");
      }

      if (namesToDelete.Count > 0) {
        Console.WriteLine("找到需要删除的驱动程序包:");
        foreach (var name in namesToDelete) {
          Console.WriteLine($"删除驱动程序包: {name}");
          ExecuteCommand($"pnputil /delete-driver \"{name}\" /uninstall /force");
        }
      } else {
        Console.WriteLine("没有需要删除的驱动程序包.");
      }

      // 清理临时文件
      //File.Delete(driversListFile);

      // 删除提取的nvpcf文件
      DeleteExtractedFiles(extractedInfFilePath);
      DeleteExtractedFiles(extractedSysFilePath);
      DeleteExtractedFiles(extractedCatFilePath);

      Console.WriteLine("操作完成.");
      Console.ReadLine();
    }

    private static void ExtractResourceToFile(string resourceName, string outputFilePath) {
      using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)) {
        if (resourceStream != null) {
          using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Create)) {
            resourceStream.CopyTo(fileStream);
          }
          Console.WriteLine($"资源文件已提取到: {outputFilePath}");
        } else {
          Console.WriteLine($"无法找到资源: {resourceName}");
        }
      }
    }

    private static void DeleteExtractedFiles(string filePath) {
      // 删除提取的文件
      if (File.Exists(filePath)) {
        File.Delete(filePath);
        Console.WriteLine($"删除临时文件:{filePath}");
      }
    }

    static ProcessResult ExecuteCommand(string command) {
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

    class ProcessResult {
      public int ExitCode { get; set; }
      public string Output { get; set; }
      public string Error { get; set; }
    }

    private static ToolStripMenuItem CreateMenuItem(string text, string group, EventHandler action, bool isChecked) {
      var item = new ToolStripMenuItem(text) {
        Tag = group,
        Checked = isChecked // Set initial checked state
      };
      item.Click += (s, e) => {
        if (item.Text == "解锁版本") {
          if (!powerOnline) {
            MessageBox.Show($"请连接交流电源", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DBVersion = 2;
            countDB = 0;
            SaveConfig("DBVersion");
            UpdateCheckedState("DBGroup", "普通版本");
            return;
          }
          if (!CheckDBVersion(1)) {
            DBVersion = 2;
            countDB = 0;
            SaveConfig("DBVersion");
            UpdateCheckedState("DBGroup", "普通版本");
            return;
          }
          //if(CPUPower > CPULimitDB + 1) {
          //  MessageBox.Show($"请在CPU低负载下解锁", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          //  DBVersion = 2;
          //  countDB = 0;
          //  SaveConfig("DBVersion");
          //  UpdateCheckedState("DBGroup", "普通版本");
          //  return;
          //}
        }
        if (item.Text == "普通版本" && !CheckDBVersion(2))
          return;
        if (item.Text == "自定义图标" && !CheckCustomIcon())
          return;

        action(s, e); // Perform the original action
        if (group != null) {
          UpdateCheckedState(group, null, item);
        }
      };
      return item;
    }

    private static void UpdateCheckedState(string group, string itemText = null, ToolStripMenuItem menuItemToCheck = null) {
      if (menuItemToCheck == null) {
        menuItemToCheck = FindMenuItem(trayIcon.ContextMenuStrip.Items, itemText);

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
    private static ToolStripMenuItem FindMenuItem(ToolStripItemCollection items, string itemText, int select = 2) {
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

    // 状态栏定时更新任务+硬件查询
    static void UpdateTooltip() {
      QueryHarware();
      fanSpeedNow = GetFanLevel();
      trayIcon.Text = monitorText();
      // Console.WriteLine("UpdateTooltip");

      UpdateFloatingText();

      if (customIcon == "dynamic")
        GenerateDynamicIcon((int)CPUTemp);

      // 启用再禁用DB驱动
      if (countDB > 0) {
        countDB--;
        if (countDB == 0) {
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /disable-device {deviceId}";
          ExecuteCommand(command);

          // 恢复模式设定
          if (fanMode.Contains("performance")) {
            SetFanMode(0x31);
          } else if (fanMode.Contains("default")) {
            SetFanMode(0x30);
          }

          if (CPUPower > CPULimitDB + 10) {
            if (powerOnline && GPUPowerLimits() >= 0) {
              MessageBox.Show($"请在CPU低负载下解锁", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              command = $"pnputil /enable-device {deviceId}";
              ExecuteCommand(command);
              DBVersion = 2;
              countDB = 0;
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", "普通版本");
            }
          } else {
            float powerLimits = GPUPowerLimits();
            if (powerOnline && powerLimits >= 0) {
              MessageBox.Show($"功耗异常，解锁失败，请重新尝试！\n当前显卡功耗限制为：{powerLimits:F2} W ！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              command = $"pnputil /enable-device {deviceId}";
              ExecuteCommand(command);
              DBVersion = 2;
              countDB = 0;
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", "普通版本");
            } else {
              if (autoStart == "off") {
                MessageBox.Show($"解锁成功！但当前未设置开机自启，解锁后若重启电脑会导致功耗异常，需要重新解锁！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              }
              //MessageBox.Show($"解锁成功！\n当前最大显卡功耗锁定为：{-powerLimits:F2} W ！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
          }

          // 恢复CPU功耗设定
          RestoreCPUPower();
        } else if (countDB == countDBInit - 1) {
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /enable-device {deviceId}";
          ExecuteCommand(command);
        }
      }

      // 从休眠中启动后恢复配置
      if (countRestore > 0) {
        countRestore--;
        if (countRestore == 0) {
          RestoreConfig();
        }
      }
    }

    static int countQuery = 0;
    static bool autoStartMonitorGPU = true, autoStopMonitorGPU = true;//是否自动根据情况开/关GPU温度监测以节约能源
    static bool hasStartAuto = false, hasStopAuto = false;//是否已经自动开/关过GPU温度监测，在手动开/关时重置
    private static void QueryHarware() {
      float openTempCPU = -300, libreTempCPU = -300, tempCPU = 50;
      float openPowerCPU = -1, librePowerCPU = -1;
      bool getGPU = false;//是否获取到GPU温度

      if (openLib) {
        foreach (OpenIHardware hardware in openComputer.Hardware) {
          hardware.Update();

          if (hardware.HardwareType == OpenHardwareType.CPU) {
            // Get CPU temperature sensor
            OpenISensor sensor = hardware.Sensors.FirstOrDefault(d => d.SensorType == OpenSensorType.Temperature && d.Name.Contains("Package"));
            OpenISensor powerSensor = hardware.Sensors.FirstOrDefault(d => d.SensorType == OpenSensorType.Power && d.Name.Contains("CPU Package"));
            if (sensor != null) {
              openTempCPU = (int)sensor.Value;
            }
            if (powerSensor != null) {
              openPowerCPU = (float)powerSensor.Value.GetValueOrDefault();
            }
          }
        }
      }
      //记录每个核心温度,计算平均温度
      List<float> tempArray = new List<float>();
      foreach (LibreIHardware hardware in libreComputer.Hardware) {
        if (hardware.HardwareType == LibreHardwareType.Cpu || hardware.HardwareType == LibreHardwareType.GpuNvidia || hardware.HardwareType == LibreHardwareType.GpuAmd) {
          hardware.Update();

          foreach (LibreISensor sensor in hardware.Sensors) {
            if (hardware.HardwareType == LibreHardwareType.Cpu) {

              if (sensor.Name.IndexOf("CPU Core") == 0 && sensor.Name.Contains("Distance to TjMax") == false && sensor.SensorType == LibreSensorType.Temperature) {
                tempArray.Add((int)sensor.Value.GetValueOrDefault());
              }
              if (sensor.Name == "CPU Package" && sensor.SensorType == LibreSensorType.Power) {
                librePowerCPU = sensor.Value.GetValueOrDefault();
              }
            } else if (monitorGPU && hardware.HardwareType == LibreHardwareType.GpuNvidia) {
              if (sensor.Name == "GPU Core" && sensor.SensorType == LibreSensorType.Temperature) {
                GPUTemp = (int)sensor.Value.GetValueOrDefault() * respondSpeed + GPUTemp * (1.0f - respondSpeed);
              }
              if (sensor.Name == "GPU Package" && sensor.SensorType == LibreSensorType.Power) {
                getGPU = true;
                if ((int)(sensor.Value.GetValueOrDefault() * 10) == 5900)
                  GPUPower = 0;
                else
                  GPUPower = sensor.Value.GetValueOrDefault();
              }
            }
          }
        }
      }

      if (tempArray.Count > 0) {
        libreTempCPU = tempArray.Average();
      }

      if (openLib && libreTempCPU > -299 && librePowerCPU >= 0) {
        openLib = false;
        openComputer.Close();
      }

      if (openTempCPU < -299) {
        if (libreTempCPU > -299)
          tempCPU = libreTempCPU;
      } else
        tempCPU = openTempCPU;
      CPUTemp = tempCPU * respondSpeed + CPUTemp * (1.0f - respondSpeed);

      if (openPowerCPU < 0) {
        if (librePowerCPU >= 0)
          CPUPower = librePowerCPU;
      } else
        CPUPower = openPowerCPU;

      //通过countQuery延时来确保温度正常读取
      if (countQuery <= 5 && monitorGPU)
        countQuery++;
      //自动关闭GPU监控
      if (countQuery > 5 && autoStopMonitorGPU && !isConnectedToNVIDIA && monitorGPU && ((GPUPower >= 0 && GPUPower <= 1.3) || !getGPU)) {
        GPUPower = 0;
        getGPU = false;
        hasStopAuto = true;
        countQuery = 0;
        monitorGPU = false;
        //重置自动开启标志
        hasStartAuto = false;
        autoStartMonitorGPU = true;
        libreComputer.IsGpuEnabled = false;
        UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");
        SaveConfig("MonitorGPU");

        // 设置通知的文本和标题
        trayIcon.BalloonTipTitle = "状态更改提示";
        trayIcon.BalloonTipText = "检测到显卡进入低功耗状态，OSH已停止监控GPU以节约能源。\n手动打开GPU监控后，本次将不再自动停止监控GPU。";
        trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
        trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      }
      //自动开启GPU监控
      if (autoStartMonitorGPU && isConnectedToNVIDIA && !monitorGPU) {
        GPUPower = 0;
        hasStartAuto = true;
        countQuery = 0;
        monitorGPU = true;
        //重置自动关闭标志
        hasStopAuto = false;
        autoStopMonitorGPU = true;
        libreComputer.IsGpuEnabled = true;
        UpdateCheckedState("monitorGPUGroup", "开启GPU监控");
        SaveConfig("MonitorGPU");

        // 设置通知的文本和标题
        trayIcon.BalloonTipTitle = "状态更改提示";
        trayIcon.BalloonTipText = "检测到显卡连接到显示器，OSH已开始监控GPU。\n手动关闭GPU监控后，本次将不再自动开始监控GPU。";
        trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
        trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      }

      // 似乎无法一次性关闭GPU监控及选项
      if (!monitorGPU && libreComputer.IsGpuEnabled) {
        libreComputer.IsGpuEnabled = false;
        UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");
      }

      //Console.WriteLine($"openCPU: {openTempCPU}℃, {openPowerCPU}W");
      //Console.WriteLine($"libreCPU: {libreTempCPU}℃, {librePowerCPU}W");
      //Console.WriteLine($"openGPU: {GPUTemp}℃, {GPUPower}W");

      //string tempUnit = "°C";
      //Console.WriteLine($"CPU: {CPU}{tempUnit}, GPU: {GPU}{tempUnit}, Max: {Math.Max(CPU, GPU + 10)}{tempUnit}");
    }

    private static void LoadDefaultFanConfig(string filePath, float silentCoef) {
      byte[] fanTableBytes = GetFanTable();

      int numberOfFans = fanTableBytes[0];
      if (numberOfFans != 2) {
        MessageBox.Show($"本机型不受支持！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        GenerateDefaultMapping(filePath);
        return;
      }
      int numberOfEntries = fanTableBytes[1];

      int originalMin = int.MaxValue;
      int originalMax = int.MinValue;

      // 首先找到 temperatureThreshold 的最小值和最大值
      for (int i = 0; i < numberOfEntries; i++) {
        int baseIndex = 2 + i * 3;
        int tempThreshold = fanTableBytes[baseIndex + 2];

        if (tempThreshold < originalMin) {
          originalMin = tempThreshold;
        }
        if (tempThreshold > originalMax) {
          originalMax = tempThreshold;
        }
      }

      // 目标温度范围为 50°C 到 97°C
      float targetMin = 50.0f;
      float targetMax = 97.0f;

      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        GPUTempFanMap.Clear();

        // 只保留最小和最大 temperatureThreshold 的映射
        for (int i = 0; i < numberOfEntries; i++) {
          int baseIndex = 2 + i * 3;
          int fan1Speed = fanTableBytes[baseIndex];
          int fan2Speed = fanTableBytes[baseIndex + 1];
          int originalTempThreshold = fanTableBytes[baseIndex + 2];

          // 将原始 temperatureThreshold 按比例映射到 50°C 到 97°C
          float cpuTempThreshold = targetMin +
              (originalTempThreshold - originalMin) * (targetMax - targetMin) / (originalMax - originalMin);
          float gpuTempThreshold = cpuTempThreshold - 10.0f;

          // 只保留最小和最大温度对应的行
          if (originalTempThreshold == originalMin || originalTempThreshold == originalMax) {
            if (!CPUTempFanMap.ContainsKey(cpuTempThreshold)) {
              CPUTempFanMap[cpuTempThreshold] = new List<int>();
            }
            CPUTempFanMap[cpuTempThreshold].Add((int)(fan1Speed * silentCoef) * 100);
            CPUTempFanMap[cpuTempThreshold].Add((int)(fan2Speed * silentCoef) * 100);

            if (!GPUTempFanMap.ContainsKey(gpuTempThreshold)) {
              GPUTempFanMap[gpuTempThreshold] = new List<int>();
            }
            GPUTempFanMap[gpuTempThreshold].Add((int)(fan1Speed * silentCoef) * 100);
            GPUTempFanMap[gpuTempThreshold].Add((int)(fan2Speed * silentCoef) * 100);
          }
        }
      }

      // 保存配置文件，只包含最小和最大温度对应的行
      var lines = new List<string> { "CPU,Fan1,Fan2,GPU,Fan1,Fan2" };
      lines.AddRange(CPUTempFanMap.Select(kvp =>
          $"{kvp.Key:F0},{kvp.Value[0]},{kvp.Value[1]},{kvp.Key - 10.0:F0},{kvp.Value[0]},{kvp.Value[1]}"));
      File.WriteAllLines(filePath, lines);
    }

    private static void LoadFanConfig(string filePath) {
      float silentCoef = 1;
      if (filePath == "silent.txt")
        silentCoef = 0.8f;
      string absoluteFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
      if (File.Exists(absoluteFilePath)) {
        lock (CPUTempFanMap) {
          CPUTempFanMap.Clear();
          GPUTempFanMap.Clear();
        }
        var lines = File.ReadAllLines(absoluteFilePath);

        for (int i = 1; i < lines.Length; i++) { // 跳过第一行标题
          var parts = lines[i].Split(',');
          if (parts.Length == 6) {
            // 解析CPU温度阈值、GPU温度阈值和两个风扇的速度
            if (float.TryParse(parts[0], out float cpuTemp) &&
                int.TryParse(parts[1], out int cpuFan1Speed) &&
                int.TryParse(parts[2], out int cpuFan2Speed) &&
                float.TryParse(parts[3], out float gpuTemp) &&
                int.TryParse(parts[4], out int gpuFan1Speed) &&
                int.TryParse(parts[5], out int gpuFan2Speed)) {

              // 将风扇速度以列表的形式存储在 CPUTempFanMap 和 GPUTempFanMap 中
              lock (CPUTempFanMap) {
                CPUTempFanMap[cpuTemp] = new List<int> { cpuFan1Speed, cpuFan2Speed };
                GPUTempFanMap[gpuTemp] = new List<int> { gpuFan1Speed, gpuFan2Speed };
              }
            }
          } else {
            Console.WriteLine($"{absoluteFilePath} error.");
            LoadDefaultFanConfig(absoluteFilePath, silentCoef);
            return;
          }
        }
        // Console.WriteLine($"{absoluteFilePath} fan config loaded successfully.");
      } else {
        Console.WriteLine($"{absoluteFilePath} not found.");
        LoadDefaultFanConfig(absoluteFilePath, silentCoef);
      }
    }

    // Generate default temperature-fan speed mapping
    private static void GenerateDefaultMapping(string filePath) {
      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        CPUTempFanMap[30] = new List<int> { 0, 0 };
        CPUTempFanMap[50] = new List<int> { 1600, 1900 };
        CPUTempFanMap[60] = new List<int> { 2000, 2300 };
        CPUTempFanMap[85] = new List<int> { 4000, 4300 };
        CPUTempFanMap[100] = new List<int> { 6100, 6400 };

        GPUTempFanMap.Clear();
        foreach (var kvp in CPUTempFanMap) {
          GPUTempFanMap[kvp.Key - 10] = new List<int> { kvp.Value[0], kvp.Value[1] };
        }
      }
      var lines = new List<string> { "CPU,Fan1,Fan2,GPU,Fan1,Fan2" };
      lines.AddRange(CPUTempFanMap.Select(kvp =>
          $"{kvp.Key:F0},{kvp.Value[0]},{kvp.Value[1]},{kvp.Key - 10:F0},{kvp.Value[0]},{kvp.Value[1]}"));
      File.WriteAllLines(filePath, lines);
    }

    // Get fan speed for CPU and GPU and return the maximum
    private static int GetFanSpeedForTemperature(int fanIndex) {
      if (CPUTempFanMap.Count == 0 || GPUTempFanMap.Count == 0) return 0;

      int cpuFanSpeed = GetFanSpeedForSpecificTemperature(CPUTemp, CPUTempFanMap, fanIndex);

      if (monitorGPU) {
        int gpuFanSpeed = GetFanSpeedForSpecificTemperature(GPUTemp, GPUTempFanMap, fanIndex);
        return Math.Max(cpuFanSpeed, gpuFanSpeed);
      }

      return cpuFanSpeed;
    }

    // Helper function to calculate fan speed for a specific temperature map
    private static int GetFanSpeedForSpecificTemperature(float temperature, Dictionary<float, List<int>> tempFanMap, int fanIndex) {
      var lowerBound = tempFanMap.Keys
                      .OrderBy(k => k)
                      .Where(t => t <= temperature)
                      .DefaultIfEmpty(tempFanMap.Keys.Min())
                      .LastOrDefault();

      var upperBound = tempFanMap.Keys
                      .OrderBy(k => k)
                      .Where(t => t > temperature)
                      .DefaultIfEmpty(tempFanMap.Keys.Max())
                      .FirstOrDefault();

      if (lowerBound == upperBound) {
        return tempFanMap[lowerBound][fanIndex];
      }

      int lowerSpeed = tempFanMap[lowerBound][fanIndex];
      int upperSpeed = tempFanMap[upperBound][fanIndex];
      float lowerTemp = lowerBound;
      float upperTemp = upperBound;

      float interpolatedSpeed = lowerSpeed + (upperSpeed - lowerSpeed) * (temperature - lowerTemp) / (upperTemp - lowerTemp);
      return (int)interpolatedSpeed;
    }

    static void SaveConfig(string configName = null) {
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            if (configName == null) {
              key.SetValue("FanTable", fanTable);
              key.SetValue("FanMode", fanMode);
              key.SetValue("FanControl", fanControl);
              key.SetValue("TempSensitivity", tempSensitivity);
              key.SetValue("CpuPower", cpuPower);
              key.SetValue("GpuPower", gpuPower);
              key.SetValue("GpuClock", gpuClock);
              key.SetValue("DBVersion", DBVersion);
              key.SetValue("AutoStart", autoStart);
              key.SetValue("AlreadyRead", alreadyRead);
              key.SetValue("CustomIcon", customIcon);
              key.SetValue("OmenKey", omenKey);
              key.SetValue("MonitorGPU", monitorGPU);
              key.SetValue("MonitorFan", monitorFan);
              key.SetValue("FloatingBarSize", textSize);
              key.SetValue("FloatingBarLoc", floatingBarLoc);
              key.SetValue("FloatingBar", floatingBar);
            } else {
              switch (configName) {
                case "FanTable":
                  key.SetValue("FanTable", fanTable);
                  break;
                case "FanMode":
                  key.SetValue("FanMode", fanMode);
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
                case "GpuPower":
                  key.SetValue("GpuPower", gpuPower);
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
                case "MonitorFan":
                  key.SetValue("MonitorFan", monitorFan);
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
              }
            }
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error saving configuration: {ex.Message}");
      }
    }

    static void RestoreConfig() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            fanTable = (string)key.GetValue("FanTable", "silent");
            if (fanTable.Contains("cool")) {
              LoadFanConfig("cool.txt");
              UpdateCheckedState("fanTableGroup", "降温模式");
            } else if (fanTable.Contains("silent")) {
              LoadFanConfig("silent.txt");
              UpdateCheckedState("fanTableGroup", "安静模式");
            }

            fanMode = (string)key.GetValue("FanMode", "performance");
            if (fanMode.Contains("performance")) {
              SetFanMode(0x31);
              UpdateCheckedState("fanModeGroup", "狂暴模式");
            } else if (fanMode.Contains("default")) {
              SetFanMode(0x30);
              UpdateCheckedState("fanModeGroup", "平衡模式");
            }

            fanControl = (string)key.GetValue("FanControl", "auto");
            if (fanControl == "auto") {
              SetMaxFanSpeedOff();
              fanControlTimer.Change(0, 1000);
              UpdateCheckedState("fanControlGroup", "自动");
            } else if (fanControl.Contains("max")) {
              SetMaxFanSpeedOn();
              fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
              UpdateCheckedState("fanControlGroup", "最大风扇");
            } else if (fanControl.Contains(" RPM")) {
              SetMaxFanSpeedOff();
              fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
              int rpmValue = int.Parse(fanControl.Replace(" RPM", "").Trim());
              SetFanLevel(rpmValue / 100, rpmValue / 100);
              UpdateCheckedState("fanControlGroup", fanControl);
            }

            tempSensitivity = (string)key.GetValue("TempSensitivity", "high");
            switch (tempSensitivity) {
              case "realtime":
                respondSpeed = 1;
                UpdateCheckedState("tempSensitivityGroup", "实时");
                break;
              case "high":
                respondSpeed = 0.4f;
                UpdateCheckedState("tempSensitivityGroup", "高");
                break;
              case "medium":
                respondSpeed = 0.1f;
                UpdateCheckedState("tempSensitivityGroup", "中");
                break;
              case "low":
                respondSpeed = 0.04f;
                UpdateCheckedState("tempSensitivityGroup", "低");
                break;
            }

            cpuPower = (string)key.GetValue("CpuPower", "max");
            if (cpuPower == "max") {
              SetCpuPowerLimit(254);
              UpdateCheckedState("cpuPowerGroup", "最大");
            } else if (cpuPower.Contains(" W")) {
              int value = int.Parse(cpuPower.Replace(" W", "").Trim());
              if (value > 10 && value <= 254) {
                SetCpuPowerLimit((byte)value);
                UpdateCheckedState("cpuPowerGroup", cpuPower);
              }
            }

            gpuPower = (string)key.GetValue("GpuPower", "max");
            switch (gpuPower) {
              case "max":
                SetMaxGpuPower();
                UpdateCheckedState("gpuPowerGroup", "CTGP开+DB开");
                break;
              case "med":
                SetMedGpuPower();
                UpdateCheckedState("gpuPowerGroup", "CTGP开+DB关");
                break;
              case "min":
                SetMinGpuPower();
                UpdateCheckedState("gpuPowerGroup", "CTGP关+DB关");
                break;
            }

            gpuClock = (int)key.GetValue("GpuClock", 0);
            if (SetGPUClockLimit(gpuClock)) {
              UpdateCheckedState("gpuClockGroup", gpuClock + " MHz");
            } else {
              UpdateCheckedState("gpuClockGroup", "还原");
            }

            DBVersion = (int)key.GetValue("DBVersion", 2);
            switch (DBVersion) {
              case 1:
                DBVersion = 1;
                SetFanMode(0x31);
                SetMaxGpuPower();
                SetCpuPowerLimit((byte)CPULimitDB);
                countDB = countDBInit;
                UpdateCheckedState("DBGroup", "解锁版本");
                break;
              case 2:
                string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
                string command = $"pnputil /enable-device {deviceId}";
                ExecuteCommand(command);
                DBVersion = 2;
                UpdateCheckedState("DBGroup", "普通版本");
                break;
            }

            autoStart = (string)key.GetValue("AutoStart", "off");
            switch (autoStart) {
              case "on":
                AutoStartEnable();
                UpdateCheckedState("autoStartGroup", "开启");
                break;
              case "off":
                UpdateCheckedState("autoStartGroup", "关闭");
                break;
            }

            alreadyRead = (int)key.GetValue("AlreadyRead", 0);

            customIcon = (string)key.GetValue("CustomIcon", "original");
            switch (customIcon) {
              case "original":
                trayIcon.Icon = Properties.Resources.smallfan;
                UpdateCheckedState("customIconGroup", "原版");
                break;
              case "custom":
                SetCustomIcon();
                UpdateCheckedState("customIconGroup", "自定义图标");
                break;
              case "dynamic":
                GenerateDynamicIcon((int)CPUTemp);
                UpdateCheckedState("customIconGroup", "动态图标");
                break;
            }

            omenKey = (string)key.GetValue("OmenKey", "default");
            switch (omenKey) {
              case "default":
                checkFloatingTimer.Enabled = false;
                OmenKeyOff();
                OmenKeyOn(omenKey);
                UpdateCheckedState("omenKeyGroup", "默认");
                break;
              case "custom":
                checkFloatingTimer.Enabled = true;
                OmenKeyOff();
                OmenKeyOn(omenKey);
                UpdateCheckedState("omenKeyGroup", "切换浮窗显示");
                break;
              case "none":
                checkFloatingTimer.Enabled = false;
                OmenKeyOff();
                UpdateCheckedState("omenKeyGroup", "取消绑定");
                break;
            }

            bool monitorGPUCache = Convert.ToBoolean(key.GetValue("MonitorGPU", true));
            if (monitorGPUCache == true) {
              libreComputer.IsGpuEnabled = true;
              monitorGPU = true;
              UpdateCheckedState("monitorGPUGroup", "开启GPU监控");
            } else {
              libreComputer.IsGpuEnabled = false;
              monitorGPU = false;
              UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");
            }

            bool monitorFanCache = Convert.ToBoolean(key.GetValue("MonitorFan", true));
            if (monitorFanCache == true) {
              monitorFan = true;
              UpdateCheckedState("monitorFanGroup", "开启风扇监控");
            } else {
              monitorFan = false;
              UpdateCheckedState("monitorFanGroup", "关闭风扇监控");
            }

            textSize = (int)key.GetValue("FloatingBarSize", 48);
            UpdateFloatingText();
            switch (textSize) {
              case 24:
                UpdateCheckedState("floatingBarSizeGroup", "24号");
                break;
              case 36:
                UpdateCheckedState("floatingBarSizeGroup", "36号");
                break;
              case 48:
                UpdateCheckedState("floatingBarSizeGroup", "48号");
                break;
            }

            floatingBarLoc = (string)key.GetValue("FloatingBarLoc", "left");
            UpdateFloatingText();
            if (floatingBarLoc == "left") {
              UpdateCheckedState("floatingBarLocGroup", "左上角");
            } else {
              UpdateCheckedState("floatingBarLocGroup", "右上角");
            }

            floatingBar = (string)key.GetValue("FloatingBar", "off");
            if (floatingBar == "on") {
              ShowFloatingForm();
              UpdateCheckedState("floatingBarGroup", "显示浮窗");
            } else {
              CloseFloatingForm();
              UpdateCheckedState("floatingBarGroup", "关闭浮窗");
            }
          } else {
            // 如果注册表键不存在，可以使用默认值
            LoadFanConfig("silent.txt");
            SetFanMode(0x31);
            SetMaxFanSpeedOff();
            SetMaxGpuPower();
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error restoring configuration: {ex.Message}");
      }
    }

    static void HandleFloatingBarToggle() {
      if (checkFloating) {
        checkFloating = false;
        try {
          using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
            if (key != null) {
              if ((string)key.GetValue("FloatingBar", "off") == "on") {
                floatingBar = "off";
                CloseFloatingForm();
                UpdateCheckedState("floatingBarGroup", "关闭浮窗");
              } else {
                floatingBar = "on";
                ShowFloatingForm();
                UpdateCheckedState("floatingBarGroup", "显示浮窗");
              }
              SaveConfig("FloatingBar");
            }
          }
        } catch (Exception ex) {
          Console.WriteLine($"Error restoring configuration: {ex.Message}");
        }
      }
    }

    static void getOmenKeyTask() {
      System.Threading.Tasks.Task.Run(() => {
        while (true) {
          using (var pipeServer = new NamedPipeServerStream("OmenSuperHubPipe", PipeDirection.In)) {
            pipeServer.WaitForConnection();
            using (var reader = new StreamReader(pipeServer)) {
              string message = reader.ReadToEnd();
              if (message.Contains("OmenKeyTriggered")) {
                if (!checkFloating)
                  checkFloating = true;
              }
            }
          }
        }
      });
    }

    // 显示浮窗
    public static void ShowFloatingForm() {
      if (floatingForm == null || floatingForm.IsDisposed) {
        floatingForm = new FloatingForm(monitorText(), textSize, floatingBarLoc);
        floatingForm.Show();
      } else {
        lock (floatingForm) {
          floatingForm.BringToFront();
        }
      }
    }

    // 关闭浮窗
    public static void CloseFloatingForm() {
      if (floatingForm != null && !floatingForm.IsDisposed) {
        lock (floatingForm) {
          floatingForm.Close();
          floatingForm.Dispose();
          floatingForm = null;
        }
      }
    }

    // 更新浮窗的文字内容
    public static void UpdateFloatingText() {
      if (floatingForm != null && !floatingForm.IsDisposed) {
        lock (floatingForm) {
          floatingForm.TopMost = true;
          floatingForm.SetText(monitorText(), textSize, floatingBarLoc);
        }
      }
    }

    //生成监控信息
    public static string monitorText() {
      string str = $"CPU: {CPUTemp:F1}°C, {CPUPower:F1}W";
      if (monitorGPU)
        str += $"\nGPU: {GPUTemp:F1}°C, {GPUPower:F1}W";
      if (monitorFan)
        str += $"\nFan:  {fanSpeedNow[0] * 100}, {fanSpeedNow[1] * 100}";
      return str;
    }

    private static void Exit() {
      if (omenKey == "custom") {
        OmenKeyOff();
      }
      tooltipUpdateTimer.Stop(); // 停止定时器

      openComputer.Close();
      libreComputer.Close();
      Application.Exit();
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Exception ex = (Exception)e.ExceptionObject;
      LogError(ex);
    }

    static void Application_ThreadException(object sender, ThreadExceptionEventArgs e) {
      Exception ex = e.Exception;
      LogError(ex);
    }

    static void LogError(Exception ex) {
      // Write exception details to a log file or other logging mechanism
      string absoluteFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
      File.AppendAllText(absoluteFilePath, DateTime.Now + ": " + ex.ToString() + Environment.NewLine);
      MessageBox.Show("An unexpected error occurred. Please check the log file for details.");
    }
  }
}
