using CpuGpuOverlay.Helpers;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CpuGpuOverlay.Services;

/// <summary>
/// Sistem tepsisi simgesi. Overlay görev çubuğunda görünmediği için uygulamayı
/// yönetmenin (ayarlar, gizle/göster, tıklama geçirme, çıkış) ana yolu budur.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Forms.ToolStripMenuItem _clickThroughItem;
    private readonly Forms.ToolStripMenuItem _lockItem;

    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ClickThroughToggleRequested;
    public event EventHandler? LockToggleRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? UpdateCheckRequested;
    public event EventHandler? BalloonClicked;

    public TrayIconService(string toggleOverlayShortcut, string clickThroughShortcut)
    {
        _icon = CreateLogoIcon();

        var settingsItem = new Forms.ToolStripMenuItem("Ayarlar...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        _toggleItem = new Forms.ToolStripMenuItem("Overlay'i gizle", null, (_, _) => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty))
        {
            ShortcutKeyDisplayString = toggleOverlayShortcut
        };
        _clickThroughItem = new Forms.ToolStripMenuItem("Tıklama geçirme", null, (_, _) => ClickThroughToggleRequested?.Invoke(this, EventArgs.Empty))
        {
            ShortcutKeyDisplayString = clickThroughShortcut
        };
        _lockItem = new Forms.ToolStripMenuItem("Konumu kilitle", null, (_, _) => LockToggleRequested?.Invoke(this, EventArgs.Empty));
        var updateItem = new Forms.ToolStripMenuItem($"Güncellemeleri denetle ({UpdateService.CurrentVersionText})", null,
            (_, _) => UpdateCheckRequested?.Invoke(this, EventArgs.Empty));
        var exitItem = new Forms.ToolStripMenuItem("Çıkış", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([
            settingsItem,
            _toggleItem,
            new Forms.ToolStripSeparator(),
            _clickThroughItem,
            _lockItem,
            new Forms.ToolStripSeparator(),
            updateItem,
            exitItem
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "dHud",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _notifyIcon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke(this, EventArgs.Empty);
    }

    public void SetStates(bool overlayVisible, bool clickThrough, bool locked)
    {
        _toggleItem.Text = overlayVisible ? "Overlay'i gizle" : "Overlay'i göster";
        _clickThroughItem.Checked = clickThrough;
        _lockItem.Checked = locked;
    }

    public void UpdateTooltip(string text)
    {
        // NotifyIcon.Text en fazla 127 karakter olabilir.
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>En son gösterilen balonun türü; tıklamanın hangi balona ait olduğunu ayırt etmek için.</summary>
    public string? BalloonKind { get; private set; }

    public void ShowBalloon(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, string? kind = null)
    {
        BalloonKind = kind;
        _notifyIcon.ShowBalloonTip(5000, title, text, icon);
    }

    /// <summary>
    /// Harici bir .ico dosyasına ihtiyaç duymamak için dHud logosunu (assets/logo.svg) çizer:
    /// koyu panel, mavi HUD köşeleri ve yeşil nabız çizgisi. Koordinatlar logonun 128'lik ölçeğindedir.
    /// </summary>
    private static Drawing.Icon CreateLogoIcon()
    {
        const int size = 64;
        using var bitmap = new Drawing.Bitmap(size, size);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            g.ScaleTransform(size / 128f, size / 128f);

            using (var panel = RoundedRect(4, 4, 120, 120, 28))
            using (var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 28, 30, 37)))
            {
                g.FillPath(background, panel);
            }

            using var bracketPen = new Drawing.Pen(Drawing.Color.FromArgb(255, 59, 130, 246), 9)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
                LineJoin = Drawing.Drawing2D.LineJoin.Round
            };
            g.DrawLines(bracketPen, [new(28, 46), new(28, 28), new(46, 28)]);
            g.DrawLines(bracketPen, [new(82, 28), new(100, 28), new(100, 46)]);
            g.DrawLines(bracketPen, [new(100, 82), new(100, 100), new(82, 100)]);
            g.DrawLines(bracketPen, [new(46, 100), new(28, 100), new(28, 82)]);

            using var pulsePen = new Drawing.Pen(Drawing.Color.FromArgb(255, 74, 222, 128), 9)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
                LineJoin = Drawing.Drawing2D.LineJoin.Round
            };
            g.DrawLines(pulsePen, [new(38, 68), new(52, 68), new(58, 54), new(67, 80), new(75, 60), new(80, 68), new(90, 68)]);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var d = radius * 2;
        var path = new Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
