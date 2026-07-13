[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$generatorProjectPath = Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\KlrpxyGameplayStats.csproj'
$runtimeProjectPath = Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats.Runtime\KlrpxyGameplayStats.Runtime.csproj'
if (-not $OutputPath)
{
    $OutputPath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "UNITY_ENVIRONMENT_FAILURE Unity executable was not found: $UnityPath"
}

dotnet build $generatorProjectPath --configuration Release --nologo
if ($LASTEXITCODE -ne 0)
{
    throw 'UNITY_SCRIPT_EXIT_FAILURE The Stats analyzer build failed.'
}

dotnet build $runtimeProjectPath --configuration Release --nologo
if ($LASTEXITCODE -ne 0)
{
    throw 'UNITY_SCRIPT_EXIT_FAILURE The Stats runtime build failed.'
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayStatsPackage-" + [Guid]::NewGuid().ToString('N'))
try
{
    $assetsRoot = Join-Path $stagingRoot 'Assets\KlrpxyGameplayStats'
    New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'ProjectSettings') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\bin\Release\netstandard2.0\KlrpxyGameplayStats.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\unity-package\KlrpxyGameplayStats.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats.Runtime\bin\Release\netstandard2.0\KlrpxyGameplayStats.Runtime.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.Runtime.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\unity-package\KlrpxyGameplayStats.Runtime.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.Runtime.dll.meta')
    @'
m_EditorVersion: 2022.3.62f3
m_EditorVersionWithRevision: 2022.3.62f3
'@ | Set-Content -LiteralPath (Join-Path $stagingRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

    $stagedPackage = Join-Path $stagingRoot 'Klrpxy.Gameplay.Stats.unitypackage'
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $stagingRoot -Wait -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-exportPackage',
        'Assets/KlrpxyGameplayStats', 'Klrpxy.Gameplay.Stats.unitypackage')
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $stagedPackage))
    {
        throw 'UNITY_SCRIPT_EXIT_FAILURE Unity failed to export the Stats package.'
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    Copy-Item -LiteralPath $stagedPackage -Destination $OutputPath -Force
}
finally
{
    if (Test-Path -LiteralPath $stagingRoot)
    {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

& (Join-Path $PSScriptRoot 'Verify-StatsUnityPackage.ps1') -PackagePath $OutputPath
Write-Output "KLRPXY_STATS_PACKAGE_BUILD_PASS path=$OutputPath"
