<#
.SYNOPSIS
  dHud testlerini çalıştırır.

.EXAMPLE
  .\tests\run-tests.ps1            # hızlı küme (saniyeler / ~1 dakika)
  .\tests\run-tests.ps1 -Full      # hızlı + entegrasyon + fuzz
  .\tests\run-tests.ps1 -Stress    # sızıntı ve uzun süreli testler (dakikalar)
  .\tests\run-tests.ps1 -Filter "FullyQualifiedName~FanLogic"
#>
param(
    [switch]$Full,
    [switch]$Stress,
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "dHud.Tests\dHud.Tests.csproj"

$categories = if ($Stress) { "Category=Stress" }
              elseif ($Full) { "Category!=Stress" }
              else { "Category=Fast" }
$filter = if ($Filter) { "($categories)&($Filter)" } else { $categories }

Write-Host "dHud testleri: $filter" -ForegroundColor Cyan
dotnet test $project --filter $filter --logger "console;verbosity=normal" --nologo
exit $LASTEXITCODE
