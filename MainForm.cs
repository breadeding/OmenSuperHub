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

  public sealed class MainForm : Form {
    private const string SeriesName = "FanSpeed";
    private const int PointHitRadius = 10;

    private readonly Chart cpuChart;
    private readonly Chart gpuChart;
    private readonly int cpuTemperatureMaximum;
    private readonly int gpuTemperatureMaximum;
    private readonly int fanSpeedMaximum;
    private readonly string customFilePath;
    private readonly ToolTip pointToolTip = new ToolTip();

    private Chart draggingChart;
    private DataPoint draggingPoint;

    internal FanCurveEditorResult EditorResult { get; private set; }

    internal MainForm(
        FanCurveProfile initialProfile,
        int cpuTemperatureMaximum,
        int gpuTemperatureMaximum,
        int fanSpeedMaximum,
        string customFilePath) {
      this.cpuTemperatureMaximum = Math.Max(1, cpuTemperatureMaximum);
      this.gpuTemperatureMaximum = Math.Max(1, gpuTemperatureMaximum);
      this.fanSpeedMaximum = Math.Max(100, fanSpeedMaximum);
      this.customFilePath = customFilePath;

      Text = Strings.FanCurveEditorTitle;
      Icon = Properties.Resources.fan;
      StartPosition = FormStartPosition.CenterScreen;
      FormBorderStyle = FormBorderStyle.Sizable;
      MinimumSize = new Size(980, 560);
      Size = new Size(1180, 650);
      MaximizeBox = true;
      MinimizeBox = false;

      var rootLayout = new TableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 3,
        RowCount = 2,
        Padding = new Padding(12)
      };
      rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
      rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
      rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      var instructions = new Label {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = Strings.FanCurveInstructions,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(6, 0, 6, 0)
      };
      rootLayout.Controls.Add(instructions, 0, 0);
      rootLayout.SetColumnSpan(instructions, 2);

      cpuChart = CreateChart(Strings.FanCurveCpuTitle, this.cpuTemperatureMaximum);
      gpuChart = CreateChart(Strings.FanCurveGpuTitle, this.gpuTemperatureMaximum);
      rootLayout.Controls.Add(cpuChart, 0, 1);
      rootLayout.Controls.Add(gpuChart, 1, 1);

      var buttonLayout = new FlowLayoutPanel {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(10, 8, 0, 0)
      };
      Button saveButton = CreateButton(Strings.FanCurveSave);
      Button saveAndApplyButton = CreateButton(Strings.FanCurveSaveAndApply);
      Button cancelButton = CreateButton(Strings.FanCurveCancel);
      Button loadButton = CreateButton(Strings.FanCurveLoad);
      saveButton.Click += (sender, args) => SaveAndClose(FanCurveEditorResult.Saved);
      saveAndApplyButton.Click += (sender, args) => SaveAndClose(FanCurveEditorResult.SavedAndApplied);
      cancelButton.Click += (sender, args) => {
        DialogResult = DialogResult.Cancel;
        Close();
      };
      loadButton.Click += LoadButton_Click;
      buttonLayout.Controls.Add(saveButton);
      buttonLayout.Controls.Add(saveAndApplyButton);
      buttonLayout.Controls.Add(cancelButton);
      buttonLayout.Controls.Add(loadButton);
      rootLayout.Controls.Add(buttonLayout, 2, 0);
      rootLayout.SetRowSpan(buttonLayout, 2);

      Controls.Add(rootLayout);
      AcceptButton = saveButton;
      CancelButton = cancelButton;

      ApplyProfile(initialProfile);
    }

    private Chart CreateChart(string title, int temperatureMaximum) {
      var chart = new Chart {
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Control,
        Margin = new Padding(4, 4, 8, 4),
        Cursor = Cursors.Cross
      };
      chart.Titles.Add(new Title(title) {
        Font = new Font(Font.FontFamily, 11F, FontStyle.Bold)
      });

      var chartArea = new ChartArea("FanSpeedArea");
      chartArea.BackColor = Color.White;
      chartArea.AxisX.Minimum = 0;
      chartArea.AxisX.Maximum = temperatureMaximum;
      chartArea.AxisX.Interval = 10;
      chartArea.AxisX.Title = Strings.FanCurveTemperatureAxis;
      chartArea.AxisX.MajorGrid.LineColor = Color.Gainsboro;
      chartArea.AxisY.Minimum = 0;
      chartArea.AxisY.Maximum = fanSpeedMaximum;
      chartArea.AxisY.Interval = GetFanSpeedInterval(fanSpeedMaximum);
      chartArea.AxisY.Title = Strings.FanCurveFanSpeedAxis;
      chartArea.AxisY.LabelStyle.Format = "0";
      chartArea.AxisY.MajorGrid.LineColor = Color.Gainsboro;
      chart.ChartAreas.Add(chartArea);

      chart.Series.Add(new Series(SeriesName) {
        ChartType = SeriesChartType.Line,
        BorderWidth = 3,
        Color = Color.FromArgb(0, 122, 204),
        MarkerStyle = MarkerStyle.Circle,
        MarkerSize = 9,
        MarkerColor = Color.FromArgb(220, 53, 69),
        MarkerBorderColor = Color.White,
        MarkerBorderWidth = 1,
        ToolTip = "#VALX°C, #VALY RPM"
      });

      chart.MouseDown += Chart_MouseDown;
      chart.MouseMove += Chart_MouseMove;
      chart.MouseUp += Chart_MouseUp;
      chart.MouseLeave += (sender, args) => pointToolTip.Hide(chart);
      return chart;
    }

    private static Button CreateButton(string text) {
      return new Button {
        Text = text,
        Width = 104,
        Height = 38,
        Margin = new Padding(0, 0, 0, 10)
      };
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
      foreach (FanCurvePoint point in points.OrderBy(point => point.Temperature))
        series.Points.AddXY(point.Temperature, point.FanSpeed);
      chart.Invalidate();
    }

    private void Chart_MouseDown(object sender, MouseEventArgs e) {
      var chart = sender as Chart;
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
        return;
      }

      var points = GetPoints(chart);
      points.Add(new FanCurvePoint(roundedTemperature, roundedFanSpeed));
      SetSeriesPoints(chart, points);
    }

    private void Chart_MouseMove(object sender, MouseEventArgs e) {
      var chart = sender as Chart;
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
          string.Format("{0}°C, {1} RPM", (int)Math.Round(point.XValue), (int)Math.Round(point.YValues[0])),
          chart,
          e.X + 14,
          e.Y - 28,
          500);
    }

    private void Chart_MouseUp(object sender, MouseEventArgs e) {
      var chart = sender as Chart;
      if (chart != null) chart.Cursor = Cursors.Cross;
      draggingChart = null;
      draggingPoint = null;
    }

    private void DragPoint(Chart chart, Point location) {
      double temperature;
      double fanSpeed;
      if (!TryGetChartValues(chart, location, out temperature, out fanSpeed)) return;

      List<DataPoint> orderedPoints = chart.Series[SeriesName].Points
          .OrderBy(point => point.XValue)
          .ToList();
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
      chart.Invalidate();
    }

    private DataPoint FindPoint(Chart chart, Point mouseLocation) {
      foreach (DataPoint point in chart.Series[SeriesName].Points) {
        double pointX = chart.ChartAreas[0].AxisX.ValueToPixelPosition(point.XValue);
        double pointY = chart.ChartAreas[0].AxisY.ValueToPixelPosition(point.YValues[0]);
        double distance = Math.Sqrt(
            Math.Pow(pointX - mouseLocation.X, 2) +
            Math.Pow(pointY - mouseLocation.Y, 2));
        if (distance <= PointHitRadius)
          return point;
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
        return xValue >= area.AxisX.Minimum && xValue <= area.AxisX.Maximum &&
               yValue >= area.AxisY.Minimum && yValue <= area.AxisY.Maximum;
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
        } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) {
          MessageBox.Show(this, Strings.FanCurveLoadFailed + Environment.NewLine + ex.Message, Strings.Error,
              MessageBoxButtons.OK, MessageBoxIcon.Error);
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
      } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) {
        MessageBox.Show(this, Strings.FanCurveSaveFailed + Environment.NewLine + ex.Message, Strings.Error,
            MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
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
      if (points.Count < 2 ||
          points.Any(point =>
              point.Temperature < 0 ||
              point.Temperature > temperatureMaximum ||
              point.FanSpeed < 0 ||
              point.FanSpeed > fanSpeedMaximum) ||
          points.GroupBy(point => point.Temperature).Any(group => group.Count() > 1))
        throw new InvalidDataException(Strings.FanCurveOutOfRange);
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
}
