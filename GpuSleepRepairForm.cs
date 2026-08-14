using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OmenSuperHub {
  public sealed class GpuSleepRepairForm : Form {
    readonly List<GpuSleepManager.GpuProcessInfo> processes;
    readonly ListView processList;
    readonly Button applyButton, restartButton, retryButton, restoreButton;
    public string TransactionId { get; private set; }

    public GpuSleepRepairForm(IEnumerable<GpuSleepManager.GpuProcessInfo> detected) {
      processes = detected?.ToList() ?? new List<GpuSleepManager.GpuProcessInfo>();
      Text = Strings.GpuSleepRepairTitle;
      StartPosition = FormStartPosition.CenterScreen;
      Size = new Size(920, 520);
      MinimizeBox = false;
      MaximizeBox = false;

      var description = new Label {
        Dock = DockStyle.Top,
        Height = 58,
        Padding = new Padding(10),
        Text = Strings.GpuSleepRepairDescription
      };
      processList = new ListView {
        Dock = DockStyle.Fill,
        CheckBoxes = true,
        FullRowSelect = true,
        GridLines = true,
        View = View.Details
      };
      processList.Columns.Add(Strings.GpuSleepProcessColumn, 170);
      processList.Columns.Add("PID", 70);
      processList.Columns.Add(Strings.GpuSleepUsageColumn, 90);
      processList.Columns.Add(Strings.GpuSleepPreferenceColumn, 130);
      processList.Columns.Add(Strings.GpuSleepCustomListColumn, 90);
      processList.Columns.Add(Strings.GpuSleepPathColumn, 330);
      foreach (GpuSleepManager.GpuProcessInfo process in processes) {
        var item = new ListViewItem(process.ProcessName ?? "?") { Tag = process, Checked = process.CanConfigurePreference && !process.IsSystemProcess };
        item.SubItems.Add(process.ProcessId.ToString());
        item.SubItems.Add(process.UsageType ?? "?");
        item.SubItems.Add(process.PreferenceDisplay ?? Strings.GpuPreferenceUnavailable);
        item.SubItems.Add(process.PreferenceEntryExisted ? Strings.Yes : Strings.No);
        item.SubItems.Add(process.ExecutablePath ?? Strings.GpuPreferenceUnavailable);
        if (!process.CanConfigurePreference) item.ForeColor = Color.Gray;
        processList.Items.Add(item);
      }

      var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
      var closeButton = new Button { Text = Strings.Cancel, AutoSize = true };
      closeButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
      retryButton = new Button { Text = Strings.GpuSleepRetry, AutoSize = true, Enabled = false };
      retryButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
      restartButton = new Button { Text = Strings.GpuSleepRestartSelected, AutoSize = true, Enabled = false };
      restartButton.Click += RestartSelected;
      restoreButton = new Button { Text = Strings.GpuPreferenceRestore, AutoSize = true, Enabled = false };
      restoreButton.Click += RestorePreferences;
      applyButton = new Button { Text = Strings.GpuPreferenceApplyPowerSaving, AutoSize = true };
      applyButton.Click += ApplyPreferences;
      buttons.Controls.Add(closeButton);
      buttons.Controls.Add(retryButton);
      buttons.Controls.Add(restartButton);
      buttons.Controls.Add(restoreButton);
      buttons.Controls.Add(applyButton);
      Controls.Add(processList);
      Controls.Add(description);
      Controls.Add(buttons);
    }

    List<GpuSleepManager.GpuProcessInfo> SelectedProcesses() {
      return processList.CheckedItems.Cast<ListViewItem>()
          .Select(i => i.Tag as GpuSleepManager.GpuProcessInfo)
          .Where(p => p != null && p.CanConfigurePreference).ToList();
    }

    void ApplyPreferences(object sender, EventArgs e) {
      List<GpuSleepManager.GpuProcessInfo> selected = SelectedProcesses();
      if (selected.Count == 0) { MessageBox.Show(this, Strings.GpuSleepSelectApplication, Strings.Hint); return; }
      if (selected.Any(p => string.Equals(p.ProcessName, "dwm.exe", StringComparison.OrdinalIgnoreCase)) &&
          MessageBox.Show(this, Strings.GpuSleepDwmWarning, Strings.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
      try {
        TransactionId = GpuSleepManager.ApplyPowerSavingPreferences(selected);
        if (string.IsNullOrEmpty(TransactionId)) return;
        applyButton.Enabled = false;
        restartButton.Enabled = true;
        restoreButton.Enabled = true;
        MessageBox.Show(this, Strings.GpuPreferenceAppliedRestartRequired, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
      } catch (Exception ex) {
        MessageBox.Show(this, Strings.GpuPreferenceApplyFailed(ex.Message), Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    void RestartSelected(object sender, EventArgs e) {
      List<GpuSleepManager.GpuProcessInfo> selected = SelectedProcesses();
      if (MessageBox.Show(this, Strings.GpuSleepRestartWarning, Strings.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
      GpuSleepManager.RestartApplications(selected, true);
      retryButton.Enabled = true;
      MessageBox.Show(this, Strings.GpuSleepApplicationsRestarted, Strings.Hint, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void RestorePreferences(object sender, EventArgs e) {
      if (string.IsNullOrEmpty(TransactionId)) return;
      GpuSleepManager.RestoreResult result = GpuSleepManager.RestorePreferences(TransactionId, false);
      if (result.ConflictedPaths.Count > 0) {
        if (MessageBox.Show(this, Strings.GpuPreferenceConflict, Strings.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        result = GpuSleepManager.RestorePreferences(TransactionId, true);
      }
      if (!result.Success) { MessageBox.Show(this, result.Error ?? Strings.GpuPreferenceRestoreFailed, Strings.Error); return; }
      MessageBox.Show(this, Strings.GpuPreferenceRestoredRestartRequired, Strings.Hint);
      if (MessageBox.Show(this, Strings.GpuSleepRestartWarning, Strings.Warning, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        GpuSleepManager.RestartApplications(SelectedProcesses(), true);
      TransactionId = null;
      restoreButton.Enabled = false;
      restartButton.Enabled = false;
      retryButton.Enabled = false;
      applyButton.Enabled = true;
    }
  }
}
