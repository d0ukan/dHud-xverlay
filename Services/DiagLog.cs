using System.IO;
using DHud.Helpers;

namespace DHud.Services;

/// <summary>
/// Küçük hata günlüğü: son kayıtlar bellekte durur ve (en fazla ~512 KB) %AppData%\dHud\dhud.log dosyasına yazılır.
/// Hiçbir şey ağa gönderilmez; kullanıcı isterse "tanı bilgisini kopyala" ile paylaşır. Asla istisna fırlatmaz.
/// </summary>
public static class DiagLog
{
    private const int MaxRecent = 60;
    private const long MaxFileBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();

    public static void Add(string area, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{area}] {message.ReplaceLineEndings(" ")}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > MaxRecent)
            {
                Recent.Dequeue();
            }

            try
            {
                var path = AppPaths.File("dhud.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    File.Move(path, path + ".old", overwrite: true); // eski günlük bir tur saklanır
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Günlük yazılamıyorsa sessizce bellekte kalır.
            }
        }
    }

    public static void Add(string area, Exception ex) => Add(area, $"{ex.GetType().Name}: {ex.Message}");

    /// <summary>Son kayıtlar (eskiden yeniye).</summary>
    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate)
        {
            return Recent.ToList();
        }
    }
}
