---
status: accepted
---

# 使用强类型 StatSet 与生成的 StatKey

StatSet 使用继承 `StatSet` 的强类型 C# 类声明，Source Generator 为其中合法的 `Stat` 与 `RangeStat` 属性生成当前构建内稳定、携带目标类型的静态 StatKey。Modifier 统一使用这些 Key 指定目标，不接受裸字符串；普通玩法代码不能动态构造 Key。

Resource 由成员描述参与 Runtime 绑定，但第一版不生成 ResourceKey，因为 Resource 不接受 Modifier。生成的 Key 保存不可变 getter，Runtime 通过 getter 从兼容的 StatSet 实例取得属性，不使用反射或字符串查找。

这让作者以集中、可扫读的属性类定义 Stat，同时让编译器拒绝标量与区间目标的错误组合。跨版本持久化 Key、动态 StatSet 和运行时字符串目录留到出现真实需求后再设计。
