using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmenSuperHub {
  internal sealed class FanCurvePoint {
    public FanCurvePoint(int temperature, int fanSpeed) {
      Temperature = temperature;
      FanSpeed = fanSpeed;
    }

    public int Temperature { get; set; }
    public int FanSpeed { get; set; }
  }

  internal sealed class FanCurveProfile {
    public FanCurveProfile(IEnumerable<FanCurvePoint> cpuPoints, IEnumerable<FanCurvePoint> gpuPoints) {
      CpuPoints = cpuPoints.OrderBy(point => point.Temperature).ToList();
      GpuPoints = gpuPoints.OrderBy(point => point.Temperature).ToList();
    }

    public List<FanCurvePoint> CpuPoints { get; }
    public List<FanCurvePoint> GpuPoints { get; }

    public static FanCurveProfile Load(string filePath) {
      string[] lines = File.ReadAllLines(filePath);
      if (lines.Length == 0)
        throw new InvalidDataException(Strings.FanCurveInvalidFile);

      if (lines.Any(line => line.Contains("=")))
        return LoadKeyValueFormat(lines);

      return LoadLegacyCsvFormat(lines);
    }

    public void Save(string filePath) {
      ValidatePoints(CpuPoints);
      ValidatePoints(GpuPoints);

      string[] lines = {
        "Fan_Table_CPU_Temperature_List=" + string.Join(",", CpuPoints.Select(point => point.Temperature)),
        "Fan_Table_CPU_Fan_Speed_List=" + string.Join(",", CpuPoints.Select(point => point.FanSpeed)),
        "Fan_Table_GPU_Temperature_List=" + string.Join(",", GpuPoints.Select(point => point.Temperature)),
        "Fan_Table_GPU_Fan_Speed_List=" + string.Join(",", GpuPoints.Select(point => point.FanSpeed))
      };
      File.WriteAllLines(filePath, lines);
    }

    private static FanCurveProfile LoadKeyValueFormat(IEnumerable<string> lines) {
      var valuesByKey = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
      foreach (string rawLine in lines) {
        if (string.IsNullOrWhiteSpace(rawLine)) continue;
        int separatorIndex = rawLine.IndexOf('=');
        if (separatorIndex < 0) continue;

        string key = rawLine.Substring(0, separatorIndex).Trim();
        string value = rawLine.Substring(separatorIndex + 1).Trim();
        valuesByKey[key] = ParseIntegerList(value);
      }

      List<int> cpuTemperatures;
      List<int> cpuSpeeds;
      List<int> gpuTemperatures;
      List<int> gpuSpeeds;
      if (!valuesByKey.TryGetValue("Fan_Table_CPU_Temperature_List", out cpuTemperatures) ||
          !valuesByKey.TryGetValue("Fan_Table_CPU_Fan_Speed_List", out cpuSpeeds) ||
          !valuesByKey.TryGetValue("Fan_Table_GPU_Temperature_List", out gpuTemperatures) ||
          !valuesByKey.TryGetValue("Fan_Table_GPU_Fan_Speed_List", out gpuSpeeds))
        throw new InvalidDataException(Strings.FanCurveInvalidFile);

      return Create(cpuTemperatures, cpuSpeeds, gpuTemperatures, gpuSpeeds);
    }

    private static FanCurveProfile LoadLegacyCsvFormat(string[] lines) {
      var cpuTemperatures = new List<int>();
      var cpuSpeeds = new List<int>();
      var gpuTemperatures = new List<int>();
      var gpuSpeeds = new List<int>();

      for (int index = 1; index < lines.Length; index++) {
        if (string.IsNullOrWhiteSpace(lines[index])) continue;
        string[] parts = lines[index].Split(',');
        if (parts.Length < 6) continue;

        cpuTemperatures.Add(ParseInteger(parts[0]));
        cpuSpeeds.Add(ParseInteger(parts[1]));
        gpuTemperatures.Add(ParseInteger(parts[3]));
        gpuSpeeds.Add(ParseInteger(parts[4]));
      }

      return Create(cpuTemperatures, cpuSpeeds, gpuTemperatures, gpuSpeeds);
    }

    private static FanCurveProfile Create(
        IList<int> cpuTemperatures,
        IList<int> cpuSpeeds,
        IList<int> gpuTemperatures,
        IList<int> gpuSpeeds) {
      if (cpuTemperatures.Count != cpuSpeeds.Count ||
          gpuTemperatures.Count != gpuSpeeds.Count ||
          cpuTemperatures.Count < 2 ||
          gpuTemperatures.Count < 2)
        throw new InvalidDataException(Strings.FanCurveInvalidFile);

      var cpuPoints = cpuTemperatures
          .Select((temperature, index) => new FanCurvePoint(temperature, cpuSpeeds[index]))
          .ToList();
      var gpuPoints = gpuTemperatures
          .Select((temperature, index) => new FanCurvePoint(temperature, gpuSpeeds[index]))
          .ToList();

      ValidatePoints(cpuPoints);
      ValidatePoints(gpuPoints);
      return new FanCurveProfile(cpuPoints, gpuPoints);
    }

    private static List<int> ParseIntegerList(string value) {
      var result = value
          .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(ParseInteger)
          .ToList();
      if (result.Count == 0)
        throw new InvalidDataException(Strings.FanCurveInvalidFile);
      return result;
    }

    private static int ParseInteger(string value) {
      int result;
      if (!int.TryParse(value.Trim(), out result))
        throw new InvalidDataException(Strings.FanCurveInvalidFile);
      return result;
    }

    private static void ValidatePoints(IList<FanCurvePoint> points) {
      if (points.Count < 2 ||
          points.Any(point => point.Temperature < 0 || point.FanSpeed < 0) ||
          points.GroupBy(point => point.Temperature).Any(group => group.Count() > 1))
        throw new InvalidDataException(Strings.FanCurveInvalidFile);
    }
  }
}
