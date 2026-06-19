using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class FloatingForm : Form {
    private const int ContentPadding = 10;
    private const int ScreenMargin = 10;

    private PictureBox displayPictureBox;

    private sealed class DisplayLine {
      public string Title;
      public string Value;
      public float TitleWidth;
      public float ValueWidth;
    }

    public FloatingForm(string text, int textSize, string loc, Screen screen = null) {
      this.FormBorderStyle = FormBorderStyle.None;
      this.TopMost = true;
      this.ShowInTaskbar = false;
      this.StartPosition = FormStartPosition.Manual;

      displayPictureBox = new PictureBox();
      displayPictureBox.BackColor = Color.Transparent;
      displayPictureBox.SizeMode = PictureBoxSizeMode.AutoSize;
      this.Controls.Add(displayPictureBox);
      this.HandleCreated += (s, e) => RenderCurrentImage();

      ApplySupersampling(text, textSize, screen);
      AdjustFormSize();
      SetAnchoredPosition(loc, screen);
    }

    private void ApplySupersampling(string text, int textSize, Screen screen) {
      if (string.IsNullOrEmpty(text) || textSize <= 0)
        return;

      var workingArea = (screen ?? Screen.PrimaryScreen).WorkingArea;
      int maxBitmapWidth = Math.Max(1, workingArea.Width - ScreenMargin * 2);
      float maxContentWidth = Math.Max(1, maxBitmapWidth - ContentPadding * 2);
      Bitmap newBitmap = null;

      try {
        using (var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
        using (var measureGraphics = Graphics.FromImage(measureBitmap))
        using (var font = new Font("Calibri", textSize, FontStyle.Bold, GraphicsUnit.World))
        using (var format = CreateTextFormat()) {
          var lines = BuildDisplayLines(text, font, measureGraphics, format, maxContentWidth);
          float lineHeight = (float)Math.Ceiling(font.GetHeight(measureGraphics));
          float widestLine = 1;
          foreach (var line in lines)
            widestLine = Math.Max(widestLine, line.TitleWidth + line.ValueWidth);

          int bitmapWidth = Math.Min(maxBitmapWidth,
            Math.Max(1, (int)Math.Ceiling(widestLine) + ContentPadding * 2));
          int bitmapHeight = Math.Max(1,
            (int)Math.Ceiling(lineHeight * lines.Count) + ContentPadding * 2);

          newBitmap = new Bitmap(bitmapWidth, bitmapHeight, PixelFormat.Format32bppArgb);
          using (Graphics graphics = Graphics.FromImage(newBitmap)) {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // graphics.Clear(Color.FromArgb(180, 0, 0, 0));
            //透明
            graphics.Clear(Color.Transparent);

            float y = ContentPadding;
            using (Brush valueBrush = new SolidBrush(Color.FromArgb(255, 128, 0))) {
              foreach (var line in lines) {
                float x = ContentPadding;
                if (!string.IsNullOrEmpty(line.Title)) {
                  string titleKey = line.Title.TrimEnd(':').Trim();
                  using (Brush titleBrush = new SolidBrush(GetColorForTitle(titleKey)))
                    graphics.DrawString(line.Title, font, titleBrush, new PointF(x, y), format);
                  x += line.TitleWidth;
                }

                graphics.DrawString(line.Value, font, valueBrush, new PointF(x, y), format);
                y += lineHeight;
              }
            }
          }
        }
      } catch (ArgumentException ex) {
        newBitmap?.Dispose();
        System.Diagnostics.Debug.WriteLine($"Bitmap 创建失败: {ex.Message}");
        return;
      }

      var oldImage = displayPictureBox.Image;
      displayPictureBox.Image = newBitmap;
      displayPictureBox.Size = newBitmap.Size;
      oldImage?.Dispose();
    }

    private static StringFormat CreateTextFormat() {
      var format = (StringFormat)StringFormat.GenericTypographic.Clone();
      format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
      return format;
    }

    private static float MeasureText(Graphics graphics, string text, Font font, StringFormat format) {
      if (string.IsNullOrEmpty(text)) return 0;
      return graphics.MeasureString(text, font, int.MaxValue, format).Width;
    }

    private static List<DisplayLine> BuildDisplayLines(string text, Font font,
        Graphics graphics, StringFormat format, float maxContentWidth) {
      var result = new List<DisplayLine>();
      string[] sourceLines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

      foreach (string sourceLine in sourceLines) {
        int separatorIndex = sourceLine.IndexOf(':');
        string title = separatorIndex >= 0 ? sourceLine.Substring(0, separatorIndex).Trim() + ":" : "";
        string value = separatorIndex >= 0 ? sourceLine.Substring(separatorIndex + 1).Trim() : sourceLine.Trim();
        string valuePrefix = title.Length > 0 ? " " : "";
        float titleWidth = MeasureText(graphics, title, font, format);
        string[] segments = value.Split(new[] { ',' }, StringSplitOptions.None);
        string current = "";
        bool firstOutputLine = true;

        for (int i = 0; i < segments.Length; i++) {
          string segment = segments[i].Trim();
          if (i < segments.Length - 1) segment += ",";
          string candidate = current.Length == 0 ? segment : current + " " + segment;
          float prefixWidth = firstOutputLine ? titleWidth : 0;
          float candidateWidth = MeasureText(graphics, valuePrefix + candidate, font, format);

          if (current.Length > 0 && prefixWidth + candidateWidth > maxContentWidth) {
            AddDisplayLine(result, firstOutputLine ? title : "", valuePrefix + current,
              font, graphics, format);
            firstOutputLine = false;
            valuePrefix = "";
            current = segment;
          } else {
            current = candidate;
          }
        }

        AddDisplayLine(result, firstOutputLine ? title : "", valuePrefix + current,
          font, graphics, format);
      }

      if (result.Count == 0)
        AddDisplayLine(result, "", " ", font, graphics, format);
      return result;
    }

    private static void AddDisplayLine(List<DisplayLine> lines, string title, string value,
        Font font, Graphics graphics, StringFormat format) {
      lines.Add(new DisplayLine {
        Title = title,
        Value = value,
        TitleWidth = MeasureText(graphics, title, font, format),
        ValueWidth = MeasureText(graphics, value, font, format)
      });
    }

    private Color GetColorForTitle(string title) {
      switch (title) {
        case "CPU": return Color.FromArgb(0, 128, 192);
        case "GPU": return Color.FromArgb(0, 128, 192);
        case "Fan": return Color.FromArgb(0, 128, 64);
        default:    return Color.FromArgb(0, 128, 192);
      }
    }

    public void SetText(string text, int textSize, string loc, Screen screen = null) {
      if (InvokeRequired) {
        BeginInvoke(new Action(() => SetText(text, textSize, loc, screen)));
        return;
      }
      if (textSize <= 0) return;
      ApplySupersampling(text, textSize, screen);
      AdjustFormSize();
      SetAnchoredPosition(loc, screen);
      RenderCurrentImage();
    }

    private void AdjustFormSize() {
      this.Size = displayPictureBox.Size;
      displayPictureBox.Location = Point.Empty;
    }

    protected override void OnMove(EventArgs e) {
      base.OnMove(e);
      if (displayPictureBox.Image is Bitmap bmp && IsHandleCreated)
        RenderLayered(bmp);
    }

    protected override CreateParams CreateParams {
      get {
        CreateParams cp = base.CreateParams;
        cp.ExStyle |= WS_EX_LAYERED
                   | WS_EX_TRANSPARENT
                   | WS_EX_NOACTIVATE;
        return cp;
      }
    }

    public void SetPositionTopLeft(Screen screen = null) {
      SetAnchoredPosition("left", screen);
    }

    public void SetPositionTopRight(int textSize, Screen screen = null) {
      SetAnchoredPosition("right", screen);
    }

    private void SetAnchoredPosition(string loc, Screen screen) {
      var wa = (screen ?? Screen.PrimaryScreen).WorkingArea;
      int desiredX = loc == "left"
        ? wa.Left + ScreenMargin
        : wa.Right - this.Width - ScreenMargin;
      int desiredY = wa.Top + ScreenMargin;
      int maxX = wa.Right - this.Width;
      int maxY = wa.Bottom - this.Height;

      int x = maxX < wa.Left ? wa.Left : Math.Max(wa.Left, Math.Min(desiredX, maxX));
      int y = maxY < wa.Top ? wa.Top : Math.Max(wa.Top, Math.Min(desiredY, maxY));
      this.Location = new Point(x, y);
    }

    private void RenderCurrentImage() {
      if (displayPictureBox.Image is Bitmap bitmap && IsHandleCreated)
        RenderLayered(bitmap);
    }

    private void RenderLayered(Bitmap bitmap) {
      if (bitmap == null) return;

      IntPtr screenDC  = GetDC(IntPtr.Zero);
      IntPtr memDC     = CreateCompatibleDC(screenDC);
      IntPtr hBitmap   = bitmap.GetHbitmap(Color.FromArgb(0));
      IntPtr oldBitmap = SelectObject(memDC, hBitmap);

      NativeSize  size  = new NativeSize(bitmap.Width, bitmap.Height);
      NativePoint ptSrc = new NativePoint(0, 0);
      NativePoint ptDst = new NativePoint(this.Left, this.Top);

      BLENDFUNCTION blend = new BLENDFUNCTION {
        BlendOp             = AC_SRC_OVER,
        BlendFlags          = 0,
        SourceConstantAlpha = 255,
        AlphaFormat         = AC_SRC_ALPHA
      };

      UpdateLayeredWindow(this.Handle, screenDC, ref ptDst, ref size,
                          memDC, ref ptSrc, 0, ref blend, ULW_ALPHA);

      SelectObject(memDC, oldBitmap);
      DeleteObject(hBitmap);
      DeleteDC(memDC);
      ReleaseDC(IntPtr.Zero, screenDC);
    }

    // ── 常量 ─────────────────────────────────────────────────────────────
    private const int  WS_EX_LAYERED     = 0x80000;
    private const int  WS_EX_TRANSPARENT = 0x20;
    private const int  WS_EX_NOACTIVATE  = 0x08000000;
    private const int  ULW_ALPHA         = 0x02;
    private const byte AC_SRC_OVER       = 0x00;
    private const byte AC_SRC_ALPHA      = 0x01;

    // ── 结构体 ───────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize  { public int cx, cy; public NativeSize(int x, int y)  { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int x,  y;  public NativePoint(int x, int y) { this.x = x; this.y = y; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    // ── P/Invoke ─────────────────────────────────────────────────────────
    [DllImport("user32.dll")] static extern bool   UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")]  static extern bool   DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")]  static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")]  static extern bool   DeleteObject(IntPtr hObject);
  }
}
