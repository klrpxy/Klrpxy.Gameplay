# Current resource semantics research

## Question

Should a consumable value such as current health or mana use the same modifier-capable model as an ordinary Stat, or should it be a separate Resource bounded by capacity Stats such as MaxHealth?

## Primary-source findings

### Unreal Gameplay Ability System: one Attribute abstraction

Unreal GAS uses `FGameplayAttributeData` for both resources and ordinary stats. Each Attribute has a Base value and a Current value. Instant Gameplay Effects change Base; duration-based effects change Current and are undone when they expire. Epic's documentation explicitly lists health alongside ordinary traits as a common Attribute use case. GAS does not provide universal min/max clamping: an AttributeSet must define that behavior and the relationship between current and maximum values itself.

Sources:

- [Gameplay Attributes and Attribute Sets](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-attributes-and-attribute-sets-for-the-gameplay-ability-system-in-unreal-engine)
- [Understanding the Gameplay Ability System](https://dev.epicgames.com/documentation/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system?lang=en-US)

This proves that a mature system can let current Health use the same Base/Current and Modifier machinery as other attributes. It does not prove that doing so gives Health an unambiguous meaning; GAS deliberately leaves the policy to each game.

### Epic Action RPG sample: preserve the current percentage

Epic's Action RPG sample declares current/max Health and Mana as separate Attributes. Its `PreAttributeChange` scales the current resource when its maximum changes; `PostGameplayEffectExecute` then clamps and notifies. Consequently, a character at `80 / 100` becomes `40 / 50` when MaxHealth falls to 50, then returns to `80 / 100` when MaxHealth returns to 100.

Source: [Attributes and Effects in ARPG](https://dev.epicgames.com/documentation/en-us/unreal-engine/attributes-and-effects-in-arpg?application_version=4.27)

The important result is that proportional preservation is an explicit game rule, not an automatic consequence of Base/Current values.

### Epic Lyra sample: protect current Health from ordinary modifiers

Lyra still stores Health and MaxHealth as Gameplay Attributes, but treats Health specially:

- Health is current state and is capped by MaxHealth.
- ordinary Gameplay Effect modifiers cannot target Health directly;
- damage and healing pass through dedicated transient inputs and custom executions;
- Epic says this prevents timed or infinite effects from being applied to Health's Base value and causing long-term problems.

Source: [Abilities in Lyra — Health Set and damage/healing](https://dev.epicgames.com/documentation/unreal-engine/abilities-in-lyra-in-unreal-engine?lang=en-US#ulyrhealthset)

Although Lyra uses GAS storage internally, its public gameplay semantics resemble a Resource: current Health is protected mutable state, while effects act through explicit damage/healing operations and MaxHealth is the capacity.

### Roblox Humanoid: explicit current value and capacity

Roblox exposes `Humanoid.Health` and `Humanoid.MaxHealth` as distinct properties, provides `TakeDamage`, and exposes `HealthChanged`; its official health-bar example displays `Health / MaxHealth`. This is a direct current-state/capacity model rather than a Base/Current modifier stack on Health.

Source: [Roblox Humanoid API](https://create.roblox.com/docs/reference/engine/classes/Humanoid)

The API does not document a configurable proportional policy for MaxHealth changes, so it supports the structural comparison but should not be used as evidence for a particular max-change rule.

## The unavoidable ambiguity

Suppose Health is `80 / 100` and a temporary effect changes MaxHealth to 50. At least three coherent policies exist:

| Policy | While MaxHealth is 50 | After it returns to 100 | Meaning |
| --- | ---: | ---: | --- |
| Clamp current | `50 / 50` | `50 / 100` | Losing capacity can permanently discard excess current value. |
| Preserve ratio | `40 / 50` | `80 / 100` | The percentage filled is preserved. Epic Action RPG uses this policy. |
| Preserve current where possible | `50 / 50` | `80 / 100` | Hidden excess is retained and later restored. |

None is universally correct. The desired answer depends on whether a max-health debuff should deal effective damage, preserve health percentage, or merely hide unavailable capacity. Encoding one of these accidentally in generic Stat arithmetic would make gameplay behavior surprising.

There is a second ambiguity if current Health accepts temporary modifiers. If `+50 Health` is applied, the target takes 20 damage, then the modifier expires, the system must decide whether the result loses another 50, keeps the post-damage value, or preserves a percentage. A generic modifier stack cannot choose correctly without additional resource-specific rules.

## Recommendation for Klrpxy.Gameplay

Use a separate `Resource` abstraction for mutable consumable state, while keeping capacities such as MaxHealth as modifier-capable Stats:

```csharp
public Stat MaxHealth { get; }
public Resource Health { get; }
```

Recommended first-version boundary:

- `Resource.CurrentValue` is stored state and is changed by explicit operations such as `Set`, `Increase`, and `Decrease`.
- Resource itself does not accept ordinary `Flat`, `Percent`, `Multiply`, or `Override` modifiers.
- Resource min/max may depend on constants, Stats, or observable external values.
- Capacity effects target `MaxHealth`; damage and healing target `Health` through explicit operations.
- Max-bound changes use an explicit policy rather than inheriting Stat behavior accidentally.

For the smallest first version, use `ClampCurrent`: when a bound shrinks, clamp current state immediately; when it expands, do not restore discarded value. This matches the intuitive meaning of stored current state and avoids hidden values. Preserve-ratio behavior can later be offered as an explicit resource policy if a real game requires it:

```csharp
new Resource(
    currentValue: 80f,
    maxValue: ValueInput.Final(maxHealth),
    maxChangePolicy: ResourceMaxChangePolicy.ClampCurrent);
```

This recommendation is influenced most strongly by Lyra: even within a unified Attribute engine, Epic protects current Health from arbitrary ongoing modifiers and routes damage/healing through dedicated semantics. A separate Resource makes that boundary explicit and simpler in this library.
