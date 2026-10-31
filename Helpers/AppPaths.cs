using System.IO;

namespace DHud.Helpers;

/// <summary>
/// dHud'un verilerinin (ayarlar, widget verisi, profiller, günlük) tutulduğu klasör. Varsayılan %AppData%\dHud; test ve ekran görüntüsü
/// üretimi için <c>--data-dir &lt;klasör&gt;</c> ile değiştirilebilir (gerçek ayarlara dokunulmaz).
/// </summary>
public static class AppPaths
{
    public static string DefaultDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dHud");

    public static string DataDir { get; set; } = DefaultDir;

    /// <summary>Veri klasörü varsayılandan farklı mı (ör. --data-dir ile).</summary>
    public static bool IsCustom => !string.Equals(DataDir, DefaultDir, StringComparison.OrdinalIgnoreCase);

    public static string File(string name) => Path.Combine(DataDir, name);
}
