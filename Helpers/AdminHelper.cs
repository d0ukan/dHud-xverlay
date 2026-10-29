using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace CpuGpuOverlay.Helpers;

public static class AdminHelper
{
    /// <summary>Kendini yükseltilmiş olarak yeniden başlatan örneğe verilen argüman (sonsuz döngüyü önler).</summary>
    public const string ElevatedArgument = "--elevated";

    private const int ErrorCancelled = 1223; // Kullanıcı UAC penceresinde "Hayır" dedi.

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Uygulamayı UAC onayıyla yönetici olarak yeniden başlatır.
    /// Kullanıcı reddederse veya başlatılamazsa false döner.
    /// </summary>
    public static bool TryRelaunchElevated(IEnumerable<string> args)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }

        var arguments = string.Join(" ", args.Append(ElevatedArgument).Select(a => $"\"{a}\""));
        try
        {
            Process.Start(new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Yükseltilmiş başlatma başarısız: {ex}");
            return false;
        }
    }

    /// <summary>
    /// LibreHardwareMonitor 0.9.5+ CPU sensörlerine erişmek için PawnIO sürücüsünü kullanır.
    /// Sürücü kurulu değilse CPU sıcaklığı okunamaz.
    /// </summary>
    public static bool IsPawnIoInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }
}
