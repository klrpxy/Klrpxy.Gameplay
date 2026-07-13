namespace Klrpxy.Gameplay.Stats
{
    public sealed class RangeStat
    {
        internal StatSet StatSet { get; set; }

        public RangeStat(float minimum, float maximum)
        {
            BaseRange = new FloatRange(minimum, maximum);
        }

        public FloatRange BaseRange { get; }

        public FloatRange FinalRange => BaseRange;
    }
}
