using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  internal static class OmenKeyActions {
    public const string Default = "default";
    public const string Overlay = "custom"; // Registry compatibility: "custom" means toggle overlay.
    public const string App = "app";
    public const string Shortcut = "shortcut";
    public const string Preset = "preset";
    public const string None = "none";

    public static bool UsesPipe(string action) {
      return action == Overlay || action == App || action == Shortcut || action == Preset;
    }

    public static bool IsKnown(string action) {
      return action == Default || action == Overlay || action == App ||
          action == Shortcut || action == Preset || action == None;
    }
  }

  static partial class Program {
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const int VK_SHIFT = 0x10;
    const int VK_CONTROL = 0x11;
    const int VK_MENU = 0x12;
    const int VK_LWIN = 0x5B;
    const int VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
      public uint type;
      public InputUnion u;
    }

    // MOUSEINPUT is the largest union member; including it ensures Marshal.SizeOf(INPUT)
    // matches what Windows expects (28 bytes on 32-bit, 40 bytes on 64-bit).
    // Without it, SendInput returns 0 and GetLastWin32Error returns 87 (ERROR_INVALID_PARAMETER).
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT {
      public int dx;
      public int dy;
      public uint mouseData;
      public uint dwFlags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion {
      [FieldOffset(0)]
      public MOUSEINPUT mi;   // Largest member — sets the union's marshalled size
      [FieldOffset(0)]
      public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
      public ushort wVk;
      public ushort wScan;
      public uint dwFlags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    private static Form omenKeyDesktopAppForm;
    private static Form omenKeyUwpAppForm;
    private static Form omenKeyShortcutForm;

    private static void ActivateForm(Form form) {
      if (form == null || form.IsDisposed) return;
      if (form.WindowState == FormWindowState.Minimized) {
        form.WindowState = FormWindowState.Normal;
      }
      form.Show();
      form.BringToFront();
      form.Activate();
    }

    private static void CloseOtherOmenKeyForms(Form keepForm) {
      if (omenKeyDesktopAppForm != null && !ReferenceEquals(omenKeyDesktopAppForm, keepForm)) {
        try { if (!omenKeyDesktopAppForm.IsDisposed) omenKeyDesktopAppForm.Close(); } catch { }
        omenKeyDesktopAppForm = null;
      }
      if (omenKeyUwpAppForm != null && !ReferenceEquals(omenKeyUwpAppForm, keepForm)) {
        try { if (!omenKeyUwpAppForm.IsDisposed) omenKeyUwpAppForm.Close(); } catch { }
        omenKeyUwpAppForm = null;
      }
      if (omenKeyShortcutForm != null && !ReferenceEquals(omenKeyShortcutForm, keepForm)) {
        try { if (!omenKeyShortcutForm.IsDisposed) omenKeyShortcutForm.Close(); } catch { }
        omenKeyShortcutForm = null;
      }
    }

    static void HandleOmenKeyAction() {
      if (!omenKeyTriggered) return;

      omenKeyTriggered = false;
      switch (omenKey) {
        case OmenKeyActions.Overlay:
          ToggleFloatingBarByOmenKey();
          break;
        case OmenKeyActions.App:
          LaunchOmenKeyApp();
          break;
        case OmenKeyActions.Shortcut:
          SendOmenKeyShortcut();
          break;
        case OmenKeyActions.Preset:
          SwitchPresetByOmenKey();
          break;
      }
    }

    static void ApplyOmenKeyAction(string action, bool save = true) {
      omenKey = OmenKeyActions.IsKnown(action) ? action : OmenKeyActions.Default;
      checkFloatingTimer.Enabled = OmenKeyActions.UsesPipe(omenKey);

      OmenKeyOff();
      if (omenKey != OmenKeyActions.None)
        OmenKeyOn(omenKey);

      if (save)
        SaveConfig("OmenKey");
    }

    static void RestoreOmenKeyAction() {
      ApplyOmenKeyAction(omenKey, save: false);
      UpdateCheckedState("omenKeyGroup", GetOmenKeyActionMenuText(omenKey));
    }

    static string GetOmenKeyActionMenuText(string action) {
      switch (action) {
        case OmenKeyActions.Default:
          return Strings.OmenKeyDefault;
        case OmenKeyActions.Overlay:
          return Strings.OmenKeyToggle;
        case OmenKeyActions.App:
          return Strings.OmenKeyLaunchApp;
        case OmenKeyActions.Shortcut:
          return Strings.OmenKeyShortcut;
        case OmenKeyActions.Preset:
          return Strings.OmenKeySwitchPreset;
        case OmenKeyActions.None:
          return Strings.OmenKeyNone;
        default:
          return Strings.OmenKeyDefault;
      }
    }

    static void ToggleFloatingBarByOmenKey() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\OmenSuperHub")) {
          if (key != null) {
            if ((string)key.GetValue("FloatingBar", "off") == "on") {
              floatingBar = "off";
              CloseFloatingForm();
              UpdateCheckedState("floatingBarGroup", Strings.FloatingHide);
            } else {
              floatingBar = "on";
              ShowFloatingForm();
              UpdateCheckedState("floatingBarGroup", Strings.FloatingShow);
            }
            SaveConfig("FloatingBar");
          }
        }
      } catch (Exception ex) {
        Logger.Error($"Error restoring configuration: {ex.Message}");
      }
    }

    static void LaunchOmenKeyApp() {
      if (!IsOmenKeyAppTargetAvailable()) {
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.OmenKeyAppNotFound, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }

      try {
        if (IsOmenKeyShellAppTarget(omenKeyAppPath)) {
          Process.Start(new ProcessStartInfo {
            FileName = "explorer.exe",
            Arguments = QuoteProcessArgument(omenKeyAppPath),
            UseShellExecute = false
          });
        } else {
          string workingDirectory = Path.GetDirectoryName(omenKeyAppPath);
          var startInfo = new ProcessStartInfo {
            FileName = omenKeyAppPath,
            UseShellExecute = true
          };
          if (!string.IsNullOrWhiteSpace(workingDirectory)) {
            startInfo.WorkingDirectory = workingDirectory;
          }
          Process.Start(startInfo);
        }
      } catch (Exception ex) {
        Logger.Error($"Failed to launch Omen key app: {ex.Message}");
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.OmenKeyAppLaunchFailed(ex.Message), Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    static bool IsOmenKeyShellAppTarget(string target) {
      return !string.IsNullOrWhiteSpace(target) && target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsOmenKeyAppTargetAvailable() {
      if (string.IsNullOrWhiteSpace(omenKeyAppPath)) return false;
      return IsOmenKeyShellAppTarget(omenKeyAppPath) || File.Exists(omenKeyAppPath);
    }

    static string QuoteProcessArgument(string value) {
      if (value == null) return "\"\"";
      return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    static string GetOmenKeyAppDisplayName() {
      if (string.IsNullOrWhiteSpace(omenKeyAppPath)) return Strings.OmenKeyNoAppSelected;
      if (IsOmenKeyShellAppTarget(omenKeyAppPath)) {
        return string.IsNullOrWhiteSpace(omenKeyAppName) ? GetOmenKeyUwpAppId() : omenKeyAppName;
      }
      return Path.GetFileName(omenKeyAppPath);
    }

    static string GetOmenKeyUwpAppId() {
      const string prefix = @"shell:AppsFolder\";
      if (string.IsNullOrWhiteSpace(omenKeyAppPath)) return "";
      if (omenKeyAppPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return omenKeyAppPath.Substring(prefix.Length);
      return omenKeyAppPath;
    }

    static bool IsPresetAvailable(string presetKey) {
      switch (presetKey) {
        case "PresetExtreme":
        case "PresetGpuPriority":
        case "PresetLightUse":
          return isCPUPowerControlSupported;
        case "PresetCustom1":
        case "PresetCustom2":
        case "PresetCustom3":
          return true;
        default:
          return false;
      }
    }

    static List<string> GetAvailablePresetKeys() {
      return PresetOrder.Where(IsPresetAvailable).ToList();
    }

    static string GetDefaultOmenKeyPresetCandidates() {
      return string.Join(";", GetAvailablePresetKeys());
    }

    static List<string> GetOmenKeyPresetCandidateKeys() {
      var available = GetAvailablePresetKeys();
      var candidates = (omenKeyPresetCandidates ?? "")
          .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
          .Where(available.Contains)
          .Distinct()
          .OrderBy(key => Array.IndexOf(PresetOrder, key))
          .ToList();

      if (candidates.Count == 0) {
        if (IsPresetAvailable(currentPreset)) {
          candidates.Add(currentPreset);
        } else if (available.Count > 0) {
          candidates.Add(available[0]);
        }
      }

      omenKeyPresetCandidates = string.Join(";", candidates);
      return candidates;
    }

    static bool SetOmenKeyPresetCandidate(string presetKey, bool enabled) {
      if (!IsPresetAvailable(presetKey)) return false;

      var candidates = GetOmenKeyPresetCandidateKeys();
      if (enabled) {
        if (!candidates.Contains(presetKey))
          candidates.Add(presetKey);
      } else {
        if (candidates.Count <= 1 && candidates.Contains(presetKey))
          return false;
        candidates.Remove(presetKey);
      }

      candidates = candidates
          .Distinct()
          .OrderBy(key => Array.IndexOf(PresetOrder, key))
          .ToList();
      omenKeyPresetCandidates = string.Join(";", candidates);
      return true;
    }

    static void SwitchPresetByOmenKey() {
      var candidates = GetOmenKeyPresetCandidateKeys();
      if (candidates.Count == 0) return;

      int currentIndex = candidates.IndexOf(currentPreset);
      string targetPreset = candidates[(currentIndex + 1) % candidates.Count];
      if (targetPreset != currentPreset) {
        applyPresetLogic(targetPreset);
      } else {
        UpdateTrayIconText();
      }

      ShowOmenKeyPresetNotification();
    }

    static void ShowOmenKeyPresetNotification() {
      if (trayIcon == null) return;

      if (trayIcon.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Visible) {
        return;
      }

      trayIcon.BalloonTipTitle = Strings.OmenKeyPresetBalloonTitle;
      trayIcon.BalloonTipText = Strings.OmenKeyPresetBalloonText(GetCurrentPresetDisplayName());
      trayIcon.BalloonTipIcon = ToolTipIcon.Info;
      trayIcon.ShowBalloonTip(3000);
    }

    static void SelectOmenKeyApp() {
      if (omenKeyDesktopAppForm != null && !omenKeyDesktopAppForm.IsDisposed) {
        ActivateForm(omenKeyDesktopAppForm);
        return;
      }

      CloseOtherOmenKeyForms(null);

      var form = new Form {
        Text = Strings.OmenKeySelectDesktopApp,
        StartPosition = FormStartPosition.CenterScreen,
        Width = 720,
        Height = 240,
        MinimizeBox = false,
        MaximizeBox = false,
        ShowIcon = false
      };
      omenKeyDesktopAppForm = form;

      var promptLabel = new Label {
        Left = 12,
        Top = 12,
        Width = form.ClientSize.Width - 24,
        Height = 36,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        Text = Strings.OmenKeySelectApp
      };
      var pathBox = new TextBox {
        Left = 12,
        Top = promptLabel.Bottom + 8,
        Width = form.ClientSize.Width - 124,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        Text = omenKeyAppPath
      };
      var browseButton = new Button {
        Text = "浏览...",
        Width = 92,
        Height = 30,
        Left = form.ClientSize.Width - 104,
        Top = pathBox.Top - 1,
        Anchor = AnchorStyles.Top | AnchorStyles.Right
      };
      var fileNameLabel = new Label {
        Left = 12,
        Top = pathBox.Bottom + 10,
        Width = form.ClientSize.Width - 24,
        Height = 24,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        ForeColor = System.Drawing.Color.FromArgb(70, 70, 70),
        Text = GetOmenKeyAppDisplayName()
      };
      var okButton = new Button {
        Text = Strings.OK,
        Width = 92,
        Height = 32,
        Left = form.ClientSize.Width - 304,
        Top = form.ClientSize.Height - 44,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right
      };
      var cancelButton = new Button {
        Text = Strings.Cancel,
        Width = 92,
        Height = 32,
        Left = form.ClientSize.Width - 204,
        Top = form.ClientSize.Height - 44,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        DialogResult = DialogResult.Cancel
      };
      cancelButton.Click += (s, e) => form.Close();

      Action refreshPreview = () => {
        string path = pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path)) {
          fileNameLabel.Text = Strings.OmenKeyNoAppSelected;
        } else {
          fileNameLabel.Text = File.Exists(path) ? Path.GetFileName(path) : path;
        }
        okButton.Enabled = !string.IsNullOrWhiteSpace(path);
      };

      browseButton.Click += (s, e) => {
        using (var dialog = new System.Windows.Forms.OpenFileDialog()) {
          dialog.Title = Strings.OmenKeySelectApp;
          dialog.Filter = Strings.OmenKeyAppFilter;
          dialog.CheckFileExists = true;
          dialog.Multiselect = false;
          dialog.RestoreDirectory = true;

          string currentPath = pathBox.Text.Trim();
          if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath)) {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
          } else {
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
          }

          if (dialog.ShowDialog(form) != DialogResult.OK) return;
          pathBox.Text = dialog.FileName;
          refreshPreview();
        }
      };

      pathBox.TextChanged += (s, e) => refreshPreview();
      okButton.Click += (s, e) => {
        string selectedPath = pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath)) {
          MessageBox.Show(form, Strings.OmenKeyAppNotFound, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return;
        }

        omenKeyAppPath = selectedPath;
        omenKeyAppName = "";
        SaveConfig("OmenKeyAppPath");
        SaveConfig("OmenKeyAppName");
        ApplyOmenKeyAction(OmenKeyActions.App);
        UpdateCheckedState("omenKeyGroup", Strings.OmenKeyLaunchApp);
        form.Close();
      };

      form.FormClosed += (s, e) => {
        if (ReferenceEquals(omenKeyDesktopAppForm, form)) omenKeyDesktopAppForm = null;
      };

      form.Controls.Add(promptLabel);
      form.Controls.Add(pathBox);
      form.Controls.Add(browseButton);
      form.Controls.Add(fileNameLabel);
      form.Controls.Add(okButton);
      form.Controls.Add(cancelButton);
      form.CancelButton = cancelButton;
      form.AcceptButton = okButton;

      refreshPreview();
      ActivateForm(form);
    }

    class OmenKeyStartApp {
      public string Name { get; set; }
      public string AppId { get; set; }
    }

    static List<OmenKeyStartApp> LoadStartApps() {
      string command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
          "Get-StartApps | Sort-Object Name | Select-Object Name, AppID | ConvertTo-Json -Compress";
      string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
      var startInfo = new ProcessStartInfo {
        FileName = "powershell.exe",
        Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
      };

      using (var process = new Process { StartInfo = startInfo }) {
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) {
          try { process.Kill(); } catch { }
          throw new TimeoutException("Get-StartApps timed out.");
        }

        string output = outputTask.Result;
        string error = errorTask.Result;
        if (process.ExitCode != 0) {
          throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Get-StartApps failed." : error.Trim());
        }

        if (string.IsNullOrWhiteSpace(output)) return new List<OmenKeyStartApp>();
        JToken token = JToken.Parse(output);
        IEnumerable<JToken> items;
        if (token.Type == JTokenType.Array) {
          items = token.Children();
        } else {
          items = new[] { token };
        }
        return items
            .Select(item => new OmenKeyStartApp {
              Name = (string)item["Name"],
              AppId = (string)item["AppID"]
            })
            .Where(app => !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.AppId))
            .GroupBy(app => app.AppId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
      }
    }

    static void SelectOmenKeyUwpApp() {
      if (omenKeyUwpAppForm != null && !omenKeyUwpAppForm.IsDisposed) {
        ActivateForm(omenKeyUwpAppForm);
        return;
      }

      CloseOtherOmenKeyForms(null);

      List<OmenKeyStartApp> apps;
      try {
        apps = LoadStartApps();
      } catch (Exception ex) {
        Logger.Error($"Failed to load UWP apps: {ex.Message}");
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.OmenKeyUwpLoadFailed(ex.Message), Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
      }

      if (apps.Count == 0) {
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.OmenKeyUwpNoApps, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var form = new Form {
        Text = Strings.OmenKeySelectUwpApp,
        StartPosition = FormStartPosition.CenterScreen,
        Width = Screen.PrimaryScreen.Bounds.Width / 2,
        Height = Screen.PrimaryScreen.Bounds.Height / 2,
        MinimizeBox = false,
        MaximizeBox = false,
        ShowIcon = false
      };
      omenKeyUwpAppForm = form;

      var searchBox = new TextBox {
        Left = 12,
        Top = 12,
        Width = form.ClientSize.Width - 24,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
      };

      var listView = new ListView {
        Left = 12,
        Top = searchBox.Bottom + 8,
        Width = form.ClientSize.Width - 24,
        Height = form.ClientSize.Height - 92,
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false
      };
      listView.Columns.Add(Strings.OmenKeyUwpAppName, listView.Width / 3 - 24);
      listView.Columns.Add("AppID", listView.Width * 2 / 3 - 24);

      var okButton = new Button {
        Text = Strings.OK,
        Width = 92,
        Height = 36,
        Left = form.ClientSize.Width - 204,
        Top = form.ClientSize.Height - 39,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        Enabled = false
      };
      var cancelButton = new Button {
        Text = Strings.Cancel,
        Width = 92,
        Height = 36,
        Left = form.ClientSize.Width - 104,
        Top = form.ClientSize.Height - 39,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        DialogResult = DialogResult.Cancel
      };
      cancelButton.Click += (s, e) => form.Close();

      Action refreshList = () => {
        string filter = searchBox.Text.Trim();
        listView.BeginUpdate();
        listView.Items.Clear();
        foreach (var app in apps.Where(app =>
            string.IsNullOrWhiteSpace(filter) ||
            app.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            app.AppId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) {
          var item = new ListViewItem(app.Name);
          item.SubItems.Add(app.AppId);
          item.Tag = app;
          listView.Items.Add(item);
        }

        string currentAppId = GetOmenKeyUwpAppId();
        foreach (ListViewItem item in listView.Items) {
          var app = (OmenKeyStartApp)item.Tag;
          if (!string.IsNullOrWhiteSpace(currentAppId) && string.Equals(app.AppId, currentAppId, StringComparison.OrdinalIgnoreCase)) {
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            break;
          }
        }
        if (listView.SelectedItems.Count == 0 && listView.Items.Count > 0) {
          listView.Items[0].Selected = true;
          listView.Items[0].Focused = true;
        }
        okButton.Enabled = listView.SelectedItems.Count > 0;
        listView.EndUpdate();
      };

      searchBox.TextChanged += (s, e) => refreshList();
      listView.SelectedIndexChanged += (s, e) => okButton.Enabled = listView.SelectedItems.Count > 0;
      listView.DoubleClick += (s, e) => {
        if (listView.SelectedItems.Count > 0) okButton.PerformClick();
      };
      okButton.Click += (s, e) => {
        if (listView.SelectedItems.Count == 0) return;

        var selectedApp = (OmenKeyStartApp)listView.SelectedItems[0].Tag;
        omenKeyAppPath = @"shell:AppsFolder\" + selectedApp.AppId;
        omenKeyAppName = selectedApp.Name;
        SaveConfig("OmenKeyAppPath");
        SaveConfig("OmenKeyAppName");
        ApplyOmenKeyAction(OmenKeyActions.App);
        UpdateCheckedState("omenKeyGroup", Strings.OmenKeyLaunchApp);
        form.Close();
      };

      form.FormClosed += (s, e) => {
        if (ReferenceEquals(omenKeyUwpAppForm, form)) omenKeyUwpAppForm = null;
      };

      form.Controls.Add(searchBox);
      form.Controls.Add(listView);
      form.Controls.Add(okButton);
      form.Controls.Add(cancelButton);
      form.CancelButton = cancelButton;

      refreshList();
      ActivateForm(form);
    }

    static void SendOmenKeyShortcut() {
      List<ushort> modifierKeys;
      ushort keyCode;
      if (!TryParseOmenKeyShortcut(omenKeyShortcut, out modifierKeys, out keyCode)) {
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.OmenKeyShortcutNotSet, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }

      var inputs = new List<INPUT>();
      foreach (ushort modifier in modifierKeys) {
        inputs.Add(CreateKeyboardInput(modifier, false));
      }
      inputs.Add(CreateKeyboardInput(keyCode, false));
      inputs.Add(CreateKeyboardInput(keyCode, true));
      for (int i = modifierKeys.Count - 1; i >= 0; i--) {
        inputs.Add(CreateKeyboardInput(modifierKeys[i], true));
      }

      uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
      if (sent != inputs.Count) {
        int error = Marshal.GetLastWin32Error();
        Logger.Error($"Failed to send Omen key shortcut. Sent {sent}/{inputs.Count}, Win32Error={error}");
        MessageBox.Show(Application.OpenForms.OfType<HelpForm>().FirstOrDefault(), Strings.OmenKeyShortcutSendFailed(error), Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    static INPUT CreateKeyboardInput(ushort keyCode, bool keyUp) {
      return new INPUT {
        type = INPUT_KEYBOARD,
        u = new InputUnion {
          ki = new KEYBDINPUT {
            wVk = keyCode,
            wScan = 0,
            dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            time = 0,
            dwExtraInfo = IntPtr.Zero
          }
        }
      };
    }

    static void SelectOmenKeyShortcut() {
      if (omenKeyShortcutForm != null && !omenKeyShortcutForm.IsDisposed) {
        ActivateForm(omenKeyShortcutForm);
        return;
      }

      CloseOtherOmenKeyForms(null);

      var form = new Form {
        Text = Strings.OmenKeySetShortcut,
        StartPosition = FormStartPosition.CenterScreen,
        Width = 440,
        Height = 210,
        MinimizeBox = false,
        MaximizeBox = false,
        ShowIcon = false,
        KeyPreview = true
      };
      omenKeyShortcutForm = form;

      var promptLabel = new Label {
        Left = 12,
        Top = 12,
        Width = form.ClientSize.Width - 24,
        Height = 36,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        Text = Strings.OmenKeyShortcutCapturePrompt
      };
      var shortcutBox = new TextBox {
        Left = 12,
        Top = promptLabel.Bottom + 8,
        Width = form.ClientSize.Width - 24,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        ReadOnly = true,
        TabStop = false,
        Text = FormatOmenKeyShortcut(omenKeyShortcut)
      };
      var okButton = new Button {
        Text = Strings.OK,
        Width = 92,
        Height = 32,
        Left = form.ClientSize.Width - 304,
        Top = form.ClientSize.Height - 44,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        Enabled = !string.IsNullOrWhiteSpace(omenKeyShortcut)
      };
      var clearButton = new Button {
        Text = Strings.Clear,
        Width = 92,
        Height = 32,
        Left = form.ClientSize.Width - 204,
        Top = form.ClientSize.Height - 44,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right
      };
      var cancelButton = new Button {
        Text = Strings.Cancel,
        Width = 92,
        Height = 32,
        Left = form.ClientSize.Width - 104,
        Top = form.ClientSize.Height - 44,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        DialogResult = DialogResult.Cancel
      };
      cancelButton.Click += (s, e) => form.Close();

      string capturedShortcut = omenKeyShortcut;
      form.KeyDown += (s, e) => {
        if (IsModifierKey(e.KeyCode)) {
          e.SuppressKeyPress = true;
          return;
        }

        bool winDown = IsWinKeyDown();
        capturedShortcut = BuildOmenKeyShortcut(e.KeyCode, e.Modifiers, winDown);
        shortcutBox.Text = FormatOmenKeyShortcut(capturedShortcut);
        okButton.Enabled = !string.IsNullOrWhiteSpace(capturedShortcut);
        e.SuppressKeyPress = true;
      };
      okButton.Click += (s, e) => {
        if (string.IsNullOrWhiteSpace(capturedShortcut)) {
          MessageBox.Show(form, Strings.OmenKeyShortcutNotSet, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return;
        }

        omenKeyShortcut = capturedShortcut;
        SaveConfig("OmenKeyShortcut");
        ApplyOmenKeyAction(OmenKeyActions.Shortcut);
        UpdateCheckedState("omenKeyGroup", Strings.OmenKeyShortcut);
        form.Close();
      };
      clearButton.Click += (s, e) => {
        capturedShortcut = "";
        shortcutBox.Text = "";
        okButton.Enabled = false;
      };

      form.FormClosed += (s, e) => {
        if (ReferenceEquals(omenKeyShortcutForm, form)) omenKeyShortcutForm = null;
      };

      form.Controls.Add(promptLabel);
      form.Controls.Add(shortcutBox);
      form.Controls.Add(okButton);
      form.Controls.Add(clearButton);
      form.Controls.Add(cancelButton);
      form.CancelButton = cancelButton;
      form.AcceptButton = okButton;

      ActivateForm(form);
    }

    static bool IsWinKeyDown() {
      return (GetAsyncKeyState(VK_LWIN) & unchecked((short)0x8000)) != 0 ||
          (GetAsyncKeyState(VK_RWIN) & unchecked((short)0x8000)) != 0;
    }

    static bool IsModifierKey(Keys keyCode) {
      return keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey || keyCode == Keys.Menu ||
          keyCode == Keys.LWin || keyCode == Keys.RWin;
    }

    static string BuildOmenKeyShortcut(Keys keyCode, Keys modifiers, bool winDown) {
      var parts = new List<string>();
      if ((modifiers & Keys.Control) == Keys.Control) parts.Add("Ctrl");
      if ((modifiers & Keys.Shift) == Keys.Shift) parts.Add("Shift");
      if ((modifiers & Keys.Alt) == Keys.Alt) parts.Add("Alt");
      if (winDown) parts.Add("Win");
      parts.Add((keyCode & Keys.KeyCode).ToString());
      return string.Join("+", parts);
    }

    static string FormatOmenKeyShortcut(string shortcut) {
      if (string.IsNullOrWhiteSpace(shortcut)) return Strings.OmenKeyNoShortcutSelected;
      return string.Join("+", shortcut
          .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(GetShortcutPartDisplayName));
    }

    static string GetShortcutPartDisplayName(string part) {
      switch (part.Trim()) {
        case "Control":
        case "ControlKey":
        case "Ctrl":
          return "Ctrl";
        case "Menu":
        case "Alt":
          return "Alt";
        case "ShiftKey":
        case "Shift":
          return "Shift";
        case "LWin":
        case "RWin":
        case "Win":
          return "Win";
        case "Escape":
          return "Esc";
        case "Return":
          return "Enter";
        case "Prior":
          return "PageUp";
        case "Next":
          return "PageDown";
        case "Snapshot":
          return "PrintScreen";
        case "OemMinus":
          return "-";
        case "Oemplus":
          return "=";
        case "Oemcomma":
          return ",";
        case "OemPeriod":
          return ".";
        default:
          return part.Trim();
      }
    }

    static bool TryParseOmenKeyShortcut(string shortcut, out List<ushort> modifierKeys, out ushort keyCode) {
      modifierKeys = new List<ushort>();
      keyCode = 0;
      if (string.IsNullOrWhiteSpace(shortcut)) return false;

      foreach (string rawPart in shortcut.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)) {
        string part = rawPart.Trim();
        switch (part.ToLowerInvariant()) {
          case "ctrl":
          case "control":
          case "controlkey":
            AddUniqueVirtualKey(modifierKeys, VK_CONTROL);
            break;
          case "shift":
          case "shiftkey":
            AddUniqueVirtualKey(modifierKeys, VK_SHIFT);
            break;
          case "alt":
          case "menu":
            AddUniqueVirtualKey(modifierKeys, VK_MENU);
            break;
          case "win":
          case "lwin":
          case "rwin":
            AddUniqueVirtualKey(modifierKeys, VK_LWIN);
            break;
          default:
            Keys parsedKey;
            if (!Enum.TryParse(part, true, out parsedKey)) return false;
            parsedKey &= Keys.KeyCode;
            if (IsModifierKey(parsedKey) || parsedKey == Keys.None || keyCode != 0) return false;
            keyCode = (ushort)parsedKey;
            break;
        }
      }

      return keyCode != 0;
    }

    static void AddUniqueVirtualKey(List<ushort> keys, int keyCode) {
      ushort value = (ushort)keyCode;
      if (!keys.Contains(value)) keys.Add(value);
    }

    static CancellationTokenSource _pipeCts;
    static void getOmenKeyTask() {
      _pipeCts = new CancellationTokenSource();
      var token = _pipeCts.Token;
      System.Threading.Tasks.Task.Run(() => {
        while (!token.IsCancellationRequested) {
          try {
            using (var pipeServer = new NamedPipeServerStream("OmenSuperHubPipe", PipeDirection.In)) {
              pipeServer.WaitForConnection();
              using (var reader = new StreamReader(pipeServer)) {
                string message = reader.ReadToEnd();
                if (message.Contains("OmenKeyTriggered") && !omenKeyTriggered)
                  omenKeyTriggered = true;
              }
            }
          } catch (Exception) when (token.IsCancellationRequested) {
            break;
          } catch (Exception ex) {
            Logger.Error("Pipe error: " + ex.Message);
          }
        }
      }, token);
    }
  }
}
