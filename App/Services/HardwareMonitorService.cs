using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace OmenSuperHub {
  public sealed class HardwareMonitorService : IDisposable {
    private readonly string _executablePath;
    private Process _process;
    private StreamWriter _input;
    private bool _stopping;
    private bool _cpuEnabled;
    private bool _gpuEnabled;
    private int _intervalMs = 1000;

    public event EventHandler<HardwareMonitorSnapshotEventArgs> SnapshotReceived;
    public event EventHandler<HardwareMonitorErrorEventArgs> ErrorReceived;

    public HardwareMonitorService(string executablePath) {
      _executablePath = executablePath;
    }

    public bool IsRunning {
      get {
        try {
          return _process != null && !_process.HasExited;
        } catch {
          return false;
        }
      }
    }

    public void Start(bool cpuEnabled, bool gpuEnabled, int intervalMs) {
      _cpuEnabled = cpuEnabled;
      _gpuEnabled = gpuEnabled;
      _intervalMs = intervalMs > 0 ? intervalMs : 1000;

      if (IsRunning)
        return;

      _process = new Process {
        StartInfo = new ProcessStartInfo {
          FileName = _executablePath,
          Arguments = "--hwmonitor",
          UseShellExecute = false,
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden
        }
      };

      _process.OutputDataReceived += OnOutputDataReceived;
      _process.ErrorDataReceived += OnErrorDataReceived;
      _process.EnableRaisingEvents = true;
      _process.Exited += OnExited;

      try {
        _process.Start();
        _input = _process.StandardInput;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        SetGpuMonitorState(_gpuEnabled);
        SetCpuMonitorState(_cpuEnabled);
        SetMonitorInterval(_intervalMs);
      } catch (Exception ex) {
        _input = null;
        _process = null;
        OnError("Start failed: " + ex.Message);
      }
    }

    public void SetGpuMonitorState(bool enable) {
      _gpuEnabled = enable;
      WriteLine(enable ? "GPU:ON" : "GPU:OFF");
    }

    public void SetCpuMonitorState(bool enable) {
      _cpuEnabled = enable;
      WriteLine(enable ? "CPU:ON" : "CPU:OFF");
    }

    public void SetMonitorInterval(int ms) {
      _intervalMs = ms > 0 ? ms : 1000;
      WriteLine("INTERVAL:" + _intervalMs);
    }

    public void Stop() {
      if (!IsRunning)
        return;

      _stopping = true;
      try {
        _process.Kill();
      } catch {
        _stopping = false;
      }
    }

    public void Dispose() {
      Stop();
      if (_process != null)
        _process.Dispose();
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e) {
      HardwareMonitorSnapshot snapshot;
      if (!HardwareMonitorSnapshot.TryParse(e.Data, out snapshot))
        return;

      EventHandler<HardwareMonitorSnapshotEventArgs> handler = SnapshotReceived;
      if (handler != null)
        handler(this, new HardwareMonitorSnapshotEventArgs(snapshot));
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e) {
      if (string.IsNullOrEmpty(e.Data))
        return;
      OnError(e.Data);
    }

    private void OnExited(object sender, EventArgs e) {
      if (_stopping) {
        _stopping = false;
        return;
      }

      Task.Delay(3000).ContinueWith(_ => {
        try {
          Start(_cpuEnabled, _gpuEnabled, _intervalMs);
        } catch {
        }
      });
    }

    private void WriteLine(string line) {
      if (!IsRunning || _input == null)
        return;

      try {
        _input.WriteLine(line);
      } catch {
      }
    }

    private void OnError(string message) {
      EventHandler<HardwareMonitorErrorEventArgs> handler = ErrorReceived;
      if (handler != null)
        handler(this, new HardwareMonitorErrorEventArgs(message));
    }
  }

  public sealed class HardwareMonitorSnapshotEventArgs : EventArgs {
    public HardwareMonitorSnapshot Snapshot { get; private set; }

    public HardwareMonitorSnapshotEventArgs(HardwareMonitorSnapshot snapshot) {
      Snapshot = snapshot;
    }
  }

  public sealed class HardwareMonitorErrorEventArgs : EventArgs {
    public string Message { get; private set; }

    public HardwareMonitorErrorEventArgs(string message) {
      Message = message;
    }
  }
}
