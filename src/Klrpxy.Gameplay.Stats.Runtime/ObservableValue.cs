using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class ObservableValue
    {
        private float value;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();

        public ObservableValue(float value)
        {
            Modifier.ValidateFinite(value, nameof(value));
            this.value = value;
        }

        public float Value
        {
            get => value;
            set
            {
                threadGuard.Verify();
                Modifier.ValidateFinite(value, nameof(value));
                if (this.value == value) return;
                StatsPropagationCoordinator.Execute(() =>
                {
                    this.value = value;
                    Changed?.Invoke();
                });
            }
        }

        internal event Action Changed;

        internal void VerifyThread() => threadGuard.Verify();
    }
}
