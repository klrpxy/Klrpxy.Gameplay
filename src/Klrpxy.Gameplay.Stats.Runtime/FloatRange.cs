namespace Klrpxy.Gameplay.Stats
{
    public readonly struct FloatRange
    {
        public FloatRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }

        public float Max { get; }
    }
}
