using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class FloatingForm : Form {
    private PictureBox displayPictureBox;

    public FloatingForm(string text, int textSize, string loc) {
      this.FormBorderStyle = FormBorderStyle.None; // Remove window border
      this.BackColor = Color.Black; // Set background to special color
      this.TransparencyKey = this.BackColor; // Make that color transparent
      this.TopMost = true; // Always stay on top
      this.ShowInTaskbar = false; // Hide from taskbar
      this.StartPosition = FormStartPosition.Manual;

      // 初始化 PictureBox
      displayPictureBox = new PictureBox();
      displayPictureBox.BackColor = Color.Transparent; // 背景色透明
      displayPictureBox.SizeMode = PictureBoxSizeMode.AutoSize; // 自适应大小

      ApplySupersampling(text, textSize); // 应用超采样

      if (loc == "left") {
        // 左上角
        SetPositionTopLeft();
      } else {
        // 右上角
        SetPositionTopRight(textSize);
      }

      this.Controls.Add(displayPictureBox);
      AdjustFormSize();
    }

    private void ApplySupersampling(string text, int textSize) {
      string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
      Bitmap newBitmap = new Bitmap(700, 300); // 创建新的 bitmap
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
      // 释放旧的图片
      displayPictureBox.Image?.Dispose();
      displayPictureBox.Image = newBitmap; // 赋值给 PictureBox
      displayPictureBox.Size = newBitmap.Size;
    }

    private Color GetColorForTitle(string title) {
      // 根据title或其他逻辑为其分配不同的颜色
      switch (title) {
        case "CPU":
          return Color.FromArgb(0, 128, 192);
        case "GPU":
          return Color.FromArgb(0, 128, 192);
        case "Fan":
          return Color.FromArgb(0, 128, 64);
        default:
          return Color.Black; // 默认颜色
      }
    }

    public void SetText(string text, int textSize, string loc) {
      if (InvokeRequired) {
        // 使用 BeginInvoke 以减少 UI 阻塞
        BeginInvoke(new Action(() => SetText(text, textSize, loc)));
        return;
      }
      ApplySupersampling(text, textSize);
      AdjustFormSize();
      if (loc == "left") {
        // 左上角
        SetPositionTopLeft();
      } else {
        // 右上角
        SetPositionTopRight(textSize);
      }
    }

    private void AdjustFormSize() {
      // 根据Label的大小动态调整窗体大小
      this.Size = new Size(displayPictureBox.Width + 20, displayPictureBox.Height + 20);
      displayPictureBox.Location = new Point(10, 10); // 设置label的居中位置
    }

    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    protected override CreateParams CreateParams {
      get {
        CreateParams cp = base.CreateParams;
        cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE; // 设置窗口为透明和不激活
        return cp;
      }
    }

    // 设置窗口位于左上角
    public void SetPositionTopLeft() {
      this.Location = new Point(0, 0);
    }

    // 设置窗口位于右上角
    public void SetPositionTopRight(int textSize) {
      var screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
      this.Location = new Point((int)(screenWidth - textSize * screenWidth / 256), 0);
    }
  }
}
