using System;

namespace Klrpxy.Gameplay.Tags
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GenerateGameplayTagsAttribute : Attribute
    {
    }
}
