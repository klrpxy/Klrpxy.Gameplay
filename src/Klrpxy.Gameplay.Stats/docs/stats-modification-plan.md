# Stats 易用 interface 修改计划

> 状态：设计已收敛，已发布为 PRD #46 并拆分为 #47–#54
> 创建日期：2026-07-14
> 最后更新：2026-07-15
> 适用范围：Stats 的属性声明、Modifier 注册、条件表达、Tags 便捷方法和可选 R3 adapter

## 目标

玩法作者应当按接近自然语言的顺序表达属性规则：

```text
来源 → 作用范围 → 条件 → 目标属性 → 运算 → 数值
```

常见代码应当能够直接读成“狂怒使英雄力量增加 5”“快捷物品使棋盘急速增加 20”。调用者不需要为了正确使用 Stats 记住 Key 定位、Modifier 添加顺序、手动刷新、依赖图重建、条件重新求值、订阅清理或失败回滚。

本计划只改变和补充易用 interface。传播、固定计算阶段、依赖循环检测、事件派发、Group 规则和生命周期等既有行为继续由同一套 implementation 提供。

## 不在本次范围

- 不进行独立的架构深化或无关重构。
- 不把 Tags 改成可选依赖；Stats 继续保持现有 Tags 依赖。
- 不改变 StatSet 与其主体的组合关系，也不让泛型主体自动转发具体 StatSet 的成员。
- 不重新讨论 `StatSet`、`Stat`、`Resource`、`RangeStat` 等其他领域名称。
- 不设计 UPM、`.unitypackage` 或版本分发策略。
- 不开放任意自定义 Modifier 运算；继续使用固定计算阶段。
- 不把 R3 变成 Stats Runtime 的必需依赖。

## 保持不变的行为

- 一个属性主体仍恰好拥有一个 StatSet 和一个用于分类自身的 TagSet；StatSet 不能在主体之间转移。
- 单目标 Modifier 使用实际 `Stat` 定位目标；Group Modifier 使用生成的强类型 `StatKey` 定位不同具体 StatSet 中的兼容成员。
- `ModifierSource` 继续代表同一玩法来源；销毁 Source 会撤销它创建的全部直接注册和 Group 规则。
- `ModifierHandle` 继续代表一条可独立撤销的挂载。
- 一次公开修改继续进入完整 Propagation Round，依赖图全部重算后才发布最终变化。
- FinalValue 依赖继续进行循环检测；注册失败不能留下部分 Modifier、订阅或依赖。
- Group 规则继续影响已有成员和未来加入的兼容成员；成员离开后自动失去影响。
- Tags 变化继续自动重新求值，不要求调用者手动刷新。

## 已确定的 interface

### 1. StatSet 声明

StatSet 继续使用顶层、非泛型 `partial` 类型以及公开只读自动属性：

```csharp
public partial class UnitStats : StatSet
{
    public Stat Haste { get; } = new(0f);
    public RangeStat Attack { get; } = new(5f, 10f);
    public Stat HealthMax { get; } = new(10f);
    public Resource Health { get; } = new(20f);
}
```

表达式属性 `public Stat Haste => new(0f)` 不合法，因为它会在每次读取时创建不同的 Stat。Resource 与 `HealthMax` 之间也不会仅因名称自动建立边界；需要动态上限时必须明确声明。

### 2. Subject 重命名

- `StatsOwner` / `StatsOwner<TStatSet>` 重命名为 `StatSubject` / `StatSubject<TStatSet>`。
- `StatsOwnerGroup` 重命名为 `StatSubjectGroup`。
- 这只是领域重命名，不改变构造、StatSet、TagSet、Group 或 Dispose 关系。
- 生成代码、诊断、README、DESIGN、测试和 Unity 示例必须在同一次迁移中使用新词汇，不长期保留两套同义公开类型。

### 3. 单目标 Modifier

Modifier 从 `ModifierSource` 开始，并先指定实际目标 Stat：

```csharp
var rage = new ModifierSource();

rage.Modify(hero.Stats.Power)
    .Add(5f);

rage.Modify(hero.Stats.Power)
    .AddPercent(50f);

rage.Modify(hero.Stats.Power)
    .Multiply(1.5f);

rage.Modify(hero.Stats.Power)
    .Override(100f, priority: 10);

rage.Modify(hero.Stats.Power)
    .Clamp(0f, 200f);
```

`For`、`Where...` 和 `Modify` 返回不可变、可复用、可分叉的 builder。调用下一步不会改变原 builder；`Add`、`AddPercent`、`Multiply`、`Override` 和 `Clamp` 是终结方法，每次调用都会独立、原子地创建一条注册。builder 不需要 `Begin`、`using`、Dispose 或“已消费”状态。

```csharp
var power = rage.Modify(hero.Stats.Power);

var flatHandle = power.Add(5f);
var percentHandle = power.AddPercent(20f);
```

Group 的范围与条件也可以分叉复用：

```csharp
var quickItems = aura
    .For(board)
    .WhereTargetHas(GameTags.Item.Quick);

quickItems.Modify(BoardStats.HasteKey).Add(20f);
quickItems.Modify(BoardStats.PowerKey).Add(5f);
```

Source、Group 或 Subject 在 builder 创建后结束时，后续终结方法按已结束对象的既有错误语义失败；builder 不延长这些对象的生命周期。

终结方法返回 `ModifierHandle`：

```csharp
var handle = rage.Modify(hero.Stats.Power)
    .Add(5f);

handle.Dispose(); // 只撤销这一条注册
rage.Dispose();   // 撤销 rage 仍然拥有的全部注册
```

Handle 和 Source 的重复 Dispose 保持安全。

### 4. Group Modifier

```csharp
var board = new StatSubjectGroup()
    .Add(allHeroes)
    .Add(allItems)
    .Add(allEnemies);

var quickItemAura = new ModifierSource();

quickItemAura
    .For(board)
    .Modify(BoardStats.HasteKey)
    .Add(20f);
```

`StatSubjectGroup.Add` 支持单个 Subject 和 Subject 序列，并返回自身以便连续添加。Group 中不包含目标 Key 的成员被视为不兼容成员，规则自动跳过它们。

序列重载具有整体原子性：先完整枚举输入，验证 null、批次内重复、已有成员、已结束 Subject，并为所有现有 Group 规则准备目标和依赖；全部成功后才在一个 Propagation Round 中提交。枚举、验证或规则准备的任一步失败时，一个成员也不加入，Group、Modifier、订阅和 FinalValue 保持调用前状态。批次内重复或与现有成员重复沿用单成员 `Add` 的失败语义，不静默跳过。

Group 动态数值只支持整条规则共享的一份输入；共享值变化时，所有兼容且满足条件的成员一起更新。第一版不增加 `AddEach(inputKey, selector)` 等按成员解析其自身 Stat/Resource 的动态输入。需要每成员根据自己的属性计算时，分别向具体 Subject 注册直接 Modifier。

### 5. 动态 Modifier 数值

直接传入数值对象时，统一读取它正常对外呈现的玩法值：

- `Stat` 读取 `FinalValue`；
- `Resource` 读取 `Value`；
- `RangeStat` 读取 `FinalRange`。

例如：

```csharp
rage.Modify(hero.Stats.Power)
    .Add(hero.Stats.Rage, value => value * 0.5f);

shield.Modify(hero.Stats.Armor)
    .Add(hero.Stats.Mana, value => value * 0.2f);

weapon.Modify(hero.Stats.Power)
    .Add(hero.Stats.AttackRange, range => range.Max * 0.5f);
```

这些表达分别等价于当前 `ValueInput.Final`、`ValueInput.Current` 与 RangeStat FinalRange 输入和 `ModifierValue.From` 的组合，但调用者不需要手动构造中间对象。输入变化自动触发传播；形成 FinalValue/FinalRange/Resource Value 依赖循环时，终结方法失败且不留下注册。

`Stat.BaseValue` 不属于正常玩法结果，不提供容易与 FinalValue 混淆的普通重载。确实需要绕过其他 Modifier 时，继续使用明确的 `ValueInput.Base(stat)` 高级形式。

动态输入覆盖当前已有的四种数值运算：

```csharp
var modification = rage.Modify(hero.Stats.Power);

modification.Add(dynamicValue);
modification.AddPercent(dynamicPercent);
modification.Multiply(dynamicMultiplier);
modification.Override(dynamicOverride, priority: 10);
```

四种终结方法都支持 `Stat + selector`、`Resource + selector`、`RangeStat + selector` 和 R3 adapter 提供的 `ReadOnlyReactiveProperty<float>` 输入。第一版不新增动态 Clamp；运行中两个动态边界发生 `minimum > maximum` 还需要额外失败与恢复语义，现有固定 Clamp 和永久动态 Bounds 已覆盖当前能力。RangeStat 的动态区间 Override 同样不属于本次易用 interface 改造。

直接便捷重载只接受一个动态输入，不为 Stat、Resource、RangeStat 的双输入、三输入排列增加重载。`ModifierValue` 降为 implementation，不再作为公开多输入组合入口。

不可拆分的多输入响应式公式统一由可选 R3 adapter 组合成一个 `ReadOnlyReactiveProperty<float>`，再作为单个状态输入交给 Stats：

```csharp
ReadOnlyReactiveProperty<float> bonus = Observable
    .CombineLatest(
        hero.Stats.Constitution.ObserveFinalValue(),
        hero.Stats.Level.ObserveFinalValue(),
        (constitution, level) => constitution * level)
    .ToReadOnlyReactiveProperty();

source.Modify(hero.Stats.HealthMax)
    .Add(bonus);
```

没有安装 R3 时，Core 不提供不可拆分的多输入公式；可以拆分为多条独立运算的玩法继续使用多条单输入 Modifier。`ValueInput` 仍可服务于现有动态 Bounds 和明确读取 BaseValue，但不因此继续公开 `ModifierValue`。

### 6. 条件 Modifier

条件必须同时具有当前布尔值和变化通知。第一版不公开 Core 通用条件 interface，也不接受无法声明依赖、无法通知何时重新求值的裸 `Func<bool>`。公开的条件来源只有：

- Tags：`WhereTargetHas` 与 `WhereTargetMatches`；
- 可选 R3 adapter：`Where(ReadOnlyReactiveProperty<bool>)`。

Stat、Resource 或多个外部值组合形成的任意条件交给 R3 表达，不增加 `.Where(stat, predicate)` 等第三套规则。这样可以避免 predicate 捕获未声明外部值后，系统只订阅显式参数却漏掉真实依赖。

条件控制一条 Modifier 挂载是否活跃：

- 注册时立即读取当前条件；
- 条件变化时自动进入 Propagation Round；
- 反复成立和失效不改变 Modifier 的添加顺序或 Handle 身份；
- Group 为每个成员保存独立条件结果；
- Handle、Source、Group、Subject 或条件输入结束时取消相应订阅。

### 7. Tags 便捷方法

Stats 继续把 Tags Runtime 作为必需依赖，不进行运行时探测或条件编译。安装 Stats 时必须同时安装 Tags，因此 Tags 条件 API 始终可用；本轮不提供不含 Tags 的纯数值 Stats 变体。连续 `WhereTargetHas` 表示目标必须同时拥有全部指定 Tag：

```csharp
quickItemAura
    .For(board)
    .WhereTargetHas(GameTags.Item.Quick)
    .WhereTargetHas(GameTags.Item.Fire)
    .Modify(BoardStats.HasteKey)
    .Add(20f);
```

复杂查询统一使用 `WhereTargetMatches`：

```csharp
quickItemAura
    .For(board)
    .WhereTargetMatches(
        TagQuery
            .Has(GameTags.Item.Quick)
            .Not(GameTags.Item.Fire))
    .Modify(BoardStats.HasteKey)
    .Add(20f);
```

两种形式都对 Group 中每个目标独立求值。Tag 变化后自动更新，不要求手动刷新。

连续调用任意 `Where...` 一律按 AND 组合，包括 Tags 条件与 R3 条件混用。继续添加 Where 表示继续缩小生效范围：

```csharp
aura.For(board)
    .WhereTargetHas(GameTags.Item.Quick)
    .WhereTargetMatches(notFire)
    .Where(isEnabled)
    .Modify(BoardStats.HasteKey)
    .Add(20f);
```

Tags 的 OR/NOT 由 `TagQuery.Any/None` 等查询组合表达；R3 的 OR/NOT 在传入前组合为一个 `ReadOnlyReactiveProperty<bool>`。第一版不增加 `OrWhere`、条件分组或条件优先级。Group 为每个成员独立计算完整的 AND 条件。

Group 上的 R3 条件表示整条规则共享的一份布尔状态；共享条件为 false 时没有成员生效，为 true 时再对每个成员独立检查 Tags 条件。第一版不提供 `WhereEach(subject => condition)` 等按成员创建 R3 条件的 factory。需要每成员不同的结果时，使用 Tags 查询或分别向具体 Subject 注册直接 Modifier，不引入每成员 ReactiveProperty 与 N 份额外订阅。

### 8. 可选 R3 adapter

R3 能力位于独立的 `Klrpxy.Gameplay.Stats.R3` adapter 程序集和安装包中。Stats Runtime 不引用 R3；只有项目同时安装 R3 与该 adapter 后，R3 扩展方法才进入编译和代码补全。只安装核心 Stats 时不存在 R3 类型引用、缺失依赖或需要调用者设置的条件编译符号。

安装 R3 adapter 后，可以把 FinalValue 接入 R3：

```csharp
hero.Stats.Power
    .ObserveFinalValue()
    .Subscribe(UpdatePowerText)
    .AddTo(gameObject);
```

`ObserveFinalValue()` 在订阅后先发送当前 FinalValue，之后只发送实际最终值变化。所属 `StatSubject` Dispose 时，观察流正常完成，不发送错误或伪造的最终值。UI 仍可通过 `.AddTo(gameObject)` 让自己的生命周期先结束；无论 UI 还是 Subject 先结束，订阅都能安全终止，观察流不会延长 Subject 生命周期。未来的 `ObserveFinalRange()` 与 `ObserveValue()` 采用相同规则。

第一版只提供三种正常玩法结果的观察方法：

```csharp
Stat.ObserveFinalValue();
RangeStat.ObserveFinalRange();
Resource.ObserveValue();
```

不提供 `ObserveBaseValue()` 或 `ObserveBaseRange()`。基础值仍可同步读取；只有出现真实的基础值观察需求时才扩展 R3 adapter，避免使用者误订阅基础值后遗漏 Modifier 导致的最终变化。

R3 数值可以作为动态 Modifier 输入：

```csharp
ReadOnlyReactiveProperty<float> bonus = ...;

rage.Modify(hero.Stats.Power)
    .Add(bonus);
```

R3 布尔状态可以控制 Modifier：

```csharp
ReadOnlyReactiveProperty<bool> isRaging = ...;

rage.Modify(hero.Stats.Power)
    .Where(isRaging)
    .Add(5f);
```

第一版只接受具有当前值的 `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` 作为 R3 动态数值或条件输入，不接受普通 `Observable<T>`。普通 Observable 不保证订阅时拥有当前值；拒绝它可以避免“第一次通知前按 0/false”“暂不生效”“额外传 initialValue”或“必须同步发送首值”等额外规则。

Stats 只拥有为该 Modifier 建立的订阅，不拥有调用者传入的 ReactiveProperty。Handle、Source、Group 或 Subject 结束时会取消 Stats 自己的订阅，但不会 Dispose 外部 ReactiveProperty。

作为动态数值或条件输入的 ReactiveProperty 正常完成时，Modifier 冻结并继续使用最后有效状态，同时结束 Stats 对该输入的订阅；输入失败完成时还要通过 `StatsDiagnostics` 报告错误，但同样保留最后有效状态。输入完成不等同于玩法效果结束，也不会自动撤销 Modifier；效果生命周期仍只由 Handle、ModifierSource、Group 或 Subject 控制。

## 注册与失败规则

- `For`、`Where...` 和 `Modify` 只配置 builder；终结方法执行全部验证并原子注册。
- null、已 Dispose 的 Source/Subject/Group、未绑定 Stat、无效数值、错误 Key、条件初始读取失败、订阅失败和依赖循环都必须在终结方法中失败。
- 动态 selector 必须在改变系统状态前使用当前输入成功计算一次，并产生有限数值。初次计算抛异常或产生 NaN/Infinity 时，终结方法原子拒绝注册；不会创建 Handle、Source 记录、Modifier 挂载、输入订阅或依赖关系。
- 失败后 FinalValue、Group 规则、Source、Handle、条件订阅和依赖图保持调用前状态。
- 事件只表达完整 Propagation Round 后实际发生的最终变化，不公开中间启停状态。
- 动态 selector 在注册成功后的某次输入变化中抛异常或产生 NaN/Infinity 时，保留上一次成功计算的 Modifier 数值，通过 `StatsDiagnostics` 报告错误，并保持注册与订阅以便下次输入变化时重试。失败不会自动移除或永久停用 Modifier，也不发布虚假的 FinalValue 变化。

## 旧 interface 迁移

本次修改与 `StatsOwner → StatSubject` 破坏性重命名在同一版本完成，不为旧公开注册方式保留迁移窗口。以下方式从公开 interface 移除：

```csharp
subject.AddModifier(
    Modifier.Flat(5f, UnitStats.PowerKey),
    source);
```

调用者统一迁移为：

```csharp
source.Modify(subject.StatSet.Power)
    .Add(5f);
```

旧 `Modifier` 工厂与 `AddModifier` 可以继续作为私有 implementation 存在，但不能与 fluent interface 同时作为两套公开入口。仓库内 Runtime 测试、真实消费者测试、README、DESIGN 和 Unity 示例必须在同一次修改中完成迁移。

## 完整玩法走查

下面的案例覆盖玩法作者从声明属性、创建效果、作用于 Group、表达条件和动态数值，到观察结果与结束生命周期的完整顺序：

```csharp
public partial class UnitStats : StatSet
{
    public Stat Power { get; } = new(10f);
    public Stat Haste { get; } = new(0f);
    public Stat Rage { get; } = new(0f);
    public Stat HealthMax { get; } = new(100f);
    public Resource Health { get; } = new(100f);
    public RangeStat Attack { get; } = new(5f, 10f);
}
```

```csharp
var rage = new ModifierSource();

ModifierHandle flatBonus = rage
    .Modify(hero.Stats.Power)
    .Add(5f);

rage.Modify(hero.Stats.Power)
    .AddPercent(50f);

rage.Modify(hero.Stats.Power)
    .Add(hero.Stats.Rage, value => value * 0.5f);
```

```csharp
var board = new StatSubjectGroup()
    .Add(allHeroes)
    .Add(allItems)
    .Add(allEnemies);

var quickItemAura = new ModifierSource();

quickItemAura
    .For(board)
    .Where(isBattleActive)
    .WhereTargetHas(GameTags.Item.Quick)
    .WhereTargetMatches(notFireItems)
    .Modify(UnitStats.HasteKey)
    .Add(20f);
```

安装 R3 adapter 后，正常玩法结果可以直接进入响应式代码：

```csharp
hero.Stats.Power
    .ObserveFinalValue()
    .Subscribe(UpdatePowerText)
    .AddTo(gameObject);

hero.Stats.Attack
    .ObserveFinalRange()
    .Subscribe(UpdateAttackRange);

hero.Stats.Health
    .ObserveValue()
    .Subscribe(UpdateHealthText);
```

玩法效果与主体生命周期保持显式且一致：

```csharp
flatBonus.Dispose();        // 结束单条 Modifier
rage.Dispose();             // 结束同一来源的其余 Modifier
quickItemAura.Dispose();    // 结束整个 Aura
board.Dispose();            // 结束 Group 规则和成员关系
hero.Subject.Dispose();     // 结束主体及其内部订阅
```

示例中的 `hero.Stats` 是玩法对象对其具体 StatSet 的命名，不表示 `StatSubject<TStatSet>` 自动转发 StatSet 成员。

使用者只需记住：

1. StatSet 用稳定的只读自动属性声明属性。
2. 直接修改一个对象时传实际 Stat；修改 Group 时传生成的 StatKey。
3. 从 ModifierSource 开始，按“范围、条件、目标、运算、数值”的顺序书写规则。
4. Dispose Handle 撤销一条，Dispose Source 撤销同一玩法来源的全部规则。
5. 直接传 Stat、RangeStat 或 Resource 时读取其正常玩法结果。
6. 连续的 `Where...` 永远按 AND 组合。
7. Tags 条件按目标独立判断；R3 条件表达共享的外部响应式状态。
8. 刷新、传播、循环检测、失败回滚和订阅清理由 Stats 内部负责。

## Grilling 结论

完整玩法场景与扩展包边界均已确认，没有剩余的 interface 决策。Tags 保持 Stats 的必需依赖；R3 通过独立可选 adapter 开启。本计划已发布为 PRD #46，并按可独立验收的纵向行为拆分为 #47–#54。

## 验证要求

- 用公开 interface 覆盖 StatSet 声明、五种固定运算、单条撤销和 Source 批量撤销。
- 覆盖 Group 的已有成员、未来成员、不兼容成员、离开和 Dispose。
- 覆盖动态输入传播、循环拒绝和失败原子性。
- 覆盖 Tags 初始匹配、变化后匹配和每目标不同结果。
- 覆盖 R3 初始值、后续变化、取消订阅和 Unity `AddTo` 生命周期。
- 使用 Roslyn 3.8 真实消费者编译测试验证 fluent interface 与 Source Generator 兼容。
- 运行 Release 构建、完整 .NET 测试以及 Unity 2022.3 和 Unity 6 的安装运行烟测。
