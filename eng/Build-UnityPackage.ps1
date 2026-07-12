[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

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
    New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'ProjectSettings') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\bin\Release\netstandard2.0\KlrpxyGameplayTags.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\unity-package\KlrpxyGameplayTags.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags.Runtime\bin\Release\netstandard2.0\KlrpxyGameplayTags.Runtime.dll') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.Runtime.dll')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\unity-package\KlrpxyGameplayTags.Runtime.dll.meta') -Destination (Join-Path $assetsRoot 'KlrpxyGameplayTags.Runtime.dll.meta')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\README.md') -Destination (Join-Path $assetsRoot 'README.md')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\Klrpxy.Gameplay.Tags\README.zh-CN.md') -Destination (Join-Path $assetsRoot 'README.zh-CN.md')
    @'
m_EditorVersion: 2022.3.62f3
m_EditorVersionWithRevision: 2022.3.62f3
'@ | Set-Content -LiteralPath (Join-Path $stagingRoot 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

    $stagedPackage = Join-Path $stagingRoot 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage'
    $process = Start-Process -FilePath $UnityPath -WorkingDirectory $stagingRoot -Wait -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-quit', '-projectPath', '.', '-exportPackage',
        'Assets/KlrpxyGameplayTags', 'Klrpxy.Gameplay.Tags.0.2.0.unitypackage')
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $stagedPackage))
    {
        throw 'Unity failed to export the package.'
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

Write-Output "KLRPXY_PACKAGE_BUILD_PASS path=$OutputPath"
