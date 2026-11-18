using System;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HP.Omen.Core.Model.Device.Models;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  /// <summary>
  /// CPU 功率模式。全局设置，接管 CPU 的 PL1/PL2 以及 SMU 平台功耗上限。
  /// GPU 一侧的 Tgp/Ppab/dState 仍由预设与「性能控制」中的滑块管理。
  /// </summary>
  public enum PowerLimitMode {
    NotSet = 0,   // 不设置：不写入任何功耗值，交还 BIOS
    High = 1,     // 高性能：平台允许的最高功耗
    Balanced = 2, // 均衡
    Quiet = 3,    // 节能
    Custom = 4    // 自定义：由滑块决定
  }

  static partial class Program {
    // ── 功率模式状态 ────────────────────────────────────────────────────────
    static PowerLimitMode powerLimitMode = PowerLimitMode.NotSet;
    static int powerLimitCustomW = 100;
    static int _lastAppliedPl1 = -1;              // 上次成功下发的 PL1
    static int _lastAppliedSmuW = -1;             // 上次成功写入 SMU 的功耗值
    static DateTime _lastApplyTime = DateTime.MinValue;
    static bool _suppressPowerLimitEvents = false; // 程序化修改滑块时屏蔽 ValueChanged
    static int _adapterEvalGeneration = 0;         // 插拔代次，用于取消过期复核
    static bool? _lastAdapterVerdict = null;
    static bool? _cachedAdapterVerdict = null;
    static string _lastAdapterSource = "";
    static bool _adapterDowngradeLogged = false;   // 适配器降级提示只记一次

    /// <summary>
    /// 插电状态下的功耗基线。用于「上电复原」：
    /// 插电时记录 BIOS 给出的正常值，离电后 BIOS 会压低且不恢复，再插回时写回。
    /// </summary>
    static int _acSnapshotPl1 = -1;
    static int _acSnapshotSmu = -1;
    static bool _acBaselineCaptured = false;
    static readonly object _acBaselineGate = new object();

    // ── 滑块拖动期间的 SMU 合并写入 ─────────────────────────────────────────
    // 拖动时每格都同步写 SMU 会让 UI 线程卡顿（首次还要加载 PawnIO 驱动），
    // 这里改为后台串行合并：只保留最新目标值，旧的待写值被覆盖。
    static readonly object _smuFlushGate = new object();
    static int _pendingSmuW = -1;
    static bool _pendingSmuForce = false;
    static bool _pendingSmuLog = false;
    static bool _smuFlusherRunning = false;
    // 串行化「同步复原写入」与「后台合并写入」，防止旧值在复原之后才落地
    static readonly object _smuWriteLock = new object();
    static int _smuWriteGeneration = 0;

    // ── 菜单引用 ────────────────────────────────────────────────────────────
    static ToolStripMenuItem powerLimitModeMenu, powerLimitValueLabel, powerLimitTipItem, powerLimitCustomItem;
    static ToolStripTrackBar powerLimitTrackBar;

    // ═══════════════════════════════════════════════════════════════════════
    // 模式取值
    // ═══════════════════════════════════════════════════════════════════════

    static int ClampPl1(int watts) {
      if (watts < 10) return 10;
      if (watts > 254) return 254;
      return watts;
    }

    /// <summary>
    /// 通过反射读取 PlatformSettings 上的整型字段，取不到时用回退值。
    /// </summary>
    static int GetPlatformInt(string member, int fallback) {
      if (platformSettings == null) return fallback;
      try {
        Type t = platformSettings.GetType();
        PropertyInfo pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
        if (pi != null) {
          object v = pi.GetValue(platformSettings, null);
          if (v != null) {
            int n = Convert.ToInt32(v);
            if (n > 0) return n;
          }
          return fallback;
        }
        FieldInfo fi = t.GetField(member, BindingFlags.Public | BindingFlags.Instance);
        if (fi != null) {
          object v = fi.GetValue(platformSettings);
          if (v != null) {
            int n = Convert.ToInt32(v);
            if (n > 0) return n;
          }
        }
      } catch { }
      return fallback;
    }

    /// <summary>
    /// 各模式对应的典型平台功耗值（用于菜单标注与硬件写入）。
    /// 返回 -1 表示该模式不写入硬件。
    /// </summary>
    static int ResolvePl1ForMode(PowerLimitMode mode) {
      int upperDefault = GetPlatformInt("NbPL1UpperBoundDefault", 55);

      switch (mode) {
        case PowerLimitMode.High:
          // 依次尝试：大师模式上限 → 超频后上限 → 游戏上限 → 性能上限
          // 8BCA 上 UnleashedModeMaxPL1 为 0（不支持大师模式），
          // 此时 NbPL1UpperBoundPerformanceWithOC(100W) 才是真实天花板
          return ClampPl1(GetPlatformInt("UnleashedModeMaxPL1",
                  GetPlatformInt("NbPL1UpperBoundPerformanceWithOC",
                  GetPlatformInt("NbPL1UpperBoundGaming",
                  GetPlatformInt("NbPL1UpperBoundPerformance", 254)))));

        case PowerLimitMode.Balanced:
          return ClampPl1(upperDefault);

        case PowerLimitMode.Quiet:
          // 优先取平台定义的功耗下限，取不到时按均衡值的 60% 估算
          return ClampPl1(GetPlatformInt("NbPL1LowerBound", (int)Math.Round(upperDefault * 0.6)));

        case PowerLimitMode.Custom:
          return ClampPl1(powerLimitCustomW);

        default:
          return -1;
      }
    }

    static string PowerLimitModeToString(PowerLimitMode mode) {
      switch (mode) {
        case PowerLimitMode.High: return "high";
        case PowerLimitMode.Balanced: return "balanced";
        case PowerLimitMode.Quiet: return "quiet";
        case PowerLimitMode.Custom: return "custom";
        default: return "null";
      }
    }

    static PowerLimitMode PowerLimitModeFromString(string value) {
      switch (value) {
        case "high": case "max": return PowerLimitMode.High;   // 兼容旧版 "max"
        case "balanced": return PowerLimitMode.Balanced;
        case "quiet": return PowerLimitMode.Quiet;
        case "custom": return PowerLimitMode.Custom;
        default: return PowerLimitMode.NotSet;
      }
    }

    /// <summary>模式显示名，附带典型功耗标注。</summary>
    static string PowerLimitModeLabel(PowerLimitMode mode) {
      switch (mode) {
        case PowerLimitMode.High: return $"{Strings.PowerLimitHigh}（{ResolvePl1ForMode(PowerLimitMode.High)}W）";
        case PowerLimitMode.Balanced: return $"{Strings.PowerLimitBalanced}（{ResolvePl1ForMode(PowerLimitMode.Balanced)}W）";
        case PowerLimitMode.Quiet: return $"{Strings.PowerLimitQuiet}（{ResolvePl1ForMode(PowerLimitMode.Quiet)}W）";
        case PowerLimitMode.Custom: return string.Format(Strings.PowerLimitCustomFmt, powerLimitCustomW);
        default: return Strings.NotSet;
      }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 适配器识别
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>判断当前适配器是否足以支撑高功耗。null = 无法判断。</summary>
    static bool? ProbeAdapterAdequacy() {
      if (_cachedAdapterVerdict.HasValue) return _cachedAdapterVerdict;

      bool? verdict = QueryHpAdapterNative();
      string source = "HP SDK";
      if (!verdict.HasValue) {
        verdict = QueryBatteryDrainVerdict();
        source = "电池放电速率";
      }
      if (!verdict.HasValue) {
        verdict = true;   // 无证据时按「插电即可用」处理，避免误判降频
        source = "插电状态兜底";
      }

      _cachedAdapterVerdict = verdict;

      if (_lastAdapterVerdict != verdict || _lastAdapterSource != source) {
        _lastAdapterVerdict = verdict;
        _lastAdapterSource = source;
        Logger.Info(verdict.Value
          ? $"适配器判定：高功率适配器（来源：{source}）。"
          : $"适配器判定：功率不足（来源：{source}）。");
      }
      return verdict;
    }

    /// <summary>第一级：HP 原生接口，只采信语义明确的低功率布尔标记。</summary>
    static bool? QueryHpAdapterNative() {
      try {
        Type dm = typeof(DeviceModel);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        PropertyInfo lowProp = dm.GetProperty("IsLowWattage", flags);
        if (lowProp != null && lowProp.PropertyType == typeof(bool)) {
          object v = lowProp.GetValue(null, null);
          if (v is bool) return !(bool)v;
        }
        FieldInfo lowField = dm.GetField("IsLowWattage", flags);
        if (lowField != null && lowField.FieldType == typeof(bool)) {
          object v = lowField.GetValue(null);
          if (v is bool) return !(bool)v;
        }
      } catch (Exception ex) {
        if (_lastAdapterSource != "HP SDK 不可用") {
          _lastAdapterSource = "HP SDK 不可用";
          Logger.Info($"HP 适配器识别接口不可用，改用电池放电速率判断（{ex.GetType().Name}）。");
        }
      }
      return null;
    }

    /// <summary>第二级：插电时电池是否仍在明显放电（适配器带不动）。</summary>
    static bool? QueryBatteryDrainVerdict() {
      try {
        using (var searcher = new ManagementObjectSearcher(
                 @"root\wmi", "SELECT PowerOnline, Discharging, Rate FROM MSAcpi_BatteryStatus"))
        using (var collection = searcher.Get()) {
          foreach (ManagementBaseObject mo in collection) {
            object onlineObj = mo["PowerOnline"];
            if (onlineObj == null) continue;
            if (!Convert.ToBoolean(onlineObj)) return null;

            object rateObj = mo["Rate"];
            if (rateObj == null) return null;

            long rateMw;
            if (rateObj is uint) rateMw = unchecked((int)(uint)rateObj);
            else if (rateObj is int) rateMw = (int)rateObj;
            else rateMw = Convert.ToInt64(rateObj);

            // 0x80000000 = BATTERY_UNKNOWN_RATE，读数无效，不能当作放电
            if (rateMw == -2147483648L || rateMw == 0x80000000L) return null;

            if (rateMw < -15000) return false;   // 放电 >15W 视为带不动

            object dischargingObj = mo["Discharging"];
            if (dischargingObj != null && Convert.ToBoolean(dischargingObj) && rateMw <= -5000)
              return false;

            return true;
          }
        }
      } catch { }
      return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 应用与复原
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 按当前功率模式写入功耗限制。
    /// force=true 时即使目标值与上次相同也强制重发（EC 会对相同值去重，
    /// 插电复原场景必须重新握手）。
    /// </summary>
    static void ApplyPowerLimitMode(bool force) {
      if (!isCPUPowerControlSupported) return;

      if (!powerOnline) {
        // 电池供电：不写入任何限制，交给 BIOS
        _lastAppliedPl1 = -1;
        _lastAppliedSmuW = -1;
        return;
      }

      PowerLimitMode mode = powerLimitMode;
      if (mode == PowerLimitMode.NotSet) {
        // 「不设置」不主动施加模式限制，但仍执行独立的「上电复原」：
        // 把插电基线写回，抵消离电期间 BIOS 压低且插回不恢复的问题。
        // 基线取自插电状态下 SMU 的真实值，因此不会改变 BIOS 原有的充电设定。
        RestorePowerBaseline(force);
        return;
      }

      int target = ResolvePl1ForMode(mode);
      if (target < 0) return;

      // 高性能模式下若适配器带不动，降级为均衡限制
      if (mode == PowerLimitMode.High && ProbeAdapterAdequacy() == false) {
        target = ResolvePl1ForMode(PowerLimitMode.Balanced);
        if (!_adapterDowngradeLogged) {
          _adapterDowngradeLogged = true;
          Logger.Info($"高性能受适配器限制，降级为均衡限制 {target}W。");
        }
      }

      WritePowerLimit(target, force);
    }

    /// <summary>
    /// 把功耗值同时写入 BIOS（PL1/PL2）与 SMU（平台上限）。
    /// </summary>
    static void WritePowerLimit(int watts, bool force, bool quiet = false) {
      if (!force && watts == _lastAppliedPl1 && (DateTime.Now - _lastApplyTime).TotalSeconds < 10)
        return;

      SetCpuPowerLimit((byte)watts);

      bool changed = (watts != _lastAppliedPl1);
      _lastAppliedPl1 = watts;
      _lastApplyTime = DateTime.Now;
      // quiet 用于滑块连续拖动，避免逐格刷日志
      if (!quiet && (changed || force))
        Logger.Info($"功率模式：下发 CPU 功耗限制 {watts}W。");

      // BIOS 的 0x29 只能改 PL1/PL2/PPT FAST，碰不到 SMU 里被 BIOS
      // 压低且不恢复的平台上限（APU 的 PPT LIMIT APU）。这里补写 SMU。
      ApplySmuPlatformLimit(watts, force, quiet);
    }

    /// <summary>
    /// 上电复原：把插电基线写回 BIOS 与 SMU。
    /// 这是独立于功率模式的机制——即使模式为「不设置」也生效，
    /// 目的只是消除「离电后 BIOS 压低、插回不恢复」的残留限制，
    /// 不会把功耗抬到 BIOS 在充电状态下原本设定的水平之上。
    /// </summary>
    static void RestorePowerBaseline(bool force) {
      int target;
      lock (_acBaselineGate) {
        if (_acSnapshotSmu > 0) target = _acSnapshotPl1 > 0 ? _acSnapshotPl1 : _acSnapshotSmu;
        else return;   // 尚未建立基线（例如启动即离电），无可复原内容
      }

      if (force) {
        // 插电事件/模式切换：先丢弃拖动时遗留的待写值，避免旧值后落地覆盖复原结果
        ClearPendingSmuWrite();

        // 读一次 SMU 现值，已达标就跳过，减少无谓写入
        int current = RyzenSmuAccess.IsAvailable ? RyzenSmuAccess.GetPlatformPowerLimit() : -1;
        if (current > 0 && Math.Abs(current - target) < 2) {
          _lastAppliedPl1 = target;
          _lastAppliedSmuW = target;
          _lastApplyTime = DateTime.Now;
          return;
        }

        SetCpuPowerLimit((byte)target);
        _lastAppliedPl1 = target;
        _lastApplyTime = DateTime.Now;

        if (!RyzenSmuAccess.IsAvailable) {
          Logger.Info($"上电复原：BIOS 功耗限制已恢复为 {target}W（SMU 通道不可用）。");
          return;
        }

        bool smuOk;
        lock (_smuWriteLock) {
          smuOk = RyzenSmuAccess.SetPlatformPowerLimit(target);
          if (smuOk) _lastAppliedSmuW = target;
        }
        if (smuOk) {
          Logger.Info($"上电复原：平台功耗上限已恢复为 {target}W"
            + (current > 0 ? $"（离电期间为 {current}W）。" : "。"));
        } else {
          Logger.Info($"上电复原：平台功耗上限写回 {target}W 失败。");
        }
        return;
      }

      // 周期保底：仅当上次下发值与基线不一致时才补写。
      // 正常插电且已复原时完全不触碰硬件，避免每 30 秒一次冗余写入。
      if (target == _lastAppliedPl1) return;

      SetCpuPowerLimit((byte)target);
      _lastAppliedPl1 = target;
      _lastApplyTime = DateTime.Now;
      bool ok;
      lock (_smuWriteLock) {
        ok = !RyzenSmuAccess.IsAvailable || RyzenSmuAccess.SetPlatformPowerLimit(target);
        if (ok) _lastAppliedSmuW = target;
      }
      if (ok) {
        Logger.Info($"上电复原：保底重申平台功耗上限 {target}W。");
      }
    }

    /// <summary>丢弃尚未执行的合并 SMU 写入（插拔/复原时调用）。</summary>
    static void ClearPendingSmuWrite() {
      lock (_smuFlushGate) {
        _pendingSmuW = -1;
        _pendingSmuForce = false;
        _pendingSmuLog = false;
      }
      lock (_smuWriteLock) {
        _smuWriteGeneration++;   // 使正在执行中的旧写入作废
      }
    }

    /// <summary>
    /// 建立/刷新插电基线。只在「首次捕获」或显式要求刷新时更新，
    /// 避免运行期 RefreshMenu 把已被压低的值误当作基线。
    /// </summary>
    static void CaptureAcBaseline(bool force) {
      if (!powerOnline || !isCPUPowerControlSupported) return;

      lock (_acBaselineGate) {
        if (_acBaselineCaptured && !force) return;

        // 已选模式时，模式目标值才是「应有的插电水平」，优先采用；
        // 避免启动时 BIOS 仍残留压低值（插电但未复原）被当成基线记录下来。
        int modeValue = ResolvePl1ForMode(powerLimitMode);

        int smu = RyzenSmuAccess.IsAvailable ? RyzenSmuAccess.GetPlatformPowerLimit() : -1;
        if (smu > 0 && smu <= 250) {
          // 模式值与 SMU 实测偏差明显时，以模式值为准（残留压低场景）
          if (modeValue > 0 && Math.Abs(smu - modeValue) >= 2) {
            _acSnapshotSmu = modeValue;
            _acSnapshotPl1 = modeValue;
            _acBaselineCaptured = true;
            Logger.Info($"上电复原：记录插电基线 {modeValue}W（SMU 实测 {smu}W 偏低，按模式值取）。");
            return;
          }
          _acSnapshotSmu = smu;
          _acSnapshotPl1 = smu;
          _acBaselineCaptured = true;
          Logger.Info($"上电复原：记录插电基线 {smu}W。");
          return;
        }

        // SMU 读不到时用当前模式的推算值兜底；「不设置」模式则依赖旧 cpuPower
        int pl1 = modeValue;
        if (pl1 <= 0) {
          if (cpuPower == "max") pl1 = ClampPl1(GetPlatformInt("NbPL1UpperBoundPerformance", 65));
          else if (cpuPower.Contains(" W")) {
            int v;
            if (int.TryParse(cpuPower.Replace(" W", "").Trim(), out v) && v >= 10 && v <= 254) pl1 = v;
          }
        }
        if (pl1 <= 0) return;   // 拿不到可用值，留待下次再试

        _acSnapshotPl1 = pl1;
        _acSnapshotSmu = pl1;
        _acBaselineCaptured = true;
        Logger.Info($"上电复原：记录插电基线 {pl1}W（SMU 读数不可用）。");
      }
    }

    /// <summary>
    /// 以指定瓦数更新插电基线。用于「不设置」模式下用户手动指定的 CPU 功耗：
    /// 该值就是这一电源状态下应保持的水平，复原机制据此对齐。
    /// </summary>
    static void SetAcBaseline(int watts) {
      if (watts <= 0) return;
      lock (_acBaselineGate) {
        _acSnapshotPl1 = watts;
        _acSnapshotSmu = watts;
        _acBaselineCaptured = true;
      }
    }

    /// <summary>
    /// 解析并应用旧版「CPU功耗」手动值（cpuPower）。仅「不设置」模式使用。
    /// 写入后同步更新上电复原基线，避免复原机制与手动值互相覆盖。
    /// </summary>
    static void ApplyManualCpuPowerValue() {
      if (!isCPUPowerControlSupported || !powerOnline) return;

      int target = -1;
      if (cpuPower == "max") {
        target = 254;
      } else if (cpuPower.Contains(" W")) {
        int v;
        if (int.TryParse(cpuPower.Replace(" W", "").Trim(), out v) && v >= 10 && v <= 254) target = v;
      }
      if (target <= 0) return;

      SetCpuPowerLimit((byte)target);
      _lastAppliedPl1 = target;
      _lastApplyTime = DateTime.Now;
      SetAcBaseline(target);
      ApplySmuPlatformLimit(target, true);
    }

    /// <summary>
    /// 同步 SMU 的平台功耗上限。
    /// 所有 SMU 写入都经单一后台线程串行执行：滑块快速拖动时只保留最新值，
    /// 既不阻塞 UI 线程，也不会让多个线程同时操作 SMN 邮箱。
    /// force=true 表示值未变也重发（插电复原必须重新握手）。
    /// </summary>
    static void ApplySmuPlatformLimit(int watts, bool force, bool quiet = false) {
      if (!RyzenSmuAccess.IsAvailable) return;

      // 非强制且值未变 → 无事可做
      if (!force && Math.Abs(watts - _lastAppliedSmuW) < 1) return;

      lock (_smuFlushGate) {
        _pendingSmuW = watts;
        _pendingSmuForce = _pendingSmuForce || force;
        _pendingSmuLog = _pendingSmuLog || !quiet;
        if (_smuFlusherRunning) return;
        _smuFlusherRunning = true;
      }

      Task.Run(() => {
        while (true) {
          int target;
          bool forced, log;
          lock (_smuFlushGate) {
            if (_pendingSmuW < 0) { _smuFlusherRunning = false; return; }
            target = _pendingSmuW;
            forced = _pendingSmuForce;
            log = _pendingSmuLog;
            _pendingSmuW = -1;
            _pendingSmuForce = false;
            _pendingSmuLog = false;
          }

          if (!forced && Math.Abs(target - _lastAppliedSmuW) < 1) continue;

          // 出队时记录代次：若期间发生插拔/复原（代次 +1），这次写入已过期
          int genAtDequeue = Volatile.Read(ref _smuWriteGeneration);

          if (!forced && Math.Abs(target - _lastAppliedSmuW) < 1) continue;

          // 后台线程落地前再确认一次仍插着电，避免排队期间拔电后仍写硬件
          if (!powerOnline) continue;
          if (!isCPUPowerControlSupported) continue;

          lock (_smuWriteLock) {
            // 与同步复原路径串行；期间若被 ClearPendingSmuWrite 作废则放弃
            if (genAtDequeue != _smuWriteGeneration) continue;

            int before = _lastAppliedSmuW;
            if (RyzenSmuAccess.SetPlatformPowerLimit(target)) {
              _lastAppliedSmuW = target;
              if (log && (target != before || forced))
                Logger.Info($"功率模式：SMU 平台功耗上限已设为 {target}W。");
            }
          }
        }
      });
    }

    /// <summary>
    /// 插电事件。立即应用一次（含「不设置」模式下的基线复原），
    /// 3 秒后复核适配器能力。
    /// </summary>
    static void OnAcConnected() {
      _adapterEvalGeneration++;
      _adapterDowngradeLogged = false;
      if (!powerOnline) return;

      // 首次插电（或启动即插电）时建立基线，之后不再被刷新，
      // 保证基线始终是 BIOS 在充电状态下的原始设定。
      CaptureAcBaseline(false);
      ApplyPowerLimitMode(true);

      // 3 秒后复核：PD 头接入瞬间电池可能短暂放电，适配器能力判定需重算
      int generation = _adapterEvalGeneration;
      Task.Delay(3000).ContinueWith(_ => {
        if (generation != _adapterEvalGeneration || !powerOnline) return;
        ApplyPowerLimitMode(true);
      });
    }

    /// <summary>拔电事件。不写入硬件，只重置缓存并清空菜单勾选。</summary>
    static void OnAcDisconnected() {
      _adapterEvalGeneration++;
      ClearPendingSmuWrite();   // 丢弃拖动遗留的待写值，离电后不得再写硬件
      _lastAppliedPl1 = -1;
      _lastAppliedSmuW = -1;
      _lastAdapterVerdict = null;
      _lastAdapterSource = "";
      _cachedAdapterVerdict = null;
      _adapterDowngradeLogged = false;
      UpdatePowerLimitMenuState();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 诊断
    // ═══════════════════════════════════════════════════════════════════════

    static string DumpMember(string member) {
      if (platformSettings == null) return "n/a";
      try {
        Type t = platformSettings.GetType();
        PropertyInfo pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
        if (pi != null) {
          object v = pi.GetValue(platformSettings, null);
          if (v == null) return "null";
          if (v is bool) return (bool)v ? "true" : "false";
          return v.ToString();
        }
        FieldInfo fi = t.GetField(member, BindingFlags.Public | BindingFlags.Instance);
        if (fi != null) {
          object v = fi.GetValue(platformSettings);
          if (v == null) return "null";
          if (v is bool) return (bool)v ? "true" : "false";
          return v.ToString();
        }
        return "(无此成员)";
      } catch (Exception ex) {
        return "err:" + ex.GetType().Name;
      }
    }

    /// <summary>启动时输出平台功耗相关参数，便于排查。</summary>
    static void DumpPlatformPowerDiagnostics() {
      try {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("── 平台功耗诊断 ──");

        byte[] design = GetSystemDesignData();
        if (design != null && design.Length >= 12) {
          int adapter = design[0] | (design[1] << 8);
          sb.AppendLine($"额定适配器 {adapter}W | 热策略V{design[3]} | 特性0x{design[4]:X2}");
          sb.AppendLine($"PL4默认 {design[5]}W | DefaultTDP {design[8]} | LoadLine 0x{design[9]:X2}");
        } else {
          sb.AppendLine("SystemDesignData 读取失败");
        }

        if (platformSettings != null) {
          sb.AppendLine($"平台PL1: Default={DumpMember("NbPL1UpperBoundDefault")}"
            + $" Perf={DumpMember("NbPL1UpperBoundPerformance")}"
            + $" Gaming={DumpMember("NbPL1UpperBoundGaming")}"
            + $" WithOC={DumpMember("NbPL1UpperBoundPerformanceWithOC")}");
          sb.AppendLine($"Unleashed: Support={DumpMember("UnleashedModeSupport")}"
            + $" MaxPL1={DumpMember("UnleashedModeMaxPL1")}");
          sb.AppendLine($"Tpp: Support={DumpMember("TppSupport")}"
            + $" Min={DumpMember("TppMinValue")} Max={DumpMember("TppMaxValue")}");
          sb.AppendLine($"AdapterPeakPower={DumpMember("AdapterPeakPower")}"
            + $" ThrottlePerf={DumpMember("temperatureThrottlingPerformance")}");
        } else {
          sb.AppendLine("platformSettings 为 null（GetPlatformSettings 未返回数据）");
        }

        sb.AppendLine($"SMU: 可用={RyzenSmuAccess.IsAvailable} 类型={RyzenSmuAccess.CpuKind}"
          + $" 当前平台上限={RyzenSmuAccess.GetPlatformPowerLimit()}W");
        sb.AppendLine($"当前登记: CpuPower={cpuPower} TppPower={tppPower} 模式={PowerLimitModeToString(powerLimitMode)}");
        sb.AppendLine("──────────────────");
        Logger.Info(sb.ToString());
      } catch (Exception ex) {
        Logger.Error($"[功率诊断] {ex.Message}");
      }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 菜单（合并后的「CPU 功率模式」）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 构建合并后的「CPU 功率模式」菜单：模式选择 + 自定义滑块。
    /// 拖动滑块会自动切到自定义模式并实时写入。
    /// </summary>
    static void BuildPowerLimitMenu(ToolStripMenuItem parent) {
      powerLimitModeMenu = new ToolStripMenuItem(Strings.PowerLimitModeMenu);

      DumpPlatformPowerDiagnostics();
      CaptureAcBaseline(false);

      powerLimitTipItem = new ToolStripMenuItem(Strings.PowerLimitTip) { Enabled = false };
      powerLimitModeMenu.DropDownItems.Add(powerLimitTipItem);
      powerLimitModeMenu.DropDownItems.Add(new ToolStripSeparator());

      powerLimitModeMenu.DropDownItems.Add(CreateMenuItem(
        PowerLimitModeLabel(PowerLimitMode.High), "powerLimitModeGroup",
        (s, e) => SetPowerLimitMode(PowerLimitMode.High), powerLimitMode == PowerLimitMode.High));

      powerLimitModeMenu.DropDownItems.Add(CreateMenuItem(
        PowerLimitModeLabel(PowerLimitMode.Balanced), "powerLimitModeGroup",
        (s, e) => SetPowerLimitMode(PowerLimitMode.Balanced), powerLimitMode == PowerLimitMode.Balanced));

      powerLimitModeMenu.DropDownItems.Add(CreateMenuItem(
        PowerLimitModeLabel(PowerLimitMode.Quiet), "powerLimitModeGroup",
        (s, e) => SetPowerLimitMode(PowerLimitMode.Quiet), powerLimitMode == PowerLimitMode.Quiet));

      powerLimitModeMenu.DropDownItems.Add(new ToolStripSeparator());

      // ── 自定义模式项（带当前瓦数标注，随滑块实时刷新）──
      powerLimitCustomItem = CreateMenuItem(
        PowerLimitModeLabel(PowerLimitMode.Custom), "powerLimitModeGroup",
        (s, e) => SetPowerLimitMode(PowerLimitMode.Custom), powerLimitMode == PowerLimitMode.Custom);
      powerLimitModeMenu.DropDownItems.Add(powerLimitCustomItem);

      // ── 滑块（拖动即进入自定义模式）──
      powerLimitModeMenu.DropDownItems.Add(
        new ToolStripMenuItem(Strings.SetPowerLimitCustomSlider) { Enabled = false });

      powerLimitTrackBar = new ToolStripTrackBar();
      powerLimitTrackBar.Minimum = 10;
      powerLimitTrackBar.Maximum = 254;
      powerLimitTrackBar.Value = Math.Max(10, Math.Min(254, powerLimitCustomW));
      powerLimitTrackBar.TickFrequency = powerLimitTrackBar.Maximum - powerLimitTrackBar.Minimum;
      powerLimitTrackBar.Width = 800;

      powerLimitValueLabel = new ToolStripMenuItem(
        string.Format(Strings.CurrentSliderValueTemp, $"{powerLimitCustomW} W")) { Enabled = false };

      powerLimitTrackBar.ValueChanged += (s, e) => {
        if (_suppressPowerLimitEvents) return;
        int watts = powerLimitTrackBar.Value;
        powerLimitCustomW = watts;
        powerLimitValueLabel.Text = string.Format(Strings.CurrentSliderValueTemp, $"{watts} W");
        if (powerLimitCustomItem != null)
          powerLimitCustomItem.Text = PowerLimitModeLabel(PowerLimitMode.Custom);

        // 电池供电时不写任何功耗值；滑块只记录用户意图，插电后再生效。
        if (!powerOnline) {
          if (powerLimitMode != PowerLimitMode.Custom) {
            powerLimitMode = PowerLimitMode.Custom;
            SaveConfig("PowerLimitMode");
            UpdateCheckedState("powerLimitModeGroup", null, powerLimitCustomItem);
          }
          return;
        }

        // 拖动即实时生效（这是需求），但注册表只在模式切换时落盘一次，
        // 松手时再统一保存，避免快速拖动反复写盘。
        if (powerLimitMode != PowerLimitMode.Custom) {
          powerLimitMode = PowerLimitMode.Custom;
          SaveConfig("PowerLimitMode");
          UpdateCheckedState("powerLimitModeGroup", null, powerLimitCustomItem);
        }
        WritePowerLimit(watts, false, true);
      };

      powerLimitTrackBar.MouseUp += (s, e) => {
        if (_suppressPowerLimitEvents) return;
        powerLimitCustomW = powerLimitTrackBar.Value;
        SaveConfig("PowerLimitMode");
        if (!powerOnline) return;   // 电池供电：只记配置，不动硬件
        // 松手时正式落定：强制重发一次并写日志
        WritePowerLimit(powerLimitCustomW, true);
      };

      powerLimitModeMenu.DropDownItems.Add(powerLimitTrackBar);
      powerLimitModeMenu.DropDownItems.Add(powerLimitValueLabel);

      powerLimitModeMenu.DropDownItems.Add(new ToolStripSeparator());
      powerLimitModeMenu.DropDownItems.Add(CreateMenuItem(
        Strings.NotSet, "powerLimitModeGroup",
        (s, e) => SetPowerLimitMode(PowerLimitMode.NotSet), powerLimitMode == PowerLimitMode.NotSet));

      powerLimitModeMenu.DropDownOpening += (s, e) => UpdatePowerLimitMenuState();

      parent.DropDownItems.Add(powerLimitModeMenu);
      UpdatePowerLimitMenuState();
    }

    static void SetPowerLimitMode(PowerLimitMode mode) {
      powerLimitMode = mode;

      // 选自定义时以滑块当前值为准
      if (mode == PowerLimitMode.Custom && powerLimitTrackBar != null)
        powerLimitCustomW = powerLimitTrackBar.Value;

      SaveConfig("PowerLimitMode");

      if (mode == PowerLimitMode.NotSet) {
        // 交还 BIOS：先按旧 cpuPower 恢复手动设定（若有），并把它登记为
        // 当前电源状态的基线，随后 30 秒保底只重申这一个值，不会打架。
        ApplyManualCpuPowerValue();
      } else {
        ApplyPowerLimitMode(true);
      }

      UpdatePowerLimitMenuState();
    }

    /// <summary>
    /// 同步菜单显示：插电时勾选当前模式，离电时全部取消勾选（交还 BIOS）。
    /// </summary>
    static void UpdatePowerLimitMenuState() {
      if (powerLimitModeMenu == null) return;

      // 电源事件来自 SystemEvents 线程，WinForms 控件必须回到 UI 线程
      if (uiContext != null && SynchronizationContext.Current != uiContext) {
        uiContext.Post(_ => UpdatePowerLimitMenuState(), null);
        return;
      }

      // 刷新带功耗标注的模式项文本
      RefreshModeItemTexts();

      if (powerOnline && powerLimitMode != PowerLimitMode.NotSet) {
        if (powerLimitMode == PowerLimitMode.Custom && powerLimitCustomItem != null) {
          // 自定义项文本含实时瓦数，直接按引用勾选
          UpdateCheckedState("powerLimitModeGroup", null, powerLimitCustomItem);
        } else {
          UpdateCheckedState("powerLimitModeGroup", ModeItemText(powerLimitMode));
        }
      } else {
        ClearGroupChecks("powerLimitModeGroup");
      }

      if (powerLimitTipItem != null)
        powerLimitTipItem.Text = powerOnline ? Strings.PowerLimitTip : Strings.PowerLimitOnBattery;

      if (powerLimitTrackBar != null) {
        int clamped = Math.Max(powerLimitTrackBar.Minimum,
                      Math.Min(powerLimitTrackBar.Maximum, powerLimitCustomW));
        if (powerLimitTrackBar.Value != clamped) {
          _suppressPowerLimitEvents = true;
          try { powerLimitTrackBar.Value = clamped; }
          finally { _suppressPowerLimitEvents = false; }
        }
      }
      if (powerLimitValueLabel != null)
        powerLimitValueLabel.Text = string.Format(Strings.CurrentSliderValueTemp, $"{powerLimitCustomW} W");
    }

    /// <summary>模式项在菜单中显示的完整文本。</summary>
    static string ModeItemText(PowerLimitMode mode) {
      switch (mode) {
        case PowerLimitMode.High: return PowerLimitModeLabel(PowerLimitMode.High);
        case PowerLimitMode.Balanced: return PowerLimitModeLabel(PowerLimitMode.Balanced);
        case PowerLimitMode.Quiet: return PowerLimitModeLabel(PowerLimitMode.Quiet);
        default: return Strings.NotSet;
      }
    }

    /// <summary>把各模式项的文本刷新为最新功耗标注。</summary>
    static void RefreshModeItemTexts() {
      if (powerLimitModeMenu == null) return;
      SetItemTextIfMatches(PowerLimitMode.High, x => x.StartsWith(Strings.PowerLimitHigh, StringComparison.Ordinal));
      SetItemTextIfMatches(PowerLimitMode.Balanced, x => x.StartsWith(Strings.PowerLimitBalanced, StringComparison.Ordinal));
      SetItemTextIfMatches(PowerLimitMode.Quiet, x => x.StartsWith(Strings.PowerLimitQuiet, StringComparison.Ordinal));
      // 自定义项文本随滑块瓦数变化，直接按引用更新
      if (powerLimitCustomItem != null)
        powerLimitCustomItem.Text = PowerLimitModeLabel(PowerLimitMode.Custom);
    }

    static void SetItemTextIfMatches(PowerLimitMode mode, Func<string, bool> predicate) {
      string target = ModeItemText(mode);
      foreach (ToolStripItem it in powerLimitModeMenu.DropDownItems) {
        var mi = it as ToolStripMenuItem;
        if (mi == null) continue;
        if (string.Equals(mi.Tag as string, "powerLimitModeGroup") && predicate(mi.Text)) {
          mi.Text = target;
          return;
        }
      }
    }

    static void ClearGroupChecks(string group) {
      if (trayIcon == null || trayIcon.ContextMenuStrip == null) return;

      void Walk(ToolStripItemCollection items) {
        foreach (ToolStripMenuItem item in items.OfType<ToolStripMenuItem>()) {
          if (string.Equals(item.Tag as string, group)) item.Checked = false;
          if (item.HasDropDownItems) Walk(item.DropDownItems);
        }
      }
      Walk(trayIcon.ContextMenuStrip.Items);
    }
  }
}
