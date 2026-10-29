using DHud.Helpers;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DHud.Services;

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
    private readonly Forms.ToolStripMenuItem _settingsItem;
    private readonly Forms.ToolStripMenuItem _updateItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private bool _overlayVisible = true;

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

        _settingsItem = new Forms.ToolStripMenuItem(Loc.T("T.Settings"), null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Bold)
        };
        _toggleItem = new Forms.ToolStripMenuItem(Loc.T("T.Hide"), null, (_, _) => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty))
        {
            ShortcutKeyDisplayString = toggleOverlayShortcut
        };
        _clickThroughItem = new Forms.ToolStripMenuItem(Loc.T("T.ClickThrough"), null, (_, _) => ClickThroughToggleRequested?.Invoke(this, EventArgs.Empty))
        {
            ShortcutKeyDisplayString = clickThroughShortcut
        };
        _lockItem = new Forms.ToolStripMenuItem(Loc.T("T.Lock"), null, (_, _) => LockToggleRequested?.Invoke(this, EventArgs.Empty));
        _updateItem = new Forms.ToolStripMenuItem(Loc.T("T.CheckUpdates", UpdateService.CurrentVersionText), null,
            (_, _) => UpdateCheckRequested?.Invoke(this, EventArgs.Empty));
        _exitItem = new Forms.ToolStripMenuItem(Loc.T("T.Exit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            Font = new Drawing.Font("Segoe UI", 9.5f),
            BackColor = Drawing.Color.FromArgb(255, 26, 27, 34),
            ForeColor = Drawing.Color.FromArgb(255, 241, 242, 246),
            Padding = new Forms.Padding(0, 4, 0, 4),
            ShowImageMargin = false,
            ShowCheckMargin = true
        };
        menu.Items.AddRange([
            _settingsItem,
            _toggleItem,
            new Forms.ToolStripSeparator(),
            _clickThroughItem,
            _lockItem,
            new Forms.ToolStripSeparator(),
            _updateItem,
            _exitItem
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
        _overlayVisible = overlayVisible;
        _toggleItem.Text = Loc.T(overlayVisible ? "T.Hide" : "T.Show");
        _clickThroughItem.Checked = clickThrough;
        _lockItem.Checked = locked;
    }

    /// <summary>Dil değişince menü metinlerini yeniden yazar.</summary>
    public void ApplyLanguage()
    {
        _settingsItem.Text = Loc.T("T.Settings");
        _toggleItem.Text = Loc.T(_overlayVisible ? "T.Hide" : "T.Show");
        _clickThroughItem.Text = Loc.T("T.ClickThrough");
        _lockItem.Text = Loc.T("T.Lock");
        _updateItem.Text = Loc.T("T.CheckUpdates", UpdateService.CurrentVersionText);
        _exitItem.Text = Loc.T("T.Exit");
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
