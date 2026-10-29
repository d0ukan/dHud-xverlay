using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CpuGpuOverlay.Helpers;

/// <summary>
/// Tepsi menüsünü uygulamanın koyu temasına uydurur (arka plan, vurgu rengi, ayraç, onay işareti).
/// Renkler App.xaml / SettingsWindow ile aynıdır.
/// </summary>
public sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private static readonly Drawing.Color Background = Drawing.Color.FromArgb(255, 26, 27, 34);
    private static readonly Drawing.Color Border = Drawing.Color.FromArgb(255, 58, 61, 74);
    private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(255, 48, 51, 66);
    private static readonly Drawing.Color Text = Drawing.Color.FromArgb(255, 241, 242, 246);
    private static readonly Drawing.Color Accent = Drawing.Color.FromArgb(255, 59, 130, 246);

    public DarkMenuRenderer() : base(new DarkColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new Drawing.SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(Border);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    {
        // Görsel kenar boşluğu çizilmez; arka plan düz kalır.
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new Drawing.SolidBrush(Hover);
        using var path = RoundedRect(new Drawing.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2), 6);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Drawing.Color.FromArgb(255, 107, 114, 128);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        using var pen = new Drawing.Pen(Accent, 2f)
        {
            StartCap = Drawing.Drawing2D.LineCap.Round,
            EndCap = Drawing.Drawing2D.LineCap.Round,
            LineJoin = Drawing.Drawing2D.LineJoin.Round
        };
        e.Graphics.DrawLines(pen, [new Drawing.PointF(cx - 4, cy), new Drawing.PointF(cx - 1, cy + 3), new Drawing.PointF(cx + 4, cy - 3)]);
    }

    private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Border;
    }
}
