using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Hp.Bridge.Client.SDKs.PerformanceControl.DataStructure;
using HP.Omen.Core.Common.NVidiaApi;
using HP.Omen.Core.Model.Device.Enums;
using HP.Omen.Core.Model.Device.Models;
using Microsoft.Win32;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using static HP.Omen.Core.Model.Device.Models.GraphicsSwitcherHelper;
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

    // ── 低级鼠标钩子（用于托盘图标滚轮切换预设）──────────────────────────
    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    // Shell_NotifyIconGetRect：获取托盘图标的屏幕矩形
    [StructLayout(LayoutKind.Sequential)]
    struct NOTIFYICONIDENTIFIER {
      public uint cbSize;
      public IntPtr hWnd;
      public uint uID;
      public Guid guidItem;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconRect);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT {
      public Point pt;
      public int mouseData;
      public int flags;
      public int time;
      public IntPtr dwExtraInfo;
    }

    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEWHEEL = 0x020A;

    static IntPtr _mouseHook = IntPtr.Zero;
    static LowLevelMouseProc _mouseHookProc; // 防止 GC 回收委托
    static Control _invokeTarget;            // 专用 marshal 控件（句柄在安装钩子前已创建）

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
    static int DBVersion = 2, countDB = 0, countDBInit = 10, tryTimes = 0, maxRetry = 5, CPULimitDB = 20;
    static ToolStripMenuItem performanceControlMenu;
    static int textSize = 40;
    static int countRestore = 0, gpuClock = 0, gpuCoreOverclock = -1, gpuMemoryOverclock = -1, maxFrameRate = -1, graphicsBoostClock = 0;
    static int alreadyRead = 0, alreadyReadCode = 1000;
    static readonly string[] PresetOrder = { "PresetExtreme", "PresetGpuPriority", "PresetLightUse", "PresetCustom1", "PresetCustom2", "PresetCustom3" };
    static string currentPreset = "PresetCustom1", presetCustom1Name = Strings.PresetCustom1, presetCustom2Name = Strings.PresetCustom2, presetCustom3Name = Strings.PresetCustom3;
    static string fanTable = "cool", fanControl = "auto", tempSensitivity = "high", tppPower = "null", iccMax = "null", acLoadline = "null", cpuPower = "null", tgpPower = "on", ppabPower = "on", dState = "normal", autoStart = "off", customIcon = "original", floatingBar = "off", floatingBarLoc = "left", floatingBarScreen = "", omenKey = OmenKeyActions.Default, omenKeyAppPath = "", omenKeyAppName = "", omenKeyShortcut = "", omenKeyPresetCandidates = "", dataLocalize = "off", appLanguage = "zh-CN", autoFanProtect = "on";
    static volatile bool monitorFan = false;
    static bool skipCheckedUpdate = false; // action 内拦截时置 true，阻止 CreateMenuItem 覆盖勾选
    static bool powerOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
    static bool monitorCPU = true, monitorGPU = true, isConnectedToNVIDIA = true, prevIsConnectedToNVIDIA = true, omenKeyTriggered = false; // isTwoBytePL4 = false;
    static bool hasNVIDIAGpu; // 启动时一次性检测，硬件状态不会改变
    static string monitorRefreshRate = "low"; // 刷新频率：low=1s, high=0.25s
    static List<int> fanSpeedNow = new List<int> { 20, 20, 0 };
    static float respondSpeed = 0.4f;

    static int? maxCPUTemp = null;
    static int? maxGPUTemp = null;
    static float CPUTemp = 50, GPUTemp = 40, rawTempCPU = 50f, rawTempGPU = 40f;
    static float CPUPower = 0, GPUPower = 0, rawPowerCPU = 0f, rawPowerGPU = 0f;
    static bool rawGotGPU = false;
    static volatile bool tempReady = false;   // 子进程首次输出有效温度后置 true
    static volatile bool cpuTempReady = false; // CPU 温度已初始化给平滑值，允许参与风扇控制
    static volatile bool gpuTempReady = false; // GPU 温度已初始化给平滑值，允许参与风扇控制
    static volatile bool hwMonitorStopping = false; // 主动停止时置 true，阻止 Exited 自动重启
    static Process hwMonitorProcess;
    static StreamWriter hwMonitorIn;

    // Cache last written values to avoid unnecessary disk reads/writes
    static string lastCpuText = null, lastGpuText = null, lastFanText = null, pawnIOState = "";
    static string tempDisplayMode = "smoothed"; // 温度显示方式：smoothed=平滑值, raw=原始值
    static int? platformMaxFanSpeed = null; // 平台最大转速（RPM），由LoadDefaultFanConfig获取后缓存
    static SortedDictionary<float, int> CPUTempFanMap = new SortedDictionary<float, int>();
    static SortedDictionary<float, int> GPUTempFanMap = new SortedDictionary<float, int>();
    static System.Threading.Timer fanControlTimer;
    static System.Timers.Timer tooltipUpdateTimer; // Timer for updating tooltip
    static System.Windows.Forms.Timer checkFloatingTimer, optimiseTimer;
    static NotifyIcon trayIcon;
    static FloatingForm floatingForm;
    static ToolStripMenuItem irSensorMenu;
    static ToolStripMenuItem ambientSensorMenu;
    static ToolStripMenuItem pchSensorMenu;
    static ToolStripMenuItem vrSensorMenu;
    static ToolStripTrackBar fanTrackBar, cpuPowerTrackBar, tppTrackBar, gpuCoreOverclockTrackBar, gpuMemoryOverclockTrackBar, gpuClockTrackBar, maxFrameRateTrackBar, textSizeTrackBar;
    static ToolStripMenuItem fanValueLabel, cpuPowerValueLabel, tppValueLabel, gpuCoreOverclockValueLabel, gpuMemoryOverclockValueLabel, gpuClockValueLabel, maxFrameRateValueLabel, textSizeLabel;

    static bool Is3FanNb = false, isFanCleanSupported = false, isFanLegacyCleanSupported = false;
    static bool isSysInfoMenuOpen = false;
    static string systemSSID, sku, biosVersion;
    static bool supportAni = false, supportDojo = false, supportLightbar = false, supportHotSwitch = false;
    static bool isCPUPowerControlSupported = false, isAmbientSensorSupported = false;
    static DeviceEnums.DeviceType deviceType;
    static string deviceDisplayName;
    static int cycleNumber;
    static PlatformSettings platformSettings;
    static GraphicsSwitcherMode NvGraphicsMode;
    static NbKeyboardLightingType kbType;
    static SynchronizationContext uiContext;
    //static Stopwatch sw = Stopwatch.StartNew();

    [STAThread]
    static void Main(string[] args) {
      //Console.WriteLine($"0.1: {sw.ElapsedMilliseconds}ms");
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

        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        // 读取 deviceDisplayName / cycleNumber / deviceType / supportDojo / systemSSID / alreadyRead
        LoadDeviceInfoFromRegistry();
        //Console.WriteLine($"0.2: {sw.ElapsedMilliseconds}ms");
        // 每版本仅显示一次
        if (alreadyRead != alreadyReadCode) {
          string validationResult = Validation(deviceDisplayName);
          if (validationResult == Strings.ValidationUnsupported) {
            var result = MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.ProductUnsupported, Strings.Warning, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result != DialogResult.OK)
              return; // 退出程序
          } else if (validationResult == Strings.ValidationUnsupportedHPProduct) {
            var result = MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.ProductUnsupportedHP, Strings.Warning, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result != DialogResult.OK)
              return; // 退出程序
          } else if (validationResult == Strings.ValidationOldOmenProduct) {
            var result = MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.ProductOldOmen, Strings.Warning, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result != DialogResult.OK)
              return; // 退出程序
          }
        }

        var t1 = Task.Run(() => {
          //Console.WriteLine($"1.1: {sw.ElapsedMilliseconds}ms");
          platformSettings = PerformanceControlHelper.GetPlatformSettings(deviceType.ToString(), sku);
          //Console.WriteLine($"1.2: {sw.ElapsedMilliseconds}ms");

          if (platformSettings != null) {
            currentPreset = "PresetExtreme";
            isCPUPowerControlSupported = true;
          }
          //isCPUPowerControlSupported = IsPowerControlSupported(deviceType); // 似乎不准确
          InitPlatformMaxFanSpeed();
          InitMaxTemp();
        });
        var t2 = Task.Run(() => {
          biosVersion = GetBiosVersion();
        });
        var t3 = Task.Run(() => {
          hasNVIDIAGpu = HasNvidiaGpu();
          if (hasNVIDIAGpu) {
            ExtractAndPreloadNativeDll("NvidiaApi.dll");
            maxFrameRate = NvApiWrapper.NVAPI_GetMaxFrameRate();
            graphicsBoostClock = GetGraphicsBoostClock();
          }
        });
        var t4 = Task.Run(() => kbType = GetKeyboardType());
        var t5 = Task.Run(() => NvGraphicsMode = GetGfxMode());
        var t6 = Task.Run(() => {
          SetUnleashMode(); // 固定为释放全部性能模式
          Is3FanNb = IsThreeFanSupported();
        });
        var t7 = Task.Run(() => {
          isFanCleanSupported = IsCleanCreekSupported();
          isFanLegacyCleanSupported = IsLegacyCleanCreekSupported();
        });
        var t8 = Task.Run(() => {
          getOmenKeyTask();
          monitorQuery();
          if (supportDojo && IsLightBarPlatform())
            supportLightbar = true;
        });
        var t9 = Task.Run(() => {
          SetBrowserEmulationForWebBrowser();
          int irTemp = GetSensorTemperature(0);
          int ambientTemp = GetSensorTemperature(1);
          isAmbientSensorSupported = ambientTemp > 1 && irTemp != ambientTemp;
        });
        //var t10 = Task.Run(() => isTwoBytePL4 = IsTwoBytePL4Supported());

        //Console.WriteLine($"1: {sw.ElapsedMilliseconds}ms");
        Task.WaitAll(t1, t2, t3, t4, t5, t6, t7, t8, t9);
        //Console.WriteLine($"2: {sw.ElapsedMilliseconds}ms");

        if (FourZoneSupportHelper.IsAnimationSupported(kbType, deviceType, cycleNumber)) {
          supportAni = true;
        }

        LoadLanguageSetting();  // 必须在 InitTrayIcon 之前，使菜单使用正确语言
        InitTrayIcon();
        uiContext = SynchronizationContext.Current;
        //Console.WriteLine($"3: {sw.ElapsedMilliseconds}ms");

        optimiseTimer = new System.Windows.Forms.Timer();
        optimiseTimer.Interval = 30000;
        optimiseTimer.Tick += (s, e) => optimiseSchedule();
        optimiseTimer.Start();

        // 立即执行一次
        optimiseSchedule();

        // Main loop to query CPU and GPU temperature every second
        fanControlTimer = new System.Threading.Timer((e) => {
          if (fanControl != "auto") return;
          int fanSpeed = GetFanSpeedForTemperature() / 100;
          if (fanSpeed < 0) return;
          int s0, s1;
          lock (fanSpeedNow) { s0 = fanSpeedNow[0]; s1 = fanSpeedNow[1]; }
          if (Math.Abs(fanSpeed - s0) > 1 || Math.Abs(fanSpeed - s1) > 1) {
            SetFanLevel(fanSpeed, fanSpeed, Is3FanNb);
            if (!monitorFan) {
              lock (fanSpeedNow) { fanSpeedNow[0] = fanSpeed; fanSpeedNow[1] = fanSpeed; }
            }
          }
        }, null, 100, 1000);

        checkFloatingTimer = new System.Windows.Forms.Timer();
        checkFloatingTimer.Interval = 100;
        checkFloatingTimer.Tick += (s, e) => HandleOmenKeyAction();
        checkFloatingTimer.Start();

        RestoreConfig();
        //Console.WriteLine($"4: {sw.ElapsedMilliseconds}ms");

        if (alreadyRead != alreadyReadCode) {
          HelpForm.Instance.Show();
          alreadyRead = alreadyReadCode;
          SaveConfig("AlreadyRead");
        }

        SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerChange);
        //PrintSystemDesignData();

        //MessageBox.Show($"消息测试", Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        //trayIcon.BalloonTipTitle = "消息测试";
        //trayIcon.BalloonTipText = $"消息测试";
        //trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
        //trayIcon.ShowBalloonTip(3000);

        //Stopwatch sw = Stopwatch.StartNew();
        //Console.WriteLine($"1: {sw.ElapsedMilliseconds}ms");
        //Console.Error.WriteLine("CRASH: " + $"1: {sw.ElapsedMilliseconds}ms");

        //Platform omenPlatform = DeviceModel.OmenPlatform;
        //Console.WriteLine($"Platform Name: {omenPlatform.Name}");
        //Console.WriteLine($"Display Name: {omenPlatform.DisplayName}");
        //Console.WriteLine($"Features: {string.Join(", ", omenPlatform.Feature ?? new List<string>())}");
        //Console.WriteLine($"IsNVdGPU: {OmenHsaClient.IsNVdGPU}");
        //Console.WriteLine($"IsIntelGPU: {OmenHsaClient.IsIntelGPU}");
        //Console.WriteLine($"IsGpuSupport: {OmenHsaClient.IsGpuSupport}");
        //Console.WriteLine($"IsIntelGraphics: {OmenHsaClient.IsIntelGraphics()}");

        Logger.Info($"version: {version}");
        Application.Run();
      }
    }

    private static void SetBrowserEmulationForWebBrowser() {
      string appName = Process.GetCurrentProcess().ProcessName + ".exe";
      using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", true)) {
        if (key == null) {
          // 如果键不存在则创建
          Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
          using (var newKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", true)) {
            newKey?.SetValue(appName, 11001, RegistryValueKind.DWord);
          }
        } else {
          key.SetValue(appName, 11001, RegistryValueKind.DWord);
        }
      }
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
        Logger.Error($"LoadLibrary 失败，错误码: {Marshal.GetLastWin32Error()}");
      } else {
        supportHotSwitch = true;
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
      bool isEnabled = false;
      //Console.Error.WriteLine("CRASH: " + $"1: {sw.ElapsedMilliseconds}ms");
      var computer = new LibreComputer() { };
      //Console.Error.WriteLine("CRASH: " + $"2: {sw.ElapsedMilliseconds}ms");
      try {
        computer.Open();
      } catch (Exception ex) {
        Console.Error.WriteLine("CRASH: Open failed - " + ex.Message);
        Environment.Exit(1);
      }
      //Console.Error.WriteLine("CRASH: " + $"3: {sw.ElapsedMilliseconds}ms");
      int sleepMs = 1000;

      var readThread = new Thread(() => {
        while (true) {
          string line = Console.ReadLine();
          if (line == null) Environment.Exit(0);
          if (line == "GPU:ON") {
            isEnabled = false;
            computer.IsGpuEnabled = true;
            isEnabled = true;
          }
          if (line == "GPU:OFF") {
            computer.IsGpuEnabled = false;
          }
          if (line == "CPU:ON") {
            isEnabled = false;
            computer.IsCpuEnabled = true;
            isEnabled = true;
          }
          if (line == "CPU:OFF") {
            computer.IsCpuEnabled = false;
          }
          if (line.StartsWith("INTERVAL:") && int.TryParse(line.Substring(9), out int ms) && ms > 0)
            sleepMs = ms;
        }
      });
      readThread.IsBackground = true;
      readThread.Start();

      float tCpu = 50, pCpu = 0, tGpu = 40, pGpu = 0;

      while (!isEnabled) {
        Thread.Sleep(1);
      }
      // 等待可能的第二个监控打开
      Thread.Sleep(1);
      while (!isEnabled) {
        Thread.Sleep(1);
      }
      //Console.Error.WriteLine("CRASH: " + $"4: {sw.ElapsedMilliseconds}ms");
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
            //Console.Error.WriteLine("CRASH: " + $"5: {sw.ElapsedMilliseconds}ms");
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
                    if (sensor.Value.HasValue) {
                      pGpu = sensor.Value.GetValueOrDefault();
                      gGpu = true;
                    } else {
                      pGpu = -1;
                      gGpu = false;
                    }
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
          if (!gpuTempReady && rawGotGPU) {
            smoothedGPUTemp = rawTempGPU;
            gpuTempReady = true;
          }
          if (!rawGotGPU) {
            gpuTempReady = false;
            GPUTemp = 40;
            GPUPower = 0;
          }

          if (!tempReady) {
            tempReady = true;
            // 首次获取到数据立即刷新
            try {
              QueryHardware();
            } catch (Exception ex) {
              Logger.Error($"[UpdateTooltip] QueryHardware 异常: {ex.Message}");
            }
            UpdateFloatingText();
            UpdateTrayIconText();

            if (customIcon == "dynamic")
              UpdateDynamicIcon();
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
          SetFanLevel(rpmValue / 100, rpmValue / 100, Is3FanNb);
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
        Logger.Info("系统已恢复启动。");
        GetFanCount(out bool ocp, out bool otp);

        tooltipUpdateTimer.Start();
        countRestore = 3;
      }

      // 检查电源模式是否发生变化
      if (e.Mode == PowerModes.StatusChange) {
        // 获取当前电源连接状态
        var powerLineStatus = SystemInformation.PowerStatus.PowerLineStatus;
        if (powerOnline != (powerLineStatus == PowerLineStatus.Online)) {
          powerOnline = powerLineStatus == PowerLineStatus.Online;
          if (powerOnline) {
            Logger.Info("笔记本已连接到电源。");
            RestorePowerConfig();
          } else {
            Logger.Info("笔记本已断开电源。");
          }
        }
      }
    }

    // ── 托盘图标滚轮钩子 ─────────────────────────────────────────────────
    static void InstallTrayScrollHook() {
      if (_mouseHook != IntPtr.Zero) return;

      // 创建专用 marshal 控件并强制创建窗口句柄，确保 BeginInvoke 可用
      _invokeTarget = new Control();
      _invokeTarget.CreateControl(); // 强制创建 HWND

      _mouseHookProc = TrayScrollHookProc;
      _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
    }

    static void UninstallTrayScrollHook() {
      if (_mouseHook == IntPtr.Zero) return;
      UnhookWindowsHookEx(_mouseHook);
      _mouseHook = IntPtr.Zero;
      _invokeTarget?.Dispose();
      _invokeTarget = null;
    }

    // 获取托盘图标屏幕矩形；失败时返回 Rectangle.Empty
    static Rectangle GetTrayIconRect() {
      try {
        // NotifyIcon 内部 hWnd 通过反射取得
        var windowField = typeof(NotifyIcon).GetField("window",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (windowField == null) return Rectangle.Empty;
        var nativeWindow = windowField.GetValue(trayIcon) as System.Windows.Forms.NativeWindow;
        if (nativeWindow == null) return Rectangle.Empty;
        IntPtr hWnd = nativeWindow.Handle;

        var idField = typeof(NotifyIcon).GetField("id",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        uint id = idField != null ? (uint)(int)idField.GetValue(trayIcon) : 1u;

        var nid = new NOTIFYICONIDENTIFIER {
          cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)),
          hWnd = hWnd,
          uID = id
        };
        RECT rc;
        if (Shell_NotifyIconGetRect(ref nid, out rc) == 0) {
          return Rectangle.FromLTRB(rc.left, rc.top, rc.right, rc.bottom);
        }
      } catch { }
      return Rectangle.Empty;
    }

    static IntPtr TrayScrollHookProc(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL) {
        var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
        Rectangle iconRect = GetTrayIconRect();

        bool isOverTray = !iconRect.IsEmpty && iconRect.Contains(info.pt);

        if (isOverTray) {
          // mouseData 高 16 位为滚轮增量，向上为正
          int delta = (short)((info.mouseData >> 16) & 0xFFFF);
          // 用专用 marshal 控件切回 UI 线程（ContextMenuStrip 在首次打开前没有 HWND）
          bool up = delta > 0;
          _invokeTarget?.BeginInvoke(new Action(() => CyclePresetByScroll(up)));
        }
      }
      return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // scrollUp=true 向上滚（候选列表向前）；false 向下滚（向后）
    static void CyclePresetByScroll(bool scrollUp) {
      // 右键菜单打开时不切换，避免误操作
      if (trayIcon?.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Visible) return;

      var candidates = GetOmenKeyPresetCandidateKeys();
      if (candidates.Count == 0) return;

      int idx = candidates.IndexOf(currentPreset);
      if (idx < 0) idx = 0;
      int next = scrollUp
          ? (idx - 1 + candidates.Count) % candidates.Count
          : (idx + 1) % candidates.Count;
      string targetPreset = candidates[next];
      if (targetPreset != currentPreset) {
        applyPresetLogic(targetPreset);
      } else {
        UpdateTrayIconText();
      }
    }

    static void TrayIcon_MouseClick(object sender, MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) {
        ToggleFloatingBar();
      }
    }

    static bool CheckCustomIcon() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string iconPath = Path.Combine(currentPath, "custom.ico");
      // 检查图标文件是否存在
      if (File.Exists(iconPath)) {
        return true;
      } else {
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.NoCustomIcon, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.NoCustomIcon, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
      // 获取系统推荐的图标尺寸（已适配 DPI）
      Size iconSize = SystemInformation.IconSize;
      int width = iconSize.Width * 2;
      int height = iconSize.Height * 2;

      using (Bitmap bitmap = new Bitmap(width, height)) {
        using (Graphics graphics = Graphics.FromImage(bitmap)) {
          graphics.Clear(Color.Transparent);
          graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

          string text = number.ToString("00");

          using (Font font = new Font("Arial", 45.5f, FontStyle.Bold)) {
            // 测量文本大小
            SizeF textSize = graphics.MeasureString(text, font);

            // 计算居中位置
            float x = (width - textSize.Width) / 2;
            float y = (height - textSize.Height) / 8;

            // 绘制文本
            graphics.DrawString(text, font, Brushes.Tan, x, y);
          }

          // 转换为图标
          IntPtr hIcon = bitmap.GetHicon();
          Icon newIcon = Icon.FromHandle(hIcon);
          // 替换托盘图标
          Icon oldIcon = trayIcon.Icon;
          trayIcon.Icon = newIcon;
          // 如果旧图标不是默认图标，则显式释放
          if (oldIcon != null && oldIcon != Properties.Resources.smallfan) {
            oldIcon.Dispose();
          }
          // 销毁旧句柄（注意：不能直接销毁，因为 Icon.FromHandle 需要手动释放）
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

      UpdateTrayIconText();
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
        int irTemp = GetSensorTemperature(0);
        int ambientTemp = GetSensorTemperature(1);
        int pchTemp = GetSensorTemperature(2);
        int vrTemp = GetSensorTemperature(3);
        if (parentStrip.InvokeRequired) {
          parentStrip.Invoke(new System.Action(() => {
            if (irSensorMenu != null) irSensorMenu.Text = $"{Strings.SysIRSensor}: {FormatSensorTemperature(irTemp)}";
            if (ambientSensorMenu != null) ambientSensorMenu.Text = $"{Strings.SysAmbient}: {FormatSensorTemperature(ambientTemp)}";
            if (pchSensorMenu != null) pchSensorMenu.Text = $"{Strings.SysPCH}: {FormatSensorTemperature(pchTemp)}";
            if (vrSensorMenu != null) vrSensorMenu.Text = $"{Strings.SysVR}: {FormatSensorTemperature(vrTemp)}";
          }));
        } else {
          if (irSensorMenu != null) irSensorMenu.Text = $"{Strings.SysIRSensor}: {FormatSensorTemperature(irTemp)}";
          if (ambientSensorMenu != null) ambientSensorMenu.Text = $"{Strings.SysAmbient}: {FormatSensorTemperature(ambientTemp)}";
          if (pchSensorMenu != null) pchSensorMenu.Text = $"{Strings.SysPCH}: {FormatSensorTemperature(pchTemp)}";
          if (vrSensorMenu != null) vrSensorMenu.Text = $"{Strings.SysVR}: {FormatSensorTemperature(vrTemp)}";
        }
      }

      // 启用再禁用DB驱动
      if (countDB > 0) {
        countDB--;
        // 提前判断是否符合条件
        if (CPUPower > 0 && CPUPower < CPULimitDB) {
          float[] limits = GetGpuPowerLimits();   // limits[0] = Current, limits[1] = Max
          if (!powerOnline || Math.Abs(limits[1] - limits[0]) < 1f)
            countDB = 0;
        }
        if (tryTimes == 0)
          performanceControlMenu.ToolTipText = Strings.UnavailableReasonTip(countDB + 1);
        else
          performanceControlMenu.ToolTipText = Strings.UnavailableRetryTip(countDB + 1, tryTimes, maxRetry);
        if (countDB == 0) {
          ChangeDBState(false);

          float[] limits = GetGpuPowerLimits();   // limits[0] = Current, limits[1] = Max
          // 检查显卡当前功耗限制，离电时当作解锁成功
          if (powerOnline && Math.Abs(limits[1] - limits[0]) > 1f) {
            tryTimes++;
            // 失败时重试maxRetry次
            if (tryTimes > maxRetry) {
              tryTimes = 0;
              if (CPUPower > CPULimitDB + 10)
                MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.DbUnlockCpuHighWarning, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
              else
                MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.DbUnlockFailed(limits[0]), Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
              ChangeDBState(true);
              DBVersion = 2;
              countDB = 0;
              performanceControlMenu.Enabled = true;
              performanceControlMenu.ToolTipText = "";
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", Strings.DbNormal);
            } else {
              countDB = countDBInit;
              // 启用DB驱动
              ChangeDBState(true);
              SetGpuPowerState(true, true);
            }
          } else {
            tryTimes = 0;
            performanceControlMenu.Enabled = true;
            performanceControlMenu.ToolTipText = "";
            if (autoStart == "off") {
              MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.DbUnlockSuccessNoAutoStart, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
          if (isCPUPowerControlSupported) SetCpuPowerLimit((byte)CPULimitDB);
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
            fanText = ((fanSpeedNow[0] + fanSpeedNow[1]) * 50).ToString();
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
      if (autoFanProtect == "on" && platformMaxFanSpeed.HasValue && (monitorCPU || monitorGPU) && smoothedCPUTemp > currentMaxCPUTemp - 2 && fanControl.Contains(" RPM")) {
        // 检查是否满足转速低于平台最大转速80%的条件
        bool fanSpeedCondition = true;
        if (platformMaxFanSpeed.Value > 0) {
          int currentFanSpeed;
          lock (fanSpeedNow) { currentFanSpeed = (fanSpeedNow[0] + fanSpeedNow[1]) * 50; }
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
      ////自动关闭GPU监控
      //if (countQuery > 5 && autoStopMonitorGPU && !isConnectedToNVIDIA && monitorGPU && ((GPUPower >= 0 && GPUPower <= 1.3) || !getGPU)) {
      //  // 如果是NVIDIAGpu平台，进一步检查是否有程序占用GPU
      //  bool isGpuIdle = true;
      //  if (hasNVIDIAGpu) {
      //    var gpuApps = GetGpuApps();
      //    if (gpuApps != null && gpuApps.Count > 0) {
      //      isGpuIdle = false;
      //    }
      //  }

      //  if (isGpuIdle) {
      //    GPUPower = 0;
      //    rawPowerGPU = 0f;
      //    getGPU = false;
      //    hasStopAuto = true;
      //    countQuery = 0;
      //    monitorGPU = false;
      //    gpuTempReady = false; // 关闭后温度不再有效
      //                          //重置自动开启标志
      //    hasStartAuto = false;
      //    autoStartMonitorGPU = true;
      //    SetGpuMonitorState(false);
      //    UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOff);
      //    SaveConfig("MonitorGPU");

      //    // 设置通知的文本和标题
      //    trayIcon.BalloonTipTitle = Strings.GpuAutoStopTitle;
      //    trayIcon.BalloonTipText = Strings.GpuAutoStopText;
      //    trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
      //    trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      //  }
      //}
      ////自动开启GPU监控：需为自动转速控制且从"未连接显示器"切换为"已连接"时才触发
      //if (autoStartMonitorGPU && isConnectedToNVIDIA && !prevIsConnectedToNVIDIA && !monitorGPU && fanControl == "auto") {
      //  GPUPower = 0;
      //  rawPowerGPU = 0f;
      //  hasStartAuto = true;
      //  countQuery = 0;
      //  monitorGPU = true;
      //  gpuTempReady = false; // 等待获取到温度后再参与风扇控制
      //  //重置自动关闭标志
      //  hasStopAuto = false;
      //  autoStopMonitorGPU = true;
      //  SetGpuMonitorState(true);
      //  UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOn);
      //  SaveConfig("MonitorGPU");

      //  // 设置通知的文本和标题
      //  trayIcon.BalloonTipTitle = Strings.GpuAutoStopTitle;
      //  trayIcon.BalloonTipText = Strings.GpuAutoStartText;
      //  trayIcon.BalloonTipIcon = ToolTipIcon.Info; // 图标类型
      //  trayIcon.ShowBalloonTip(3000); // 显示气泡通知，持续时间为 3 秒
      //}

      // 似乎无法一次性关闭GPU监控及选项
      //if (!monitorGPU) {
      //  SetGpuMonitorState(false);
      //  UpdateCheckedState("monitorGPUGroup", Strings.MonitorGpuOff);
      //}

      prevIsConnectedToNVIDIA = isConnectedToNVIDIA;

      // 释放防重入标志
      Interlocked.Exchange(ref _isQuerying, 0);
    }

    // Helper function to calculate fan speed for a specific temperature map
    static int GetFanSpeedForSpecificTemperature(float temperature, SortedDictionary<float, int> tempFanMap) {
      // 字典已按键升序排列，直接线性扫描，O(n) 但 n 极小（通常 3~6 个点）
      float lowerKey = tempFanMap.Keys.First();
      float upperKey = lowerKey;

      foreach (float key in tempFanMap.Keys) {
        if (key <= temperature) lowerKey = key;
        else { upperKey = key; break; }
        upperKey = key; // 如果循环完也没 break，upper == lower == 最大键
      }

      if (lowerKey == upperKey)
        return tempFanMap[lowerKey];

      int lowerSpeed = tempFanMap[lowerKey];
      int upperSpeed = tempFanMap[upperKey];
      float interpolated = lowerSpeed + (upperSpeed - lowerSpeed) * (temperature - lowerKey) / (upperKey - lowerKey);
      return (int)interpolated;
    }

    // 根据 floatingBarScreen 获取目标显示器，找不到时回退主屏幕
    static Screen GetFloatingScreen() {
      if (!string.IsNullOrEmpty(floatingBarScreen)) {
        foreach (var s in Screen.AllScreens) {
          if (s.DeviceName == floatingBarScreen) return s;
        }
      }
      return Screen.PrimaryScreen;
    }

    static readonly object _floatingLock = new object();
    // 显示浮窗
    static void ShowFloatingForm() {
      lock (_floatingLock) {
        if (floatingForm == null || floatingForm.IsDisposed) {
          floatingForm = new FloatingForm(monitorText(), textSize, floatingBarLoc, GetFloatingScreen());
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
        floatingForm.SetText(monitorText(), textSize, floatingBarLoc, GetFloatingScreen());
      }
    }

    //生成监控信息
    static string monitorText() {
      string str = "";
      if (monitorCPU) {
        if (CPUPower > 0)
          str += $"CPU: {CPUTemp:F1}°C, {CPUPower:F1}W";
        else {
          if (pawnIOState == "RUNNING")
            str += $"CPU: {Strings.MonitorPrepareLabel}";
          else if (pawnIOState.Length > 0)
            str += $"CPU: PawnIO {pawnIOState}";
        }
      }
      if (monitorGPU) {
        if (str.Length > 0) str += "\n";
        if (pawnIOState == "RUNNING" && !gpuTempReady) {
          if (rawPowerGPU < 0)
            str += $"GPU: {Strings.GpuPoweredOff}";
          else
            str += $"GPU: {Strings.MonitorPrepareLabel}";
        } else if (pawnIOState.Length > 0)
          str += $"GPU: {GPUTemp:F1}°C, {GPUPower:F1}W";
      }
      if (monitorFan) {
        if (str.Length > 0) str += "\n";
        if (Is3FanNb)
          str += $"Fan:  {fanSpeedNow[0] * 100}, {fanSpeedNow[1] * 100}, {fanSpeedNow[2] * 100}";
        else
          str += $"Fan:  {fanSpeedNow[0] * 100}, {fanSpeedNow[1] * 100}";
      }
      if (str.Length == 0) str = Strings.MonitorClosed;
      return str;
    }

    static string GetPresetDisplayName(string presetKey) {
      switch (presetKey) {
        case "PresetExtreme": return Strings.PresetExtreme;
        case "PresetGpuPriority": return Strings.PresetGpuPriority;
        case "PresetLightUse": return Strings.PresetLightUse;
        case "PresetCustom1": return presetCustom1Name;
        case "PresetCustom2": return presetCustom2Name;
        case "PresetCustom3": return presetCustom3Name;
        default: return presetKey;
      }
    }

    static string GetCurrentPresetDisplayName() {
      return GetPresetDisplayName(currentPreset);
    }

    static void UpdateTrayIconText() {
      if (trayIcon == null) return;

      string text = "";
      string presetName = GetCurrentPresetDisplayName();
      string monitor = monitorText();
      if (Strings.ActivePreset.Length + presetName.Length + 1 + monitor.Length <= 63) {
        text = $"{Strings.ActivePreset + presetName}\n{monitor}";
      } else if (presetName.Length + 1 + monitor.Length <= 63) {
        text = presetName + "\n" + monitor;
      } else {
        text = monitor;
      }

      const int notifyIconTextLimit = 63;
      if (text.Length > notifyIconTextLimit)
        text = text.Substring(0, notifyIconTextLimit);

      trayIcon.Text = text;
    }

    static void Exit() {
      _pipeCts?.Cancel();
      if (OmenKeyActions.UsesPipe(omenKey)) {
        OmenKeyOff();
      }
      UninstallTrayScrollHook(); // 卸载鼠标钩子
      tooltipUpdateTimer.Stop(); // 停止定时器

      //openComputer.Close();
      StopHardwareMonitor();
      Application.Exit();
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Logger.Error($"CurrentDomain_UnhandledException: {e.ExceptionObject}");
      MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.CrashMessage);
    }

    static void Application_ThreadException(object sender, ThreadExceptionEventArgs e) {
      Logger.Error($"Application_ThreadException: {e.Exception}");
      MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.CrashMessage);
    }
  }
}
