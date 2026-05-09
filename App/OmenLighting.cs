using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
//using Hp.Bridge.Client.SDKs.McuSDK2;
//using Hp.Bridge.Client.SDKs.McuSDK2.Common.DataStructure;
//using Hp.Bridge.Client.SDKs.McuSDK2.Common.Enums;
//using Hp.Bridge.Client.SDKs.McuSDK2.General.Enums;
//using Hp.Bridge.Client.SDKs.McuSDK2.General.Enums.Lighting;
//using Hp.Bridge.Client.SDKs.McuSDK2.Keyboard;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  /// <summary>
  /// 提供 OMEN 笔记本键盘灯光控制的全部功能。
  /// 支持：
  ///   - 四区/单区键盘（通过 WMI 命令 0x20009 / CommandType=11 或 7）
  ///   - 每键 RGB 键盘（通过 HID / McuGeneralHelper 直通 MCU）
  ///   - 侧面灯条（LightBar，同样通过 WMI 0x20009 / CommandType=11 或 7）
  /// 所有平台识别均通过 <see cref="PlatformSettings"/> 自动完成，无硬编码 SSID。
  /// </summary>
  internal class OmenLighting {
    // ─── 静态构造函数：处理 Libs 目录下的 DLL 加载路径 ─────────────
    static OmenLighting() {
      AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
      {
        string assemblyName = new AssemblyName(args.Name).Name + ".dll";
        string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", assemblyName);
        if (File.Exists(libPath))
          return Assembly.LoadFrom(libPath);
        return null;
      };
    }

    // ─── 键盘类型枚举 ─────────────────────────────────────────────
    /// <summary>键盘灯光分区类型</summary>
    public enum NbKeyboardLightingType : byte {
      OneZoneWithoutNumpad = 0,
      OneZoneWithNumpad = 1,
      FourZoneWithoutNumpad = 2,
      FourZoneWithNumpad = 3,
      RgbPerKey = 4,
      NotSupported = 255
    }

    /// <summary>
    /// 将键盘分区类型转换为中文说明。
    /// </summary>
    /// <param name="type">键盘类型</param>
    /// <returns>中文名称字符串</returns>
    public static string GetKeyboardTypeName(NbKeyboardLightingType type) {
      switch (type) {
        case NbKeyboardLightingType.OneZoneWithoutNumpad: return "单分区无小键盘";
        case NbKeyboardLightingType.OneZoneWithNumpad: return "单分区带小键盘";
        case NbKeyboardLightingType.FourZoneWithoutNumpad: return "四分区无小键盘";
        case NbKeyboardLightingType.FourZoneWithNumpad: return "四分区带小键盘";
        case NbKeyboardLightingType.RgbPerKey: return "单键 RGB";
        default: return "未知或不支持";
      }
    }

    // ─── 目标设备枚举（用于 WMI 命令） ──────────────────────────────
    private enum TargetDevice : byte {
      LightBar = 1,
      FourZoneAni = 2      // 键盘（四区/单区均使用此值）
    }

    // ─── WMI 协议枚举 ─────────────────────────────────────────────
    private enum WmiProtocol {
      Unknown,
      Dojo,       // commandType=11, 固定128字节结构
      Drax,       // commandType=7, 变长结构，与Noctali相似但关闭命令不同
      Noctali     // commandType=7, 变长结构，关闭命令特殊 [0, 0xFF]
    }

    // ─── WMI 常量 ─────────────────────────────────────────────────
    private const int WMI_COMMAND_ID = 131081; // 0x20009

    // ─── 颜色缓存：记录每个设备最后一次设置的颜色，用于亮度调整等操作 ────
    private static readonly Dictionary<LightingDevice, List<System.Windows.Media.Color>> _lastDeviceColors =
        new Dictionary<LightingDevice, List<System.Windows.Media.Color>>();

    // ─── 通过 WMI 获取键盘类型 ──────────────────────────────────────
    /// <summary>
    /// 获取键盘硬件灯光类型。
    /// 发送 WMI 命令 0x20008 / commandType=43，返回一个字节表示分区类型。
    /// </summary>
    /// <returns>键盘类型枚举值；失败时返回 <see cref="NbKeyboardLightingType.NotSupported"/>。</returns>
    public static NbKeyboardLightingType GetKeyboardType() {
      byte[] result = SendOmenBiosWmi(
          commandType: 0x2B,
          data: new byte[4] { 0, 0, 0, 0 },
          outputSize: 4,
          command: 0x20008);
      if (result != null && result.Length > 0)
        return (NbKeyboardLightingType)result[0];
      return NbKeyboardLightingType.NotSupported;
    }

    /// <summary>
    /// 当前键盘是否为每键 RGB 类型。
    /// </summary>
    public static bool IsPerKeyRgb => GetKeyboardType() >= NbKeyboardLightingType.RgbPerKey;

    // ─── 平台识别辅助 ──────────────────────────────────────────────
    /// <summary>
    /// 从已加载的 <see cref="PlatformSettings"/> 中获取当前系统的平台族（如 "Dojo", "Starmade" 等）。
    /// 该方法完全复用您项目中的 <see cref="PlatformSettingsResolver.LoadFromCurrentSystem"/>，无硬编码。
    /// </summary>
    /// <returns>平台族名称；如果无法获取，返回空字符串。</returns>
    public static string GetPlatformFamily() {
      var settings = PlatformSettingsResolver.LoadFromCurrentSystem();
      return settings?.PlatformFamily ?? "";
    }

    /// <summary>
    /// 根据平台族判断当前机型使用的 WMI 灯光协议。
    /// </summary>
    private static WmiProtocol DetectWmiProtocol() {
      string family = GetPlatformFamily().ToLowerInvariant();

      if (family == "dojo" || family == "vibrance" || family == "starmade" || family == "modena")
        return WmiProtocol.Dojo;

      if (family == "drax")
        return WmiProtocol.Drax;

      if (family == "noctali")
        return WmiProtocol.Noctali;

      // 未识别平台，回退为 Dojo 协议（多数新机型）
      return WmiProtocol.Dojo;
    }

    //// ─── HID 设备打开（每键 RGB 键盘专用） ──────────────────────────
    ///// <summary>
    ///// 同步打开指定 PID/VID 的 HID 设备，返回设备句柄。
    ///// </summary>
    ///// <param name="pid">产品 ID（Product ID）</param>
    ///// <param name="vid">供应商 ID（Vendor ID）</param>
    ///// <param name="interfaceString">可选接口字符串，例如 "mi_03"、"mi_02"，用于区分同一 VID/PID 下的不同接口。</param>
    ///// <returns>正数设备句柄（>0）；失败返回 -1。</returns>
    //public static int OpenHidDevice(int pid, int vid, string interfaceString = "") {
    //  try {
    //    Task<int> task = McuGeneralHelper.OpenDevice(pid, vid, interfaceString, "");
    //    task.Wait();
    //    return task.Result;
    //  } catch (Exception ex) {
    //    System.Diagnostics.Debug.WriteLine($"[OmenLighting] OpenHidDevice failed: {ex.Message}");
    //    return -1;
    //  }
    //}

    ///// <summary>
    ///// 关闭 HID 设备句柄。
    ///// </summary>
    ///// <param name="handle">由 <see cref="OpenPerKeyKeyboard"/> 返回的句柄。</param>
    ///// <returns>true 表示成功关闭；false 表示失败。</returns>
    //public static async Task<bool> CloseDeviceAsync(int handle) {
    //  return await McuGeneralHelper.CloseDevice(handle);
    //}

    ///// <summary>
    ///// 根据当前平台族自动选择正确的 PID/VID 并打开每键 RGB 键盘。
    ///// 平台识别完全依赖 <see cref="GetPlatformFamily"/>，无硬编码 SSID。
    ///// </summary>
    ///// <returns>设备句柄（>0）；如果非每键 RGB 或无法匹配平台，返回 -1。</returns>
    //public static int OpenPerKeyKeyboard() {
    //  if (!IsPerKeyRgb)
    //    return -1;

    //  string family = GetPlatformFamily().ToLowerInvariant();

    //  // Dojo / Vibrance
    //  if (family == "dojo" || family == "vibrance") {
    //    int handle = OpenHidDevice(21695, 3426, "mi_03");
    //    if (handle > 0) return handle;
    //    return OpenHidDevice(12479, 3426, "mi_03");
    //  }
    //  // Starmade
    //  if (family == "starmade") {
    //    return OpenHidDevice(20171, 1121, "mi_02");
    //  }
    //  // Modena
    //  if (family == "modena") {
    //    return OpenHidDevice(8760, 8137, "mi_02");
    //  }
    //  // 后续可继续添加其他每键 RGB 平台

    //  return -1;
    //}

    //// ════════════ 每键 RGB 控制（HID / MCU 通道） ════════════════════

    ///// <summary>
    ///// 设置每键 RGB 键盘的静态颜色。
    ///// </summary>
    ///// <param name="handle">设备句柄，由 <see cref="OpenPerKeyKeyboard"/> 返回。</param>
    ///// <param name="r">每个键的红色分量数组，长度必须与键盘物理键数一致（参见对应平台的键位 JSON 文件）。</param>
    ///// <param name="g">每个键的绿色分量数组，长度同 r。</param>
    ///// <param name="b">每个键的蓝色分量数组，长度同 r。</param>
    ///// <returns>true 表示设置成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyStaticColor(int handle, byte[] r, byte[] g, byte[] b) {
    //  return await McuGeneralHelper.SetKeyboardStaticLighting(handle, r, g, b);
    //}

    ///// <summary>
    ///// 设置每键 RGB 键盘的动画效果。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <param name="setting">动画效果配置，类型为 <see cref="LightingSetting"/>。请使用相应的 EffectCommandTable 映射效果 ID 到 Effect 字节。</param>
    ///// <returns>true 表示设置成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyAnimation(int handle, LightingSetting setting) {
    //  return await McuGeneralHelper.SetLightingEffect(handle, setting, LightingEffectTarget.ALL_LED_AREA);
    //}

    ///// <summary>
    ///// 设置每键 RGB 键盘的音乐律动（音频脉冲）动画。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <param name="setting">音频动画参数，<see cref="LightingAudioEffectSetting"/> 结构体。</param>
    ///// <returns>true 表示设置成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyAudioAnimation(int handle, LightingAudioEffectSetting setting) {
    //  return await McuKeyboardHelper.SetLightingAudioEffect(handle, setting);
    //}

    ///// <summary>
    ///// 设置每键 RGB 键盘的全局亮度（物理值 0～100）。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <param name="level">亮度值，合法范围 0～100。超出范围将被硬件限制。</param>
    ///// <returns>true 表示设置成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyBrightness(int handle, byte level) {
    //  return await McuGeneralHelper.SetKeyboardBrightness(handle, level);
    //}

    ///// <summary>点亮每键 RGB 键盘的所有 LED。</summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>true 表示成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyLightingOn(int handle) {
    //  return await McuGeneralHelper.SetKeyboardLightingOn(handle);
    //}

    ///// <summary>关闭每键 RGB 键盘的所有 LED。</summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>true 表示成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyLightingOff(int handle) {
    //  return await McuGeneralHelper.SetKeyboardLightingOff(handle);
    //}

    ///// <summary>
    ///// 控制每键 RGB 键盘上单个 LED 的亮灭。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <param name="allKeyStatus">每个键的状态字节列表，长度必须与键盘物理键数一致。字节含义由硬件定义。</param>
    ///// <returns>true 表示设置成功；false 表示失败。</returns>
    //public static async Task<bool> SetPerKeyLedOnOff(int handle, List<byte> allKeyStatus) {
    //  return await McuGeneralHelper.SetKeyboardIndividualLEDOnOff(handle, allKeyStatus);
    //}

    ///// <summary>
    ///// 将当前每键 RGB 灯光配置保存到 MCU 闪存（持久化），确保重启后不丢失。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>true 表示保存成功；false 表示失败。</returns>
    //public static async Task<bool> StorePerKeyToFlash(int handle) {
    //  return await McuGeneralHelper.StoreLightingToFlash(handle, LightingEffectTarget.ALL_LED_AREA);
    //}

    ///// <summary>
    ///// 将每键 RGB 键盘的灯光设置恢复为出厂默认值。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>true 表示恢复成功；false 表示失败。</returns>
    //public static async Task<bool> RestorePerKeyLightingToDefault(int handle) {
    //  return await McuGeneralHelper.RestoreLightingToDefault(handle);
    //}

    ///// <summary>
    ///// 读取每键 RGB 键盘当前正在运行的动画效果。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>成功返回 <see cref="LightingSetting"/>；失败返回 null。</returns>
    //public static async Task<LightingSetting> GetPerKeyCurrentEffect(int handle) {
    //  var (success, setting) = await McuGeneralHelper.GetLightingEffect(handle, LightingEffectTarget.ALL_LED_AREA);
    //  return success ? setting : null;
    //}

    ///// <summary>
    ///// 获取每键 RGB 键盘的物理布局语言（如美国英语、英国英语等）。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>键盘语言枚举值；失败时返回 <see cref="KeyboardLanguage.LANGUAGE_US_ENGLISH"/>。</returns>
    //public static async Task<KeyboardLanguage> GetPerKeyLanguage(int handle) {
    //  var (success, lang) = await McuGeneralHelper.GetKeyboardLanguage(handle);
    //  return success ? lang : KeyboardLanguage.LANGUAGE_US_ENGLISH;
    //}

    ///// <summary>
    ///// 读取键盘的各类开关状态（例如 Fn 锁定状态等）。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>键状态字典，键为 <see cref="KeyboardStatusType"/>，值为 <see cref="CommonToggleEnum"/>。</returns>
    //public static async Task<Dictionary<KeyboardStatusType, CommonToggleEnum>> GetPerKeyKeyStatus(int handle) {
    //  return await McuGeneralHelper.GetKeyboardKeyStatus(handle);
    //}

    ///// <summary>
    ///// 读取每键 RGB 键盘的当前亮度值。
    ///// </summary>
    ///// <param name="handle">设备句柄。</param>
    ///// <returns>亮度值 (0～100)；失败时返回 0。</returns>
    //public static async Task<byte> GetPerKeyBrightness(int handle) {
    //  try {
    //    var (success, brightness) = await McuKeyboardHelper.GetAllKeyboardBrightness(handle);
    //    return success ? brightness : (byte)0;
    //  } catch { return 0; }
    //}

    // ════════════ 四区 / 单区键盘 + 灯条控制（WMI 通道） ════════════

    // ─── 灯光设备类型 ──────────────────────────────────────────────
    /// <summary>可独立控制的灯光设备</summary>
    public enum LightingDevice {
      /// <summary>键盘（单区/四区）</summary>
      Keyboard,
      /// <summary>侧面灯条</summary>
      LightBar
    }

    // ─── 支持的灯光功能结构体 ───────────────────────────────────────
    /// <summary>描述当前系统支持的灯光设备及其能力。</summary>
    public struct LightingCapabilities {
      /// <summary>是否有键盘</summary>
      public bool HasKeyboard;
      /// <summary>键盘的具体分区类型</summary>
      public NbKeyboardLightingType KeyboardType;
      /// <summary>是否配备灯条</summary>
      public bool HasLightBar;
      /// <summary>四区键盘是否支持动画效果</summary>
      public bool HasFourZoneAnimation;
      /// <summary>是否支持每键 RGB</summary>
      public bool HasPerKeyRgb;
    }

    // ─── 设备感知 ─────────────────────────────────────────────────
    /// <summary>
    /// 获取当前系统支持的所有灯光设备及其能力。
    /// </summary>
    /// <returns>包含所有设备信息的 <see cref="LightingCapabilities"/> 结构体。</returns>
    public static LightingCapabilities GetLightingCapabilities() {
      var caps = new LightingCapabilities();

      caps.KeyboardType = GetKeyboardType();
      caps.HasKeyboard = caps.KeyboardType != NbKeyboardLightingType.NotSupported;
      caps.HasPerKeyRgb = caps.KeyboardType >= NbKeyboardLightingType.RgbPerKey;

      caps.HasLightBar = CheckLightBarSupport();
      caps.HasFourZoneAnimation = CheckFourZoneAnimationSupport();

      return caps;
    }

    /// <summary>
    /// 检测灯条硬件是否可用（WMI 命令 0x20008 / CommandType=1，bit1 为 1 表示支持）。
    /// </summary>
    /// <returns>true 表示灯条可用；false 表示不可用。</returns>
    private static bool CheckLightBarSupport() {
      try {
        byte[] result = SendOmenBiosWmi(
            commandType: 1,
            data: null,
            outputSize: 4,
            command: 0x20008);
        if (result != null && result.Length >= 1) {
          return (result[0] & 0x02) != 0;
        }
      } catch { }
      return false;
    }

    /// <summary>
    /// 检测四区键盘是否支持动画效果（WMI 命令 0x20009 / CommandType=12，返回有效效果ID即支持）。
    /// </summary>
    /// <returns>true 表示支持；false 表示不支持。</returns>
    public static bool CheckFourZoneAnimationSupport() {
      try {
        byte[] result = SendOmenBiosWmi(
            commandType: 12,
            data: new byte[4] { 0, 0, 0, 0 },
            outputSize: 4,
            command: 0x20009);
        if (result != null && result.Length > 0 && result[0] != 0xFF)
          return true;
      } catch { }
      return false;
    }

    // ─── 静态颜色设置（键盘/灯条通用，自动适配协议） ──────────────
    /// <summary>
    /// 为指定设备设置静态颜色。自动根据平台选择 WMI 协议。
    /// 单区键盘时仅第一个颜色生效，其余颜色建议填黑色（0,0,0）。
    /// </summary>
    /// <param name="device">目标设备（Keyboard 或 LightBar）</param>
    /// <param name="colors">长度必须为 4 的颜色列表，每个颜色包含 R、G、B 分量（0～255）</param>
    /// <param name="brightness">亮度，整数 0～100；默认 100。</param>
    /// <exception cref="ArgumentException">当 colors 为 null 或长度不为 4 时抛出。</exception>
    public static void SetZoneStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors, byte brightness = 100) {
      if (colors == null || colors.Count != 4)
        throw new ArgumentException("必须提供 4 个颜色");

      // 缓存颜色，供亮度调整等后续操作使用
      _lastDeviceColors[device] = new List<System.Windows.Media.Color>(colors);

      WmiProtocol protocol = DetectWmiProtocol();
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (protocol == WmiProtocol.Dojo) {
        byte[] data = new byte[128];
        data[0] = target;
        data[1] = 0;               // 静态模式标记
        data[3] = brightness;
        data[6] = 4;               // 颜色数量固定为 4

        for (int i = 0; i < 4; i++) {
          data[7 + i * 3] = colors[i].R;
          data[8 + i * 3] = colors[i].G;
          data[9 + i * 3] = colors[i].B;
        }

        SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
      } else // Drax / Noctali 协议：使用命令类型 7，静态颜色格式
        {
        // 仅使用第一个颜色，因为单色设备
        var c = colors[0];
        byte[] data = new byte[8];
        data[0] = 0;                   // 固定
        data[1] = 0;                   // 效果码 0 = 静态
        data[2] = 0;                   // 间隔
        data[3] = brightness;
        data[4] = 1;                   // 颜色数
        data[5] = c.R;
        data[6] = c.G;
        data[7] = c.B;

        SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
      }
    }

    // ─── 动画设置（键盘/灯条通用，自动适配协议） ────────────────
    /// <summary>
    /// 为指定设备设置动画效果。自动根据平台选择 WMI 协议及效果映射。
    /// </summary>
    /// <param name="device">目标设备（Keyboard 或 LightBar）</param>
    /// <param name="effectId">效果编号（Dojo 协议）：
    /// 2=COLOR_CYCLE 颜色循环, 3=STARLIGHT 星光, 4=BREATHING 呼吸,
    /// 6=WAVE 波浪, 7=RAINDROP 雨滴, 8=AUDIO_PULSE 音频脉冲,
    /// 9=CONFETTI 五彩纸屑, 10=SUN 太阳, 11=SWIPE 划过</param>
    /// <param name="speed">速度：0=慢，1=中，2=快</param>
    /// <param name="direction">方向：0=左/逆时针，1=右/顺时针（仅 Dojo 协议有效）</param>
    /// <param name="theme">主题：0=银河，1=火山，2=丛林，3=海洋，4=自定义（仅 Dojo 协议有效）</param>
    /// <param name="customColors">自定义主题时的颜色列表（最多 4 种颜色）；若 theme 不为 4 则忽略。
    /// Drax/Noctali 协议下必须提供此参数，否则动画无效。</param>
    /// <param name="brightness">亮度 0～100，默认 100。</param>
    public static void SetZoneAnimation(LightingDevice device, byte effectId, byte speed = 1, byte direction = 0,
        byte theme = 0, List<System.Windows.Media.Color> customColors = null, byte brightness = 100) {
      WmiProtocol protocol = DetectWmiProtocol();
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (protocol == WmiProtocol.Dojo) {
        byte[] data = new byte[128];
        data[0] = target;
        data[1] = effectId;

        // 速度 (bit0-1)
        data[2] &= 0xFC;
        data[2] |= (byte)(speed & 0x03);
        // 方向 (bit2-3)
        data[2] &= 0xF3;
        data[2] |= (byte)(direction == 1 ? 0x08 : 0x04);
        // 主题 (bit4-7)
        data[2] &= 0x0F;
        switch (theme) {
          case 0: data[2] |= 0x10; break;
          case 1: data[2] |= 0x20; break;
          case 2: data[2] |= 0x30; break;
          case 3: data[2] |= 0x40; break;
          case 4: data[2] |= 0x50; break;
        }
        data[3] = brightness;
        data[4] = 0; // tribe
        data[5] = 0; // bass

        if (theme == 4 && customColors != null) {
          int count = Math.Min(customColors.Count, 4);
          data[6] = (byte)count;
          for (int i = 0; i < count; i++) {
            data[7 + i * 3] = customColors[i].R;
            data[8 + i * 3] = customColors[i].G;
            data[9 + i * 3] = customColors[i].B;
          }
        }

        SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
      } else // Drax / Noctali 协议
        {
        // 效果映射：Dojo -> Drax
        byte draxEffect;
        if (effectId == 2) draxEffect = 2;      // 颜色循环
        else if (effectId == 4) draxEffect = 1; // 呼吸
        else {
          // 不支持的效果，静默返回
          return;
        }

        // 速度 -> 间隔（速越快间隔越小）
        byte interval;
        if (speed == 0) interval = 10;
        else if (speed == 1) interval = 5;
        else interval = 2;

        List<System.Windows.Media.Color> animColors;
        if (theme == 4 && customColors != null && customColors.Count > 0)
          animColors = customColors;
        else {
          // 无自定义颜色时使用缓存颜色（若存在），否则默认黑色
          if (_lastDeviceColors.TryGetValue(device, out var lastColors) && lastColors.Count > 0)
            animColors = new List<System.Windows.Media.Color> { lastColors[0] };
          else
            animColors = new List<System.Windows.Media.Color> { System.Windows.Media.Color.FromRgb(255, 255, 255) };
        }

        byte[] data = new byte[5 + animColors.Count * 3];
        data[0] = 0;
        data[1] = draxEffect;
        data[2] = interval;
        data[3] = brightness;
        data[4] = (byte)animColors.Count;
        for (int i = 0; i < animColors.Count; i++) {
          data[5 + i * 3] = animColors[i].R;
          data[6 + i * 3] = animColors[i].G;
          data[7 + i * 3] = animColors[i].B;
        }

        SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
      }
    }

    // ─── 亮度（键盘/灯条通用，自动适配协议） ────────────────────────
    /// <summary>
    /// 为指定设备设置亮度。会自动使用最后设置的颜色保持当前灯光效果。
    /// 若从未设置过颜色，则 Drax/Noctali 协议将不执行操作。
    /// </summary>
    /// <param name="device">目标设备（Keyboard 或 LightBar）</param>
    /// <param name="brightness">亮度值，0～100。</param>
    public static void SetZoneBrightness(LightingDevice device, byte brightness) {
      WmiProtocol protocol = DetectWmiProtocol();

      if (protocol == WmiProtocol.Dojo) {
        byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
        byte[] data = new byte[128];
        data[0] = target;
        data[3] = brightness;
        SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
      } else // Drax / Noctali 必须同时提供颜色
        {
        if (_lastDeviceColors.TryGetValue(device, out var colors) && colors.Count > 0) {
          SetZoneStaticColor(device, colors, brightness);
        }
        // 否则无操作，避免异常
      }
    }

    /// <summary>
    /// 关闭指定设备的灯光。
    /// Dojo 协议：将亮度设为 0。
    /// Drax 协议：设置为黑色且亮度 0。
    /// Noctali 协议：发送专用关闭命令 [0, 0xFF]。
    /// </summary>
    public static void SetZoneOff(LightingDevice device) {
      WmiProtocol protocol = DetectWmiProtocol();

      if (protocol == WmiProtocol.Noctali) {
        // Noctali 特殊关闭命令
        byte[] offData = new byte[2] { 0, 0xFF };
        SendOmenBiosWmi(7, offData, 0, WMI_COMMAND_ID);
      } else if (protocol == WmiProtocol.Drax) {
        // Drax 使用静态黑色 + 亮度0
        var black = new List<System.Windows.Media.Color> { System.Windows.Media.Color.FromRgb(0, 0, 0), System.Windows.Media.Color.FromRgb(0, 0, 0), System.Windows.Media.Color.FromRgb(0, 0, 0), System.Windows.Media.Color.FromRgb(0, 0, 0) };
        SetZoneStaticColor(device, black, 0);
      } else // Dojo
        {
        SetZoneBrightness(device, 0);
      }
    }

    /// <summary>
    /// 读取当前键盘/灯条的静态颜色（返回 4 个颜色）。
    /// 注意：该读取命令仅适用于 Dojo 协议，Drax/Noctali 可能不支持。
    /// </summary>
    /// <returns>包含 4 个颜色的数组，索引 0-3 对应分区 1-4；失败返回 null。</returns>
    public static System.Windows.Media.Color[] GetZoneStaticColor() {
      byte[] result = SendOmenBiosWmi(2, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      if (result == null || result.Length < 22) return null;

      var colors = new System.Windows.Media.Color[4];
      for (int i = 0; i < 4; i++) {
        int idx = 25 + i * 3;
        colors[i] = System.Windows.Media.Color.FromRgb(result[idx], result[idx + 1], result[idx + 2]);
      }
      return colors;
    }

    /// <summary>
    /// 读取键盘/灯条的当前亮度（0～100）。
    /// 仅适用于 Dojo 协议；Drax/Noctali 协议返回 0。
    /// </summary>
    /// <returns>亮度值；失败返回 0。</returns>
    public static byte GetZoneBrightness() {
      byte[] result = SendOmenBiosWmi(4, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      if (result != null && result.Length > 0)
        return result[0];
      return 0;
    }

    /// <summary>
    /// 获取当前键盘/灯条正在运行的动画效果 ID（Dojo 协议）。
    /// Drax/Noctali 协议不支持此查询。
    /// </summary>
    /// <returns>效果 ID（2～11）；失败返回 -1。</returns>
    public static int GetCurrentAnimationEffect() {
      byte[] result = SendOmenBiosWmi(12, new byte[4] { 0, 0, 0, 0 }, 4, WMI_COMMAND_ID);
      if (result != null && result.Length > 0)
        return result[0];
      return -1;
    }
  }
}
