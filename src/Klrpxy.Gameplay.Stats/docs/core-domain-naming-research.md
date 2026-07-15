# 游戏统计属性系统核心领域命名调研

> 调研日期：2026-07-14
> 目的：比较市售工具与开源系统如何区分“单个数值、数值集合、拥有数值并承担 Tags/效果生命周期的对象，以及群组或效果来源”，为 Klrpxy.Gameplay.Stats 的核心命名提供依据。

## 方法与边界

本报告只引用产品官方文档、引擎官方 API 或项目自己的官方仓库。开源样本按“能从公开源码或 README 确认其领域职责”选择，不据此推断流行度、成熟度或市场占有率。

“Tags 与 Stats 合并”在本文中特指：同一个运行时宿主同时拥有或统一协调数值、Gameplay Tags 与效果生命周期。若 Tag 仍是独立类型或子系统，但由同一宿主管理，记为“宿主合并、概念分离”。

## 对照总表

| 系统 | 单个数值 | 数值集合 | 数值宿主 / 生命周期对象 | 群组、效果与来源 | Tags 与 Stats | 典型用户代码层级 |
| --- | --- | --- | --- | --- | --- | --- |
| Unreal GAS | `FGameplayAttributeData`；`FGameplayAttribute` 是属性引用 | `UAttributeSet` | `UAbilitySystemComponent`（挂到 Actor） | `UGameplayEffect` → `FGameplayEffectSpec` → active effect；`FGameplayEffectContext` / `SourceObject` 记录来源 | 宿主合并、概念分离 | Actor → ASC → AttributeSet / Attribute；Ability → EffectSpec → ASC |
| Game Creator Stats | `Stat`（可成长/公式值）、`Attribute`（有上下界的当前值） | `Class` 资产包含 Stats 与 Attributes | `Traits` 组件把 Class 绑定到 GameObject，并承载运行时值 | `Stat Modifier`、`Status Effect`；目标仍是 `Traits` | 未在 Stats 领域模型中合并 Gameplay Tags | GameObject → Traits → Class → Stat / Attribute；主要通过 Inspector 与 Visual Scripting |
| Opsive UCC Attributes | `Attribute` | `AttributeManager` 内的属性列表 | `AttributeManager` 组件本身 | 公开 Attribute API 以直接改值、自动恢复和事件为主；无独立效果来源对象 | AttributeManager 契约不合并 Tags | GameObject → AttributeManager → `GetAttribute(name)` → Attribute.Value |
| sjai013 Unity GAS | `AttributeScriptableObject` 定义属性；运行时属性有 Base / Current Value | Attribute System 内的属性集合 | `AbilitySystemCharacter` | Gameplay Effect 定义资产与 Effect Spec；Ability Spec 持有每角色状态 | 宿主合并、概念分离 | Component → AbilitySystemCharacter → Attribute / Gameplay Effect / Ability Spec |
| No78Vino EX-GAS | `AttributeBase` | `AttributeSet`，再由 `AttributeSetContainer` 保存 | `AbilitySystemComponent` | `GameplayEffect` / `GameplayEffectSpec`；ASC 暴露 Effect、Tag、Ability、AttributeSet 容器 | 宿主合并、概念分离 | ASC → AttributeSetContainer → AttributeSet → AttributeBase |
| OctoD Godot Gameplay Attributes | `Attribute` | `AttributeSet` 描述定义 | `AttributeContainer` Node 暴露值、信号与生命周期 | `AttributeBuff` 支持即时、持续、堆叠和移除 | Attributes 插件本身不合并 Tags；Ability 是独立插件与 `AbilityContainer` | Scene Node → AttributeContainer → Attribute；Buff → Container |
| meredoth Unity Stat System | `Stat` | 无框架级集合 | 无框架级 Owner；由游戏对象自行持有 Stat 字段 | `Modifier` 可记录任意 source object，并按来源批量移除 | 不含 Tags | Character / Item 自有字段 → Stat → Modifier |

## 各系统的一手证据

### 1. Unreal Gameplay Ability System

Epic 将单个存储值命名为 `FGameplayAttributeData`，而 `FGameplayAttribute` 描述 AttributeSet 内某个属性的引用。`UAttributeSet` 的官方职责是定义一组 Gameplay Attributes；项目应继承它并加入 Health、Damage 等属性，随后把实例注册到 `UAbilitySystemComponent`。一个项目可以有多个相互继承的 AttributeSet。[UAttributeSet API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAttributeSet)

更高层的 `UAbilitySystemComponent` 是 Actor 与 GAS 交互的宿主：官方概览明确把 Attributes/Attribute Sets、Gameplay Abilities、Gameplay Effects 都放在这一框架下；Gameplay Tags 参与 Ability 与 Effect 的约束。[GAS 官方概览](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-ability-system-for-unreal-engine) [UAbilitySystemComponent API](https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent?lang=en-US)

Effect 的定义、运行时规格与已应用实例使用不同名字：`UGameplayEffect` 是定义资产，`FGameplayEffectSpec` 是可变的运行时规格，应用后再成为 active effect。来源不是一个统一的 “group” 类型，而由 `FGameplayEffectContext` 的 instigator、effect causer、source object 等信息表达。[Gameplay Effects 官方文档](https://dev.epicgames.com/documentation/unreal-engine/gameplay-effects-for-the-gameplay-ability-system-in-unreal-engine) [FGameplayEffectSpec API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectSpec) [FGameplayEffectContext API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectContext)

结论：GAS 对集合使用 `Set`，对统一宿主使用 `Component`；Tags 与数值类型没有揉成同一个类型，但由 ASC 统一持有和协调。

### 2. Game Creator Stats

Game Creator 明确区分两种单值：`Stat` 是可随游戏成长并可由 Formula 修改的数值特征；`Attribute` 是限制在最小值与最大值之间的数值特征，典型例子是 Health，而且最大值可来自另一个 Stat。[Stats 官方文档](https://docs.gamecreator.io/stats/classes/stats/) [Attributes 官方文档](https://docs.gamecreator.io/stats/classes/attributes/)

集合定义叫 `Class`，其中包含 Stats 与 Attributes；运行时宿主叫 `Traits`，它把一个 Class 资产绑定到任意 GameObject，并在运行时显示和操作实际值。[Classes 官方文档](https://docs.gamecreator.io/stats/classes/classes/) [Traits 官方文档](https://docs.gamecreator.io/stats/classes/traits/)

临时影响分为 `Stat Modifier` 和 `Status Effect`。Modifier 添加到拥有 Traits 的目标；Status Effect 具有持续时间、堆叠、On Start / On End / While Active 等生命周期，也要求目标拥有 Traits。[Stat Modifiers 官方文档](https://docs.gamecreator.io/stats/stat-modifiers/) [Status Effects 官方文档](https://docs.gamecreator.io/stats/status-effects/)

结论：它没有用 `Stats` 同时表示单值、集合和宿主，而是用 `Stat/Attribute`、`Class`、`Traits` 三层词汇。其 Stats 官方领域模型没有 Gameplay Tags 聚合概念。

### 3. Opsive Ultimate Character Controller Attributes

Opsive 把单个可变值叫 `Attribute`，字段包括 Name、Min Value、Max Value、Value 与自动增减策略；多个 Attribute 被添加到 `AttributeManager` 组件。公开代码从 Manager 按名字取得 Attribute，再直接改变 `Value`，范围约束由 Attribute 自己保证。[Opsive Attributes 官方文档](https://opsive.com/support/documentation/ultimate-character-controller/attributes/)

结论：这是较浅的两层模型：`AttributeManager → Attribute`。Manager 同时是集合与 Unity 宿主，但其公开 Attribute 契约不承担 Gameplay Tags 或通用效果来源生命周期。

### 4. sjai013 Unity Gameplay Ability System

该项目把系统拆成 Attribute System、Gameplay Tags、Ability System 三部分，并明确称 Ability System 负责协调前两者。属性定义资产使用 `AttributeScriptableObject`，属性具有 Base Value、Current Value 与活动 modifiers；角色级宿主是 `AbilitySystemCharacter`。Gameplay Effect 有定义资产和运行时 spec，Ability 也区分 `AbstractAbilityScriptableObject` 与每角色有状态的 `AbstractAbilitySpec`。[项目官方 README](https://github.com/sjai013/unity-gameplay-ability-system)

结论：它沿用 GAS 的“属性定义 / 运行时宿主 / Effect Spec”层级；Tags 与 Attributes 保持概念分离，但在 `AbilitySystemCharacter` 汇合。仓库已由作者归档，因此这里只把它作为可核验的 API 命名样本。

### 5. No78Vino EX-GAS

项目文档把 `Attribute` 称为核心运行时数据单位，把 `AttributeSet` 称为运行时数据单位集合；实际单值基类是 `AttributeBase`。多个集合由 `AttributeSetContainer` 管理。更高层 `AbilitySystemComponent` 被定义为 GAS 的基本运行单位和外部干预入口，同时公开 `GameplayEffectContainer`、`GameplayTagAggregator`、`AbilityContainer` 与 `AttributeSetContainer`。[项目官方 README / API 文档](https://github.com/No78Vino/gameplay-ability-system-for-unity)

结论：这是最明确的四层命名：`AttributeBase → AttributeSet → AttributeSetContainer → AbilitySystemComponent`。Tags 与 Stats 在 ASC 汇合，但仍各自有 Aggregator/Container。

### 6. OctoD Godot Gameplay Attributes / Abilities

Attributes 插件使用 `Attribute`、`AttributeSet`、`AttributeContainer`、`AttributeBuff`：Set 描述 Container 有哪些属性，Container 作为 Node 向场景树暴露属性值、信号与生命周期，Buff 负责即时或临时修改、持续时间和堆叠。[Godot Gameplay Attributes 官方仓库](https://github.com/OctoD/godot_gameplay_attributes)

同一作者把 Ability 放在独立插件中，命名为 `Ability`、`AbilityContainer` 与 `RuntimeAbility`；Container 可挂到任意场景 Node，并负责 grant/revoke/activate 等生命周期。[Godot Gameplay Abilities 官方仓库](https://github.com/OctoD/godot-gameplay-abilities)

结论：`Set` 表示定义集合，`Container` 表示场景中的运行时宿主。属性与能力是相邻但独立的模块，没有用一个 Stats 类型覆盖全部职责。

### 7. meredoth Unity Stat System

这是刻意较小的系统：用户直接 `new Stat(baseValue)`，再 `AddModifier`。框架没有 StatSet 或 Owner；角色、装备等游戏类自行持有 Stat 字段。`Modifier` 可带任意对象引用，Stat 能按该对象一次移除全部 modifiers。[项目官方 README](https://github.com/meredoth/Stat-System)

结论：轻量模型可以只需要 `Stat`，但一旦需要 Tags、跨 Stat 规则与统一清理，任意 `object` 来源就不再提供足够的领域语义。

## 跨系统命名模式

### 单值通常是 `Stat` 或 `Attribute`

- `Stat` 常指基础值加 modifiers 后得到的数值，尤其适合 Strength、Attack 等可成长量。
- `Attribute` 的含义更宽：GAS 用它指所有可被 Effect 修改的数值；Game Creator 与 Opsive 又常用它指有当前值、上下界或自动恢复的资源量。
- 因此名字不能只靠行业习惯决定，必须由本项目领域语义限定。本项目用 `Stat`、`RangeStat`、`Resource` 明确区分计算值、受动态边界约束的计算值和主动消费/恢复值，比把三者都叫 Attribute 更精确。

### 集合通常用 `Set`，运行时宿主用 `Component`、`Container` 或角色语义名

`AttributeSet` 在 Unreal、No78Vino 与 OctoD 中都只表示属性集合或集合定义，不负责完整的 Gameplay Tags、能力和效果生命周期。更高层职责另由 `AbilitySystemComponent`、`AttributeContainer`、`AbilitySystemCharacter` 或 `Traits` 承担。

这说明“集合”与“拥有集合且协调生命周期的对象”是稳定的两层概念，不宜用一个 `Stats` 类型混合。

### Tags 的常见边界是“统一宿主，独立子系统”

完整 ability system 往往让同一个宿主持有 Attributes、Tags 与 active effects，但仍保留 `AttributeSet`、Tag Container/Aggregator、Effect Spec 等独立类型。轻量 stat system 通常完全没有 Gameplay Tags。

因此，本项目让 `StatSubject` 拥有 TagSet，同时保持依赖方向为 Stats → Tags，而不把 Tag 类型实现并入 Stats，符合这一模式，也符合本仓库 [CONTEXT-MAP](../../../CONTEXT-MAP.md) 的 bounded-context 约束。

### 效果定义、挂载实例和来源是不同概念

Unreal 区分 `GameplayEffect`、`GameplayEffectSpec`、active effect 与 `EffectContext/SourceObject`；sjai013 也区分定义资产和运行时 spec。meredoth 的轻量方案只保存任意 source object，简单但缺少显式生命周期。

本项目的 `Modifier`（不可变规则）、`ModifierHandle`（一次挂载）、`ModifierSource`（装备、技能、Buff 或光环实例产生的一组挂载）具有清晰分层。`ModifierSource` 比泛化的 `SourceObject` 更窄，也更能表达批量撤销职责。

### 群组不是普遍的 stats 基础层

上述系统通常以 Actor/Component 为目标；“把同一规则持续应用到动态成员集合”的群组并非它们共同的核心抽象。因此不能以外部系统缺少 Group 为由否定本项目的 `StatSubjectGroup`。在本项目中，它表达真实玩法职责，而不是 StatSet 集合，名字应准确限定成员类型与行为。

## 对本项目候选名的评价

评价以已确认的 [Stats CONTEXT](../CONTEXT.md) 与 [PRD #46](https://github.com/klrpxy/Klrpxy.Gameplay/issues/46) 为准：一个 `StatSubject` 恰好拥有一个 `StatSet` 和一个用于自身分类的 TagSet，并且是整个属性模型的生命周期入口。

### `StatSet`：推荐保留

优点：

- 与 Unreal、No78Vino、OctoD 的 `AttributeSet` 模式一致：`Set` 清楚表示多个相关数值的集合，而不是角色或系统宿主。
- 单数前缀 `StatSet` 符合英文复合名习惯，也避免 `StatsSet` 的重复复数感。
- `CharacterStatSet`、`EnemyStatSet` 能自然表达强类型集合，并与生成的 `StatKey` 对齐。

边界：它不应吸收 Tags、Group、active Modifier 生命周期；否则会失去 `Set` 所承诺的狭窄含义。

### `StatsOwner`：有依据，但不作为最终名称

它曾是合理候选：

- “Owner”直接表达领域归属与不可转移关系，而不是 Unity `Component` 或 Godot `Node` 这样的技术宿主。
- 复数 `Stats` 在这里表示整个 Stats 能力域：对象不仅持有单个 Stat，还持有一个 StatSet、对象 Tags、Modifier 挂载、Group 关系和统一 Dispose 生命周期。

但 “Owner” 主要强调持有关系，没有表达它也是 Modifier 和观察行为所作用的目标；还容易被理解为玩法对象必须继承该类型。最终选择 `StatSubject`，用 Subject 表达“属性修改和观察所作用的主体”，并继续由领域契约保证它恰好拥有一个 StatSet 和一个 TagSet。该名称不是调研样本中的主流原名，但比包含更宽 ability/traits 语义的 `AbilitySystemComponent` 或 `Traits` 更准确地限定了本项目边界。

### `StatBlock`：不推荐替代 `StatSet` 或 `StatSubject`

- `Block` 没有说明它是定义、实例、快照、集合还是生命周期宿主。
- 在本次可核验样本中，没有系统用 `StatBlock` 表示同时拥有数值、Tags 与效果生命周期的核心对象；这只是样本观察，不是流行度结论。
- 若未来出现纯序列化快照或 UI 展示 DTO，`StatBlock` 尚可作为局部名字；当前用它替代 `StatSet` 会弱化“集合”，替代 `StatSubject` 会隐藏“作用主体与清理”。

### `TStats`：不推荐作为公开领域类型；也不推荐替代 `TStatSet`

- `T` 通常只表达泛型类型参数，不表达一个对象在领域中的职责。
- `Stats` 无法区分“数值集合”与“拥有集合的对象”，会把本项目最重要的两层模型重新压平。
- 在 `StatSubject<TStatSet>` 中，`TStatSet` 虽更长，却把约束和用户预期说清楚；`TStats` 会让人误以为它可能是 Subject、服务或任意 Stats API。

## 结论

最终采用以下核心词汇：

```text
Stat / RangeStat / Resource
            ↓ 属于
          StatSet
            ↓ 恰好一个、不可换绑
      StatSubject + TagSet
          ↙             ↘
StatSubjectGroup     ModifierHandle
                           ↓ 由同一玩法来源汇总
                     ModifierSource
```

外部一手资料最稳定的共识不是某一个具体类名，而是职责分层：单值、集合、运行时主体、效果定义/实例/来源分别命名。`StatSet` 与 `StatSubject` 保留了这条边界；`StatBlock` 太含糊，`TStats` 只适合作为缺乏领域信息的泛型占位符。若后续调整命名，优先验证职责是否变化，而不应仅为贴近 GAS 的 `AttributeSet` 或 `AbilitySystemComponent` 而改名。
