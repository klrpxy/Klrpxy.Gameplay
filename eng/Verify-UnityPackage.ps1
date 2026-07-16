[CmdletBinding()]
param(
    [string]$PackagePath = 'artifacts/Klrpxy.Gameplay.Tags.0.2.1.unitypackage'
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

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KlrpxyGameplayTagsVerify-" + [Guid]::NewGuid().ToString('N'))
try
{
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    tar -xzf $PackagePath -C $extractRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "Unity package could not be inspected: $PackagePath"
    }

    $packageAssets = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter pathname | ForEach-Object {
        [PSCustomObject]@{
            Path = (Get-Content -Raw -LiteralPath $_.FullName).Trim()
            Meta = Get-Content -Raw -LiteralPath (Join-Path $_.DirectoryName 'asset.meta')
            Asset = Join-Path $_.DirectoryName 'asset'
        }
    })
    $analyzer = @($packageAssets | Where-Object { $_.Path -eq 'Assets/KlrpxyGameplayTags/KlrpxyGameplayTags.dll' })
    $runtime = @($packageAssets | Where-Object { $_.Path -eq 'Assets/KlrpxyGameplayTags/KlrpxyGameplayTags.Runtime.dll' })
    if ($analyzer.Count -ne 1 -or $runtime.Count -ne 1)
    {
        throw 'Unity package must contain exactly one analyzer DLL and one runtime DLL.'
    }

    if (($analyzer[0].Meta -notmatch '(?m)^- RoslynAnalyzer\r?$') -or ($analyzer[0].Meta -notmatch '(?s)Any:\s*second:\s*enabled: 0'))
    {
        throw 'The analyzer DLL must be labelled RoslynAnalyzer with runtime platforms disabled.'
    }

    if (($runtime[0].Meta -match 'RoslynAnalyzer') -or ($runtime[0].Meta -notmatch '(?s)Any:\s*second:\s*enabled: 1'))
    {
        throw 'The runtime DLL must not be a RoslynAnalyzer and must have runtime platforms enabled.'
    }

    $runtimeAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($runtime[0].Asset))
    $requiredTypes = @(
        'Klrpxy.Gameplay.Tags.Runtime.IGameplayTag',
        'Klrpxy.Gameplay.Tags.Runtime.IHierarchicalGameplayTag',
        'Klrpxy.Gameplay.Tags.Runtime.ITagSet',
        'Klrpxy.Gameplay.Tags.Runtime.ITagQuery',
        'Klrpxy.Gameplay.Tags.Runtime.TagSetChange'
    )
    $exportedTypes = @($runtimeAssembly.GetExportedTypes() | ForEach-Object FullName)
    $missingTypes = @($requiredTypes | Where-Object { $_ -notin $exportedTypes })
    if ($runtimeAssembly.GetName().Version -ne [Version]'0.2.1.0' -or $missingTypes.Count -ne 0)
    {
        throw "The runtime DLL must be v0.2.1 and expose the Stats integration contract. Missing: $($missingTypes -join ', ')"
    }
}
finally
{
    if (Test-Path -LiteralPath $extractRoot)
    {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}

Write-Output "KLRPXY_PACKAGE_VERIFY_PASS path=$PackagePath"
