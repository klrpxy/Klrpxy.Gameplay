# Klrpxy Gameplay Tags

[中文文档](README.zh-CN.md)

Klrpxy Gameplay Tags turns one small text file into type-safe, hierarchical tags for a Unity project. Use it when gameplay code needs to describe categories such as `Unit.Enemy.Boss` without repeatedly passing strings around.

## Quick start

1. Download `Klrpxy.Gameplay.Tags.0.1.0.unitypackage` from the [v0.1.0 release](https://github.com/klrpxy/Klrpxy.Gameplay/releases/tag/v0.1.0).
2. In Unity, choose **Assets > Import Package > Custom Package** and import the downloaded file.
3. Under `Assets`, create a text file named exactly `GameplayTags.KlrpxyGameplayTags.additionalfile`:

   ```text
   Unit.Enemy.Boss
   Ability.Cast
   Ability.Channel
   State.Stunned
   ```

4. Add one script in the same Unity assembly that will use the tags:

   ```csharp
   using Klrpxy.Gameplay.Tags;

   [GenerateGameplayTags]
   public static partial class Tags
   {
   }
   ```

5. After Unity finishes compiling, use the generated tags in your code:

   ```csharp
   Tag boss = Tags.Unit.Enemy.Boss;
   Tag enemy = boss.GetParent(); // Tags.Unit.Enemy
   ```

If `Tags.Unit.Enemy.Boss` compiles or appears in code completion, setup succeeded. If it does not, first check the Unity Console for a `KTAG` diagnostic and verify the file name in step 3.

## Why a Tag Table?

The Tag Table is the single source of truth for the project vocabulary. Each non-empty line declares one tag path; declaring a child also creates its parents. The example above creates `Unit`, `Unit.Enemy`, `Unit.Enemy.Boss`, `Ability`, `Ability.Cast`, `Ability.Channel`, `State`, and `State.Stunned`.

This lets code share the same names safely. A typo such as `Unit.Enemey.Boss` is caught by the compiler instead of becoming a silent string mismatch at runtime.

Keep exactly one Tag Table under `Assets`. Paths are case-sensitive; each segment starts with an uppercase letter and then uses letters or digits. Blank lines and full-line comments are allowed.

```text
# Valid paths
Unit.Player
Unit.Enemy.Boss

# Invalid: each segment must begin with an uppercase letter
unit.Enemy
```

## Store the tags an object owns

`TagSet` holds the explicit, unique tags owned by an object. It does not duplicate parent tags, but it understands their hierarchy when matching.

```csharp
var tags = new TagSet();
tags.Add(Tags.Unit.Enemy.Boss);

bool isEnemy = tags.Has(Tags.Unit.Enemy);          // true: Boss is an Enemy
bool isExactlyEnemy = tags.HasExact(Tags.Unit.Enemy); // false: only Boss was added
bool isBoss = tags.HasExact(Tags.Unit.Enemy.Boss); // true
```

Use `Has` for category checks and `HasExact` when the exact tag matters.

## Ask reusable questions with TagQuery

Use `TagQuery` when a check combines several conditions or will be reused. Queries are immutable: evaluating one never changes it or the `TagSet`.

```csharp
TagQuery hostileCaster = TagQuery.All(
    TagQuery.Has(Tags.Unit.Enemy),
    TagQuery.Any(Tags.Ability.Cast, Tags.Ability.Channel),
    TagQuery.None(Tags.State.Stunned));

var casterTags = new TagSet();
casterTags.Add(Tags.Unit.Enemy.Boss);
casterTags.Add(Tags.Ability.Cast);

bool canAct = hostileCaster.Matches(casterTags); // true
```

`All` requires every condition, `Any` requires at least one, and `None` requires no condition to match. For one tag, the concise forms are `TagQuery.Has(tag)` and `TagQuery.HasExact(tag)`.

## Troubleshooting

| What you see | What to check |
| --- | --- |
| No generated `Tags` members | The Tag Table is named `GameplayTags.KlrpxyGameplayTags.additionalfile`, exists once under `Assets`, and Unity has finished compiling. |
| `KTAG001` or `KTAG002` | There must be exactly one non-generic, top-level `static partial` class marked with `[GenerateGameplayTags]` in the consumer assembly. |
| `KTAG003`, `KTAG004`, or `KTAG005` | Correct the indicated Tag Table line: use valid path segments, remove duplicate explicit paths, and avoid reserved segments. |
| `KTAG006` or `KTAG007` | Create exactly one Tag Table under `Assets`. |

## Advanced and maintainer notes

The generator supports Unity 2022.3 LTS and Unity 6. It is distributed as a .NET Standard 2.0 Roslyn 3.8.0 analyzer. The imported DLL must retain its exact `RoslynAnalyzer` label with runtime platform compatibility disabled; do not place `Microsoft.CodeAnalysis` DLLs beside it.

To build the local package from this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Build-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe"
```

Run the .NET/Roslyn suite during development:

```powershell
dotnet test Klrpxy.Gameplay.sln -c Release
```

At the release boundary, run the Unity package smoke test for each supported editor:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe" -UnityVersion 2022.3.62f3
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 6000.5.0f1>/Editor/Unity.exe" -UnityVersion 6000.5.0f1
```

For completeness, empty `All`, `Any`, and `None` queries evaluate to `true`, `false`, and `true` respectively.
