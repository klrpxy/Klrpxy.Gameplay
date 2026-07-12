# Stats 设计记录

> 状态：讨论中。本文只记录已经确认的设计，以及明确标出的待讨论事项；它不是最终实现规格。

## 如何阅读本文

- `CONTEXT.md` 解释领域术语，例如 Stat、StatsOwner 和 ModifierSource。
- 本文记录这些概念准备如何协作，并用伪代码展示期望的使用方式。
- `docs/adr/` 只记录难以反转、存在明显取舍的架构决策。

## 已确认的设计

### 1. 强类型 StatSet 与自动生成的 StatKey

玩法代码通过具体 C# 类型声明属性集：

```csharp
public partial class EnemyStatSet : StatSet
{
    public Stat Health;
    public Stat Attack;
}
```

Source Generator 为每个声明的 Stat 生成稳定的 `StatKey`。Modifier 使用 `StatKey` 指定目标，不使用裸字符串：

```csharp
// 自动生成，具体语法仍可调整
public partial class EnemyStatSet
{
    public static StatKey HealthKey { get; }
    public static StatKey AttackKey { get; }
}
```

StatKey 通过 `GetPath()` 返回用于显示和诊断的路径，与 Tags 中 `Tag.GetPath()` 的接口保持一致。

这项架构选择记录在 [`docs/adr/0001-strongly-typed-statsets-with-generated-keys.md`](../../docs/adr/0001-strongly-typed-statsets-with-generated-keys.md)。

### 2. StatsOwner 恰好拥有一个 StatSet 和一个 TagSet

Tag 描述游戏对象本身，而不是某一个 Stat。`StatsOwner<TStatSet>` 恰好拥有一个具体 StatSet，并持有对象级 TagSet：

```csharp
StatsOwner<EnemyStatSet> boss;

EnemyStatSet stats = boss.StatSet;
TagSet tags = boss.Tags;
```

StatSet 保有其 StatsOwner 的只读引用，归属建立后不能更换。只有 Tag、Stat 数值和 Modifier 会在运行时变化。

### 3. Stat 统一使用 float 计算

第一版不引入 `Stat<int>` 和 `Stat<float>` 两套计算系统。所有 BaseValue、中间值和 FinalValue 都使用 `float`。

需要整数语义的 Stat 配置固定的 RoundingRule：

```csharp
var health = new Stat(
    baseValue: 100f,
    rounding: RoundingMode.Floor);
```

Health 的 FinalValue 类型仍是 `float`，但结果保证是整数值，例如 `87f`。取整是 Stat 自身的最终规则，不是可随意移除的 Modifier。

### 4. 普通 Stat 可以受动态上下限约束

当前生命值这类数据仍是一个标量，而不是一个范围：

```csharp
var maxHealth = new Stat(100f);
var health = new Stat(100f).BoundedBy(0f, maxHealth);
```

`Health / MaxHealth` 与真正的范围属性是两个不同概念。

### 5. RangeStat 表示可能值区间

RangeStat 用于伤害范围等 `[最小值, 最大值]` 数据：

```csharp
var damage = new RangeStat(10f, 15f);

FloatRange baseRange = damage.BaseRange;
FloatRange finalRange = damage.FinalRange;
```

RangeStat 不表示 `CurrentHealth / MaxHealth`，也不负责随机采样。战斗逻辑可以从 FinalRange 中自行采样。

FinalRange 变化时可以通过 `OnFinalRangeChanged` 观察。

### 6. Modifier 与 Modifier 注册是两个概念

Modifier 是不可变的计算规则。把它添加到一个目标后，会产生一次可移除的注册：

```csharp
Modifier modifier = Modifier.Flat(
    10f,
    EnemyStatSet.AttackKey);

ModifierHandle handle = target.AddModifier(
    modifier,
    source);
```

同一个 Modifier 添加到三个目标，会产生三个不同的 ModifierHandle。

### 7. ModifierSource 可以跨目标移除 Modifier

ModifierSource 表示产生 Modifier 的同一个玩法来源，例如一件装备、一次技能、一层 Buff 或一个光环实例。

```csharp
var source = new ModifierSource();

enemy01.AddModifier(modifier1, source);
enemy02.AddModifier(modifier2, source);

source.RemoveAllModifiers();
```

Source 不理解具体玩法对象，但会在内部跟踪自己的 ModifierHandle，因此可以移除它添加到不同 StatsOwner 上的全部 Modifier。

### 8. 动态 Modifier 必须明确选择 BaseValue 或 FinalValue

动态修饰值可以依赖另一个 Stat，但调用者必须明确依赖来源：

```csharp
ModifierValue.FromBase(attack, value => value * 10f);
ModifierValue.FromFinal(attack, value => value * 10f);
```

读取 BaseValue 不建立计算依赖。读取 FinalValue 会建立依赖关系；来源变化时，目标 Stat 自动重新计算。

FinalValue 依赖必须保持无环。添加一个会形成循环的 Modifier 时，系统立即拒绝：

```text
Attack -> Defence -> Attack  // 不允许
```

### 9. 第一版只支持固定 Modifier 运算类型

第一版不允许调用者插入自定义计算阶段。自定义表达式只能决定 Modifier 提供的数值，不能改变这份数值进入管线的方式。

目前确认的基础类型包括：

```text
Flat
Percent
Multiply
Override
Clamp
```

RoundingRule 属于 Stat，不是普通 Modifier 类型。

### 10. Flat、Percent 和 Multiply 的计算语义

前三个阶段按下面的公式组合：

```text
ArithmeticValue =
    (BaseValue + 所有 Flat 之和)
    x (1 + 所有 Percent 之和)
    x 所有 Multiply 之积
```

`Modifier.Percent(20f)` 表示增加 `20%`；多个 Percent 相加。多个 Multiply 相乘。

```text
BaseValue = 100
Flat = 10
Percent = 20% + 30%
Multiply = 2

(100 + 10) x (1 + 0.2 + 0.3) x 2 = 330
```

### 11. 修改后保持 FinalValue 与事件及时更新

添加或移除 Modifier 后，受影响的 Stat 会重新计算。FinalValue 确实变化时，派发 `OnFinalValueChanged`。

Stats 层不提供公开的 `DeferRefresh()` 批处理接口。UI 是否在同一帧合并多次显示更新，由 UI 层决定。

`ModifierSource.RemoveAllModifiers()` 是一个完整操作：它可以先移除所有相关注册，再让每个受影响的 Stat 根据最终状态更新，避免暴露没有意义的中间状态。

### 12. 多个 Override 通过优先级决定结果

多个 Override 可以同时存在。优先级最高的 Override 生效；优先级相同时，最后添加的 Override 生效。

```csharp
Modifier.Override(0f, EnemyStatSet.MoveSpeedKey, priority: 100);
Modifier.Override(3f, EnemyStatSet.MoveSpeedKey, priority: 200);
```

移除当前生效的 Override 后，系统自动使用下一个有效 Override；没有 Override 时，使用正常算术阶段的结果。默认优先级为 `0`。

### 13. 固有边界与临时 Clamp 是两个概念

Stat 固有边界是永久规则，临时 Clamp Modifier 是可以移除的玩法限制：

```csharp
var health = new Stat(100f).BoundedBy(0f, maxHealth);

Modifier.Clamp(
    min: 0f,
    max: 3f,
    target: EnemyStatSet.MoveSpeedKey);
```

多个有效范围正常情况下取交集。Stat 固有边界不随 Modifier 移除；临时 Clamp 可以通过 ModifierHandle 或 ModifierSource 移除。

Clamp 有交集时共同收紧范围；没有交集时，优先级较高的 Clamp 生效，优先级相同时最后添加的 Clamp 生效。Stat 固有边界始终拥有最终决定权。

Override 和 Clamp 的默认优先级都是 `0`。数值越大，优先级越高，并且允许负数；`9999` 这类高值由特殊玩法显式指定。

### 14. 单值 Stat 的计算顺序

单值 Stat 按以下顺序计算：

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

Override 只替换算术结果，不能绕过取整、临时 Clamp 或 Stat 固有边界。动态 ModifierValue 为某个固定运算阶段提供数值，不单独占据一个计算阶段。

整数 Stat 的 Clamp 范围会先转换为合法整数范围：下限向上取整，上限向下取整。

### 15. DynamicModifierValue 可以读取外部动态数值

不是 Stat 的动态玩法数值也可以影响 Modifier，但它必须作为可观察的 ValueInput，在值变化时通知 Stats 系统：

```csharp
Modifier.Flat(
    ModifierValue.From(
        ValueInput.External(comboCount),
        combo => combo * 5f),
    EnemyStatSet.AttackKey);
```

外部输入变化时，依赖它的目标 Stat 自动重新计算。Modifier 注册移除时，系统必须取消对外部输入的订阅。

Stat 输入与外部输入使用同一组明确接口：

```csharp
ValueInput.Base(level);
ValueInput.Final(attack);
ValueInput.External(comboCount);
```

只有 FinalValue 输入参与 Stat 依赖图和循环检查。外部输入不能在实现内部隐藏地读取目标 Stat，否则系统无法发现这种循环。

### 16. DynamicModifierValue 支持一至三个输入

一个 DynamicModifierValue 可以组合一至三个显式 ValueInput：

```csharp
ModifierValue.From(
    ValueInput.Final(strength),
    ValueInput.External(comboCount),
    (strengthValue, combo) =>
        strengthValue * 2f + combo * 5f);
```

任一输入变化都会使目标 Stat 重新计算。所有 FinalValue 输入共同参与循环检测。第一版不提供依赖数组下标的任意数量输入接口。

### 17. ModifierHandle 与 ModifierSource 的生命周期

ModifierHandle 和 ModifierSource 都实现 `IDisposable`，并且重复清理安全：

```text
ModifierHandle.Dispose()
    移除一个 Modifier 注册

ModifierSource.RemoveAllModifiers()
    移除当前全部注册，但 Source 之后仍可复用

ModifierSource.Dispose()
    移除当前全部注册并永久结束 Source
```

Handle 被移除时，同时从目标和 Source 注销，并取消动态 ValueInput 订阅。已经 Dispose 的 Source 不能再用于添加 Modifier；尝试使用时抛出 `ObjectDisposedException`。

### 18. Gameplay 对象继承 StatsOwner，Unity 视图与它分离

拥有属性的 Gameplay 对象是纯 C# 对象，不继承 MonoBehaviour。它可以直接继承 `StatsOwner<TStatSet>`：

```csharp
public sealed class EnemyInstance : StatsOwner<EnemyStatSet>
{
}
```

因此 Gameplay 代码可以直接调用：

```csharp
enemy.AddModifier(modifier, source);
enemy.AddTag(tag);
```

MonoBehaviour 主要作为视图层组件，观察并展示 Gameplay Model 的状态。Stats 核心和 Gameplay Model 都保持对 Unity API 的独立。

### 19. 派生 Gameplay 对象通过基类构造函数提供 StatSet

派生的 Gameplay 对象把唯一 StatSet 与初始 Tag 交给 `StatsOwner<TStatSet>` 的基类构造函数：

```csharp
public sealed class EnemyInstance : StatsOwner<EnemyStatSet>
{
    public EnemyInstance()
        : base(
            statSet: new EnemyStatSet(
                health: 100f,
                attack: 10f),
            initialTags: new[] { Tags.Unit.Enemy })
    {
    }
}
```

基类保存 StatSet、创建 TagSet，并把 `StatSet.Owner` 一次性绑定到自身。StatSet.Owner 对外只读且不能更换；已归属的 StatSet 不能交给第二个 StatsOwner。

### 20. StatSet 使用只读属性声明 Stat

StatSet 通过公开只读属性声明 Stat：

```csharp
public partial class EnemyStatSet : StatSet
{
    public Stat Health { get; }
    public Stat Attack { get; }
}
```

StatSet 创建后不能替换 Stat 实例，但仍可修改 Stat 的 BaseValue、添加 Modifier 和观察变化。Source Generator 扫描这些只读属性并生成对应 StatKey。

### 21. StatKey 使用 Key 后缀，并支持直接向 Stat 添加 Modifier

Source Generator 把 StatKey 直接生成到 StatSet 类型上，不生成中间的 `Keys` 类型：

```csharp
EnemyStatSet.HealthKey;
EnemyStatSet.AttackKey;
```

已有具体 Stat 实例时，可以绕过 StatKey，直接添加 Modifier：

```csharp
enemy.StatSet.Health.AddModifier(
    Modifier.Flat(5f),
    source);
```

创建可复用或跨目标的 Modifier 时仍使用生成的 StatKey：

```csharp
Modifier.Flat(5f, EnemyStatSet.HealthKey);
```

### 22. StatKey 不保证跨版本持久化

第一版的 StatKey 只保证在当前构建内稳定。`GetPath()` 用于代码引用、调试和配置定位，但不是玩家存档或长期外部协议中的永久 ID。

ScriptableObject、JSON 或表格配置可以引用当前 StatKey；重命名或删除 Stat 后，由游戏设计者重新配置失效数据。编辑器或构建验证应发现无法解析的引用，避免坏配置进入发布版本。

如果未来确实需要跨版本持久化，再增加显式 `StatPath` 与迁移别名，不在第一版承担这项成本。

### 23. RangeStat 的标量 Modifier 同时作用于两个端点

RangeStat 的 Min 和 Max 分别运行相同的标量计算。`Flat / Percent / Multiply` 将同一个数值作用于两端：

```text
BaseRange = [10, 20]
Flat = 5
Percent = 50%
Multiply = 2

FinalMin = (10 + 5) x 1.5 x 2 = 45
FinalMax = (20 + 5) x 1.5 x 2 = 75
FinalRange = [45, 75]
```

计算后自动重新排序两个端点，始终保证 `Min <= Max`。RangeStat 的 Override 提供完整 FloatRange，而不是单个标量。

### 24. RangeStat 不支持分别修改两个端点的算术 Modifier

第一版的 `Flat / Percent / Multiply` 只接受一个标量，并把它同时应用到 Min 和 Max。不提供分别修改两个端点的 FloatRange 修饰值。

需要直接替换整个区间时，使用接受 FloatRange 的 Override。只有出现 Modifier 必须改变区间宽度的真实需求后，才考虑端点独立运算。

### 25. RangeStat 对两个端点使用同一个 RoundingRule

RangeStat 可以配置 RoundingRule，并把同一规则分别应用到 Min 和 Max：

```text
Floor：   [10.2, 20.8] -> [10, 20]
Ceiling： [10.2, 20.8] -> [11, 21]
Nearest： [10.2, 20.8] -> [10, 21]
None：    [10.2, 20.8] -> [10.2, 20.8]
```

RangeStat 不负责随机采样。端点取整后再进入 Clamp 与固有边界，最终规范化为 `Min <= Max`。

### 26. RangeStat 的 Clamp 分别作用于两个端点

Clamp 分别钳制 RangeStat 的 Min 和 Max：

```text
[10, 20] Clamp 到 [12, 18] -> [12, 18]
[10, 20] Clamp 到 [30, 40] -> [30, 30]
[50, 60] Clamp 到 [30, 40] -> [40, 40]
```

结果允许退化为 `[x, x]`。多个 Clamp 继续使用已确认的交集与优先级规则；Override 产生的区间也必须经过 Clamp 和 Stat 固有边界。

### 27. FinalValue 变化事件携带旧值和新值

单值 Stat 与 RangeStat 的变化事件都携带旧值和新值：

```csharp
stat.OnFinalValueChanged += (previous, current) => { };
rangeStat.OnFinalRangeChanged += (previous, current) => { };
```

系统使用精确相等判断；值没有变化时不派发事件，微小变化不会被 Stats 层忽略。UI 可以根据自己的显示精度过滤更新。

BaseValue、ModifierValue 和计算结果都不允许 NaN 或正负无穷；遇到非有限值时抛出明确异常。

### 28. StatsOwnerGroup 是异构 Owner 集合

`StatSetGroup` 更名为 `StatsOwnerGroup`。它保存不同具体 StatSet 类型的 StatsOwner：

```csharp
var battle = new StatsOwnerGroup();

battle.Add(player);
battle.Add(enemy);
battle.Add(summon);
```

Group Modifier 只应用于拥有目标 StatKey 且满足 Tag 条件的成员；不包含目标 Key 的成员正常跳过。后续加入的成员执行同样判断，成员离开时移除由该 Group 建立的注册。Owner 自己直接添加的 Modifier 不受 Group 成员关系影响。

### 29. 共同属性声明在共享 StatSet 基类中

不同具体 StatSet 需要共享同一个属性目标时，把属性声明在共同基类中：

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

派生 StatSet 继承这些 Stat 与 StatKey。一个 Group Modifier 可以通过共享 Key 作用于所有兼容成员；没有该 Key 或不满足 Tag 条件的成员正常跳过。

### 30. 条件 Modifier 使用 TagQuery 动态判断目标

Modifier 可以通过现有 Tags 模块的 TagQuery 声明目标条件：

```csharp
Modifier modifier = Modifier
    .Percent(20f, CharacterStatSet.AttackKey)
    .WhenTargetMatches(
        TagQuery.All(
            TagQuery.Has(Tags.Unit.Ally),
            TagQuery.None(Tags.State.Stunned)));
```

条件不满足时，Modifier 注册保持存在但不参与计算。目标 TagSet 变化后立即重新判断；条件变为满足或不满足时，重新计算目标 Stat。该规则同时适用于直接注册和 StatsOwnerGroup 注册。

### 31. TagSet 提供变化通知

Tags 模块的 TagSet 增加 `OnChanged`，并且只在集合实际发生变化时通知：

```csharp
tags.Add(tag);    // 首次添加时通知
tags.Add(tag);    // 已存在，不通知
tags.Remove(tag); // 实际移除时通知
tags.Remove(tag); // 不存在，不通知
```

StatsOwner 在构造时订阅自己的 TagSet。`AddTag()` 与 `RemoveTag()` 是便捷方法；直接调用 `owner.Tags.Add()` 或 `Remove()` 也能触发条件 Modifier 重新判断。Tags 模块不依赖 Stats，依赖方向仍然是 `Stats -> Tags`。

### 32. Group Modifier 不复制到每个 Owner

StatsOwner 保存自己所属的 StatsOwnerGroup。Group Modifier 只在 Group 中保存一份；计算 Stat 时，Owner 收集自己的 Modifier 与所属 Group 中适用于自己的 Modifier，并按相同计算阶段统一聚合。

```text
所有本地与 Group Flat
-> 所有本地与 Group Percent
-> 所有本地与 Group Multiply
-> 从全部 Override 中按优先级选择
-> 合并全部 Clamp
```

不会先完整计算 Group 再计算 Owner，因为 Modifier 来源不应改变数学结果。Group 添加、移除 Modifier或成员关系变化时，通知受影响的 Owner 重新计算，但不创建每个成员专属的内部 ModifierHandle。

### 33. Modifier 按 StatKey 索引，第一版不缓存合并结果

Owner 本地 Modifier 与 Group Modifier 都按 StatKey 建索引。FinalValue 保存已计算结果；只有 Modifier、Tag、成员关系或动态输入变化时才重新计算，不在每帧或每次读取时扫描 Group。

Group 变化时遍历可能受影响的成员，并通过目标 Key 与 TagQuery 过滤。第一版不缓存本地与多个 Group 的合并 Modifier 列表；如果基准测试发现瓶颈，可以在不改变外部接口的前提下增加内部缓存。

## 仍待讨论
