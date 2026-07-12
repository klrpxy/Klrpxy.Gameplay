# Klrpxy Gameplay Tags

[English](README.md)

Klrpxy Gameplay Tags 会把类内 Tag Table 生成 Unity 项目中类型安全、具备层级关系的 Tag。当玩法代码需要表达 `Unit.Enemy.Boss` 这类分类，而不希望在各处传递字符串时，可以使用它。

## 快速开始

1. 从 v0.2.0 Release 下载 `Klrpxy.Gameplay.Tags.0.2.0.unitypackage`。
2. 在 Unity 中选择 **Assets > Import Package > Custom Package**，导入下载的文件。
3. 在将要使用这些 Tag 的脚本所在的同一个 Unity 程序集中，新建一个脚本。如果项目没有使用程序集定义文件（`.asmdef`），所有脚本默认都满足这项要求。在带标记的根类中声明项目唯一的 **Tag Table**：

   ```csharp
   using Klrpxy.Gameplay.Tags;

   [GenerateGameplayTags]
   public static partial class Tags
   {
       private const string TagTable = @"Unit.Enemy.Boss
Ability.Cast
Ability.Channel
State.Stunned";
   }
   ```

5. 等待 Unity 编译完成后，即可在代码中使用生成的 Tag：

   ```csharp
   Tag boss = Tags.Unit.Enemy.Boss;
   Tag enemy = boss.GetParent(); // Tags.Unit.Enemy
   ```

如果 `Tags.Unit.Enemy.Boss` 能编译通过或出现在代码补全中，说明配置成功。如果没有生成，请先在 Unity Console 中查看 `KTAG` 诊断，并核对第 3 步的 `TagTable` 声明。

## 从 v0.1.0 迁移

1. 删除 `Assets` 中的 `GameplayTags.KlrpxyGameplayTags.additionalfile`。
2. 将其中内容复制到带标记根类唯一的 `private const string TagTable` 字段，格式见上文。
3. 导入 v0.2.0 安装包；该包同时安装分析器 DLL 与 runtime DLL。

不要让旧外部文件与类内字段同时存在。v0.2.0 检测到旧文件时会报告 `KTAG007`。

## 为什么使用 Tag Table？

Tag Table 是项目词汇的唯一来源。每个非空行声明一个完整 Tag 路径；声明子节点时，它的父节点也会一并生成。上面的示例会生成 `Unit`、`Unit.Enemy`、`Unit.Enemy.Boss`、`Ability`、`Ability.Cast`、`Ability.Channel`、`State` 和 `State.Stunned`。

这样一来，项目中的代码会安全地共享同一套名称。像 `Unit.Enemey.Boss` 这样的拼写错误会在编译时被发现，而不会在运行时变成难以察觉的字符串不匹配。

带标记根类中只能保留一个 `private const string TagTable` 字段。路径区分大小写；每个路径段以大写字母开头，后续只能使用字母或数字。允许空行与整行注释，但不支持在 Tag 后添加行内注释。

```text
# 合法路径
Unit.Player
Unit.Enemy.Boss

# 非法：每个路径段必须以大写字母开头
unit.Enemy
```

## 为对象添加 Tag

为每个对象准备一个 `TagSet`：它是该对象显式拥有的 Tag 集合。添加 `Unit.Enemy.Boss` 时，不会把父 Tag 作为独立成员重复保存；但在提问时，它仍会理解 Boss 属于 Enemy。

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

三个组合器的含义如下：

- `All`：所有条件都必须匹配。
- `Any`：至少一个条件匹配即可。
- `None`：没有任何条件可以匹配。

只有一个 Tag 时，可以直接使用简洁的 `TagQuery.Has(tag)` 和 `TagQuery.HasExact(tag)`。

## 排查问题

| 现象 | 检查项 |
| --- | --- |
| 没有生成 `Tags` 成员 | 带标记根类是否恰有一个 `private const string TagTable` 字段，以及 Unity 是否已完成编译。 |
| `KTAG001` 或 `KTAG002` | 在游戏脚本所在的程序集中，必须且只能有一个带 `[GenerateGameplayTags]` 的非泛型、顶层 `static partial` 类。 |
| `KTAG003`、`KTAG004` 或 `KTAG005` | 修正 Tag Table 中被定位的行：使用合法路径段、移除重复的显式路径，并避免保留路径段。 |
| `KTAG006` | 为带标记根类添加且只添加一个 `private const string TagTable` 字段。 |
| `KTAG007` | 删除旧 `GameplayTags.KlrpxyGameplayTags.additionalfile`，并将其内容迁入 `TagTable`。 |

## 高级与维护说明

生成器支持 Unity 2022.3 LTS 和 Unity 6。安装包同时包含基于 Roslyn 3.8.0 的 .NET Standard 2.0 分析器和独立 runtime DLL。直接使用 Release 安装包时无需改动导入设置。包维护者必须保留分析器 DLL 大小写准确的 `RoslynAnalyzer` 标签并禁用其运行时平台兼容性，同时保持 runtime DLL 的运行时平台兼容性；也不要在任一 DLL 旁放置 `Microsoft.CodeAnalysis` DLL。

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
