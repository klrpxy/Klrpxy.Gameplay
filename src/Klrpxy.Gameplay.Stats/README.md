# Klrpxy Gameplay Stats

[中文文档](README.zh-CN.md)

Klrpxy Gameplay Stats expresses character attributes, item auras, combat growth, and resources with a small set of gameplay concepts. Dependency propagation, Group membership, Tag conditions, and lifetime cleanup are automatic after rules are declared.

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

### 2. Represent the owning object with an Owner

```csharp
public sealed class Hero : StatsOwner<HeroStats>
{
    public Hero() : base(new HeroStats()) { }
}
```

Read `hero.StatSet.Power.FinalValue` for the result of all active rules. Changing a `BaseValue` or a `Resource` automatically updates dependent results.

### 3. Describe rules with Modifier and lifetimes with Source

```csharp
var combat = new ModifierSource();
hero.AddModifier(Modifier.Flat(5f, HeroStats.PowerKey), combat);

// End combat and remove every effect from this source.
combat.Dispose();
```

Results propagate automatically when a stat or Resource used by a rule changes.

### 4. Declare shared rules with a Group

```csharp
var otherHero = new Hero();
var partyAura = new ModifierSource();
var party = new StatsOwnerGroup();
party.Add(hero);
party.Add(otherHero);
party.AddModifier(Modifier.Flat(5f, HeroStats.PowerKey), partyAura);
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
- Generated keys: target a stat from a `Modifier` with compile-time safety.
- `StatsOwner` / `StatsOwnerGroup`: define an owner and the scope of shared rules.
- `Modifier`: describes a numeric rule.
- `ModifierSource`: owns the lifetime of a batch of effects.
- `Resource`: changes through `Set`, `Increase`, and `Decrease`.
- Final-value events: notify UI or gameplay code after results are stable.

`ModifierHandle` is an advanced escape hatch. Keep and `Dispose` one only when a single Modifier must end before the rest of its Source; most gameplay code only needs to manage the Source.

See [`samples/Stats/BazaarGameplay.cs`](../../samples/Stats/BazaarGameplay.cs) for a complete Bazaar-style example with a Hero, several Item types, a heterogeneous Board, a Tag-filtered aura, temporary effects, combat growth, external input, UI events, and lifetime cleanup.
