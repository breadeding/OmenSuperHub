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

namespace OmenSuperHub {
  /// <summary>
  /// 提供 OMEN 笔记本键盘灯光控制的全部功能。
  /// 使用 <see cref="PlatformSettingsResolver.GetLightingCapability"/> 自动获取平台灯光特性与硬件参数，
  /// 无任何硬编码 SSID。
  /// </summary>
  internal class OmenLighting {
    static OmenLighting() {
      AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
        string assemblyName = new AssemblyName(args.Name).Name + ".dll";
        string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", assemblyName);
        if (File.Exists(libPath))
          return Assembly.LoadFrom(libPath);
        return null;
      };
    }

    // ─── 键盘类型枚举 ─────────────────────────────────────────────
    public enum NbKeyboardLightingType : byte {
      OneZoneWithoutNumpad = 0,
      OneZoneWithNumpad = 1,
      FourZoneWithoutNumpad = 2,
      FourZoneWithNumpad = 3,
      RgbPerKey = 4,
      NotSupported = 255
    }

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

    // ─── 目标设备枚举 ────────────
    private enum TargetDevice : byte {
      LightBar = 0,
      FourZoneAni = 1
    }

    private enum WmiProtocol {
      Unknown,
      Dojo,       // commandType=11, 128 字节结构
      Drax,       // commandType=7, 变长结构
      Noctali     // commandType=7, 关闭命令特殊 [0, 0xFF]
    }

    private const int WMI_COMMAND_ID = 131081; // 0x20009

    private static readonly Dictionary<LightingDevice, List<System.Windows.Media.Color>> _lastDeviceColors =
        new Dictionary<LightingDevice, List<System.Windows.Media.Color>>();

    // ─── 获取系统 ID（SSID） ──────────────────────────────────────
    private static string GetSystemId() {
      // 假设 OmenHardware.GetSystemID() 返回类似 "8BAD" 的字符串
      return OmenHardware.GetSystemID()?.Trim() ?? string.Empty;
    }

    // ─── 缓存当前平台的灯光能力（应用程序生命周期内不变） ──────────
    private static PlatformSettingsResolver.LightingCapability _cachedCapability;
    private static PlatformSettingsResolver.LightingCapability GetPlatformCapability() {
      if (_cachedCapability == null) {
        _cachedCapability = PlatformSettingsResolver.GetLightingCapability(GetSystemId());
      }
      return _cachedCapability;
    }

    // ─── 键盘硬件类型（通过 WMI 查询） ──────────────────────────────
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

    public static bool IsPerKeyRgb => GetKeyboardType() >= NbKeyboardLightingType.RgbPerKey;

    // ─── 平台族名（从 LightingCapability 获取） ────────────────────
    public static string GetPlatformFamily() {
      var capability = GetPlatformCapability();
      // 直接通过 PlatformSettings 获取 Family（需要扩展 LightingCapability 或仍用 Resolver）
      // 此处暂时保留原有的 PlatformSettings 获取方式，以便 WMI 协议判断继续工作
      var settings = PlatformSettingsResolver.LoadFromCurrentSystem();
      return settings?.PlatformFamily ?? "";
    }

    /// <summary>
    /// 根据平台族名判断 WMI 命令类型。
    /// </summary>
    private static WmiProtocol DetectWmiProtocol() {
      string family = GetPlatformFamily().ToLowerInvariant();
      if (family == "dojo" || family == "vibrance" || family == "starmade" || family == "modena")
        return WmiProtocol.Dojo;
      if (family == "drax")
        return WmiProtocol.Drax;
      if (family == "noctali")
        return WmiProtocol.Noctali;
      return WmiProtocol.Dojo; // 默认
    }

    // ─── HID 设备操作（每键 RGB） ──────────────────────────────────
    public static int OpenHidDevice(int pid, int vid, string interfaceString = "") {
      try {
        Task<int> task = McuGeneralHelper.OpenDevice(pid, vid, interfaceString, "");
        task.Wait();
        return task.Result;
      } catch (AggregateException ae) {
        foreach (var inner in ae.InnerExceptions) {
           Logger.Error($"OpenHidDevice AggregateException: {inner.Message}");
        }
        return -1;
      } catch (Exception ex) {
        Logger.Error($"OpenHidDevice Exception: {ex.Message}");
        return -1;
      }
    }

    public static async Task<bool> CloseDeviceAsync(int handle) {
      return await McuGeneralHelper.CloseDevice(handle);
    }

    /// <summary>
    /// 根据平台能力自动打开每键 RGB 键盘。
    /// 现在完全依赖 <see cref="GetPlatformCapability"/> 返回的参数。
    /// </summary>
    public static int OpenPerKeyKeyboard() {
      var cap = GetPlatformCapability();
      if ((!cap.SupportsPerKeyRGB && !cap.SupportsLightBar) || cap.KeyboardPids == null || cap.KeyboardVid == null)
        return -1;

      foreach (int pid in cap.KeyboardPids) {
        int handle = OpenHidDevice(pid, cap.KeyboardVid.Value, cap.KeyboardInterfaceString ?? "");
        if (handle > 0)
          return handle;
      }
      return -1;
    }

    // ════════════ 每键 RGB 控制（HID / MCU 通道） ════════════════════
    public static async Task<bool> SetPerKeyStaticColor(int handle, byte[] r, byte[] g, byte[] b) {
      return await McuGeneralHelper.SetKeyboardStaticLighting(handle, r, g, b);
    }

    public static async Task<bool> SetPerKeyAnimation(int handle, LightingSetting setting) {
      return await McuGeneralHelper.SetLightingEffect(handle, setting, LightingEffectTarget.ALL_LED_AREA);
    }

    public static async Task<bool> SetPerKeyAudioAnimation(int handle, LightingAudioEffectSetting setting) {
      return await McuKeyboardHelper.SetLightingAudioEffect(handle, setting);
    }

    public static async Task<bool> SetPerKeyBrightness(int handle, byte level) {
      return await McuGeneralHelper.SetKeyboardBrightness(handle, level);
    }

    public static async Task<bool> SetPerKeyLightingOn(int handle) {
      return await McuGeneralHelper.SetKeyboardLightingOn(handle);
    }

    public static async Task<bool> SetPerKeyLightingOff(int handle) {
      return await McuGeneralHelper.SetKeyboardLightingOff(handle);
    }

    public static async Task<bool> SetPerKeyLedOnOff(int handle, List<byte> allKeyStatus) {
      return await McuGeneralHelper.SetKeyboardIndividualLEDOnOff(handle, allKeyStatus);
    }

    public static async Task<bool> StorePerKeyToFlash(int handle) {
      return await McuGeneralHelper.StoreLightingToFlash(handle, LightingEffectTarget.ALL_LED_AREA);
    }

    public static async Task<bool> RestorePerKeyLightingToDefault(int handle) {
      return await McuGeneralHelper.RestoreLightingToDefault(handle);
    }

    public static async Task<LightingSetting> GetPerKeyCurrentEffect(int handle) {
      var (success, setting) = await McuGeneralHelper.GetLightingEffect(handle, LightingEffectTarget.ALL_LED_AREA);
      return success ? setting : null;
    }

    public static async Task<KeyboardLanguage> GetPerKeyLanguage(int handle) {
      var (success, lang) = await McuGeneralHelper.GetKeyboardLanguage(handle);
      return success ? lang : KeyboardLanguage.LANGUAGE_US_ENGLISH;
    }

    public static async Task<Dictionary<KeyboardStatusType, CommonToggleEnum>> GetPerKeyKeyStatus(int handle) {
      return await McuGeneralHelper.GetKeyboardKeyStatus(handle);
    }

    public static async Task<byte> GetPerKeyBrightness(int handle) {
      try {
        var (success, brightness) = await McuKeyboardHelper.GetAllKeyboardBrightness(handle);
        return success ? brightness : (byte)0;
      } catch { return 0; }
    }

    // ════════════ 四区 / 单区键盘 + 灯条控制（WMI 通道） ════════════

    public enum LightingDevice {
      Keyboard,
      LightBar
    }

    public struct LightingCapabilities {
      public bool HasKeyboard;
      public NbKeyboardLightingType KeyboardType;
      public bool HasLightBar;
      public bool HasFourZone;
      public bool HasFourZoneAnimation;
      public bool HasPerKeyRgb;
    }

    /// <summary>
    /// 获取当前系统支持的灯光设备及能力。
    /// 结合软件特性声明（PlatformSettings）与硬件查询，给出综合结果。
    /// </summary>
    public static LightingCapabilities GetLightingCapabilities() {
      var caps = new LightingCapabilities();
      var platCap = GetPlatformCapability();

      caps.KeyboardType = GetKeyboardType();
      caps.HasKeyboard = caps.KeyboardType != NbKeyboardLightingType.NotSupported;
      caps.HasPerKeyRgb = caps.KeyboardType == NbKeyboardLightingType.RgbPerKey || platCap.SupportsPerKeyRGB;
      caps.HasLightBar = platCap.SupportsLightBar && CheckLightBarSupport();
      caps.HasFourZone = caps.KeyboardType == NbKeyboardLightingType.FourZoneWithoutNumpad ||
          caps.KeyboardType == NbKeyboardLightingType.FourZoneWithNumpad || platCap.SupportsFourZone;
      caps.HasFourZoneAnimation = caps.HasFourZone;
      //caps.HasFourZoneAnimation = CheckFourZoneAnimationSupport();
      return caps;
    }

    private static bool CheckLightBarSupport() {
      try {
        byte[] result = SendOmenBiosWmi(1, null, 4, 0x20008);
        if (result != null && result.Length >= 1)
          return (result[0] & 0x02) != 0;
      } catch { }
      return false;
    }

    public static bool CheckFourZoneAnimationSupport() {
      try {
        byte[] result = SendOmenBiosWmi(12, new byte[4] { 0, 0, 0, 0 }, 4, 0x20009);
        if (result != null && result.Length > 0 && result[0] != 0xFF)
          return true;
      } catch { }
      return false;
    }

    // ─── 静态颜色 ─────────────────────────────────────────────────
    public static void SetZoneStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors, byte brightness = 100) {
      if (colors == null || colors.Count != 4)
        throw new ArgumentException("必须提供 4 个颜色");

      _lastDeviceColors[device] = new List<System.Windows.Media.Color>(colors);
      WmiProtocol protocol = DetectWmiProtocol();
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (protocol == WmiProtocol.Dojo) {
        byte[] data = new byte[128];
        data[0] = target;
        data[1] = 0;               // 静态模式
        data[3] = brightness;
        data[6] = 4;               // 颜色数量
        for (int i = 0; i < 4; i++) {
          data[7 + i * 3] = colors[i].R;
          data[8 + i * 3] = colors[i].G;
          data[9 + i * 3] = colors[i].B;
        }
        SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
      } else { // Drax / Noctali
        var c = colors[0];
        byte[] data = new byte[8] { 0, 0, 0, brightness, 1, c.R, c.G, c.B };
        SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
      }
    }

    // ─── 动画 ────────────────────────────────────────────────────
    public static void SetZoneAnimation(LightingDevice device, byte effectId, byte speed = 1, byte direction = 0,
        byte theme = 0, List<System.Windows.Media.Color> customColors = null, byte brightness = 100) {
      WmiProtocol protocol = DetectWmiProtocol();
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (protocol == WmiProtocol.Dojo) {
        byte[] data = new byte[128];
        data[0] = target;
        data[1] = effectId;
        data[2] = (byte)(data[2] & 0xFC | (speed & 0x03));
        data[2] = (byte)(data[2] & 0xF3 | (direction == 1 ? 0x08 : 0x04));
        data[2] = (byte)(data[2] & 0x0F);
        switch (theme) {
          case 0: data[2] |= 0x10; break;
          case 1: data[2] |= 0x20; break;
          case 2: data[2] |= 0x30; break;
          case 3: data[2] |= 0x40; break;
          case 4: data[2] |= 0x50; break;
        }
        data[3] = brightness;
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
      } else { // Drax / Noctali
        byte draxEffect = effectId == 2 ? (byte)2 : (effectId == 4 ? (byte)1 : (byte)0);
        if (draxEffect == 0) return;

        byte interval = speed == 0 ? (byte)10 : (speed == 1 ? (byte)5 : (byte)2);
        List<System.Windows.Media.Color> animColors = (theme == 4 && customColors?.Count > 0) ? customColors
            : _lastDeviceColors.TryGetValue(device, out var last) ? new List<System.Windows.Media.Color> { last[0] }
            : new List<System.Windows.Media.Color> { System.Windows.Media.Color.FromRgb(255, 255, 255) };
        byte[] data = new byte[5 + animColors.Count * 3];
        data[0] = 0; data[1] = draxEffect; data[2] = interval; data[3] = brightness; data[4] = (byte)animColors.Count;
        for (int i = 0; i < animColors.Count; i++) {
          data[5 + i * 3] = animColors[i].R;
          data[6 + i * 3] = animColors[i].G;
          data[7 + i * 3] = animColors[i].B;
        }
        SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
      }
    }

    // ─── 亮度 / 关灯 ──────────────────────────────────────────────
    public static void SetZoneBrightness(LightingDevice device, byte brightness) {
      WmiProtocol protocol = DetectWmiProtocol();
      if (protocol == WmiProtocol.Dojo) {
        byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
        byte[] data = new byte[128];
        data[0] = target;
        data[3] = brightness;
        SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
      } else {
        if (_lastDeviceColors.TryGetValue(device, out var colors) && colors.Count > 0)
          SetZoneStaticColor(device, colors, brightness);
      }
    }

    public static void SetZoneOff(LightingDevice device) {
      WmiProtocol protocol = DetectWmiProtocol();
      if (protocol == WmiProtocol.Noctali) {
        SendOmenBiosWmi(7, new byte[2] { 0, 0xFF }, 0, WMI_COMMAND_ID);
      } else if (protocol == WmiProtocol.Drax) {
        var black = new List<System.Windows.Media.Color> {
          System.Windows.Media.Color.FromRgb(0,0,0), System.Windows.Media.Color.FromRgb(0,0,0),
          System.Windows.Media.Color.FromRgb(0,0,0), System.Windows.Media.Color.FromRgb(0,0,0)
        };
        SetZoneStaticColor(device, black, 0);
      } else {
        SetZoneBrightness(device, 0);
      }
    }

    // ─── 读取（Dojo 协议，其他协议返回空或默认） ─────────────────
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

    public static byte GetZoneBrightness() {
      byte[] result = SendOmenBiosWmi(4, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      return (result != null && result.Length > 0) ? result[0] : (byte)0;
    }

    public static int GetCurrentAnimationEffect() {
      byte[] result = SendOmenBiosWmi(12, new byte[4] { 0, 0, 0, 0 }, 4, WMI_COMMAND_ID);
      return (result != null && result.Length > 0) ? result[0] : -1;
    }
  }
}
