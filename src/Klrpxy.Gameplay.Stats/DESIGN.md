# Stats 设计记录

> 状态：讨论中。本文记录已经确认的设计，并把尚未确认的事项单独列在末尾；它不是最终实现规格。

## 文档职责

- [`CONTEXT.md`](CONTEXT.md) 只解释 Stats 领域术语。
- 本文描述 Stats 模块准备提供的 interface、行为规则和重要实现约束。
- [`docs/adr/`](../../docs/adr/) 只记录难以反转、存在明显取舍的架构决定。

## 期望的使用方式

下面的伪代码集中展示当前设计方向。具体构造函数和事件类型仍可能在后续讨论中调整。

```csharp
public abstract partial class CharacterStatSet : StatSet
{
    public Stat Health { get; }
    public Stat Attack { get; }

    protected CharacterStatSet(float health, float attack)
    {
        Health = new Stat(
            baseValue: health,
            rounding: RoundingMode.Floor);
        Attack = new Stat(attack);
    }
}

public sealed partial class EnemyStatSet : CharacterStatSet
{
    public RangeStat Damage { get; }

    public EnemyStatSet(float health, float attack)
        : base(health, attack)
    {
        Damage = new RangeStat(10f, 15f);
    }
}

public sealed class EnemyInstance : StatsOwner<EnemyStatSet>
{
    public EnemyInstance()
        : base(
            statSet: new EnemyStatSet(100f, 10f),
            initialTags: new[] { Tags.Unit.Enemy })
    {
    }
}
```

Source Generator 为声明的属性生成 Key：

```csharp
CharacterStatSet.HealthKey;
CharacterStatSet.AttackKey;
EnemyStatSet.DamageKey;
```

Modifier 可以直接添加到 Owner，也可以在已有 Stat 实例时省略目标 Key：

```csharp
var source = new ModifierSource();

ModifierHandle attackHandle = enemy.AddModifier(
    Modifier.Percent(20f, CharacterStatSet.AttackKey),
    source);

ModifierHandle healthHandle = enemy.StatSet.Health.AddModifier(
    Modifier.Flat(5f),
    source);
```

Group 可以向不同类型的 Owner 提供共享 Modifier：

```csharp
var battle = new StatsOwnerGroup();
battle.Add(player);
battle.Add(enemy);

battle.AddModifier(
    Modifier
        .Percent(20f, CharacterStatSet.AttackKey)
        .WhenTargetMatches(TagQuery.Has(Tags.Unit.Ally)),
    passiveSource);
```

## 对象与归属

### StatsOwner 与 StatSet

`StatsOwner<TStatSet>` 恰好拥有一个具体 StatSet 和一个对象级 TagSet。Tag 描述 Gameplay 对象本身，不描述某一个 Stat。

Gameplay 对象是纯 C# 对象，可以直接继承 StatsOwner：

```csharp
public sealed class EnemyInstance : StatsOwner<EnemyStatSet>
{
}
```

MonoBehaviour 主要作为视图层 Adapter，观察并展示 Gameplay Model；Stats 核心与 Gameplay Model 不依赖 Unity API。这项决定记录在 [ADR 0002](../../docs/adr/0002-separate-gameplay-model-from-unity-views.md)。

派生对象通过基类构造函数提供 StatSet 和初始 Tag。基类负责：

1. 保存唯一 StatSet。
2. 创建并初始化 TagSet。
3. 把 `StatSet.Owner` 一次性绑定到自身。

`StatSet.Owner` 对外只读且不能更换。已经归属一个 Owner 的 StatSet 不能再交给另一个 Owner。

### Stat 的对象身份

StatSet 通过公开只读属性声明 Stat：

```csharp
public partial class EnemyStatSet : StatSet
{
    public Stat Health { get; }
    public Stat Attack { get; }
}
```

StatSet 创建后不能替换 Stat 实例，但可以修改 Stat 的 BaseValue、添加或移除 Modifier，以及观察 FinalValue 变化。稳定的对象身份让 StatKey、Modifier 注册和依赖关系始终指向同一个 Stat。

## StatSet 声明与代码生成

### 强类型 StatSet

玩法代码通过具体 C# 类型声明 StatSet。Source Generator 扫描公开只读的 `Stat` 与 `RangeStat` 属性，并生成对应 StatKey。这项架构选择记录在 [ADR 0001](../../docs/adr/0001-strongly-typed-statsets-with-generated-keys.md)。

生成的 Key 直接位于声明它的 StatSet 类型上，不生成中间 `Keys` 类型：

```csharp
EnemyStatSet.HealthKey;
EnemyStatSet.AttackKey;
```

`StatKey.GetPath()` 返回用于日志、诊断和配置定位的路径，与 Tags 的 `Tag.GetPath()` 保持一致。

### 共享属性

不同具体 StatSet 需要共享同一个属性目标时，把属性声明在共同的 StatSet 基类中：

```csharp
public abstract partial class CharacterStatSet : StatSet
{
    public Stat Health { get; }
    public Stat Attack { get; }
}

public sealed partial class PlayerStatSet : CharacterStatSet
{
    public Stat Mana { get; }
}

public sealed partial class EnemyStatSet : CharacterStatSet
{
    public Stat Rage { get; }
}
```

生成器为共同声明生成共享 Key：

```csharp
CharacterStatSet.HealthKey;
CharacterStatSet.AttackKey;
```

派生 StatSet 继承这些 Stat 与 StatKey。一个 Group Modifier 因而可以通过共享 Key 作用于所有兼容成员。

### StatKey 的稳定范围

第一版的 StatKey 只保证在当前构建内稳定。`GetPath()` 不是玩家存档或长期外部协议中的永久 ID。

ScriptableObject、JSON 或表格配置可以引用当前 StatKey；重命名或删除 Stat 后，由游戏设计者重新配置失效数据。编辑器或构建验证应发现无法解析的引用，避免坏配置进入发布版本。

如果未来确实需要跨版本持久化，再增加显式 `StatPath` 与迁移别名。

## 单值 Stat

### 数值类型和整数语义

第一版的 BaseValue、中间值和 FinalValue 统一使用 `float`，不引入 `Stat<int>` 与 `Stat<float>` 两套计算系统。

需要整数语义的 Stat 配置固定 RoundingRule：

```csharp
var health = new Stat(
    baseValue: 100f,
    rounding: RoundingMode.Floor);
```

FinalValue 的类型仍是 `float`，但结果保证为整数值，例如 `87f`。RoundingRule 是 Stat 自身的最终规则，不是可移除的 Modifier。

### 固有边界

普通 Stat 可以受固定值或另一个 Stat 的动态边界约束：

```csharp
var maxHealth = new Stat(100f);
var health = new Stat(100f).BoundedBy(0f, maxHealth);
```

这是一个受约束的标量，不是 RangeStat。Stat 固有边界永久生效，不能作为 Modifier 移除。

整数 Stat 的边界会先转换为合法整数范围：下限向上取整，上限向下取整。

## RangeStat

### 含义

RangeStat 表示最小可能值和最大可能值组成的区间，例如伤害范围：

```csharp
var damage = new RangeStat(10f, 15f);

FloatRange baseRange = damage.BaseRange;
FloatRange finalRange = damage.FinalRange;
```

RangeStat 不表示 `CurrentHealth / MaxHealth`，也不负责随机采样。战斗逻辑从 FinalRange 中自行采样。

### Modifier 计算

Min 和 Max 分别运行同一套标量计算。`Flat / Percent / Multiply` 只接受一个标量，并同时作用于两个端点：

```text
BaseRange = [10, 20]
Flat = 5
Percent = 50%
Multiply = 2

FinalMin = (10 + 5) x 1.5 x 2 = 45
FinalMax = (20 + 5) x 1.5 x 2 = 75
FinalRange = [45, 75]
```

第一版不支持分别修改 Min 和 Max 的范围修饰值。Override 接受完整 FloatRange；所有端点计算完成后自动重新排序，保证 `Min <= Max`。

### RoundingRule 与 Clamp

RangeStat 可以配置一个 RoundingRule，并把同一规则分别应用到 Min 和 Max：

```text
Floor：   [10.2, 20.8] -> [10, 20]
Ceiling： [10.2, 20.8] -> [11, 21]
Nearest： [10.2, 20.8] -> [10, 21]
None：    [10.2, 20.8] -> [10.2, 20.8]
```

Clamp 分别钳制两个端点，并允许区间退化成一个确定值：

```text
[10, 20] Clamp 到 [12, 18] -> [12, 18]
[10, 20] Clamp 到 [30, 40] -> [30, 30]
[50, 60] Clamp 到 [30, 40] -> [40, 40]
```

Override 产生的区间同样必须经过 RoundingRule、Clamp 和固有边界。

## Modifier

### 固定运算类型

第一版只支持固定运算类型：

```text
Flat
Percent
Multiply
Override
Clamp
```

调用者可以计算 Modifier 提供的数值，但不能插入新的计算阶段。RoundingRule 属于 Stat，不是 Modifier 类型。

### Flat、Percent 与 Multiply

三个算术阶段按以下公式组合：

```text
ArithmeticValue =
    (BaseValue + 所有 Flat 之和)
    x (1 + 所有 Percent 之和)
    x 所有 Multiply 之积
```

`Modifier.Percent(20f)` 表示增加 `20%`。多个 Percent 相加，多个 Multiply 相乘：

```text
BaseValue = 100
Flat = 10
Percent = 20% + 30%
Multiply = 2

(100 + 10) x (1 + 0.2 + 0.3) x 2 = 330
```

### Override 与 Clamp

多个 Override 可以共存。优先级最高者生效；优先级相同时，最后添加者生效。移除胜出者后，自动回退到下一个有效 Override。

Clamp 可以是永久的 Stat 固有边界，也可以是可移除的临时 Modifier。多个临时 Clamp 有交集时共同收紧；没有交集时，优先级较高者生效，优先级相同时最后添加者生效。Stat 固有边界始终拥有最终决定权。

Override 与 Clamp 的默认优先级均为 `0`。数值越大，优先级越高，并且允许负数；`9999` 这类高值由特殊玩法显式指定。

### 单值 Stat 的完整计算顺序

```text
1. 读取所有 ModifierValue
2. BaseValue + 所有 Flat 之和
3. 乘以 1 + 所有 Percent 之和
4. 乘以所有 Multiply 之积
5. 使用胜出的 Override 替换前面结果（如果存在）
6. 应用 RoundingRule
7. 应用临时 Clamp Modifier
8. 应用 Stat 固有边界
9. 得到 FinalValue
10. FinalValue 变化时派发 OnFinalValueChanged
```

Override 只替换算术结果，不能绕过取整、临时 Clamp 或 Stat 固有边界。

## 动态 ModifierValue

### ValueInput

DynamicModifierValue 从一至三个显式 ValueInput 计算数值：

```csharp
ModifierValue value = ModifierValue.From(
    ValueInput.Final(strength),
    ValueInput.External(comboCount),
    (strengthValue, combo) =>
        strengthValue * 2f + combo * 5f);
```

支持三种输入：

```csharp
ValueInput.Base(level);
ValueInput.Final(attack);
ValueInput.External(comboCount);
```

外部输入必须是可观察数值，并在变化时通知 Stats 模块。任一输入变化都会重新计算目标 Stat；Modifier 注册移除时，系统自动取消订阅。

### 依赖与循环

BaseValue 输入不形成 FinalValue 计算环。FinalValue 输入进入 Stat 依赖图，所有 FinalValue 依赖必须保持无环；添加会形成循环的 Modifier 时立即拒绝。

外部输入默认不参与 Stat 循环检查，因此不能在实现内部隐藏地读取目标 Stat。

## Modifier 生命周期

### Modifier、ModifierHandle 与 ModifierSource

Modifier 是不可变的计算规则；把它直接添加到一个目标后，会产生一个 ModifierHandle：

```csharp
Modifier modifier = Modifier.Flat(
    10f,
    EnemyStatSet.AttackKey);

ModifierHandle handle = enemy.AddModifier(
    modifier,
    source);
```

同一个 Modifier 添加到三个目标，会产生三个不同的 ModifierHandle。ModifierSource 表示产生这些 Modifier 的同一个玩法来源，例如装备、技能、Buff 或光环实例。

### 清理规则

ModifierHandle 和 ModifierSource 都实现 `IDisposable`，重复清理安全：

```text
ModifierHandle.Dispose()
    移除一个直接 Modifier 注册

ModifierSource.RemoveAllModifiers()
    移除当前全部 Modifier，但 Source 之后仍可复用

ModifierSource.Dispose()
    移除当前全部 Modifier并永久结束 Source
```

Handle 被移除时，同时从目标和 Source 注销，并取消动态 ValueInput 订阅。已经 Dispose 的 Source 不能再用于添加 Modifier；尝试使用时抛出 `ObjectDisposedException`。

## Tag 条件

### TagQuery

Modifier 通过 Tags 模块现有的 TagQuery 声明目标条件：

```csharp
Modifier modifier = Modifier
    .Percent(20f, CharacterStatSet.AttackKey)
    .WhenTargetMatches(
        TagQuery.All(
            TagQuery.Has(Tags.Unit.Ally),
            TagQuery.None(Tags.State.Stunned)));
```

条件不满足时，直接 Modifier 注册或 Group Modifier 规则保持存在，但不参与计算。目标 TagSet 变化后立即重新判断；条件启用状态变化时重新计算目标 Stat。

### TagSet 变化通知

Tags 模块的 TagSet 增加 `OnChanged`，只在集合实际发生变化时通知：

```csharp
tags.Add(tag);    // 首次添加时通知
tags.Add(tag);    // 已存在，不通知
tags.Remove(tag); // 实际移除时通知
tags.Remove(tag); // 不存在，不通知
```

StatsOwner 在构造时订阅自己的 TagSet。`AddTag()` 与 `RemoveTag()` 是便捷方法；直接调用 `owner.Tags.Add()` 或 `Remove()` 也能触发条件 Modifier 重新判断。依赖方向保持为 `Stats -> Tags`。

## StatsOwnerGroup

### 异构成员

StatsOwnerGroup 保存不同具体 StatSet 类型的 StatsOwner：

```csharp
var battle = new StatsOwnerGroup();

battle.Add(player);
battle.Add(enemy);
battle.Add(summon);
```

Group Modifier 只应用于拥有目标 StatKey 且满足 TagQuery 的成员；不包含目标 Key 的成员正常跳过。同一个 Owner 在同一个 Group 中最多出现一次。

### 多 Group 归属

一个 Owner 可以同时属于多个 Group：

```text
enemy
├── BattleGroup
├── EnemyTeamGroup
├── NightAreaGroup
└── HardDifficultyGroup
```

不同 Group 提供的 Modifier 是独立贡献，即使引用同一个 Modifier 定义也正常叠加。Owner 离开一个 Group 时，只停止收集该 Group 的 Modifier；其他 Group 和本地 Modifier 不受影响。

### Group Modifier 聚合

Group Modifier 只在 Group 中保存一份，不复制成每个成员的直接注册。计算 Stat 时，Owner 收集自己的 Modifier 和所属 Group 中适用于自己的 Modifier，并按相同阶段统一聚合：

```text
所有本地与 Group Flat
-> 所有本地与 Group Percent
-> 所有本地与 Group Multiply
-> 从全部 Override 中按优先级选择
-> 合并全部 Clamp
```

不会先完整计算 Group 再计算 Owner，因为 Modifier 来源不应改变数学结果。

后续加入的成员自动考虑 Group 当前的 Modifier。成员离开时停止接收 Group 贡献。Group 添加、移除 Modifier 或成员关系变化时，通知受影响的 Owner 重新计算。

## 更新、事件与错误

### 及时更新

添加或移除 Modifier 后，受影响的 Stat 立即重新计算。Stats 模块不提供公开的 `DeferRefresh()`；UI 是否在同一帧合并多次显示更新，由 UI 层决定。

`ModifierSource.RemoveAllModifiers()` 是一个完整操作：先移除该 Source 的全部影响，再让每个受影响的 Stat 根据最终状态更新，避免暴露没有意义的中间状态。

### 变化事件

单值 Stat 与 RangeStat 的事件都携带旧值和新值：

```csharp
stat.OnFinalValueChanged += (previous, current) => { };
rangeStat.OnFinalRangeChanged += (previous, current) => { };
```

系统使用精确相等判断。值没有变化时不派发事件；微小变化不会被 Stats 模块忽略，UI 可以根据自己的显示精度过滤。

### 非法数值

BaseValue、ModifierValue 和计算结果都不允许 NaN 或正负无穷；遇到非有限值时抛出明确异常。

## 性能约束

Owner 的直接 Modifier 与 Group Modifier 都按 StatKey 建索引。FinalValue 保存已计算结果；只有 Modifier、Tag、成员关系或动态输入变化时才重新计算，不在每帧或每次读取时扫描 Group。

Group 变化时遍历可能受影响的成员，并通过目标 Key 与 TagQuery 过滤。第一版不缓存本地与多个 Group 的合并 Modifier 列表；如果基准测试发现瓶颈，可以在不改变外部 interface 的前提下增加内部缓存。

## 仍待讨论

- `StatsOwnerGroup.AddModifier()` 返回什么类型，以及 Group 规则如何被 ModifierSource 跟踪和清理。
- 本地与多个 Group 中优先级相同的 Override/Clamp，如何定义统一的“最后添加”顺序。
- TagSet.OnChanged 的事件参数。
- Stats 模块是否明确限定为单线程使用。
