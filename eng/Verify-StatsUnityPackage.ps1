[CmdletBinding()]
param(
    [string]$PackagePath = 'artifacts/Klrpxy.Gameplay.Stats.unitypackage'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf))
{
    throw "UNITY_PACKAGE_SETTINGS_FAILURE Expected Unity package was not found: $PackagePath"
}

if ([System.IO.Path]::GetExtension($PackagePath) -ne '.unitypackage')
{
    throw "UNITY_PACKAGE_SETTINGS_FAILURE Expected a .unitypackage artifact: $PackagePath"
}

if ((Get-Item -LiteralPath $PackagePath).Length -eq 0)
{
    throw "UNITY_PACKAGE_SETTINGS_FAILURE Unity package is empty: $PackagePath"
}

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayStatsVerify-" + [Guid]::NewGuid().ToString('N'))
try
{
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    tar -xzf $PackagePath -C $extractRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "UNITY_PACKAGE_SETTINGS_FAILURE Unity package could not be inspected: $PackagePath"
    }

    $packageAssets = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter pathname | ForEach-Object {
        [PSCustomObject]@{
            Path = (Get-Content -Raw -LiteralPath $_.FullName).Trim()
            Meta = Get-Content -Raw -LiteralPath (Join-Path $_.DirectoryName 'asset.meta')
        }
    })
    $analyzer = @($packageAssets | Where-Object { $_.Path -eq 'Assets/KlrpxyGameplayStats/KlrpxyGameplayStats.dll' })
    $runtime = @($packageAssets | Where-Object { $_.Path -eq 'Assets/KlrpxyGameplayStats/KlrpxyGameplayStats.Runtime.dll' })
    if ($analyzer.Count -ne 1 -or $runtime.Count -ne 1)
    {
        throw 'UNITY_PACKAGE_SETTINGS_FAILURE Stats Unity package must contain exactly one analyzer DLL and one runtime DLL.'
    }

    $expectedPaths = @(
        'Assets/KlrpxyGameplayStats',
        'Assets/KlrpxyGameplayStats/KlrpxyGameplayStats.dll',
        'Assets/KlrpxyGameplayStats/KlrpxyGameplayStats.Runtime.dll'
    )
    $unexpectedPaths = @($packageAssets.Path | Where-Object { $_ -notin $expectedPaths })
    $missingPaths = @($expectedPaths | Where-Object { $_ -notin $packageAssets.Path })
    if ($unexpectedPaths.Count -ne 0 -or $missingPaths.Count -ne 0)
    {
        throw "UNITY_PACKAGE_SETTINGS_FAILURE Stats Unity package has an unexpected asset manifest. Missing: $($missingPaths -join ', '). Unexpected: $($unexpectedPaths -join ', ')"
    }

    $hasAnalyzerImportSettings = ($analyzer[0].Meta -match '(?m)^- RoslynAnalyzer\r?$') -and
        ($analyzer[0].Meta -match '(?s)Any:\s*second:\s*enabled: 0') -and
        ($analyzer[0].Meta -match '(?m)^  isExplicitlyReferenced: 0\r?$')
    if (-not $hasAnalyzerImportSettings)
    {
        throw 'UNITY_PACKAGE_SETTINGS_FAILURE The Stats analyzer must be a RoslynAnalyzer with runtime platforms disabled and implicit references enabled.'
    }

    $hasRuntimeImportSettings = ($runtime[0].Meta -notmatch 'RoslynAnalyzer') -and
        ($runtime[0].Meta -match '(?s)Any:\s*second:\s*enabled: 1') -and
        ($runtime[0].Meta -match '(?m)^  isExplicitlyReferenced: 0\r?$')
    if (-not $hasRuntimeImportSettings)
    {
        throw 'UNITY_PACKAGE_SETTINGS_FAILURE The Stats runtime must have runtime platforms enabled and implicit references enabled.'
    }

    $forbiddenAssets = @($packageAssets | Where-Object {
        $_.Path -match '(?i)(KlrpxyGameplayTags|Microsoft\.CodeAnalysis)'
    })
    if ($forbiddenAssets.Count -ne 0)
    {
        throw "UNITY_PACKAGE_SETTINGS_FAILURE Stats Unity package contains forbidden dependency assets: $($forbiddenAssets.Path -join ', ')"
    }
}
finally
{
    if (Test-Path -LiteralPath $extractRoot)
    {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}

Write-Output "KLRPXY_STATS_PACKAGE_VERIFY_PASS path=$PackagePath"
