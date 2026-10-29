using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using CpuGpuOverlay.Helpers;
using Microsoft.Win32;

namespace CpuGpuOverlay.Services;

/// <summary>
/// "Windows başlangıcında çalıştır" ayarını yönetir. İki yöntem desteklenir:
/// <list type="bullet">
/// <item><b>Görev Zamanlayıcı</b> (önerilen): Oturum açılışında "en yüksek ayrıcalıklarla" çalışan bir görev.
/// UAC sormadan yönetici olarak başlar, böylece CPU sıcaklığı okunabilir.</item>
/// <item><b>Registry</b> (HKCU\...\Run): Windows yükseltme isteyen uygulamaları buradan otomatik yükseltmez;
/// uygulama her açılışta UAC onayı ister.</item>
/// </list>
/// </summary>
public static class StartupManager
{
    private const string TaskName = "CpuGpuOverlay";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "CpuGpuOverlay";

    static StartupManager()
    {
        // schtasks çıktısı OEM kod sayfasında (Türkçe Windows'ta 857) gelir.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string ExePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException(Loc.T("St.NoPath"));

    /// <summary>Şu an etkin olan başlangıç yöntemi; hiçbiri yoksa null.</summary>
    public static StartupMethod? GetActiveMethod()
    {
        if (TaskExists())
        {
            return StartupMethod.TaskScheduler;
        }

        if (RegistryEntryExists())
        {
            return StartupMethod.Registry;
        }

        return null;
    }

    public static void Enable(StartupMethod method)
    {
        if (method == StartupMethod.TaskScheduler && !AdminHelper.IsElevated())
        {
            throw new InvalidOperationException(
                Loc.T("St.NeedAdmin"));
        }

        // İki yöntem aynı anda etkin olursa uygulama iki kez başlatılmaya çalışılır.
        Disable();

        if (method == StartupMethod.TaskScheduler)
        {
            CreateTask();
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(RunValueName, $"\"{ExePath}\"", RegistryValueKind.String);
        }
    }

    public static void Disable()
    {
        if (TaskExists())
        {
            var (exitCode, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    Loc.T("St.DeleteFailed") + output);
            }
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(RunValueName) is not null)
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"").ExitCode == 0;

    private static bool RegistryEntryExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    private static void CreateTask()
    {
        string userId;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            userId = identity.Name;
        }

        var exe = SecurityElement.Escape(ExePath);
        var workDir = SecurityElement.Escape(Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory);
        var user = SecurityElement.Escape(userId);

        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>dHud donanım overlay'ini oturum açılışında yönetici olarak başlatır.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <WorkingDirectory>{workDir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"CpuGpuOverlay_{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks XML'i UTF-16 bekler.
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            var (exitCode, output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            if (exitCode != 0)
            {
                throw new InvalidOperationException(Loc.T("St.CreateFailed") + output);
            }
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* geçici dosya */ }
        }
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        var oemEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oemEncoding,
            StandardErrorEncoding = oemEncoding
        };

        try
        {
            using var process = Process.Start(psi)!;
            var stdErrTask = process.StandardError.ReadToEndAsync();
            var stdOut = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, (stdOut + stdErrTask.Result).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
