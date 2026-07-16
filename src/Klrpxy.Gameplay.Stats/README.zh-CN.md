# Klrpxy Gameplay Stats

[English](README.md)

Klrpxy Gameplay Stats 用少量领域概念表达角色属性、物品光环、战斗成长和资源变化。声明规则后，依赖传播、Group 成员变化、Tag 条件和生命周期清理由系统完成。

## 安装

Stats v0.3.2 已在 Unity 2022.3.62f3 和 Unity 6000.5.0f1 中完成验证。

Tags v0.2.1 补齐 Stats 所需的运行时集成契约；Stats v0.3.2 会验证实际契约，不再把不兼容的 Tags v0.2.0 误判为可用。Stats R3 v0.3.0 与 Core v0.3.2 兼容。

1. 下载并导入 [Klrpxy Gameplay Tags v0.2.1](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.2.1/Klrpxy.Gameplay.Tags.0.2.1.unitypackage)。
2. 下载并导入 [Klrpxy Gameplay Stats v0.3.2](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.3.2/Klrpxy.Gameplay.Stats.0.3.2.unitypackage)。

Stats 安装包不会复制 Tags DLL。请先安装 Tags，再安装 Stats。

### 可选 R3 Adapter

Core Stats 无需 R3。只有需要响应式动态值、条件或观察 API 时才执行以下步骤：

1. 按 [R3 官方 Unity 安装说明](https://github.com/Cysharp/R3#unity)安装 R3 1.3.1。
2. 导入 [Klrpxy Gameplay Stats R3 v0.3.0](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.3.0/Klrpxy.Gameplay.Stats.R3.0.3.0.unitypackage)。

R3 Adapter 不捆绑 R3 DLL，也不会让 Core Stats 依赖 R3。

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

### 3. 按玩法语序描述规则，用 Source 管理持续时间

```csharp
var combat = new ModifierSource();
combat.Modify(hero.StatSet.Power).Add(5f);
combat.Modify(hero.StatSet.Power).AddPercent(20f);

// 输入变化时，动态规则会自动更新。
combat.Modify(hero.StatSet.Power)
    .Add(hero.StatSet.Shield, shield => shield * 0.5f);

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
partyAura.For(party).Modify(HeroStats.PowerKey).Add(5f);
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
- 生成的 Key：在异构 Group 中类型安全地指向属性。
- `StatSubject` / `StatSubjectGroup`：表示拥有者与共享规则范围。
- `ModifierSource`：声明数值规则，并表示一批效果的生命周期。
- `Resource`：直接执行 `Set`、`Increase`、`Decrease`。
- 最终值事件：在结果稳定后通知 UI 或玩法代码。

`ModifierHandle` 属于高级用法，只在需要提前结束一条规则，而不是结束整个 Source 时保留并 `Dispose`。通常只管理 Source 即可。

Core 包无需 R3 即可表达固定值和单输入动态规则。只有规则需要 R3 可观察值、可观察条件，或 UI 需要通过 R3 观察 `FinalValue` 时，才安装可选的 `Klrpxy.Gameplay.Stats.R3` Adapter。

完整的 Bazaar 风格 Core 案例见 [`samples/Stats/BazaarGameplay.cs`](../../samples/Stats/BazaarGameplay.cs)。它覆盖 Hero、多种 Item、异构 Board、Tag 条件光环、临时效果、战斗成长、动态输入、UI 事件和生命周期清理；可选 companion [`BazaarGameplay.R3.cs`](../../samples/Stats/BazaarGameplay.R3.cs) 增加 R3 动态值、条件和最终值观察，Unity R3 烟测会把两个合约作为同一条纵向场景运行。
