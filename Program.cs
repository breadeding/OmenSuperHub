using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Hp.Bridge.Client.SDKs.PerformanceControl.DataStructure;
using HP.Omen.Core.Model.Device.Enums;
using HP.Omen.Core.Model.Device.Models;
using Microsoft.Win32;
using static OmenSuperHub.GpuAppManager;
using static OmenSuperHub.OmenHardware;
using static OmenSuperHub.OmenLighting;
using LibreComputer = LibreHardwareMonitor.Hardware.Computer;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreIHardware = LibreHardwareMonitor.Hardware.IHardware;
using LibreISensor = LibreHardwareMonitor.Hardware.ISensor;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace OmenSuperHub {
  static partial class Program {
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static float CPUTemp = 50;
    static float GPUTemp = 40;
    static byte currentAnimSpeed = 1, currentAnimDirection = 0, currentAnimTheme = 0, currentAnimEffect = 2;
    // 单键RGB当前选中状态（用于菜单勾选，null/-1 表示未选择）
    static string perKeyStaticColorSel = null;
    static string perKeyAnimationSel = null;
    static int perKeyBrightnessSel = -1;
    // 四分区/灯条颜色选择状态（key = device tag, value = 颜色名称；null 表示自定义或未选）
    static string zoneGlobalColorSel_Keyboard = null;
    static string zoneGlobalColorSel_LightBar = null;
    static string[] zoneColorSel_Keyboard = new string[4];
    static string[] zoneColorSel_LightBar = new string[4];
    // 四分区/灯条 WMI 协议选择（默认 BasicFourZone；用户可在菜单中切换并持久化）
    static LightingControlInterface kbControlInterface = LightingControlInterface.BasicFourZone;
    static LightingControlInterface lbControlInterface = LightingControlInterface.Dojo;
    static float CPUPower = 0;
    static float GPUPower = 0;
    static int DBVersion = 2, countDB = 0, countDBInit = 5, tryTimes = 0, CPULimitDB = 25;
    static int textSize = 48;
    static int countRestore = 0, gpuClock = 0;
    static int alreadyRead = 0, alreadyReadCode = 1000;
    static string fanTable = "cool", fanControl = "auto", tempSensitivity = "high", tppPower = "null", iccMax = "null", acLoadline = "null", cpuPower = "null", tgpPower = "on", ppabPower = "on", dState = "normal", autoStart = "off", customIcon = "original", floatingBar = "off", floatingBarLoc = "left", omenKey = "default", dataLocalize = "off", appLanguage = "zh-CN";
    static volatile bool monitorFan = true;
    static bool skipCheckedUpdate = false; // action 内拦截时置 true，阻止 CreateMenuItem 覆盖勾选
    static bool monitorCPU = true, monitorGPU = true, isConnectedToNVIDIA = true, prevIsConnectedToNVIDIA = true, powerOnline = true, checkFloating = false, isTwoBytePL4 = false;
    static bool hasNVIDIAGpu, hasAMDDiscreteGpu; // 启动时一次性检测，硬件状态不会改变
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
    static SortedDictionary<float, List<int>> CPUTempFanMap = new SortedDictionary<float, List<int>>();
    static SortedDictionary<float, List<int>> GPUTempFanMap = new SortedDictionary<float, List<int>>();
    static System.Threading.Timer fanControlTimer;
    static System.Timers.Timer tooltipUpdateTimer; // Timer for updating tooltip
    static System.Windows.Forms.Timer checkFloatingTimer, optimiseTimer;
    static NotifyIcon trayIcon;
    static FloatingForm floatingForm;
    static ToolStripMenuItem irSensorMenu;
    static ToolStripMenuItem ambientSensorMenu;
    static ToolStripMenuItem pchSensorMenu;
    static ToolStripMenuItem vrSensorMenu;

    static bool isSysInfoMenuOpen = false;
    static int? maxCPUTemp = null;
    static int? maxGPUTemp = null;
    static string systemSSID;
    static bool supportAni = false;
    static bool supportDojo = false;
    static bool supportLightbar = false;
    static DeviceEnums.DeviceType deviceType;
    static PlatformSettings platformSettings;
    static GraphicsMode NvGraphicsMode;
    static NbKeyboardLightingType kbType;

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
        kbType = GetKeyboardType();
        systemSSID = DeviceModel.ThisSystemID; // DeviceModel.OmenPlatform.Name
        deviceType = DeviceModel.DeviceType;
        string sku = PerformanceControlHelper.GetPlatformSku(isInit: true);
        platformSettings = PerformanceControlHelper.GetPlatformSettings(deviceType.ToString(), sku);
        if (FourZoneHelper.IsAnimationSupported) {
          supportAni = true;
        }
        if (DeviceModel.OmenPlatform.Feature.Contains("DojoLighting")) {
          supportDojo = true;
          if (IsLightBarPlatform())
            supportLightbar = true;
        }

        NvGraphicsMode = GetGfxMode();
        hasAMDDiscreteGpu = HasAmdDiscreteGpu();
        hasNVIDIAGpu = GetNVIDIAModel() != null;
        if (hasNVIDIAGpu && (NvGraphicsMode == GraphicsMode.Hybrid || NvGraphicsMode == GraphicsMode.Optimus))
          ExtractAndPreloadNativeDll("NvidiaApi.dll");
        // 固定为释放全部性能模式
        SetUnleashMode();

        powerOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        monitorQuery();

        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        isTwoBytePL4 = IsTwoBytePL4Supported();

        // Initialize tray icon
        InitMaxTemp();
        InitPlatformMaxFanSpeed();
        LoadLanguageSetting();  // 必须在 InitTrayIcon 之前，使菜单使用正确语言
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

        //MessageBox.Show($"消息测试", Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        //trayIcon.BalloonTipTitle = "消息测试";
        //trayIcon.BalloonTipText = $"消息测试";
        //trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
        //trayIcon.ShowBalloonTip(3000);

        //Console.WriteLine($"DeviceType: {WindowsLightingUtility.IsLightBarPlatform()}");
        //Console.WriteLine($"DeviceType: {deviceType}");
        //Console.WriteLine($"PlatformSku: {sku}");
        //Console.WriteLine($"TppMaxValue: {platformSettings.TppMaxValue}");

        //Platform omenPlatform = DeviceModel.OmenPlatform;
        //Console.WriteLine($"Platform Name: {omenPlatform.Name}");
        //Console.WriteLine($"Display Name: {omenPlatform.DisplayName}");
        //Console.WriteLine($"Features: {string.Join(", ", omenPlatform.Feature ?? new List<string>())}");
        //Console.WriteLine($"Background Features: {string.Join(", ", omenPlatform.BackgroundFeature ?? new List<string>())}");
        //if (omenPlatform.ProductNum != null) {
        //  foreach (var info in omenPlatform.ProductNum)
        //    Console.WriteLine($"  SSID: {info.SSID}, Cycle: {info.Cycle}");
        //}

        Logger.Info($"version: {version}");
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

    static string GetBiosVersion() {
      using (var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
      using (var collection = searcher.Get())
        foreach (ManagementObject obj in collection)
          return obj["SMBIOSBIOSVersion"]?.ToString() ?? "未知";
      return "未知";
    }

    static string GetCpuModel() {
      using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
      using (var collection = searcher.Get())
        foreach (ManagementObject obj in collection)
          return obj["Name"]?.ToString()?.Trim() ?? "未知";
      return "未知";
    }

    public static bool HasIntelCpu() {
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Manufacturer, Name FROM Win32_Processor")) {
          foreach (var obj in searcher.Get()) {
            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            string name = obj["Name"]?.ToString() ?? "";

            // GenuineIntel 是 Intel CPU 的标准制造商字符串
            if (manufacturer.IndexOf("GenuineIntel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) {
              return true;
            }
          }
        }
      } catch { }
      return false;
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

    public static bool HasAmdDiscreteGpu() {
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, AdapterCompatibility, VideoProcessor FROM Win32_VideoController")) {
          foreach (var obj in searcher.Get()) {
            string name = obj["Name"]?.ToString() ?? "";
            string vendor = obj["AdapterCompatibility"]?.ToString() ?? "";
            string processor = obj["VideoProcessor"]?.ToString() ?? "";

            // AMD 显卡供应商 ID 为 1002
            bool isAmd = vendor.Contains("1002") || name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isAmd) continue;

            // 排除典型的集显命名特征（Radeon Graphics 不带 RX/数字型号）
            bool isIntegrated = name.Contains("Radeon Graphics") && !name.Contains("RX")
                               || name.Contains("AMD Radeon(TM) Graphics");

            if (!isIntegrated) // 不是集显，那就是独显
              return true;

            // 也可以进一步检查 VideoProcessor 字段，独显通常有具体代号如 "Navi", "Ellesmere", "Vega 10"
            if (!string.IsNullOrEmpty(processor) && !processor.Contains("Renoir") && !processor.Contains("Cezanne") && !processor.Contains("Rembrandt"))
              return true; // 集显 APU 代号为 Renoir/Cezanne/Rembrandt 等
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
        if (hasNVIDIAGpu || !hasAMDDiscreteGpu)      // ★ 先检查硬件，避免触发 ADL
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
      var computer = new LibreComputer() { IsCpuEnabled = true };

      try {
        computer.Open();
      } catch (Exception ex) {
        Console.Error.WriteLine("CRASH: Open failed - " + ex.Message);
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
              Console.Error.WriteLine("CRASH: Update failed - " + ex.Message);
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
          Console.Error.WriteLine("CRASH: " + ex.Message);
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
        Logger.Error("HardwareMonitor [HWMonitor ERR] " + e.Data);
      };

      hwMonitorProcess.EnableRaisingEvents = true;
      hwMonitorProcess.Exited += (s, e) => {
        if (hwMonitorStopping) {
          hwMonitorStopping = false;
          return;
        }
        //Logger.Info("StartHardwareMonitor [HWMonitor] 进程退出，准备重启...");
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
          Logger.Info($"BIOS 保护状态 - 过流: {ocp}, 过温: {otp}");
        }
      } else {
        Logger.Error("无法读取 BIOS 保护状态");
      }

      //更新显示器连接到显卡状态
      monitorQuery();
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
          //Logger.Info("笔记本已连接到电源。");
          powerOnline = true;
        } else {
          //Logger.Info("笔记本未连接到电源。");
          powerOnline = false;
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
        MessageBox.Show(Strings.NoCustomIcon, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        MessageBox.Show(Strings.NoCustomIcon, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    
    // 状态栏定时更新任务+硬件查询+DB解锁
    static void UpdateTooltip() {
      try {
        QueryHardware();
      } catch (Exception ex) {
        Logger.Error($"[UpdateTooltip] QueryHardware 异常: {ex.Message}");
      }

      if (monitorFan)
        fanSpeedNow = GetFanLevel();
      trayIcon.Text = monitorText();
      //Console.WriteLine("UpdateTooltip");

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
            irSensorMenu.Text = $"{Strings.SysIRSensor}: {GetSensorTemperature(0)}°C";
            ambientSensorMenu.Text = $"{Strings.SysAmbient}: {GetSensorTemperature(1)}°C";
            pchSensorMenu.Text = $"{Strings.SysPCH}: {GetSensorTemperature(2)}°C";
            vrSensorMenu.Text = $"{Strings.SysVR}: {GetSensorTemperature(3)}°C";
            if (hasNVIDIAGpu) {
              var limits = GetGpuPowerLimits();
              string limitsText = limits[0] == -2f ? "--W / --W" : $"{limits[0]:F0}W / {limits[1]:F0}W";
              ToolStripMenuItem limitsMenu = (ToolStripMenuItem)parentStrip.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Text.StartsWith(Strings.SysNvidiaPower));
              if (limitsMenu != null) limitsMenu.Text = Strings.SysNvidiaPowerLimitText(limitsText);
            }
          }));
        } else {
          irSensorMenu.Text = $"{Strings.SysIRSensor}: {GetSensorTemperature(0)}°C";
          ambientSensorMenu.Text = $"{Strings.SysAmbient}: {GetSensorTemperature(1)}°C";
          pchSensorMenu.Text = $"{Strings.SysPCH}: {GetSensorTemperature(2)}°C";
          vrSensorMenu.Text = $"{Strings.SysVR}: {GetSensorTemperature(3)}°C";
          if (hasNVIDIAGpu) {
            var limits = GetGpuPowerLimits();
            string limitsText = limits[0] == -2f ? "--W / --W" : $"{limits[0]:F0}W / {limits[1]:F0}W";
            ToolStripMenuItem limitsMenu = (ToolStripMenuItem)parentStrip.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Text.StartsWith(Strings.SysNvidiaPower));
            if (limitsMenu != null) limitsMenu.Text = $"{Strings.SysNvidiaPower}: {limitsText}";
          }
        }
      }

      // 启用再禁用DB驱动
      if (countDB > 0) {
        countDB--;
        if (countDB == 0) {
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /disable-device {deviceId}";
          ExecuteCommand(command);

          float[] limits = GetGpuPowerLimits();   // limits[0] = Current, limits[1] = Max
          // 检查显卡当前功耗限制，离电时当作解锁成功
          if (powerOnline && Math.Abs(limits[1] - limits[0]) > 1f) {
            tryTimes++;
            // 失败时重试一次
            if (tryTimes == 2) {
              tryTimes = 0;
              if (CPUPower > CPULimitDB + 10)
                MessageBox.Show(Strings.DbUnlockCpuHighWarning, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
              else
                MessageBox.Show(Strings.DbUnlockFailed(limits[0]), Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
              command = $"pnputil /enable-device {deviceId}";
              ExecuteCommand(command);
              DBVersion = 2;
              countDB = 0;
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", Strings.DbNormal);
            } else {
              SetGpuPowerState(true, true);
              SetCpuPowerLimit((byte)CPULimitDB);
              countDB = countDBInit;
            }
          } else {
            tryTimes = 0;
            if (autoStart == "off") {
              MessageBox.Show(Strings.DbUnlockSuccessNoAutoStart, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            //MessageBox.Show($"解锁成功！\n当前最大显卡功耗锁定为：{-powerLimits:F2} W ！", Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
          }
          if (tryTimes == 0) {
            // 恢复CPU功耗设定
            RestoreCPUPower();
            // 恢复GPU功耗设定
            SetGpuPowerState(tgpPower == "on", ppabPower == "on", dState == "normal" ? 1 : 2);
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
            Logger.Error($"Sync error when writing cpu_temp.txt: {ex.Message}");
          }

          try {
            if (lastGpuText == null || lastGpuText != gpuText) {
              File.WriteAllText(Path.Combine(basePath, "gpu_temp.txt"), gpuText);
              lastGpuText = gpuText;
            }
          } catch (Exception ex) {
            Logger.Error($"Sync error when writing gpu_temp.txt: {ex.Message}");
          }

          try {
            if (lastFanText == null || lastFanText != fanText) {
              File.WriteAllText(Path.Combine(basePath, "fan_rpm.txt"), fanText);
              lastFanText = fanText;
            }
          } catch (Exception ex) {
            Logger.Error($"Sync error when writing fan_rpm.txt: {ex.Message}");
          }

        } catch (Exception ex) {
          // 忽略文件被占用的偶发错误，或者在这里记录日志
          Logger.Error("Sync error: " + ex.Message);
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
      if (platformMaxFanSpeed.HasValue && smoothedCPUTemp > currentMaxCPUTemp - 2 && fanControl.Contains(" RPM")) {
        // 检查是否满足转速低于平台最大转速80%的条件
        bool fanSpeedCondition = true;
        if (platformMaxFanSpeed.Value > 0) {
          int currentFanSpeed;
          lock (fanSpeedNow) { currentFanSpeed = fanSpeedNow[0] * 100; }
          fanSpeedCondition = currentFanSpeed < platformMaxFanSpeed.Value * 0.8;
        }

        if (fanSpeedCondition) {
          // 先切换为降温模式（cool配置）
          fanTable = "cool";
          LoadFanConfig("cool.txt");
          UpdateCheckedState("fanTableGroup", Strings.FanCoolMode);
          SaveConfig("FanTable");

          // 再切换为自动风扇控制
          fanControl = "auto";
          SetMaxFanSpeedOff();
          fanControlTimer.Change(0, 1000);
          UpdateCheckedState("fanControlGroup", Strings.FanAuto);
          SaveConfig("FanControl");

          trayIcon.BalloonTipTitle = Strings.HighTempBalloonTitle;
          trayIcon.BalloonTipText = Strings.HighTempBalloonText(currentMaxCPUTemp, smoothedCPUTemp);
          trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
          trayIcon.ShowBalloonTip(3000);
        }
      }

      //通过countQuery延时来确保温度正常读取
      if (countQuery <= 5 && monitorGPU)
        countQuery++;
      //自动关闭GPU监控
      if (countQuery > 5 && autoStopMonitorGPU && !isConnectedToNVIDIA && monitorGPU && ((GPUPower >= 0 && GPUPower <= 1.3) || !getGPU)) {
        // 如果是NVIDIAGpu平台，进一步检查是否有程序占用GPU
        bool isGpuIdle = true;
        if (hasNVIDIAGpu) {
          var gpuApps = GetGpuApps();
          if (gpuApps != null && gpuApps.Count > 0) {
            isGpuIdle = false;
          }
        }

        if (isGpuIdle) {
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
          UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOff);
          SaveConfig("MonitorGPU");

          // 设置通知的文本和标题
          trayIcon.BalloonTipTitle = Strings.GpuAutoStopTitle;
          trayIcon.BalloonTipText = Strings.GpuAutoStopText;
          trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
          trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
        }
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
        UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOn);
        SaveConfig("MonitorGPU");

        // 设置通知的文本和标题
        trayIcon.BalloonTipTitle = Strings.GpuAutoStopTitle;
        trayIcon.BalloonTipText = Strings.GpuAutoStartText;
        trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
        trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      }

      // 似乎无法一次性关闭GPU监控及选项
      if (!monitorGPU) {
        SetGpuMonitorState(false);
        UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOff);
      }

      prevIsConnectedToNVIDIA = isConnectedToNVIDIA;

      // 释放防重入标志
      Interlocked.Exchange(ref _isQuerying, 0);
    }

    // Helper function to calculate fan speed for a specific temperature map
    static int GetFanSpeedForSpecificTemperature(float temperature, SortedDictionary<float, List<int>> tempFanMap, int fanIndex) {
      // 字典已按键升序排列，直接线性扫描，O(n) 但 n 极小（通常 3~6 个点）
      float lowerKey = tempFanMap.Keys.First();
      float upperKey = lowerKey;

      foreach (float key in tempFanMap.Keys) {
        if (key <= temperature) lowerKey = key;
        else { upperKey = key; break; }
        upperKey = key; // 如果循环完也没 break，upper == lower == 最大键
      }

      if (lowerKey == upperKey)
        return tempFanMap[lowerKey][fanIndex];

      int lowerSpeed = tempFanMap[lowerKey][fanIndex];
      int upperSpeed = tempFanMap[upperKey][fanIndex];
      float interpolated = lowerSpeed + (upperSpeed - lowerSpeed) * (temperature - lowerKey) / (upperKey - lowerKey);
      return (int)interpolated;
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
                UpdateCheckedState("floatingBarGroup", Strings.FloatingHide);
              } else {
                floatingBar = "on";
                ShowFloatingForm();
                UpdateCheckedState("floatingBarGroup", Strings.FloatingShow);
              }
              SaveConfig("FloatingBar");
            }
          }
        } catch (Exception ex) {
          Logger.Error($"Error restoring configuration: {ex.Message}");
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
            Logger.Error("Pipe error: " + ex.Message);
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
      if (str.Length == 0) str = Strings.MonitorClosed;
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
      Logger.Error($"CurrentDomain_UnhandledException: {e.ExceptionObject}");
      MessageBox.Show(Strings.CrashMessage);
    }

    static void Application_ThreadException(object sender, ThreadExceptionEventArgs e) {
      Logger.Error($"Application_ThreadException: {e.Exception}");
      MessageBox.Show(Strings.CrashMessage);
    }
  }
}
