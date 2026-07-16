[CmdletBinding()]
param(
    [string]$PackagePath = 'artifacts/Klrpxy.Gameplay.Stats.R3.unitypackage',

    [string]$ReportPath,

    [string[]]$EditorVersions = @()
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf))
{
    throw "UNITY_PACKAGE_SETTINGS_FAILURE Expected Stats R3 Unity package was not found: $PackagePath"
}

if ([System.IO.Path]::GetExtension($PackagePath) -ne '.unitypackage')
{
    throw "UNITY_PACKAGE_SETTINGS_FAILURE Expected a .unitypackage artifact: $PackagePath"
}

$packageSize = (Get-Item -LiteralPath $PackagePath).Length
if ($packageSize -eq 0 -or $packageSize -gt 5MB)
{
    throw "UNITY_PACKAGE_SETTINGS_FAILURE Unity package must be larger than zero and no larger than 5 MB: $PackagePath"
}

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayStatsR3Verify-" + [Guid]::NewGuid().ToString('N'))
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
            Size = if (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'asset') -PathType Leaf)
            {
                (Get-Item -LiteralPath (Join-Path $_.DirectoryName 'asset')).Length
            }
            else
            {
                0
            }
        }
    })
    $runtime = @($packageAssets | Where-Object { $_.Path -eq 'Assets/KlrpxyGameplayStatsR3/KlrpxyGameplayStats.R3.dll' })
    if ($runtime.Count -ne 1)
    {
        throw 'UNITY_PACKAGE_SETTINGS_FAILURE Stats R3 Unity package must contain exactly one adapter runtime DLL.'
    }

    $expectedPaths = @(
        'Assets/KlrpxyGameplayStatsR3',
        'Assets/KlrpxyGameplayStatsR3/KlrpxyGameplayStats.R3.dll'
    )
    $unexpectedPaths = @($packageAssets.Path | Where-Object { $_ -notin $expectedPaths })
    $missingPaths = @($expectedPaths | Where-Object { $_ -notin $packageAssets.Path })
    if ($unexpectedPaths.Count -ne 0 -or $missingPaths.Count -ne 0)
    {
        throw "UNITY_PACKAGE_SETTINGS_FAILURE Stats R3 Unity package has an unexpected asset manifest. Missing: $($missingPaths -join ', '). Unexpected: $($unexpectedPaths -join ', ')"
    }

    $hasRuntimeImportSettings = ($runtime[0].Meta -notmatch 'RoslynAnalyzer') -and
        ($runtime[0].Meta -match '(?s)Any:\s*second:\s*enabled: 1') -and
        ($runtime[0].Meta -match '(?m)^  isExplicitlyReferenced: 0\r?$')
    if (-not $hasRuntimeImportSettings)
    {
        throw 'UNITY_PACKAGE_SETTINGS_FAILURE The Stats R3 adapter must have runtime platforms enabled and implicit references enabled.'
    }

    $forbiddenAssets = @($packageAssets | Where-Object {
        $_.Path -match '(?i)(KlrpxyGameplayTags|KlrpxyGameplayStats\.Runtime|(^|/)R3\.dll$|Microsoft\.CodeAnalysis)'
    })
    if ($forbiddenAssets.Count -ne 0)
    {
        throw "UNITY_PACKAGE_SETTINGS_FAILURE Stats R3 Unity package contains forbidden dependency assets: $($forbiddenAssets.Path -join ', ')"
    }

    if ($runtime[0].Size -eq 0 -or $runtime[0].Size -gt 2MB)
    {
        throw "UNITY_PACKAGE_SETTINGS_FAILURE Stats R3 adapter binary must be larger than zero and no larger than 2 MB: $($runtime[0].Path)"
    }

    if ($ReportPath)
    {
        $report = [PSCustomObject]@{
            Package = [PSCustomObject]@{
                Path = $PackagePath
                Size = $packageSize
            }
            Assets = @($packageAssets | Sort-Object Path | ForEach-Object {
                [PSCustomObject]@{
                    Path = $_.Path
                    Size = $_.Size
                    AnyPlatform = if ($_.Meta -match '(?s)Any:\s*second:\s*enabled: ([01])') { $Matches[1] } else { $null }
                    IsExplicitlyReferenced = if ($_.Meta -match '(?m)^  isExplicitlyReferenced: ([01])\r?$') { $Matches[1] } else { $null }
                    Labels = @([regex]::Matches($_.Meta, '(?m)^- ([^\r\n]+)\r?$') | ForEach-Object { $_.Groups[1].Value })
                }
            })
            EditorVersions = @($EditorVersions)
        }
        $reportDirectory = Split-Path -Parent $ReportPath
        if ($reportDirectory)
        {
            New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
        }

        $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    }
}
finally
{
    if (Test-Path -LiteralPath $extractRoot)
    {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}

Write-Output "KLRPXY_STATS_R3_PACKAGE_VERIFY_PASS path=$PackagePath"
