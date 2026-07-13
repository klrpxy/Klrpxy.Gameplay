namespace Klrpxy.Gameplay.Stats
{
    public sealed class Stat
    {
        public Stat(float baseValue)
        {
            BaseValue = baseValue;
        }

        public float BaseValue { get; }

        public float FinalValue => BaseValue;
    }
}
