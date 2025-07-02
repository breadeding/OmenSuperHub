using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace OmenSuperHub {
  public partial class MainForm : Form {
    private static MainForm _instance;
    private Chart cpuChart, gpuChart;
    private CheckBox advancedControlCheckBox;
    private ComboBox profileComboBox;
    private Button createProfileButton, deleteProfileButton;
    private PointF? nearestPoint = null;
    private bool dragging = false;
    private DataPoint selectedPoint = null;

    public MainForm() {
      Text = "Coming Soon";

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

      InitializeFanConfigUI();
    }

    private void InitializeFanConfigUI() {
      // CPU Chart
      cpuChart = new Chart { Dock = DockStyle.Top };
      ConfigureChart(cpuChart, "CPU Fan Speed vs Temperature");
      this.Controls.Add(cpuChart);

      // GPU Chart
      gpuChart = new Chart { Dock = DockStyle.Top };
      ConfigureChart(gpuChart, "GPU Fan Speed vs Temperature");
      this.Controls.Add(gpuChart);

      // Advanced Control CheckBox
      advancedControlCheckBox = new CheckBox {
        Text = "Enable Advanced Control",
        Dock = DockStyle.Top,
        Height = 30,
      };
      advancedControlCheckBox.CheckedChanged += AdvancedControlCheckBox_CheckedChanged;
      this.Controls.Add(advancedControlCheckBox);

      // Profile Management
      profileComboBox = new ComboBox { Dock = DockStyle.Top, Height = 30 };
      createProfileButton = new Button { Text = "Create New Profile", Dock = DockStyle.Top, Height = 30 };
      deleteProfileButton = new Button { Text = "Delete Profile", Dock = DockStyle.Top, Height = 30 };

      this.Controls.Add(profileComboBox);
      this.Controls.Add(createProfileButton);
      this.Controls.Add(deleteProfileButton);

      // Set up event handlers for profile management
      createProfileButton.Click += CreateProfileButton_Click;
      deleteProfileButton.Click += DeleteProfileButton_Click;
    }

    private void ConfigureChart(Chart chart, string title) {
      chart.Titles.Add(title);
      var chartArea = new ChartArea("FanSpeedArea") {
        AxisX = { Minimum = 0, Maximum = 100, Title = "Temperature (°C)" },
        AxisY = { Minimum = 0, Maximum = 7000, Title = "Fan Speed (RPM)" }
      };
      chart.ChartAreas.Add(chartArea);
      chart.Series.Add(new Series("FanSpeed") {
        ChartType = SeriesChartType.Line,
        BorderWidth = 4,
        MarkerStyle = MarkerStyle.Circle, // 设置转折点样式
        MarkerSize = 8, // 设置转折点大小
        MarkerColor = System.Drawing.Color.Red, // 设置转折点颜色
      });

      // Add some default points
      chart.Series["FanSpeed"].Points.AddXY(50, 1600);
      chart.Series["FanSpeed"].Points.AddXY(60, 2000);
      chart.Series["FanSpeed"].Points.AddXY(85, 4000);
      chart.Series["FanSpeed"].Points.AddXY(100, 6100);

      chart.ChartAreas[0].AxisX.Title = "Temperature (°C)";
      chart.ChartAreas[0].AxisY.Title = "Fan Speed (RPM)";

      // Attach event handlers for mouse interactions
      chart.MouseMove += Chart_MouseMove;
      chart.MouseDown += Chart_MouseDown;
      chart.MouseUp += Chart_MouseUp;
    }

    private void Chart_MouseMove(object sender, MouseEventArgs e) {
      if (dragging) {
        Chart_MouseMove_Drag(sender, e);
      } else {
        Chart chart = sender as Chart;
        if (chart == null) return;

        var chartArea = chart.ChartAreas[0];
        double xValue = chartArea.AxisX.PixelPositionToValue(e.X);
        double yValue = chartArea.AxisY.PixelPositionToValue(e.Y);

        // 查找最近的点或线段上的点
        nearestPoint = FindNearestPointOrLine(chart, xValue, yValue);

        // 如果找到最近点，显示其坐标
        if (nearestPoint != null) {
          // 在鼠标附近显示坐标
          ToolTip tt = new ToolTip();
          tt.Show($"({nearestPoint.Value.X} °C, {nearestPoint.Value.Y} RPM)", chart, e.Location.X + 15, e.Location.Y - 15, 1000);
        }

        chart.Invalidate();
      }
    }

    private PointF? FindNearestPointOrLine(Chart chart, double xValue, double yValue) {
      Series series = chart.Series["FanSpeed"];
      PointF? nearest = null;
      double minDist = double.MaxValue;

      for (int i = 0; i < series.Points.Count - 1; i++) {
        var p1 = series.Points[i];
        var p2 = series.Points[i + 1];

        // 计算鼠标到线段的最短距离
        var (dist, projX, projY) = GetDistanceToSegment(
            new PointF((float)p1.XValue, (float)p1.YValues[0]),
            new PointF((float)p2.XValue, (float)p2.YValues[0]),
            new PointF((float)xValue, (float)yValue)
        );

        if (dist < minDist) {
          minDist = dist;
          nearest = new PointF(projX, projY);
        }
      }

      // 如果最近点不是已有的点，则新增这个点
      if (nearest.HasValue && !series.Points.Any(p => p.XValue == nearest.Value.X && p.YValues[0] == nearest.Value.Y)) {
        // 添加新点
        series.Points.AddXY(nearest.Value.X, nearest.Value.Y);

        // 将 DataPoints 转换为 List，然后排序
        var sortedPoints = series.Points.OrderBy(p => p.XValue).ToList();

        // 清除旧点并添加排序后的新点
        series.Points.Clear();
        foreach (var point in sortedPoints) {
          series.Points.Add(point);
        }
      }

      return nearest;
    }


    private (double dist, float projX, float projY) GetDistanceToSegment(PointF p1, PointF p2, PointF p) {
      float dx = p2.X - p1.X;
      float dy = p2.Y - p1.Y;

      if ((dx == 0) && (dy == 0)) {
        dx = p.X - p1.X;
        dy = p.Y - p1.Y;
        return (Math.Sqrt(dx * dx + dy * dy), p1.X, p1.Y);
      }

      float t = ((p.X - p1.X) * dx + (p.Y - p1.Y) * dy) / (dx * dx + dy * dy);

      if (t < 0) {
        dx = p.X - p1.X;
        dy = p.Y - p1.Y;
        return (Math.Sqrt(dx * dx + dy * dy), p1.X, p1.Y);
      } else if (t > 1) {
        dx = p.X - p2.X;
        dy = p.Y - p2.Y;
        return (Math.Sqrt(dx * dx + dy * dy), p2.X, p2.Y);
      } else {
        var projX = p1.X + t * dx;
        var projY = p1.Y + t * dy;
        dx = p.X - projX;
        dy = p.Y - projY;
        return (Math.Sqrt(dx * dx + dy * dy), projX, projY);
      }
    }

    private void Chart_MouseDown(object sender, MouseEventArgs e) {
      if (nearestPoint != null) {
        dragging = true;
      }
    }

    private void Chart_MouseUp(object sender, MouseEventArgs e) {
      dragging = false;
      selectedPoint = null;
    }

    private void Chart_MouseMove_Drag(object sender, MouseEventArgs e) {
      if (dragging && selectedPoint != null) {
        Chart chart = sender as Chart;
        var chartArea = chart.ChartAreas[0];
        double newXValue = chartArea.AxisX.PixelPositionToValue(e.X);
        double newYValue = chartArea.AxisY.PixelPositionToValue(e.Y);

        // 限制坐标范围
        newXValue = Math.Max(0, Math.Min(100, newXValue));
        newYValue = Math.Max(0, Math.Min(7000, newYValue));

        // 更新点的位置并重绘图表
        selectedPoint.XValue = Math.Floor(newXValue); // 横坐标取整
        selectedPoint.YValues[0] = Math.Floor(newYValue / 100) * 100; // 纵坐标按100取整
        chart.Invalidate();
      }
    }

    private void AdvancedControlCheckBox_CheckedChanged(object sender, EventArgs e) {
      if (advancedControlCheckBox.Checked) {
        // Enable advanced control: show four charts
        ShowAdvancedCharts();
      } else {
        // Disable advanced control: show two charts
        HideAdvancedCharts();
      }
    }

    private void ShowAdvancedCharts() {
      // Add additional charts for advanced control
      // Implement additional chart initialization for Fan 1 and Fan 2 if advanced control is enabled
    }

    private void HideAdvancedCharts() {
      // Remove additional charts for basic control
      // Implement hiding logic for extra charts
    }

    private void CreateProfileButton_Click(object sender, EventArgs e) {
      // Implement logic to create a new profile
    }

    private void DeleteProfileButton_Click(object sender, EventArgs e) {
      // Implement logic to delete the selected profile
    }

    public static MainForm Instance {
      get {
        if (_instance == null || _instance.IsDisposed) {
          _instance = new MainForm();
        }
        return _instance;
      }
    }

    private void MainForm_FormClosed(object sender, FormClosedEventArgs e) {
      _instance = null;
    }
  }
}
