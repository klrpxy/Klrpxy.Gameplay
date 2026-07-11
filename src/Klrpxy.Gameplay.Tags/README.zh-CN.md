# Klrpxy Gameplay Tags

[English](README.md)

`KlrpxyGameplayTags.dll` 是一个 Unity 源码生成器。它从项目唯一的 Tag Table 生成引擎无关的 `Tag`、`TagSet` 和 `TagQuery` API。相同的 .NET Standard 2.0 生成器 DLL 支持 Unity 2022.3 LTS 与 Unity 6，并基于 Roslyn 3.8.0 构建。

## 安装

在仓库根目录构建本地安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Build-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe"
```

在 Unity 中选择 **Assets > Import Package > Custom Package**，然后选择生成的 `artifacts/Klrpxy.Gameplay.Tags.0.1.0.unitypackage`。该包会将 `KlrpxyGameplayTags.dll` 作为分析器导入：运行时平台兼容性已禁用，且 DLL 带有大小写准确的 `RoslynAnalyzer` 标签。不要在生成器旁分发或复制 `Microsoft.CodeAnalysis` DLL。

## 声明 Tag Table

在 `Assets` 下创建且只创建一个名为 `GameplayTags.KlrpxyGameplayTags.additionalfile` 的文件。其中的 `KlrpxyGameplayTags` 后缀必须与生成器程序集名称保持一致。

每行写一个区分大小写的完整路径。每个路径段必须匹配 `[A-Z][A-Za-z0-9]*`。空行和以 `#` 开头的整行注释会被忽略；不支持行内注释。

```text
# Unit tags
Unit.Enemy.Boss
Ability.Cast
```

## 生成并使用 Tag

在消费者程序集的一个非泛型、顶层静态 partial 类上标记特性：

```csharp
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class Tags
{
}
```

生成器会补全并生成声明的层级。每个节点都是一个规范、不可变的 `Tag`：

```csharp
Tag boss = Tags.Unit.Enemy.Boss;
Tag parent = boss.GetParent(); // Tags.Unit.Enemy
```

## TagSet

`TagSet` 保存对象显式拥有的唯一 Tag，不会存储推断出的祖先。`Has` 使用方向性的 Tag Match：已拥有的后代会匹配被查询的祖先；`HasExact` 只检查精确成员。

```csharp
var tags = new TagSet();
tags.Add(Tags.Unit.Enemy.Boss);

bool broadMatch = tags.Has(Tags.Unit.Enemy);
bool exactMatch = tags.HasExact(Tags.Unit.Enemy.Boss);
```

## TagQuery

`TagQuery` 不可变且可复用。通过 `Has`、`HasExact`、`All`、`Any` 和 `None` 创建；三个组合器也支持直接传入 Tag。空 `All`、`Any` 和 `None` 分别求值为 `true`、`false` 和 `true`。

```csharp
TagQuery hostileCaster = TagQuery.All(
    TagQuery.Has(Tags.Unit.Enemy),
    TagQuery.Any(Tags.Ability.Cast, Tags.Ability.Channel));

bool matches = hostileCaster.Matches(tags);
```

## 诊断

- `KTAG001`：生成根无效。
- `KTAG002`：存在多个生成根。
- `KTAG003`：Tag 路径段无效。
- `KTAG004`：显式 Tag 声明重复。
- `KTAG005`：使用了保留路径段。
- `KTAG006`：缺少 Tag Table。
- `KTAG007`：Tag Table 不唯一。

Tag Table 相关诊断会定位到对应 AdditionalFile 的错误行。

## 验证

日常开发使用快速 .NET/Roslyn 测试：

```powershell
dotnet test Klrpxy.Gameplay.sln -c Release
```

仅在发布边界运行 Unity 烟测。它会创建临时宿主项目，验证两个受支持编辑器，但不会将宿主项目加入安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 2022.3.62f3>/Editor/Unity.exe" -UnityVersion 2022.3.62f3
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Smoke-Test-UnityPackage.ps1 -UnityPath "<Unity 6000.5.0f1>/Editor/Unity.exe" -UnityVersion 6000.5.0f1
```

若要交互验证 Unity 6，可将同一个 `.unitypackage` 导入测试项目，创建 Tag Table 和标注根，然后在运行时确认生成的后代 Tag 及其父级。
