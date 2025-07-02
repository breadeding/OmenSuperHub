using System;
using System.Diagnostics; // 用于打开浏览器
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class HelpForm : Form {
    private static HelpForm _instance;
    public HelpForm() {
      this.TopMost = true;
      Text = "OmenSuperHub";

      // 获取屏幕的大小
      Rectangle screenBounds = Screen.PrimaryScreen.Bounds;

      // 计算窗体的大小为屏幕大小的一半
      Size formSize = new Size(screenBounds.Width / 2, screenBounds.Height / 2);

      // 设置窗体的大小
      this.Size = formSize;

      // 计算窗体的位置使其位于屏幕的中央
      Point formLocation = new Point(
          (screenBounds.Width - formSize.Width) / 2,
          (screenBounds.Height - formSize.Height) / 2);

      // 设置窗体的位置
      this.StartPosition = FormStartPosition.Manual;
      this.Location = formLocation;

      Icon = Properties.Resources.fan;

      var panel = new Panel() {
        Dock = DockStyle.Fill,
        Padding = new Padding(12),  // 设置 Panel 的内边距
        AutoScroll = true,
        BackColor = SystemColors.Control
      };

      Assembly assembly = Assembly.GetExecutingAssembly();
      Version version = assembly.GetName().Version;

      var richTextBox = new RichTextBox() {
        Dock = DockStyle.Fill,
        Text = "Version: " + version +
        "\nUpdate Notes:\n" +
        "(1) Automatically retries once if DB unlock fails;\n" +
        "(2) Fixed issue where fixed fan speed might not restore after reboot;\n" +
        "(3) Fixed crash in floating window.\n\n" +

        "This project is open-sourced at: https://github.com/breadeding/OmenSuperHub\n\n" +

        "1.   Fan Configuration:\n" +
        "(1) The program supports two fan curve profiles. Silent mode loads \"silent.txt\" (for conservative settings), Cool mode loads \"cool.txt\" (for aggressive cooling). The format is: “60,2000,2300,50,2000,2300” — CPU temp, GPU temp, then 2 fan speeds. No missing fields, commas only. You can edit the files directly. Interpolation is performed automatically. For example, 50,3000,3400 and 52,3200,3600 will result in 3200/3400 at 51°C. Changes take effect after re-clicking the mode;\n" +
        "(2) The program updates fan speed immediately based on temperature. The smoothing level ('Real-time', 'High', 'Medium', 'Low') controls how gradually speed changes with temperature.\n\n" +

        "2.   Fan Control:\n" +
        "(1) 'Auto' sets speed based on the higher value of CPU or GPU target fan RPM;\n" +
        "(2) 'Max fan' relies on BIOS and might not be truly maximum — manual or auto might yield higher speeds;\n" +
        "(3) On the HP Omen 9, fan speed ranges from 0–6400 RPM, but going below 2000 requires disabling 'Fan Always On' in BIOS.\n\n" +

        "3.   Performance Control:\n" +
        "(1) 'Turbo' and 'Balanced' behave differently per model. On Omen 9: Balanced limits CPU PL1 to 55W and restricts GPU power. Switching between them resets CPU power. These modes affect max performance, not power saving. Use hybrid GPU mode and disable GPU-monitoring apps to save power;\n" +
        "(2) GPU power = BTGP + CTGP + DB. DB boosts GPU power when CPU usage is low. Example: BTGP=80W, total power=170W. If CPU drops from 60W to 30W, GPU can jump from 110W to 140W via DB. Enable CTGP and DB for max performance;\n" +
        "(3) DB version refers to driver version under Device Manager > Software Components > NVIDIA Platform Controllers and Framework. Use driver 31.0.15.3730 (from 537.42). Using newer DB drivers with old GPU drivers may lock GPU at base power;\n" +
        "(4) 'Unlock version' removes all other DB drivers, keeps 537.42 DB, and toggles it to lock power state (BTGP + CTGP + DB). This avoids DB throttling when CPU power is high. 'Standard version' reverts the driver state. Updating NVIDIA drivers resets DB, so re-unlock is needed;\n" +
        "(5) OSH briefly switches to Turbo + CTGP + DB to unlock. Avoid unlocking during high CPU usage or it may fail. Unlock state resets on reboot, so auto-run is recommended;\n" +
        "(6) If unlocking fails due to abnormal GPU power, try unlocking again;\n" +
        "(7) Changing CPU power sets both PL1 and PL2. Using ThrottleStop may interfere with this;\n" +
        "(8) GPU frequency limits allow smoother power control (like flattening the curve in MSI Afterburner), but it’s not an overclock.\n\n" +

        "4.   Hardware Monitor:\n" +
        "(1) You can enable/disable sensors. If using hybrid mode, disable GPU monitoring to prevent high CPU usage from toggling GPU on/off.\n\n" +

        "5.   Floating Window:\n" +
        "(1) When enabled, hardware stats appear as an overlay at the top of the screen, refreshed every second.\n\n" +

        "6.   Omen Key Settings:\n" +
        "(1) 'Default': bound to the Task Scheduler entry 'Omen Key'. You can edit the launched program;\n" +
        "(2) 'Toggle Floating Window': uses OSH with a flag to trigger toggle;\n" +
        "(3) May depend on HP services — disabling them might break the key;\n" +
        "(4) 'Unbind' disables the key.\n\n" +

        "7.   Other Settings:\n" +
        "(1) 'Default Icon': built-in program icon;\n" +
        "(2) 'Custom Icon': requires custom.ico in the program folder;\n" +
        "(3) 'Dynamic Icon': shows CPU temp as icon, updated every second;\n" +
        "(4) Enabling autostart creates a scheduled task. If you've set one manually, remove it. Old OSH startup method is also removed here;\n" +
        "(5) Disabling autostart removes the scheduled task.\n\n",



        BorderStyle = BorderStyle.None,  // 隐藏边框
        Font = new Font("Microsoft YaHei UI", 12, FontStyle.Regular),
        ReadOnly = true,  // 设置为只读模式
        BackColor = SystemColors.Control,  // 设置背景颜色与 Label 一致
        ScrollBars = RichTextBoxScrollBars.Both
      };

      // 启用自动检测 URL
      richTextBox.DetectUrls = true;

      // 添加 LinkClicked 事件处理
      richTextBox.LinkClicked += (sender, e) => {
        Process.Start(new ProcessStartInfo(e.LinkText) { UseShellExecute = true });
      };

      panel.Controls.Add(richTextBox);
      this.Controls.Add(panel);
    }

    public static HelpForm Instance {
      get {
        if (_instance == null || _instance.IsDisposed) {
          _instance = new HelpForm();
        }
        return _instance;
      }
    }

    private void HelpForm_FormClosed(object sender, FormClosedEventArgs e) {
      _instance = null;
    }
  }
}
