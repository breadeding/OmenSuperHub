using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;

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

    // 提取适配器功率
    public static int GetAdapterPower() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 2) {
        return -1;
      }
      return data[0] | (data[1] << 8);
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

      // --- 字节 [7]: 图形模式支持标记 ---
      Console.WriteLine($"[7] 图形模式支持标记 = 0x{data[7]:X2}");
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

      // --- 字节 [10]: 传感器能力，但是为0也可能能获取传感器值 ---
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
      Console.WriteLine($"  => PCH 传感器 (Bit2)      : {((b10 & 0x04) != 0 ? "是" : "否")}");
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

    public enum GraphicsMode {
      Hybrid = 0,    // 混合模式
      Discrete = 1,  // 独显直连
      Optimus = 2,   // Optimus
      UMA = 3        // 仅核显
    }

    /// <summary>
    /// 获取当前显卡模式（指BIOS中的设置）
    /// </summary>
    public static GraphicsMode GetGfxMode() {
      byte[] result = SendOmenBiosWmi(82, new byte[4] { 0, 0, 0, 0 }, 4, 1);
      if (result != null && result.Length > 0) {
        int modeValue = result[0] & 0x7F; // 忽略最高位（可能是状态位）
        if (modeValue >= 0 && modeValue <= 3)
          return (GraphicsMode)modeValue;
      }
      return GraphicsMode.Hybrid; // 默认返回混合模式
    }

    /// <summary>
    /// 检查支持的模式（若需要）
    /// 优先从系统设计数据字节[7]获取，失败时回退到旧版检测
    /// </summary>
    public static byte GetSupportedGfxModes() {
      // 尝试从系统设计数据获取
      byte[] designData = GetSystemDesignData();
      if (designData != null && designData.Length > 7 && designData[7] != 0)
        return designData[7];

      // 回退到旧版检测：读取命令1, 82的返回码
      byte[] result = SendOmenBiosWmi(82, null, 4, 1);
      if (result != null && result.Length > 0) {
        int code = result[0];
        if (code != 3 && code != 4) // 3=无效命令, 4=无效命令类型
          return 6; // 6 = 支持 legacy 模式
      }
      return 0; // 不支持
    }

    public static void PrintSupportedGfxModes(byte supported) {
      Console.WriteLine($"支持的模式标志：0x{supported:X2}");

      if (supported == 0) {
        Console.WriteLine("  → 不支持任何显卡切换模式");
        return;
      }

      var modes = new List<string>();
      if ((supported & 1) != 0) modes.Add("UMA（仅集显）");
      if ((supported & 2) != 0) modes.Add("Hybrid（混合输出/Optimus）");
      if ((supported & 4) != 0) modes.Add("Discrete（独显直连）");
      if ((supported & 8) != 0) modes.Add("DDS（动态切换/Advanced Optimus）");

      Console.WriteLine("  → 支持的模式：");
      foreach (var m in modes)
        Console.WriteLine($"     - {m}");

      // 用传统 switch 语句替代 switch 表达式
      string constantName = null;
      switch (supported) {
        case 0:
          constantName = "SupportMode_None";
          break;
        case 1:
          constantName = "SupportMode_UMA";
          break;
        case 2:
          constantName = "SupportMode_Hybrid";
          break;
        case 3:
          constantName = "SupportMode_HybridUMA";
          break;
        case 4:
          constantName = "SupportMode_Discrete";
          break;
        case 5:
          constantName = "SupportMode_DiscrerteUMA";
          break;
        case 6:
          constantName = "SupportMode_Legacy";
          break;
        case 8:
          constantName = "SupportMode_DDS";
          break;
          // 其他组合没有对应常量，保持 null
      }

      if (constantName != null)
        Console.WriteLine($"  → 对应常量：{constantName}");
      else
        Console.WriteLine("  （此为组合模式，无对应单一常量）");
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
    public static void SetIccMaxByWmi(decimal iccMaxAmpere) {
      byte[] inputData = new byte[128];
      inputData[0] = 0;
      inputData[1] = 15;        // 子命令：IccMax 操作
      inputData[2] = (byte)((int)iccMaxAmpere & 0xFF);
      inputData[3] = (byte)(((int)iccMaxAmpere >> 8) & 0xFF);
      SendOmenBiosWmi(0x37, inputData, 0);
    }

    /// <summary>
    /// 根据传感器索引获取温度（摄氏度）
    /// </summary>
    /// <param name="sensorIndex">
    /// 传感器索引：
    /// 0 - IR 传感器（主板/系统内部温度，部分新机型会映射为环境传感器）
    /// 1 - 环境传感器（机箱内空气温度）
    /// 2 - PCH 芯片温度
    /// 3 - VR（电压调节模块）温度
    /// </param>
    /// <returns>温度值（℃），失败或传感器无效时返回 -1</returns>
    public static int GetSensorTemperature(byte sensorIndex) {
      byte[] input = new byte[4];
      input[0] = sensorIndex;   // 其余字节自动为 0

      // commandType = 35 (0x23), 返回 4 字节
      byte[] result = SendOmenBiosWmi(0x23, input, 4);

      if (result != null && result.Length > 0) {
        return result[0];
      }

      return -1;
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

    /// <summary>读取指定分区的灯光颜色</summary>
    /// <param name="zone">分区编号 (1-4)</param>
    public static Color GetLightZoneColor(int zone) {
      byte[] input = new byte[4];
      input[0] = (byte)zone;
      byte[] result = SendOmenBiosWmi(0x04, input, 4, 0x20009);
      if (result != null && result.Length >= 3)
        return Color.FromRgb(result[0], result[1], result[2]);
      return Colors.Black;
    }

    /// <summary>设置动画效果（使用默认参数）</summary>
    public static int GetCurrentAnimationEffect() {
      byte[] input = new byte[4];
      byte[] result = SendOmenBiosWmi(0x0C, input, 4, 0x20009); // commandType=12
      if (result == null || result.Length < 1)
        return -1;
      return result[0];  // 效果编号，如 2=COLOR_CYCLE, 8=AUDIO_PULSE 等
    }

    /// <summary>
    /// 设置键盘灯光动画效果
    /// </summary>
    /// <param name="device">设备类型：1=LightBar, 2=Keyboard</param>
    /// <param name="effectId">效果编号：2=COLOR_CYCLE颜色循环, 3=STARLIGHT星光, 4=BREATHING呼吸, 6=WAVE波浪,
    /// 7=RAINDROP雨滴, 8=AUDIO_PULSE音频脉冲, 9=CONFETTI五彩纸屑, 10=SUN太阳, 11=SWIPE划过</param>
    /// <param name="brightness">亮度 0-100</param>
    /// <param name="speed">速度：0=慢, 1=中, 2=快</param>
    /// <param name="direction">方向：0=左, 1=右</param>
    /// <param name="theme">主题：0=银河, 1=火山, 2=丛林, 3=海洋, 4=自定义</param>
    /// <param name="customColors">自定义颜色列表（仅 theme=4 时有效，最多 4 个颜色）</param>
    public static void SetLightAnimation(
        byte device,
        int effectId,
        byte brightness = 100,
        byte speed = 1,
        byte direction = 0,
        byte theme = 0,
        List<Color> customColors = null) {
      byte[] data = new byte[128];

      // [0] 设备类型
      data[0] = device;

      // [1] 效果编号
      data[1] = (byte)effectId;

      // [2] 速度 + 方向 + 主题（严格遵照官方位域编码）
      // Bit0-1: 速度 (0=慢,1=中,2=快)
      // Bit2-3: 方向 (4=左,8=右)
      // Bit4-7: 主题 (0x10=银河,0x20=火山,0x30=丛林,0x40=海洋,0x50=自定义)
      data[2] &= 0xFC;  // 清除速度位
      switch (speed) {
        case 0: /* slow, 值=0 */ break;
        case 1: data[2]++; break;      // medium = 1
        case 2: data[2] += 2; break;   // fast = 2
      }

      data[2] &= 0xF3;  // 清除方向位
      switch (direction) {
        case 0: data[2] += 4; break;   // LEFT = 4
        case 1: data[2] += 8; break;   // RIGHT = 8
      }

      data[2] &= 0x0F;  // 清除主题位，保留低4位
      switch (theme) {
        case 0: data[2] += 0x10; break; // GALAXY
        case 1: data[2] += 0x20; break; // VOLCANO
        case 2: data[2] += 0x30; break; // JUNGLE
        case 3: data[2] += 0x40; break; // OCEAN
        case 4: data[2] += 0x50; break; // CUSTOM
      }

      // [3] 亮度
      data[3] = brightness;

      // [4]-[5] 保留为 0 (tribe 和 bass，音频律动用)
      // data[4] = 0;
      // data[5] = 0;

      // [6]-[18] 自定义颜色 (仅 CUSTOM 主题时填充)
      if (theme == 4 && customColors != null && customColors.Count > 0) {
        data[6] = (byte)customColors.Count;  // 颜色数量
        for (int i = 0; i < Math.Min(customColors.Count, 4); i++) {
          data[7 + i * 3] = customColors[i].R;
          data[8 + i * 3] = customColors[i].G;
          data[9 + i * 3] = customColors[i].B;
        }
      }

      SendOmenBiosWmi(0x0B, data, 0, 0x20009);
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
