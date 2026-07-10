# Compose immutable tag queries with factories

`TagQuery` will be an immutable query tree built from `Has`, `HasExact`, `All`, `Any`, and `None` factories, with tag-only convenience overloads for the three combinators. Empty `All`, `Any`, and `None` combinations evaluate to `true`, `false`, and `true` respectively. This keeps nested conditions explicit and reusable without a mutable builder, overloaded logical operators, or implicit conversion from `Tag` to `TagQuery`.
