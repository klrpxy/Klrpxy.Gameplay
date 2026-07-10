# Tags

Tags is a gameplay context for naming, grouping, and querying hierarchical gameplay concepts. The core context is engine-agnostic and does not depend on Unity APIs.

## Language

**Tag**:
A predeclared hierarchical gameplay concept identifier, written as dot-separated segments. Declaring a tag also declares every ancestor in its path; an undeclared name cannot become a tag at runtime.
_Avoid_: Label, string tag, category

**Tag Table**:
The project's single authoritative list of declared tags.
_Avoid_: Tag config, tag file

**TagSet**:
A mutable set of unique tags explicitly owned by an object or context. It represents final tag state without tracking inferred ancestors, contributing sources, or reference counts.
_Avoid_: Tag list, tag collection

**Tag Match**:
A directional relationship in which a candidate tag is the queried tag or one of its descendants. An exact match requires both tags to be identical.
_Avoid_: Contains, parent match

**TagQuery**:
An immutable, reusable composition of tag matching conditions evaluated against a `TagSet`.
_Avoid_: Tag filter, tag predicate
