using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OmenSuperHub {
  internal sealed class PlatformSettings {
    [JsonIgnore]
    public string RawJson { get; private set; }

    [JsonIgnore]
    public string SystemId { get; internal set; }

    [JsonIgnore]
    public string PlatformFamily { get; internal set; }

    [JsonIgnore]
    public string SkuKey { get; internal set; }

    [JsonIgnore]
    public string ResourceName { get; internal set; }

    public int Version { get; set; }
    public decimal WorkingRatioPL1 { get; set; }
    public decimal Decrease { get; set; }
    public decimal Increase { get; set; }
    public int PL1DeltaValue { get; set; }
    public int RecorderLong { get; set; }
    public int RecorderShort { get; set; }
    public int NbPL1LowerBoundMaxQ { get; set; }
    public List<int> NbPL1LowerBound { get; set; }
    public int NbPL1LowerBoundL0 { get; set; }
    public int NbPL1LowerBoundL1 { get; set; }
    public int NbPL1LowerBoundMaxP { get; set; }
    public int IntervalHeartbeat { get; set; }
    public int IntervalAlgoLong { get; set; }
    public int IntervalAlgoShort { get; set; }
    public int PL1DefaultValueI5 { get; set; }
    public int PL1DefaultValueI7 { get; set; }
    public int PL1DefaultValueI9 { get; set; }
    public int DtPL1UpperBound { get; set; }
    public int NbPL1UpperBound { get; set; }
    public int NbPL1UpperBoundDefault { get; set; }
    public int NbPL1UpperBoundPerformance { get; set; }
    public int NbPL1UpperBoundPerformanceWithOC { get; set; }
    public int NbPL1UpperBoundGaming { get; set; }
    public int PchOverheatThreshold { get; set; }
    public int PchOverheatThreshold2 { get; set; }
    public bool VrOverheatSupport { get; set; }
    public int VrOverheatThreshold { get; set; }
    public int VrOverheatThreshold2 { get; set; }
    public bool TppSupport { get; set; }
    public int TppSupportVersion { get; set; }
    public bool TppDefualtMode { get; set; }
    public int TppMinValue { get; set; }
    public int TppMaxValue { get; set; }
    public bool TppPchSupport { get; set; }
    public int TppPchSupportVersion { get; set; }
    public int IrSensorThreshold { get; set; }
    public int IrSensorThresholdDefault { get; set; }
    public List<int> IrSensorThresholdN18E { get; set; }
    public List<int> IrSensorThresholdN18P { get; set; }
    public int temperatureThrottlingBalance { get; set; }
    public int temperatureThrottlingPerformance { get; set; }
    public int PL4_Default { get; set; }
    public int PL4_Threshold { get; set; }
    public GpuConsts GpuSettings { get; set; }
    public bool BiosAutoFanControlInDc { get; set; }
    public int BiosAutoFanControlInDcVersion { get; set; }
    public SwFanControlCustom SwFanControlCustomDefault { get; set; }
    public SwFanControlCustom SwFanControlCustomPerformance { get; set; }
    public SwFanControlCustom SwFanControlCustomUnleashed { get; set; }
    public SwFanControlCustom SwFanControlCustomFanCurve { get; set; }
    public FanTable FanTable { get; set; }
    public double Lamda_Increase { get; set; }
    public double Lamda_Decrease { get; set; }
    public List<int> IrOverheatThreshold { get; set; }
    public List<int> IrGpsThreshold { get; set; }
    public List<int> IrPl1Threshold { get; set; }
    public List<int> IrReleaseThreshold { get; set; }
    public int IrCycle { get; set; }
    public int IrOverheatPl1Reduce { get; set; }
    public int IrPl1Reduce { get; set; }
    public bool PpabOnInPerformanceMode { get; set; }
    public bool PpabOffWhenIrOverheat { get; set; }
    public bool AlwaysSupportPerformanceMode { get; set; }
    public int LightingBoardVid { get; set; }
    public int LightingBoardPid { get; set; }
    public int DtManualFanSliderLowerBound { get; set; }
    public bool HybridBatteryWorkaround { get; set; }
    public int HybridBatteryWorkaroundLife { get; set; }
    public bool Win11PowerModeSyncSupport { get; set; }
    public string DefaultPowerMode { get; set; }
    public bool DisableBatteryDrain { get; set; }
    public int AdapterPeakPower { get; set; }
    public double GpuPowerRatio { get; set; }
    public double GpuPeakPowerRatio { get; set; }
    public bool UnleashedModeSupport { get; set; }
    public int UnleashedModeMinPL1 { get; set; }
    public int UnleashedModeMaxPL1 { get; set; }
    public int UnleashedModeDefaultPL1 { get; set; }
    public int UnleashedModeMinPL2 { get; set; }
    public int UnleashedModeMaxPL2 { get; set; }
    public int UnleashedModeDefaultPL2 { get; set; }
    public int UnleashedModeMinPL4 { get; set; }
    public int UnleashedModeMaxPL4 { get; set; }
    public int UnleashedModeDefaultPL4 { get; set; }
    public int UnleashedModeMinIccMax { get; set; }
    public int UnleashedModeMaxIccMax { get; set; }
    public int UnleashedModeDefaultIccMax { get; set; }
    public int UnleashedModeMinSurfaceTemp { get; set; }
    public int UnleashedModeMaxSurfaceTemp { get; set; }
    public int UnleashedModeDefaultSurfaceTemp { get; set; }
    public bool? UnleashedModeDefaultCtgp { get; set; }
    public bool UnleashedModeTppDefaultMode { get; set; }
    public int UnleashedModeTppDefaultValue { get; set; }
    public int UnleashedModeDefaultBatteryDrain { get; set; }
    public int CleanCreekCpuFanSpeed { get; set; }
    public int CleanCreekGpuFanSpeed { get; set; }
    public int CleanCreekFan3Speed { get; set; }
    public int CleanCreekDuration { get; set; }
    public int CleanCreekCpuTemp { get; set; }
    public int CleanCreekGpuTemp { get; set; }
    public int CleanCreekIrTemp { get; set; }
    public int CleanCreekIdleTime { get; set; }
    public bool StableModeSupport { get; set; }
    public int StableModeDelay { get; set; }
    public List<int> DtDynamicPl1HighLimit { get; set; }
    public List<int> DtDynamicPl1LowLimit { get; set; }
    public List<int> DtDynamicPl1Setting { get; set; }
    public List<int> DtDynamicSleepTime { get; set; }
    public int CpuOverheatThreshold { get; set; }
    public int GpuOverheatThreshold { get; set; }
    public int AmbientOverheatThreshold { get; set; }
    public int CustomFanCurveVersion { get; set; }
    public DtSwFanControlCustom DtSwFanControlCustomFanCurveAcsCPU { get; set; }
    public DtSwFanControlCustom DtSwFanControlCustomFanCurveLcsCPU { get; set; }
    public DtSwFanControlCustom DtSwFanControlCustomFanCurveIntake { get; set; }
    public DtSwFanControlCustom DtSwFanControlCustomFanCurveExhaust { get; set; }
    public DtSwFanControlCustom DtSwFanControlCustomFanCurveVrm { get; set; }
    public int GamingGpuUsage { get; set; }
    public float BatteryLifeThreshold { get; set; }
    public int UnleashedModeIrReleaseThresholdOffset { get; set; }
    public int CpuTemperatureThreshold { get; set; }
    public float BatteryDrainBuffer { get; set; }
    public int BatteryLowerBound { get; set; }
    public int CpuTemperatureThresholdLower { get; set; }
    public byte GpsMaxTemperature { get; set; }
    public byte GpsMinTemperature { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }

    public bool SupportsTpp => TppSupport || TppPchSupport;
    public bool SupportsCustomFanCurve => SwFanControlCustomFanCurve != null || DtSwFanControlCustomFanCurveAcsCPU != null || DtSwFanControlCustomFanCurveLcsCPU != null;
    public bool SupportsUnleashedMode => UnleashedModeSupport;
    public bool SupportsBiosAutoFanControlInDc => BiosAutoFanControlInDc;
    public bool SupportsPpab => PpabOnInPerformanceMode || PpabOffWhenIrOverheat;

    public static PlatformSettings FromJson(string json) {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
      });

      using (var sr = new StringReader(json))
      using (var reader = new JsonTextReader(sr) {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Double
      }) {
        var settings = serializer.Deserialize<PlatformSettings>(reader);
        if (settings != null)
          settings.RawJson = json;
        return settings;
      }
    }

    public string GetCapabilitySummary() {
      return string.Join(", ", new[]
      {
                "Tpp=" + (SupportsTpp ? "Y" : "N"),
                "Ppab=" + (SupportsPpab ? "Y" : "N"),
                "Unleashed=" + (SupportsUnleashedMode ? "Y" : "N"),
                "CustomFanCurve=" + (SupportsCustomFanCurve ? "Y" : "N"),
                "BiosAutoFanControlInDc=" + (SupportsBiosAutoFanControlInDc ? "Y" : "N")
            });
    }
  }

  internal static class PlatformSettingsResolver {
    private const string DefaultBaseDirectory = @"C:\Users\fiveb\Desktop";
    private const string ModelDeviceDllName = "HP.Omen.Core.Model.Device.dll";
    private const string CommonDllName = "HP.Omen.Core.Common.dll";
    private const string PerformancePlatformListSuffix = "HP.Omen.Core.Model.Device.JSON.PerformancePlatformList.json";

    private static readonly IReadOnlyDictionary<string, string> SsidToPlatformFamily = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      ["8572"] = "DRX",
      ["8573"] = "DRX",
      ["8574"] = "Gamora",
      ["8575"] = "Gamora",
      ["8600"] = "Milos",
      ["8601"] = "Milos",
      ["8602"] = "Milos",
      ["8603"] = "Santorini",
      ["8604"] = "Santorini",
      ["8605"] = "Milos",
      ["8606"] = "Milos",
      ["8607"] = "Milos",
      ["860A"] = "Gamora",
      ["8746"] = "Milos",
      ["8747"] = "Milos",
      ["8748"] = "Santorini",
      ["8749"] = "Milos",
      ["874A"] = "Milos",
      ["8786"] = "Valkyrie",
      ["8787"] = "Valkyrie",
      ["8788"] = "Valkyrie",
      ["878A"] = "Starmade",
      ["878B"] = "Starmade",
      ["878C"] = "Starmade",
      ["87B5"] = "Gamora",
      ["88C8"] = "Starmade",
      ["88CB"] = "Starmade",
      ["88D1"] = "Valkyrie",
      ["88D2"] = "Valkyrie",
      ["88EB"] = "Taffyta",
      ["88EC"] = "Taffyta",
      ["88ED"] = "Taffyta",
      ["88EE"] = "Taffyta",
      ["88F4"] = "Ralph",
      ["88F5"] = "Ralph",
      ["88F6"] = "Ralph",
      ["88F7"] = "Cybug",
      ["88F8"] = "Calhoun",
      ["88F9"] = "Calhoun",
      ["88FA"] = "Calhoun",
      ["88FD"] = "Ralph",
      ["88FE"] = "Cybug",
      ["88FF"] = "Vanellope",
      ["8900"] = "Vanellope",
      ["8901"] = "Vanellope",
      ["8902"] = "Vanellope",
      ["8912"] = "Vanellope",
      ["8934"] = "Taffyta",
      ["89B5"] = "Noctali",
      ["89D8"] = "NoctaliA",
      ["8A13"] = "Ralph",
      ["8A14"] = "Ralph",
      ["8A15"] = "Ralph",
      ["8A16"] = "Ralph",
      ["8A17"] = "Cybug",
      ["8A18"] = "Cybug",
      ["8A19"] = "Cybug",
      ["8A1A"] = "Cybug",
      ["8A21"] = "Taffyta",
      ["8A22"] = "Taffyta",
      ["8A25"] = "Calhoun",
      ["8A26"] = "Calhoun",
      ["8A27"] = "Calhoun",
      ["8A3C"] = "Opihr",
      ["8A3D"] = "Opihr",
      ["8A3E"] = "Opihr",
      ["8A3F"] = "Opihr",
      ["8A40"] = "Nolets",
      ["8A41"] = "Nolets",
      ["8A42"] = "Nolets",
      ["8A43"] = "Nolets",
      ["8A44"] = "Nolets",
      ["8A4C"] = "Hendricks",
      ["8A4D"] = "Hendricks",
      ["8A4E"] = "Hendricks",
      ["8A4F"] = "Roku",
      ["8A50"] = "Roku",
      ["8BA8"] = "Brunobear",
      ["8BA9"] = "Brunobear",
      ["8BAA"] = "Brunobear",
      ["8BAB"] = "Brunobear",
      ["8BAC"] = "Brunobear",
      ["8BAD"] = "Cybug",
      ["8BB0"] = "Cybug",
      ["8BB1"] = "Roku",
      ["8BB2"] = "Roku",
      ["8BB3"] = "Quaker",
      ["8BB4"] = "Quaker",
      ["8BBD"] = "Roaree",
      ["8BBE"] = "Roaree",
      ["8BC1"] = "Roaree",
      ["8BC2"] = "Roaree",
      ["8BC8"] = "Roku",
      ["8BCA"] = "Thetiger",
      ["8BCB"] = "Thetiger",
      ["8BCD"] = "Thetiger",
      ["8BCF"] = "Thetiger",
      ["8BD4"] = "Bigred",
      ["8BD5"] = "Bigred",
      ["8C2D"] = "Roku",
      ["8C2E"] = "Roku",
      ["8C2F"] = "Opihr",
      ["8C30"] = "Opihr",
      ["8C3F"] = "Roku",
      ["8C40"] = "Roku",
      ["8C4D"] = "Quaker",
      ["8C4E"] = "Quaker",
      ["8C58"] = "Voco",
      ["8C73"] = "Avid",
      ["8C74"] = "Avid",
      ["8C75"] = "Avid",
      ["8C76"] = "Brunobear",
      ["8C77"] = "Brunobear",
      ["8C78"] = "Brunobear",
      ["8C99"] = "Roaree",
      ["8C9A"] = "Roaree",
      ["8C9B"] = "Thetiger",
      ["8C9C"] = "Bigred",
      ["8C9D"] = "Bigred",
      ["8CC0"] = "Ritchie",
      ["8CC1"] = "Ritchie",
      ["8CF3"] = "Snowball",
      ["8D07"] = "Opihr",
      ["8D23"] = "Khalilah",
      ["8D24"] = "Khalilah",
      ["8D25"] = "Khalilah",
      ["8D26"] = "Khalilah",
      ["8D2C"] = "Articuno",
      ["8D2D"] = "Hanna",
      ["8D2E"] = "Hanna",
      ["8D2F"] = "Hanna",
      ["8D31"] = "SnowflakeA",
      ["8D34"] = "Snowflake",
      ["8D3F"] = "Hanna",
      ["8D40"] = "Hanna",
      ["8D41"] = "Dojo",
      ["8D42"] = "Dojo",
      ["8D7F"] = "Snowflake",
      ["8D87"] = "Vibrance",
      ["8D88"] = "Vibrance",
      ["8DCD"] = "Roku",
      ["8DCE"] = "Roku",
      ["8DCF"] = "Roku",
      ["8DD0"] = "Opihr",
      ["8DD1"] = "Opihr",
      ["8DD2"] = "Opihr",
      ["8DD5"] = "Vibrance",
      ["8DD6"] = "Vibrance",
      ["8E0F"] = "Avid",
      ["8E10"] = "Avid",
      ["8E35"] = "Khalilah",
      ["8E41"] = "Voco",
      ["8E55"] = "SnowballA",
      ["8E59"] = "Hanna",
      ["8E5C"] = "Roku",
      ["8E5D"] = "Roku",
      ["8E5E"] = "Roku",
      ["8E5F"] = "Opihr",
      ["8E71"] = "Hanna",
      ["8E72"] = "Hanna",
      ["8E8C"] = "Opihr",
      ["8E9A"] = "Dojo",
      ["8E9F"] = "Vibrance",
      ["8EDC"] = "Pocari",
      ["8EDD"] = "Pocari",
      ["8EDE"] = "Pocari",
      ["8EDF"] = "Pocari",
      ["8EEC"] = "Propel",
      ["8EED"] = "Propel",
      ["8EEE"] = "Propel",
      ["8EEF"] = "Propel",
      ["8F05"] = "Khalilah",
      ["8F06"] = "Khalilah",
      ["8F1C"] = "Hanna",
      ["8F1D"] = "Hanna",
      ["8F1E"] = "Hanna",
      ["8F1F"] = "Hanna",
      ["8F20"] = "Hanna",
      ["8F21"] = "Khalilah",
      ["8F28"] = "Roku",
      ["8F5C"] = "Pocari",
    };

    public static PlatformSettings LoadFromCurrentSystem(string baseDirectory = DefaultBaseDirectory) {
      if (string.IsNullOrWhiteSpace(baseDirectory))
        baseDirectory = DefaultBaseDirectory;

      return LoadFromSystemId(OmenHardware.GetSystemID(), baseDirectory);
    }

    public static PlatformSettings LoadFromSystemId(string systemId, string baseDirectory = DefaultBaseDirectory, string gpuName = null, int? cpuCores = null, int? adapterWatts = null) {
      if (string.IsNullOrWhiteSpace(systemId))
        return null;

      if (string.IsNullOrWhiteSpace(baseDirectory))
        baseDirectory = DefaultBaseDirectory;

      string modelDeviceDllPath = Path.Combine(baseDirectory, ModelDeviceDllName);
      string commonDllPath = Path.Combine(baseDirectory, CommonDllName);

      if (!File.Exists(modelDeviceDllPath))
        throw new FileNotFoundException("未找到设备模型 DLL", modelDeviceDllPath);

      if (!File.Exists(commonDllPath))
        throw new FileNotFoundException("未找到 Common DLL", commonDllPath);

      var performancePlatforms = LoadPerformancePlatformList(modelDeviceDllPath);
      var entry = performancePlatforms.FirstOrDefault(x => string.Equals(x.SSID, systemId.Trim(), StringComparison.OrdinalIgnoreCase));
      if (entry == null)
        return null;

      string family = ResolveFamily(systemId.Trim(), entry);
      string sku = ResolveSku(entry, gpuName, cpuCores, adapterWatts);
      if (string.IsNullOrWhiteSpace(sku))
        return null;

      var commonAssembly = Assembly.LoadFrom(commonDllPath);
      var resourceName = ResolveResourceName(commonAssembly, family, sku);
      if (string.IsNullOrWhiteSpace(resourceName))
        return null;

      var json = ReadEmbeddedResource(commonAssembly, resourceName);
      if (string.IsNullOrWhiteSpace(json))
        return null;

      return AttachMeta(PlatformSettings.FromJson(json), systemId.Trim(), family, sku, resourceName);
    }

    public static bool TryLoadFromSystemId(string systemId, out PlatformSettings settings, string baseDirectory = DefaultBaseDirectory, string gpuName = null, int? cpuCores = null, int? adapterWatts = null) {
      try {
        settings = LoadFromSystemId(systemId, baseDirectory, gpuName, cpuCores, adapterWatts);
        return settings != null;
      } catch {
        settings = null;
        return false;
      }
    }

    public static string ReadPerformancePlatformListJson(string baseDirectory = DefaultBaseDirectory) {
      if (string.IsNullOrWhiteSpace(baseDirectory))
        baseDirectory = DefaultBaseDirectory;

      string modelDeviceDllPath = Path.Combine(baseDirectory, ModelDeviceDllName);
      if (!File.Exists(modelDeviceDllPath))
        return null;

      var assembly = Assembly.LoadFrom(modelDeviceDllPath);
      var resourceName = FindResourceName(assembly, PerformancePlatformListSuffix);
      if (resourceName == null)
        return null;

      return ReadEmbeddedResource(assembly, resourceName);
    }

    private static PlatformSettings AttachMeta(PlatformSettings settings, string systemId, string family, string sku, string resourceName) {
      if (settings != null) {
        settings.SystemId = systemId;
        settings.PlatformFamily = family;
        settings.SkuKey = sku;
        settings.ResourceName = resourceName;
      }
      return settings;
    }

    private static List<PerformancePlatformEntry> LoadPerformancePlatformList(string modelDeviceDllPath) {
      var assembly = Assembly.LoadFrom(modelDeviceDllPath);
      var resourceName = FindResourceName(assembly, PerformancePlatformListSuffix);
      if (resourceName == null)
        return new List<PerformancePlatformEntry>();

      var json = ReadEmbeddedResource(assembly, resourceName);
      if (string.IsNullOrWhiteSpace(json))
        return new List<PerformancePlatformEntry>();

      var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
      });

      using (var sr = new StringReader(json))
      using (var reader = new JsonTextReader(sr) {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Double
      }) {
        return serializer.Deserialize<List<PerformancePlatformEntry>>(reader) ?? new List<PerformancePlatformEntry>();
      }
    }

    private static string ResolveFamily(string systemId, PerformancePlatformEntry entry) {
      if (SsidToPlatformFamily.TryGetValue(systemId, out var family) && !string.IsNullOrWhiteSpace(family))
        return family;

      if (!string.IsNullOrWhiteSpace(entry.Sku))
        return GuessFamilyFromSku(entry.Sku);

      if (!string.IsNullOrWhiteSpace(entry.DefaultSkuCpuPart))
        return GuessFamilyFromSku(entry.DefaultSkuCpuPart);

      return null;
    }

    private static string ResolveSku(PerformancePlatformEntry entry, string gpuName, int? cpuCores, int? adapterWatts) {
      if (!string.IsNullOrWhiteSpace(entry.Sku))
        return entry.Sku.Trim();

      var cpuPart = ResolvePart(entry.DefaultSkuCpuPart, entry.GetSkuCpuPart, r => r.CpuCores.HasValue && cpuCores.HasValue && r.CpuCores.Value == cpuCores.Value);
      var gpuPart = ResolvePart(entry.DefaultSkuGpuPart, entry.GetSkuGpuPart, r => !string.IsNullOrWhiteSpace(r.Gpu) && !string.IsNullOrWhiteSpace(gpuName) && gpuName.IndexOf(r.Gpu, StringComparison.OrdinalIgnoreCase) >= 0);
      var adapterPart = ResolvePart(entry.DefaultSkuAdapterPart, entry.GetSkuAdapterPart, r => r.Adapter.HasValue && adapterWatts.HasValue && r.Adapter.Value == adapterWatts.Value);

      var parts = new List<string>();
      if (!string.IsNullOrWhiteSpace(cpuPart)) parts.Add(cpuPart.Trim());
      if (!string.IsNullOrWhiteSpace(gpuPart)) parts.Add(gpuPart.Trim());
      if (!string.IsNullOrWhiteSpace(adapterPart)) parts.Add(adapterPart.Trim());

      return parts.Count == 0 ? null : string.Join("_", parts);
    }

    private static string ResolvePart(string defaultPart, List<SkuPartRule> rules, Func<SkuPartRule, bool> predicate) {
      if (rules != null) {
        var matched = rules.FirstOrDefault(predicate);
        if (matched != null && !string.IsNullOrWhiteSpace(matched.Sku))
          return matched.Sku.Trim();
      }

      return string.IsNullOrWhiteSpace(defaultPart) ? null : defaultPart.Trim();
    }

    private static string ResolveResourceName(Assembly assembly, string family, string sku) {
      var resourceNames = assembly.GetManifestResourceNames();

      if (!string.IsNullOrWhiteSpace(family)) {
        string exactSuffix = $"PowerControl.JSON.{family}_{sku}.json";
        var exact = resourceNames.FirstOrDefault(x => x.EndsWith(exactSuffix, StringComparison.OrdinalIgnoreCase) || x.IndexOf(exactSuffix, StringComparison.OrdinalIgnoreCase) >= 0);
        if (exact != null)
          return exact;

        var familyMatches = resourceNames
            .Where(x => x.IndexOf($".{family}_", StringComparison.OrdinalIgnoreCase) >= 0 && x.EndsWith($"_{sku}.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (familyMatches.Count > 0)
          return familyMatches[0];
      }

      var suffixMatches = resourceNames.Where(x => x.EndsWith($"_{sku}.json", StringComparison.OrdinalIgnoreCase)).ToList();
      if (suffixMatches.Count > 0)
        return suffixMatches[0];

      return null;
    }

    private static string FindResourceName(Assembly assembly, string suffix) {
      return assembly.GetManifestResourceNames()
          .FirstOrDefault(x => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || x.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName) {
      using (var stream = assembly.GetManifestResourceStream(resourceName)) {
        if (stream == null)
          return null;

        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
          return reader.ReadToEnd();
      }
    }

    private static string GuessFamilyFromSku(string sku) {
      if (string.IsNullOrWhiteSpace(sku))
        return null;

      var idx = sku.IndexOf('_');
      return idx > 0 ? sku.Substring(0, idx) : null;
    }

    private sealed class PerformancePlatformEntry {
      public string SSID { get; set; }
      public bool AlwaysSupport { get; set; }
      public bool IsPowerAwareSupported { get; set; }
      public bool IsMaxFanSupported { get; set; }
      public string Sku { get; set; }
      public string DefaultSkuCpuPart { get; set; }
      public string DefaultSkuGpuPart { get; set; }
      public string DefaultSkuAdapterPart { get; set; }
      public List<SkuPartRule> GetSkuCpuPart { get; set; }
      public List<SkuPartRule> GetSkuGpuPart { get; set; }
      public List<SkuPartRule> GetSkuAdapterPart { get; set; }
      public LegacyV2Sku GetLegacyV2Sku { get; set; }
    }

    private sealed class SkuPartRule {
      public int? CpuCores { get; set; }
      public string Gpu { get; set; }
      public int? Adapter { get; set; }
      public string Sku { get; set; }
    }

    private sealed class LegacyV2Sku {
      public List<string> Modes { get; set; }
      public int? SPG { get; set; }
    }
  }

  internal sealed class GpuConsts {
    public double UsageThreshold { get; set; }
    public int DataCount { get; set; }
    public int UsageCount { get; set; }
    public double UsageGaming { get; set; }
    public double UsageVideo { get; set; }
  }

  internal sealed class SwFanControlCustom {
    public FanTable FanTable { get; set; }
    public double Lamda_Increase { get; set; }
    public double Lamda_Decrease { get; set; }
  }

  internal sealed class FanTable {
    public List<int> Fan_Table_CPU_Temperature_List { get; set; }
    public List<int> Fan_Table_CPU_Fan_Speed_List { get; set; }
    public List<int> Fan_Table_GPU_Temperature_List { get; set; }
    public List<int> Fan_Table_GPU_Fan_Speed_List { get; set; }
    public List<int> Fan_Table_IR_Temperature_List { get; set; }
    public List<int> Fan_Table_IR_Fan_Speed_List { get; set; }
    public Boundary Boundary { get; set; }
  }

  internal sealed class Boundary {
    public List<int> CPU_Fan_Speed_Upper_Bound_List { get; set; }
    public List<int> CPU_Fan_Speed_Lower_Bound_List { get; set; }
    public List<int> GPU_Fan_Speed_Upper_Bound_List { get; set; }
    public List<int> GPU_Fan_Speed_Lower_Bound_List { get; set; }
    public List<int> IR_Fan_Speed_Upper_Bound_List { get; set; }
    public List<int> IR_Fan_Speed_Lower_Bound_List { get; set; }
  }

  internal sealed class DtSwFanControlCustom {
    public List<int> DtDynamicPl1HighLimit { get; set; }
    public List<int> DtDynamicPl1LowLimit { get; set; }
    public List<int> DtDynamicPl1Setting { get; set; }
    public List<int> DtDynamicSleepTime { get; set; }
  }
}
