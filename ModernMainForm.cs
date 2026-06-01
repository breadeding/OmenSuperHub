using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace OmenSuperHub {
  public partial class ModernMainForm : Form {
    private static ModernMainForm _instance;
    private TabControl mainTabControl;
    private Panel monitoringPanel;
    private Panel fanControlPanel;
    private Panel settingsPanel;
    private Label cpuTempLabel, cpuPowerLabel;
    private Label gpuTempLabel, gpuPowerLabel;
    private Label fanSpeedLabel;
    private TrackBar fanSpeedTrackBar;
    private ComboBox fanModeComboBox;
    private Label cpuLimitLabel, gpuLimitLabel;
    private TrackBar cpuLimitTrackBar, gpuLimitTrackBar;
    private CheckBox monitorCpuCheckBox, monitorGpuCheckBox, monitorFanCheckBox;
    private Timer updateTimer;
    private Chart tempChart;

    public ModernMainForm() {
      InitializeModernUI();
      this.FormClosed += (s, e) => _instance = null;
    }

    private void InitializeModernUI() {
      // 窗体基本属性
      this.Text = "Omen SuperHub - 性能管理中心";
      this.Size = new Size(1000, 700);
      this.StartPosition = FormStartPosition.CenterScreen;
      this.Icon = Properties.Resources.fan;
      this.BackColor = Color.FromArgb(240, 240, 240);
      this.Font = new Font("Microsoft YaHei", 10, FontStyle.Regular);

      // 主容器
      mainTabControl = new TabControl {
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Point(15, 10)
      };

      // 创建选项卡
      CreateMonitoringTab();
      CreateFanControlTab();
      CreateSettingsTab();
      CreateAdvancedTab();

      this.Controls.Add(mainTabControl);

      // 启动定时器更新数据
      updateTimer = new Timer();
      updateTimer.Interval = 1000;
      updateTimer.Tick += UpdateTimer_Tick;
      updateTimer.Start();
    }

    private void CreateMonitoringTab() {
      TabPage monitoringTab = new TabPage("📊 实时监控") {
        BackColor = Color.White,
        Padding = new Padding(15)
      };

      // 创建表格布局
      TableLayoutPanel mainLayout = new TableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 3,
        AutoSize = true
      };
      mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
      mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

      // CPU 监控卡片
      Panel cpuPanel = CreateMonitorCard("CPU 处理器", Color.FromArgb(52, 152, 219));
      cpuTempLabel = new Label { Text = "温度: 65°C", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(52, 152, 219) };
      cpuPowerLabel = new Label { Text = "功率: 45W", Font = new Font("Microsoft YaHei", 14), ForeColor = Color.Gray };
      cpuPanel.Controls.Add(cpuTempLabel);
      cpuPanel.Controls.Add(cpuPowerLabel);

      // GPU 监控卡片
      Panel gpuPanel = CreateMonitorCard("GPU 显卡", Color.FromArgb(46, 204, 113));
      gpuTempLabel = new Label { Text = "温度: 58°C", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(46, 204, 113) };
      gpuPowerLabel = new Label { Text = "功率: 35W", Font = new Font("Microsoft YaHei", 14), ForeColor = Color.Gray };
      gpuPanel.Controls.Add(gpuTempLabel);
      gpuPanel.Controls.Add(gpuPowerLabel);

      // 风扇监控卡片
      Panel fanPanel = CreateMonitorCard("风扇速度", Color.FromArgb(155, 89, 182));
      fanSpeedLabel = new Label { Text = "转速: 2500 RPM", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(155, 89, 182) };
      fanPanel.Controls.Add(fanSpeedLabel);

      mainLayout.Controls.Add(cpuPanel, 0, 0);
      mainLayout.Controls.Add(gpuPanel, 1, 0);
      mainLayout.Controls.Add(fanPanel, 0, 1);

      // 温度趋势图表
      tempChart = new Chart {
        Dock = DockStyle.Bottom,
        Height = 250,
        Margin = new Padding(0, 20, 0, 0)
      };
      ConfigureTemperatureChart();

      monitoringTab.Controls.Add(tempChart);
      monitoringTab.Controls.Add(mainLayout);

      mainTabControl.TabPages.Add(monitoringTab);
    }

    private void CreateFanControlTab() {
      TabPage fanControlTab = new TabPage("❄️ 风扇控制") {
        BackColor = Color.White,
        Padding = new Padding(15)
      };

      TableLayoutPanel layout = new TableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 5,
        AutoSize = true
      };

      // 风扇模式选择
      Label modeLabel = new Label { Text = "风扇模式:", Font = new Font("Microsoft YaHei", 12, FontStyle.Bold), AutoSize = true };
      fanModeComboBox = new ComboBox {
        Items = { "自动", "性能", "平衡", "安静", "自定义" },
        SelectedIndex = 0,
        Width = 200,
        Height = 35,
        Font = new Font("Microsoft YaHei", 11)
      };
      fanModeComboBox.SelectedIndexChanged += FanModeComboBox_SelectedIndexChanged;

      Panel modePanel = new Panel { Height = 50, Dock = DockStyle.Top };
      modePanel.Controls.Add(fanModeComboBox);
      modePanel.Controls.Add(modeLabel);

      // 风扇速度调节
      Label speedLabel = new Label { Text = "风扇速度: 50%", Font = new Font("Microsoft YaHei", 12, FontStyle.Bold), AutoSize = true };
      fanSpeedTrackBar = new TrackBar {
        Minimum = 0,
        Maximum = 100,
        Value = 50,
        Dock = DockStyle.Top,
        Height = 50
      };
      fanSpeedTrackBar.ValueChanged += FanSpeedTrackBar_ValueChanged;

      Panel speedPanel = new Panel { Height = 70, Dock = DockStyle.Top };
      speedPanel.Controls.Add(fanSpeedTrackBar);
      speedPanel.Controls.Add(speedLabel);

      // 预设按钮
      Panel presetsPanel = new Panel { Height = 60, Dock = DockStyle.Top };
      presetsPanel.Controls.Add(CreatePresetButton("🔧 性能模式", 10, 10, () => { fanModeComboBox.SelectedIndex = 1; }));
      presetsPanel.Controls.Add(CreatePresetButton("⚖️ 平衡模式", 150, 10, () => { fanModeComboBox.SelectedIndex = 2; }));
      presetsPanel.Controls.Add(CreatePresetButton("🔇 安静模式", 290, 10, () => { fanModeComboBox.SelectedIndex = 3; }));

      layout.Controls.Add(presetsPanel);
      layout.Controls.Add(speedPanel);
      layout.Controls.Add(modePanel);

      fanControlTab.Controls.Add(layout);
      mainTabControl.TabPages.Add(fanControlTab);
    }

    private void CreateSettingsTab() {
      TabPage settingsTab = new TabPage("⚙️ 设置") {
        BackColor = Color.White,
        Padding = new Padding(15)
      };

      TableLayoutPanel layout = new TableLayoutPanel {
        Dock = DockStyle.Top,
        ColumnCount = 2,
        RowCount = 4,
        AutoSize = true
      };
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

      // CPU 功耗限制
      Label cpuLimitTextLabel = new Label { Text = "CPU 功耗限制:", Font = new Font("Microsoft YaHei", 11, FontStyle.Bold) };
      cpuLimitLabel = new Label { Text = "45W", Font = new Font("Microsoft YaHei", 11) };
      cpuLimitTrackBar = new TrackBar { Minimum = 10, Maximum = 100, Value = 45, Height = 35 };
      cpuLimitTrackBar.ValueChanged += (s, e) => cpuLimitLabel.Text = $"{cpuLimitTrackBar.Value}W";

      Panel cpuPanel = new Panel { Height = 50, Dock = DockStyle.Top };
      cpuPanel.Controls.Add(cpuLimitTrackBar);
      cpuPanel.Controls.Add(cpuLimitLabel);
      cpuPanel.Controls.Add(cpuLimitTextLabel);

      // GPU 功耗限制
      Label gpuLimitTextLabel = new Label { Text = "GPU 功耗限制:", Font = new Font("Microsoft YaHei", 11, FontStyle.Bold) };
      gpuLimitLabel = new Label { Text = "80W", Font = new Font("Microsoft YaHei", 11) };
      gpuLimitTrackBar = new TrackBar { Minimum = 10, Maximum = 150, Value = 80, Height = 35 };
      gpuLimitTrackBar.ValueChanged += (s, e) => gpuLimitLabel.Text = $"{gpuLimitTrackBar.Value}W";

      Panel gpuPanel = new Panel { Height = 50, Dock = DockStyle.Top };
      gpuPanel.Controls.Add(gpuLimitTrackBar);
      gpuPanel.Controls.Add(gpuLimitLabel);
      gpuPanel.Controls.Add(gpuLimitTextLabel);

      // 监控选项
      Panel monitorPanel = new Panel { Height = 100, Dock = DockStyle.Top };
      monitorCpuCheckBox = new CheckBox { Text = "监控 CPU", Location = new Point(10, 10), Width = 150, Checked = true };
      monitorGpuCheckBox = new CheckBox { Text = "监控 GPU", Location = new Point(10, 40), Width = 150, Checked = true };
      monitorFanCheckBox = new CheckBox { Text = "监控风扇", Location = new Point(10, 70), Width = 150, Checked = true };
      monitorPanel.Controls.Add(monitorCpuCheckBox);
      monitorPanel.Controls.Add(monitorGpuCheckBox);
      monitorPanel.Controls.Add(monitorFanCheckBox);

      Label monitorLabel = new Label { Text = "监控选项:", Font = new Font("Microsoft YaHei", 11, FontStyle.Bold), Location = new Point(0, 0) };
      monitorPanel.Controls.Add(monitorLabel);

      settingsTab.Controls.Add(cpuPanel);
      settingsTab.Controls.Add(gpuPanel);
      settingsTab.Controls.Add(monitorPanel);

      mainTabControl.TabPages.Add(settingsTab);
    }

    private void CreateAdvancedTab() {
      TabPage advancedTab = new TabPage("🚀 高级") {
        BackColor = Color.White,
        Padding = new Padding(15)
      };

      Label advancedLabel = new Label {
        Text = "高级功能（待开发）\n\n• GPU 功耗解锁\n• 动态功率转移\n• 自定义风扇曲线\n• 性能监控日志",
        Font = new Font("Microsoft YaHei", 12),
        AutoSize = true,
        Margin = new Padding(20)
      };

      advancedTab.Controls.Add(advancedLabel);
      mainTabControl.TabPages.Add(advancedTab);
    }

    private Panel CreateMonitorCard(string title, Color accentColor) {
      Panel card = new Panel {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
        Height = 180,
        Margin = new Padding(10),
        Padding = new Padding(15)
      };

      Label titleLabel = new Label {
        Text = title,
        Font = new Font("Microsoft YaHei", 13, FontStyle.Bold),
        ForeColor = accentColor,
        Location = new Point(15, 10),
        AutoSize = true
      };

      // 上边框颜色
      Label borderTop = new Label {
        BackColor = accentColor,
        Height = 4,
        Dock = DockStyle.Top
      };

      card.Controls.Add(borderTop);
      card.Controls.Add(titleLabel);

      return card;
    }

    private Button CreatePresetButton(string text, int x, int y, Action onClick) {
      Button btn = new Button {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(120, 40),
        Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
        BackColor = Color.FromArgb(52, 152, 219),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand
      };
      btn.FlatAppearance.BorderSize = 0;
      btn.Click += (s, e) => onClick?.Invoke();
      return btn;
    }

    private void ConfigureTemperatureChart() {
      tempChart.Titles.Add(new Title("温度趋势 (最近 60 秒)") { Font = new Font("Microsoft YaHei", 12, FontStyle.Bold) });

      ChartArea chartArea = new ChartArea("MainArea") {
        AxisX = { Minimum = 0, Maximum = 60, Title = "时间 (秒)" },
        AxisY = { Minimum = 0, Maximum = 100, Title = "温度 (°C)" }
      };
      tempChart.ChartAreas.Add(chartArea);

      Series cpuSeries = new Series("CPU") {
        ChartType = SeriesChartType.Line,
        BorderWidth = 2,
        Color = Color.FromArgb(52, 152, 219)
      };

      Series gpuSeries = new Series("GPU") {
        ChartType = SeriesChartType.Line,
        BorderWidth = 2,
        Color = Color.FromArgb(46, 204, 113)
      };

      tempChart.Series.Add(cpuSeries);
      tempChart.Series.Add(gpuSeries);

      // 添加示例数据
      for (int i = 0; i < 60; i++) {
        cpuSeries.Points.AddXY(i, 50 + Math.Sin(i * 0.1) * 15);
        gpuSeries.Points.AddXY(i, 45 + Math.Cos(i * 0.1) * 12);
      }
    }

    private void UpdateTimer_Tick(object sender, EventArgs e) {
      // 这里会由 Program.cs 调用更新方法
      // 更新 CPU/GPU 温度和功率标签
    }

    private void FanModeComboBox_SelectedIndexChanged(object sender, EventArgs e) {
      // 处理风扇模式变化
      string selectedMode = (string)fanModeComboBox.SelectedItem;
      // 调用实际的风扇模式设置
    }

    private void FanSpeedTrackBar_ValueChanged(object sender, EventArgs e) {
      // 处理风扇速度变化
      int speed = fanSpeedTrackBar.Value;
      // 调用实际的风扇速度设置
    }

    // 公共方法供 Program.cs 调用
    public void UpdateMonitoringData(float cpuTemp, float cpuPower, float gpuTemp, float gpuPower, int fanSpeed) {
      if (InvokeRequired) {
        BeginInvoke(new Action(() => UpdateMonitoringData(cpuTemp, cpuPower, gpuTemp, gpuPower, fanSpeed)));
        return;
      }

      cpuTempLabel.Text = $"温度: {cpuTemp:F1}°C";
      cpuPowerLabel.Text = $"功率: {cpuPower:F1}W";
      gpuTempLabel.Text = $"温度: {gpuTemp:F1}°C";
      gpuPowerLabel.Text = $"功率: {gpuPower:F1}W";
      fanSpeedLabel.Text = $"转速: {fanSpeed} RPM";

      // 更新图表
      if (tempChart.Series.Count > 0) {
        var cpuSeries = tempChart.Series["CPU"];
        var gpuSeries = tempChart.Series["GPU"];

        if (cpuSeries.Points.Count > 60) cpuSeries.Points.RemoveAt(0);
        if (gpuSeries.Points.Count > 60) gpuSeries.Points.RemoveAt(0);

        cpuSeries.Points.AddXY(cpuSeries.Points.Count, cpuTemp);
        gpuSeries.Points.AddXY(gpuSeries.Points.Count, gpuTemp);
      }
    }

    public static ModernMainForm Instance {
      get {
        if (_instance == null || _instance.IsDisposed) {
          _instance = new ModernMainForm();
        }
        return _instance;
      }
    }
  }
}
