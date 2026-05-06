using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using static OmenSuperHub.OmenHardware;
using LibreComputer = LibreHardwareMonitor.Hardware.Computer;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreIHardware = LibreHardwareMonitor.Hardware.IHardware;
using LibreISensor = LibreHardwareMonitor.Hardware.ISensor;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace OmenSuperHub {
  static class Program {
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static float CPUTemp = 50;
    static float GPUTemp = 40;
    static float CPUPower = 0;
    static float GPUPower = 0;
    static int DBVersion = 2, countDB = 0, countDBInit = 5, tryTimes = 0, CPULimitDB = 25;
    static int textSize = 48;
    static int countRestore = 0, gpuClock = 0;
    static int alreadyRead = 0, alreadyReadCode = 1000;
    static string fanTable = "cool", fanMode = "performance", fanControl = "auto", tempSensitivity = "high", tppPower = "null", iccMax = "null", acLoadline = "null", cpuPower = "null", gpuPower = "max", autoStart = "off", customIcon = "original", floatingBar = "off", floatingBarLoc = "left", omenKey = "default", dataLocalize = "off";
    static volatile bool monitorFan = true;
    static bool skipCheckedUpdate = false; // action 内拦截时置 true，阻止 CreateMenuItem 覆盖勾选
    static bool monitorCPU = true, monitorGPU = true, isConnectedToNVIDIA = true, prevIsConnectedToNVIDIA = true, powerOnline = true, checkFloating = false, isTwoBytePL4 = false;
    static bool hasAmdGpu; // 启动时一次性检测，硬件状态不会改变
    static string monitorRefreshRate = "low"; // 刷新频率：low=1s, high=0.25s
    static List<int> fanSpeedNow = new List<int> { 20, 23 };
    static float respondSpeed = 0.4f;

    static float rawTempCPU = 50f;
    static float rawPowerCPU = 0f;
    static float rawTempGPU = 40f;
    static float rawPowerGPU = 0f;
    static bool rawGotGPU = false;
    static volatile bool tempReady = false;   // 子进程首次输出有效温度后置 true
    static volatile bool cpuTempReady = false; // CPU 温度已初始化给平滑值，允许参与风扇控制
    static volatile bool gpuTempReady = false; // GPU 温度已初始化给平滑值，允许参与风扇控制
    static volatile bool hwMonitorStopping = false; // 主动停止时置 true，阻止 Exited 自动重启
    static Process hwMonitorProcess;
    static StreamWriter hwMonitorIn;

    // Cache last written values to avoid unnecessary disk reads/writes
    static string lastCpuText = null;
    static string lastGpuText = null;
    static string lastFanText = null;
    static string tempDisplayMode = "smoothed"; // 温度显示方式：smoothed=平滑值, raw=原始值
    static int? platformMaxFanSpeed = null; // 平台最大转速（RPM），由LoadDefaultFanConfig获取后缓存
    static Dictionary<float, List<int>> CPUTempFanMap = new Dictionary<float, List<int>>();
    static Dictionary<float, List<int>> GPUTempFanMap = new Dictionary<float, List<int>>();
    static System.Threading.Timer fanControlTimer;
    static System.Timers.Timer tooltipUpdateTimer; // Timer for updating tooltip
    static System.Windows.Forms.Timer checkFloatingTimer, optimiseTimer;
    static NotifyIcon trayIcon;
    static FloatingForm floatingForm;
    static PlatformSettings platformSettings;
    static ToolStripMenuItem irSensorMenu;
    static ToolStripMenuItem ambientSensorMenu;
    static ToolStripMenuItem pchSensorMenu;
    static ToolStripMenuItem vrSensorMenu;
    static ToolStripMenuItem adapterPowerMenu;

    static bool isSysInfoMenuOpen = false;
    static int? maxCPUTemp = null;
    static int maxGPUTemp = 87;

    [STAThread]
    static void Main(string[] args) {
      if (args.Length > 0 && args[0] == "--hwmonitor") {
        RunHardwareMonitor();
        return;
      }

      // ── 静默重启模式：由任务计划登录触发器调用
      if (args.Length > 0 && args[0] == "--relaunch") {
        // 终止其他已有实例
        var currentId = Process.GetCurrentProcess().Id;
        foreach (var proc in Process.GetProcessesByName("OmenSuperHub")) {
          if (proc.Id == currentId) continue;
          try { proc.Kill(); proc.WaitForExit(3000); } catch { }
        }
        // 启动新实例（不带参数，走正常流程）
        Process.Start(new ProcessStartInfo {
          FileName = Application.ExecutablePath,
          UseShellExecute = true
        });
        return; // 本实例立即退出，不做任何初始化
      }

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

        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        hasAmdGpu = HasAmdGpu();
        if (!hasAmdGpu)
          ExtractAndPreloadNativeDll("NvidiaApi.dll");

        powerOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        monitorQuery();

        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        isTwoBytePL4 = IsTwoBytePL4Supported();

        // Initialize tray icon
        platformSettings = LoadPlatformSettingsFromDll();
        InitMaxTemp();
        InitPlatformMaxFanSpeed();
        InitTrayIcon();

        optimiseTimer = new System.Windows.Forms.Timer();
        optimiseTimer.Interval = 30000;
        optimiseTimer.Tick += (s, e) => optimiseSchedule();
        optimiseTimer.Start();

        // 立即执行一次
        optimiseSchedule();

        // Main loop to query CPU and GPU temperature every second
        fanControlTimer = new System.Threading.Timer((e) => {
          // 自动模式下，首次获取到真实温度数据前不进行转速控制
          if (!tempReady && fanControl == "auto") return;
          int fanSpeed1 = GetFanSpeedForTemperature(0) / 100;
          int fanSpeed2 = GetFanSpeedForTemperature(1) / 100;
          if (monitorFan) {
            int s0, s1;
            lock (fanSpeedNow) { s0 = fanSpeedNow[0]; s1 = fanSpeedNow[1]; }
            if (fanSpeed1 != s0 || fanSpeed2 != s1) {
              SetFanLevel(fanSpeed1, fanSpeed2);
            }
          } else
            SetFanLevel(fanSpeed1, fanSpeed2);
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
        //PrintSystemDesignData();
        //Console.WriteLine($"GetSystemID: {GetSystemID()}");

        //MessageBox.Show($"消息测试", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        //trayIcon.BalloonTipTitle = "消息测试";
        //trayIcon.BalloonTipText = $"消息测试";
        //trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
        //trayIcon.ShowBalloonTip(3000);

        Application.Run();
      }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvidiaAPI_SYS_UIControl_Delegate(bool on);

    public static int LaunchDDS() {
      IntPtr hModule = GetModuleHandle("NvidiaApi.dll");
      if (hModule == IntPtr.Zero) return -1;
      IntPtr proc = GetProcAddress(hModule, "NvidiaAPI_SYS_UIControl");
      if (proc == IntPtr.Zero) return -1;
      var fn = (NvidiaAPI_SYS_UIControl_Delegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(NvidiaAPI_SYS_UIControl_Delegate));
      return fn(true);
    }

    public static bool HasAmdGpu() {
      try {
        using (var searcher = new System.Management.ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Name FROM Win32_VideoController")) {
          foreach (var obj in searcher.Get()) {
            string name = obj["Name"]?.ToString() ?? "";
            if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
              return true;
          }
        }
      } catch { }
      return false;
    }

    public static class AmdGpuSwitcher {
      // 本地枚举定义（与 DLL 中的值完全对应）
      public enum LocalADLSmartMuxEnableState {
        ADL_MUXCONTROL_DISABLED = 0,
        ADL_MUXCONTROL_ENABLED = 1
      }

      private static object GetSAGHelper() {
        // 获取 SmartAccessGraphicsHelp 类型
        Assembly commonAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "HP.Omen.Core.Common");
        Type sagHelpType = commonAssembly.GetType(
            "HP.Omen.Core.Common.Utilities.SmartAccessGraphicsHelp.SmartAccessGraphicsHelp");

        // 获取 SAGHelper 静态属性（单例）
        PropertyInfo sagHelperProp = sagHelpType.GetProperty("SAGHelper",
            BindingFlags.Public | BindingFlags.Static);
        return sagHelperProp.GetValue(null); // 静态属性，get
      }

      public static bool IsSupported() {
        if (!hasAmdGpu)      // ★ 先检查硬件，避免触发 ADL
          return false;

        object helper = GetSAGHelper();
        if (helper == null) return false;

        PropertyInfo supportProp = helper.GetType().GetProperty(
            "SmartAccessGraphicsSupport",
            BindingFlags.Public | BindingFlags.Instance);
        return (bool)supportProp.GetValue(helper);
      }

      public static LocalADLSmartMuxEnableState GetMode() {
        object helper = GetSAGHelper();
        if (helper == null) return LocalADLSmartMuxEnableState.ADL_MUXCONTROL_DISABLED;

        PropertyInfo modeProp = helper.GetType().GetProperty(
            "SmartAccessGraphicsMode",
            BindingFlags.Public | BindingFlags.Instance);
        int modeValue = (int)modeProp.GetValue(helper);
        return (LocalADLSmartMuxEnableState)modeValue;
      }

      public static void SetMode(LocalADLSmartMuxEnableState mode) {
        object helper = GetSAGHelper();
        if (helper == null) return;

        // 获取 DLL 中的枚举类型
        Assembly commonAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "HP.Omen.Core.Common");
        Type stateEnum = commonAssembly.GetType(
            "HP.Omen.Core.Common.Utilities.SmartAccessGraphicsHelp.ADLSmartMuxEnableState");

        // 将本地枚举值转换为 DLL 枚举对象
        object modeValue = Enum.ToObject(stateEnum, (int)mode);

        MethodInfo setMethod = helper.GetType().GetMethod(
            "SetSmartAccessGraphicsMode",
            BindingFlags.Public | BindingFlags.Instance);
        setMethod.Invoke(helper, new[] { modeValue });
      }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private static void ExtractAndPreloadNativeDll(string dllName) {
      var currentAssembly = Assembly.GetExecutingAssembly();

      // 在嵌入资源中查找（资源名通常是 "命名空间.文件名"）
      var resourceName = currentAssembly
          .GetManifestResourceNames()
          .FirstOrDefault(r => r.EndsWith(dllName, StringComparison.OrdinalIgnoreCase));

      if (resourceName == null) {
        throw new FileNotFoundException($"嵌入资源中找不到 {dllName}");
      }

      // 释放到程序目录（或 Temp 目录）
      string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);

      if (!File.Exists(outputPath)) {
        using (var stream = currentAssembly.GetManifestResourceStream(resourceName))
        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write)) {
          stream.CopyTo(fs);
        }
      }

      // 提前加载，之后 DllImport 会自动复用
      IntPtr handle = LoadLibrary(outputPath);
      if (handle == IntPtr.Zero) {
        throw new Exception($"LoadLibrary 失败，错误码: {Marshal.GetLastWin32Error()}");
      }
    }

    private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args) {
      var assemblyName = new AssemblyName(args.Name).Name + ".dll";

      var currentAssembly = Assembly.GetExecutingAssembly();

      var resourceName = currentAssembly
          .GetManifestResourceNames()
          .FirstOrDefault(r => r.EndsWith(assemblyName, StringComparison.OrdinalIgnoreCase));

      if (resourceName == null)
        return null;

      using (var stream = currentAssembly.GetManifestResourceStream(resourceName)) {
        if (stream == null)
          return null;

        var buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);

        return Assembly.Load(buffer);
      }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct DISPLAY_DEVICE {
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
    enum DisplayDeviceStateFlags : int {
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
    static extern bool EnumDisplayDevices(
        string lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    // 判断独显未工作条件
    static void monitorQuery() {
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

    [HandleProcessCorruptedStateExceptions]
    static void RunHardwareMonitor() {
      var computer = new LibreComputer() { IsCpuEnabled = true, IsGpuEnabled = true };

      try {
        computer.Open();
      } catch (Exception ex) {
        Console.WriteLine("CRASH: Open failed - " + ex.Message);
        Environment.Exit(1);
      }

      int sleepMs = 1000;

      var readThread = new Thread(() => {
        while (true) {
          string line = Console.ReadLine();
          if (line == null) Environment.Exit(0);
          if (line == "GPU:ON") computer.IsGpuEnabled = true;
          if (line == "GPU:OFF") computer.IsGpuEnabled = false;
          if (line == "CPU:ON") computer.IsCpuEnabled = true;
          if (line == "CPU:OFF") computer.IsCpuEnabled = false;
          if (line.StartsWith("INTERVAL:") && int.TryParse(line.Substring(9), out int ms) && ms > 0)
            sleepMs = ms;
        }
      });
      readThread.IsBackground = true;
      readThread.Start();

      float tCpu = 50, pCpu = 0, tGpu = 40, pGpu = 0;

      while (true) {
        bool gGpu = false;
        try {
          foreach (LibreIHardware hw in computer.Hardware) {
            if (hw.HardwareType != LibreHardwareType.Cpu && hw.HardwareType != LibreHardwareType.GpuNvidia && hw.HardwareType != LibreHardwareType.GpuAmd) continue;

            // 如果底层驱动对象因为驱动更新导致句柄无效，Update会抛出异常。
            // 此时我们直接让子进程退出，父进程会重新启动一个新的子进程来进行初始化。
            try {
              hw.Update();
            } catch (Exception ex) {
              Console.WriteLine("CRASH: Update failed - " + ex.Message);
              Environment.Exit(1);
            }

            foreach (LibreISensor sensor in hw.Sensors) {
              try {
                if (hw.HardwareType == LibreHardwareType.Cpu) {
                  if (sensor.SensorType == LibreSensorType.Temperature && (sensor.Name.Contains("Package") || sensor.Name.Contains("Tctl/Tdie")))
                    tCpu = sensor.Value.GetValueOrDefault();
                  if (sensor.SensorType == LibreSensorType.Power && sensor.Name.Contains("Package"))
                    pCpu = sensor.Value.GetValueOrDefault();
                } else if (hw.HardwareType == LibreHardwareType.GpuNvidia || hw.HardwareType == LibreHardwareType.GpuAmd) {
                  if (sensor.SensorType == LibreSensorType.Temperature && sensor.Name == "GPU Core")
                    tGpu = sensor.Value.GetValueOrDefault();
                  if (sensor.SensorType == LibreSensorType.Power && sensor.Name == "GPU Package") {
                    gGpu = true;
                    pGpu = sensor.Value.GetValueOrDefault();
                  }
                }
              } catch { }
            }
          }
          Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2};{1:F2};{2:F2};{3:F2};{4}", tCpu, pCpu, tGpu, pGpu, gGpu ? 1 : 0));
        } catch (Exception ex) {
          Console.WriteLine("CRASH: " + ex.Message);
          Environment.Exit(1);
        }
        Thread.Sleep(sleepMs);
      }
    }

    static void StartHardwareMonitor() {
      if (hwMonitorProcess != null && !hwMonitorProcess.HasExited) return;

      hwMonitorProcess = new Process {
        StartInfo = new ProcessStartInfo {
          FileName = Application.ExecutablePath,
          Arguments = "--hwmonitor",
          UseShellExecute = false,
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden
        }
      };

      hwMonitorProcess.OutputDataReceived += (s, e) => {
        if (string.IsNullOrEmpty(e.Data)) return;
        //Debug.WriteLine("[HWMonitor OUT] " + e.Data); // 将子进程输出重定向到VS的输出窗口
        if (e.Data.StartsWith("CRASH:")) return;
        var parts = e.Data.Split(';');
        if (parts.Length == 5) {
          if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float tc)) rawTempCPU = tc;
          if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pc) && pc < 9999) rawPowerCPU = pc;
          if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float tg)) rawTempGPU = tg;
          if (float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float pg)) rawPowerGPU = pg;
          rawGotGPU = parts[4] == "1";
          // 首次收到数据时，初始化对应传感器的平滑温度
          if (!cpuTempReady) {
            smoothedCPUTemp = rawTempCPU;
            cpuTempReady = true;
          }
          if (!gpuTempReady) {
            smoothedGPUTemp = rawTempGPU;
            gpuTempReady = true;
          }
          if (!tempReady) {
            tempReady = true;
          }
        }
      };

      hwMonitorProcess.ErrorDataReceived += (s, e) => {
        if (string.IsNullOrEmpty(e.Data)) return;
        Debug.WriteLine("[HWMonitor ERR] " + e.Data);
      };

      hwMonitorProcess.EnableRaisingEvents = true;
      hwMonitorProcess.Exited += (s, e) => {
        if (hwMonitorStopping) {
          hwMonitorStopping = false;
          return;
        }
        Debug.WriteLine("[HWMonitor] 进程退出，准备重启...");
        System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ => {
          try { StartHardwareMonitor(); } catch { }
        });
      };

      try {
        hwMonitorProcess.Start();
        hwMonitorIn = hwMonitorProcess.StandardInput;
        hwMonitorProcess.BeginOutputReadLine();
        hwMonitorProcess.BeginErrorReadLine(); // 必须读取错误流避免死锁
        SetGpuMonitorState(monitorGPU);
        SetCpuMonitorState(monitorCPU);
        SetMonitorInterval(monitorRefreshRate == "high" ? 250 : 1000);
      } catch (Exception) { }
    }

    static void SetGpuMonitorState(bool enable) {
      if (hwMonitorIn != null && hwMonitorProcess != null && !hwMonitorProcess.HasExited) {
        try { hwMonitorIn.WriteLine(enable ? "GPU:ON" : "GPU:OFF"); } catch { }
      }
    }

    static void SetCpuMonitorState(bool enable) {
      if (hwMonitorIn != null && hwMonitorProcess != null && !hwMonitorProcess.HasExited) {
        try { hwMonitorIn.WriteLine(enable ? "CPU:ON" : "CPU:OFF"); } catch { }
      }
    }

    static void SetMonitorInterval(int ms) {
      if (hwMonitorIn != null && hwMonitorProcess != null && !hwMonitorProcess.HasExited) {
        try { hwMonitorIn.WriteLine($"INTERVAL:{ms}"); } catch { }
      }
    }

    static void StopHardwareMonitor() {
      if (hwMonitorProcess != null && !hwMonitorProcess.HasExited) {
        hwMonitorStopping = true;
        try { hwMonitorProcess.Kill(); } catch { hwMonitorStopping = false; }
      }
    }

    static int flagStart = 0;
    static void optimiseSchedule() {
      // 延时等待风扇恢复响应
      if (flagStart < 5) {
        flagStart++;
        if (fanControl.Contains("max")) {
          SetMaxFanSpeedOn();
        } else if (fanControl.Contains(" RPM")) {
          SetMaxFanSpeedOff();
          int rpmValue = int.Parse(fanControl.Replace(" RPM", "").Trim());
          SetFanLevel(rpmValue / 100, rpmValue / 100);
        }
      }

      //定时通信避免功耗锁定
      if (GetFanCount(out bool ocp, out bool otp)) {
        if (ocp || otp) {
          Console.WriteLine($"BIOS 保护状态 - 过流: {ocp}, 过温: {otp}");
        }
      } else {
        Console.WriteLine("无法读取 BIOS 保护状态");
      }
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
        Console.WriteLine("任务一已创建：系统启动时运行。");

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
        Console.WriteLine("任务二已创建：用户登录时重启。");
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
            Console.WriteLine($"任务 {taskName} 已删除。");
          } else {
            Console.WriteLine($"任务 {taskName} 不存在，无需删除。");
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
    static void InitTrayIcon() {
      trayIcon = new NotifyIcon() {
        // Icon = SystemIcons.Application,
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
              UpdateCheckedState("CustomIcon", "原版");
            }
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error restoring configuration: {ex.Message}");
      }

      switch (customIcon) {
        case "original":
          trayIcon.Icon = Properties.Resources.smallfan;
          break;
        case "custom":
          SetCustomIcon();
          break;
        case "dynamic":
          UpdateDynamicIcon();
          break;
      }

      ToolStripMenuItem sysInfoMenu = new ToolStripMenuItem("本机信息");
      sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"主板产品号: {GetSystemID()}") { Enabled = false });
      if (platformMaxFanSpeed.HasValue) {
        sysInfoMenu.DropDownItems.Add(new ToolStripMenuItem($"CPU温度墙: {(maxCPUTemp.HasValue ? maxCPUTemp.Value.ToString() : "未知")}°C") { Enabled = false });
      }
      irSensorMenu = new ToolStripMenuItem("IR传感器: --°C") { Enabled = false };
      ambientSensorMenu = new ToolStripMenuItem("环境传感器: --°C") { Enabled = false };
      pchSensorMenu = new ToolStripMenuItem("PCH传感器: --°C") { Enabled = false };
      vrSensorMenu = new ToolStripMenuItem("VR传感器: --°C") { Enabled = false };
      adapterPowerMenu = new ToolStripMenuItem("适配器功率: --W") { Enabled = false };
      sysInfoMenu.DropDownItems.Add(irSensorMenu);
      sysInfoMenu.DropDownItems.Add(ambientSensorMenu);
      sysInfoMenu.DropDownItems.Add(pchSensorMenu);
      sysInfoMenu.DropDownItems.Add(vrSensorMenu);
      sysInfoMenu.DropDownItems.Add(adapterPowerMenu);

      // 订阅 DropDownOpening 和 DropDownClosed 事件来控制是否更新信息
      sysInfoMenu.DropDownOpening += (s, e) => { isSysInfoMenuOpen = true; };
      sysInfoMenu.DropDownClosed += (s, e) => { isSysInfoMenuOpen = false; };

      trayIcon.ContextMenuStrip.Items.Add(sysInfoMenu);

      trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
      ToolStripMenuItem fanConfigMenu = new ToolStripMenuItem("风扇配置");
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("安静模式", "fanTableGroup", (s, e) => {
        fanTable = "silent";
        LoadFanConfig("silent.txt");
        SaveConfig("FanTable");
      }, false));
      fanConfigMenu.DropDownItems.Add(CreateMenuItem("降温模式", "fanTableGroup", (s, e) => {
        fanTable = "cool";
        LoadFanConfig("cool.txt");
        SaveConfig("FanTable");
      }, true));
      fanConfigMenu.DropDownItems.Add(new ToolStripSeparator());
      ToolStripMenuItem respondSpeedMenu = new ToolStripMenuItem("风扇响应速度");
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem("实时", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "realtime";
        respondSpeed = 1;
        SaveConfig("TempSensitivity");
      }, false));
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem("高", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "high";
        respondSpeed = 0.4f;
        SaveConfig("TempSensitivity");
      }, true));
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem("中", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "medium";
        respondSpeed = 0.1f;
        SaveConfig("TempSensitivity");
      }, false));
      respondSpeedMenu.DropDownItems.Add(CreateMenuItem("低", "tempSensitivityGroup", (s, e) => {
        tempSensitivity = "low";
        respondSpeed = 0.04f;
        SaveConfig("TempSensitivity");
      }, false));
      fanConfigMenu.DropDownItems.Add(respondSpeedMenu);
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
      if (platformSettings != null && platformSettings.UnleashedModeSupport) {
        performanceControlMenu.DropDownItems.Add(CreateMenuItem("大师模式", "fanModeGroup", (s, e) => {
          fanMode = "extreme";
          SetFanMode(0x04);
          SaveConfig("FanMode");
          // 恢复CPU功耗设定
          RestoreCPUPower();
        }, false));
      }
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
      // 图形模式
      ToolStripMenuItem graphicsModeControlMenu = new ToolStripMenuItem("图形模式");
      if (hasAmdGpu) {
        var initAmdMode = AmdGpuSwitcher.GetMode();
        bool amdIsDiscrete = initAmdMode == AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_ENABLED;
        graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("独显直连", "graphicsModeGroup", (s, e) => {
          AmdGpuSwitcher.SetMode(AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_ENABLED);
        }, amdIsDiscrete));
        graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("混合输出", "graphicsModeGroup", (s, e) => {
          AmdGpuSwitcher.SetMode(AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_DISABLED);
        }, !amdIsDiscrete));
      } else {
        var initNvMode = GetGfxMode();
        graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("NV动态切换", "graphicsModeGroup", (s, e) => {
          LaunchDDS();
        }, initNvMode == GraphicsMode.Hybrid));
        graphicsModeControlMenu.DropDownItems.Add(CreateMenuItem("独显直连", "graphicsModeGroup", (s, e) => {
          LaunchDDS();
        }, initNvMode == GraphicsMode.Discrete));
      }
      performanceControlMenu.DropDownItems.Add(graphicsModeControlMenu);
      graphicsModeControlMenu.DropDownOpening += (s, e) => {
        if (hasAmdGpu) {
          var amdMode = AmdGpuSwitcher.GetMode();
          bool isDisc = amdMode == AmdGpuSwitcher.LocalADLSmartMuxEnableState.ADL_MUXCONTROL_ENABLED;
          UpdateCheckedState("graphicsModeGroup", isDisc ? "独显直连" : "混合输出");
        } else {
          var nvMode = GetGfxMode();
          string chk;
          switch (nvMode) {
            case GraphicsMode.Discrete: chk = "独显直连"; break;
            default: chk = "NV动态切换"; break;
          }
          UpdateCheckedState("graphicsModeGroup", chk);
        }
      };
      performanceControlMenu.DropDownItems.Add(new ToolStripSeparator()); // Separator between groups
      ToolStripMenuItem DBMenu = new ToolStripMenuItem("切换DB版本");
      DBMenu.DropDownItems.Add(CreateMenuItem("解锁版本", "DBGroup", (s, e) => {
        string gpuModel = GetNVIDIAModel();
        if (gpuModel != null) {
          var match = Regex.Match(gpuModel, @"^\d+");
          if (match.Success && int.TryParse(match.Value, out int modelNum)) {
            if (modelNum >= 5000) {
              MessageBox.Show($"不支持英伟达50系及以后的显卡解锁DB！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              DBVersion = 2;
              countDB = 0;
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", "普通版本");
              return;
            }
          }
        }
        if (platformSettings != null && platformSettings.UnleashedModeSupport)
          SetFanMode(0x04);
        else
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
      if (platformSettings != null && platformSettings.TppSupport) {
        ToolStripMenuItem tppMenu = new ToolStripMenuItem("Tpp");
        tppMenu.DropDownItems.Add(CreateMenuItem("不设置", "tppPowerGroup", (s, e) => {
          tppPower = "null";
          SaveConfig("TppPower");
        }, true));
        tppMenu.DropDownItems.Add(CreateMenuItem("最大", "tppPowerGroup", (s, e) => {
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
        performanceControlMenu.DropDownItems.Add(tppMenu);
      }
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
        ToolStripMenuItem iccMaxMenu = new ToolStripMenuItem("IccMax");
        iccMaxMenu.DropDownItems.Add(CreateMenuItem("不设置", "iccMaxGroup", (s, e) => {
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
        ToolStripMenuItem acLoadLineMenu = new ToolStripMenuItem("AC Load Line");
        acLoadLineMenu.DropDownItems.Add(CreateMenuItem("不设置", "acLoadLineGroup", (s, e) => {
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
              trayIcon.BalloonTipTitle = "AC Load Line 提示";
              trayIcon.BalloonTipText = $"当前设备支持的最大 AC Load Line 为 {180 - 10 * maxSupportedLevel}，将尝试设置 {180 - 10 * currentLevel}。";
              trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
              trayIcon.ShowBalloonTip(3000);
            }
            SetLoadLine(currentLevel);
            SaveConfig("AcLoadLine");
          }, false));
        }
        performanceControlMenu.DropDownItems.Add(acLoadLineMenu);
      }
      ToolStripMenuItem cpuPowerMenu = new ToolStripMenuItem("CPU功率");
      cpuPowerMenu.DropDownItems.Add(CreateMenuItem("不设置", "cpuPowerGroup", (s, e) => {
        cpuPower = "null";
        SaveConfig("CpuPower");
      }, true));
      cpuPowerMenu.DropDownItems.Add(CreateMenuItem("最大", "cpuPowerGroup", (s, e) => {
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
      ToolStripMenuItem gpuPowerMenu = new ToolStripMenuItem("GPU功率");
      gpuPowerMenu.DropDownItems.Add(CreateMenuItem("最大", "gpuPowerGroup", (s, e) => {
        gpuPower = "max";
        SetMaxGpuPower();
        SaveConfig("GpuPower");
      }, true));
      gpuPowerMenu.DropDownItems.Add(CreateMenuItem("中等", "gpuPowerGroup", (s, e) => {
        gpuPower = "med";
        SetMedGpuPower();
        SaveConfig("GpuPower");
      }, false));
      gpuPowerMenu.DropDownItems.Add(CreateMenuItem("最小", "gpuPowerGroup", (s, e) => {
        gpuPower = "min";
        SetMinGpuPower();
        SaveConfig("GpuPower");
      }, false));
      performanceControlMenu.DropDownItems.Add(gpuPowerMenu);
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
      ToolStripMenuItem monitorCPUMenu = new ToolStripMenuItem("CPU");
      monitorCPUMenu.DropDownItems.Add(CreateMenuItem("开启CPU监控", "monitorCPUGroup", (s, e) => {
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
      monitorCPUMenu.DropDownItems.Add(CreateMenuItem("关闭CPU监控", "monitorCPUGroup", (s, e) => {
        // 自动转速模式下禁止彻底关闭监控
        if (!monitorGPU && fanControl == "auto") {
          MessageBox.Show("当前为自动转速模式，若要关闭监控需切换为其他转速控制模式。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          UpdateCheckedState("monitorCPUGroup", monitorCPU ? "开启CPU监控" : "关闭CPU监控");
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
      ToolStripMenuItem monitorGPUMenu = new ToolStripMenuItem("GPU");
      monitorGPUMenu.DropDownItems.Add(CreateMenuItem("开启GPU监控", "monitorGPUGroup", (s, e) => {
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
      monitorGPUMenu.DropDownItems.Add(CreateMenuItem("关闭GPU监控", "monitorGPUGroup", (s, e) => {
        // 自动转速模式下禁止彻底关闭监控
        if (!monitorCPU && fanControl == "auto") {
          MessageBox.Show("当前为自动转速模式，若要关闭监控需切换为其他转速控制模式。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          UpdateCheckedState("monitorGPUGroup", monitorGPU ? "开启GPU监控" : "关闭GPU监控");
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
      ToolStripMenuItem monitorRefreshMenu = new ToolStripMenuItem("刷新频率");
      monitorRefreshMenu.DropDownItems.Add(CreateMenuItem("高", "monitorRefreshGroup", (s, e) => {
        monitorRefreshRate = "high";
        tooltipUpdateTimer.Interval = 250;
        SetMonitorInterval(250);
        SaveConfig("MonitorRefreshRate");
      }, false));
      monitorRefreshMenu.DropDownItems.Add(CreateMenuItem("低", "monitorRefreshGroup", (s, e) => {
        monitorRefreshRate = "low";
        tooltipUpdateTimer.Interval = 1000;
        SetMonitorInterval(1000);
        SaveConfig("MonitorRefreshRate");
      }, true));
      hardwareMonitorMenu.DropDownItems.Add(monitorRefreshMenu);
      ToolStripMenuItem tempDisplayMenu = new ToolStripMenuItem("温度显示方式");
      tempDisplayMenu.DropDownItems.Add(CreateMenuItem("平滑值", "tempDisplayGroup", (s, e) => {
        tempDisplayMode = "smoothed";
        SaveConfig("TempDisplayMode");
      }, true));
      tempDisplayMenu.DropDownItems.Add(CreateMenuItem("原始值", "tempDisplayGroup", (s, e) => {
        tempDisplayMode = "raw";
        SaveConfig("TempDisplayMode");
      }, false));
      hardwareMonitorMenu.DropDownItems.Add(tempDisplayMenu);
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
        UpdateDynamicIcon();
        SaveConfig("CustomIcon");
      }, false));
      settingMenu.DropDownItems.Add(customIconMenu);
      ToolStripMenuItem dataLocalizeMenu = new ToolStripMenuItem("数据本地化");
      dataLocalizeMenu.DropDownItems.Add(CreateMenuItem("开启", "dataLocalizeGroup", (s, e) => {
        dataLocalize = "on";
        SaveConfig("DataLocalize");
      }, false));
      dataLocalizeMenu.DropDownItems.Add(CreateMenuItem("关闭", "dataLocalizeGroup", (s, e) => {
        dataLocalize = "off";
        SaveConfig("DataLocalize");
      }, true));
      settingMenu.DropDownItems.Add(dataLocalizeMenu);
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
      trayIcon.ContextMenuStrip.Items.Add(CreateMenuItem("帮助", null, (s, e) => {
        HelpForm.Instance.Show();
      }, false));
      trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator()); // Separator between groups
      trayIcon.ContextMenuStrip.Items.Add(CreateMenuItem("退出", null, (s, e) => Exit(), false));

      // Initialize tooltip update timer
      tooltipUpdateTimer = new System.Timers.Timer(1000); // Set interval to 1 second (low, default)
      tooltipUpdateTimer.Elapsed += (s, e) => UpdateTooltip();
      tooltipUpdateTimer.AutoReset = true; // Ensure the timer keeps running
      tooltipUpdateTimer.Start();
    }

    static void RestoreCPUPower() {
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

    static void TrayIcon_MouseClick(object sender, MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) {
        //MainForm.Instance.Show();
        //MainForm.Instance.TopMost = true;
        //MainForm.Instance.TopMost = false;
      }
    }

    static bool CheckCustomIcon() {
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

    static void SetCustomIcon() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string iconPath = Path.Combine(currentPath, "custom.ico");
      // 检查图标文件是否存在
      if (File.Exists(iconPath)) {
        trayIcon.Icon = new Icon(iconPath);
      } else {
        MessageBox.Show("不存在自定义图标custom.ico", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    // 根据当前监控状态决定动态图标显示内容：
    // CPU监控开 → CPU温度；CPU关GPU开 → GPU温度；均关 → 原版图标（不改 customIcon 设置）
    static void UpdateDynamicIcon() {
      if (customIcon != "dynamic") return;
      if (monitorCPU) {
        GenerateDynamicIcon((int)CPUTemp);
      } else if (monitorGPU) {
        GenerateDynamicIcon((int)GPUTemp);
      } else {
        trayIcon.Icon = Properties.Resources.smallfan;
      }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    extern static bool DestroyIcon(IntPtr handle);
    static void GenerateDynamicIcon(int number) {
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
    static string GetNVIDIAModel() {
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
    static bool SetGPUClockLimit(int freq) {
      if (freq < 210) {
        ExecuteCommand("nvidia-smi --reset-gpu-clocks");
        return false;
      } else {
        ExecuteCommand("nvidia-smi --lock-gpu-clocks=0," + freq);
        return true;
      }
    }

    // 判断是否为最大显卡功耗并得到当前显卡功耗限制
    // 若限制超过1W则输出当前显卡功耗限制，否则输出为负数
    static float GPUPowerLimits() {
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

    static bool CheckDBVersion(int kind) {
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

    static void ChangeDBVersion(int kind) {
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
    }

    static void ExtractResourceToFile(string resourceName, string outputFilePath) {
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

    static void DeleteExtractedFiles(string filePath) {
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

    static ToolStripMenuItem CreateMenuItem(string text, string group, EventHandler action, bool isChecked) {
      var item = new ToolStripMenuItem(text) {
        Tag = group,
        Checked = isChecked // Set initial checked state
      };
      item.Click += (s, e) => {
        if (item.Text == "解锁版本") {
          string gpuModel = GetNVIDIAModel();
          if (gpuModel != null) {
            var match = Regex.Match(gpuModel, @"^\d+");
            if (match.Success && int.TryParse(match.Value, out int modelNum)) {
              if (modelNum >= 5000) {
                MessageBox.Show($"不支持英伟达50系及以后的显卡解锁DB！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DBVersion = 2;
                countDB = 0;
                SaveConfig("DBVersion");
                UpdateCheckedState("DBGroup", "普通版本");
                return;
              }
            }
          }

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

    // 状态栏定时更新任务+硬件查询+DB解锁
    static void UpdateTooltip() {
      try {
        QueryHardware();
      } catch (Exception ex) {
        Console.WriteLine($"[UpdateTooltip] QueryHardware 异常: {ex.Message}");
      }

      if (monitorFan)
        fanSpeedNow = GetFanLevel();
      trayIcon.Text = monitorText();
      // Console.WriteLine("UpdateTooltip");

      // 同步数据到本地txt
      SyncDataToTxt();

      UpdateFloatingText();

      if (customIcon == "dynamic")
        UpdateDynamicIcon();

      // Debug/Release模式下可能不支持在非UI线程直接修改MenuItem.Text，因此使用Invoke
      // 同时只有当 SysInfo 菜单处于展开状态时，才去进行耗时的查询和更新操作，以节省资源
      ToolStrip parentStrip = irSensorMenu?.GetCurrentParent();
      if (isSysInfoMenuOpen && parentStrip != null) {
        if (parentStrip.InvokeRequired) {
          parentStrip.Invoke(new System.Action(() => {
            irSensorMenu.Text = $"IR传感器: {GetSensorTemperature(0)}°C";
            ambientSensorMenu.Text = $"环境传感器: {GetSensorTemperature(1)}°C";
            pchSensorMenu.Text = $"PCH传感器: {GetSensorTemperature(2)}°C";
            vrSensorMenu.Text = $"VR传感器: {GetSensorTemperature(3)}°C";
            adapterPowerMenu.Text = $"适配器功率: {GetAdapterPower()}W";
          }));
        } else {
          irSensorMenu.Text = $"IR传感器: {GetSensorTemperature(0)}°C";
          ambientSensorMenu.Text = $"环境传感器: {GetSensorTemperature(1)}°C";
          pchSensorMenu.Text = $"PCH传感器: {GetSensorTemperature(2)}°C";
          vrSensorMenu.Text = $"VR传感器: {GetSensorTemperature(3)}°C";
          adapterPowerMenu.Text = $"适配器功率: {GetAdapterPower()}W";
        }
      }

      // 启用再禁用DB驱动
      if (countDB > 0) {
        countDB--;
        if (countDB == 0) {
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /disable-device {deviceId}";
          ExecuteCommand(command);

          float powerLimits = GPUPowerLimits();
          // 检查显卡当前功耗限制，离电时当作解锁成功
          if (powerOnline && powerLimits >= 0) {
            tryTimes++;
            // 失败时重试一次
            if (tryTimes == 2) {
              tryTimes = 0;
              if (CPUPower > CPULimitDB + 10)
                MessageBox.Show($"请在CPU低负载下解锁", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              else
                MessageBox.Show($"功耗异常，解锁失败，请重新尝试！\n当前显卡功耗限制为：{powerLimits:F2} W ！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              command = $"pnputil /enable-device {deviceId}";
              ExecuteCommand(command);
              DBVersion = 2;
              countDB = 0;
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", "普通版本");
            } else {
              if (platformSettings != null && platformSettings.UnleashedModeSupport)
                SetFanMode(0x04);
              else
                SetFanMode(0x31);
              SetMaxGpuPower();
              SetCpuPowerLimit((byte)CPULimitDB);
              countDB = countDBInit;
            }
          } else {
            tryTimes = 0;
            if (autoStart == "off") {
              MessageBox.Show($"解锁成功！但当前未设置开机自启，解锁后若重启电脑会导致功耗异常，需要重新解锁！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            //MessageBox.Show($"解锁成功！\n当前最大显卡功耗锁定为：{-powerLimits:F2} W ！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
          }
          if (tryTimes == 0) {
            // 恢复模式设定
            if (fanMode.Contains("performance")) {
              SetFanMode(0x31);
            } else if (fanMode.Contains("default")) {
              SetFanMode(0x30);
            } else if (fanMode.Contains("unleashed")) {
              SetFanMode(0x04);
            }

            // 恢复CPU功耗设定
            RestoreCPUPower();
          }
        } else if (countDB == countDBInit - 1) {
          // 启用DB驱动
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

    static void SyncDataToTxt() {
      if (dataLocalize != "on") return;
      System.Threading.Tasks.Task.Run(() => {
        try {
          // 获取程序根目录
          string basePath = AppDomain.CurrentDomain.BaseDirectory;

          // 将浮点数转换为整数并转换为文本
          string cpuText = ((int)Math.Round(CPUTemp)).ToString();
          string gpuText = ((int)Math.Round(GPUTemp)).ToString();
          string fanText;
          // brief lock to avoid potential race on the list
          lock (fanSpeedNow) {
            fanText = (fanSpeedNow[0] * 100).ToString();
          }

          // 仅当与上次内存中保存的值不同时才写入磁盘，避免不必要的 I/O
          try {
            if (lastCpuText == null || lastCpuText != cpuText) {
              File.WriteAllText(Path.Combine(basePath, "cpu_temp.txt"), cpuText);
              lastCpuText = cpuText;
            }
          } catch (Exception ex) {
            Console.WriteLine($"Sync error when writing cpu_temp.txt: {ex.Message}");
          }

          try {
            if (lastGpuText == null || lastGpuText != gpuText) {
              File.WriteAllText(Path.Combine(basePath, "gpu_temp.txt"), gpuText);
              lastGpuText = gpuText;
            }
          } catch (Exception ex) {
            Console.WriteLine($"Sync error when writing gpu_temp.txt: {ex.Message}");
          }

          try {
            if (lastFanText == null || lastFanText != fanText) {
              File.WriteAllText(Path.Combine(basePath, "fan_rpm.txt"), fanText);
              lastFanText = fanText;
            }
          } catch (Exception ex) {
            Console.WriteLine($"Sync error when writing fan_rpm.txt: {ex.Message}");
          }

        } catch (Exception ex) {
          // 忽略文件被占用的偶发错误，或者在这里记录日志
          Console.WriteLine("Sync error: " + ex.Message);
        }
      });
    }

    // 硬件传感器查询
    private static int _isQuerying = 0; // 防重入标志，支持 Interlocked 原子操作
    static int countQuery = 0;
    static bool autoStartMonitorGPU = true, autoStopMonitorGPU = true;//是否自动根据情况开/关GPU温度监测以节约能源
    static bool hasStartAuto = false, hasStopAuto = false;//是否已经自动开/关过GPU温度监测，在手动开/关时重置
    // 用于风扇查表的平滑温度（受高中低档影响）
    static float smoothedCPUTemp = 50f;
    static float smoothedGPUTemp = 40f;

    static void QueryHardware() {
      // 防止定时器重入：上次查询未完成时直接跳过本次
      if (Interlocked.CompareExchange(ref _isQuerying, 1, 0) != 0)
        return;

      float tempCPU = rawTempCPU;
      bool getGPU = false;

      if (monitorCPU && cpuTempReady) {
        CPUPower = rawPowerCPU;
      }
      if (monitorGPU) {
        getGPU = rawGotGPU;
        if (getGPU) {
          if ((int)(rawPowerGPU * 10) == 5900)
            GPUPower = 0;
          else
            GPUPower = rawPowerGPU;
        }
      }

      // 每次调用都直接平滑（不再做1s均值），风扇响应速度由respondSpeed本身控制
      if (monitorCPU && cpuTempReady) {
        smoothedCPUTemp = tempCPU * respondSpeed + smoothedCPUTemp * (1.0f - respondSpeed);
      }
      if (monitorGPU && gpuTempReady) {
        smoothedGPUTemp = rawTempGPU * respondSpeed + smoothedGPUTemp * (1.0f - respondSpeed);
      }

      // 根据显示方式决定展示原始值或平滑值
      if (monitorCPU && cpuTempReady)
        CPUTemp = (tempDisplayMode == "raw") ? tempCPU : smoothedCPUTemp;
      if (monitorGPU && gpuTempReady)
        GPUTemp = (tempDisplayMode == "raw") ? rawTempGPU : smoothedGPUTemp;

      int currentMaxCPUTemp = maxCPUTemp ?? 97;
      if (platformMaxFanSpeed.HasValue && smoothedCPUTemp > currentMaxCPUTemp - 5 && fanControl.Contains(" RPM")) {
        // 检查是否满足转速低于平台最大转速90%的条件
        bool fanSpeedCondition = true;
        if (platformMaxFanSpeed.Value > 0) {
          int currentFanSpeed;
          lock (fanSpeedNow) { currentFanSpeed = fanSpeedNow[0] * 100; }
          fanSpeedCondition = currentFanSpeed < platformMaxFanSpeed.Value * 0.9;
        }

        if (fanSpeedCondition) {
          // 先切换为降温模式（cool配置）
          fanTable = "cool";
          LoadFanConfig("cool.txt");
          UpdateCheckedState("fanTableGroup", "降温模式");
          SaveConfig("FanTable");

          // 再切换为自动风扇控制
          fanControl = "auto";
          SetMaxFanSpeedOff();
          fanControlTimer.Change(0, 1000);
          UpdateCheckedState("fanControlGroup", "自动");
          SaveConfig("FanControl");

          trayIcon.BalloonTipTitle = "温度过高警告";
          trayIcon.BalloonTipText = $"检测到CPU温度高于{currentMaxCPUTemp - 5}℃ ({smoothedCPUTemp:F1}℃)，且风扇处于固定转速状态，OSH已自动切换为降温模式并将风扇控制切换为自动模式。";
          trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
          trayIcon.ShowBalloonTip(3000);
        }
      }

      //通过countQuery延时来确保温度正常读取
      if (countQuery <= 5 && monitorGPU)
        countQuery++;
      //自动关闭GPU监控
      if (countQuery > 5 && autoStopMonitorGPU && !isConnectedToNVIDIA && monitorGPU && ((GPUPower >= 0 && GPUPower <= 1.3) || !getGPU)) {
        GPUPower = 0;
        rawPowerGPU = 0f;
        getGPU = false;
        hasStopAuto = true;
        countQuery = 0;
        monitorGPU = false;
        gpuTempReady = false; // 关闭后温度不再有效
        //重置自动开启标志
        hasStartAuto = false;
        autoStartMonitorGPU = true;
        SetGpuMonitorState(false);
        UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");
        SaveConfig("MonitorGPU");

        // 设置通知的文本和标题
        trayIcon.BalloonTipTitle = "状态更改提示";
        trayIcon.BalloonTipText = "检测到显卡进入低功耗状态，OSH已停止监控GPU以节约能源。\n手动打开GPU监控后，本次将不再自动停止监控GPU。";
        trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
        trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      }
      //自动开启GPU监控：需从"未连接显示器"切换为"已连接"时才触发
      if (autoStartMonitorGPU && isConnectedToNVIDIA && !prevIsConnectedToNVIDIA && !monitorGPU) {
        GPUPower = 0;
        rawPowerGPU = 0f;
        hasStartAuto = true;
        countQuery = 0;
        monitorGPU = true;
        gpuTempReady = false; // 等待获取到温度后再参与风扇控制
        //重置自动关闭标志
        hasStopAuto = false;
        autoStopMonitorGPU = true;
        SetGpuMonitorState(true);
        UpdateCheckedState("monitorGPUGroup", "开启GPU监控");
        SaveConfig("MonitorGPU");

        // 设置通知的文本和标题
        trayIcon.BalloonTipTitle = "状态更改提示";
        trayIcon.BalloonTipText = "检测到显卡连接到显示器，OSH已开始监控GPU。\n手动关闭GPU监控后，本次将不再自动开始监控GPU。";
        trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
        trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      }

      // 似乎无法一次性关闭GPU监控及选项
      if (!monitorGPU) {
        SetGpuMonitorState(false);
        UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");
      }

      prevIsConnectedToNVIDIA = isConnectedToNVIDIA;

      // 释放防重入标志
      Interlocked.Exchange(ref _isQuerying, 0);
    }

    static void InitMaxTemp() {
      maxCPUTemp = null;
      maxGPUTemp = 87;
      if (platformSettings != null) {
        int throttle = platformSettings.temperatureThrottlingPerformance;
        if (throttle > 0) {
          maxCPUTemp = throttle;
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
      if (maxCPUTemp.HasValue) {
        tempDelta = maxCPUTemp.Value - maxGPUTemp;
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
          gpuTempList = new List<int> { 30 - delta, 60 - delta, 87 - delta, maxGPUTemp };
          gpuSpeedList = new List<int> { 0, maxRpm / 3, maxRpm * 2 / 3, maxRpm - maxRpm / 10 };
        } else {
          // cool: cpu45/gpu35 → maxRpm/4, (maxTemp-5)℃ → maxRpm
          cpuTempList = new List<int> { 45, maxCpu - 5, maxCpu };
          cpuSpeedList = new List<int> { maxRpm / 4, maxRpm, maxRpm + maxRpm / 10 };
          gpuTempList = new List<int> { 45 - delta, maxGPUTemp - 5, maxGPUTemp };
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
        Console.WriteLine($"{absoluteFilePath} not found.");
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
          Console.WriteLine($"{absoluteFilePath} invalid new format, regenerating.");
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
          Console.WriteLine($"{absoluteFilePath} parse error, regenerating.");
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

    // Helper function to calculate fan speed for a specific temperature map
    static int GetFanSpeedForSpecificTemperature(float temperature, Dictionary<float, List<int>> tempFanMap, int fanIndex) {
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
              key.SetValue("MonitorCPU", monitorCPU);
              key.SetValue("MonitorFan", monitorFan);
              key.SetValue("MonitorRefreshRate", monitorRefreshRate);
              key.SetValue("TempDisplayMode", tempDisplayMode);
              key.SetValue("FloatingBarLoc", floatingBarLoc);
              key.SetValue("FloatingBar", floatingBar);
              key.SetValue("DataLocalize", dataLocalize);
              key.SetValue("TppPower", tppPower);
              //key.SetValue("PL4Power", powerLimit4);
              key.SetValue("IccMax", iccMax);
              key.SetValue("AcLoadLine", acLoadline);
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
            fanTable = (string)key.GetValue("FanTable", "cool");
            if (fanTable.Contains("cool")) {
              LoadFanConfig("cool.txt");
              UpdateCheckedState("fanTableGroup", "降温模式");
            } else if (fanTable.Contains("silent")) {
              LoadFanConfig("silent.txt");
              UpdateCheckedState("fanTableGroup", "安静模式");
            }

            fanMode = (string)key.GetValue("FanMode", "performance");
            if (fanMode.Contains("extreme")) {
              SetFanMode(0x04);
              UpdateCheckedState("fanModeGroup", "大师模式");
            } else if (fanMode.Contains("performance")) {
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

            tppPower = (string)key.GetValue("TppPower", "null");
            // TPP 设置单独延迟 1s 应用，避免启动时与其他设置冲突
            {
              string tppPowerSnapshot = tppPower;
              System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
                if (tppPowerSnapshot == "null") {
                  UpdateCheckedState("tppPowerGroup", "不设置");
                } else if (tppPowerSnapshot == "max") {
                  SetConcurrentTdp(254);
                  UpdateCheckedState("tppPowerGroup", "最大");
                } else if (tppPowerSnapshot.Contains(" W")) {
                  int value = int.Parse(tppPowerSnapshot.Replace(" W", "").Trim());
                  if (value >= 20 && value <= 240) {
                    SetConcurrentTdp((byte)value);
                    UpdateCheckedState("tppPowerGroup", tppPowerSnapshot);
                  }
                }
              });
            }

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

            iccMax = (string)key.GetValue("IccMax", "null");
            if (iccMax == "null") {
              UpdateCheckedState("iccMaxGroup", "不设置");
            } else if (iccMax.Contains(" A")) {
              if (int.TryParse(iccMax.Replace(" A", "").Trim(), out int ampVal) && ampVal >= 150 && ampVal <= 350) {
                SetIccMaxByWmi((decimal)ampVal);
                UpdateCheckedState("iccMaxGroup", iccMax);
              }
            }

            acLoadline = (string)key.GetValue("AcLoadLine", "null");
            if (acLoadline == "null") {
              UpdateCheckedState("acLoadLineGroup", "不设置");
            } else if (int.TryParse(acLoadline, out int llVal) && llVal >= 1) {
              SetLoadLine(llVal);
              string llDisplay = (180 - 10 * llVal).ToString();
              UpdateCheckedState("acLoadLineGroup", llDisplay);
            }

            cpuPower = (string)key.GetValue("CpuPower", "null");
            if (cpuPower == "null") {
              UpdateCheckedState("cpuPowerGroup", "不设置");
            } else if (cpuPower == "max") {
              SetCpuPowerLimit(254);
              UpdateCheckedState("cpuPowerGroup", "最大");
            } else if (cpuPower.Contains(" W")) {
              int value = int.Parse(cpuPower.Replace(" W", "").Trim());
              if (value >= 5 && value <= 254) {
                SetCpuPowerLimit((byte)value);
                UpdateCheckedState("cpuPowerGroup", cpuPower);
              }
            }

            gpuPower = (string)key.GetValue("GpuPower", "max");
            switch (gpuPower) {
              case "max":
                SetMaxGpuPower();
                UpdateCheckedState("gpuPowerGroup", "最大");
                break;
              case "med":
                SetMedGpuPower();
                UpdateCheckedState("gpuPowerGroup", "中等");
                break;
              case "min":
                SetMinGpuPower();
                UpdateCheckedState("gpuPowerGroup", "最小");
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
                string gpuModel = GetNVIDIAModel();
                if (gpuModel != null) {
                  var match = Regex.Match(gpuModel, @"^\d+");
                  if (match.Success && int.TryParse(match.Value, out int modelNum)) {
                    if (modelNum >= 5000) {
                      DBVersion = 2;
                      string deviceId50 = "\"ACPI\\NVDA0820\\NPCF\"";
                      string command50 = $"pnputil /enable-device {deviceId50}";
                      ExecuteCommand(command50);
                      UpdateCheckedState("DBGroup", "普通版本");
                      break;
                    }
                  }
                }
                DBVersion = 1;
                if (platformSettings != null && platformSettings.UnleashedModeSupport)
                  SetFanMode(0x04);
                else
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
                UpdateDynamicIcon();
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

            bool monitorCPUCache = Convert.ToBoolean(key.GetValue("MonitorCPU", true));
            monitorCPU = monitorCPUCache;
            UpdateCheckedState("monitorCPUGroup", monitorCPU ? "开启CPU监控" : "关闭CPU监控");

            bool monitorGPUCache = Convert.ToBoolean(key.GetValue("MonitorGPU", true));
            monitorGPU = monitorGPUCache;
            UpdateCheckedState("monitorGPUGroup", monitorGPU ? "开启GPU监控" : "关闭GPU监控");

            // 仅当至少一个监控开启时才启动 libre 进程；进程启动后再发送 CPU/GPU 状态
            if (monitorCPU || monitorGPU) {
              StartHardwareMonitor(); // 内部已调用 SetCpuMonitorState / SetGpuMonitorState
            }

            bool monitorFanCache = Convert.ToBoolean(key.GetValue("MonitorFan", true));
            if (monitorFanCache == true) {
              monitorFan = true;
              UpdateCheckedState("monitorFanGroup", "开启风扇监控");
            } else {
              monitorFan = false;
              UpdateCheckedState("monitorFanGroup", "关闭风扇监控");
            }

            monitorRefreshRate = (string)key.GetValue("MonitorRefreshRate", "low");
            switch (monitorRefreshRate) {
              case "high":
                tooltipUpdateTimer.Interval = 250;
                SetMonitorInterval(250);
                UpdateCheckedState("monitorRefreshGroup", "高");
                break;
              case "low":
              default:
                monitorRefreshRate = "low";
                tooltipUpdateTimer.Interval = 1000;
                SetMonitorInterval(1000);
                UpdateCheckedState("monitorRefreshGroup", "低");
                break;
            }

            tempDisplayMode = (string)key.GetValue("TempDisplayMode", "smoothed");
            if (tempDisplayMode == "raw") {
              UpdateCheckedState("tempDisplayGroup", "原始值");
            } else {
              tempDisplayMode = "smoothed";
              UpdateCheckedState("tempDisplayGroup", "平滑值");
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

            dataLocalize = (string)key.GetValue("DataLocalize", "off");
            if (dataLocalize == "on") {
              UpdateCheckedState("dataLocalizeGroup", "开启");
            } else {
              UpdateCheckedState("dataLocalizeGroup", "关闭");
            }
          } else {
            // 如果注册表键不存在，可以使用默认值
            LoadFanConfig("silent.txt");
            if (platformSettings != null && platformSettings.UnleashedModeSupport)
              SetFanMode(0x04);
            else
              SetFanMode(0x31);
            SetMaxFanSpeedOff();
            SetMaxGpuPower();
            // 首次运行：默认开启 CPU/GPU 监控，启动进程
            StartHardwareMonitor();
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error restoring configuration: {ex.Message}");
      }

      // 保证应用启动时如果不包含 DataLocalize 键（第一次运行或旧版升级），菜单项UI依然能被初始化选中
      if (dataLocalize == "on") {
        UpdateCheckedState("dataLocalizeGroup", "开启");
      } else {
        UpdateCheckedState("dataLocalizeGroup", "关闭");
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

    static CancellationTokenSource _pipeCts;
    static void getOmenKeyTask() {
      _pipeCts = new CancellationTokenSource();
      var token = _pipeCts.Token;
      System.Threading.Tasks.Task.Run(() => {
        while (!token.IsCancellationRequested) {
          try {
            using (var pipeServer = new NamedPipeServerStream("OmenSuperHubPipe", PipeDirection.In)) {
              pipeServer.WaitForConnection();    // 若需要支持取消，可用异步版本
              using (var reader = new StreamReader(pipeServer)) {
                string message = reader.ReadToEnd();
                if (message.Contains("OmenKeyTriggered") && !checkFloating)
                  checkFloating = true;
              }
            }
          } catch (Exception) when (token.IsCancellationRequested) {
            break;
          } catch (Exception ex) {
            Console.WriteLine("Pipe error: " + ex.Message);
          }
        }
      }, token);
    }

    static readonly object _floatingLock = new object();
    // 显示浮窗
    static void ShowFloatingForm() {
      lock (_floatingLock) {
        if (floatingForm == null || floatingForm.IsDisposed) {
          floatingForm = new FloatingForm(monitorText(), textSize, floatingBarLoc);
          floatingForm.Show();
        } else {
          floatingForm.BringToFront();
        }
      }
    }

    // 关闭浮窗
    static void CloseFloatingForm() {
      lock (_floatingLock) {
        if (floatingForm != null && !floatingForm.IsDisposed) {
          floatingForm.Close();
          floatingForm.Dispose();
          floatingForm = null;
        }
      }
    }

    // 更新浮窗的文字内容
    static void UpdateFloatingText() {
      lock (_floatingLock) {
        if (floatingForm == null || floatingForm.IsDisposed) return;
        // debug模式下需取消注释，release模式下需注释以避免打断右键菜单
        //if (floatingForm.InvokeRequired) {
        //  floatingForm.BeginInvoke(new System.Action(() => UpdateFloatingText()));
        //  return;
        //}
        floatingForm.TopMost = true;
        floatingForm.SetText(monitorText(), textSize, floatingBarLoc);
      }
    }

    //生成监控信息
    static string monitorText() {
      string str = "";
      if (monitorCPU)
        str += $"CPU: {CPUTemp:F1}°C, {CPUPower:F1}W";
      if (monitorGPU) {
        if (str.Length > 0) str += "\n";
        str += $"GPU: {GPUTemp:F1}°C, {GPUPower:F1}W";
      }
      if (monitorFan) {
        if (str.Length > 0) str += "\n";
        str += $"Fan:  {fanSpeedNow[0] * 100}, {fanSpeedNow[1] * 100}";
      }
      if (str.Length == 0) str = "监控已关闭";
      return str;
    }

    static void Exit() {
      _pipeCts?.Cancel();
      if (omenKey == "custom") {
        OmenKeyOff();
      }
      tooltipUpdateTimer.Stop(); // 停止定时器

      //openComputer.Close();
      StopHardwareMonitor();
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
      MessageBox.Show("OSH出现意外错误，详细信息请查看error.log。");
    }
  }
}
