using System.Windows.Media;

namespace DHud.Widgets;

/// <summary>Widget kartlarının renk teması: kart zemini, vurgu rengi ve açık/koyu.</summary>
public sealed record WidgetTheme(string Id, string NameKey, Color Card, Color Accent, bool Light);

/// <summary>Hazır widget temaları. Tüm widget'lara birlikte uygulanır (yapışkan notlar kendi renklerini korur).</summary>
public static class WidgetThemes
{
    public const string Default = "graphite";

    public static readonly IReadOnlyList<WidgetTheme> All =
    [
        new(Default, "Theme.graphite", Color.FromArgb(0xF2, 0x1A, 0x1B, 0x22), Color.FromRgb(0x3B, 0x82, 0xF6), false),
        new("midnight", "Theme.midnight", Color.FromArgb(0xF2, 0x14, 0x1B, 0x30), Color.FromRgb(0x60, 0xA5, 0xFA), false),
        new("forest", "Theme.forest", Color.FromArgb(0xF2, 0x14, 0x20, 0x19), Color.FromRgb(0x4A, 0xDE, 0x80), false),
        new("wine", "Theme.wine", Color.FromArgb(0xF2, 0x26, 0x15, 0x1A), Color.FromRgb(0xF4, 0x72, 0xB6), false),
        new("violet", "Theme.violet", Color.FromArgb(0xF2, 0x1E, 0x17, 0x30), Color.FromRgb(0xA7, 0x8B, 0xFA), false),
        new("amber", "Theme.amber", Color.FromArgb(0xF2, 0x25, 0x1E, 0x12), Color.FromRgb(0xFA, 0xCC, 0x15), false),
        new("teal", "Theme.teal", Color.FromArgb(0xF2, 0x10, 0x25, 0x2A), Color.FromRgb(0x2D, 0xD4, 0xBF), false),
        new("light", "Theme.light", Color.FromArgb(0xF2, 0xF3, 0xF4, 0xF8), Color.FromRgb(0x25, 0x63, 0xEB), true)
    ];

    /// <summary>Kimliğe göre tema; bilinmiyorsa varsayılan (eski/bozuk ayar dosyaları için).</summary>
    public static WidgetTheme Get(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
