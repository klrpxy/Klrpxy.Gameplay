using System;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatsOwnerTests
    {
        [Fact]
        public void OwnerBindsProvidedStatSet()
        {
            // 验证创建 StatsOwner 时会把传入的 StatSet 绑定到该 Owner。
            var statSet = new TestStatSet();

            var owner = new TestOwner(statSet);

            Assert.Same(owner, statSet.Owner);
        }

        [Fact]
        public void StatSetCannotBeBoundToSecondOwner()
        {
            // 验证已绑定的 StatSet 不能再次交给另一个 StatsOwner。
            var statSet = new TestStatSet();
            _ = new TestOwner(statSet);

            Assert.Throws<InvalidOperationException>(() => new TestOwner(statSet));
        }

        [Fact]
        public void OwnerRejectsMissingStatSet()
        {
            // 验证创建 StatsOwner 时必须提供非空 StatSet。
            Assert.Throws<ArgumentNullException>(() => new TestOwner(null));
        }

        [Fact]
        public void OwnerRejectsNullGeneratedMemberWithItsPath()
        {
            // 验证绑定 StatSet 时会拒绝空成员，并在错误中指出声明路径。
            var statSet = new NullMemberStatSet();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new NullMemberOwner(statSet));

            Assert.Contains("Game::Combat.NullMemberStatSet.Health", exception.Message);
            Assert.Null(statSet.Owner);
        }

        [Fact]
        public void OwnerRejectsMemberAlreadyBoundToAnotherStatSet()
        {
            // 验证一个成员实例不能被两个不同的 StatSet 绑定。
            var health = new Stat(100f);
            _ = new SharedMemberOwner(new SharedMemberStatSet(health));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new SharedMemberOwner(new SharedMemberStatSet(health)));

            Assert.Contains("Game::Combat.SharedMemberStatSet.Health", exception.Message);
        }

        [Fact]
        public void OwnerRejectsDuplicateGeneratedMemberWithoutBindingStatSet()
        {
            // 验证同一成员被两个属性引用时，绑定失败且 StatSet 保持未归属。
            var statSet = new DuplicateMemberStatSet(new Stat(100f));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new DuplicateMemberOwner(statSet));

            Assert.Contains("Game::Combat.DuplicateMemberStatSet.Attack", exception.Message);
            Assert.Null(statSet.Owner);
        }

        private sealed class TestStatSet : StatSet
        {
        }

        private sealed class TestOwner : StatsOwner<TestStatSet>
        {
            public TestOwner(TestStatSet statSet)
                : base(statSet)
            {
            }
        }

        private sealed class NullMemberStatSet : StatSet
        {
            public Stat Health { get; }

            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>("Game::Combat.NullMemberStatSet.Health", StatMemberKind.Stat, set => ((NullMemberStatSet)set).Health));
            }
        }

        private sealed class NullMemberOwner : StatsOwner<NullMemberStatSet>
        {
            public NullMemberOwner(NullMemberStatSet statSet)
                : base(statSet)
            {
            }
        }

        private sealed class SharedMemberStatSet : StatSet
        {
            public SharedMemberStatSet(Stat health) => Health = health;

            public Stat Health { get; }

            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>("Game::Combat.SharedMemberStatSet.Health", StatMemberKind.Stat, set => ((SharedMemberStatSet)set).Health));
            }
        }

        private sealed class SharedMemberOwner : StatsOwner<SharedMemberStatSet>
        {
            public SharedMemberOwner(SharedMemberStatSet statSet)
                : base(statSet)
            {
            }
        }

        private sealed class DuplicateMemberStatSet : StatSet
        {
            public DuplicateMemberStatSet(Stat value) { Health = value; Attack = value; }
            public Stat Health { get; }
            public Stat Attack { get; }
            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>("Game::Combat.DuplicateMemberStatSet.Health", StatMemberKind.Stat, set => ((DuplicateMemberStatSet)set).Health));
                members.Add(CreateMember<Stat>("Game::Combat.DuplicateMemberStatSet.Attack", StatMemberKind.Stat, set => ((DuplicateMemberStatSet)set).Attack));
            }
        }

        private sealed class DuplicateMemberOwner : StatsOwner<DuplicateMemberStatSet>
        {
            public DuplicateMemberOwner(DuplicateMemberStatSet statSet) : base(statSet) { }
        }
    }
}
