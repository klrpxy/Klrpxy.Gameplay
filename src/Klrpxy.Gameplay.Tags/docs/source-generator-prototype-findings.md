# Tags source-generator prototype findings

## Verdict

The accepted source-generator design is viable in the tested editors. One `netstandard2.0` generator DLL built against `Microsoft.CodeAnalysis.CSharp` 3.8.0 loaded, generated, compiled, and reported exact AdditionalFile diagnostics in both Unity 2022.3.62f3 and Unity 6000.5.0f1.

No accepted ADR is contradicted. The prototype exposed implementation and packaging constraints that should be carried into the PRD.

## Environment and command

- Repository checkpoint: `caa419c docs: 记录标签包设计决策`
- Unity editors: `2022.3.62f3` and `6000.5.0f1`
- Generator target: `.NETStandard,Version=v2.0`
- Generator compile-time Roslyn package: `Microsoft.CodeAnalysis.CSharp` `3.8.0`
- Standalone host evidence: loaded `Microsoft.CodeAnalysis` `3.8.0.0`
- Command: `src/Klrpxy.Gameplay.Tags/PROTOTYPE-source-generator/run-prototype.ps1`
- Successful run artifacts: `C:\Users\sqz\AppData\Local\Temp\KlrpxyGameplayTagsPrototype-20260710-161148`

The prototype directory and temporary Unity projects are disposable and must not become production implementation.

## Results by question

| # | Result | Evidence |
|---|---|---|
| 1 | Pass | `KlrpxyGameplayTags.dll` targeted .NET Standard 2.0, referenced Roslyn 3.8, and loaded as a `RoslynAnalyzer` in Unity 2022.3.62f3. The generated `Assembly-CSharp` compiled and the editor validation method ran. |
| 2 | Pass | Unity's compiler response included `Assets/GameplayTags.KlrpxyGameplayTags.additionalfile`. Diagnostics `KTAG003`, `KTAG004`, and `KTAG005` were reported against the exact file at lines 2, 3, 5, 6, and 7 in both editors. |
| 3 | Pass | `[GenerateGameplayTags] public static partial class Tags` in `Assembly-CSharp` selected the consumer namespace and root. The generated API compiled as `Prototype.Consumer.Tags`; a standalone compilation without the marker emitted no hierarchy. |
| 4 | Pass | Both editor runs accessed `Tags.Unit.Enemy.Boss`. Paths were `Unit`, `Unit.Enemy`, and `Unit.Enemy.Boss`; `GetParent()` returned the canonical object references; generated nodes had no public constructors; the `Tag` base constructor was private. |
| 5 | Pass | The invalid table produced exactly `KTAG003@2`, `KTAG003@3`, `KTAG004@5`, `KTAG005@6`, and `KTAG003@7` in both Unity versions and the standalone Roslyn 3.8 host. Blank lines and full-line `#` comments in the valid table were ignored. |
| 6 | Pass | The unchanged Roslyn 3.8 generator DLL also loaded and generated successfully in Unity 6000.5.0f1. A Unity-6-specific host-facing DLL was not required in this tested version; parser and emitter code were shared because the entire binary was shared. |

Unity log markers:

```text
KLRPXY_PROTOTYPE_PASS unity=2022.3.62f3 boss=Unit.Enemy.Boss parent=Unit.Enemy
KLRPXY_PROTOTYPE_PASS unity=6000.5.0f1 boss=Unit.Enemy.Boss parent=Unit.Enemy
```

## Installation/import sequence proven by the run

1. Build the generator as `KlrpxyGameplayTags.dll` for .NET Standard 2.0 with Roslyn 3.8.0; do not copy Roslyn assemblies beside it.
2. Put the DLL under `Assets`, disable runtime platform compatibility in `PluginImporter`, and apply the case-sensitive `RoslynAnalyzer` label.
3. Put `GameplayTags.KlrpxyGameplayTags.additionalfile` under `Assets`. No label is required for this file; its `KlrpxyGameplayTags` analyzer-name suffix matches the generator assembly name.
4. Add the annotated consumer-owned root and allow Unity to recompile.
5. For the diagnostic run, replace the valid table with the invalid table and let Unity recompile; both editors retained file and line locations.

The official setup and compatibility facts are cited separately in [unity-roslyn-source-generator-research.md](./unity-roslyn-source-generator-research.md). Documentation says Unity 6 generators should compile against Roslyn 4.3, but the runtime evidence above proves that this particular 3.8 binary works unchanged in Unity 6000.5.0f1. This is evidence for the tested editor, not a guarantee for every future Unity 6 release.

## Constraints discovered

- Roslyn 3.8 does not expose `GeneratorInitializationContext.RegisterForPostInitialization`. Emit the marker attribute with `AddSource` from `Execute`, or use another API that exists in 3.8.
- The accepted filename makes `KlrpxyGameplayTags` the analyzer-name suffix. Keep the analyzer assembly/package identity aligned with that suffix so Unity passes the file.
- A `protected Tag(...)` constructor lets consumers create undeclared subclasses. The prototype preserved the invariant by nesting the public sealed generated node types inside `Tag`, allowing them to call a private `Tag` constructor while keeping `Tags.Unit.Enemy.Boss` unchanged.
- Unity 6 currently accepts the 3.8 binary, but the Unity-6 smoke test should remain in release verification because Unity's documentation targets Roslyn 4.3 there.

## ADR amendments

None required. The accepted behavior and minimum-version decisions held. The constraints above belong in implementation acceptance criteria and packaging instructions.
