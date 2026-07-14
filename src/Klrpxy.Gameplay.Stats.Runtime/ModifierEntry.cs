namespace Klrpxy.Gameplay.Stats
{
    internal interface IModifierEntry
    {
        Modifier Modifier { get; }

        long Order { get; }
    }
}
