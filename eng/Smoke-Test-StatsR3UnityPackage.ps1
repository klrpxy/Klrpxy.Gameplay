[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,

    [string]$StatsR3PackagePath,

    [string]$StatsPackagePath,

    [string]$TagsPackagePath = 'artifacts/Klrpxy.Gameplay.Tags.0.2.0.unitypackage',

    [switch]$KeepHost
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

function Wait-UnityProcess([System.Diagnostics.Process]$Process, [string]$LogPath)
{
    if (-not $Process.WaitForExit(300000))
    {
        $Process.Kill()
        $Process.WaitForExit()
        throw "UNITY_TIMEOUT_FAILURE Unity did not exit within five minutes. log=$LogPath"
    }
}

function Import-UnityPackage([string]$Path, [string]$PackageName, [string]$LogPath)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-importPackage',
        $PackageName, '-logFile', $LogPath)
    Wait-UnityProcess $process $LogPath
    if ($process.ExitCode -ne 0)
    {
        throw "UNITY_IMPORT_FAILURE package=$PackageName exit=$($process.ExitCode) log=$LogPath"
    }
}

function Invoke-UnityHost([string]$Path, [string]$LogPath)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-logFile', $LogPath)
    Wait-UnityProcess $process $LogPath
    if ($process.ExitCode -ne 0)
    {
        throw "UNITY_SCRIPT_EXIT_FAILURE exit=$($process.ExitCode) log=$LogPath"
    }

    return Get-Content -Raw -LiteralPath $LogPath
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $StatsR3PackagePath)
{
    $StatsR3PackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.R3.unitypackage'
}
if (-not $StatsPackagePath)
{
    $StatsPackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "UNITY_ENVIRONMENT_FAILURE Unity executable was not found: $UnityPath"
}

foreach ($packagePath in @($TagsPackagePath, $StatsPackagePath, $StatsR3PackagePath))
{
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf))
    {
        throw "UNITY_ENVIRONMENT_FAILURE Required package was not found: $packagePath"
    }
}

& (Join-Path $PSScriptRoot 'Verify-StatsUnityPackage.ps1') -PackagePath $StatsPackagePath
& (Join-Path $PSScriptRoot 'Verify-StatsR3UnityPackage.ps1') -PackagePath $StatsR3PackagePath

$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
$r3Dependencies = @(
    'r3\1.3.1\lib\netstandard2.1\R3.dll',
    'microsoft.bcl.asyncinterfaces\6.0.0\lib\netstandard2.1\Microsoft.Bcl.AsyncInterfaces.dll',
    'microsoft.bcl.timeprovider\8.0.0\lib\netstandard2.0\Microsoft.Bcl.TimeProvider.dll',
    'system.runtime.compilerservices.unsafe\6.0.0\lib\netstandard2.0\System.Runtime.CompilerServices.Unsafe.dll',
    'system.threading.channels\8.0.0\lib\netstandard2.1\System.Threading.Channels.dll'
)
foreach ($dependency in $r3Dependencies)
{
    if (-not (Test-Path -LiteralPath (Join-Path $nugetRoot $dependency) -PathType Leaf))
    {
        throw "UNITY_ENVIRONMENT_FAILURE R3 NuGet dependency was not restored: $dependency"
    }
}

$hostRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayStatsR3Smoke-" + $UnityVersion + "-" + [Guid]::NewGuid().ToString('N'))
try
{
    $assetsRoot = Join-Path $hostRoot 'Assets'
    $r3Root = Join-Path $assetsRoot 'NuGet\R3'
    New-Item -ItemType Directory -Path $r3Root -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $hostRoot 'ProjectSettings') -Force | Out-Null
    foreach ($dependency in $r3Dependencies)
    {
        Copy-Item -LiteralPath (Join-Path $nugetRoot $dependency) -Destination $r3Root
    }
    Copy-Item -LiteralPath $TagsPackagePath -Destination (Join-Path $hostRoot 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage')
    Copy-Item -LiteralPath $StatsPackagePath -Destination (Join-Path $hostRoot 'Klrpxy.Gameplay.Stats.unitypackage')
    Copy-Item -LiteralPath $StatsR3PackagePath -Destination (Join-Path $hostRoot 'Klrpxy.Gameplay.Stats.R3.unitypackage')
    @"
m_EditorVersion: $UnityVersion
m_EditorVersionWithRevision: $UnityVersion
"@ | Set-Content -LiteralPath (Join-Path $hostRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

    $tagsImportLog = Join-Path $hostRoot 'TagsImport.log'
    $statsImportLog = Join-Path $hostRoot 'StatsImport.log'
    $statsR3ImportLog = Join-Path $hostRoot 'StatsR3Import.log'
    Import-UnityPackage $hostRoot 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage' $tagsImportLog
    Import-UnityPackage $hostRoot 'Klrpxy.Gameplay.Stats.unitypackage' $statsImportLog
    Import-UnityPackage $hostRoot 'Klrpxy.Gameplay.Stats.R3.unitypackage' $statsR3ImportLog

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'samples\Stats\BazaarGameplay.cs') -Destination (Join-Path $assetsRoot 'BazaarGameplay.cs')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'samples\Stats\BazaarGameplay.R3.cs') -Destination (Join-Path $assetsRoot 'BazaarGameplay.R3.cs')
    @'
using UnityEditor;
using UnityEngine;

namespace Consumer
{
    [InitializeOnLoad]
    public static class StatsR3SmokeContract
    {
        static StatsR3SmokeContract()
        {
            bool corePassed = ConsumerContract.VerifyAll();
            bool r3Passed = BazaarR3ConsumerContract.VerifyR3ConditionsAndObservation();
            if (corePassed && r3Passed)
            {
                Debug.Log("KLRPXY_STATS_R3_UNITY_VALID_PASS");
            }
            else
            {
                Debug.LogError("KLRPXY_STATS_R3_UNITY_VALID_FAIL core=" + corePassed + " r3=" + r3Passed);
            }
        }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $assetsRoot 'StatsR3Consumer.cs') -Encoding utf8

    $editorLog = Join-Path $hostRoot 'Editor.log'
    $editorOutput = Invoke-UnityHost $hostRoot $editorLog
    if ($editorOutput -match '(?m)error CS\d+')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The Stats R3 consumer did not compile. log=$editorLog"
    }

    $passCount = [regex]::Matches($editorOutput, 'KLRPXY_STATS_R3_UNITY_VALID_PASS').Count
    if ($passCount -ne 1)
    {
        throw "UNITY_FUNCTIONAL_FAILURE Expected exactly one Stats R3 runtime pass marker but found $passCount. log=$editorLog"
    }

    $allOutput = (Get-Content -Raw -LiteralPath $tagsImportLog),
        (Get-Content -Raw -LiteralPath $statsImportLog),
        (Get-Content -Raw -LiteralPath $statsR3ImportLog),
        $editorOutput -join [Environment]::NewLine
    if ($allOutput -match '(?i)(Microsoft\.CodeAnalysis.*(conflict|duplicate)|AD0001|analyzer.+(exception|crash)|KlrpxyGameplayTags\.Runtime.+already loaded)')
    {
        throw "UNITY_FUNCTIONAL_FAILURE Unity reported a Roslyn conflict, analyzer failure, or duplicate dependency. log=$editorLog"
    }

    Write-Output "KLRPXY_STATS_R3_UNITY_SMOKE_PASS unity=$UnityVersion temp=$hostRoot"
}
finally
{
    if ((-not $KeepHost) -and (Test-Path -LiteralPath $hostRoot))
    {
        Remove-TemporaryDirectory $hostRoot
    }
}
