# Strongly typed StatSets with generated keys

Stat sets will be defined as strongly typed C# classes that inherit from `StatSet`, and a source generator will produce build-stable keys for their declared stats. This keeps the authoring model close to GAS-style attribute sets while avoiding raw string targeting in modifiers; cross-version persistent keys and dynamic stat sets can be considered later if real use cases appear.
