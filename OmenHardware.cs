using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmenSuperHub {
  internal class OmenHardware {
    // 获取系统 ID (SystemID)，即主板产品号 (Win32_BaseBoard.Product)
    public static string GetSystemID() {
      try {
        using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Product FROM Win32_BaseBoard")) {
          using (ManagementObjectCollection results = searcher.Get()) {
            foreach (ManagementObject obj in results) {
              // 直接返回第一个非空的 Product 值
              object product = obj["Product"];
              if (product != null)
                return product.ToString().Trim();
            }
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"[ERROR] GetSystemID: {ex.Message}");
      }
      return string.Empty;
    }

    /// <summary>
    /// 从本机系统 ID 直接加载平台能力配置。
    /// 默认从 C:\Users\fiveb\Desktop\ 下的两个 DLL 中读取资源。
    /// </summary>
    public static PlatformSettings LoadPlatformSettingsFromDll() {
      return PlatformSettingsResolver.LoadFromCurrentSystem();
    }

    /// <summary>
    /// 仅返回 HP.Omen.Core.Model.Device.dll 中的 PerformancePlatformList 原始 JSON。
    /// </summary>
    public static string ReadPerformancePlatformListJson() {
      return PlatformSettingsResolver.ReadPerformancePlatformListJson();
    }

    // 获取系统设计数据（128字节），包含硬件能力、传感器、热策略等
    public static byte[] GetSystemDesignData() {
      return SendOmenBiosWmi(0x28, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
    }

    // 解析并输出 SystemDesignData (128字节) 的关键比特位含义
    public static void PrintSystemDesignData() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 12) {
        Console.WriteLine("[ERROR] SystemDesignData 获取失败或长度不足");
        return;
      }

      Console.WriteLine("========== System Design Data 解析 ==========");
      Console.WriteLine($"完整数据: {BitConverter.ToString(data)}");
      Console.WriteLine();

      // --- 字节 [0]-[1]: 适配器功率 (来自 PowerControlHelper) ---
      int adapterPower = data[0] | (data[1] << 8);
      Console.WriteLine($"[0]-[1] 出厂适配器额定功率 = {adapterPower} W");
      Console.WriteLine(adapterPower >= 200 ? "  → 支持 BIOS 性能模式 (IsBiosPerformanceModeSupport)" : "  → 不支持 BIOS 性能模式");
      Console.WriteLine(adapterPower >= 280 ? "  → 支持 TGP/PPAB 功能" : "  → 不支持 TGP/PPAB");
      Console.WriteLine();

      // --- 字节 [2]: 保留 ---
      Console.WriteLine($"[2] (保留) = 0x{data[2]:X2}");
      Console.WriteLine();

      // --- 字节 [3]: 热策略版本 ---
      Console.WriteLine($"[3] ThermalPolicyVersion = {data[3]}");
      Console.WriteLine(data[3] == 1 ? "  → V1 (BIOS 性能控制)" : "  → V0 (Legacy)");
      Console.WriteLine();

      // --- 字节 [4]: 平台特性标识 ---
      byte b4 = data[4];
      Console.WriteLine($"[4] 平台特性 = 0x{b4:X2} ({Convert.ToString(b4, 2).PadLeft(8, '0')})");
      Console.WriteLine($"  Bit 0 (SwFanControl)              : {(b4 & 0x01) != 0}");
      Console.WriteLine($"  Bit 1 (TurboMode/Extreme支持)     : {(b4 & 0x02) != 0}");
      Console.WriteLine($"  Bit 2 (Extreme解锁)               : {(b4 & 0x04) != 0}");
      Console.WriteLine($"  Bit 3 (BIOS掌管控温)              : {(b4 & 0x08) != 0}");
      Console.WriteLine($"  Bit 4 (TwoBytePL4Support)         : {(b4 & 0x10) != 0}");
      Console.WriteLine($"  Bit 5 (待验证)                    : {(b4 & 0x20) != 0}");
      Console.WriteLine($"  Bit 6                             : {(b4 & 0x40) != 0}");
      Console.WriteLine($"  Bit 7                             : {(b4 & 0x80) != 0}");

      // 补充上述位的业务含义
      Console.WriteLine($"  → 软件风扇控制 (Bit0)      : {((b4 & 0x01) != 0 ? "是" : "否")}");
      Console.WriteLine($"  → 支持狂暴模式 (Bit1)      : {((b4 & 0x02) != 0 ? "是" : "否")}");
      Console.WriteLine($"  → 极限模式已解锁 (Bit2)    : {((b4 & 0x04) != 0 ? "是" : "否")}");
      Console.WriteLine($"  → BIOS完全掌管控温 (Bit3)  : {((b4 & 0x08) != 0 ? "是" : "否")}");
      Console.WriteLine($"  → 双字节 PL4 支持 (Bit4)   : {((b4 & 0x10) != 0 ? "是" : "否")}");
      Console.WriteLine();

      // --- 字节 [5]: PL4 默认值 ---
      Console.WriteLine($"[5] PL4_Default = {data[5]}W (单字节模式)");
      Console.WriteLine();

      // --- 字节 [6]: 超频与硬件支持 ---
      byte b6 = data[6];
      Console.WriteLine($"[6] 超频支持 = 0x{b6:X2} ({Convert.ToString(b6, 2).PadLeft(8, '0')})");
      Console.WriteLine($"  Bit 0 (BIOSDefinedOC_AMD)    : {(b6 & 0x01) != 0}");
      Console.WriteLine($"  Bit 1                        : {(b6 & 0x02) != 0}");
      Console.WriteLine($"  Bit 2                        : {(b6 & 0x04) != 0}");
      Console.WriteLine($"  Bit 3                        : {(b6 & 0x08) != 0}");
      Console.WriteLine($"  Bit 4                        : {(b6 & 0x10) != 0}");
      Console.WriteLine($"  Bit 5                        : {(b6 & 0x20) != 0}");
      Console.WriteLine($"  Bit 6                        : {(b6 & 0x40) != 0}");
      Console.WriteLine($"  Bit 7                        : {(b6 & 0x80) != 0}");
      Console.WriteLine($"  => AMD BIOS 定义超频 (Bit0) : {((b6 & 0x01) != 0 ? "是" : "否")}");
      Console.WriteLine();

      // --- 字节 [7]: 保留/未使用 ---
      Console.WriteLine($"[7] (保留) = 0x{data[7]:X2}");
      Console.WriteLine();

      // --- 字节 [8]: Default Concurrent TDP ---
      Console.WriteLine($"[8] DefaultConcurrentTdp = {data[8]} (TPP 最小值)");
      Console.WriteLine($"  → 取自 PerformanceControlHelper.DefaultConcurrentTdp");
      Console.WriteLine();

      // --- 字节 [9]: 负载线支持级别 ---
      byte b9 = data[9];
      int loadLineLevels = b9 & 0x0F;
      int defaultLoadLine = (b9 & 0xF0) >> 4;
      Console.WriteLine($"[9] LoadLine 信息 = 0x{b9:X2} ({Convert.ToString(b9, 2).PadLeft(8, '0')})");
      Console.WriteLine($"  LoadLineSupportLevels (低4位) : {loadLineLevels}");
      Console.WriteLine($"  DefaultLoadLine (高4位)       : {defaultLoadLine}");
      Console.WriteLine();

      // --- 字节 [10]: 传感器能力 ---
      byte b10 = data[10];
      Console.WriteLine($"[10] 传感器能力 = 0x{b10:X2} ({Convert.ToString(b10, 2).PadLeft(8, '0')})");
      Console.WriteLine($"  Bit 0 (IR_Sensor)            : {(b10 & 0x01) != 0}");
      Console.WriteLine($"  Bit 1 (Ambient_Sensor)       : {(b10 & 0x02) != 0}");
      Console.WriteLine($"  Bit 2 (PCH_Sensor)           : {(b10 & 0x04) != 0}");
      Console.WriteLine($"  Bit 3 (VR_Sensor)            : {(b10 & 0x08) != 0}");
      Console.WriteLine($"  Bit 4                        : {(b10 & 0x10) != 0}");
      Console.WriteLine($"  Bit 5                        : {(b10 & 0x20) != 0}");
      Console.WriteLine($"  Bit 6                        : {(b10 & 0x40) != 0}");
      Console.WriteLine($"  Bit 7                        : {(b10 & 0x80) != 0}");
      Console.WriteLine($"  => IR 传感器 (Bit0)          : {((b10 & 0x01) != 0 ? "是" : "否")}");
      Console.WriteLine($"  => 环境传感器 (Bit1)        : {((b10 & 0x02) != 0 ? "是" : "否")}");
      Console.WriteLine($"  => PCH 过热支持 (Bit2)      : {((b10 & 0x04) != 0 ? "是" : "否")}");
      Console.WriteLine($"  => VR 传感器 (Bit3)         : {((b10 & 0x08) != 0 ? "是" : "否")}");
      Console.WriteLine();

      // --- 字节 [11]: 热键与其它功能 ---
      byte b11 = data[11];
      Console.WriteLine($"[11] 热键与功能 = 0x{b11:X2} ({Convert.ToString(b11, 2).PadLeft(8, '0')})");
      Console.WriteLine($"  Bit 0 (Hotkey_FnP)           : {(b11 & 0x01) != 0}");
      Console.WriteLine($"  Bit 1 (Hotkey_FnF1)          : {(b11 & 0x02) != 0}");
      Console.WriteLine($"  Bit 2                        : {(b11 & 0x04) != 0}");
      Console.WriteLine($"  Bit 3                        : {(b11 & 0x08) != 0}");
      Console.WriteLine($"  Bit 4                        : {(b11 & 0x10) != 0}");
      Console.WriteLine($"  Bit 5                        : {(b11 & 0x20) != 0}");
      Console.WriteLine($"  Bit 6                        : {(b11 & 0x40) != 0}");
      Console.WriteLine($"  Bit 7                        : {(b11 & 0x80) != 0}");
      Console.WriteLine($"  => Fn+P 热键 (Bit0)          : {((b11 & 0x01) != 0 ? "是" : "否")}");
      Console.WriteLine($"  => Fn+F1 热键 (Bit1)         : {((b11 & 0x02) != 0 ? "是" : "否")}");
      Console.WriteLine();

      // --- 后续字节（12-127）：可能包含 OEM 字符串、UI 数据等 ---
      Console.WriteLine("[12-127] 额外数据:");
      for (int i = 12; i < data.Length && i < 32; i++)  // 仅打印前 32 字节以减少输出
      {
        Console.Write($"0x{data[i]:X2} ");
      }
      Console.WriteLine(Environment.NewLine);
      Console.WriteLine("=============================================");
    }

    public static bool IsLoadLineSupported() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 10)
        return false;

      byte b9 = data[9];
      int levels = b9 & 0x0F;           // 低 4 位：支持的级别数
      int defaultLL = (b9 >> 4) & 0x0F; // 高 4 位：默认级别

      return levels > 0 && defaultLL > 0;
    }

    public static int GetLoadLineSupportLevels() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 10)
        return 0;

      return data[9] & 0x0F;
    }

    // 设置 LoadLine（负载线校准）级别,level 取值范围取决于平台，通常为 1 ~ LoadLineSupportLevels
    public static void SetLoadLine(int level) {
      byte[] inputData = new byte[128];
      inputData[0] = 0;
      inputData[1] = 13;        // 子命令：LoadLine 操作
      inputData[2] = (byte)level;
      SendOmenBiosWmi(0x37, inputData, 0); // 0x37 = commandType 55
    }

    // 获取当前 LoadLine 级别
    public static int GetLoadLine() {
      byte[] inputData = new byte[4];
      inputData[0] = 0;
      inputData[1] = 13;        // 子命令：LoadLine 读取
      byte[] result = SendOmenBiosWmi(0x37, inputData, 4);
      if (result != null && result.Length > 2) {
        return result[2];     // 返回值第3字节为当前级别
      }
      return -1;                // 失败或无支持返回 -1
    }

    // 通过 WMI 设置 IccMax（CPU 电流限制，单位安培）
    public static bool SetIccMaxByWmi(decimal iccMaxAmpere) {
      byte[] inputData = new byte[128];
      inputData[0] = 0;
      inputData[1] = 15;        // 子命令：IccMax 操作
      inputData[2] = (byte)((int)iccMaxAmpere & 0xFF);
      inputData[3] = (byte)(((int)iccMaxAmpere >> 8) & 0xFF);
      // 写操作，outputSize=0 表示不关心返回数据（官方亦如此）
      SendOmenBiosWmi(0x37, inputData, 0);
      // 官方通过 BiosWmiCmd_SetSync 的返回值判断，此处简化处理，认为无异常即成功
      return true;
    }

    /// <param name="ocp">输出：是否触发过流保护 (Bit 0)</param>
    /// <param name="otp">输出：是否触发过温保护 (Bit 1)</param>
    /// <returns>true：成功读取并解析；false：WMI 通信失败</returns>
    public static bool GetFanCount(out bool ocp, out bool otp) {
      ocp = false;
      otp = false;

      byte[] result = SendOmenBiosWmi(0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);

      if (result == null || result.Length < 2)
        return false;   // 通信失败或返回数据不足

      byte protectionByte = result[1];
      ocp = (protectionByte & 0x01) != 0;    // Bit 0 : OCP
      otp = (protectionByte & 0x02) != 0;    // Bit 1 : OTP

      return true;
    }

    public static List<int> GetFanLevel() {
      // Send command to retrieve fan speed
      List<int> fanSpeedNow = new List<int> { 0, 0 };
      byte[] fanLevel = SendOmenBiosWmi(0x2D, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
      if (fanLevel != null) {
        fanSpeedNow[0] = fanLevel[0];
        fanSpeedNow[1] = fanLevel[1];
        //Console.WriteLine("GetFanLevel: " + level * 100);
      }
      return fanSpeedNow;
    }

    public static byte[] GetFanTable() {
      // 0x19-0x34?
      return SendOmenBiosWmi(0x2F, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
    }

    public static void SetFanLevel(int fanSpeed1, int fanSpeed2) {
      SendOmenBiosWmi(0x2E, new byte[] { (byte)fanSpeed1, (byte)fanSpeed2 }, 0);
      //Console.WriteLine("SetFanLevel: " + fanSpeed * 100);
    }

    //mode为0x31代表狂暴模式，0x30代表Eco/平衡模式，0x04代表极限/大师模式
    public static void SetFanMode(byte mode) {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, mode }, 0);
    }

    public static void SetMaxGpuPower() {
      SendOmenBiosWmi(0x22, new byte[] { 0x01, 0x01, 0x01, 0x00 }, 0);
    }

    public static void SetMedGpuPower() {
      SendOmenBiosWmi(0x22, new byte[] { 0x01, 0x00, 0x01, 0x00 }, 0);
    }

    public static void SetMinGpuPower() {
      SendOmenBiosWmi(0x22, new byte[] { 0x00, 0x00, 0x01, 0x00 }, 0);
    }

    // Tpp设置，这里似乎无作用
    public static void SetConcurrentTdp(byte value) {
      SendOmenBiosWmi(0x29, new byte[] { 0xFF, 0xFF, 0xFF, value }, 0);
    }

    // PL2和PL1，立即生效，狂暴平衡都生效，直接对应功率W，1-254，需关闭ts，再点击狂暴模式失效
    public static void SetCpuPowerLimit(byte value) {
      SendOmenBiosWmi(0x29, new byte[] { value, value, 0xFF, 0xFF }, 0);
      //Console.WriteLine("SetCpuPowerLimit: " + value);
    }

    // PL4，狂暴平衡都生效，50-19，100-54，180-106，200-122，需关闭ts，1-254，和SetCpuPowerLimit优先级相同
    public static void SetCpuPowerLimit4(byte value) {
      SendOmenBiosWmi(0x29, new byte[] { 0xFF, 0xFF, value, 0xFF }, 0);
    }

    public static bool IsTwoBytePL4Supported() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 5) {
        Console.WriteLine("[ERROR] 无法获取 SystemDesignData");
        return false;
      }

      // data[4] 的 Bit4 (掩码 0x10) 就是 TwoBytePL4Support 标志
      return (data[4] & 0x10) != 0;
    }

    public static void SetPL4DoubleByte(ushort pl4Value) {
      byte[] data = new byte[128];
      data[0] = 0x20;                         // 固定标识
      data[2] = (byte)(pl4Value & 0xFF);       // PL4 低字节
      data[3] = (byte)((pl4Value >> 8) & 0xFF);// PL4 高字节
                                               // 其余保留位用默认值
      data[6] = 0xFF; data[7] = 0xFF;
      data[10] = 0xFF; data[11] = 0xFF;

      SendOmenBiosWmi(0x37, data, 0); // commandType = 55 = 0x37
    }

    public static void SetMaxFanSpeedOn() {
      SendOmenBiosWmi(0x27, new byte[] { 0x01 }, 0);
    }

    public static void SetMaxFanSpeedOff() {
      SendOmenBiosWmi(0x27, new byte[] { 0x00 }, 0);
    }

    public static void BacklightOn() {
      SendOmenBiosWmi(0x05, new byte[] { 0xE4 }, 0, 0x20009);
    }

    public static void BacklightOff() {
      SendOmenBiosWmi(0x05, new byte[] { 0x64 }, 0, 0x20009);
    }

    public static void SetlightColor() {
      byte[] dataIn = new byte[128];
      dataIn[0] = 0x03;
      for (int i = 25; i <= 36; i++)
        dataIn[i] = 0x80;
      SendOmenBiosWmi(0x03, dataIn, 0, 0x20009);
    }

    //// 似乎没有作用，且不支持AMD
    //public static void InitializeIntelOC() {
    //  string outputData = SendOmenBiosWmi(0x35, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
    //  //Console.WriteLine("+ OK: " + outputData);
    //}

    //// 可导致直接黑屏
    //public static void SetVoltageOffset(int volOff) {
    //  byte[] dataIn = new byte[128];
    //  dataIn[0] = 0x00;
    //  dataIn[1] = 0x03;
    //  dataIn[2] = (byte)(volOff < 0 ? 0 : 1);
    //  dataIn[3] = (byte)(Math.Abs(volOff) / 256);
    //  dataIn[4] = (byte)(Math.Abs(volOff) % 256);
    //  string outputData = SendOmenBiosWmi(0x37, dataIn, 4);
    //  Console.WriteLine("+ OK: " + outputData);
    //}

    public static ManagementObjectSearcher searcher;
    public static ManagementObject biosMethods;
    public static byte[] SendOmenBiosWmi(uint commandType, byte[] data, int outputSize, uint command = 0x20008) {
      const string namespaceName = @"root\wmi";
      const string className = "hpqBIntM";
      string methodName = "hpqBIOSInt" + outputSize.ToString(); // Change here
      byte[] sign = { 0x53, 0x45, 0x43, 0x55 };

      // Prepare the request
      using (var biosDataIn = new ManagementClass(namespaceName, "hpqBDataIn", null).CreateInstance()) {
        biosDataIn["Command"] = command;
        biosDataIn["CommandType"] = commandType;
        biosDataIn["Sign"] = sign;
        if (data != null) {
          biosDataIn["hpqBData"] = data;
          biosDataIn["Size"] = (uint)data.Length;
        } else {
          biosDataIn["Size"] = (uint)0;
        }

        // Obtain BIOS method class instance
        if (searcher == null)
          searcher = new ManagementObjectSearcher(namespaceName, $"SELECT * FROM {className}");
        if (biosMethods == null)
          biosMethods = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

        // Make a call to write to the BIOS
        var inParams = biosMethods.GetMethodParameters(methodName); // Change here
        inParams["InData"] = biosDataIn;

        var result = biosMethods.InvokeMethod(methodName, inParams, null); // Change here
        var outData = result["OutData"] as ManagementBaseObject;
        uint returnCode = (uint)outData["rwReturnCode"];

        if (returnCode == 0) {
          // If operation completed successfully
          if (outputSize != 0) {
            var outputData = (byte[])outData["Data"];
            // Console.WriteLine("+ OK: " + BitConverter.ToString(outputData));
            return (byte[])outData["Data"];
          } else {
            // Console.WriteLine("+ OK");
          }
        } else {
          Console.WriteLine("- Failed: Error " + returnCode);
          switch (returnCode) {
            case 0x03:
              Console.WriteLine(" - Command Not Available");
              break;
            case 0x05:
              Console.WriteLine(" - Input or Output Size Too Small");
              break;
          }
        }
      }

      return null;
    }

    public static void OmenKeyOff() {
      const string namespaceName = @"root\subscription";
      var scope = new ManagementScope(namespaceName);

      try {
        scope.Connect();

        var query = new ObjectQuery("SELECT * FROM __EventFilter WHERE Name='OmenKeyFilter'");
        var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get()) {
          mo.Delete();
        }

        query = new ObjectQuery("SELECT * FROM CommandLineEventConsumer WHERE Name='OmenKeyConsumer'");
        searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get()) {
          mo.Delete();
        }

        query = new ObjectQuery("SELECT * FROM __FilterToConsumerBinding WHERE Filter='__EventFilter.Name=\"OmenKeyFilter\"'");
        searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get()) {
          mo.Delete();
        }

        //Console.WriteLine("Omen Key Off completed successfully.");
      } catch (Exception ex) {
        Console.WriteLine("Error: " + ex.Message);
      }
    }

    public static void OmenKeyOn(string method) {
      const string namespaceName = @"root\subscription";
      var scope = new ManagementScope(namespaceName);

      try {
        scope.Connect();

        // Create CommandLineEventConsumer
        var consumerClass = new ManagementClass(scope, new ManagementPath("CommandLineEventConsumer"), null);
        var consumer = consumerClass.CreateInstance();
        string currentPath = AppDomain.CurrentDomain.BaseDirectory;
        if (method == "custom") {
          consumer["CommandLineTemplate"] = @"cmd /c echo OmenKeyTriggered > \\.\pipe\OmenSuperHubPipe";
        } else {
          consumer["CommandLineTemplate"] = @"C:\Windows\System32\schtasks.exe /run /tn ""Omen Key""";
        }
        consumer["Name"] = "OmenKeyConsumer";
        consumer.Put();

        // Create EventFilter
        var filterClass = new ManagementClass(scope, new ManagementPath("__EventFilter"), null);
        var filter = filterClass.CreateInstance();
        filter["EventNameSpace"] = @"root\wmi";
        filter["Name"] = "OmenKeyFilter";
        filter["Query"] = "SELECT * FROM hpqBEvnt WHERE eventData = 8613 AND eventId = 29";
        filter["QueryLanguage"] = "WQL";
        filter.Put();

        // Create FilterToConsumerBinding
        var bindingClass = new ManagementClass(scope, new ManagementPath("__FilterToConsumerBinding"), null);
        var binding = bindingClass.CreateInstance();
        binding["Consumer"] = new ManagementPath(@"root\subscription:CommandLineEventConsumer.Name='OmenKeyConsumer'");
        binding["Filter"] = new ManagementPath(@"root\subscription:__EventFilter.Name='OmenKeyFilter'");
        binding.Put();

        //Console.WriteLine("Omen Key On completed successfully.");
      } catch (Exception ex) {
        Console.WriteLine("Error: " + ex.Message);
      }
    }
  }
}
