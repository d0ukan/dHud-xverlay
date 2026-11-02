using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;

namespace DHud.Services;

/// <summary>
/// Telefondaki Widget'lar sekmesinden gelen komutlar: medya denetimi, başlatıcıdaki bir uygulamayı açma, telefondaki metni PC panosuna gönderme.
/// ANA İŞ PARÇACIĞINDA çalışır. "Telefon widget'ları kullanabilir" ayarı kapalıysa hiçbiri çalışmaz.
/// Güvenlik: açılabilecek tek şey kullanıcının kendi eklediği başlatıcı öğeleridir (telefon yol veremez, yalnızca öğe kimliği seçer).
/// Komutlar: <c>media</c>{action: toggle/play/pause/next/previous}, <c>launch</c>{id}, <c>clipSend</c>{text}.
/// </summary>
public sealed class RemoteWidgetService(WidgetService widgets, SettingsService settings, MediaBridge media)
{
    public const int MaxClipboardChars = 1000;

    private static readonly string[] MediaActions = ["toggle", "play", "pause", "next", "previous"];

    public static bool IsWidgetCommand(string? cmd) => cmd is "media" or "launch" or "clipSend";

    /// <summary>Dönüş: null = başarılı; aksi halde hata kodu (<c>disabled</c>, <c>bad</c>, <c>nofound</c>, <c>missing</c>, <c>nomedia</c>, <c>busy</c>).</summary>
    public async Task<string?> ApplyAsync(JsonElement command)
    {
        if (!settings.Current.MobileWidgets)
        {
            return "disabled";
        }

        switch (Str(command, "cmd"))
        {
            case "media":
            {
                var action = Str(command, "action");
                if (!MediaActions.Contains(action))
                {
                    return "bad";
                }

                return await media.ControlAsync(action) ? null : "nomedia";
            }

            case "launch":
            {
                var id = Str(command, "id");
                var item = widgets.Data.Launchers.FirstOrDefault(l => l.Id == id);
                if (item is null)
                {
                    return "nofound";
                }

                if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
                {
                    return "missing";
                }

                try
                {
                    // Gezgin üzerinden açılır: dHud yükseltilmiş çalışsa da uygulama yönetici yetkisi miras almaz (başlatıcı widget'ıyla aynı).
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\"") { UseShellExecute = false });
                    return null;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return "missing";
                }
            }

            case "clipSend":
            {
                var text = Str(command, "text").Trim();
                if (text.Length == 0)
                {
                    return "bad";
                }

                try
                {
                    Clipboard.SetText(text.Length > MaxClipboardChars ? text[..MaxClipboardChars] : text);
                    return null;
                }
                catch (ExternalException)
                {
                    return "busy";   // pano o anda başka bir uygulamada meşgul
                }
            }

            default:
                return "bad";
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}
