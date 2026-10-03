using System;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// How a solo race is run, set in the lobby's PLAYERS panel and kept between sessions: the
    /// laps, how fast the tyres wear, and whether the safety car comes out. A LAN race takes its
    /// laps from the host's lobby instead. -laps N and -tyreWear N on the command line override
    /// the saved choice for one session, for testing, and -safetyCarAt S brings the safety car
    /// out S seconds after GO. Under -benchmark it only comes out when forced, so benchmarks
    /// stay comparable.
    /// </summary>
    public static class RaceSettings
    {
        const string LapsKey = "CarRace.Race.Laps";
        const string WearKey = "CarRace.Race.TyreWear";
        const string SafetyCarKey = "CarRace.Race.SafetyCar";
        public const int MaxLaps = 20;

        /// <summary>The wear rates on offer, multiples of real wear; 0 is off. At x1 the rear tyres
        /// last about 30 laps of Royal Park, at x5 about 6.</summary>
        public static readonly float[] WearRates = { 0f, 1f, 2f, 5f, 10f };

        public static int Laps
        {
            get => Override("-laps", out float laps) ? Mathf.Clamp((int)laps, 1, MaxLaps)
                 : Mathf.Clamp(PlayerPrefs.GetInt(LapsKey, 3), 1, MaxLaps);
            set { PlayerPrefs.SetInt(LapsKey, Mathf.Clamp(value, 1, MaxLaps)); PlayerPrefs.Save(); }
        }

        /// <summary>The chosen place in WearRates.</summary>
        public static int WearChoice
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(WearKey, 0), 0, WearRates.Length - 1);
            set { PlayerPrefs.SetInt(WearKey, Mathf.Clamp(value, 0, WearRates.Length - 1)); PlayerPrefs.Save(); }
        }

        public static float TyreWearRate => Override("-tyreWear", out float rate) ? rate : WearRates[WearChoice];

        public static bool SafetyCarChoice
        {
            get => PlayerPrefs.GetInt(SafetyCarKey, 1) != 0;
            set { PlayerPrefs.SetInt(SafetyCarKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>When -safetyCarAt forces it out, seconds after GO, or -1.</summary>
        public static float SafetyCarAt => Override("-safetyCarAt", out float at) ? at : -1f;

        public static bool SafetyCar => SafetyCarAt >= 0f
            || SafetyCarChoice && Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmark") < 0;

        public static string WearLabel(int choice) => WearRates[choice] <= 0f ? "OFF" : $"x{WearRates[choice]:0}";

        static bool Override(string flag, out float value)
        {
            value = 0f;
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length
                && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
