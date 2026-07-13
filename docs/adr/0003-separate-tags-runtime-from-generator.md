---
status: accepted
---

# 将 Tags 运行时与源生成器分离

Tags 提供独立的运行时 module，承载 `TagSet`、`TagQuery` 的通用实现；源生成器只根据 Tag Table 生成 `Tag`、层级化 Tag 类型、静态入口，以及绑定运行时实现所需的最薄代码。这样，运行时行为（包括未来的 `TagSet` 变更通知）不再藏在生成器发射的源码中，Stats 等使用方可复用同一份运行时实现。

`Tag` 继续由生成器生成，而不放入 runtime DLL。当前由私有构造函数提供的强约束必须保留：用户不能自行构造或派生 `Tag`，未在 Tag Table 声明的名称不能在运行时成为 Tag。生成代码与用户脚本同处于消费者程序集，C# 不能只向生成代码开放 runtime `Tag` 的构造方式，因此不采用把 `Tag` 直接放入 runtime DLL 的方案。

仍发布单一 `.unitypackage`，其中包含仅供编译期加载的分析器 DLL，以及供游戏程序集和 Stats 引用的 Tags runtime DLL。使用者保持一次导入的安装方式；包内两个 DLL 的职责和 Unity 导入设置必须彼此独立。

Tag 的作者 interface 使用声明在带 `[GenerateGameplayTags]` 的 `static partial` 根类中的紧凑 Tag Table，而不使用嵌套 C# 类表达层级。Tag Table 保持每行一个点分路径的可扫读形式，生成器从该常量读取定义并继续生成当前简洁的使用门面，例如 `CombatTags.Unit.Enemy.Boss`。这样，Tag 定义与其根类同文件，且允许多个根类各自声明自己的 Tag Table。

同一消费者程序集中的多个 Tags 根类共享一个 Tag universe：`CombatTags`、`UiTags` 等根类只是组织和命名入口，其 Tag 可以放入同一个 `TagSet`，并由同一个 `TagQuery` 查询。一个完整 Tag 路径在该 universe 中只能声明一次；跨根重复声明必须产生诊断。

Tags 根类可以位于不同命名空间，且不需要额外配置即可互相配合。生成器负责为同一消费者程序集提供共同的生成类型和薄门面；这些内部类型的位置不属于作者 interface。

每个 Tags 根类以固定名称的 `private const string TagTable` 声明其 Tag Table。生成器只读取该字段，不使用额外属性标记；字段名或类型不符合约定时产生诊断，避免把根类中的其他字符串误当作 Tag 定义。

每个根类必须且只能声明一份 `TagTable`。根类可以是 `partial` 并分布在多个文件，但其完整词表仍集中在这一份常量中；生成器不合并同一根类的多个 Tag Table。

Tags 根类继续使用 `[GenerateGameplayTags]` 显式标记，并必须是 `public static partial class`。只有这种根类中的约定 `TagTable` 才参与生成；普通类的同名字符串不会被识别为 Tag 定义。

类内 Tag Table 沿用现有文本规则：每个非空、非 `#` 开头的行定义一个点分 Tag 路径；空行用于分组，`#` 开头的行用于说明，行首尾空白忽略。

将 `TagSet`、`TagQuery` 的实现移入 runtime DLL 不改变游戏代码的使用写法。调用方仍使用非泛型的 `new TagSet()`、`TagQuery.Has(...)` 与 `Matches(...)`；生成器只保留把这些现有 interface 连接到 runtime 实现所需的薄代码。

旧版外部 Tag Table 不与类内 Tag Table 并存。升级后的项目必须将词表迁移到各 Tags 根类的 `TagTable`；生成器提供明确诊断和发布说明协助迁移，避免双来源带来的优先级、重复路径和维护分叉。

`TagSet.OnChanged` 是此次 Tags runtime 改动的一部分。它只在集合实际变化时派发不可变的 `TagSetChange { Tag, Kind }`，其中 `Kind` 为 `Added` 或 `Removed`；事件不携带完整集合。完整的 Stats 依赖与重算语义见 `src/Klrpxy.Gameplay.Stats/DESIGN.md` 的“TagSet 变化通知”章节，本文不重复记录。

本次不兼容的作者 interface 变更发布为 `v0.2.0`，安装包命名为 `Klrpxy.Gameplay.Tags.0.2.0.unitypackage`。发布说明必须给出从外部 Tag Table 迁移到类内 `TagTable` 的步骤。

经独立原型验证，runtime DLL 使用公开的泛型实现承载 `TagSetRuntime<TTag>` 与 `TagQueryRuntime<TTag>`；源生成器在每个消费者程序集生成唯一的非 `partial`、`sealed`、私有构造的 `Tag` 类型，以及维持现有调用写法的薄门面。运行时通过生成代码提供的同类或后代匹配行为工作，不依赖消费者的具体 `Tag` 类型。这样既保留受控 Tag 构造和跨命名空间的单一 Tag universe，也让通用集合、查询与变化通知集中在 runtime DLL。公开泛型实现是跨程序集技术约束，不是推荐给游戏代码的 interface。
