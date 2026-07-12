[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,

    [string]$PackagePath,

    [switch]$KeepHost
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $PackagePath)
{
    $PackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Tags.0.2.0.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "Unity executable was not found: $UnityPath"
}

& (Join-Path $PSScriptRoot 'Verify-UnityPackage.ps1') -PackagePath $PackagePath

$hostRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayTagsSmoke-" + $UnityVersion + "-" + [Guid]::NewGuid().ToString('N'))

function Write-HostProject([string]$Path)
{
    New-Item -ItemType Directory -Path (Join-Path $Path 'Assets') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Path 'ProjectSettings') -Force | Out-Null

    Copy-Item -LiteralPath $PackagePath -Destination (Join-Path $Path 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage')

    @"
m_EditorVersion: $UnityVersion
m_EditorVersionWithRevision: $UnityVersion
"@ | Set-Content -LiteralPath (Join-Path $Path 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

}

function Write-Consumer([string]$Path, [string]$Source)
{
    Set-Content -LiteralPath (Join-Path $Path 'Assets\Consumer.cs') -Value $Source -Encoding utf8
}

function Invoke-UnityHost([string]$Path, [string]$LogPath)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -Wait -PassThru -ArgumentList @(
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath',
        '.',
        '-logFile',
        'Editor.log')
    if ($process.ExitCode -ne 0)
    {
        throw "Unity exited with code $($process.ExitCode). See $LogPath"
    }

    return Get-Content -Raw -LiteralPath $LogPath
}

function Import-UnityPackage([string]$Path, [string]$LogPath)
{
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $Path -Wait -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-importPackage',
        'Klrpxy.Gameplay.Tags.0.2.0.unitypackage', '-logFile', 'Import.log')
    if ($process.ExitCode -ne 0)
    {
        throw "Unity failed to import the package. See $LogPath"
    }
}

try
{
    $validHost = Join-Path $hostRoot 'valid'
    Write-HostProject $validHost
    Import-UnityPackage $validHost (Join-Path $validHost 'Import.log')
    Write-Consumer $validHost @'
using Klrpxy.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = @"Unit.Enemy.Boss
Ability.Cast";
    }

    [InitializeOnLoad]
    public static class SmokeContract
    {
        static SmokeContract()
        {
            Tag boss = ProjectTags.Unit.Enemy.Boss;
            if (boss.GetPath() == "Unit.Enemy.Boss"
                && object.ReferenceEquals(boss.GetParent(), ProjectTags.Unit.Enemy))
            {
                Debug.Log("KLRPXY_UNITY_VALID_PASS");
            }
            else
            {
                Debug.LogError("KLRPXY_UNITY_VALID_FAIL");
            }
        }
    }
}
'@
    $validLog = Join-Path $validHost 'Editor.log'
    $validOutput = Invoke-UnityHost $validHost $validLog
    if ($validOutput -notmatch 'KLRPXY_UNITY_VALID_PASS')
    {
        throw "The generated valid-consumer contract did not run. See $validLog"
    }

    $invalidHost = Join-Path $hostRoot 'invalid'
    Write-HostProject $invalidHost
    Import-UnityPackage $invalidHost (Join-Path $invalidHost 'Import.log')
    Write-Consumer $invalidHost @'
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = @"Unit
Unit.invalid-name";
    }
}
'@
    $invalidLog = Join-Path $invalidHost 'Editor.log'
    $invalidOutput = Invoke-UnityHost $invalidHost $invalidLog
    $hasInvalidDiagnostic = $invalidOutput -match 'KTAG003'
    if (-not $hasInvalidDiagnostic)
    {
        throw "The invalid TagTable diagnostic did not retain KTAG003. See $invalidLog"
    }

    Write-Output "KLRPXY_UNITY_SMOKE_PASS unity=$UnityVersion temp=$hostRoot"
}
finally
{
    if ((-not $KeepHost) -and (Test-Path -LiteralPath $hostRoot))
    {
        Remove-Item -LiteralPath $hostRoot -Recurse -Force
    }
}
