# Generate into an annotated partial root

Consumers will mark one `static partial class` with `[GenerateGameplayTags]`, and the generator will extend that type with the hierarchy declared in the tag definition file. The marker selects the target assembly, namespace, accessibility, and root type name without adding output directives to the line-based definition format. Compilations without the marker are ignored; a marked compilation requires exactly one tag table and fails generation if the table is missing or ambiguous.
