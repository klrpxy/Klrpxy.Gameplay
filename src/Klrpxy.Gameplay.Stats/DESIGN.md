# Stats 设计记录

> 状态：原型验证完成，待正式实现。本文记录已经确认的设计；具体命名和内部数据结构可以在不改变这些行为契约的前提下调整。

## 文档职责

- [`CONTEXT.md`](CONTEXT.md) 只解释 Stats 领域术语。
- 本文描述 Stats 模块准备提供的 interface、行为规则和重要实现约束。
- [`docs/adr/`](../../docs/adr/) 只记录难以反转、存在明显取舍的架构决定。

## 期望的使用方式

下面的伪代码集中展示当前设计方向。具体构造函数和事件类型仍可能在后续讨论中调整。

```csharp
public abstract partial class CharacterStatSet : StatSet
{
    public Stat MaxHealth { get; }
    public Resource Health { get; }
    public Stat Attack { get; }

    protected CharacterStatSet(
        float maxHealth,
        float health,
        float attack)
    {
        MaxHealth = new Stat(maxHealth);
        Health = new Resource(health);
        Attack = new Stat(attack);
    }
}

public sealed partial class EnemyStatSet : CharacterStatSet
{
    public RangeStat Damage { get; }

    public EnemyStatSet(
        float maxHealth,
        float health,
        float attack)
        : base(maxHealth, health, attack)
    {
        Damage = new RangeStat(10f, 15f);
    }
}

public sealed class EnemyInstance : StatSubject<EnemyStatSet>
{
    public EnemyInstance()
        : base(
            statSet: new EnemyStatSet(100f, 100f, 10f),
            initialTags: new[] { Tags.Unit.Enemy })
    {
    }
}
```

Source Generator 为声明的属性生成 Key：

```csharp
CharacterStatSet.MaxHealthKey;
CharacterStatSet.AttackKey;
EnemyStatSet.DamageKey;
```

单目标规则直接接收 Stat 实例，调用顺序保持为“来源修改目标，再执行运算”：

```csharp
var source = new ModifierSource();

ModifierHandle attackHandle = source
    .Modify(enemy.StatSet.Attack)
    .AddPercent(20f);

enemy.StatSet.Health.Decrease(30f);
enemy.StatSet.Health.Increase(20f);
```

Group 可以向不同类型的 Subject 提供共享 Modifier：

```csharp
var battle = new StatSubjectGroup();
battle.Add(player);
battle.Add(enemy);

passiveSource
    .For(battle)
    .WhereTargetMatches(TagQuery.Has(Tags.Unit.Ally))
    .Modify(CharacterStatSet.AttackKey)
    .AddPercent(20f);
```

## 对象与归属

### StatSubject 与 StatSet

`StatSubject<TStatSet>` 恰好拥有一个具体 StatSet 和一个对象级 TagSet。Tag 描述 Gameplay 对象本身，不描述某一个 Stat。

Gameplay 对象是纯 C# 对象，可以直接继承 StatSubject：

```csharp
public sealed class EnemyInstance : StatSubject<EnemyStatSet>
{
}
```

MonoBehaviour 主要作为视图层 Adapter，观察并展示 Gameplay Model；Stats 核心与 Gameplay Model 不依赖 Unity API。这项决定记录在 [ADR 0002](../../docs/adr/0002-separate-gameplay-model-from-unity-views.md)。

派生对象通过基类构造函数提供 StatSet 和初始 Tag。基类负责：

1. 保存唯一 StatSet。
2. 创建并初始化 TagSet。
3. 把 `StatSet.Subject` 一次性绑定到自身。

`StatSet.Subject` 对外只读且不能更换。已经归属一个 Subject 的 StatSet 不能再交给另一个 Subject。

StatSubject 实现 `IDisposable`，并作为整个属性模型的唯一生命周期入口。Dispose 会让 Subject 退出全部 Group，移除直接 Modifier 注册，取消 TagSet 与 ValueInput 订阅，并从依赖图移除其数值节点；它不会销毁可能仍影响其他对象的 ModifierSource。StatSet、Stat、RangeStat 和 Resource 不单独公开 Dispose。Subject 结束后继续操作其自身或子对象会抛出 `ObjectDisposedException`，Dispose 过程中不派发数值变化事件。

### Stat 的对象身份

StatSet 通过公开只读属性声明 Stat、RangeStat 和 Resource：

```csharp
public partial class EnemyStatSet : StatSet
{
    public Stat MaxHealth { get; }
    public Resource Health { get; }
    public Stat Attack { get; }
}
```

StatSet 创建后不能替换这些实例。Stat 可以修改 BaseValue、添加或移除 Modifier；Resource 通过 Set、Increase 和 Decrease 改变自己的单一 Value。

## StatSet 声明与代码生成

Stats 分为独立的 Runtime 与 Source Generator 程序集。Runtime DLL 包含 `Stat`、`RangeStat`、`Resource`、`StatKey`、Modifier、Subject、Group 和内部传播协调器，并依赖 Tags Runtime；Generator DLL 只依赖 Roslyn，通过符号分析发现 StatSet 并生成引用 Stats Runtime 的源码，生成器自身不加载或调用 Runtime。运行时行为与生成器行为分别测试。这项 module seam 记录在 [ADR 0005](../../docs/adr/0005-separate-stats-runtime-from-generator.md)。

Stats 作为独立 Unity 安装包发布，只包含 Stats Runtime 与 Stats Analyzer，不复制 Tags DLL。使用者必须先安装 README 和发布说明中标注的最低兼容 Tags 版本；当前 `.unitypackage` 无法自动安装依赖，未来切换到 UPM 时再声明正式包依赖。

Stats Analyzer 自身不依赖 Tags Runtime，并在引用 Stats Runtime 的消费者编译开始时检查最低兼容 Tags Runtime 及 Stats 所需集成类型是否存在。未引用 Stats Runtime 的独立 Unity 程序集不参与检查；真正缺少或二进制不兼容时必须产生带稳定编号的明确诊断，直接提示安装所需的 Tags 版本，而不能只让使用者面对泛化的程序集或类型缺失错误。

### 强类型 StatSet

玩法代码通过具体 C# 类型声明 StatSet。任何继承 `StatSet` 的顶层、非泛型 `partial` 类都会被 Source Generator 自动发现，不需要额外的生成特性；类可以位于全局命名空间或普通命名空间。继承 `StatSet` 但缺少 `partial`，或声明为嵌套或泛型类型时产生明确诊断。Source Generator 扫描公开只读的 `Stat` 与 `RangeStat` 属性，并生成对应 StatKey；Resource 不接受 Modifier，因此第一版不为它生成 Key。这项架构选择记录在 [ADR 0001](../../docs/adr/0001-strongly-typed-statsets-with-generated-keys.md)。

生成的 Key 直接位于声明它的 StatSet 类型上，不生成中间 `Keys` 类型：

```csharp
CharacterStatSet.MaxHealthKey;
CharacterStatSet.AttackKey;
```

第一版只为公开、实例级、只读的自动属性生成 Key，属性类型必须是 `Stat` 或 `RangeStat`。带任何 setter、自定义或表达式 getter、`static` 属性和索引器都不能作为 Stat 声明，并产生明确诊断；公开只读的 `Resource` 自动属性合法，但不生成 Key。这保证 StatSet 创建后，每个声明属性始终指向同一个对象。

生成器按单个 StatSet 失败关闭：该类型存在任何无效 Stat 声明或与生成 Key 同名的成员时，报告精确诊断并停止生成该类型自己声明的全部 Key；其他合法 StatSet 继续正常生成。修复该类型的全部诊断后，再一次性恢复其生成结果，避免产生部分可用的 StatSet。

`StatKey.GetPath()` 返回用于日志、诊断和配置定位的完整路径，格式为 `程序集名称::命名空间.StatSet类型.属性名`，例如 `GameAssembly::Game.Combat.CharacterStatSet.Attack`。运行时 Key 身份由声明类型与属性决定，不使用字符串比较；程序集、类型或属性重命名会改变路径，这与 StatKey 只保证当前构建稳定的范围一致。

StatKey 按目标类型泛化：普通 Stat 生成 `StatKey<Stat>`，RangeStat 生成 `StatKey<RangeStat>`。Modifier 工厂据此在编译期拒绝标量与区间目标的错误组合；泛型参数表示目标种类，不表示 Modifier 提供的数值类型。

每个生成的 StatKey 保存一个创建后不可变的强类型 getter，用于从声明该 Key 的 StatSet 或其派生实例取得对应的 `Stat` 或 `RangeStat`。运行时先检查目标 StatSet 是否兼容，再通过 getter 定位实例；不兼容的 StatSet 视为不包含该 Key。getter 只负责定位属性，不参与 Modifier 计算或响应式传播，生成器不为每个 StatSet 发射运行时查找 switch。

除公开 Key 外，生成器还为每个 StatSet 生成不可见的只读成员描述，列出该类型自己声明的 `Stat`、`RangeStat` 和 `Resource` 属性；派生类型的描述与基类描述共同组成完整成员集合。Runtime 在绑定 Subject 时使用这些描述完成验证、Subject 绑定、传播注册和清理，不使用反射。`Resource` 仍不生成公开 Key；未来只有在出现按 Key 操作 Resource 的真实需求时才单独设计 `ResourceKey`。

StatSet 绑定到 StatSubject 时，Runtime 一次性验证完整成员集合：所有成员非空、同一个成员实例没有被多个属性重复引用，并且成员尚未归属其他 Subject。任一检查失败时抛出包含完整属性路径的明确异常；只有全部验证通过后才统一绑定，不能留下部分成员已绑定的状态。

`StatKey<T>` 不提供公开构造函数或公开动态创建方式。生成代码通过 `StatSet` 上受保护的 Key 工厂创建 Key；普通玩法代码不能创建没有真实属性、getter 错误或路径伪造的 Key。派生 StatSet 内部故意调用受保护工厂属于绕过生成器的高级误用，第一版不增加额外机制阻止。

### 共享属性

不同具体 StatSet 需要共享同一个属性目标时，把属性声明在共同的 StatSet 基类中：

```csharp
public abstract partial class CharacterStatSet : StatSet
{
    public Stat MaxHealth { get; }
    public Resource Health { get; }
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
CharacterStatSet.MaxHealthKey;
CharacterStatSet.AttackKey;
```

派生 StatSet 继承这些 Stat 与 StatKey。一个 Group Modifier 因而可以通过共享 Key 作用于所有兼容成员。

Key 只在属性最初声明的 StatSet 类型上生成一次，派生类型不会为继承属性重复生成 Key；运行时连接同时识别继承 Key 与派生类型自己声明的 Key。第一版的 Stat 属性不能使用 `virtual`、`override` 或 `new` 隐藏，否则生成器产生诊断，避免同一属性位置出现多个含义不清的 Key。

### StatKey 的稳定范围

第一版的 StatKey 只保证在当前构建内稳定。`GetPath()` 不是玩家存档或长期外部协议中的永久 ID。

第一版不提供从字符串路径解析 StatKey 的运行时目录。玩法代码直接引用生成的静态 Key，`GetPath()` 只用于日志和诊断；Source Generator 不生成全局 Key 目录。ScriptableObject、JSON 或表格配置 Adapter 等到出现真实需求时再设计。

如果未来确实需要配置解析或跨版本持久化，再增加显式 `StatPath`、Key 目录与迁移别名。

## 单值 Stat

### 数值类型和整数语义

第一版的 BaseValue、中间值和 FinalValue 统一使用 `float`，不引入 `Stat<int>` 与 `Stat<float>` 两套计算系统。

BaseValue 是可写属性。赋值时拒绝 NaN 和无穷，与旧值相同时不做任何工作；值变化后立即同步重算自身以及读取该 BaseValue 或 FinalValue 的依赖项。只有 FinalValue 实际变化时才派发公开事件，第一版不提供公开的 BaseValue 变化事件。

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
var criticalChance = new Stat(25f)
    .WithBounds(0f, 100f);
```

`WithBounds` 声明 Stat 或 RangeStat 的永久合法范围，只限制计算结果，不修改 BaseValue 或 BaseRange，也不能作为规则移除。临时玩法限制通过 `source.Modify(stat).Clamp(minimum, maximum)` 声明。

WithBounds 的 Min 和 Max 统一使用 ValueInput，并提供接收 float 的常量便捷重载。边界输入变化时立即更新目标；FinalValue 和 Resource Value 边界依赖进入统一依赖图与循环检测，BaseValue 输入只在基础值变化时传播更新。

整数 Stat 的边界会先转换为合法整数范围：下限向上取整，上限向下取整。

## Resource

Resource 表示生命、法力、耐力等单一可变值。它没有 BaseValue 和 FinalValue，也不接受 Modifier：

```csharp
public Stat MaxHealth { get; }
public Resource Health { get; }

Health = new Resource(100f);
```

玩法通过明确操作改变 Value：

```csharp
Health.Set(80f);
Health.Decrease(30f);
Health.Increase(20f);
```

Resource 通过 `WithBounds(min, max, policy)` 接受常量、Stat 或外部动态值提供的永久边界。与 Stat 不同，Resource 的边界策略会直接修改唯一的 Value。第一版支持两种策略：`Clamp` 丢弃超出新边界的值且不在边界扩大时恢复；`PreserveRatio` 在边界变化时保持 Resource 的填充比例。默认使用 `Clamp`，不支持需要额外隐藏值的策略。

只需要固定下界而没有上界的 Resource 使用便捷接口表达，不需要伪造一个极大上限：

```csharp
var shield = new Resource(0f)
    .WithMinimum(0f);
```

`WithMinimum(float)` 等价于只有固定下界的 `WithBounds`，默认采用 Clamp；第一版不增加动态 minimum 专用重载，动态下界继续使用完整的 `WithBounds`。

Resource 可以配置 RoundingRule，默认 `None`。每次 Set、Increase、Decrease 或 PreserveRatio 调整都先对候选值取整，再应用 WithBounds，并只在 Value 实际变化时通知观察者。需要逐帧累计小数的 Resource 使用 `None`，或由外部玩法系统累计后再写入。

Resource 可以通过 `ValueInput.Current(resource)` 参与其他 Stat 的动态计算，但 Resource 本身不生成 StatKey，也不能成为 Modifier 的目标。更多依据见 [current resource semantics research](docs/resource-semantics-research.md)。

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

`source.Modify(stat).AddPercent(20f)` 表示增加 `20%`。多个 Percent 相加，多个 Multiply 相乘：

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

每次执行 fluent 终结方法（`Add`、`AddPercent`、`Multiply`、`Override` 或 `Clamp`）时，对应 ModifierHandle 获得一个全局递增且不可变的内部 Order。Priority 相同时，Order 较大者视为最后添加并胜出。Tag 条件启停、Subject 加入已有 Group 或规则暂时不适用都不会改变 Order；只有注册新规则时会产生新 Order。

### 单值 Stat 的完整计算顺序

```text
1. 读取所有固定值或动态输入
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

## 动态规则值

### ValueInput

Core 公开单输入动态规则。调用者把一个 Stat、RangeStat 或 Resource 交给 fluent 终结方法，并用 selector 计算规则值：

```csharp
source.Modify(enemy.StatSet.Attack)
    .Add(strength, strengthValue => strengthValue * 2f);

source.Modify(enemy.StatSet.Attack)
    .Add(health, currentHealth => currentHealth * 0.1f);
```

Stat 默认读取 FinalValue，RangeStat 默认读取 FinalRange，Resource 默认读取当前 Value。输入变化会重新计算目标 Stat；规则移除时，系统自动取消内部订阅。Core 不公开多输入公式构造器：可以线性拆分的玩法使用多条单输入规则，不可拆分的外部组合由可选 R3 Adapter 先组合为 `ReadOnlyReactiveProperty<float>`，再传给 `Add`、`AddPercent`、`Multiply` 或 `Override`。R3 Adapter 也提供 `Where(ReadOnlyReactiveProperty<bool>)` 条件和 `ObserveFinalValue()`、`ObserveFinalRange()`、`ObserveValue()` 观察入口；没有安装 R3 时 Core 不引用 R3。

### 依赖与循环

BaseValue 输入不形成最终值计算环。`FinalValue`、`FinalRange`、`Resource.Current` 与动态边界之间的依赖统一进入内部传播协调器的依赖图，且必须保持无环；添加会形成循环的 Modifier 或动态边界时立即拒绝，并保持原有依赖图和数值不变。

Adapter 输入默认不参与 Stat 循环检查，因此组合外部流时不能隐藏地读取同一个目标 Stat。

## Modifier 生命周期

### Modifier、ModifierHandle 与 ModifierSource

Modifier 是内部不可变计算规则。公开 fluent 终结方法把规则注册到单个 Stat 或 StatSubjectGroup，并产生一个 ModifierHandle：

```csharp
ModifierHandle handle = source
    .Modify(enemy.StatSet.Attack)
    .Add(10f);

ModifierHandle groupHandle = source
    .For(battle)
    .Modify(EnemyStatSet.AttackKey)
    .Add(10f);
```

每次终结调用产生一个 ModifierHandle。直接 Handle 表示一条 Stat 注册；Group Handle 表示 Group 中保存的一条共享规则，不会为每个成员复制 Handle。ModifierSource 表示产生这些规则的同一个玩法来源，例如装备、技能、Buff 或光环实例。

### 清理规则

ModifierHandle 和 ModifierSource 都实现 `IDisposable`，重复清理安全：

```text
ModifierHandle.Dispose()
    移除一次 Subject 注册或一条 Group 规则

ModifierSource.RemoveAllModifiers()
    移除当前全部 Modifier，但 Source 之后仍可复用

ModifierSource.Dispose()
    移除当前全部 Modifier并永久结束 Source
```

Handle 被移除时，同时从挂载目标和 Source 注销，并取消动态 ValueInput 订阅。Group Handle 还会通知当前受影响的成员重新计算。已经 Dispose 的 Source 不能再用于添加 Modifier；尝试使用时抛出 `ObjectDisposedException`。

## Tag 条件

### TagQuery

Group 规则通过 Tags 模块现有的 TagQuery 声明目标条件：

```csharp
source.For(group)
    .WhereTargetMatches(
        TagQuery.All(
            TagQuery.Has(Tags.Unit.Ally),
            TagQuery.None(Tags.State.Stunned)))
    .Modify(CharacterStatSet.AttackKey)
    .AddPercent(20f);
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

事件参数为不可变的 `TagSetChange { Tag, Kind }`，Kind 取 `Added` 或 `Removed`。事件不携带完整集合；未来 Clear 为每个实际移除的 Tag 分别派发 Removed。

StatSubject 在构造时订阅自己的 TagSet。`AddTag()` 与 `RemoveTag()` 是便捷方法；直接调用 `subject.Tags.Add()` 或 `Remove()` 也能触发条件 Modifier 重新判断。依赖方向保持为 `Stats -> Tags`。

## StatSubjectGroup

### 异构成员

StatSubjectGroup 保存不同具体 StatSet 类型的 StatSubject：

```csharp
var battle = new StatSubjectGroup();

battle.Add(player);
battle.Add(enemy);
battle.Add(summon);
```

Group Modifier 只应用于拥有目标 StatKey 且满足 TagQuery 的成员；不包含目标 Key 的成员正常跳过。同一个 Subject 在同一个 Group 中最多出现一次。

### 多 Group 归属

一个 Subject 可以同时属于多个 Group：

```text
enemy
├── BattleGroup
├── EnemyTeamGroup
├── NightAreaGroup
└── HardDifficultyGroup
```

不同 Group 提供的 Modifier 是独立贡献，即使引用同一个 Modifier 定义也正常叠加。Subject 离开一个 Group 时，只停止收集该 Group 的 Modifier；其他 Group 和本地 Modifier 不受影响。

### Group Modifier 聚合

Group Modifier 只在 Group 中保存一份，不复制成每个成员的直接注册。计算 Stat 时，Subject 收集自己的 Modifier 和所属 Group 中适用于自己的 Modifier，并按相同阶段统一聚合：

```text
所有本地与 Group Flat
-> 所有本地与 Group Percent
-> 所有本地与 Group Multiply
-> 从全部 Override 中按优先级选择
-> 合并全部 Clamp
```

不会先完整计算 Group 再计算 Subject，因为 Modifier 来源不应改变数学结果。

后续加入的成员自动考虑 Group 当前的 Modifier。成员离开时停止接收 Group 贡献。Group 添加、移除 Modifier 或成员关系变化时，通知受影响的 Subject 重新计算。

StatSubjectGroup 实现 `IDisposable`。Dispose 会解除所有成员关系、移除全部 Group Modifier 规则、让对应 Handle 从 Source 注销，并通知仍存活的成员重算；Group 不拥有也不会销毁成员 Subject。Group 结束后继续 Add、Remove 或通过 fluent API 注册规则会抛出 `ObjectDisposedException`。

## 更新、事件与错误

### 及时更新

添加或移除 Modifier 后，受影响的 Stat 立即重新计算。所有合法外部 ValueInput 必须在数值变化时通知 Stats，因此第一版不提供公开的 `ForceRefresh()`、`Refresh()`、`Recalculate()`、`BeginBatch()` 或 `DeferRefresh()`；UI 是否在同一帧合并多次显示更新，由 UI 层决定。

`ModifierSource.RemoveAllModifiers()` 是一个完整操作：先移除该 Source 的全部影响，再让每个受影响的 Stat 根据最终状态更新，避免暴露没有意义的中间状态。

### 变化事件

单值 Stat 与 RangeStat 的事件都携带旧值和新值：

```csharp
stat.OnFinalValueChanged += (previous, current) => { };
rangeStat.OnFinalRangeChanged += (previous, current) => { };
```

系统使用精确相等判断。值没有变化时不派发事件；微小变化不会被 Stats 模块忽略，UI 可以根据自己的显示精度过滤。

同一轮传播中，每个 `Stat`、`RangeStat` 或 `Resource` 最多产生一个公开变化事件。事件携带本轮开始前的值和整轮传播完成后的最终值；传播过程中出现的中间值不公开。如果最终值回到本轮开始前的值，则不派发事件。事件回调造成的修改属于新一轮传播，可以再次产生事件。

### 事件传播与重入

Stats 模块内部的传播协调器统一拥有依赖图、循环检测、受影响数值重算、FIFO 事件派发与订阅清理。`Stat`、`RangeStat`、`Resource`、`StatSubject` 和 `StatSubjectGroup` 只报告自身变化并呈现协调器完成传播后的最终状态，不自行协调彼此的更新顺序。这项边界决定记录在 [ADR 0004](../../docs/adr/0004-internal-stats-propagation-coordinator.md)。

每次公开修改操作开启一轮传播，包括修改 `BaseValue`、添加或移除 Modifier、TagSet 变化、Group 成员变化以及外部 `ValueInput` 通知。该操作引起的全部依赖重算和内部步骤属于同一轮；`RemoveAllModifiers()` 与 Group 规则批量清理等完整操作无论内部处理多少项，也只算一轮。事件回调中再次调用公开修改 API 会开启新一轮，并把新事件追加到 FIFO 队尾。最外层修改方法等待全部轮次与事件队列处理完成后才返回；第一版不公开 `BeginBatch()` 或 `DeferRefresh()`。

属性修改和依赖重算保持同步。一次修改发生时，系统先更新数值并完成全部受影响 Stat、RangeStat 和 Resource 的依赖传播，再派发这一轮产生的变化事件。监听者不会观察到只完成了一部分计算的依赖图。

变化事件使用 Stats 模块内部的 FIFO 事件队列串行派发，不允许事件通知互相嵌套。当前事件正在通知监听者时，如果回调再次修改属性，新的数值和依赖项仍然立即更新，但由此产生的事件只追加到队尾；当前事件的全部监听者执行完后，才继续派发新事件。最外层修改方法返回前，队列必须排空。

同一轮传播中彼此没有依赖关系的变化事件，其相对先后顺序不属于公开契约。系统只保证开始派发前整轮相关数值均已更新完成，以及已经进入事件队列的事件按 FIFO 顺序派发；调用者不能依赖两个无依赖 Stat 的事件谁先触发。

变化事件的某个监听者抛出异常时，内部传播协调器捕获并报告该异常，然后继续通知当前事件的其他监听者并排空 FIFO 队列。监听者异常不会回滚已经完成的数值变化，也不会从 Stats 的修改方法继续向外抛出；错误报告入口自身的异常同样不能破坏传播。

每个变化事件开始派发时对当前监听者列表取一次快照。派发期间新增的监听者从下一个事件开始生效；派发期间移除但已包含在快照中的监听者仍会收到当前事件，并从下一个事件开始不再接收。监听者异常不会影响快照中的其他监听者。

Stats 核心通过全局 `StatsDiagnostics.EventExceptionHandler` 报告监听者异常。默认处理器调用 .NET 的 `Trace.TraceError`；Unity Adapter 可以在启动时把它设置为 `exception => Debug.LogException(exception)`，测试也可以临时替换它以验证错误报告。该入口只接收异常，不暴露传播协调器或公开 `StatsContext`。

依赖图的无环检查不能发现事件回调之间的动态反馈。每次最外层修改都有内部事件派发预算；超过预算时，协调器停止派发并清空本轮剩余事件，通过 `StatsDiagnostics` 报告明确错误，但不让异常继续传播到游戏。具体预算是经压力测试确定的内部常量，不属于公开 API。

```text
修改输入
→ 完成这一轮全部依赖重算
→ 将变化事件加入队列
→ 依次通知监听者
→ 回调引起的新变化事件追加到队尾
→ 队列排空后返回
```

事件队列是同一 Gameplay 线程内的内部实现，不是跨线程、跨帧或公开的命令队列。第一版由当前 Gameplay 线程共享同一个内部事件循环，使不同 StatSubject、StatSubjectGroup 和跨 Subject 动态依赖产生的事件也不会互相嵌套。

Stat、RangeStat 和 Resource 的公开构造方式不接收 Dispatcher。Stats 模块也不公开 StatsContext；事件循环依赖由模块内部提供，避免把通知机制暴露给 StatSet 定义。将来只有在确实需要多个隔离的模拟世界或多线程 Stats 模型时，才重新考虑显式 Context。

### 非法数值

BaseValue、动态规则值和计算结果都不允许 NaN 或正负无穷；遇到非有限值时抛出明确异常。

## 性能约束

Subject 的直接 Modifier 与 Group Modifier 都按 StatKey 建索引。FinalValue 保存已计算结果；只有 Modifier、Tag、成员关系或动态输入变化时才重新计算，不在每帧或每次读取时扫描 Group。

Group 变化时遍历可能受影响的成员，并通过目标 Key 与 TagQuery 过滤。第一版不缓存本地与多个 Group 的合并 Modifier 列表；如果基准测试发现瓶颈，可以在不改变外部 interface 的前提下增加内部缓存。

## 线程约束

第一版明确为单线程模型，不提供内部锁。StatSubject、StatSet、Stat、RangeStat、Resource、ModifierSource 和 StatSubjectGroup 的创建与修改，以及外部 ValueInput 的变化通知，都必须发生在同一 Gameplay 线程。对象第一次接入传播系统时记录当前线程；所有构建都会检查后续修改是否来自同一线程，错误线程调用立即抛出明确异常。该异常表示 API 使用错误，不由 `StatsDiagnostics.EventExceptionHandler` 捕获。

变化与依赖事件在当前 Gameplay 线程串行派发，并在最外层修改方法返回前完成。事件回调造成的新通知进入内部事件队列，不进行嵌套派发。未来如需后台计算，通过不可变快照或命令队列 Adapter 把结果交回 Gameplay 线程应用。

## 原型验证结论

正式实现前的独立原型已经覆盖以下最高层行为接缝：

- Roslyn 消费者编译验证 StatSet 自动发现、继承 Key、强类型 getter、完整路径、成员描述、失败关闭和生成诊断。
- 纯 C# 玩法模型验证 Stat、RangeStat、Resource、Modifier、Source、Handle、Group、Tags 条件、依赖传播、事件重入、循环拒绝、线程限制与原子失败。
- 《The Bazaar》式纵向案例验证玩法代码只声明关系和生命周期，不需要手动刷新、Group 遍历、旧值恢复或内部传播接口。
- Unity 2022.3 与 Unity 6 的隔离项目验证 Tags 先导入、Stats 后导入的安装流程，Runtime 与 Analyzer 的独立加载、默认游戏程序集中的源码生成，以及缺少 Tags 时的明确诊断。

这些原型验证的是行为契约和程序集 seam，不是可复制的生产实现。正式代码必须重新实现并由自动测试保护；实验用成功诊断、一次性脚本、简化路径和临时类型不进入发布包。

当前没有尚未确认、会改变公开使用模型的设计问题。实现阶段可以自行决定不影响上述契约的内部命名、数据结构和优化方式。
