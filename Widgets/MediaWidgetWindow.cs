using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DHud.Services;
using Windows.Media.Control;

namespace DHud.Widgets;

/// <summary>
/// Medya oynatıcı: o an çalan parçanın adı, sanatçısı, kapağı ve ilerlemesi; önceki / oynat-duraklat / sonraki düğmeleri.
/// Windows'un sistem medya oturumundan okur (Spotify, tarayıcı, VLC, Groove... hangisi çalıyorsa). Yalnızca görünürken saniyede bir sorgular.
/// </summary>
public sealed class MediaWidgetWindow : WidgetWindowBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border _cover;
    private readonly Image _coverImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _coverGlyph;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _source;
    private readonly ColumnDefinition _done = new() { Width = new GridLength(0, GridUnitType.Star) };
    private readonly ColumnDefinition _rest = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly TextBlock _position;
    private readonly TextBlock _length;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _busy;
    private string _shownKey = string.Empty;   // başlık+sanatçı+albüm: kapak yalnızca parça değişince yeniden okunur
    private bool _coverLoaded;
    private int _coverTries;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Media);

    public MediaWidgetWindow()
    {
        Card.Width = 300;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Media"), WidgetIds.Media));

        var top = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _coverGlyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _coverGlyph.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _cover = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 12, 0) };
        _cover.SetResourceReference(Border.BackgroundProperty, "BarTrackBrush");
        var coverHost = new Grid();
        coverHost.Children.Add(_coverGlyph);
        coverHost.Children.Add(_coverImage);
        _cover.Child = coverHost;
        top.Children.Add(_cover);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _artist = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        _source = Subtle("", 11);
        _source.TextTrimming = TextTrimming.CharacterEllipsis;
        _source.TextWrapping = TextWrapping.NoWrap;
        text.Children.Add(_title);
        text.Children.Add(_artist);
        text.Children.Add(_source);
        Grid.SetColumn(text, 1);
        top.Children.Add(text);
        root.Children.Add(top);

        // İlerleme çubuğu ve süreler
        var track = new Grid { Height = 5, Margin = new Thickness(0, 12, 0, 2) };
        var back = new Border { CornerRadius = new CornerRadius(2.5) };
        back.SetResourceReference(Border.BackgroundProperty, "BarTrackBrush");
        track.Children.Add(back);
        var bar = new Grid();
        bar.ColumnDefinitions.Add(_done);
        bar.ColumnDefinitions.Add(_rest);
        var fill = new Border { CornerRadius = new CornerRadius(2.5) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Children.Add(fill);
        track.Children.Add(bar);
        root.Children.Add(track);

        var times = new Grid();
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _position = Subtle("0:00", 11);
        _length = Subtle("0:00", 11);
        Grid.SetColumn(_length, 1);
        times.Children.Add(_position);
        times.Children.Add(_length);
        root.Children.Add(times);

        // Denetim düğmeleri
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        _previous = Control("", Loc.T("W.MediaPrev"), async s => await s.TrySkipPreviousAsync());
        _playPause = Control("", Loc.T("W.MediaPlayPause"), async s => await s.TryTogglePlayPauseAsync());
        _next = Control("", Loc.T("W.MediaNext"), async s => await s.TrySkipNextAsync());
        buttons.Children.Add(_previous);
        buttons.Children.Add(_playPause);
        buttons.Children.Add(_next);
        root.Children.Add(buttons);
        Card.Child = root;

        RestorePosition(WidgetIds.Media, 11, 316);
        ShowIdle();

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

    private Button Control(string glyph, string tip, Func<GlobalSystemMediaTransportControlsSession, Task> action)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Width = 46,
            Margin = new Thickness(4, 0, 4, 0),
            ToolTip = tip,
            IsEnabled = false
        };
        button.SetResourceReference(StyleProperty, "DarkButton");
        button.Click += async (_, _) =>
        {
            try
            {
                if (_manager?.GetCurrentSession() is { } session)
                {
                    await action(session);
                }
            }
            catch (Exception)
            {
                // Oturum o anda kapanmış olabilir; sonraki turda yeniden okunur.
            }

            await RefreshAsync();
        };
        return button;
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
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            var session = _manager.GetCurrentSession();
            if (session is null)
            {
                ShowIdle();
                return;
            }

            var playback = session.GetPlaybackInfo();
            var props = await session.TryGetMediaPropertiesAsync();
            var key = $"{session.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.AlbumTitle}";

            _title.Text = string.IsNullOrWhiteSpace(props.Title) ? Loc.T("W.MediaUnknown") : props.Title;
            _artist.Text = props.Artist ?? string.Empty;
            _source.Text = AppName(session.SourceAppUserModelId);

            if (key != _shownKey)
            {
                _shownKey = key;
                _coverLoaded = false;
                _coverTries = 0;
                _coverImage.Source = null;
                _coverGlyph.Visibility = Visibility.Visible;
            }

            if (!_coverLoaded && _coverTries < 5)
            {
                _coverTries++;
                if (await LoadCoverAsync(props) is { } image)
                {
                    _coverImage.Source = image;
                    _coverGlyph.Visibility = Visibility.Collapsed;
                    _coverLoaded = true;
                }
            }

            var controls = playback.Controls;
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _playPause.Content = playing ? "" : "";
            _playPause.IsEnabled = controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled;
            _previous.IsEnabled = controls.IsPreviousEnabled;
            _next.IsEnabled = controls.IsNextEnabled;

            var timeline = session.GetTimelineProperties();
            var length = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            if (playing && timeline.LastUpdatedTime.Year > 2000)
            {
                position += DateTimeOffset.UtcNow - timeline.LastUpdatedTime;   // sistem saat bilgisini yalnızca değişimde günceller
            }

            ShowProgress(position, length);
        }
        catch (Exception)
        {
            // Medya hizmeti o an yanıt vermedi (veya oturum kapandı); sonraki turda yeniden denenir.
            ShowIdle();
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<BitmapImage?> LoadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        try
        {
            if (props.Thumbnail is null)
            {
                return null;
            }

            using var stream = await props.Thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024)
            {
                return null;
            }

            using var copy = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(copy);
            copy.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 144;   // 72 px kart, yüksek DPI için iki katı
            image.StreamSource = copy;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // kapak yok / çözülemedi: simge kalır
        }
    }

    /// <summary>"Spotify.exe" → "Spotify"; Mağaza uygulamalarında "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "Microsoft.ZuneMusic".</summary>
    internal static string AppName(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return string.Empty;
        }

        var name = appUserModelId;
        var bang = name.IndexOf('!');
        if (bang > 0)
        {
            name = name[..bang];
        }

        var underscore = name.IndexOf('_');
        if (underscore > 0)
        {
            name = name[..underscore];
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Süreyi "3:07" veya (bir saati aşarsa) "1:03:07" yazar; negatif/geçersiz değer 0:00 olur.</summary>
    internal static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero || time > TimeSpan.FromDays(2))
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }

    /// <summary>Ekran görüntüsü üretimi için: örnek bir parçayı çizer.</summary>
    internal void ShowDemo(string title, string artist, string source, TimeSpan position, TimeSpan length)
    {
        _title.Text = title;
        _artist.Text = artist;
        _source.Text = source;
        _playPause.Content = "";
        _previous.IsEnabled = _playPause.IsEnabled = _next.IsEnabled = true;
        ShowProgress(position, length);
    }

    private void ShowProgress(TimeSpan position, TimeSpan length)
    {
        if (length <= TimeSpan.Zero || length > TimeSpan.FromDays(2))
        {
            _done.Width = new GridLength(0, GridUnitType.Star);
            _rest.Width = new GridLength(1, GridUnitType.Star);
            _position.Text = FormatTime(position);
            _length.Text = string.Empty;   // canlı yayın: toplam süre yok
            return;
        }

        var fraction = Math.Clamp(position.TotalSeconds / length.TotalSeconds, 0, 1);
        _done.Width = new GridLength(fraction, GridUnitType.Star);
        _rest.Width = new GridLength(1 - fraction, GridUnitType.Star);
        _position.Text = FormatTime(position);
        _length.Text = FormatTime(length);
    }

    private void ShowIdle()
    {
        _title.Text = Loc.T("W.MediaIdle");
        _artist.Text = Loc.T("W.MediaIdleHint");
        _source.Text = string.Empty;
        _shownKey = string.Empty;
        _coverImage.Source = null;
        _coverGlyph.Visibility = Visibility.Visible;
        _playPause.Content = "";
        _previous.IsEnabled = _playPause.IsEnabled = _next.IsEnabled = false;
        ShowProgress(TimeSpan.Zero, TimeSpan.Zero);
        _position.Text = string.Empty;
    }
}
