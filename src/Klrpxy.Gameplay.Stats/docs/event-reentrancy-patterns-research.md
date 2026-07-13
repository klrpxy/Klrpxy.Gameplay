# 事件重入与嵌套通知：其他应用框架的处理方式

调研日期：2026-07-12

范围：非游戏应用框架中的同步事件、观察者通知与状态传播。仅引用项目官方文档、官方源码或正式规范。

## 结论

这种问题并非游戏或属性系统特有。只要系统允许“监听者在处理通知时再次修改被观察状态或再次发出事件”，就可能出现事件重入（reentrancy）、嵌套派发（nested dispatch）、通知顺序反转、无限反馈循环等问题。

不同框架没有统一答案，而是明确选择一种语义：

| 框架 | 默认行为 | 主要保护方式 |
| --- | --- | --- |
| .NET `ObservableCollection<T>` | 同步通知 | 检测并在特定条件下拒绝重入修改 |
| Qt signals/slots | 同线程通常立即调用 | 可选事件循环排队；也可屏蔽信号或只在值变化时发信号 |
| Node.js `EventEmitter` | 同步、按注册顺序调用 | 需要延迟时显式使用 `setImmediate` / `process.nextTick` |
| Redux Store | reducer 中禁止再次 dispatch；listener 中允许 | 订阅快照、阶段限制，并明确嵌套 dispatch 的可观察语义 |
| Reactive Streams | 实现可同步或异步 | 规范要求传给同一 Subscriber 的信号序列化且不得重叠 |

因此，本项目讨论的“数值立即更新，但新事件进入 FIFO 队列，当前通知完成后再派发”是一种常见的**非重入/序列化通知**方案，而不是完整的命令队列。

## 1. 问题叫什么

最准确的核心术语是：

- **事件重入（event reentrancy）**：事件处理尚未返回时，调用链再次进入同一事件系统或对象操作。
- **嵌套派发（nested event dispatch / nested notification）**：监听者在处理事件 A 时发出事件 B，B 在 A 的其余监听者之前立即执行。
- **递归通知或反馈循环（recursive notification / feedback loop）**：通知触发修改，修改又触发通知，可能无法收敛。
- **序列化通知（serialized event delivery）**：通知之间不重叠；当前通知完成后才开始下一条。它不等于多线程锁，也不必异步或跨帧。
- **深度优先与广度优先派发（depth-first vs breadth-first dispatch）**：立即嵌套近似深度优先；新事件追加 FIFO 队列近似广度优先。

典型顺序问题：监听者 A 在处理 `100 → 50` 时把值改成 60，并立即嵌套发出 `50 → 60`；监听者 B 可能先看到 60，再回到外层看到旧事件 50，最终显示旧值。问题不在计算，而在新通知插入了旧通知中间。

## 2. .NET ObservableCollection：检测并拒绝重入

`.NET` 的 `ObservableCollection<T>` 提供 `BlockReentrancy()` 和 `CheckReentrancy()`。官方将前者描述为禁止修改集合的重入尝试；典型用法是在 `OnCollectionChanged` 周围建立保护作用域。`CheckReentrancy()` 用于检查在 `CollectionChanged` 派发期间再次修改集合的行为。

官方源码展示了一个重要细节：当前实现并非无条件禁止一切重入。只有保护监视器处于 busy 状态，并且 `CollectionChanged` 有多个订阅目标时才抛出 `InvalidOperationException`；保留单监听者重入是兼容性选择。因此它代表的是“检测/拒绝危险重入”，不是通知排队。

来源：

- [ObservableCollection.BlockReentrancy](https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.observablecollection-1.blockreentrancy)
- [ObservableCollection.CheckReentrancy](https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.observablecollection-1.checkreentrancy)
- [ObservableCollection.cs 官方源码](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ObjectModel/src/System/Collections/ObjectModel/ObservableCollection.cs)

## 3. Qt：直接调用与队列调用都由框架明确提供

Qt 官方说明，信号发出时，连接的槽通常像普通函数一样立即执行；所有槽返回之后，`emit` 后面的代码才继续。多个槽按连接顺序执行。因此，槽中再次发信号会自然形成同步嵌套调用。

Qt 同时提供 `Qt::QueuedConnection`：调用参数被记录到接收者的事件循环，槽稍后执行。`Qt::AutoConnection` 在同线程通常选择 Direct，在跨线程时选择 Queued。Qt 还展示了循环连接的另一种保护：setter 只在值确实变化时发出 `valueChanged`，从而终止 `A → B → A` 的无变化循环；`QObject::blockSignals()` 则可临时屏蔽通知。

这说明成熟框架通常不会假定一种万能策略，而是同时提供同步、排队、变化去重和临时屏蔽。

来源：

- [Qt Signals & Slots](https://doc.qt.io/qt-6/signalsandslots.html)
- [Qt ConnectionType](https://doc.qt.io/qt-6/qt.html#ConnectionType-enum)
- [Qt Threads and QObjects](https://doc.qt.io/qt-6/threads-qobject.html)
- [QObject](https://doc.qt.io/qt-6/qobject.html)

## 4. Node.js EventEmitter：同步语义意味着可以嵌套

Node.js 官方规定，`EventEmitter.emit()` 同步并按注册顺序调用监听者。由普通调用栈语义可知，监听者再次调用 `emit()` 时，内层派发会在外层后续监听者之前完成，即形成深度优先的嵌套派发。

Node 没有自动把这种调用变成非重入队列。需要延迟工作时，官方示例让监听者显式使用 `setImmediate()` 或 `process.nextTick()`。因此“同步、顺序明确、是否延后由调用者选择”也是一种合理但要求使用者理解重入的契约。

来源：[Node.js Events / EventEmitter](https://nodejs.org/api/events.html#emitteremiteventname-args)

## 5. Redux：允许部分嵌套，但定义清楚阶段和快照

Redux 官方 Store API 规定：reducer 正在执行时不能再次 `dispatch`；但订阅 listener 是在 reducer 返回后调用，listener 可以再次 dispatch。Redux 会在每次 dispatch 前对订阅列表做快照，因此派发过程中订阅或取消订阅不会改变当前这一轮的监听者集合。

官方同时警告：嵌套 dispatch 可能导致 listener 看不到每一个中间状态，但保证在一次 dispatch 退出前，所有当时已订阅的 listener 最终会收到最新状态。listener 若无条件再次 dispatch，仍可能形成无限循环。

这是一种“允许嵌套，但限制可重入阶段并定义快照语义”的设计，不是 FIFO 非重入队列。

来源：[Redux Store API：subscribe](https://redux.js.org/api/store#subscribelistener)

## 6. Reactive Streams：规范要求信号不重叠

Reactive Streams 规范的 Publisher 规则 1.3 要求：发给 Subscriber 的 `onSubscribe`、`onNext`、`onError` 和 `onComplete` 必须 serially 发生。规范把 serial 定义为信号不重叠；在 JVM 中还要求调用之间存在 happens-before 关系。

这里的重点是外部可观察保证，而不是指定实现：规范没有要求一定使用 FIFO 队列，也没有要求一定异步。实现可以用队列、锁、原子状态或其他协调方式，只要同一 Subscriber 的信号不重叠且顺序成立。

这与本项目拟议的“回调中产生的新事件不嵌套，等待当前事件全部通知完再处理”最接近。

来源：[Reactive Streams JVM Specification](https://github.com/reactive-streams/reactive-streams-jvm#1-publisher-code)

## 7. 对本项目的启示

从这些框架可以归纳出四类策略：

1. **允许立即嵌套并记录契约**：Node.js。
2. **检测或拒绝危险重入**：`ObservableCollection<T>`、Redux reducer 阶段。
3. **把通知送入事件循环或内部队列**：Qt Queued Connection、非重入 dispatcher。
4. **规定不可重叠，但不规定内部实现**：Reactive Streams。

对于 Stats 库，“值立即生效、事件 FIFO 串行派发”兼顾同步 API 和稳定通知顺序。它更准确的名字是：

- 非重入事件派发器（non-reentrant event dispatcher）；
- 序列化通知队列（serialized notification queue）；
- 延迟通知队列（deferred notification queue）。

不应只用 `command queue` 搜索，因为命令队列通常表达“把状态修改也延迟执行”，而这里主要延迟的是通知。

## 8. 推荐搜索关键词

### 英文通用词

- `event handler reentrancy`
- `reentrant event dispatch`
- `nested event dispatch`
- `nested notification observer pattern`
- `observer reentrancy`
- `recursive notification loop`
- `feedback loop reactive system`
- `synchronous event listener ordering`
- `non-reentrant event dispatcher`
- `serialized event delivery`
- `non-overlapping notifications`
- `deferred notification queue`
- `breadth-first vs depth-first event dispatch`
- `run-to-completion event loop`

### 针对框架的词

- `.NET ObservableCollection BlockReentrancy CheckReentrancy`
- `Qt signal slot DirectConnection QueuedConnection`
- `Node EventEmitter nested emit synchronous`
- `Redux nested dispatch subscription snapshot`
- `Reactive Streams serial signals rule 1.3`

### 中文词

- `事件回调重入`
- `事件派发重入`
- `嵌套事件通知`
- `同步事件嵌套调用`
- `观察者模式重入`
- `递归通知` / `通知循环`
- `属性变更回调再次修改属性`
- `非重入事件队列`
- `事件序列化派发`
- `延迟通知队列`
- `广度优先 深度优先 事件派发`
- `信号槽 直接连接 队列连接`

最推荐从 `event handler reentrancy`、`nested event dispatch` 和 `serialized event delivery` 开始，而不是从“命令队列”开始。
