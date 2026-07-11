# Klrpxy Gameplay Tags

`KlrpxyGameplayTags.dll` is a Unity source generator that creates an engine-agnostic `Tag`, `TagSet`, and `TagQuery` API from one project Tag Table. It supports Unity 2022.3 LTS and Unity 6 with the same .NET Standard 2.0 generator DLL built against Roslyn 3.8.0.

## Install

Build the local package from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Build-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe"
```

In Unity, use **Assets > Import Package > Custom Package** and select `artifacts/Klrpxy.Gameplay.Tags.0.1.0.unitypackage`. The package imports `KlrpxyGameplayTags.dll` as an analyzer: runtime platform compatibility is disabled and the DLL has the exact `RoslynAnalyzer` label. Do not copy `Microsoft.CodeAnalysis` DLLs beside the generator.

## Declare a Tag Table

Create exactly one file under `Assets` named `GameplayTags.KlrpxyGameplayTags.additionalfile`. Its `KlrpxyGameplayTags` suffix must remain aligned with the generator assembly name.

Write one case-sensitive path per line. Segments must match `[A-Z][A-Za-z0-9]*`. Blank lines and full-line `#` comments are ignored; inline comments are not supported.

```text
# Unit tags
Unit.Enemy.Boss
Ability.Cast
```

## Generate and use Tags

Mark exactly one non-generic top-level static partial class in the consumer assembly:

```csharp
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class Tags
{
}
```

The generator adds the declared hierarchy. Every node is a canonical immutable `Tag`:

```csharp
Tag boss = Tags.Unit.Enemy.Boss;
Tag parent = boss.GetParent(); // Tags.Unit.Enemy
```

`TagSet` stores only explicitly owned unique Tags. `Has` uses directional Tag Match (a stored descendant matches its queried ancestor), while `HasExact` checks only the exact member.

```csharp
var tags = new TagSet();
tags.Add(Tags.Unit.Enemy.Boss);

bool broadMatch = tags.Has(Tags.Unit.Enemy);
bool exactMatch = tags.HasExact(Tags.Unit.Enemy.Boss);
```

`TagQuery` is immutable and reusable. Build it with `Has`, `HasExact`, `All`, `Any`, and `None`; the combinators also accept Tags directly. Empty `All`, `Any`, and `None` evaluate to `true`, `false`, and `true`.

```csharp
TagQuery hostileCaster = TagQuery.All(
    TagQuery.Has(Tags.Unit.Enemy),
    TagQuery.Any(Tags.Ability.Cast, Tags.Ability.Channel));

bool matches = hostileCaster.Matches(tags);
```

## Diagnostics

`KTAG001` and `KTAG002` report invalid or multiple generated roots. `KTAG003` reports an invalid path segment, `KTAG004` a duplicate explicit declaration, and `KTAG005` a reserved segment. `KTAG006` and `KTAG007` report a missing or ambiguous Tag Table. Tag Table diagnostics point at the offending AdditionalFile line.

## Verification

Run the fast .NET/Roslyn suite during development:

```powershell
dotnet test Klrpxy.Gameplay.sln -c Release
```

Run Unity smoke verification only at the release boundary. It creates temporary host projects and checks both supported editor baselines without adding hosts to this package.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe" -UnityVersion 2022.3.62f3
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 6000.5.0f1>/Editor/Unity.exe" -UnityVersion 6000.5.0f1
```

For interactive Unity 6 verification, import the same `.unitypackage` into a test project, create a Tag Table and annotated root, then confirm a generated descendant and its parent at runtime.
