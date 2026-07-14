[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Unity2022Path,

    [Parameter(Mandatory = $true)]
    [string]$Unity2022Version,

    [Parameter(Mandatory = $true)]
    [string]$Unity6Path,

    [Parameter(Mandatory = $true)]
    [string]$Unity6Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$tagsPackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Tags.0.2.0.unitypackage'
$statsPackagePath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.unitypackage'

foreach ($unityPath in @($Unity2022Path, $Unity6Path))
{
    if (-not (Test-Path -LiteralPath $unityPath -PathType Leaf))
    {
        throw "UNITY_ENVIRONMENT_FAILURE Unity executable was not found: $unityPath"
    }
}

& (Join-Path $PSScriptRoot 'Build-UnityPackage.ps1') -UnityPath $Unity2022Path -OutputPath $tagsPackagePath
& (Join-Path $PSScriptRoot 'Build-StatsUnityPackage.ps1') -UnityPath $Unity2022Path -OutputPath $statsPackagePath

foreach ($editor in @(
    [PSCustomObject]@{ Path = $Unity2022Path; Version = $Unity2022Version },
    [PSCustomObject]@{ Path = $Unity6Path; Version = $Unity6Version }
))
{
    & (Join-Path $PSScriptRoot 'Smoke-Test-StatsUnityPackage.ps1') `
        -UnityPath $editor.Path `
        -UnityVersion $editor.Version `
        -StatsPackagePath $statsPackagePath `
        -TagsPackagePath $tagsPackagePath
}

$reportPath = Join-Path $repositoryRoot 'artifacts\Klrpxy.Gameplay.Stats.validation.json'
& (Join-Path $PSScriptRoot 'Verify-StatsUnityPackage.ps1') `
    -PackagePath $statsPackagePath `
    -ReportPath $reportPath `
    -EditorVersions @($Unity2022Version, $Unity6Version)

Write-Output "KLRPXY_STATS_UNITY_MATRIX_PASS unity2022=$Unity2022Version unity6=$Unity6Version report=$reportPath"
