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
$adapterProjectPath = Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats.R3\KlrpxyGameplayStats.R3.csproj'
if (-not $OutputPath)
{
    $OutputPath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.R3.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "UNITY_ENVIRONMENT_FAILURE Unity executable was not found: $UnityPath"
}

dotnet build $adapterProjectPath --configuration Release --nologo
if ($LASTEXITCODE -ne 0)
{
    throw 'UNITY_SCRIPT_EXIT_FAILURE The Stats R3 adapter build failed.'
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayStatsR3Package-" + [Guid]::NewGuid().ToString('N'))
try
{
    $assetsRoot = Join-Path $stagingRoot 'Assets\KlrpxyGameplayStatsR3'
    $editorRoot = Join-Path $stagingRoot 'Assets\Editor'
    New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $editorRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'ProjectSettings') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats.R3\bin\Release\netstandard2.0\KlrpxyGameplayStats.R3.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.R3.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Stats.R3\unity-package\KlrpxyGameplayStats.R3.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayStats.R3.dll.meta')
    @'
using System;
using UnityEditor;

public static class KlrpxyStatsR3PackageExporter
{
    public static void Export()
    {
        AssetDatabase.ExportPackage(
            "Assets/KlrpxyGameplayStatsR3",
            Environment.GetEnvironmentVariable("KLRPXY_STATS_R3_UNITY_PACKAGE_OUTPUT"),
            ExportPackageOptions.Recurse);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $editorRoot 'KlrpxyStatsR3PackageExporter.cs') -Encoding utf8
    @'
m_EditorVersion: 2022.3.62f3
m_EditorVersionWithRevision: 2022.3.62f3
'@ | Set-Content -LiteralPath (Join-Path $stagingRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

    $stagedPackage = Join-Path $stagingRoot 'Klrpxy.Gameplay.Stats.R3.unitypackage'
    $env:KLRPXY_STATS_R3_UNITY_PACKAGE_OUTPUT = $stagedPackage
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $stagingRoot -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-executeMethod',
        'KlrpxyStatsR3PackageExporter.Export')
    Wait-UnityProcess $process
    if (-not (Test-Path -LiteralPath $stagedPackage))
    {
        throw 'UNITY_SCRIPT_EXIT_FAILURE Unity failed to export the Stats R3 adapter package.'
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    Copy-Item -LiteralPath $stagedPackage -Destination $OutputPath -Force
}
finally
{
    Remove-Item Env:KLRPXY_STATS_R3_UNITY_PACKAGE_OUTPUT -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $stagingRoot)
    {
        Remove-TemporaryDirectory $stagingRoot
    }
}

& (Join-Path $PSScriptRoot 'Verify-StatsR3UnityPackage.ps1') -PackagePath $OutputPath
Write-Output "KLRPXY_STATS_R3_PACKAGE_BUILD_PASS path=$OutputPath"
