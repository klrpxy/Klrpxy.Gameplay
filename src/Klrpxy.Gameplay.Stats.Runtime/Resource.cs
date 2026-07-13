using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class Resource
    {
        private readonly RoundingMode rounding;
        private bool hasBounds;
        private bool hasMaximum;
        private float maximum;
        private float minimum;
        private float value;

        internal StatSet StatSet { get; set; }

        public Resource(float value, RoundingMode rounding = RoundingMode.None)
        {
            EnsureFinite(value);
            this.rounding = rounding;
            this.value = value;
        }

        public float Value => value;

        public void Set(float value)
        {
            EnsureFinite(value);
            value = ApplyRounding(value);
            value = ApplyBounds(value);

            if (this.value == value)
            {
                return;
            }

            this.value = value;
        }

        public void Increase(float amount)
        {
            Set(value + amount);
        }

        public void Decrease(float amount)
        {
            Set(value - amount);
        }

        public Resource WithBounds(float minimum, float maximum)
        {
            EnsureBoundsAreNotDeclared();
            EnsureFinite(minimum);
            EnsureFinite(maximum);

            if (minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum), "The minimum cannot exceed the maximum.");
            }

            this.minimum = minimum;
            this.maximum = maximum;
            hasBounds = true;
            hasMaximum = true;
            value = ApplyBounds(value);
            return this;
        }

        public Resource WithMinimum(float minimum)
        {
            EnsureBoundsAreNotDeclared();
            EnsureFinite(minimum);
            this.minimum = minimum;
            hasBounds = true;
            hasMaximum = false;
            value = ApplyBounds(value);
            return this;
        }

        private static void EnsureFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The value must be finite.");
            }
        }

        private float ApplyRounding(float value)
        {
            switch (rounding)
            {
                case RoundingMode.Floor:
                    return (float)Math.Floor(value);
                case RoundingMode.Ceiling:
                    return (float)Math.Ceiling(value);
                case RoundingMode.Nearest:
                    return (float)Math.Round(value, MidpointRounding.AwayFromZero);
                default:
                    return value;
            }
        }

        private float ApplyBounds(float value)
        {
            if (!hasBounds)
            {
                return value;
            }

            value = Math.Max(value, minimum);
            return hasMaximum ? Math.Min(value, maximum) : value;
        }

        private void EnsureBoundsAreNotDeclared()
        {
            if (hasBounds)
            {
                throw new InvalidOperationException("The Resource bounds have already been declared.");
            }
        }
    }
}
