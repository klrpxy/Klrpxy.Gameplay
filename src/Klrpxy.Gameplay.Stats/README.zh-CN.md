# Klrpxy Gameplay Stats

[English](README.md)

Klrpxy Gameplay Stats 用少量领域概念表达角色属性、物品光环、战斗成长和资源变化。声明规则后，依赖传播、Group 成员变化、Tag 条件和生命周期清理由系统完成。

## 快速开始

### 1. 声明 StatSet，使用生成的 Key

在 `partial StatSet` 中直接声明属性。`Stat` 表示单值属性，`RangeStat` 表示区间，`Resource` 表示会被消耗或补充的当前量。生成器会为 `Stat` 和 `RangeStat` 生成类型安全的 Key。

```csharp
public sealed partial class HeroStats : StatSet
{
    public Stat Power { get; } = new Stat(10f);
    public RangeStat Damage { get; } = new RangeStat(8f, 12f);
    public Resource Shield { get; } = new Resource(20f).WithMinimum(0f);
}
```

### 2. 用 Subject 表示拥有属性的对象

```csharp
public sealed class Hero : StatSubject<HeroStats>
{
    public Hero() : base(new HeroStats()) { }
}
```

读取 `hero.StatSet.Power.FinalValue` 即可得到包含全部规则后的结果。修改 `BaseValue` 或 `Resource` 时，相关结果会自动更新。

### 3. 用 Modifier 描述规则，用 Source 管理持续时间

```csharp
var combat = new ModifierSource();
hero.AddModifier(Modifier.Flat(5f, HeroStats.PowerKey), combat);

// 战斗结束：一次移除该战斗来源的全部效果。
combat.Dispose();
```

规则依赖的属性或 Resource 变化时，相关结果会自动传播。

### 4. 用 Group 声明共享规则

```csharp
var otherHero = new Hero();
var partyAura = new ModifierSource();
var party = new StatSubjectGroup();
party.Add(hero);
party.Add(otherHero);
party.AddModifier(Modifier.Flat(5f, HeroStats.PowerKey), partyAura);
```

成员加入或离开时，共享规则会自动应用或撤销。结束整组规则时调用 `party.Dispose()`。

### 5. 订阅最终事件更新 UI

```csharp
hero.StatSet.Power.OnFinalValueChanged += (previous, current) =>
    powerLabel.text = current.ToString();
```

事件触发时，本轮相关依赖已经传播完成；回调可以读取其他最终属性更新 UI。

## 需要记住的概念

- `StatSet`：声明一组属性和资源。
- 生成的 Key：让 `Modifier` 类型安全地指向属性。
- `StatSubject` / `StatSubjectGroup`：表示拥有者与共享规则范围。
- `Modifier`：描述数值规则。
- `ModifierSource`：表示一批效果的生命周期。
- `Resource`：直接执行 `Set`、`Increase`、`Decrease`。
- 最终值事件：在结果稳定后通知 UI 或玩法代码。

`ModifierHandle` 属于高级用法，只在需要提前结束一条 Modifier，而不是结束整个 Source 时保留并 `Dispose`。通常只管理 Source 即可。

完整的 Bazaar 风格案例见 [`samples/Stats/BazaarGameplay.cs`](../../samples/Stats/BazaarGameplay.cs)。它覆盖 Hero、多种 Item、异构 Board、Tag 条件光环、临时效果、战斗成长、动态外部输入、UI 事件和生命周期清理。
