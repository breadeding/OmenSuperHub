using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class FloatingForm : Form {
    private PictureBox displayPictureBox;

    public FloatingForm(string text, int textSize, string loc) {
      this.FormBorderStyle = FormBorderStyle.None;
      this.TopMost = true;
      this.ShowInTaskbar = false;
      this.StartPosition = FormStartPosition.Manual;

      displayPictureBox = new PictureBox();
      displayPictureBox.BackColor = Color.Transparent;
      displayPictureBox.SizeMode = PictureBoxSizeMode.AutoSize;

      ApplySupersampling(text, textSize);

      if (loc == "left") {
        SetPositionTopLeft();
      } else {
        SetPositionTopRight(textSize);
      }

      this.Controls.Add(displayPictureBox);
      AdjustFormSize();
    }

    private void ApplySupersampling(string text, int textSize) {
      string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
      Bitmap newBitmap = new Bitmap(700, 300);
      using (Graphics graphics = Graphics.FromImage(newBitmap)) {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        Color customColor = Color.FromArgb(255, 128, 0);
        using (Font font = new Font("Calibri", textSize, FontStyle.Bold, GraphicsUnit.World)) {
          using (Brush brush = new SolidBrush(Color.FromArgb(255, 128, 0))) {
            graphics.DrawString(text, font, brush, new PointF(0, 0));
          }

          PointF point = new PointF(0, 0);
          for (int i = 0; i < lines.Length; i++) {
            string[] parts = lines[i].Split(':');
            if (parts.Length > 1) {
              string title = parts[0].Trim();

              customColor = GetColorForTitle(title);
              using (Brush brush = new SolidBrush(customColor)) {
                for (int j = 1; j <= i; j++)
                  title = '\n' + title;
                graphics.DrawString(title, font, brush, point);
              }
            }
          }
        }
      }

      displayPictureBox.Image?.Dispose();
      displayPictureBox.Image = newBitmap;
      displayPictureBox.Size = newBitmap.Size;

      if (IsHandleCreated)
        RenderLayered(newBitmap);
      else
        this.HandleCreated += (s, e) => RenderLayered(newBitmap);
    }

    private Color GetColorForTitle(string title) {
      switch (title) {
        case "CPU": return Color.FromArgb(0, 128, 192);
        case "GPU": return Color.FromArgb(0, 128, 192);
        case "Fan": return Color.FromArgb(0, 128, 64);
        default:    return Color.Black;
      }
    }

    public void SetText(string text, int textSize, string loc) {
      if (InvokeRequired) {
        BeginInvoke(new Action(() => SetText(text, textSize, loc)));
        return;
      }
      ApplySupersampling(text, textSize);
      AdjustFormSize();
      if (loc == "left") {
        SetPositionTopLeft();
      } else {
        SetPositionTopRight(textSize);
      }
    }

    private void AdjustFormSize() {
      // 根据Label的大小动态调整窗体大小
      this.Size = new Size(displayPictureBox.Width + 20, displayPictureBox.Height + 20);
      displayPictureBox.Location = new Point(10, 10);
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

    public void SetPositionTopLeft() {
      this.Location = new Point(10, 10);
    }

    public void SetPositionTopRight(int textSize) {
      var screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
      this.Location = new Point((int)(screenWidth - textSize * screenWidth / 256 + 10), 10);
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
