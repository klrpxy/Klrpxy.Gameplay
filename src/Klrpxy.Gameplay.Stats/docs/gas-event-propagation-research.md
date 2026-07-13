# GAS 属性变化、回调重入与批处理机制调研

调研日期：2026-07-12

范围：Unreal Engine 5.8 的 Gameplay Ability System（GAS）。仅使用 Epic 官方文档与官方 API 参考；本文不把社区文章当作事实来源。

## 结论摘要

1. GAS 公开的属性变化接口是一个多播 Delegate：游戏代码通过 `GetGameplayAttributeValueChangeDelegate(Attribute)` 注册回调。公开资料没有描述一个把回调中的所有属性修改自动推迟到“下一轮”的通用命令队列。
2. GAS 确实有延迟机制，但它们服务于明确而局部的内部问题：
   - `FScopedAggregatorOnDirtyBatch` 在作用域内延迟所有 Aggregator 的 `OnDirty` 回调，到作用域结束再调用；
   - `FScopedActiveGameplayEffectLock` 在可能回调游戏代码并同时遍历 Active GameplayEffect 列表时，延迟该列表增删所需的内存操作。
3. 因此，不能把“GAS 有批处理/锁”概括为“GAS 使用统一命令队列解决事件重入”。更准确的说法是：GAS 允许同步调用链存在，并在容易造成重复传播或容器失效的局部位置使用作用域批处理和结构修改保护。
4. 对本项目的启示不是照搬 GAS 的复杂实现。第一版如果允许 `OnFinalValueChanged` 中再次修改属性，就必须明确选择：接受嵌套同步调用，或者由本库额外提供自己的传播队列。后者是本项目的设计选择，并非 GAS 的既定语义。

## 1. 属性变化通知是什么

Epic 将 `UAbilitySystemComponent::GetGameplayAttributeValueChangeDelegate` 描述为“注册属性值变化时的通知”。GAS 的 API 索引同时说明 `FOnGameplayAttributeValueChange` 是接收一个 `FOnAttributeChangeData` 参数的多播 Delegate。也就是说，GAS 向游戏代码暴露的是回调机制，不是可等待的任务或显式事件队列。

来源：

- [UAbilitySystemComponent::GetGameplayAttributeValueChangeDelegate](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/GetGameplayAttri-)
- [GameplayAbilities API：FOnGameplayAttributeValueChange](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities)

官方属性文档给出的典型用法也是直接在 AbilitySystemComponent 的 Delegate 上注册对象成员函数。修改基础值时，官方要求通过 `SetNumericAttributeBase` 进入 Aggregator 系统；已有的 Active Modifier 会继续作用于新基础值。

来源：

- [Gameplay Attributes and Attribute Sets](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-attributes-and-attribute-sets-for-the-gameplay-ability-system-in-unreal-engine)
- [UAbilitySystemComponent::SetNumericAttributeBase](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/SetNumericAttributeBase)

### 能否确认 Delegate 是同步派发？

Unreal 的普通多播 Delegate 调用在 C++ 调用栈中执行，但当前公开的 Epic API 页面没有展示 `OnAttributeAggregatorDirty` 的完整函数体，也没有用自然语言明确承诺“属性变化 Delegate 一定在修改 API 返回前派发”。因此本文不把具体的逐行调用顺序表述为官方稳定契约。

可以确认的是：公开 API 没有提供“排入下一帧”“等待传播循环”或“异步属性变化”的概念；同时 `OnAttributeAggregatorDirty` 被描述为 Aggregator 值变化时调用，并让依赖它的 GameplayEffect 刷新数值。这强烈表明它属于当前更新调用链，而不是一个面向游戏代码的通用异步队列，但这是基于公开接口结构的判断。

来源：

- [UAbilitySystemComponent::OnAttributeAggregatorDirty](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/OnAttributeAggregatorDirty)

## 2. Aggregator 的 Dirty 批处理

`FAggregator` 保存 BaseValue、Modifier channels、依赖它的 Active GameplayEffect handles，并公开 `OnDirty` 与 `OnDirtyRecursive`。它还保存 `BroadcastingDirtyCount`，说明实现会区分或追踪 Dirty 广播期间的递归传播；不过仅凭公开字段不能断言递归时所有具体行为。

来源：

- [FAggregator](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FAggregator)

Epic 对 `FScopedAggregatorOnDirtyBatch` 的说明非常明确：

- 它允许在一个作用域内批处理所有 Aggregator 的 `OnDirty` 调用；
- 这些回调全部延迟到该作用域退出时；
- 内部保存待处理 Aggregator 的集合；
- 因为保存的是裸 `FAggregator*`，只能用于 Aggregator 不会在该作用域内被删除的场景。

这与“命令队列”有相似之处，但队列中的不是任意玩法命令，而是“哪些 Aggregator 需要在作用域结束后发出 Dirty 回调”。它主要合并重复的依赖刷新，并避免一个复合内部操作在尚未完成时反复向外传播。

来源：

- [FScopedAggregatorOnDirtyBatch](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FScopedAggregatorOnDirtyBatch)

## 3. Active GameplayEffect 列表锁

`FScopedActiveGameplayEffectLock` 解决的是另一类问题。Epic 明确说明：当容器正在遍历 Active GameplayEffect 列表或持有列表元素指针时，回调游戏代码可能引发列表修改，导致底层内存移动。该作用域锁会排队处理增删，把 Active GameplayEffect 列表的内存操作推迟到作用域结束。

这里需要注意官方描述中的细节：“添加和删除实际上仍会发生”，延迟的是对 Active GameplayEffect 列表的内存操作。因此它不是事务，也不表示回调直到列表更新完毕后才执行；它是保护容器迭代和指针有效性的结构性防护。

来源：

- [FScopedActiveGameplayEffectLock](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FScopedActiveGameplayEffectLock)

这也直接证明 GAS 预期内部代码在“可能回调游戏代码”的过程中遭遇再次添加或删除 GameplayEffect，并专门为此保护容器。它没有通过全面禁止重入来解决问题。

## 4. GameplayEffect 应用、移除和属性更新的关系

GAS 用 GameplayEffect 表达直接改变基础值、临时 Buff/Debuff 和持续变化。`ApplyGameplayEffectToSelf` 返回 Active GameplayEffect handle；`RemoveActiveGameplayEffect` 按 handle 移除指定效果。属性最终值通过 Aggregator 系统计算；Epic 明确警告外部代码不要直接调用 `SetNumericAttribute_Internal`，因为它会绕过 Aggregator 系统。

来源：

- [Gameplay Attributes and Gameplay Effects](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-attributes-and-gameplay-effects-for-the-gameplay-ability-system-in-unreal-engine)
- [UAbilitySystemComponent::ApplyGameplayEffectToSelf](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/ApplyGameplayEffectToSelf)
- [UAbilitySystemComponent::RemoveActiveGameplayEffect](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/RemoveActiveGameplayEffect)
- [UAbilitySystemComponent::SetNumericAttribute_Internal](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/SetNumericAttrib-)

由这些资料可以确认“修改定义/基础值 → Aggregator 变脏 → 刷新相关数值”这条结构，但官方公开页面不足以给出所有 Delegate 在 GameplayEffect 应用、移除、依赖递归和网络接收情况下的严格全序。若要把某个精确回调顺序当作兼容性保证，仍需针对目标 UE 版本检查 Epic 源码和自动化测试。

## 5. GAS 是否使用了通用命令队列

结论：没有证据表明 GAS 为属性变化回调提供“所有再次修改一律进入下一轮”的通用命令队列。

公开资料能证明的延迟只有局部语义：

| 机制 | 延迟的内容 | 目的 |
| --- | --- | --- |
| `FScopedAggregatorOnDirtyBatch` | Aggregator `OnDirty` 回调 | 合并/推迟数值依赖传播，直到复合操作的作用域结束 |
| `FScopedActiveGameplayEffectLock` | Active GameplayEffect 列表增删的内存操作 | 防止游戏代码回调期间的结构修改破坏遍历或指针 |

它们都采用 RAII 作用域：进入作用域增加锁或批处理计数，退出最外层作用域时处理积累的工作。这比全局、跨帧命令队列更局部，也更贴近“当前 API 调用完成前保持内部一致”的目标。

## 6. 对本项目的设计启示

如果本项目选择立即嵌套执行，最典型的风险不是“值一定算错”，而是调用栈和观察顺序变得难以控制。例如：

1. `Attack` 从 10 变为 20，开始派发 `Attack.OnFinalValueChanged`；
2. 第一个订阅者在回调中把 `Health` 减少 5；
3. `Health` 立即重算并派发自己的事件；
4. `Health` 的订阅者又移除一个影响 `Attack` 的 Modifier；
5. 在最初的 `Attack` 事件尚未通知完其他订阅者前，`Attack` 已再次改变并开始第二层 `Attack` 事件。

于是后续订阅者可能先看到嵌套产生的第二次 `Attack` 变化，再回到外层收到第一次变化；两个属性的回调还可能互相触发，形成深递归或无限递归。GAS 的 Active GameplayEffect 列表锁正说明“回调游戏代码时修改正在遍历的容器”是实际需要防护的问题；Aggregator 的 Dirty 批处理则说明复合更新中延迟传播也有现实价值。

本项目若采用传播队列，可以更简单地定义：回调中的修改请求先入 FIFO 队列；当前一次值变化的全部订阅者执行完后，再取出下一项修改并完成它的重算与通知。它不是多线程消息队列，也不必跨帧，只是同一线程、同一次最外层调用中的“延后到当前通知结束后”。

但这会形成不同于 GAS 已公开机制的更强语义。优点是事件顺序容易解释、不会出现同一事件派发中途插入另一整轮派发；代价是回调中发起修改后，紧接着在同一回调里读取值时，读取到的究竟是旧值还是预期新值必须额外规定。

## 7. 不确定性与版本边界

- Epic API 参考公开了类型、签名、字段和部分设计注释，但没有完整展示相关 `.cpp` 函数体。因此本文没有声称 `OnAttributeAggregatorDirty`、属性 Delegate、GameplayEffect 应用/移除在 UE 5.8 中存在一个文档保证的逐行调用全序。
- `FScopedAggregatorOnDirtyBatch` 和 `FScopedActiveGameplayEffectLock` 的目的与延迟范围由 Epic 文档直接确认；“GAS 没有通用属性命令队列”是基于官方公开接口中未出现该语义以及两种局部机制的明确边界得出的结论。
- 网络复制与预测可能导致客户端观察到额外的属性变化或修正；这不等同于本地事件回调重入。本报告没有尝试规定跨网络的事件顺序。
- 如果实现目标需要严格模拟某一 UE 版本，应该以该版本 Epic 源码和测试为最终依据，而不是把本文的架构级结论当作逐行兼容规范。
