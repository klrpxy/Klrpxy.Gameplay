using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Stats.Generator;
using Klrpxy.Gameplay.Tags.Generator;
using Klrpxy.Gameplay.Tags.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace KlrpxyGameplayStats.Tests
{
    public sealed class GameplayStatsGeneratorTests
    {
        [Fact]
        public void Roslyn38ConsumerUsesSingleInputDynamicFluentModifiers()
        {
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed partial class HeroStats : StatSet
    {
        public Stat Power { get; } = new Stat(100f);
        public Stat Rage { get; } = new Stat(10f);
        public RangeStat Damage { get; } = new RangeStat(5f, 10f);
        public Resource Energy { get; } = new Resource(2f);
    }
    public sealed class Hero : StatSubject<HeroStats>
    {
        public Hero() : base(new HeroStats()) { }
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var hero = new Hero();
            var source = new ModifierSource();
            source.Modify(hero.StatSet.Power).Add(hero.StatSet.Rage, value => value * 0.5f);
            source.Modify(hero.StatSet.Power).AddPercent(hero.StatSet.Damage, range => range.Max);
            source.Modify(hero.StatSet.Power).Multiply(hero.StatSet.Energy, value => value);
            bool initial = hero.StatSet.Power.FinalValue == 231f;
            hero.StatSet.Rage.BaseValue = 20f;
            hero.StatSet.Energy.Set(3f);
            return initial && hero.StatSet.Power.FinalValue == 363f;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedRangeStatKeyTargetsRangeModifier()
        {
            // 验证生成的 RangeStatKey 可作为 Modifier 的唯一目标并改变 FinalRange。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed partial class WeaponStatSet : StatSet
    {
        public RangeStat Damage { get; } = new RangeStat(10f, 15f);
    }
    public sealed class Weapon : StatSubject<WeaponStatSet>
    {
        public Weapon() : base(new WeaponStatSet()) { }
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var weapon = new Weapon();
            var source = new ModifierSource();
            var group = new StatSubjectGroup().Add(weapon);
            source.For(group).Modify(WeaponStatSet.DamageKey)
                .Override(new FloatRange(40f, 20f));
            return weapon.StatSet.Damage.FinalRange.Min == 20f
                && weapon.StatSet.Damage.FinalRange.Max == 40f;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedRangeStatKeyGetsDeclaredRangeStat()
        {
            // 验证生成的 RangeStatKey 能从声明的 StatSet 取得区间属性。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed partial class WeaponStatSet : StatSet
    {
        public RangeStat Damage { get; } = new RangeStat(10f, 20f);
        public Resource Ammo { get; } = new Resource(3f);
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            RangeStat damage;
            return WeaponStatSet.DamageKey.TryGet(new WeaponStatSet(), out damage)
                && damage != null;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void DerivedStatSetReusesBaseStatKeyWithBuildPath()
        {
            // 验证派生 StatSet 复用基类 Key，且 Key 保留完整构建内路径。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public abstract partial class CharacterStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
    public sealed partial class EnemyStatSet : CharacterStatSet { }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            Stat health;
            return CharacterStatSet.HealthKey.TryGet(new EnemyStatSet(), out health)
                && health != null
                && CharacterStatSet.HealthKey.GetPath().EndsWith(""Consumer.CharacterStatSet.Health"");
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void NonAutoStatPropertyReportsDiagnostic()
        {
            // 验证带自定义 getter 的 Stat 属性会得到可操作的生成诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health => new Stat(100f);
    }
}");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public void NonPartialStatSetReportsDiagnostic()
        {
            // 验证缺少 partial 的 StatSet 会得到稳定的声明诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
}");

            Assert.Equal("KGS002", Assert.Single(result.Diagnostics).Id);
        }

        [Theory]
        [InlineData("public partial class Outer { public partial class NestedStatSet : StatSet { public Stat Health { get; } = new Stat(1f); } }")]
        [InlineData("public partial class GenericStatSet<T> : StatSet { public Stat Health { get; } = new Stat(1f); }")]
        public void NestedOrGenericStatSetReportsDiagnostic(string declaration)
        {
            // 验证嵌套或泛型 StatSet 会在类型名位置报告声明诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult("using Klrpxy.Gameplay.Stats; namespace Consumer { " + declaration + " }");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS002", diagnostic.Id);
            Assert.Equal("Consumer.cs", diagnostic.Location.SourceTree.FilePath);
        }

        [Fact]
        public void UnrelatedCompilationWithoutStatsRuntimeDoesNotReportTagsDiagnostic()
        {
            // 验证 Unity 中未引用 Stats Runtime 的独立程序集不会收到 Tags 安装误报。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false,
                includeStatsRuntime: false);

            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void MissingTagsRuntimeReportsInstallationDiagnostic()
        {
            // 验证缺少 Tags Runtime 时会得到明确的安装诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false);

            Assert.Equal("KGS003", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void OlderTagsRuntimeReportsInstallationDiagnostic()
        {
            // 验证低于最低兼容版本的 Tags Runtime 会得到安装诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false,
                tagsRuntimeReference: CreateTagsRuntimeReference("0.1.0.0"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS003", diagnostic.Id);
            Assert.Contains("Gameplay Tags v0.2.1", diagnostic.GetMessage());
        }

        [Fact]
        public void TagsRuntimeWithoutStatsIntegrationReportsInstallationDiagnostic()
        {
            // 验证程序集名称和版本看似兼容、但缺少 Stats 集成类型时仍会得到安装诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false,
                tagsRuntimeReference: CreateTagsRuntimeReference("1.0.0.0"));

            Assert.Equal("KGS003", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void TagsRuntimeBeforeMinimumPackageVersionReportsInstallationDiagnostic()
        {
            // 验证即使集成类型齐全，低于最低发布版本的 Tags Runtime 仍会得到安装诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false,
                tagsRuntimeReference: CreateTagsRuntimeReference("0.2.0.0", @"
namespace Klrpxy.Gameplay.Tags.Runtime
{
    public interface IGameplayTag { }
    public interface IHierarchicalGameplayTag : IGameplayTag { }
    public interface ITagSet { }
    public interface ITagQuery { }
    public sealed class TagSetChange { }
}"));

            Assert.Equal("KGS003", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void InvalidStatSetDoesNotPreventValidNeighborGeneration()
        {
            // 验证无效 StatSet 失败关闭时，同一编译中的合法邻居仍会生成代码。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed partial class InvalidStatSet : StatSet
    {
        public Stat Health => new Stat(100f);
    }
    public sealed partial class ValidStatSet : StatSet
    {
        public Stat Attack { get; } = new Stat(20f);
    }
}");

            Assert.Equal("KGS001", Assert.Single(result.Diagnostics).Id);
            Assert.Single(result.Results.Single().GeneratedSources);
        }

        [Theory]
        [InlineData("public Stat Health { get; private set; } = new Stat(100f);")]
        [InlineData("public static Stat Health { get; } = new Stat(100f);")]
        [InlineData("public Stat this[int index] { get { return new Stat(100f); } }")]
        [InlineData("public virtual Stat Health { get; } = new Stat(100f);")]
        public void InvalidStatMemberShapesReportPropertyDiagnostic(string declaration)
        {
            // 验证非法 Stat 属性形状会在其声明位置报告诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet
    {
        " + declaration + @"
    }
}");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS001", diagnostic.Id);
            Assert.Equal("Consumer.cs", diagnostic.Location.SourceTree.FilePath);
        }

        [Fact]
        public void ImplicitlyHiddenStatPropertyReportsDiagnostic()
        {
            // 验证派生 StatSet 不能用 new 隐藏已有 Stat 属性。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class BaseStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
    public partial class DerivedStatSet : BaseStatSet
    {
        public Stat Health { get; } = new Stat(50f);
    }
}");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS001", diagnostic.Id);
            Assert.Equal("Consumer.cs", diagnostic.Location.SourceTree.FilePath);
        }

        [Fact]
        public void OverriddenStatPropertyReportsDiagnostic()
        {
            // 验证派生 StatSet 不能 override 已声明的 Stat 属性。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class BaseStatSet : StatSet { public virtual Stat Health { get; } = new Stat(1f); }
    public partial class DerivedStatSet : BaseStatSet { public override Stat Health { get; } = new Stat(2f); }
}");

            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("KGS001", diagnostic.Id));
            Assert.Equal(2, result.Diagnostics.Length);
        }

        [Fact]
        public void ExistingStatKeyMemberReportsConflictDiagnostic()
        {
            // 验证已有同名 Key 成员时，生成器会拒绝生成部分 StatSet。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
        public object HealthKey { get; } = null;
    }
}");

            Assert.Equal("KGS004", Assert.Single(result.Diagnostics).Id);
            Assert.Empty(result.Results.Single().GeneratedSources);
        }

        [Fact]
        public void ResourceDoesNotGenerateOrConflictWithKey()
        {
            // 验证 Resource 进入成员描述但不生成 Key，也不与同名成员冲突。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet
    {
        public Resource Mana { get; } = new Resource(50f);
        public object ManaKey { get; } = null;
    }
    public static class ConsumerContract
    {
        public static bool Verify() => new HeroStatSet().Mana != null;
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedMemberBindingRejectsNullPropertyWithPath()
        {
            // 验证 Runtime 会使用真实生成成员描述拒绝空属性并保留完整路径。
            Compilation compilation = RunGenerator(@"
using System;
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet { public Stat Health { get; } }
    public sealed class Hero : StatSubject<HeroStatSet> { public Hero() : base(new HeroStatSet()) { } }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            try { new Hero(); return false; }
            catch (InvalidOperationException exception) { return exception.Message.Contains(""HeroStatSet.Health""); }
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedStatKeyGetsDeclaredStatFromSubjectsStatSet()
        {
            // 验证生成的 StatKey 能从 Subject 的实际 StatSet 取得声明的 Stat。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public sealed class Hero : StatSubject<HeroStatSet>
    {
        public Hero(HeroStatSet statSet) : base(statSet) { }
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var statSet = new HeroStatSet();
            var hero = new Hero(statSet);
            Stat health;
            return HeroStatSet.HealthKey.TryGet(hero.StatSet, out health)
                && object.ReferenceEquals(statSet.Health, health);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedStatKeyRejectsIncompatibleStatSet()
        {
            // 验证生成的 StatKey 不会接受类型不兼容的 StatSet。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public sealed partial class ItemStatSet : StatSet
    {
        public Stat Price { get; } = new Stat(25f);
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            Stat health;
            return !HeroStatSet.HealthKey.TryGet(new ItemStatSet(), out health)
                && health == null;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedMemberDescriptionsIncludeAllDeclaredValueKinds()
        {
            // 验证生成的成员描述同时包含 Stat、RangeStat 和 Resource。
            Compilation compilation = RunGenerator(@"
using System.Collections.Generic;
using System.Linq;
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
        public RangeStat Damage { get; } = new RangeStat(10f, 15f);
        public Resource Mana { get; } = new Resource(50f);

        public bool HasCompleteGeneratedDescription()
        {
            var members = new List<StatMemberDescriptor>();
            AppendGeneratedMembers(members);
            return members.Count == 3
                && members.Any(member => member.Kind == StatMemberKind.Stat)
                && members.Any(member => member.Kind == StatMemberKind.RangeStat)
                && members.Any(member => member.Kind == StatMemberKind.Resource);
        }
    }

    public static class ConsumerContract
    {
        public static bool Verify() => new HeroStatSet().HasCompleteGeneratedDescription();
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void MultiFilePartialStatSetGeneratesOnce()
        {
            // 验证分散在多个声明中的 partial StatSet 只生成一份代码。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
    }

    public sealed partial class HeroStatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var statSet = new HeroStatSet();
            Stat health;
            return HeroStatSet.HealthKey.TryGet(statSet, out health);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void KeywordStatSetAndMemberNamesGenerateValidSource()
        {
            // 验证使用 C# 关键字命名的 StatSet 和成员仍能生成有效源码。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class @class : StatSet
    {
        public Stat @event { get; } = new Stat(100f);
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var statSet = new @class();
            Stat value;
            return @class.eventKey.TryGet(statSet, out value);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void StatSetsWithCollidingDisplayNamesUseDistinctGeneratedFiles()
        {
            // 验证显示名称碰撞的不同 StatSet 会使用互不冲突的生成文件名。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace A
{
    public sealed partial class B_C : StatSet
    {
        public Stat First { get; } = new Stat(1f);
    }
}

namespace A_B
{
    public sealed partial class C : StatSet
    {
        public Stat Second { get; } = new Stat(2f);
    }
}

namespace Consumer
{
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            Stat first;
            Stat second;
            return A.B_C.FirstKey.TryGet(new A.B_C(), out first)
                && A_B.C.SecondKey.TryGet(new A_B.C(), out second);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedTagQueryControlsSubjectModifierThroughPublicApi()
        {
            // 验证玩法代码可直接用现有 TagQuery 声明 Modifier 条件并随 Subject Tags 自动启停。
            Compilation compilation = RunStatsAndTagsGenerators(@"
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class GameTags
{
    private const string TagTable = ""Unit.Ally"";
}

namespace Consumer
{
    public sealed partial class HeroStats : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public sealed class Hero : StatSubject<HeroStats>
    {
        public Hero() : base(new HeroStats(), GameTags.Unit.Ally) { }
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var hero = new Hero();
            var source = new ModifierSource();
            var group = new StatSubjectGroup().Add(hero);
            source.For(group)
                .WhereTargetMatches(TagQuery.Has(GameTags.Unit.Ally))
                .Modify(HeroStats.HealthKey)
                .Add(25f);
            bool enabled = hero.StatSet.Health.FinalValue == 125f;
            hero.RemoveTag(GameTags.Unit.Ally);
            bool disabled = hero.StatSet.Health.FinalValue == 100f;
            hero.Tags.Add(GameTags.Unit.Ally);
            return enabled && disabled && hero.StatSet.Health.FinalValue == 125f;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedTagQueryFiltersGroupMembersAsTagsChange()
        {
            // 验证 Group 共享规则通过现有 TagQuery 自动跟随各 Subject 的 Tags 变化。
            Compilation compilation = RunStatsAndTagsGenerators(@"
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class GameTags
{
    private const string TagTable = ""Unit.Ally"";
}

namespace Consumer
{
    public sealed partial class HeroStats : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
    public sealed class Hero : StatSubject<HeroStats>
    {
        public Hero(params Klrpxy.Gameplay.Tags.Runtime.IGameplayTag[] tags) : base(new HeroStats(), tags) { }
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var ally = new Hero(GameTags.Unit.Ally);
            var neutral = new Hero();
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            group.Add(ally);
            group.Add(neutral);
            source.For(group)
                .WhereTargetMatches(TagQuery.Has(GameTags.Unit.Ally))
                .Modify(HeroStats.HealthKey)
                .Add(25f);
            bool publicQueryMatchesSubjectTags = TagQuery.Has(GameTags.Unit.Ally).Matches(ally.Tags);
            Klrpxy.Gameplay.Tags.Runtime.TagSetChange observedChange = null;
            neutral.Tags.OnChanged += change => observedChange = change;
            bool initiallyFiltered = ally.StatSet.Health.FinalValue == 125f
                && neutral.StatSet.Health.FinalValue == 100f;
            neutral.AddTag(GameTags.Unit.Ally);
            ally.RemoveTag(GameTags.Unit.Ally);
            return publicQueryMatchesSubjectTags
                && observedChange != null
                && object.ReferenceEquals(observedChange.Tag, GameTags.Unit.Ally)
                && observedChange.Kind == Klrpxy.Gameplay.Tags.Runtime.TagSetChangeKind.Added
                && initiallyFiltered
                && ally.StatSet.Health.FinalValue == 100f
                && neutral.StatSet.Health.FinalValue == 125f;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void Roslyn38ConsumerUsesGeneratedKeyWithGroupAndTagFluentRules()
        {
            Compilation compilation = RunStatsAndTagsGenerators(@"
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class GameTags
{
    private const string TagTable = @""Item.Quick
Item.Fire"";
}

namespace Consumer
{
    public sealed partial class ItemStats : StatSet
    {
        public Stat Haste { get; } = new Stat(10f);
    }
    public sealed partial class OtherStats : StatSet
    {
        public Stat Armor { get; } = new Stat(50f);
    }
    public sealed class Item : StatSubject<ItemStats>
    {
        public Item(params Klrpxy.Gameplay.Tags.Runtime.IGameplayTag[] tags) : base(new ItemStats(), tags) { }
    }
    public sealed class Other : StatSubject<OtherStats>
    {
        public Other() : base(new OtherStats()) { }
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var quick = new Item(GameTags.Item.Quick);
            var fire = new Item(GameTags.Item.Quick, GameTags.Item.Fire);
            var other = new Other();
            var group = new StatSubjectGroup().Add(new StatSubject[] { quick, fire, other });
            var source = new ModifierSource();
            source.For(group)
                .WhereTargetHas(GameTags.Item)
                .WhereTargetHas(GameTags.Item.Quick)
                .WhereTargetMatches(TagQuery.None(GameTags.Item.Fire))
                .Modify(ItemStats.HasteKey)
                .Add(5f);
            bool initial = quick.StatSet.Haste.FinalValue == 15f
                && fire.StatSet.Haste.FinalValue == 10f
                && other.StatSet.Armor.FinalValue == 50f;
            quick.AddTag(GameTags.Item.Fire);
            return initial && quick.StatSet.Haste.FinalValue == 10f;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void TagConditionActivationRetainsOriginalModifierOrder()
        {
            // 验证 Tag 条件启停时保留 Modifier 的原始添加顺序。
            Compilation compilation = RunStatsAndTagsGenerators(@"
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class GameTags
{
    private const string TagTable = ""State.Enabled"";
}

namespace Consumer
{
    public sealed partial class HeroStats : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
    public sealed class Hero : StatSubject<HeroStats>
    {
        public Hero() : base(new HeroStats()) { }
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var hero = new Hero();
            var source = new ModifierSource();
            var group = new StatSubjectGroup().Add(hero);
            source.For(group)
                .WhereTargetMatches(TagQuery.Has(GameTags.State.Enabled))
                .Modify(HeroStats.HealthKey)
                .Override(200f);
            ModifierHandle later = source.Modify(hero.StatSet.Health).Override(300f);
            hero.AddTag(GameTags.State.Enabled);
            bool laterStillWins = hero.StatSet.Health.FinalValue == 300f;
            later.Dispose();
            return laterStillWins && hero.StatSet.Health.FinalValue == 200f;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void BazaarBoardAppliesSharedRulesToMatchingHeterogeneousSubjects()
        {
            Compilation compilation = RunStatsAndTagsGenerators(ReadBazaarGameplaySource());

            Assert.True((bool)RunConsumerContract(compilation, "VerifyBoardRules"));
        }

        [Fact]
        public void BazaarEffectsEndThroughHandlesSourcesGroupsAndSubjects()
        {
            Compilation compilation = RunStatsAndTagsGenerators(ReadBazaarGameplaySource());

            Assert.True((bool)RunConsumerContract(compilation, "VerifyEffectLifetimes"));
        }

        [Fact]
        public void BazaarCombatGrowthPublishesStableFinalValuesToUi()
        {
            Compilation compilation = RunStatsAndTagsGenerators(ReadBazaarGameplaySource());

            Assert.True((bool)RunConsumerContract(compilation, "VerifyCombatGrowthAndUi"));
        }

        [Fact]
        public void BazaarInvalidEffectsFailAtomically()
        {
            Compilation compilation = RunStatsAndTagsGenerators(ReadBazaarGameplaySource());

            Assert.True((bool)RunConsumerContract(compilation, "VerifyAtomicFailures"));
        }

        [Fact]
        public void BazaarGameplayUsesOnlyTheIntendedPublicCallSurface()
        {
            SyntaxNode[] roots =
            {
                CSharpSyntaxTree.ParseText(ReadBazaarGameplaySource()).GetRoot(),
                CSharpSyntaxTree.ParseText(ReadBazaarR3GameplaySource()).GetRoot()
            };
            var forbiddenCalls = new HashSet<string>(StringComparer.Ordinal)
            {
                "Register",
                "Refresh",
                "Recalculate",
                "Dispatch",
                "Rebuild"
            };

            string[] usedForbiddenCalls = roots.SelectMany(root => root.DescendantNodes())
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => GetInvokedName(invocation.Expression))
                .Where(name => name != null && forbiddenCalls.Contains(name))
                .ToArray();
            bool restoresBaseValues = roots.SelectMany(root => root.DescendantNodes())
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => assignment.Left is MemberAccessExpressionSyntax member
                    && member.Name.Identifier.ValueText == "BaseValue");

            Assert.Empty(usedForbiddenCalls);
            Assert.Empty(roots.SelectMany(root => root.DescendantNodes()).OfType<ForEachStatementSyntax>());
            Assert.False(restoresBaseValues);
        }

        // 使用给定消费者源码运行 Stats Generator，并返回没有生成错误的输出编译。
        private static Compilation RunGenerator(string source)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp8),
                        "Consumer.cs")
                },
                GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayStatsGenerator() },
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
            Assert.True(
                generatorDiagnostics.Length == 0,
                string.Join(Environment.NewLine, generatorDiagnostics.Select(diagnostic => diagnostic.ToString())));
            return outputCompilation;
        }

        // 同时运行 Stats 与 Tags Generator，验证真实消费者的跨 module 公开行为。
        private static Compilation RunStatsAndTagsGenerators(string source)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp8),
                        "Consumer.cs")
                },
                GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayStatsGenerator(), new GameplayTagsGenerator() },
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> diagnostics);
            Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics));
            return outputCompilation;
        }

        // 使用给定消费者源码运行 Stats Generator，并返回公开的生成诊断结果。
        private static GeneratorDriverRunResult RunGeneratorWithResult(
            string source,
            bool includeTagsRuntime = true,
            MetadataReference tagsRuntimeReference = null,
            bool includeStatsRuntime = true)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp8), "Consumer.cs") },
                GetReferences(includeTagsRuntime, tagsRuntimeReference, includeStatsRuntime),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayStatsGenerator() },
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return driver.GetRunResult();
        }

        // 编译并加载消费者程序集，然后调用 ConsumerContract.Verify 返回运行结果。
        private static object RunConsumerContract(Compilation compilation, string methodName = "Verify")
        {
            AssertCompiles(compilation);

            using (var assemblyStream = new MemoryStream())
            {
                EmitResult result = compilation.Emit(assemblyStream);
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

                assemblyStream.Position = 0;
                Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
                MethodInfo verify = assembly.GetType("Consumer.ConsumerContract").GetMethod(methodName);
                return verify.Invoke(null, null);
            }
        }

        private static string ReadBazaarGameplaySource()
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BazaarGameplay.cs.txt"));
        }

        private static string ReadBazaarR3GameplaySource()
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BazaarGameplay.R3.cs.txt"));
        }

        private static string GetInvokedName(ExpressionSyntax expression)
        {
            if (expression is IdentifierNameSyntax identifier) return identifier.Identifier.ValueText;
            if (expression is MemberAccessExpressionSyntax member) return member.Name.Identifier.ValueText;
            return null;
        }

        // 检查编译结果中不存在错误，并在失败时附带生成源码帮助定位问题。
        private static void AssertCompiles(Compilation compilation)
        {
            Diagnostic[] errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.True(
                errors.Length == 0,
                string.Join(Environment.NewLine, errors.Select(error => error.ToString()))
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        compilation.SyntaxTrees.Skip(1).Select(tree => tree.ToString())));
        }

        // 收集消费者编译所需的 .NET、Stats Runtime 与 Tags Runtime 程序集引用。
        private static IEnumerable<MetadataReference> GetReferences(
            bool includeTagsRuntime = true,
            MetadataReference tagsRuntimeReference = null,
            bool includeStatsRuntime = true)
        {
            var paths = new HashSet<string>(
                ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                    .Split(Path.PathSeparator),
                StringComparer.OrdinalIgnoreCase);

            if (includeStatsRuntime)
            {
                paths.Add(typeof(StatSet).Assembly.Location);
            }
            else
            {
                paths.Remove(typeof(StatSet).Assembly.Location);
            }

            if (includeTagsRuntime)
            {
                paths.Add(typeof(TagSetRuntime<>).Assembly.Location);
            }
            else
            {
                paths.Remove(typeof(TagSetRuntime<>).Assembly.Location);
            }

            var references = new List<MetadataReference>(paths.Select(path => MetadataReference.CreateFromFile(path)));
            if (tagsRuntimeReference != null) references.Add(tagsRuntimeReference);
            return references;
        }

        // 创建仅用于消费者编译的指定版本 Tags Runtime 元数据引用。
        private static MetadataReference CreateTagsRuntimeReference(string version, string source = null)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "KlrpxyGameplayTags.Runtime",
                new[] { CSharpSyntaxTree.ParseText("using System.Reflection; [assembly: AssemblyVersion(\"" + version + "\")]\n" + source, new CSharpParseOptions(LanguageVersion.CSharp8)) },
                GetReferences(includeTagsRuntime: false),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                Assert.True(compilation.Emit(stream).Success);
                return MetadataReference.CreateFromImage(stream.ToArray());
            }
        }
    }
}
