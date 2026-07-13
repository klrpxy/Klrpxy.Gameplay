[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,

    [string]$StatsPackagePath,

    [string]$TagsPackagePath = 'artifacts/Klrpxy.Gameplay.Tags.0.2.0.unitypackage',

    [switch]$KeepHost
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $StatsPackagePath)
{
    $StatsPackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "UNITY_ENVIRONMENT_FAILURE Unity executable was not found: $UnityPath"
}

foreach ($packagePath in @($TagsPackagePath, $StatsPackagePath))
{
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf))
    {
        throw "UNITY_ENVIRONMENT_FAILURE Required package was not found: $packagePath"
    }
}

& (Join-Path $PSScriptRoot 'Verify-StatsUnityPackage.ps1') -PackagePath $StatsPackagePath

$hostRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayStatsSmoke-" + $UnityVersion + "-" + [Guid]::NewGuid().ToString('N'))

function Write-HostProject([string]$Path)
{
    New-Item -ItemType Directory -Path (Join-Path $Path 'Assets') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Path 'ProjectSettings') -Force | Out-Null
    Copy-Item -LiteralPath $TagsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage')
    Copy-Item -LiteralPath $StatsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Stats.unitypackage')

    @"
m_EditorVersion: $UnityVersion
m_EditorVersionWithRevision: $UnityVersion
"@ | Set-Content -LiteralPath (Join-Path $Path 'ProjectSettings\ProjectVersion.txt') -Encoding utf8
}

function Import-UnityPackage([string]$Path, [string]$PackageName, [string]$LogPath)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -Wait -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-importPackage',
        $PackageName, '-logFile', $LogPath)
    if ($process.ExitCode -ne 0)
    {
        throw "UNITY_IMPORT_FAILURE package=$PackageName exit=$($process.ExitCode) log=$LogPath"
    }
}

function Invoke-UnityHost([string]$Path, [string]$LogPath)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -Wait -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-logFile', $LogPath)
    if ($process.ExitCode -ne 0)
    {
        throw "UNITY_SCRIPT_EXIT_FAILURE exit=$($process.ExitCode) log=$LogPath"
    }

    return Get-Content -Raw -LiteralPath $LogPath
}

try
{
    Write-HostProject $hostRoot
    Import-UnityPackage $hostRoot 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage' (Join-Path $hostRoot 'TagsImport.log')
    Import-UnityPackage $hostRoot 'Klrpxy.Gameplay.Stats.unitypackage' (Join-Path $hostRoot 'StatsImport.log')

    @'
using Klrpxy.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace Consumer
{
    public sealed partial class SmokeStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    [InitializeOnLoad]
    public static class SmokeContract
    {
        static SmokeContract()
        {
            var statSet = new SmokeStatSet();
            Stat health;
            if (SmokeStatSet.HealthKey.TryGet(statSet, out health)
                && object.ReferenceEquals(health, statSet.Health)
                && health.FinalValue == 100f)
            {
                Debug.Log("KLRPXY_STATS_UNITY_VALID_PASS");
            }
            else
            {
                Debug.LogError("KLRPXY_STATS_UNITY_VALID_FAIL");
            }
        }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $hostRoot 'Assets\Consumer.cs') -Encoding utf8

    $editorLog = Join-Path $hostRoot 'Editor.log'
    $editorOutput = Invoke-UnityHost $hostRoot $editorLog
    if ($editorOutput -match '(?m)error CS\d+')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The consumer did not compile. log=$editorLog"
    }

    $passCount = [regex]::Matches($editorOutput, 'KLRPXY_STATS_UNITY_VALID_PASS').Count
    if ($passCount -ne 1)
    {
        throw "UNITY_FUNCTIONAL_FAILURE Expected exactly one runtime pass marker but found $passCount. log=$editorLog"
    }

    Write-Output "KLRPXY_STATS_UNITY_SMOKE_PASS unity=$UnityVersion temp=$hostRoot"
}
finally
{
    if ((-not $KeepHost) -and (Test-Path -LiteralPath $hostRoot))
    {
        Remove-Item -LiteralPath $hostRoot -Recurse -Force
    }
}
