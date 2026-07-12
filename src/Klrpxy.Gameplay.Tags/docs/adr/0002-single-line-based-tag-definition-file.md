# Use one line-based tag definition file

> Superseded by the v0.2.0 class-local `TagTable` authoring model documented in [ADR 0006](0006-distribute-tags-as-a-unitypackage.md). The external AdditionalFile described below is not supported by v0.2.0.

Each Unity project will declare its tags in exactly one `GameplayTags.KlrpxyGameplayTags.additionalfile`, located anywhere under `Assets`. The file uses one full tag path per line, permits blank lines and `#` full-line comments, and rejects duplicate explicit declarations; every case-sensitive path segment must match `[A-Z][A-Za-z0-9]*`. This follows Unity's official additional-file pipeline while keeping authoring simpler than JSON for metadata-free tags and preserving a direct mapping from each segment to its generated C# member.
