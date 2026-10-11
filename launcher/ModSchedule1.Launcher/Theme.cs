using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ModSchedule1.Launcher
{
    /// <summary>Paleta escura (estilo GitHub dark) e controles com cantos arredondados.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("0D1117");
        public static readonly Color Card = Hex("161B22");
        public static readonly Color Border = Hex("30363D");
        public static readonly Color Text = Hex("E6EDF3");
        public static readonly Color Muted = Hex("8B949E");
        public static readonly Color Accent = Hex("238636");
        public static readonly Color AccentHover = Hex("2EA043");
        public static readonly Color Green = Hex("3FB950");
        public static readonly Color Amber = Hex("D29922");
        public static readonly Color Red = Hex("F85149");
        public static readonly Color Blue = Hex("58A6FF");
        public static readonly Color Button = Hex("21262D");
        public static readonly Color ButtonHover = Hex("30363D");

        public static Color Hex(string hex) => ColorTranslator.FromHtml("#" + hex);
        public static Font Font(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);
        public static Font Semibold(float size) => new Font("Segoe UI Semibold", size);

        public static Button Btn(string text, Color bg, Color hover, Font font = null)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Text,
                Font = font ?? Font(9.5f),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = false,
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            return b;
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Barra de título escura no Windows 10 20H1+ / 11 (atributo 20; 19 em builds antigos).</summary>
        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            try { if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4); }
            catch { }
        }
    }

    /// <summary>Painel com título, fundo e borda arredondados.</summary>
    public class CardPanel : Panel
    {
        public string Title;

        public CardPanel(string title)
        {
            Title = title;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Padding = new Padding(16, 42, 16, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(r, 10))
            using (var fill = new SolidBrush(Theme.Card))
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            using var f = Theme.Semibold(10.5f);
            TextRenderer.DrawText(e.Graphics, Title, f, new Point(16, 13), Theme.Text);
        }
    }

    /// <summary>"Pílula" de status no cabeçalho.</summary>
    public class Pill : Control
    {
        Color _color = Theme.Muted;

        public Pill()
        {
            DoubleBuffered = true;
            Font = Theme.Semibold(9.5f);
            BackColor = Theme.Bg;
        }

        public void Set(string text, Color color)
        {
            Text = text;
            _color = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(r, Height / 2 - 1))
            using (var fill = new SolidBrush(Color.FromArgb(40, _color)))
            using (var pen = new Pen(Color.FromArgb(160, _color)))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            using (var dot = new SolidBrush(_color))
                e.Graphics.FillEllipse(dot, 14, Height / 2 - 4, 8, 8);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(28, 0, Width - 34, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
