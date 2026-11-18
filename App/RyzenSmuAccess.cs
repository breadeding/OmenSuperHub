using System;
using System.Reflection;
using System.Threading;

namespace OmenSuperHub {
  /// <summary>CPU 家族分类：决定 SMU 消息号与邮箱地址。</summary>
  internal enum SmuCpuKind {
    Unknown = 0,
    Apu = 1,    // Phoenix / Hawk Point / Strix Point（H、HS、U 系列）
    Hx = 2      // Dragon Range / Fire Range（桌面下放 HX 系列）
  }

  /// <summary>
  /// 通过 PawnIO 的 RyzenSMU 模块直接读写 AMD SMU 的 SMN 寄存器，
  /// 用于修改 BIOS 不会自动恢复的平台功耗上限。
  ///
  /// 背景：拔掉电源后 BIOS 会把 SMU 里的功耗限制压低，插回电源时不恢复，
  /// 必须重启才复原。BIOS WMI 的 0x29 命令只能改 PL1/PL2/PPT FAST，
  /// 碰不到 APU 平台上的 PPT LIMIT APU 等字段，因此需要直写 SMU。
  ///
  /// APU 与 HX 的消息号、邮箱地址均不同（数据取自 RyzenAdj 源码）：
  ///   参数             APU(0x14/0x15/0x16/0x23)   HX(0x4f/0x3e/0x5f/不支持)
  ///   STAPM / FAST / SLOW / APU_SLOW
  ///
  /// LibreHardwareMonitor 的 PawnIo.LoadModuleFromResource 是 internal，
  /// 这里用反射调用，避免改动上游代码。
  /// </summary>
  internal static class RyzenSmuAccess {
    // ── 邮箱地址 ─────────────────────────────────────────────────────────
    // APU：RyzenAdj nb_smu_ops.c 的 ADDR_2 组
    private const uint APU_MP1_MSG = 0x03B10528;
    private const uint APU_MP1_REP = 0x03B10578;
    private const uint APU_MP1_ARG_BASE = 0x03B10998;

    // HX：ADDR_4 组（Dragon Range / Fire Range）
    private const uint HX_MP1_MSG = 0x03B10530;
    private const uint HX_MP1_REP = 0x03B1057C;
    private const uint HX_MP1_ARG_BASE = 0x03B109C4;

    private const uint REP_MSG_OK = 0x1;
    private const string ModuleResourceName = "LibreHardwareMonitor.Resources.PawnIo.RyzenSMU.bin";

    private static object _pawnInstance;
    private static MethodInfo _executeMethod;
    private static bool _initFailed;
    private static SmuCpuKind _cpuKind = SmuCpuKind.Unknown;
    private static readonly object _gate = new object();

    /// <summary>通道是否可用。</summary>
    public static bool IsAvailable {
      get { return EnsureOpen(); }
    }

    /// <summary>探测到的 CPU 家族类型。</summary>
    public static SmuCpuKind CpuKind {
      get { EnsureOpen(); return _cpuKind; }
    }

    private static uint MsgAddr {
      get { return _cpuKind == SmuCpuKind.Hx ? HX_MP1_MSG : APU_MP1_MSG; }
    }
    private static uint RepAddr {
      get { return _cpuKind == SmuCpuKind.Hx ? HX_MP1_REP : APU_MP1_REP; }
    }
    private static uint ArgBaseAddr {
      get { return _cpuKind == SmuCpuKind.Hx ? HX_MP1_ARG_BASE : APU_MP1_ARG_BASE; }
    }

    private static bool EnsureOpen() {
      lock (_gate) {
        if (_pawnInstance != null) return true;
        if (_initFailed) return false;

        try {
          Assembly lhmAsm = typeof(LibreHardwareMonitor.Hardware.Computer).Assembly;
          Type pawnType = lhmAsm.GetType("LibreHardwareMonitor.PawnIo.PawnIo", false);
          if (pawnType == null) { _initFailed = true; return false; }

          MethodInfo loadMethod = pawnType.GetMethod(
            "LoadModuleFromResource", BindingFlags.NonPublic | BindingFlags.Static);
          if (loadMethod == null) { _initFailed = true; return false; }

          object inst = loadMethod.Invoke(null, new object[] { lhmAsm, ModuleResourceName });
          if (inst == null) { _initFailed = true; return false; }

          PropertyInfo isLoadedProp = pawnType.GetProperty("IsLoaded");
          if (isLoadedProp == null || !(bool)isLoadedProp.GetValue(inst, null)) {
            _initFailed = true;
            return false;
          }

          _executeMethod = pawnType.GetMethod("Execute");
          if (_executeMethod == null) { _initFailed = true; return false; }

          _pawnInstance = inst;
          _cpuKind = DetectCpuKind();
          Logger.Info($"SMU 通道已就绪（CPU 类型：{_cpuKind}）。");
          return true;
        } catch (Exception ex) {
          _initFailed = true;
          Logger.Info($"SMU 通道不可用（{ex.GetType().Name}），将跳过平台功耗上限写入。");
          return false;
        }
      }
    }

    /// <summary>
    /// 通过 CPU 型号名与 SMU Code Name 判定家族。
    /// 型号名优先（"HX" 字样最可靠），Code Name 仅作兜底。
    /// 判定失败时按 APU 处理（覆盖面更广）。
    /// </summary>
    private static SmuCpuKind DetectCpuKind() {
      // 一级：Win32_Processor 型号名。桌面下放的 HX 机型型号必带 "HX"
      // （7945HX / 8945HX 等），这是最直接、不依赖枚举序号的依据。
      try {
        string name = string.Empty;
        using (var searcher = new System.Management.ManagementObjectSearcher(
                 "SELECT Name FROM Win32_Processor"))
        using (var collection = searcher.Get()) {
          foreach (System.Management.ManagementBaseObject o in collection) {
            object n = o["Name"];
            if (n != null) { name = n.ToString(); break; }
          }
        }
        if (!string.IsNullOrEmpty(name)) {
          string up = name.ToUpperInvariant();
          if (up.Contains("HX") || up.Contains("DRAGON RANGE") || up.Contains("FIRE RANGE"))
            return SmuCpuKind.Hx;
        }
      } catch { }

      // 二级：SMU Code Name，必须用 LHM CpuCodeName 的实际序号
      // （模块返回值被直接强转为该枚举）：
      //   FireFlight=19, Phoenix=23, Phoenix2=24,
      //   DragonRange=28, HawkPoint=30, StrixPoint=31, StrixHalo=32
      // 只有 DragonRange(28) 属桌面下放 HX 家族；
      // HawkPoint(30) 是 APU，误判会导致使用错误的邮箱地址写入。
      try {
        long[] code = (long[])_executeMethod.Invoke(_pawnInstance,
          new object[] { "ioctl_get_code_name", new long[0], 1 });
        int cn = (int)(code[0] & 0xFFFFFFFFL);
        if (cn == 28) return SmuCpuKind.Hx;
      } catch { }

      return SmuCpuKind.Apu;
    }

    // ── SMN 寄存器读写 ───────────────────────────────────────────────────
    private static uint ReadSmn(uint address) {
      long[] r = (long[])_executeMethod.Invoke(_pawnInstance,
        new object[] { "ioctl_read_smu_register", new long[] { address }, 1 });
      return unchecked((uint)(r[0] & 0xFFFFFFFFL));
    }

    private static void WriteSmn(uint address, uint value) {
      _executeMethod.Invoke(_pawnInstance,
        new object[] { "ioctl_write_smu_register", new long[] { address, value }, 0 });
    }

    /// <summary>按 ryzenadj 的 smu_service_req 流程发送消息。0x1 = OK。</summary>
    private static uint SendSmu(uint messageId, uint arg0, int timeoutMs) {
      WriteSmn(RepAddr, 0);

      for (int i = 0; i < 6; i++)
        WriteSmn(ArgBaseAddr + (uint)(4 * i), i == 0 ? arg0 : 0u);

      WriteSmn(MsgAddr, messageId);

      int waited = 0;
      while (waited < timeoutMs) {
        uint resp = ReadSmn(RepAddr);
        if (resp != 0) return resp;
        Thread.Sleep(5);
        waited += 5;
      }
      return 0;
    }

    // ── 消息号（APU / HX 各自一套）────────────────────────────────────────
    private static uint MsgStapmLimit { get { return _cpuKind == SmuCpuKind.Hx ? 0x4Fu : 0x14u; } }
    private static uint MsgFastLimit { get { return _cpuKind == SmuCpuKind.Hx ? 0x3Eu : 0x15u; } }
    private static uint MsgSlowLimit { get { return _cpuKind == SmuCpuKind.Hx ? 0x5Fu : 0x16u; } }
    /// <summary>PPT LIMIT APU：仅 APU 平台支持，HX 返回 0 表示不适用。</summary>
    private static uint MsgApuSlowLimit { get { return _cpuKind == SmuCpuKind.Hx ? 0u : 0x23u; } }

    // ── 对外接口 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 设置平台持续功耗上限。
    /// APU：同时写 STAPM / FAST / SLOW / APU_SLOW（APU_SLOW 决定拔电残留的那道墙）。
    /// HX ：写 STAPM / FAST / SLOW（不支持 APU_SLOW）。
    /// </summary>
    /// <returns>true 表示所有必要消息都被 SMU 接受。</returns>
    public static bool SetPlatformPowerLimit(int watts) {
      if (watts < 5 || watts > 250) return false;
      if (!EnsureOpen()) return false;

      try {
        lock (_gate) {
          uint mw = (uint)(watts * 1000);
          bool allOk = true;

          allOk &= SendSmu(MsgStapmLimit, mw, 2000) == REP_MSG_OK;
          allOk &= SendSmu(MsgFastLimit, mw, 2000) == REP_MSG_OK;
          allOk &= SendSmu(MsgSlowLimit, mw, 2000) == REP_MSG_OK;

          uint apuMsg = MsgApuSlowLimit;
          if (apuMsg != 0) {
            // APU 平台的这个字段是拔电后被压低、插电不恢复的关键项。
            // 若 SMU 拒绝（个别固件不支持），不视为整体失败。
            SendSmu(apuMsg, mw, 2000);
          }

          return allOk;
        }
      } catch (Exception ex) {
        Logger.Error($"SMU 写入异常: {ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// 读取当前平台功耗上限（PM 表 idx 3）。失败返回 -1。
    /// APU 上该字段是 PPT LIMIT APU，HX 上为其他含义，仅作参考。
    /// </summary>
    public static int GetPlatformPowerLimit() {
      if (!EnsureOpen()) return -1;
      try {
        lock (_gate) {
          _executeMethod.Invoke(_pawnInstance,
            new object[] { "ioctl_resolve_pm_table", new long[0], 2 });
          _executeMethod.Invoke(_pawnInstance,
            new object[] { "ioctl_update_pm_table", new long[0], 0 });
          Thread.Sleep(120);

          long[] pm = (long[])_executeMethod.Invoke(_pawnInstance,
            new object[] { "ioctl_read_pm_table", new long[0], 1024 });
          if (pm == null || pm.Length < 4) return -1;

          uint raw = unchecked((uint)(pm[3] & 0xFFFFFFFFL));
          float f = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
          if (float.IsNaN(f) || f < 0 || f > 500) return -1;
          return (int)Math.Round(f);
        }
      } catch {
        return -1;
      }
    }

    /// <summary>SMU 自检（消息 0x01），仅用于诊断。</summary>
    public static bool TestChannel() {
      if (!EnsureOpen()) return false;
      try {
        lock (_gate) {
          return SendSmu(0x01, 0x47, 2000) == REP_MSG_OK;
        }
      } catch {
        return false;
      }
    }
  }
}
