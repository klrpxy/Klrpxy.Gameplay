[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Remove-TemporaryDirectory([string]$Path)
{
    for ($attempt = 0; $attempt -lt 20; $attempt++)
    {
        try
        {
            [System.IO.Directory]::Delete($Path, $true)
            return
        }
        catch [System.IO.IOException]
        {
            Start-Sleep -Milliseconds 250
        }
    }

    [System.IO.Directory]::Delete($Path, $true)
}

function Wait-UnityProcess([System.Diagnostics.Process]$Process)
{
    if (-not $Process.WaitForExit(300000))
    {
        $Process.Kill()
        $Process.WaitForExit()
        throw 'UNITY_TIMEOUT_FAILURE Unity did not exit within five minutes.'
    }
}

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
    $buildDependenciesRoot = Join-Path $stagingRoot 'Assets\BuildDependencies'
    $editorRoot = Join-Path $stagingRoot 'Assets\Editor'
    New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $buildDependenciesRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $editorRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'ProjectSettings') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\bin\Release\netstandard2.0\KlrpxyGameplayStats.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\unity-package\KlrpxyGameplayStats.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats.Runtime\bin\Release\netstandard2.0\KlrpxyGameplayStats.Runtime.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.Runtime.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\unity-package\KlrpxyGameplayStats.Runtime.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.Runtime.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\unity-package\StatsDiagnosticsUnityAdapter.cs') -Destination (Join-Path $assetsRoot 'StatsDiagnosticsUnityAdapter.cs')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats\unity-package\StatsDiagnosticsUnityAdapter.cs.meta') -Destination (Join-Path $assetsRoot 'StatsDiagnosticsUnityAdapter.cs.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags.Runtime\bin\Release\netstandard2.0\KlrpxyGameplayTags.Runtime.dll') -Destination (Join-Path $buildDependenciesRoot 'KlrpxyGameplayTags.Runtime.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\unity-package\KlrpxyGameplayTags.Runtime.dll.meta') -Destination (Join-Path $buildDependenciesRoot 'KlrpxyGameplayTags.Runtime.dll.meta')
    @'
using System;
using UnityEditor;

public static class KlrpxyPackageExporter
{
    public static void Export()
    {
        AssetDatabase.ExportPackage(
            "Assets/KlrpxyGameplayStats",
            Environment.GetEnvironmentVariable("KLRPXY_UNITY_PACKAGE_OUTPUT"),
            ExportPackageOptions.Recurse);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $editorRoot 'KlrpxyPackageExporter.cs') -Encoding utf8
    @'
m_EditorVersion: 2022.3.62f3
m_EditorVersionWithRevision: 2022.3.62f3
'@ | Set-Content -LiteralPath (Join-Path $stagingRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

    $stagedPackage = Join-Path $stagingRoot 'Klrpxy.Gameplay.Stats.unitypackage'
    $env:KLRPXY_UNITY_PACKAGE_OUTPUT = $stagedPackage
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $stagingRoot -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-executeMethod',
        'KlrpxyPackageExporter.Export')
    Wait-UnityProcess $process
    if (-not (Test-Path -LiteralPath $stagedPackage))
    {
        throw 'UNITY_SCRIPT_EXIT_FAILURE Unity failed to export the Stats package.'
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    Copy-Item -LiteralPath $stagedPackage -Destination $OutputPath -Force
}
finally
{
    Remove-Item Env:KLRPXY_UNITY_PACKAGE_OUTPUT -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $stagingRoot)
    {
        Remove-TemporaryDirectory $stagingRoot
    }
}

& (Join-Path $PSScriptRoot 'Verify-StatsUnityPackage.ps1') -PackagePath $OutputPath
Write-Output "KLRPXY_STATS_PACKAGE_BUILD_PASS path=$OutputPath"
