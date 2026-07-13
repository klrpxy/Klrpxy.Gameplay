namespace Klrpxy.Gameplay.Stats
{
    public sealed class Resource
    {
        internal StatSet StatSet { get; set; }

        public Resource(float value)
        {
            Value = value;
        }

        public float Value { get; }
    }
}
