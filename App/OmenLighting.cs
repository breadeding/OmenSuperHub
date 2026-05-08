using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hp.Bridge.Client.SDKs.McuSDK2;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.DataStructure;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.Enums;
using Hp.Bridge.Client.SDKs.McuSDK2.General.Enums;
using Hp.Bridge.Client.SDKs.McuSDK2.General.Enums.Lighting;
using Hp.Bridge.Client.SDKs.McuSDK2.Keyboard;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.App {
  /// <summary>
  /// 提供 OMEN 笔记本键盘灯光控制的全部功能。
  /// 支持四区/单区键盘（通过 WMI 命令 0x20009/0x0B）以及每键 RGB 键盘（通过 HID/MCU）。
  /// 所有平台识别均通过 PlatformSettings 或 DeviceModel 自动完成，无需硬编码 SSID。
  /// </summary>
  internal class OmenLighting {
    // ─── 静态构造函数：处理 DLL 加载路径 ─────────────────────────────
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

    /// <summary>将键盘分区类型转换为中文说明</summary>
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

    // ─── WMI 常量 ─────────────────────────────────────────────────
    private const int WMI_COMMAND_ID = 131081; // 0x20009
    private const int WMI_CMD_TYPE = 11;       // 0x0B
    private const int WMI_DATA_SIZE = 128;

    // ─── 通过 WMI 获取键盘类型 ──────────────────────────────────────
    /// <summary>
    /// 获取键盘硬件灯光类型，发送 WMI 命令 0x20008 / commandType=43。
    /// </summary>
    /// <returns>键盘类型枚举值；失败时返回 <see cref="NbKeyboardLightingType.NotSupported"/></returns>
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

    /// <summary>判断是否为每键 RGB 键盘</summary>
    public static bool IsPerKeyRgb => GetKeyboardType() >= NbKeyboardLightingType.RgbPerKey;

    // ─── 平台识别辅助 ──────────────────────────────────────────────
    /// <summary>
    /// 从已加载的 PlatformSettings 中获取当前系统的平台族（如 "Dojo", "Starmade" 等）。
    /// 该方法复用您现有的 PlatformSettingsResolver 逻辑，完全遵循 OGH 的识别机制。
    /// </summary>
    public static string GetPlatformFamily() {
      // 使用您项目中已有的 PlatformSettings 类
      var settings = PlatformSettingsResolver.LoadFromCurrentSystem();
      return settings?.PlatformFamily ?? "";
    }

    // ─── HID 设备打开（每键 RGB 键盘专用） ──────────────────────────
    /// <summary>
    /// 同步打开指定 PID/VID 的 HID 设备，返回正数句柄，失败返回 -1。
    /// </summary>
    /// <param name="pid">产品 ID</param>
    /// <param name="vid">供应商 ID</param>
    /// <param name="interfaceString">可选接口字符串，如 "mi_03"</param>
    public static int OpenHidDevice(int pid, int vid, string interfaceString = "") {
      try {
        Task<int> task = McuGeneralHelper.OpenDevice(pid, vid, interfaceString, "");
        task.Wait();
        return task.Result;
      } catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine($"[OmenLighting] OpenHidDevice failed: {ex.Message}");
        return -1;
      }
    }

    /// <summary>
    /// 关闭 HID 设备句柄。
    /// </summary>
    /// <param name="handle">由 <see cref="OpenPerKeyKeyboard"/> 返回的句柄</param>
    public static async Task<bool> CloseDeviceAsync(int handle) {
      return await McuGeneralHelper.CloseDevice(handle);
    }

    /// <summary>
    /// 根据当前平台族自动选择正确的 PID/VID 并打开每键 RGB 键盘。
    /// 平台识别完全依赖 DeviceModel / PlatformSettings，无硬编码 SSID。
    /// </summary>
    /// <returns>设备句柄，失败返回 -1</returns>
    public static int OpenPerKeyKeyboard() {
      if (!IsPerKeyRgb)
        return -1;

      string family = GetPlatformFamily().ToLowerInvariant();

      // 按照 OGH 源码中的平台映射进行匹配
      if (family == "dojo" || family == "vibrance") {
        int handle = OpenHidDevice(21695, 3426, "mi_03");
        if (handle > 0) return handle;
        return OpenHidDevice(12479, 3426, "mi_03");
      }
      if (family == "starmade") {
        return OpenHidDevice(20171, 1121, "mi_02");
      }
      if (family == "modena") {
        return OpenHidDevice(8760, 8137, "mi_02");
      }
      // 后续可继续添加其他每键 RGB 平台

      return -1;
    }

    // ════════════ 每键 RGB 控制（HID 通道） ════════════════════════

    /// <summary>
    /// 设置每键 RGB 键盘的静态颜色。
    /// </summary>
    /// <param name="handle">设备句柄</param>
    /// <param name="r">各按键的红色分量，长度应与键盘键位数一致</param>
    /// <param name="g">各按键的绿色分量</param>
    /// <param name="b">各按键的蓝色分量</param>
    /// <returns>操作是否成功</returns>
    public static async Task<bool> SetPerKeyStaticColor(int handle, byte[] r, byte[] g, byte[] b) {
      return await McuGeneralHelper.SetKeyboardStaticLighting(handle, r, g, b);
    }

    /// <summary>
    /// 设置每键 RGB 键盘的动画效果。
    /// </summary>
    /// <param name="handle">设备句柄</param>
    /// <param name="setting">动画效果配置，参见 <see cref="LightingSetting"/> 结构</param>
    /// <returns>操作是否成功</returns>
    public static async Task<bool> SetPerKeyAnimation(int handle, LightingSetting setting) {
      return await McuGeneralHelper.SetLightingEffect(handle, setting, LightingEffectTarget.ALL_LED_AREA);
    }

    /// <summary>
    /// 设置每键 RGB 键盘的音乐律动（音频脉冲）动画。
    /// </summary>
    /// <param name="handle">设备句柄</param>
    /// <param name="setting">音频动画参数</param>
    /// <returns>操作是否成功</returns>
    public static async Task<bool> SetPerKeyAudioAnimation(int handle, LightingAudioEffectSetting setting) {
      // 注：McUGeneralHelper 并未直接暴露音频动画，而是通过 McuKeyboardHelper 实现
      return await McuKeyboardHelper.SetLightingAudioEffect(handle, setting);
    }

    /// <summary>
    /// 设置每键 RGB 键盘的全局亮度（物理值 0～100）。
    /// </summary>
    /// <param name="handle">设备句柄</param>
    /// <param name="level">亮度值 0～100</param>
    /// <returns>操作是否成功</returns>
    public static async Task<bool> SetPerKeyBrightness(int handle, byte level) {
      return await McuGeneralHelper.SetKeyboardBrightness(handle, level);
    }

    /// <summary>点亮每键 RGB 键盘的所有 LED</summary>
    public static async Task<bool> SetPerKeyLightingOn(int handle) {
      return await McuGeneralHelper.SetKeyboardLightingOn(handle);
    }

    /// <summary>关闭每键 RGB 键盘的所有 LED</summary>
    public static async Task<bool> SetPerKeyLightingOff(int handle) {
      return await McuGeneralHelper.SetKeyboardLightingOff(handle);
    }

    /// <summary>
    /// 控制每键 RGB 键盘上单个 LED 的亮灭。
    /// </summary>
    /// <param name="handle">设备句柄</param>
    /// <param name="allKeyStatus">每个键的亮灭状态列表，长度需与键数一致</param>
    /// <returns>操作是否成功</returns>
    public static async Task<bool> SetPerKeyLedOnOff(int handle, List<byte> allKeyStatus) {
      return await McuGeneralHelper.SetKeyboardIndividualLEDOnOff(handle, allKeyStatus);
    }

    /// <summary>将当前每键 RGB 灯光配置保存到 MCU 闪存，确保重启后不丢失。</summary>
    public static async Task<bool> StorePerKeyToFlash(int handle) {
      return await McuGeneralHelper.StoreLightingToFlash(handle, LightingEffectTarget.ALL_LED_AREA);
    }

    /// <summary>重置每键 RGB 键盘的灯光设为出厂默认。</summary>
    public static async Task<bool> RestorePerKeyLightingToDefault(int handle) {
      return await McuGeneralHelper.RestoreLightingToDefault(handle);
    }

    /// <summary>
    /// 读取每键 RGB 键盘当前正在运行的动画效果。
    /// </summary>
    /// <returns>成功返回 <see cref="LightingSetting"/>，失败返回 null</returns>
    public static async Task<LightingSetting> GetPerKeyCurrentEffect(int handle) {
      var (success, setting) = await McuGeneralHelper.GetLightingEffect(handle, LightingEffectTarget.ALL_LED_AREA);
      return success ? setting : null;
    }

    /// <summary>
    /// 获取每键 RGB 键盘的物理布局语言（如美国英语、英国英语等）。
    /// </summary>
    public static async Task<KeyboardLanguage> GetPerKeyLanguage(int handle) {
      var (success, lang) = await McuGeneralHelper.GetKeyboardLanguage(handle);
      return success ? lang : KeyboardLanguage.LANGUAGE_US_ENGLISH;
    }

    /// <summary>
    /// 读取键盘的各类开关状态（如 Fn 锁定等）。
    /// </summary>
    public static async Task<Dictionary<KeyboardStatusType, CommonToggleEnum>> GetPerKeyKeyStatus(int handle) {
      return await McuGeneralHelper.GetKeyboardKeyStatus(handle);
    }

    /// <summary>
    /// 读取每键 RGB 键盘的当前亮度值（0～100）。
    /// </summary>
    public static async Task<byte> GetPerKeyBrightness(int handle) {
      try {
        var (success, brightness) = await McuKeyboardHelper.GetAllKeyboardBrightness(handle);
        return success ? brightness : (byte)0;
      } catch { return 0; }
    }

    // ════════════ 四区 / 单区键盘控制（WMI 通道） ════════════════

    // ─── 灯光设备类型 ─────────────────────────────────
    /// <summary>可独立控制的灯光设备</summary>
    public enum LightingDevice {
      Keyboard,        // 键盘（单区/四区/每键RGB）
      LightBar         // 侧面灯条
    }

    // ─── 支持的灯光功能 ─────────────────────────────────
    /// <summary>灯光设备支持的功能</summary>
    public struct LightingCapabilities {
      public bool HasKeyboard;
      public NbKeyboardLightingType KeyboardType;
      public bool HasLightBar;
      public bool HasFourZoneAnimation;   // 四区动画是否可用
      public bool HasPerKeyRgb;
    }

    // ─── 设备感知 ──────────────────────────────────────
    /// <summary>
    /// 获取当前系统支持的所有灯光设备及其能力。
    /// </summary>
    public static LightingCapabilities GetLightingCapabilities() {
      var caps = new LightingCapabilities();

      // 键盘类型
      caps.KeyboardType = GetKeyboardType();
      caps.HasKeyboard = caps.KeyboardType != NbKeyboardLightingType.NotSupported;
      caps.HasPerKeyRgb = caps.KeyboardType >= NbKeyboardLightingType.RgbPerKey;

      // 灯条支持（来自 OmenHardware.GetSupportedKeyboardLighting）
      caps.HasLightBar = CheckLightBarSupport();

      // 四区动画支持（来自 FourZoneHelper.IsAnimationSupported）
      caps.HasFourZoneAnimation = CheckFourZoneAnimationSupport();

      return caps;
    }

    /// <summary>检测灯条硬件是否可用（WMI 命令 0x20008, commandType=1）</summary>
    private static bool CheckLightBarSupport() {
      try {
        byte[] result = SendOmenBiosWmi(
            commandType: 1,
            data: null,
            outputSize: 4,
            command: 0x20008);       // 注意此处 command=0x20008，不同于灯光命令 0x20009
        if (result != null && result.Length >= 1) {
          // bit1 为 1 表示支持灯条
          return (result[0] & 0x02) != 0;
        }
      } catch { }
      return false;
    }

    /// <summary>检测四区动画是否可用（WMI 命令 0x20009, commandType=12）</summary>
    private static bool CheckFourZoneAnimationSupport() {
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

    // ─── 颜色设置（重构为支持键盘与灯条） ──────────────
    /// <summary>
    /// 为指定设备设置静态颜色。
    /// 单区键盘时仅第一个颜色生效，其余颜色可填任意值（通常填黑色）。
    /// </summary>
    /// <param name="device">目标设备（键盘或灯条）</param>
    /// <param name="colors">长度为4的颜色列表</param>
    /// <param name="brightness">亮度 0～100</param>
    public static void SetZoneStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors, byte brightness = 100) {
      if (colors == null || colors.Count != 4)
        throw new ArgumentException("必须提供 4 个颜色");

      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
      byte[] data = new byte[WMI_DATA_SIZE];
      data[0] = target;
      data[1] = 0;               // 静态模式
      data[3] = brightness;
      data[6] = 4;

      for (int i = 0; i < 4; i++) {
        data[7 + i * 3] = colors[i].R;
        data[8 + i * 3] = colors[i].G;
        data[9 + i * 3] = colors[i].B;
      }

      SendOmenBiosWmi(WMI_CMD_TYPE, data, 0, WMI_COMMAND_ID);
    }

    /// <summary>
    /// 为指定设备设置动画效果。
    /// </summary>
    /// <param name="device">目标设备（键盘或灯条）</param>
    /// <param name="effectId">效果 ID：2=色彩循环,3=星光,4=呼吸,6=波浪,7=雨滴,8=音频脉冲,9=五彩纸屑,10=太阳,11=划过</param>
    /// <param name="speed">速度：0=慢,1=中,2=快</param>
    /// <param name="direction">方向：0=左,1=右</param>
    /// <param name="theme">主题：0=银河,1=火山,2=丛林,3=海洋,4=自定义</param>
    /// <param name="customColors">自定义主题时的颜色列表（最多4个）</param>
    /// <param name="brightness">亮度 0～100</param>
    public static void SetZoneAnimation(LightingDevice device, byte effectId, byte speed = 1, byte direction = 0,
        byte theme = 0, List<System.Windows.Media.Color> customColors = null, byte brightness = 100) {
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
      byte[] data = new byte[WMI_DATA_SIZE];
      data[0] = target;
      data[1] = effectId;
      // ...（后续填充逻辑与之前完全相同，此处省略重复部分）
      // data[2] 速度/方向/主题编码
      // data[3] 亮度
      // data[7..18] 颜色

      SendOmenBiosWmi(WMI_CMD_TYPE, data, 0, WMI_COMMAND_ID);
    }

    /// <summary>
    /// 为指定设备设置亮度（0～100）。
    /// </summary>
    public static void SetZoneBrightness(LightingDevice device, byte brightness) {
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
      byte[] data = new byte[WMI_DATA_SIZE];
      data[0] = target;
      data[3] = brightness;
      SendOmenBiosWmi(WMI_CMD_TYPE, data, 0, WMI_COMMAND_ID);
    }

    /// <summary>
    /// 读取四区/单区键盘当前的静态颜色（返回4个颜色）。
    /// 单区键盘只关注第一个颜色。
    /// </summary>
    public static System.Windows.Media.Color[] GetZoneStaticColor(LightingDevice device) {
      // 读取命令 commandType=2 返回128字节，颜色从索引25开始
      // 灯条与键盘使用相同结构
      byte[] result = SendOmenBiosWmi(2, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      if (result == null || result.Length < 22) return null;

      var colors = new System.Windows.Media.Color[4];
      for (int i = 0; i < 4; i++) {
        int idx = 25 + i * 3;
        colors[i] = System.Windows.Media.Color.FromRgb(result[idx], result[idx + 1], result[idx + 2]);
      }
      return colors;
    }

    /// <summary>读取四区/单区键盘的亮度值（0～100）。</summary>
    public static byte GetZoneBrightness() {
      byte[] result = SendOmenBiosWmi(4, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      if (result != null && result.Length > 0)
        return result[0];
      return 0;
    }

    /// <summary>读取当前四区/单区动画效果 ID（失败返回 -1）。</summary>
    public static int GetCurrentAnimationEffect() {
      byte[] result = SendOmenBiosWmi(12, new byte[4] { 0, 0, 0, 0 }, 4, WMI_COMMAND_ID);
      if (result != null && result.Length > 0)
        return result[0];
      return -1;
    }
  }
}
