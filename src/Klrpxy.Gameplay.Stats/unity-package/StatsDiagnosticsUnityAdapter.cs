using Klrpxy.Gameplay.Stats;
using UnityEngine;

namespace Klrpxy.Gameplay.Stats.Unity
{
    public static class StatsDiagnosticsUnityAdapter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            StatsDiagnostics.EventExceptionHandler = Debug.LogException;
        }
    }
}
