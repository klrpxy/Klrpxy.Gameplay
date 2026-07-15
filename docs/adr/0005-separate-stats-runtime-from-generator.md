---
status: accepted
---

# 将 Stats 运行时与 Source Generator 分离

Stats Runtime 独立承载 Stat、RangeStat、Resource、Modifier、Subject、Group 和响应式传播；Source Generator 只发现全局或普通命名空间中的顶层、非泛型 `partial StatSet`，为合法的只读属性生成强类型 StatKey、不可变 getter与 Runtime 绑定所需的最薄只读成员描述。生成器只依赖 Roslyn，不生成计算或生命周期行为。

生成代码不能直接调用 Runtime 的 internal 构造器。Runtime 在 `StatSet` 上提供受保护的 Key 创建 seam，生成代码通过它创建静态只读 Key；成员描述保存属性路径、种类、类型以及从 StatSet 实例读取成员的 getter，不能在静态元数据中直接引用实例属性。普通玩法代码只使用生成 Key，不接触这条桥接 seam。

Stats `.unitypackage` 分别分发 Runtime DLL 与 Analyzer DLL，并把 Tags 作为独立前置包依赖。Runtime 启用 Unity 运行平台且允许默认游戏程序集引用；Analyzer 禁用运行平台并使用 `RoslynAnalyzer` 标签。Stats 包不复制 Tags DLL，也不携带 Microsoft.CodeAnalysis DLL。

Analyzer 自身不依赖 Tags Runtime，并在编译入口检查最低兼容 Tags Runtime。缺少依赖时产生带稳定编号和安装指引的错误，使 `.unitypackage` 无法声明包依赖的限制不会退化为难以理解的类型缺失。

该 seam 已在 Unity 2022.3 与 Unity 6 中通过独立原型验证：两个版本都能在默认游戏程序集中生成并使用 StatKey 和成员描述；只安装 Stats 时会得到明确的 Tags 安装诊断。正式实现保留 seam 与导入设置，但不复制一次性原型代码。
