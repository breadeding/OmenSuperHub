using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace OmenSuperHub {
  internal enum FanCurveEditorResult {
    Cancelled,
    Saved,
    SavedAndApplied
  }

  public sealed class FanCurveForm : Form {
    private const string SeriesName = "FanSpeed";
    private const int PointHitRadius = 12;
    private const int PointHitRadiusSquared = PointHitRadius * PointHitRadius;

    private static readonly Color PageBackColor = Color.FromArgb(245, 247, 250);
    private static readonly Color CardBackColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(220, 225, 230);
    private static readonly Color AccentColor = Color.FromArgb(0, 122, 204);
    private static readonly Color MarkerColor = Color.FromArgb(220, 53, 69);
    private static readonly Color GridColor = Color.FromArgb(232, 236, 240);
    private static readonly Color AxisLineColor = Color.FromArgb(200, 205, 210);

    private readonly Chart cpuChart;
    private readonly Chart gpuChart;
    private readonly int cpuTemperatureMaximum;
    private readonly int gpuTemperatureMaximum;
    private readonly int fanSpeedMaximum;
    private readonly string customFilePath;
    private readonly ToolTip pointToolTip;

    private Chart draggingChart;
    private DataPoint draggingPoint;

    internal FanCurveEditorResult EditorResult { get; private set; }

    internal FanCurveForm(
        FanCurveProfile initialProfile,
        int cpuTemperatureMaximum,
        int gpuTemperatureMaximum,
        int fanSpeedMaximum,
        string customFilePath) {
      this.cpuTemperatureMaximum = Math.Max(1, cpuTemperatureMaximum);
      this.gpuTemperatureMaximum = Math.Max(1, gpuTemperatureMaximum);
      this.fanSpeedMaximum = Math.Max(100, fanSpeedMaximum);
      this.customFilePath = customFilePath;

      pointToolTip = new ToolTip {
        AutomaticDelay = 0,
        AutoPopDelay = 2000,
        InitialDelay = 0,
        ReshowDelay = 0,
        ShowAlways = true
      };

      Text = Strings.FanCurveEditorTitle;
      Icon = Properties.Resources.fan;
      StartPosition = FormStartPosition.CenterScreen;
      FormBorderStyle = FormBorderStyle.Sizable;
      MaximizeBox = true;
      MinimizeBox = true;
      BackColor = PageBackColor;

      Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
      Size = new Size(screenBounds.Width * 3 / 4, screenBounds.Height * 3 / 4);
      MinimumSize = new Size(1280, 720);
      AutoScaleMode = AutoScaleMode.Dpi;

      var rootLayout = BuildRootLayout();

      cpuChart = CreateChart("CPU", this.cpuTemperatureMaximum);
      gpuChart = CreateChart("GPU", this.gpuTemperatureMaximum);

      rootLayout.Controls.Add(cpuChart, 0, 1);
      rootLayout.Controls.Add(gpuChart, 1, 1);

      Panel rightPanel = BuildActionPanel(out Button saveButton, out Button saveAndApplyButton, out Button cancelButton, out Button loadButton);
      rootLayout.Controls.Add(rightPanel, 2, 0);
      rootLayout.SetRowSpan(rightPanel, 2);

      Controls.Add(rootLayout);

      AcceptButton = saveButton;
      CancelButton = cancelButton;

      saveButton.Click += (sender, args) => SaveAndClose(FanCurveEditorResult.Saved);
      saveAndApplyButton.Click += (sender, args) => SaveAndClose(FanCurveEditorResult.SavedAndApplied);
      cancelButton.Click += (sender, args) => {
        DialogResult = DialogResult.Cancel;
        Close();
      };
      loadButton.Click += LoadButton_Click;

      ApplyProfile(initialProfile);
    }

    protected override CreateParams CreateParams {
      get {
        CreateParams cp = base.CreateParams;
        cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
        return cp;
      }
    }

    private TableLayoutPanel BuildRootLayout() {
      var rootLayout = new TableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 3,
        RowCount = 2,
        Padding = new Padding(16),
        BackColor = PageBackColor
      };

      rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));

      rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
      rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      var instructions = new Label {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = Strings.FanCurveInstructions,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0),
        ForeColor = Color.FromArgb(70, 70, 70),
        Font = new Font(Font.FontFamily, 12F, FontStyle.Regular)
      };

      rootLayout.Controls.Add(instructions, 0, 0);
      rootLayout.SetColumnSpan(instructions, 2);

      return rootLayout;
    }

    private Panel BuildActionPanel(
        out Button saveButton,
        out Button saveAndApplyButton,
        out Button cancelButton,
        out Button loadButton) {
      var panel = new Panel {
        Dock = DockStyle.Fill,
        Padding = new Padding(12, 8, 0, 0),
        BackColor = PageBackColor
      };

      var inner = new FlowLayoutPanel {
        Dock = DockStyle.Top,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = PageBackColor
      };

      saveButton = CreateActionButton(Strings.FanCurveSave);
      saveAndApplyButton = CreateActionButton(Strings.FanCurveSaveAndApply);
      cancelButton = CreateActionButton(Strings.FanCurveCancel);
      loadButton = CreateActionButton(Strings.FanCurveLoad);

      inner.Controls.Add(saveButton);
      inner.Controls.Add(saveAndApplyButton);
      inner.Controls.Add(cancelButton);
      inner.Controls.Add(loadButton);

      panel.Controls.Add(inner);
      return panel;
    }

    private Button CreateActionButton(string text) {
      return new Button {
        Text = text,
        Width = 190,
        Height = 60,
        Margin = new Padding(0, 0, 0, 20),
        FlatStyle = FlatStyle.Flat,
        BackColor = CardBackColor,
        ForeColor = Color.FromArgb(40, 40, 40),
        Font = new Font(Font.FontFamily, 12F, FontStyle.Regular),
        Cursor = Cursors.Hand
      }.Also(button => {
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 243, 248);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 232, 240);
      });
    }

    private Chart CreateChart(string cpuOrGpu, int temperatureMaximum) {
      var chart = new Chart {
        Dock = DockStyle.Fill,
        BackColor = PageBackColor,
        Margin = new Padding(4, 4, 8, 4),
        Cursor = Cursors.Cross
      };

      chart.AntiAliasing = AntiAliasingStyles.All;
      chart.TextAntiAliasingQuality =
          TextAntiAliasingQuality.High;

      var chartArea = new ChartArea("FanSpeedArea") {
        BackColor = CardBackColor
      };

      chartArea.AxisX.Minimum = 0;
      chartArea.AxisX.Maximum = temperatureMaximum;
      chartArea.AxisX.Interval = 10;
      chartArea.AxisX.Title = $"{cpuOrGpu} {Strings.FanCurveTemperatureAxis}";
      chartArea.AxisX.LineColor = AxisLineColor;
      chartArea.AxisX.MajorGrid.LineColor = GridColor;
      chartArea.AxisX.LabelStyle.ForeColor = Color.FromArgb(90, 90, 90);
      chartArea.AxisX.TitleForeColor = Color.FromArgb(80, 80, 80);
      chartArea.AxisX.TitleFont = new Font(Font.FontFamily, 12F, FontStyle.Bold);

      chartArea.AxisY.Minimum = 0;
      chartArea.AxisY.Maximum = fanSpeedMaximum;
      chartArea.AxisY.Interval = GetFanSpeedInterval(fanSpeedMaximum);
      chartArea.AxisY.Title = Strings.FanCurveFanSpeedAxis;
      chartArea.AxisY.LabelStyle.Format = "0";
      chartArea.AxisY.LineColor = AxisLineColor;
      chartArea.AxisY.MajorGrid.LineColor = GridColor;
      chartArea.AxisY.LabelStyle.ForeColor = Color.FromArgb(90, 90, 90);
      chartArea.AxisY.TitleForeColor = Color.FromArgb(80, 80, 80);
      chartArea.AxisY.TitleFont = new Font(Font.FontFamily, 12F, FontStyle.Bold);

      chart.ChartAreas.Add(chartArea);

      var series = new Series(SeriesName) {
        ChartType = SeriesChartType.Line,
        BorderWidth = 3,
        Color = AccentColor,
        MarkerStyle = MarkerStyle.Circle,
        MarkerSize = 8,
        MarkerColor = MarkerColor,
        MarkerBorderColor = CardBackColor,
        MarkerBorderWidth = 1,
        ToolTip = "#VALX°C, #VALY RPM"
      };
      chart.Series.Add(series);

      chart.MouseDown += Chart_MouseDown;
      chart.MouseMove += Chart_MouseMove;
      chart.MouseUp += Chart_MouseUp;
      chart.MouseLeave += (sender, args) => pointToolTip.Hide(chart);

      return chart;
    }

    private static int GetFanSpeedInterval(int maximum) {
      if (maximum <= 3000) return 500;
      if (maximum <= 7000) return 1000;
      return 2000;
    }

    private void ApplyProfile(FanCurveProfile profile) {
      ValidateProfileForMachine(profile);
      SetSeriesPoints(cpuChart, profile.CpuPoints);
      SetSeriesPoints(gpuChart, profile.GpuPoints);
    }

    private static void SetSeriesPoints(Chart chart, IEnumerable<FanCurvePoint> points) {
      Series series = chart.Series[SeriesName];
      series.Points.Clear();

      foreach (FanCurvePoint point in points.OrderBy(item => item.Temperature)) {
        series.Points.Add(CreateDataPoint(point.Temperature, point.FanSpeed));
      }

      chart.Invalidate();
    }

    private static DataPoint CreateDataPoint(int temperature, int fanSpeed) {
      var point = new DataPoint {
        XValue = temperature
      };
      point.YValues = new double[] { fanSpeed };
      return point;
    }

    private void Chart_MouseDown(object sender, MouseEventArgs e) {
      Chart chart = sender as Chart;
      if (chart == null) return;

      DataPoint point = FindPoint(chart, e.Location);

      if (e.Button == MouseButtons.Right) {
        if (point != null && chart.Series[SeriesName].Points.Count > 2) {
          chart.Series[SeriesName].Points.Remove(point);
          chart.Invalidate();
        }
        return;
      }

      if (e.Button != MouseButtons.Left) return;

      if (point != null) {
        draggingChart = chart;
        draggingPoint = point;
        chart.Cursor = Cursors.SizeAll;
        return;
      }

      double temperature;
      double fanSpeed;
      if (!TryGetChartValues(chart, e.Location, out temperature, out fanSpeed)) return;

      int roundedTemperature = Clamp((int)Math.Round(temperature), 0, GetTemperatureMaximum(chart));
      int roundedFanSpeed = Clamp(RoundFanSpeed(fanSpeed), 0, fanSpeedMaximum);

      DataPoint existingPoint = chart.Series[SeriesName].Points
          .FirstOrDefault(candidate => (int)Math.Round(candidate.XValue) == roundedTemperature);

      if (existingPoint != null) {
        draggingChart = chart;
        draggingPoint = existingPoint;
        chart.Cursor = Cursors.SizeAll;
        return;
      }

      DataPoint newPoint = CreateDataPoint(roundedTemperature, roundedFanSpeed);
      InsertPointSorted(chart.Series[SeriesName].Points, newPoint);
      chart.Invalidate();
    }

    private void Chart_MouseMove(object sender, MouseEventArgs e) {
      Chart chart = sender as Chart;
      if (chart == null) return;

      if (draggingChart == chart && draggingPoint != null) {
        DragPoint(chart, e.Location);
        return;
      }

      DataPoint point = FindPoint(chart, e.Location);
      if (point == null) {
        pointToolTip.Hide(chart);
        chart.Cursor = Cursors.Cross;
        return;
      }

      chart.Cursor = Cursors.Hand;
      pointToolTip.Show(
          $"{(int)Math.Round(point.XValue)}°C, {(int)Math.Round(point.YValues[0])} RPM",
          chart,
          e.X + 14,
          e.Y - 28,
          800);
    }

    private void Chart_MouseUp(object sender, MouseEventArgs e) {
      Chart chart = sender as Chart;
      if (chart != null) chart.Cursor = Cursors.Cross;
      draggingChart = null;
      draggingPoint = null;
    }

    private void DragPoint(Chart chart, Point location) {
      double temperature;
      double fanSpeed;
      if (!TryGetChartValues(chart, location, out temperature, out fanSpeed)) return;

      Series series = chart.Series[SeriesName];
      List<DataPoint> orderedPoints = series.Points.OrderBy(item => item.XValue).ToList();
      int pointIndex = orderedPoints.IndexOf(draggingPoint);
      if (pointIndex < 0) return;

      int minimumTemperature = pointIndex == 0
          ? 0
          : (int)Math.Round(orderedPoints[pointIndex - 1].XValue) + 1;

      int maximumTemperature = pointIndex == orderedPoints.Count - 1
          ? GetTemperatureMaximum(chart)
          : (int)Math.Round(orderedPoints[pointIndex + 1].XValue) - 1;

      draggingPoint.XValue = Clamp((int)Math.Round(temperature), minimumTemperature, maximumTemperature);
      draggingPoint.YValues[0] = Clamp(RoundFanSpeed(fanSpeed), 0, fanSpeedMaximum);

      series.Points.Remove(draggingPoint);
      InsertPointSorted(series.Points, draggingPoint);
      chart.Invalidate();
    }

    private static void InsertPointSorted(DataPointCollection points, DataPoint newPoint) {
      int insertIndex = 0;
      while (insertIndex < points.Count && points[insertIndex].XValue < newPoint.XValue) {
        insertIndex++;
      }
      points.Insert(insertIndex, newPoint);
    }

    private DataPoint FindPoint(Chart chart, Point mouseLocation) {
      foreach (DataPoint point in chart.Series[SeriesName].Points) {
        double pointX = chart.ChartAreas[0].AxisX.ValueToPixelPosition(point.XValue);
        double pointY = chart.ChartAreas[0].AxisY.ValueToPixelPosition(point.YValues[0]);

        double dx = pointX - mouseLocation.X;
        double dy = pointY - mouseLocation.Y;
        double distanceSquared = dx * dx + dy * dy;

        if (distanceSquared <= PointHitRadiusSquared) {
          return point;
        }
      }

      return null;
    }

    private static bool TryGetChartValues(Chart chart, Point location, out double xValue, out double yValue) {
      xValue = 0;
      yValue = 0;

      try {
        ChartArea area = chart.ChartAreas[0];
        xValue = area.AxisX.PixelPositionToValue(location.X);
        yValue = area.AxisY.PixelPositionToValue(location.Y);

        return xValue >= area.AxisX.Minimum &&
               xValue <= area.AxisX.Maximum &&
               yValue >= area.AxisY.Minimum &&
               yValue <= area.AxisY.Maximum;
      } catch (ArgumentException) {
        return false;
      }
    }

    private void LoadButton_Click(object sender, EventArgs e) {
      using (var dialog = new OpenFileDialog {
        Filter = Strings.FanCurveFileFilter,
        InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
        CheckFileExists = true,
        Multiselect = false
      }) {
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try {
          ApplyProfile(FanCurveProfile.Load(dialog.FileName));
        } catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is InvalidDataException) {
          ShowError(Strings.FanCurveLoadFailed, ex);
        }
      }
    }

    private void SaveAndClose(FanCurveEditorResult result) {
      try {
        FanCurveProfile profile = GetProfile();
        ValidateProfileForMachine(profile);
        profile.Save(customFilePath);

        EditorResult = result;
        DialogResult = DialogResult.OK;
        Close();
      } catch (Exception ex) when (
          ex is IOException ||
          ex is UnauthorizedAccessException ||
          ex is InvalidDataException) {
        ShowError(Strings.FanCurveSaveFailed, ex);
      }
    }

    private void ShowError(string message, Exception ex) {
      MessageBox.Show(
          this,
          message + Environment.NewLine + ex.Message,
          Strings.Error,
          MessageBoxButtons.OK,
          MessageBoxIcon.Error);
    }

    private FanCurveProfile GetProfile() {
      return new FanCurveProfile(GetPoints(cpuChart), GetPoints(gpuChart));
    }

    private static List<FanCurvePoint> GetPoints(Chart chart) {
      return chart.Series[SeriesName].Points
          .Select(point => new FanCurvePoint(
              (int)Math.Round(point.XValue),
              (int)Math.Round(point.YValues[0])))
          .OrderBy(point => point.Temperature)
          .ToList();
    }

    private void ValidateProfileForMachine(FanCurveProfile profile) {
      ValidatePointsForMachine(profile.CpuPoints, cpuTemperatureMaximum);
      ValidatePointsForMachine(profile.GpuPoints, gpuTemperatureMaximum);
    }

    private void ValidatePointsForMachine(IList<FanCurvePoint> points, int temperatureMaximum) {
      if (points.Count < 1 ||
          points.Any(point => point.FanSpeed < 0) ||
          points.GroupBy(point => point.Temperature).Any(group => group.Count() > 1)) {
        throw new InvalidDataException(Strings.FanCurveOutOfRange);
      }
    }

    private int GetTemperatureMaximum(Chart chart) {
      return chart == cpuChart ? cpuTemperatureMaximum : gpuTemperatureMaximum;
    }

    private static int RoundFanSpeed(double fanSpeed) {
      return (int)(Math.Round(fanSpeed / 100D) * 100D);
    }

    private static int Clamp(int value, int minimum, int maximum) {
      return Math.Max(minimum, Math.Min(maximum, value));
    }
  }

  internal static class ObjectExtensions {
    internal static T Also<T>(this T value, Action<T> action) {
      action(value);
      return value;
    }
  }
}
