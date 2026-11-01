using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Panoya kopyalanan son metinleri listeler; birine tıklayınca tekrar panoya konur. Geçmiş YALNIZCA bellekte tutulur
/// (diske yazılmaz, widget kapalıyken toplanmaz) ve parola yöneticilerinin "izleme dışı" işaretli kopyaları yok sayılır.
/// </summary>
public sealed class ClipboardWidgetWindow : WidgetWindowBase
{
    private const int MaxItems = 10;
    private const int MaxChars = 2000;
    private const int ShownChars = 44;

    private static readonly List<string> History = [];

    private readonly StackPanel _list = new();
    private readonly TextBlock _status;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IntPtr _hwnd;
    private bool _ignoreNext;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Clipboard);

    protected override bool ZoomByWidth => true;   // liste: genişlikle büyür, fazla yükseklik daha çok satır gösterir

    public ClipboardWidgetWindow()
    {
        Card.Width = 280;
        var clear = IconButton("🗑", Loc.T("W.ClipClear"), (_, _) => ClearHistory());
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Clipboard"), WidgetIds.Clipboard, clear));
        _list.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_list);
        _status = Subtle("");
        _status.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_status);
        Card.Child = root;

        RestorePosition(WidgetIds.Clipboard, 4, 296);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); _status.Text = ""; };
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
            }
        };

        Rebuild();
        ApplyOptions();
    }

    /// <summary>Ekran görüntüsü üretimi için: geçmişi verilen metinlerle doldurur.</summary>
    internal static void SeedDemo(IEnumerable<string> items)
    {
        History.Clear();
        History.AddRange(items);
    }

    /// <summary>Tüm geçmişi siler (ayarlardaki düğme de bunu kullanır).</summary>
    public static void ClearHistory()
    {
        History.Clear();
        foreach (var window in Application.Current.Windows.OfType<ClipboardWidgetWindow>())
        {
            window.Rebuild();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        NativeMethods.AddClipboardFormatListener(_hwnd);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            // Pano o anda başka bir uygulamada meşgul olabilir; kısa bir gecikmeyle okunur.
            Dispatcher.BeginInvoke(() => Capture(retry: true), DispatcherPriority.Background);
        }

        return IntPtr.Zero;
    }

    private void Capture(bool retry)
    {
        if (_ignoreNext)
        {
            _ignoreNext = false;
            return;
        }

        string text;
        try
        {
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing") || !Clipboard.ContainsText())
            {
                return;
            }

            text = Clipboard.GetText();
        }
        catch (COMException)
        {
            if (retry)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, _) => { timer.Stop(); Capture(retry: false); };
                timer.Start();
            }

            return;
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > MaxChars)
        {
            text = text[..MaxChars];
        }

        History.Remove(text);
        History.Insert(0, text);
        while (History.Count > MaxItems)
        {
            History.RemoveAt(History.Count - 1);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        if (History.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.ClipEmpty")));
            return;
        }

        foreach (var item in History.ToList())
        {
            var oneLine = string.Join(' ', item.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            var shown = oneLine.Length > ShownChars ? oneLine[..ShownChars] + "…" : oneLine;
            var button = new Button
            {
                Content = new TextBlock { Text = shown, TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(8, 4, 8, 4),
                ToolTip = item.Length > 300 ? item[..300] + "…" : item
            };
            button.SetResourceReference(StyleProperty, "DarkButton");
            var text = item;
            button.Click += (_, _) => CopyBack(text);
            _list.Children.Add(button);
        }
    }

    private void CopyBack(string text)
    {
        try
        {
            _ignoreNext = true; // kendi yazdığımızı geçmişe tekrar eklemeyelim
            Clipboard.SetText(text);
            _status.Text = Loc.T("W.ClipCopied");
        }
        catch (COMException)
        {
            _ignoreNext = false;
            _status.Text = Loc.T("W.ClipBusy");
        }

        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
