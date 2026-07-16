[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,

    [string]$StatsPackagePath,

    [string]$TagsPackagePath = 'artifacts/Klrpxy.Gameplay.Tags.0.2.1.unitypackage',

    [string]$LegacyTagsPackagePath = 'artifacts/Klrpxy.Gameplay.Tags.0.2.0.unitypackage',

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
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $StatsPackagePath)
{
    $StatsPackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "UNITY_ENVIRONMENT_FAILURE Unity executable was not found: $UnityPath"
}

foreach ($packagePath in @($TagsPackagePath, $LegacyTagsPackagePath, $StatsPackagePath))
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
    Copy-Item -LiteralPath $TagsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Tags.0.2.1.unitypackage')
    Copy-Item -LiteralPath $StatsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Stats.unitypackage')

    @"
m_EditorVersion: $UnityVersion
m_EditorVersionWithRevision: $UnityVersion
"@ | Set-Content -LiteralPath (Join-Path $Path 'ProjectSettings\ProjectVersion.txt') -Encoding utf8
}

function Write-NegativeHostProject([string]$Path)
{
    New-Item -ItemType Directory -Path (Join-Path $Path 'Assets') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Path 'ProjectSettings') -Force | Out-Null
    Copy-Item -LiteralPath $StatsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Stats.unitypackage')

    @"
m_EditorVersion: $UnityVersion
m_EditorVersionWithRevision: $UnityVersion
"@ | Set-Content -LiteralPath (Join-Path $Path 'ProjectSettings\ProjectVersion.txt') -Encoding utf8
}

function Write-LegacyTagsHostProject([string]$Path)
{
    New-Item -ItemType Directory -Path (Join-Path $Path 'Assets') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Path 'ProjectSettings') -Force | Out-Null
    Copy-Item -LiteralPath $LegacyTagsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage')
    Copy-Item -LiteralPath $StatsPackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Stats.unitypackage')

    @"
m_EditorVersion: $UnityVersion
m_EditorVersionWithRevision: $UnityVersion
"@ | Set-Content -LiteralPath (Join-Path $Path 'ProjectSettings\ProjectVersion.txt') -Encoding utf8
}

function Import-UnityPackage([string]$Path, [string]$PackageName, [string]$LogPath, [switch]$AllowCompilationErrors)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-importPackage',
        $PackageName, '-logFile', $LogPath)
    Wait-UnityProcess $process $LogPath
    if ($process.ExitCode -ne 0 -and -not $AllowCompilationErrors)
    {
        throw "UNITY_IMPORT_FAILURE package=$PackageName exit=$($process.ExitCode) log=$LogPath"
    }
}

function Invoke-UnityHost([string]$Path, [string]$LogPath, [switch]$AllowCompilationErrors)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-logFile', $LogPath)
    Wait-UnityProcess $process $LogPath
    if ($process.ExitCode -ne 0 -and -not $AllowCompilationErrors)
    {
        throw "UNITY_SCRIPT_EXIT_FAILURE exit=$($process.ExitCode) log=$LogPath"
    }

    return Get-Content -Raw -LiteralPath $LogPath
}

try
{
    $validHost = Join-Path $hostRoot 'valid'
    Write-HostProject $validHost
    $tagsImportLog = Join-Path $validHost 'TagsImport.log'
    $statsImportLog = Join-Path $validHost 'StatsImport.log'
    Import-UnityPackage $validHost 'Klrpxy.Gameplay.Tags.0.2.1.unitypackage' $tagsImportLog
    Import-UnityPackage $validHost 'Klrpxy.Gameplay.Stats.unitypackage' $statsImportLog

    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'samples\Stats\BazaarGameplay.cs') `
        -Destination (Join-Path $validHost 'Assets\BazaarGameplay.cs')

    @'
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Stats.Unity;
using System;
using UnityEditor;
using UnityEngine;

namespace Consumer
{
    [InitializeOnLoad]
    public static class SmokeContract
    {
        static SmokeContract()
        {
            StatsDiagnosticsUnityAdapter.Install();
            var diagnosticsHero = new Hero();
            diagnosticsHero.StatSet.Power.OnFinalValueChanged += (previous, current) =>
                throw new InvalidOperationException("KLRPXY_STATS_DIAGNOSTICS_EXCEPTION");
            diagnosticsHero.StatSet.Power.BaseValue = 20f;

            if (ConsumerContract.VerifyAll())
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
'@ | Set-Content -LiteralPath (Join-Path $validHost 'Assets\Consumer.cs') -Encoding utf8

    $unrelatedAssembly = Join-Path $validHost 'Assets\Unrelated'
    New-Item -ItemType Directory -Path $unrelatedAssembly -Force | Out-Null
    @'
{
    "name": "Klrpxy.Unrelated",
    "overrideReferences": true,
    "precompiledReferences": [],
    "autoReferenced": false
}
'@ | Set-Content -LiteralPath (Join-Path $unrelatedAssembly 'Klrpxy.Unrelated.asmdef') -Encoding utf8
    @'
namespace Unrelated
{
    public static class IndependentContract { }
}
'@ | Set-Content -LiteralPath (Join-Path $unrelatedAssembly 'IndependentContract.cs') -Encoding utf8

    $editorLog = Join-Path $validHost 'Editor.log'
    $editorOutput = Invoke-UnityHost $validHost $editorLog
    $unrelatedResponseFile = Get-ChildItem `
        -LiteralPath (Join-Path $validHost 'Library') `
        -Recurse `
        -Filter 'Klrpxy.Unrelated.rsp' | Select-Object -First 1
    if (-not $unrelatedResponseFile)
    {
        throw "UNITY_FUNCTIONAL_FAILURE The unrelated assembly response file was not generated. log=$editorLog"
    }

    $unrelatedResponse = Get-Content -Raw -LiteralPath $unrelatedResponseFile.FullName
    if ($unrelatedResponse -notmatch '(?m)^-analyzer:.*KlrpxyGameplayStats\.dll' -or
        $unrelatedResponse -match '(?m)^-r:.*KlrpxyGameplayStats\.Runtime\.dll')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The unrelated assembly did not isolate the analyzer from Stats Runtime. response=$($unrelatedResponseFile.FullName)"
    }

    if ($editorOutput -match '(?m)error (CS|KGS)\d+')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The consumer did not compile. log=$editorLog"
    }

    if ($editorOutput -notmatch 'KLRPXY_STATS_DIAGNOSTICS_EXCEPTION')
    {
        throw "UNITY_FUNCTIONAL_FAILURE StatsDiagnostics did not reach Debug.LogException. log=$editorLog"
    }

    $passCount = [regex]::Matches($editorOutput, 'KLRPXY_STATS_UNITY_VALID_PASS').Count
    if ($passCount -ne 1)
    {
        throw "UNITY_FUNCTIONAL_FAILURE Expected exactly one runtime pass marker but found $passCount. log=$editorLog"
    }

    $validOutput = (Get-Content -Raw -LiteralPath $tagsImportLog),
        (Get-Content -Raw -LiteralPath $statsImportLog),
        $editorOutput -join [Environment]::NewLine
    if ($validOutput -match '(?i)(Microsoft\.CodeAnalysis.*(conflict|duplicate)|AD0001|analyzer.+(exception|crash)|KlrpxyGameplayTags\.Runtime.+already loaded)')
    {
        throw "UNITY_FUNCTIONAL_FAILURE Unity reported a Roslyn conflict, analyzer failure, or duplicate dependency. log=$editorLog"
    }

    $negativeHost = Join-Path $hostRoot 'missing-tags'
    Write-NegativeHostProject $negativeHost
    $negativeImportLog = Join-Path $negativeHost 'StatsImport.log'
    Import-UnityPackage $negativeHost 'Klrpxy.Gameplay.Stats.unitypackage' $negativeImportLog -AllowCompilationErrors
    @'
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class MissingTagsStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $negativeHost 'Assets\Consumer.cs') -Encoding utf8

    $negativeEditorLog = Join-Path $negativeHost 'Editor.log'
    $negativeEditorOutput = Invoke-UnityHost $negativeHost $negativeEditorLog -AllowCompilationErrors
    $negativeOutput = (Get-Content -Raw -LiteralPath $negativeImportLog), $negativeEditorOutput -join [Environment]::NewLine
    if ($negativeOutput -notmatch 'KlrpxyGameplayStats\.Runtime')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The Stats Runtime was not referenced in the missing-Tags project. log=$negativeEditorLog"
    }

    if ($negativeOutput -notmatch 'KGS003' -or $negativeOutput -notmatch 'Gameplay Tags v0\.2\.1')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The missing-Tags project did not report KGS003 with installation guidance. log=$negativeEditorLog"
    }

    if ($negativeOutput -match '(?i)(AD0001|analyzer.+(exception|crash))')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The Stats analyzer failed in the missing-Tags project. log=$negativeEditorLog"
    }

    $legacyHost = Join-Path $hostRoot 'tags-v0.2.0'
    Write-LegacyTagsHostProject $legacyHost
    $legacyTagsImportLog = Join-Path $legacyHost 'TagsImport.log'
    $legacyStatsImportLog = Join-Path $legacyHost 'StatsImport.log'
    Import-UnityPackage $legacyHost 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage' $legacyTagsImportLog
    Import-UnityPackage $legacyHost 'Klrpxy.Gameplay.Stats.unitypackage' $legacyStatsImportLog -AllowCompilationErrors
    @'
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class LegacyTagsStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $legacyHost 'Assets\Consumer.cs') -Encoding utf8

    $legacyEditorLog = Join-Path $legacyHost 'Editor.log'
    $legacyEditorOutput = Invoke-UnityHost $legacyHost $legacyEditorLog -AllowCompilationErrors
    $legacyOutput = (Get-Content -Raw -LiteralPath $legacyTagsImportLog),
        (Get-Content -Raw -LiteralPath $legacyStatsImportLog),
        $legacyEditorOutput -join [Environment]::NewLine
    if ($legacyOutput -notmatch 'KGS003' -or $legacyOutput -notmatch 'Gameplay Tags v0\.2\.1')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The Tags v0.2.0 project did not report KGS003 with upgrade guidance. log=$legacyEditorLog"
    }

    if ($legacyOutput -match '(?i)(AD0001|analyzer.+(exception|crash))')
    {
        throw "UNITY_FUNCTIONAL_FAILURE The Stats analyzer failed with Tags v0.2.0. log=$legacyEditorLog"
    }

    Write-Output "KLRPXY_STATS_UNITY_SMOKE_PASS unity=$UnityVersion temp=$hostRoot"
}
finally
{
    if ((-not $KeepHost) -and (Test-Path -LiteralPath $hostRoot))
    {
        Remove-TemporaryDirectory $hostRoot
    }
}
