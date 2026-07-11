# Unity Roslyn source generator compatibility research

This note records only behavior documented by Unity. It is not runtime proof that the Tags prototype works in a particular Editor patch release.

## Documented behavior

### Unity 2022.3 LTS

- Unity's source-generator setup targets **.NET Standard 2.0** and requires **Microsoft.CodeAnalysis 3.8**. The documented path uses `ISourceGenerator`; APIs introduced by later Roslyn packages are therefore outside this compatibility guarantee. [Unity 2022.3 Manual: Roslyn analyzers and source generators](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html)
- Import the analyzer/source-generator DLL under `Assets`, disable `Any Platform`, `Editor`, and `Standalone` in the Plugin Inspector, then assign the exact, case-sensitive asset label **`RoslynAnalyzer`**. Unity recognizes assets with that label as analyzers or source generators. [Unity 2022.3 Manual: Roslyn analyzers and source generators](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html)
- Unity evaluates files ending in **`.additionalfile`**. A file is passed to compilation only when its name has the form **`Filename.[Analyzer Name].additionalfile`**. `[Analyzer Name]` is case-sensitive and must match the targeted analyzer; `Filename` must not contain a period. A file without the analyzer-name suffix is imported but not passed to compilation. [Unity 2022.3 Scripting API: `ScriptCompilerOptions.RoslynAdditionalFilePaths`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Compilation.ScriptCompilerOptions.RoslynAdditionalFilePaths.html)
- Unity filters additional files per compiled assembly according to the analyzers running for that assembly. The analyzer context receives the assembly's full additional-file list, not a list already narrowed to the current analyzer, so the generator remains responsible for selecting its own input. [Unity 2022.3 Scripting API: `ScriptCompilerOptions.RoslynAdditionalFilePaths`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Compilation.ScriptCompilerOptions.RoslynAdditionalFilePaths.html)
- Unity's documented AdditionalFile workflow specifies the `Assets` location, extension, and filename convention; it does **not** prescribe an asset label for the text file. `RoslynAnalyzer` is the documented label for the analyzer/source-generator DLL. [Unity 2022.3 Manual](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html), [Unity 2022.3 Scripting API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Compilation.ScriptCompilerOptions.RoslynAdditionalFilePaths.html)

Consequently, `GameplayTags.KlrpxyGameplayTags.additionalfile` satisfies the documented filename shape only if Unity resolves **`KlrpxyGameplayTags`** as the installed analyzer's name. The documentation does not define which artifact identity supplies `[Analyzer Name]`.

### Unity 6.0

- The generator still targets **.NET Standard 2.0**, but Unity now requires **Microsoft.CodeAnalysis.CSharp 4.3**, rather than the 3.8 dependency documented for 2022.3. The `RoslynAnalyzer` DLL label and Plugin Inspector setup remain the same. [Unity 6 Manual: Create and use a source generator](https://docs.unity3d.com/6000.0/Documentation/Manual/create-source-generator.html)
- Unity 6 documents the same AdditionalFile extension, filename, case-sensitivity, per-assembly filtering, and analyzer-side selection rules. No behavioral difference from the 2022.3 API documentation is stated. [Unity 6 Manual: Additional files for Roslyn analyzers and source generators](https://docs.unity3d.com/6000.0/Documentation/Manual/roslyn-analyzers-additional-files.html)

## Requires runtime proof

The official documentation does not establish:

- whether `[Analyzer Name]` resolves from the DLL filename, assembly name, generator type, or another identifier;
- whether a DLL compiled against Roslyn 3.8 loads unchanged in Unity 6's Roslyn 4.3 host;
- whether changing the AdditionalFile reliably retriggers generation and refreshes diagnostics in the exact Unity 2022.3 and Unity 6 patch versions under test;
- which target compilations receive the generator in the prototype's actual assembly layout;
- whether diagnostics against the AdditionalFile retain the required file path and exact line/column locations;
- whether generation into the annotated consumer-owned partial root and the generated tag hierarchy compile and behave as designed.

These items must be reported from Editor runs (or, where explicitly identified, isolated Roslyn tests), not inferred from the documentation above.
