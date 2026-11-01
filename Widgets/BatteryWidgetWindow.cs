using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DHud.Services;
using Forms = System.Windows.Forms;

namespace DHud.Widgets;

/// <summary>
/// Pil ve cihazlar: dizüstünün pili (şarj durumu, kalan süre) ile Windows'un pil düzeyi bildirdiği cihazlar (Bluetooth kulaklık, mouse, klavye, kol).
/// Yalnızca görünürken yarım dakikada bir okur.
/// </summary>
public sealed class BatteryWidgetWindow : WidgetWindowBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly StackPanel _body = new();
    private List<DeviceBattery>? _devices;   // null: henüz okunmadı
    private bool _busy;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Battery);

    public BatteryWidgetWindow()
    {
        Card.Width = 280;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        var refresh = IconButton("⟳", Loc.T("W.MarketRefresh"), async (_, _) => await RefreshAsync());
        root.Children.Add(TitleBar(Loc.T("W.Battery"), WidgetIds.Battery, refresh));
        _body.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_body);
        Card.Child = root;

        RestorePosition(WidgetIds.Battery, 14, 296);
        Render();

        _timer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
                await RefreshAsync();
            }
            else
            {
                _timer.Stop();
            }
        };
        Closed += (_, _) => _timer.Stop();
        ApplyOptions();
    }

    /// <summary>Ekran görüntüsü üretimi için: örnek cihaz listesini çizer.</summary>
    internal void ShowDemo(List<DeviceBattery> devices)
    {
        _devices = devices;
        Render();
    }

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            _devices = await Task.Run(DeviceBatteryService.Read);
            Render();
        }
        catch (Exception)
        {
            _devices ??= [];
            Render();
        }
        finally
        {
            _busy = false;
        }
    }

    private void Render()
    {
        _body.Children.Clear();

        // Bu bilgisayar
        var power = Forms.SystemInformation.PowerStatus;
        var hasBattery = !power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
                         && power.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
        _body.Children.Add(Section(Loc.T("W.BatteryThisPc")));
        if (hasBattery)
        {
            var percent = Math.Clamp((int)Math.Round(power.BatteryLifePercent * 100), 0, 100);
            var charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            _body.Children.Add(BarRow(Loc.T("W.BatteryPc"), percent, charging ? "⚡" : null));
            _body.Children.Add(Subtle(Status(charging, power.BatteryLifeRemaining, percent)));
        }
        else
        {
            _body.Children.Add(Subtle(Loc.T("W.BatteryNone")));
        }

        // Cihazlar
        _body.Children.Add(Section(Loc.T("W.BatteryDevices")));
        if (_devices is null)
        {
            _body.Children.Add(Subtle(Loc.T("W.TaskLoading")));
        }
        else if (_devices.Count == 0)
        {
            _body.Children.Add(Subtle(Loc.T("W.BatteryNoDevices")));
        }
        else
        {
            foreach (var device in _devices)
            {
                _body.Children.Add(BarRow(device.Name, device.Percent, null));
            }
        }
    }

    private TextBlock Section(string text)
    {
        var block = Subtle(text, 11);
        block.FontWeight = FontWeights.SemiBold;
        block.Margin = new Thickness(0, _body.Children.Count == 0 ? 0 : 12, 0, 4);
        return block;
    }

    /// <summary>"Şarj oluyor" / "Pilde · 2 sa 15 dk kaldı" gibi durum yazısı (kalan süre bilinmiyorsa yazılmaz).</summary>
    internal static string Status(bool charging, int remainingSeconds, int percent)
    {
        if (charging)
        {
            return percent >= 100 ? Loc.T("W.BatteryFull") : Loc.T("W.BatteryCharging");
        }

        return remainingSeconds > 0
            ? Loc.T("W.BatteryOnBattery", Duration(remainingSeconds))
            : Loc.T("W.BatteryOnBatteryUnknown");
    }

    /// <summary>Saniyeyi "2 sa 15 dk" (bir saatin altında "40 dk") yazar.</summary>
    internal static string Duration(int seconds)
    {
        var minutes = Math.Max(seconds, 0) / 60;
        return minutes >= 60 ? Loc.T("W.BatteryHM", minutes / 60, minutes % 60) : Loc.T("W.BatteryM", Math.Max(minutes, 1));
    }

    private FrameworkElement BarRow(string name, int percent, string? badge)
    {
        var brush = percent <= 20 ? "HotBrush" : percent <= 40 ? "WarnBrush" : "GoodBrush";
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name };
        var value = new TextBlock { Text = $"{badge} {percent}%".Trim(), FontWeight = FontWeights.SemiBold };
        value.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Grid.SetColumn(value, 1);

        var track = new Grid { Height = 5, Margin = new Thickness(0, 3, 0, 0) };
        var back = new System.Windows.Controls.Border { CornerRadius = new CornerRadius(2.5) };
        back.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BarTrackBrush");
        track.Children.Add(back);
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(percent, 0.0001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - percent, 0), GridUnitType.Star) });
        var fill = new System.Windows.Controls.Border { CornerRadius = new CornerRadius(2.5) };
        fill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, brush);
        bar.Children.Add(fill);
        track.Children.Add(bar);
        Grid.SetRow(track, 1);
        Grid.SetColumnSpan(track, 2);

        grid.Children.Add(label);
        grid.Children.Add(value);
        grid.Children.Add(track);
        return grid;
    }
}
