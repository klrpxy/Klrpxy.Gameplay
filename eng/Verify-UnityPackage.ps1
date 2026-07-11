[CmdletBinding()]
param(
    [string]$PackagePath = 'artifacts/Klrpxy.Gameplay.Tags.0.1.0.unitypackage'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf))
{
    throw "Expected Unity package was not found: $PackagePath"
}

if ([System.IO.Path]::GetExtension($PackagePath) -ne '.unitypackage')
{
    throw "Expected a .unitypackage artifact: $PackagePath"
}

if ((Get-Item -LiteralPath $PackagePath).Length -eq 0)
{
    throw "Unity package is empty: $PackagePath"
}

Write-Output "KLRPXY_PACKAGE_VERIFY_PASS path=$PackagePath"
