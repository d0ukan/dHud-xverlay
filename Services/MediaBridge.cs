using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace DHud.Services;

/// <summary>Çalan parçanın telefona gönderilecek özeti. <see cref="CoverJpeg"/> yalnızca istenirse dolar (küçük kapak, ~96 px).</summary>
public sealed record MediaState(string Title, string Artist, string App, double PositionSeconds, double LengthSeconds, bool Playing,
    bool CanPrevious, bool CanNext, bool CanPlayPause, string CoverKey, byte[]? CoverJpeg);

/// <summary>
/// Windows'un sistem medya oturumunu (Spotify, tarayıcı, VLC...) okur ve denetler; telefondaki "Widget'lar" sekmesi için.
/// Pencere widget'ından bağımsızdır: widget kapalıyken de çalışır.
/// </summary>
public sealed class MediaBridge
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string _coverKey = string.Empty;
    private byte[]? _cover;
    private int _coverTries;

    /// <summary>Şu anki parça; çalan bir şey yoksa null. Kapak yalnızca <paramref name="includeCover"/> doğruysa veya değiştiyse eklenir.</summary>
    public async Task<MediaState?> ReadAsync(bool includeCover)
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            if (session is null)
            {
                return null;
            }

            var playback = session.GetPlaybackInfo();
            var props = await session.TryGetMediaPropertiesAsync();
            var key = $"{session.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != _coverKey)
            {
                _coverKey = key;
                _cover = null;
                _coverTries = 0;
            }

            if (_cover is null && _coverTries < 5)
            {
                _coverTries++;
                _cover = await LoadCoverAsync(props);
            }

            var timeline = session.GetTimelineProperties();
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var length = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            if (playing && timeline.LastUpdatedTime.Year > 2000)
            {
                position += DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            }

            var controls = playback.Controls;
            return new MediaState(
                props.Title ?? string.Empty,
                props.Artist ?? string.Empty,
                DHud.Widgets.MediaWidgetWindow.AppName(session.SourceAppUserModelId),
                Math.Max(0, position.TotalSeconds),
                length.TotalSeconds is > 0 and < 172800 ? length.TotalSeconds : 0,
                playing,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled,
                _cover is null ? string.Empty : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_cover))[..12],
                includeCover ? _cover : null);
        }
        catch (Exception)
        {
            _manager = null;   // medya hizmeti yanıt vermedi; bir sonraki turda yeniden istenir
            return null;
        }
    }

    /// <summary>Medya denetimi: <c>toggle</c>, <c>play</c>, <c>pause</c>, <c>next</c>, <c>previous</c>. Dönüş: başarılı mı.</summary>
    public async Task<bool> ControlAsync(string action)
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager.GetCurrentSession() is not { } session)
            {
                return false;
            }

            return action switch
            {
                "toggle" => await session.TryTogglePlayPauseAsync(),
                "play" => await session.TryPlayAsync(),
                "pause" => await session.TryPauseAsync(),
                "next" => await session.TrySkipNextAsync(),
                "previous" => await session.TrySkipPreviousAsync(),
                _ => false
            };
        }
        catch (Exception)
        {
            _manager = null;
            return false;
        }
    }

    private static async Task<byte[]?> LoadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
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
            var frame = BitmapFrame.Create(copy, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return ClipJpeg.Encode(frame, 96);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Telefona giden küçük resimler için kodlama (kapaklar ve pano önizlemeleri JPEG, başlatıcı simgeleri PNG). Arka plan iş parçacığından çağrılabilir.</summary>
public static class ClipJpeg
{
    private static BitmapSource Scaled(BitmapSource source, int maxSide)
    {
        var scale = Math.Min(1.0, maxSide / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        return scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
    }

    /// <summary>En çok <paramref name="maxSide"/> piksel kenarlı JPEG (saydam yerler siyah olur); çözülemezse null.</summary>
    public static byte[]? Encode(BitmapSource source, int maxSide, int quality = 72)
    {
        try
        {
            var opaque = new FormatConvertedBitmap(Scaled(source, maxSide), PixelFormats.Bgr24, null, 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>En çok <paramref name="maxSide"/> piksel kenarlı PNG (saydamlık korunur; simgeler için).</summary>
    public static byte[]? EncodePng(BitmapSource source, int maxSide)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Scaled(source, maxSide)));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
