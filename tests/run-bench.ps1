<#
.SYNOPSIS
  dHud performans ölçümü: yayın exe'sini gizli --bench modunda çalıştırır, süreci DIŞARIDAN da örnekler ve rapor yazar.

.PARAMETER Exe      Ölçülecek exe (varsayılan: bin\Release\publish\dHud.exe)
.PARAMETER Scale    Süre çarpanı: 1 = ~7 dk, 0.2 = hızlı deneme
.PARAMETER SoakMinutes  >0 ise sonda bu kadar dakikalık uzun süreli (soak) senaryo eklenir (büyüme eğimi ölçülür)
.PARAMETER Real     Gerçek donanım okumasıyla çalıştır (yönetici + sürücü gerekir); varsayılan sentetik
.PARAMETER OutDir   Rapor klasörü (varsayılan: tests\reports\bench-<tarih>)

Ölçüm gerçek pencereleri ekrana açar (widget'lar görünür); bu sırada bilgisayarı yoğun kullanma. Kullanıcının ayarlarına dokunmaz
(geçici veri klasörü, rastgele port, fan kontrolü kapalı).
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\Release\publish\dHud.exe'),
    [double]$Scale = 1,
    [int]$SoakMinutes = 0,
    [switch]$Real,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot ("reports\bench-" + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$benchArgs = @('--bench')
if ($Real) { $benchArgs += 'real' }
$benchArgs += @('--bench-scale', $Scale.ToString([Globalization.CultureInfo]::InvariantCulture), '--bench-out', "`"$OutDir`"")
if ($SoakMinutes -gt 0) { $benchArgs += @('--soak', $SoakMinutes) }

Write-Output "Exe     : $Exe ($([math]::Round((Get-Item $Exe).Length/1MB,1)) MB)"
Write-Output "Ölçek   : $Scale  Soak: $SoakMinutes dk  Donanım: $(if ($Real) {'gerçek'} else {'sentetik'})"
Write-Output "Rapor   : $OutDir"

$process = Start-Process -FilePath $Exe -ArgumentList $benchArgs -PassThru
$rows = New-Object System.Collections.Generic.List[object]
$started = Get-Date
$lastCpu = 0.0
$lastTime = $started
$timeoutMinutes = [math]::Ceiling(12 * $Scale) + $SoakMinutes + 10

while (-not $process.HasExited) {
    Start-Sleep -Seconds 1
    try {
        $process.Refresh()
        $now = Get-Date
        $cpu = $process.TotalProcessorTime.TotalSeconds
        $pct = if (($now - $lastTime).TotalSeconds -gt 0) { [math]::Round((($cpu - $lastCpu) / ($now - $lastTime).TotalSeconds) * 100, 2) } else { 0 }
        $lastCpu = $cpu; $lastTime = $now
        $rows.Add([pscustomobject]@{
            Seconds    = [math]::Round(($now - $started).TotalSeconds, 0)
            WorkingSet = [math]::Round($process.WorkingSet64 / 1MB, 1)
            Private    = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
            Handles    = $process.HandleCount
            Threads    = $process.Threads.Count
            CpuPercent = $pct
        })
    } catch { }

    if (((Get-Date) - $started).TotalMinutes -gt $timeoutMinutes) {
        Write-Warning "Zaman aşımı ($timeoutMinutes dk): ölçüm süreci (PID $($process.Id)) kapatılıyor."
        Stop-Process -Id $process.Id -Force
        break
    }
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $OutDir 'external-samples.csv')

if ($rows.Count -gt 0) {
    $summary = [pscustomobject]@{
        Samples        = $rows.Count
        MaxWorkingSetMb = ($rows | Measure-Object WorkingSet -Maximum).Maximum
        MaxPrivateMb   = ($rows | Measure-Object Private -Maximum).Maximum
        LastPrivateMb  = $rows[-1].Private
        AvgCpuPercent  = [math]::Round(($rows | Measure-Object CpuPercent -Average).Average, 2)
        MaxHandles     = ($rows | Measure-Object Handles -Maximum).Maximum
        MaxThreads     = ($rows | Measure-Object Threads -Maximum).Maximum
    }
    $summary | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $OutDir 'external-summary.json')
    Write-Output "Dıştan ölçüm: en yüksek WS $($summary.MaxWorkingSetMb) MB, en yüksek Private $($summary.MaxPrivateMb) MB, ortalama CPU %$($summary.AvgCpuPercent) (tek çekirdek), en çok tutamak $($summary.MaxHandles)"
}

$report = Join-Path $OutDir 'bench-report.md'
if (Test-Path (Join-Path $OutDir 'bench-error.txt')) {
    Write-Error "Ölçüm hata verdi: $(Join-Path $OutDir 'bench-error.txt')"
    exit 1
}
if (Test-Path $report) { Write-Output "Rapor hazır: $report" } else { Write-Error 'Rapor üretilmedi.'; exit 1 }
