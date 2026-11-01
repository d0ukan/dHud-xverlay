using System.Runtime.ExceptionServices;
using DHud.Helpers;
using Xunit;

// AppPaths ve Loc gibi süreç geneli durumlar paylaşıldığı için testler sırayla çalışır.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DHud.Tests;

/// <summary>Test kategorileri (<c>[Trait("Category", ...)]</c>). Varsayılan çalıştırma <c>Stress</c> dışındakileri çalıştırır.</summary>
public static class Cat
{
    public const string Name = "Category";
    public const string Fast = "Fast";
    public const string Integration = "Integration";
    public const string Fuzz = "Fuzz";
    public const string Stress = "Stress";
}

/// <summary>WPF tipleri (DispatcherTimer, Window ...) tek iş parçacıklı bir daire (STA) ister.</summary>
public static class Sta
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}

/// <summary>Her test için geçici bir veri klasörü; <see cref="AppPaths.DataDir"/> geçici olarak oraya yönlenir ve sonda eski haline döner.</summary>
public sealed class TempDataDir : IDisposable
{
    private readonly string _previous = AppPaths.DataDir;

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dhud-test-" + Guid.NewGuid().ToString("N"));

    public TempDataDir()
    {
        Directory.CreateDirectory(Path);
        AppPaths.DataDir = Path;
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        AppPaths.DataDir = _previous;
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Dosya hâlâ kullanımda olabilir; geçici klasör sistem temizliğine kalır.
        }
    }
}

/// <summary>Deponun kökünü (dHud.csproj'un olduğu klasör) bulur; kaynak kodu tarayan testler için.</summary>
public static class Repo
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "dHud.csproj")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Depo kökü (dHud.csproj) bulunamadı.");
    }

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);
}
