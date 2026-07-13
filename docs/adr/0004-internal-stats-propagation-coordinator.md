---
status: accepted
---

# 将 Stats 响应式传播集中到内部协调器

Stats 以完全内部、按当前 Gameplay 线程共享的传播协调器统一拥有依赖图、循环检测、受影响数值重算、FIFO 事件派发与订阅清理；`Stat`、`RangeStat`、`Resource`、`StatsOwner` 和 `StatsOwnerGroup` 不协调彼此的传播顺序。

每次公开修改开启一个 Propagation Round。协调器先完成全部受影响节点的重算，再为每个实际变化的节点派发一次“轮次开始值到最终值”事件；回到原值时不派发。回调引起的修改开启新轮次并追加到 FIFO 队尾，事件不嵌套。添加会形成最终值依赖环的关系必须在改变图和值之前原子拒绝。

监听者异常由 `StatsDiagnostics` 报告，并继续通知其他监听者和排空队列。动态事件反馈超过内部预算时报告错误并清空剩余队列，不能让监听者异常或反馈循环导致游戏崩溃。

第一版不公开 `StatsContext`、Dispatcher、`ForceRefresh`、`Recalculate`、`BeginBatch` 或 `DeferRefresh`。合法外部 ValueInput 必须主动通知变化；只有出现多个隔离模拟世界或多线程模型的真实需求时，才重新考虑显式 Context。
