# Klrpxy Gameplay

面向 Unity 项目的玩法基础库集合。

## 包与版本

| 包 | 当前版本 | 说明 | 文档 |
| --- | --- | --- | --- |
| Klrpxy Gameplay Tags | v0.2.0 | 类型安全、具备层级关系的 Gameplay Tag。 | [中文](src/Klrpxy.Gameplay.Tags/README.zh-CN.md) · [English](src/Klrpxy.Gameplay.Tags/README.md) |
| Klrpxy Gameplay Stats | v0.3.1 | 可组合、自动传播的属性、资源与 Group 规则。 | [中文](src/Klrpxy.Gameplay.Stats/README.zh-CN.md) · [English](src/Klrpxy.Gameplay.Stats/README.md) |
| Klrpxy Gameplay Stats R3 | v0.3.0 | 为 Stats 增加可选的 R3 动态值、条件和观察 API。 | [中文](src/Klrpxy.Gameplay.Stats/README.zh-CN.md#可选-r3-adapter) · [English](src/Klrpxy.Gameplay.Stats/README.md#optional-r3-adapter) |

## 安装 Stats v0.3.1

当前安装包已在 Unity 2022.3.62f3 和 Unity 6000.5.0f1 中完成导入与运行验证。

v0.3.1 修复了 v0.3.0 在 Unity Test Framework 等不引用 Stats Runtime 的独立程序集上误报 `KGS003` 的问题；建议 v0.3.0 使用者升级。Stats R3 v0.3.0 与 Core v0.3.1 兼容。

1. 下载并导入 [Klrpxy Gameplay Tags v0.2.0](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.2.0/Klrpxy.Gameplay.Tags.0.2.0.unitypackage)。
2. 下载并导入 [Klrpxy Gameplay Stats v0.3.1](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.3.1/Klrpxy.Gameplay.Stats.0.3.1.unitypackage)。
3. 如需 R3 集成，先按 [R3 官方 Unity 安装说明](https://github.com/Cysharp/R3#unity)安装 R3 1.3.1，再导入 [Klrpxy Gameplay Stats R3 v0.3.0](https://github.com/klrpxy/Klrpxy.Gameplay/releases/download/v0.3.0/Klrpxy.Gameplay.Stats.R3.0.3.0.unitypackage)。

Stats 安装包不复制 Tags，R3 Adapter 也不捆绑 R3；请保持上述依赖顺序，避免项目中出现重复 DLL。

## Stats 示例

常见规则从 `ModifierSource` 开始，按照“来源、目标、运算、数值”的顺序书写：

```csharp
var combat = new ModifierSource();

combat.Modify(hero.StatSet.Power).Add(5f);
combat.Modify(hero.StatSet.Power).AddPercent(50f);
combat.Modify(hero.StatSet.Power)
    .Add(hero.StatSet.Shield, shield => shield * 0.5f);
```

系统自动处理依赖传播、Group 成员变化、Tag 条件与生命周期清理。完整玩法示例见 [`samples/Stats/BazaarGameplay.cs`](samples/Stats/BazaarGameplay.cs) 和可选的 [`BazaarGameplay.R3.cs`](samples/Stats/BazaarGameplay.R3.cs)。
