using System;
using System.IO;

namespace OmenSuperHub {
  public static class Logger {
    public static readonly string logFileName = "OmenSuperHub.log";
    private static readonly string LogPath
        = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFileName);
    private static readonly object FileLock = new object();

    // 节流：相同消息 30 秒内不重复写入文件
    private static string lastMessage = "";
    private static DateTime lastWriteTime = DateTime.MinValue;
    private const int ThrottleSeconds = 30;

    /// <summary>
    /// 写入普通日志（同时输出控制台 + 文件）
    /// </summary>
    public static void Info(string message) {
      Console.WriteLine(message);
      WriteToFile(message);
    }

    /// <summary>
    /// 写入错误日志（同时输出控制台 + 文件，带 `[ERROR]` 前缀）
    /// </summary>
    public static void Error(string message) {
      Console.WriteLine(message);
      WriteToFile($"[ERROR] {message}");
    }

    private static void WriteToFile(string line) {
      // 节流：完全相同的行在时间内跳过
      if (line == lastMessage &&
          (DateTime.Now - lastWriteTime).TotalSeconds < ThrottleSeconds)
        return;

      lastMessage = line;
      lastWriteTime = DateTime.Now;

      lock (FileLock) {
        File.AppendAllText(LogPath,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
      }
    }
  }
}
