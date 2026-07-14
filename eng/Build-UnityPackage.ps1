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
$projectPath = Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\KlrpxyGameplayTags.csproj'
if (-not $OutputPath)
{
    $OutputPath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Tags.0.2.0.unitypackage'
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "Unity executable was not found: $UnityPath"
}

dotnet build $projectPath --configuration Release --nologo
if ($LASTEXITCODE -ne 0)
{
    throw 'The generator build failed.'
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayTagsPackage-" + [Guid]::NewGuid().ToString('N'))
try
{
    $assetsRoot = Join-Path $stagingRoot 'Assets\KlrpxyGameplayTags'
    $editorRoot = Join-Path $stagingRoot 'Assets\Editor'
    New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $editorRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'ProjectSettings') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\bin\Release\netstandard2.0\KlrpxyGameplayTags.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\unity-package\KlrpxyGameplayTags.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags.Runtime\bin\Release\netstandard2.0\KlrpxyGameplayTags.Runtime.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.Runtime.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\unity-package\KlrpxyGameplayTags.Runtime.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.Runtime.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\README.md') -Destination (Join-Path $assetsRoot 'README.md')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\README.zh-CN.md') -Destination (Join-Path $assetsRoot 'README.zh-CN.md')
    @'
using System;
using UnityEditor;

public static class KlrpxyPackageExporter
{
    public static void Export()
    {
        AssetDatabase.ExportPackage(
            "Assets/KlrpxyGameplayTags",
            Environment.GetEnvironmentVariable("KLRPXY_UNITY_PACKAGE_OUTPUT"),
            ExportPackageOptions.Recurse);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $editorRoot 'KlrpxyPackageExporter.cs') -Encoding utf8
    @'
m_EditorVersion: 2022.3.62f3
m_EditorVersionWithRevision: 2022.3.62f3
'@ | Set-Content -LiteralPath (Join-Path $stagingRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

    $stagedPackage = Join-Path $stagingRoot 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage'
    $env:KLRPXY_UNITY_PACKAGE_OUTPUT = $stagedPackage
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $stagingRoot -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-executeMethod',
        'KlrpxyPackageExporter.Export')
    Wait-UnityProcess $process
    if (-not (Test-Path -LiteralPath $stagedPackage))
    {
        throw 'Unity failed to export the package.'
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

Write-Output "KLRPXY_PACKAGE_BUILD_PASS path=$OutputPath"
