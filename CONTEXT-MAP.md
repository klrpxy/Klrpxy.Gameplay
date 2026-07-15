# Context Map

## Contexts

- [Tags](./src/Klrpxy.Gameplay.Tags/CONTEXT.md) - defines hierarchical gameplay tags and tag sets.
- [Stats](./src/Klrpxy.Gameplay.Stats/CONTEXT.md) - defines runtime numeric stats, stat sets, modifiers, and stat calculation.

## Relationships

- **Stats -> Tags**: A StatSubject owns a TagSet that classifies the gameplay object which owns its stat set. Tags remains a separate context from Stats.
