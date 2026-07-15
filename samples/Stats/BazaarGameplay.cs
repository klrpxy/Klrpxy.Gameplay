using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags;
using System;

[GenerateGameplayTags]
public static partial class GameTags
{
    private const string TagTable = @"
Object.Hero
Object.Item
Item.Weapon
Item.Relic
Item.Quick";
}

namespace Consumer
{
    public abstract partial class BoardStats : StatSet
    {
        public Stat Haste { get; } = new Stat(0f);
    }

    public sealed partial class HeroStats : BoardStats
    {
        public Stat Power { get; } = new Stat(10f);

        public Resource Shield { get; } = new Resource(20f).WithMinimum(0f);
    }

    public sealed partial class WeaponStats : BoardStats
    {
        public RangeStat Damage { get; } = new RangeStat(8f, 12f);
    }

    public sealed partial class RelicStats : BoardStats
    {
        public Stat ChargeRate { get; } = new Stat(5f);
    }

    public sealed class Hero : StatSubject<HeroStats>
    {
        public Hero()
            : base(new HeroStats(), GameTags.Object.Hero)
        {
        }
    }

    public sealed class Weapon : StatSubject<WeaponStats>
    {
        public Weapon()
            : base(new WeaponStats(), GameTags.Object.Item, GameTags.Item.Weapon, GameTags.Item.Quick)
        {
        }
    }

    public sealed class Relic : StatSubject<RelicStats>
    {
        public Relic()
            : base(new RelicStats(), GameTags.Object.Item, GameTags.Item.Relic)
        {
        }
    }

    public static class ConsumerContract
    {
        public static bool VerifyAll()
        {
            return VerifyBoardRules()
                && VerifyEffectLifetimes()
                && VerifyCombatGrowthAndUi()
                && VerifyAtomicFailures();
        }

        public static bool VerifyBoardRules()
        {
            var hero = new Hero();
            var weapon = new Weapon();
            var relic = new Relic();
            var board = new StatSubjectGroup();
            var aura = new ModifierSource();
            board.Add(hero);
            board.Add(weapon);
            board.Add(relic);
            board.AddModifier(
                Modifier.Flat(20f, BoardStats.HasteKey)
                    .WhenTargetMatches(TagQuery.Has(GameTags.Item.Quick)),
                aura);

            bool initialTargetsAreCorrect = hero.StatSet.Haste.FinalValue == 0f
                && weapon.StatSet.Haste.FinalValue == 20f
                && relic.StatSet.Haste.FinalValue == 0f;
            relic.AddTag(GameTags.Item.Quick);
            weapon.RemoveTag(GameTags.Item.Quick);
            hero.StatSet.Shield.Decrease(100f);

            RangeStat damage;
            Stat haste;
            return initialTargetsAreCorrect
                && relic.StatSet.Haste.FinalValue == 20f
                && weapon.StatSet.Haste.FinalValue == 0f
                && hero.StatSet.Shield.Value == 0f
                && WeaponStats.DamageKey.TryGet(weapon.StatSet, out damage)
                && object.ReferenceEquals(damage, weapon.StatSet.Damage)
                && BoardStats.HasteKey.TryGet(relic.StatSet, out haste)
                && object.ReferenceEquals(haste, relic.StatSet.Haste);
        }

        public static bool VerifyEffectLifetimes()
        {
            var hero = new Hero();
            var weapon = new Weapon();
            var board = new StatSubjectGroup();
            var boardAura = new ModifierSource();
            board.Add(hero);
            board.Add(weapon);
            board.AddModifier(Modifier.Flat(20f, BoardStats.HasteKey), boardAura);

            var temporaryEffect = new ModifierSource();
            ModifierHandle temporaryPower = hero.AddModifier(
                Modifier.Flat(15f, HeroStats.PowerKey),
                temporaryEffect);
            bool handleApplied = hero.StatSet.Power.FinalValue == 25f;
            temporaryPower.Dispose();
            bool handleEnded = hero.StatSet.Power.FinalValue == 10f;

            var combat = new ModifierSource();
            hero.AddModifier(Modifier.Flat(8f, HeroStats.PowerKey), combat);
            weapon.AddModifier(Modifier.Flat(4f, WeaponStats.DamageKey), combat);
            bool combatApplied = hero.StatSet.Power.FinalValue == 18f
                && weapon.StatSet.Damage.FinalRange.Min == 12f
                && weapon.StatSet.Damage.FinalRange.Max == 16f;
            combat.Dispose();
            bool combatEnded = hero.StatSet.Power.FinalValue == 10f
                && weapon.StatSet.Damage.FinalRange.Min == 8f
                && weapon.StatSet.Damage.FinalRange.Max == 12f;

            board.Remove(weapon);
            bool leavingBoardEndedAura = weapon.StatSet.Haste.FinalValue == 0f;
            board.Add(weapon);
            bool rejoiningBoardRestoredAura = weapon.StatSet.Haste.FinalValue == 20f;
            board.Dispose();
            bool boardEndRemovedAura = hero.StatSet.Haste.FinalValue == 0f
                && weapon.StatSet.Haste.FinalValue == 0f;

            var subjectLifetime = new ModifierSource();
            hero.AddModifier(Modifier.Flat(50f, HeroStats.PowerKey), subjectLifetime);
            hero.Dispose();
            subjectLifetime.Dispose();

            return handleApplied
                && handleEnded
                && combatApplied
                && combatEnded
                && leavingBoardEndedAura
                && rejoiningBoardRestoredAura
                && boardEndRemovedAura;
        }

        public static bool VerifyCombatGrowthAndUi()
        {
            var hero = new Hero();
            var combatLevel = new ObservableValue(1f);
            var combat = new ModifierSource();
            hero.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.External(combatLevel), level => level * 5f),
                    BoardStats.HasteKey),
                combat);
            hero.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(
                        ValueInput.Final(hero.StatSet.Haste),
                        ValueInput.External(combatLevel),
                        (haste, level) => haste * level),
                    HeroStats.PowerKey),
                combat);

            float uiPrevious = -1f;
            float uiCurrent = -1f;
            float powerObservedByUi = -1f;
            int uiEventCount = 0;
            hero.StatSet.Haste.OnFinalValueChanged += (previous, current) =>
            {
                uiPrevious = previous;
                uiCurrent = current;
                powerObservedByUi = hero.StatSet.Power.FinalValue;
                uiEventCount++;
            };

            bool roundStartedAtExpectedValues = hero.StatSet.Haste.FinalValue == 5f
                && hero.StatSet.Power.FinalValue == 15f;
            combatLevel.Value = 3f;

            return roundStartedAtExpectedValues
                && hero.StatSet.Haste.FinalValue == 15f
                && hero.StatSet.Power.FinalValue == 55f
                && uiEventCount == 1
                && uiPrevious == 5f
                && uiCurrent == 15f
                && powerObservedByUi == 55f;
        }

        public static bool VerifyAtomicFailures()
        {
            var hero = new Hero();
            int uiEventCount = 0;
            hero.StatSet.Power.OnFinalValueChanged += (previous, current) => uiEventCount++;
            float valueBeforeFailures = hero.StatSet.Power.FinalValue;

            bool cycleWasRejected = false;
            var cycleSource = new ModifierSource();
            try
            {
                hero.AddModifier(
                    Modifier.Flat(
                        ModifierValue.From(
                            ValueInput.Final(hero.StatSet.Power),
                            value => value + 1f),
                        HeroStats.PowerKey),
                    cycleSource);
            }
            catch (InvalidOperationException)
            {
                cycleWasRejected = true;
            }

            bool disposedSourceWasRejected = false;
            var endedEffect = new ModifierSource();
            endedEffect.Dispose();
            try
            {
                hero.AddModifier(Modifier.Flat(50f, HeroStats.PowerKey), endedEffect);
            }
            catch (ObjectDisposedException)
            {
                disposedSourceWasRejected = true;
            }

            return cycleWasRejected
                && disposedSourceWasRejected
                && hero.StatSet.Power.FinalValue == valueBeforeFailures
                && uiEventCount == 0;
        }
    }
}
