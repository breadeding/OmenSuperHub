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
  internal class OmenLighting {
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

    // ─── 灯具、接口枚举 ─────────────────────────────────────────────
    public enum LightingDevice {
      Keyboard,
      LightBar
    }

    public enum LightingControlInterface {
      None = 0,
      BasicFourZone,  // WMI CommandType 2/3/4/5
      Dojo,           // WMI CommandType 11
      Drax,           // WMI CommandType 7（Drax 协议）
      Noctali,        // WMI CommandType 7（Noctali 协议，关灯命令特殊）
      PerKeyRGB       // HID / MCU 直通
    }

    private enum TargetDevice : byte {
      LightBar = 0,
      FourZoneAni = 1
    }

    private const int WMI_COMMAND_ID = 131081; // 0x20009

    private static readonly Dictionary<LightingDevice, List<System.Windows.Media.Color>> _lastDeviceColors =
        new Dictionary<LightingDevice, List<System.Windows.Media.Color>>();

    // ─── 平台信息读取（仅用于获取 PerKey 键盘 PID/VID，非硬件探测） ───
    private static string GetSystemId() => OmenHardware.GetSystemID()?.Trim() ?? string.Empty;

    private static PlatformSettingsResolver.LightingCapability _cachedCapability;
    private static PlatformSettingsResolver.LightingCapability GetPlatformCapability() {
      if (_cachedCapability == null)
        _cachedCapability = PlatformSettingsResolver.GetLightingCapability(GetSystemId());
      return _cachedCapability;
    }

    // ════════════ HID / MCU 设备操作 ═════════════════════════════════
    public static int OpenHidDevice(int pid, int vid, string interfaceString = "") {
      try {
        Task<int> task = McuGeneralHelper.OpenDevice(pid, vid, interfaceString, "");
        task.Wait();
        return task.Result;
      } catch (AggregateException ae) {
        foreach (var inner in ae.InnerExceptions)
          Logger.Error($"OpenHidDevice AggregateException: {inner.Message}");
        return -1;
      } catch (Exception ex) {
        Logger.Error($"OpenHidDevice Exception: {ex.Message}");
        return -1;
      }
    }

    public static async Task<bool> CloseDeviceAsync(int handle) => await McuGeneralHelper.CloseDevice(handle);

    /// <summary>打开 Per‑Key RGB 键盘（需要平台配置提供 VID/PID）</summary>
    public static int OpenPerKeyKeyboard() {
      var cap = GetPlatformCapability();
      if (!cap.SupportsPerKeyRGB || cap.KeyboardPids == null || cap.KeyboardVid == null)
        return -1;

      foreach (int pid in cap.KeyboardPids) {
        int handle = OpenHidDevice(pid, cap.KeyboardVid.Value, cap.KeyboardInterfaceString ?? "");
        if (handle > 0) return handle;
      }
      return -1;
    }

    // ════════════ 每键 RGB 控制（HID / MCU） ═════════════════════════
    public static async Task<bool> SetPerKeyStaticColor(int handle, byte[] r, byte[] g, byte[] b) =>
        await McuGeneralHelper.SetKeyboardStaticLighting(handle, r, g, b);

    public static async Task<bool> SetPerKeyAnimation(int handle, LightingSetting setting) =>
        await McuGeneralHelper.SetLightingEffect(handle, setting, LightingEffectTarget.ALL_LED_AREA);

    public static async Task<bool> SetPerKeyAudioAnimation(int handle, LightingAudioEffectSetting setting) =>
        await McuKeyboardHelper.SetLightingAudioEffect(handle, setting);

    public static async Task<bool> SetPerKeyBrightness(int handle, byte level) =>
        await McuGeneralHelper.SetKeyboardBrightness(handle, level);

    public static async Task<bool> SetPerKeyLightingOn(int handle) =>
        await McuGeneralHelper.SetKeyboardLightingOn(handle);

    public static async Task<bool> SetPerKeyLightingOff(int handle) =>
        await McuGeneralHelper.SetKeyboardLightingOff(handle);

    public static async Task<bool> SetPerKeyLedOnOff(int handle, List<byte> allKeyStatus) =>
        await McuGeneralHelper.SetKeyboardIndividualLEDOnOff(handle, allKeyStatus);

    public static async Task<bool> StorePerKeyToFlash(int handle) =>
        await McuGeneralHelper.StoreLightingToFlash(handle, LightingEffectTarget.ALL_LED_AREA);

    public static async Task<bool> RestorePerKeyLightingToDefault(int handle) =>
        await McuGeneralHelper.RestoreLightingToDefault(handle);

    public static async Task<LightingSetting> GetPerKeyCurrentEffect(int handle) {
      var (success, setting) = await McuGeneralHelper.GetLightingEffect(handle, LightingEffectTarget.ALL_LED_AREA);
      return success ? setting : null;
    }

    public static async Task<KeyboardLanguage> GetPerKeyLanguage(int handle) {
      var (success, lang) = await McuGeneralHelper.GetKeyboardLanguage(handle);
      return success ? lang : KeyboardLanguage.LANGUAGE_US_ENGLISH;
    }

    public static async Task<Dictionary<KeyboardStatusType, CommonToggleEnum>> GetPerKeyKeyStatus(int handle) =>
        await McuGeneralHelper.GetKeyboardKeyStatus(handle);

    public static async Task<byte> GetPerKeyBrightness(int handle) {
      try {
        var (success, brightness) = await McuKeyboardHelper.GetAllKeyboardBrightness(handle);
        return success ? brightness : (byte)0;
      } catch { return 0; }
    }

    // ════════════ 四区 / 灯条 WMI 控制 ═══════════════════════════════
    public static void SetZoneStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors,
        byte brightness, LightingControlInterface controlInterface) {
      if (colors == null || colors.Count != 4)
        throw new ArgumentException("必须提供 4 个颜色");

      _lastDeviceColors[device] = new List<System.Windows.Media.Color>(colors);
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      switch (controlInterface) {
        case LightingControlInterface.Dojo: {
            byte[] data = new byte[128];
            data[0] = target;
            data[1] = 0;            // 静态模式
            data[3] = brightness;
            data[6] = 4;
            for (int i = 0; i < 4; i++) {
              data[7 + i * 3] = colors[i].R;
              data[8 + i * 3] = colors[i].G;
              data[9 + i * 3] = colors[i].B;
            }
            SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.BasicFourZone: {
            byte[] table = SendOmenBiosWmi(2, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
            if (table == null || table.Length < 37) return;
            for (int i = 0; i < 4; i++) {
              int idx = 25 + i * 3;
              table[idx] = colors[i].R;
              table[idx + 1] = colors[i].G;
              table[idx + 2] = colors[i].B;
            }
            SendOmenBiosWmi(3, table, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.Drax:
        case LightingControlInterface.Noctali: {
            var c = colors[0];
            byte[] data = new byte[8] { 0, 0, 0, brightness, 1, c.R, c.G, c.B };
            SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
            break;
          }
        default:
          throw new ArgumentOutOfRangeException(nameof(controlInterface));
      }
    }

    public static void SetZoneAnimation(LightingDevice device, byte effectId, byte speed, byte direction,
        byte theme, List<System.Windows.Media.Color> customColors, byte brightness,
        LightingControlInterface controlInterface) {
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (controlInterface == LightingControlInterface.Dojo) {
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
      } else {
        // 效果 ID 映射（Dojo → Drax）
        byte draxEffect;
        if (effectId == 2) draxEffect = 2;       // 颜色循环
        else if (effectId == 4) draxEffect = 1;   // 呼吸
        else return;                               // 不支持的效果

        // 速度 → 间隔转换
        byte interval = speed == 0 ? (byte)10 : (speed == 1 ? (byte)5 : (byte)2);

        // 颜色准备：优先自定义，其次最后设置的静态颜色，最后默认白色
        List<System.Windows.Media.Color> animColors;
        if (theme == 4 && customColors != null && customColors.Count > 0)
          animColors = customColors;
        else if (_lastDeviceColors.TryGetValue(device, out var last) && last.Count > 0)
          animColors = new List<System.Windows.Media.Color> { last[0] };
        else
          animColors = new List<System.Windows.Media.Color> { System.Windows.Media.Color.FromRgb(255, 255, 255) };

        // 构建变长命令
        byte[] data = new byte[5 + animColors.Count * 3];
        data[0] = 0;                    // 固定
        data[1] = draxEffect;           // 效果码
        data[2] = interval;             // 时间间隔
        data[3] = brightness;           // 亮度
        data[4] = (byte)animColors.Count; // 颜色数量
        for (int i = 0; i < animColors.Count; i++) {
          data[5 + i * 3] = animColors[i].R;
          data[6 + i * 3] = animColors[i].G;
          data[7 + i * 3] = animColors[i].B;
        }

        SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
      }
    }

    public static void SetZoneBrightness(LightingDevice device, byte brightness,
        LightingControlInterface controlInterface) {
      switch (controlInterface) {
        case LightingControlInterface.Dojo: {
            byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
            byte[] data = new byte[128];
            data[0] = target;
            data[3] = brightness;
            SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.BasicFourZone: {
            byte[] data = new byte[4] { brightness, 0, 0, 0 };
            SendOmenBiosWmi(5, data, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.Drax:
        case LightingControlInterface.Noctali:
          if (_lastDeviceColors.TryGetValue(device, out var colors) && colors.Count > 0)
            SetZoneStaticColor(device, colors, brightness, controlInterface);
          break;
      }
    }

    public static void SetZoneOff(LightingDevice device, LightingControlInterface controlInterface) {
      switch (controlInterface) {
        case LightingControlInterface.Noctali:
          SendOmenBiosWmi(7, new byte[2] { 0, 0xFF }, 0, WMI_COMMAND_ID);
          break;
        case LightingControlInterface.Drax:
          SetZoneStaticColor(device,
              new List<System.Windows.Media.Color> {
                            System.Windows.Media.Color.FromRgb(0,0,0), System.Windows.Media.Color.FromRgb(0,0,0),
                            System.Windows.Media.Color.FromRgb(0,0,0), System.Windows.Media.Color.FromRgb(0,0,0)
              }, 0, LightingControlInterface.Drax);
          break;
        default:
          SetZoneBrightness(device, 0, controlInterface);
          break;
      }
    }

    // ─── 状态读取（WMI 查询，供菜单显示，无硬件探测） ─────────────────
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
