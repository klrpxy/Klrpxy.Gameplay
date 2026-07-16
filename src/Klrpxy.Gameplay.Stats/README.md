# Klrpxy Gameplay Stats

[中文文档](README.zh-CN.md)

Klrpxy Gameplay Stats expresses character attributes, item auras, combat growth, and resources with a small set of gameplay concepts. Dependency propagation, Group membership, Tag conditions, and lifetime cleanup are automatic after rules are declared.

## Installation

Stats v0.3.2 is verified with Unity 2022.3.62f3 and Unity 6000.5.0f1.

Tags v0.2.1 adds the runtime integration contract required by Stats. Stats v0.3.2 validates that contract instead of accepting the incompatible Tags v0.2.0 package. Stats R3 v0.3.0 remains compatible with Core v0.3.2.

1. Download and import [Klrpxy Gameplay Tags v0.2.1](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.2.1/Klrpxy.Gameplay.Tags.0.2.1.unitypackage).
2. Download and import [Klrpxy Gameplay Stats v0.3.2](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.3.2/Klrpxy.Gameplay.Stats.0.3.2.unitypackage).

The Stats package does not copy the Tags DLL. Install Tags before Stats.

### Optional R3 Adapter

Core Stats does not require R3. Follow these steps only when gameplay needs reactive values, conditions, or observation APIs:

1. Install R3 1.3.1 by following the [official R3 Unity instructions](https://github.com/Cysharp/R3#unity).
2. Import [Klrpxy Gameplay Stats R3 v0.3.0](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.3.0/Klrpxy.Gameplay.Stats.R3.0.3.0.unitypackage).

The adapter does not bundle R3 DLLs and does not make Core Stats depend on R3.

## Quick start

### 1. Declare a StatSet and use generated keys

Declare properties directly in a `partial StatSet`. `Stat` is a scalar, `RangeStat` is a range, and `Resource` is a current amount that gameplay can spend or restore. The generator creates type-safe keys for `Stat` and `RangeStat` properties.

```csharp
public sealed partial class HeroStats : StatSet
{
    public Stat Power { get; } = new Stat(10f);
    public RangeStat Damage { get; } = new RangeStat(8f, 12f);
    public Resource Shield { get; } = new Resource(20f).WithMinimum(0f);
}
```

### 2. Represent the owning object with a Subject

```csharp
public sealed class Hero : StatSubject<HeroStats>
{
    public Hero() : base(new HeroStats()) { }
}
```

Read `hero.StatSet.Power.FinalValue` for the result of all active rules. Changing a `BaseValue` or a `Resource` automatically updates dependent results.

### 3. Describe rules in gameplay order and manage their lifetime with Source

```csharp
var combat = new ModifierSource();
combat.Modify(hero.StatSet.Power).Add(5f);
combat.Modify(hero.StatSet.Power).AddPercent(20f);

// A dynamic rule updates automatically when its input changes.
combat.Modify(hero.StatSet.Power)
    .Add(hero.StatSet.Shield, shield => shield * 0.5f);

// End combat and remove every effect from this source.
combat.Dispose();
```

Results propagate automatically when a stat or Resource used by a rule changes.

### 4. Declare shared rules with a Group

```csharp
var otherHero = new Hero();
var partyAura = new ModifierSource();
var party = new StatSubjectGroup();
party.Add(hero);
party.Add(otherHero);
partyAura.For(party).Modify(HeroStats.PowerKey).Add(5f);
```

Shared rules follow members as they join or leave. Call `party.Dispose()` when the whole group lifetime ends.

### 5. Update UI from final-value events

```csharp
hero.StatSet.Power.OnFinalValueChanged += (previous, current) =>
    powerLabel.text = current.ToString();
```

Related dependencies have finished propagating when the event runs, so the callback can read other final values for the same UI update.

## Concepts to remember

- `StatSet`: declares a set of stats and resources.
- Generated keys: target a stat in a heterogeneous Group with compile-time safety.
- `StatSubject` / `StatSubjectGroup`: define a subject and the scope of shared rules.
- `ModifierSource`: declares numeric rules and owns the lifetime of a batch of effects.
- `Resource`: changes through `Set`, `Increase`, and `Decrease`.
- Final-value events: notify UI or gameplay code after results are stable.

`ModifierHandle` is an advanced escape hatch. Keep and `Dispose` one only when a single rule must end before the rest of its Source; most gameplay code only needs to manage the Source.

The Core package supports fixed and single-input dynamic rules without R3. Install the optional `Klrpxy.Gameplay.Stats.R3` adapter when a rule needs an R3 observable value or condition, or when UI code should observe `FinalValue` through R3.

See [`samples/Stats/BazaarGameplay.cs`](../../samples/Stats/BazaarGameplay.cs) for the Core Bazaar-style example with a Hero, several Item types, a heterogeneous Board, a Tag-filtered aura, temporary effects, combat growth, dynamic input, UI events, and lifetime cleanup. Its optional [`BazaarGameplay.R3.cs`](../../samples/Stats/BazaarGameplay.R3.cs) companion adds an R3 dynamic value, condition, and final-value observation; the Unity R3 smoke runs both contracts as one vertical scenario.
