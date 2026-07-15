# Stats

Stats is a framework context for defining, grouping, modifying, and evaluating runtime numeric stats. The core context is engine-agnostic and does not depend on Unity APIs.

## Language

**Stat**:
A named numeric property whose value can be defined, modified, and queried within a stat system.
属性系统中可以定义、修改和查询的具名数值属性。
_Avoid_: Attribute, property

**StatSet**:
A collection of stats and resources that belongs to one StatSubject and retains an immutable reference to it.
属于一个 StatSubject 的 Stat 与 Resource 集合，并持有对该 Subject 不可变的引用。
_Avoid_: AttributeSet, stat collection

**StatSubject**:
A subject of stat modification and observation that owns exactly one stat set and a tag set used to classify it. The subject of its stat set cannot change. Objects with only tags are not StatSubjects.
属性修改和观察所作用的主体，恰好拥有一个 StatSet 和一个用于自身分类的 TagSet；StatSet 的 Subject 不可更换，只有 Tag 而没有 StatSet 的对象不属于 StatSubject。
_Avoid_: StatsOwner, Attribute owner, stat container

**StatSubjectGroup**:
A group of stat subjects, potentially with different concrete stat set types, that can apply shared modifiers to compatible members.
由 StatSubject 组成的集合，成员可以拥有不同的具体 StatSet 类型，并能向兼容成员应用共享 Modifier。
_Avoid_: StatsOwnerGroup, StatSetGroup, StatSubject collection

**StatKey**:
A build-stable, target-typed identifier for a stat that lets modifiers and other systems refer to that stat without using a raw string.
在当前构建内保持稳定并携带目标类型的 Stat 标识，使 Modifier 和其他系统无需使用裸字符串即可引用目标 Stat。
_Avoid_: StatId, StatDefinition, stat name

**Modifier**:
A change applied to a stat during one of the stat system's fixed calculation stages. Custom modifier operations are not part of the model.
在属性系统某个固定计算阶段中施加于 Stat 的变化；自定义 Modifier 运算不属于当前模型。
_Avoid_: Stat modifier, attribute modifier

**ModifierSource**:
A stable identity that groups modifiers produced by the same gameplay source so their direct registrations and group rules can be removed together.
标识同一个玩法来源所产生 Modifier 的稳定身份，使其直接注册和 Group 规则能够被统一移除。
_Avoid_: Modifier owner, source object

**ModifierHandle**:
A removable attachment of one modifier to a stat subject or subject group. It is not the modifier definition itself.
一个 Modifier 在 StatSubject 或 StatSubjectGroup 上的可移除挂载；它不是 Modifier 定义本身。
_Avoid_: Modifier reference, modifier token

**DynamicModifierValue**:
A modifier value derived from explicitly declared value inputs. Input changes cause dependent stats to be recalculated, and final-value dependencies must remain acyclic.
由显式声明的 ValueInput 推导出的 Modifier 数值；输入变化会使依赖它的 Stat 重新计算，且 FinalValue 依赖必须保持无环。
_Avoid_: Reactive modifier, calculated modifier

**ValueInput**:
An observable numeric input used by a dynamic modifier value. It can expose a stat's base value, a stat's final value, a resource's current value, or a changing value supplied by another gameplay context.
供 DynamicModifierValue 使用的可观察数值输入，可以表示 Stat 的 BaseValue、FinalValue、Resource 的当前 Value，或其他玩法上下文提供的动态数值。
_Avoid_: ModifierSource, value provider

**Propagation Round**:
A synchronous unit of work started by one public Stats mutation. It recalculates the complete affected dependency graph before publishing final value changes.
由一次 Stats 公开修改开启的同步工作单元；它先完成整个受影响依赖图的重算，再公开最终数值变化。
_Avoid_: Refresh, batch, update tick

**BaseValue**:
The unmodified value of a stat before modifiers are applied.
Stat 应用任何 Modifier 之前的未修饰数值。
_Avoid_: Initial value, raw value

**FinalValue**:
The calculated value of a stat after all relevant modifiers are applied.
Stat 应用所有相关 Modifier 后得到的计算结果。
_Avoid_: Current value, result value

**RoundingRule**:
An optional rule that gives a stat integer semantics by rounding its calculated value before it becomes the final value.
可选的取整规则，在计算值成为 FinalValue 前对其取整，使 Stat 具备整数语义。
_Avoid_: Integer stat, rounding modifier

**Resource**:
A single mutable gameplay value that can be increased, decreased, and constrained by dynamic bounds, but does not accept modifiers.
可以增加、减少并受动态边界约束，但不接受 Modifier 的单一可变玩法数值。
_Avoid_: ResourceStat, current stat

**RangeStat**:
A stat whose value is an interval between a minimum and maximum possible value. It does not represent a current value constrained by a maximum value.
数值由最小可能值和最大可能值组成区间的 Stat；它不表示受最大值约束的当前值。
_Avoid_: Bounded stat, current/max stat

**BaseRange**:
The unmodified interval of a range stat.
RangeStat 未应用 Modifier 前的区间。
_Avoid_: Initial range, raw range

**FinalRange**:
The calculated interval of a range stat after all relevant modifiers are applied.
RangeStat 应用所有相关 Modifier 后得到的计算区间。
_Avoid_: Current range, result range
