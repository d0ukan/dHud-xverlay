using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DHud.Services;
using NAudio.CoreAudioApi;

namespace DHud.Widgets;

/// <summary>Ses düzeyi, sessize alma ve mikrofon kapatma. Windows'un varsayılan çıkış/giriş aygıtını kullanır; görünürken 2 saniyede bir değerleri okur.</summary>
public sealed class AudioWidgetWindow : WidgetWindowBase
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Button _speakerButton;
    private readonly Slider _slider;
    private readonly TextBlock _percent;
    private readonly Button _micButton;
    private readonly TextBlock _micState;
    private bool _updating;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Audio);

    public AudioWidgetWindow()
    {
        Card.Width = 260;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Sound"), WidgetIds.Audio));

        // Hoparlör: sessize al + kaydırıcı + yüzde
        var speaker = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        speaker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        speaker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        speaker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _speakerButton = new Button { Content = "🔊", Width = 34, Margin = new Thickness(0, 0, 8, 0), ToolTip = Loc.T("W.Mute") };
        _speakerButton.SetResourceReference(StyleProperty, "DarkButton");
        _speakerButton.Click += (_, _) => ToggleMute(DataFlow.Render);
        _slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
        _slider.ValueChanged += (_, _) => OnSlider();
        _percent = new TextBlock { MinWidth = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_percent, 2);
        speaker.Children.Add(_speakerButton);
        speaker.Children.Add(_slider);
        speaker.Children.Add(_percent);
        root.Children.Add(speaker);

        // Mikrofon
        var mic = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        mic.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _micButton = new Button { Content = "🎤", Width = 34, Margin = new Thickness(0, 0, 8, 0), ToolTip = Loc.T("W.MicMute") };
        _micButton.SetResourceReference(StyleProperty, "DarkButton");
        _micButton.Click += (_, _) => ToggleMute(DataFlow.Capture);
        _micState = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_micState, 1);
        mic.Children.Add(_micButton);
        mic.Children.Add(_micState);
        root.Children.Add(mic);
        Card.Child = root;

        RestorePosition(WidgetIds.Audio, 7, 276);
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
                Refresh();
            }
            else
            {
                _timer.Stop();
            }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _enumerator.Dispose();
        };

        Refresh();
        ApplyOptions();
    }

    private MMDevice? Default(DataFlow flow)
    {
        try
        {
            return _enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)
                ? _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia)
                : null;
        }
        catch (Exception)
        {
            return null; // aygıt takıp çıkarılırken geçici hata
        }
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            using (var render = Default(DataFlow.Render))
            {
                if (render is null)
                {
                    _slider.IsEnabled = _speakerButton.IsEnabled = false;
                    _percent.Text = "--";
                }
                else
                {
                    var volume = render.AudioEndpointVolume;
                    _slider.IsEnabled = _speakerButton.IsEnabled = true;
                    if (!_slider.IsMouseCaptureWithin)
                    {
                        _slider.Value = Math.Round(volume.MasterVolumeLevelScalar * 100);
                    }

                    _percent.Text = volume.Mute ? Loc.T("W.Muted") : $"{Math.Round(volume.MasterVolumeLevelScalar * 100)}%";
                    _speakerButton.Content = volume.Mute ? "🔇" : "🔊";
                }
            }

            using var capture = Default(DataFlow.Capture);
            _micButton.IsEnabled = capture is not null;
            if (capture is null)
            {
                _micState.Text = Loc.T("W.NoMic");
            }
            else
            {
                var muted = capture.AudioEndpointVolume.Mute;
                _micState.Text = muted ? Loc.T("W.MicOff") : Loc.T("W.MicOn");
                _micButton.Content = muted ? "🚫" : "🎤";
            }
        }
        catch (Exception)
        {
            // Ses hizmeti o an yanıt vermedi; sonraki turda yeniden denenir.
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnSlider()
    {
        if (_updating)
        {
            return;
        }

        try
        {
            using var render = Default(DataFlow.Render);
            if (render is not null)
            {
                render.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(_slider.Value / 100);
                _percent.Text = $"{Math.Round(_slider.Value)}%";
            }
        }
        catch (Exception)
        {
            // Yazılamadı; bir sonraki yenilemede gerçek değer okunur.
        }
    }

    private void ToggleMute(DataFlow flow)
    {
        try
        {
            using var device = Default(flow);
            if (device is not null)
            {
                device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
            }
        }
        catch (Exception)
        {
            // Aygıt değişmiş olabilir.
        }

        Refresh();
    }
}
