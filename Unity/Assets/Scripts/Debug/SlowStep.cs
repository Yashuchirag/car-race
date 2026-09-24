using System.Diagnostics;

namespace CarRace.UnityGame
{
    /// <summary>
    /// DIAGNOSTIC, temporary: finds which part of a physics step takes too long. A stall of
    /// 90 to 400 ms recurs about 30 s into a benchmark race, inside the scripts' FixedUpdate
    /// (PROGRESS.md, section 6). Each timed section logs to the player log when it passes
    /// ThresholdMs. Remove once the stall is found.
    /// </summary>
    public static class SlowStep
    {
        const double ThresholdMs = 10.0;
        static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        public static long Now => Stopwatch.GetTimestamp();

        /// <summary>True, with the milliseconds, when the section since start was slow.</summary>
        public static bool Slow(long start, out double ms)
        {
            ms = (Stopwatch.GetTimestamp() - start) * MsPerTick;
            return ms > ThresholdMs;
        }

        public static void Log(string message) =>
            UnityEngine.Debug.LogWarning($"SLOW STEP t={UnityEngine.Time.time:0.000} frame={UnityEngine.Time.frameCount} {message}");
    }
}
