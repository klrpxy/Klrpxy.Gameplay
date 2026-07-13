namespace Klrpxy.Gameplay.Stats
{
    public sealed class Stat
    {
        internal StatSet StatSet { get; set; }

        public Stat(float baseValue)
        {
            BaseValue = baseValue;
        }

        public float BaseValue { get; }

        public float FinalValue => BaseValue;
    }
}
