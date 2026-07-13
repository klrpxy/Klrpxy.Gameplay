using System;
using System.Diagnostics;

namespace Klrpxy.Gameplay.Stats
{
    public static class StatsDiagnostics
    {
        public static Action<Exception> EventExceptionHandler { get; set; } = exception => Trace.TraceError(exception.ToString());

        internal static void Report(Exception exception)
        {
            try
            {
                EventExceptionHandler?.Invoke(exception);
            }
            catch
            {
            }
        }
    }
}
