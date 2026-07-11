# Klrpxy Gameplay Tags

[English](README.md)

Klrpxy Gameplay Tags 会把一份简短的文本文件生成 Unity 项目中类型安全、具备层级关系的 Tag。当玩法代码需要表达 `Unit.Enemy.Boss` 这类分类，而不希望在各处传递字符串时，可以使用它。

## 快速开始

1. 从 [v0.1.0 Release](https://github.com/klrpxy/Klrpxy.Gameplay/releases/tag/v0.1.0) 下载 `Klrpxy.Gameplay.Tags.0.1.0.unitypackage`。
2. 在 Unity 中选择 **Assets > Import Package > Custom Package**，导入下载的文件。
3. 在 `Assets` 下创建一个文本文件，名称必须严格为 `GameplayTags.KlrpxyGameplayTags.additionalfile`：

   ```text
   Unit.Enemy.Boss
   Ability.Cast
   Ability.Channel
   State.Stunned
   ```

4. 在将要使用这些 Tag 的同一个 Unity 程序集中，新建一个脚本：

   ```csharp
   using Klrpxy.Gameplay.Tags;

   [GenerateGameplayTags]
   public static partial class Tags
   {
   }
   ```

5. 等待 Unity 编译完成后，即可在代码中使用生成的 Tag：

   ```csharp
   Tag boss = Tags.Unit.Enemy.Boss;
   Tag enemy = boss.GetParent(); // Tags.Unit.Enemy
   ```

如果 `Tags.Unit.Enemy.Boss` 能编译通过或出现在代码补全中，说明配置成功。如果没有生成，请先在 Unity Console 中查看 `KTAG` 诊断，并核对第 3 步的文件名。

## 为什么需要 Tag Table？

Tag Table 是项目词汇的唯一来源。每个非空行声明一个完整 Tag 路径；声明子节点时，它的父节点也会一并生成。上面的示例会生成 `Unit`、`Unit.Enemy`、`Unit.Enemy.Boss`、`Ability`、`Ability.Cast`、`Ability.Channel`、`State` 和 `State.Stunned`。

这样一来，项目中的代码会安全地共享同一套名称。像 `Unit.Enemey.Boss` 这样的拼写错误会在编译时被发现，而不会在运行时变成难以察觉的字符串不匹配。

在 `Assets` 下只能保留一个 Tag Table。路径区分大小写；每个路径段以大写字母开头，后续只能使用字母或数字。允许空行与整行注释。

```text
# 合法路径
Unit.Player
Unit.Enemy.Boss

# 非法：每个路径段必须以大写字母开头
unit.Enemy
```

## 保存对象拥有的 Tag

`TagSet` 保存对象显式拥有且不重复的 Tag。它不会重复存储父 Tag，但匹配时会理解它们的层级关系。

```csharp
var tags = new TagSet();
tags.Add(Tags.Unit.Enemy.Boss);

bool isEnemy = tags.Has(Tags.Unit.Enemy);             // true：Boss 属于 Enemy
bool isExactlyEnemy = tags.HasExact(Tags.Unit.Enemy); // false：只添加了 Boss
bool isBoss = tags.HasExact(Tags.Unit.Enemy.Boss);    // true
```

分类判断使用 `Has`；只有精确 Tag 有意义时使用 `HasExact`。

## 用 TagQuery 编写可复用条件

当判断需要组合多个条件，或会被重复使用时，使用 `TagQuery`。查询是不可变的：重复求值不会改变查询本身，也不会改变 `TagSet`。

```csharp
TagQuery hostileCaster = TagQuery.All(
    TagQuery.Has(Tags.Unit.Enemy),
    TagQuery.Any(Tags.Ability.Cast, Tags.Ability.Channel),
    TagQuery.None(Tags.State.Stunned));

var casterTags = new TagSet();
casterTags.Add(Tags.Unit.Enemy.Boss);
casterTags.Add(Tags.Ability.Cast);

bool canAct = hostileCaster.Matches(casterTags); // true
```

`All` 要求所有条件都满足，`Any` 要求至少满足一个，`None` 要求没有条件匹配。只有一个 Tag 时，可以直接使用简洁的 `TagQuery.Has(tag)` 和 `TagQuery.HasExact(tag)`。

## 排查问题

| 现象 | 检查项 |
| --- | --- |
| 没有生成 `Tags` 成员 | Tag Table 是否命名为 `GameplayTags.KlrpxyGameplayTags.additionalfile`、是否只在 `Assets` 下存在一份，以及 Unity 是否已完成编译。 |
| `KTAG001` 或 `KTAG002` | 在消费者程序集中，必须且只能有一个带 `[GenerateGameplayTags]` 的非泛型、顶层 `static partial` 类。 |
| `KTAG003`、`KTAG004` 或 `KTAG005` | 修正 Tag Table 中被定位的行：使用合法路径段、移除重复的显式路径，并避免保留路径段。 |
| `KTAG006` 或 `KTAG007` | 在 `Assets` 下创建且只创建一个 Tag Table。 |

## 高级与维护说明

生成器支持 Unity 2022.3 LTS 和 Unity 6。它以基于 Roslyn 3.8.0 的 .NET Standard 2.0 分析器形式分发。导入的 DLL 必须保留大小写准确的 `RoslynAnalyzer` 标签，并禁用运行时平台兼容性；不要在它旁边放置 `Microsoft.CodeAnalysis` DLL。

在本仓库根目录构建本地安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Build-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe"
```

日常开发运行 .NET/Roslyn 测试：

```powershell
dotnet test Klrpxy.Gameplay.sln -c Release
```

发布前，为每个支持的编辑器运行 Unity 包烟测：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe" -UnityVersion 2022.3.62f3
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 6000.5.0f1>/Editor/Unity.exe" -UnityVersion 6000.5.0f1
```

补充：空的 `All`、`Any` 和 `None` 查询分别求值为 `true`、`false` 和 `true`。
