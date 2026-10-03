using System;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// How a solo race is run, set in the lobby's PLAYERS panel and kept between sessions: the
    /// laps, and how fast the tyres wear. A LAN race takes its laps from the host's lobby instead.
    /// -laps N and -tyreWear N on the command line override the saved choice for one session, for
    /// testing.
    /// </summary>
    public static class RaceSettings
    {
        const string LapsKey = "CarRace.Race.Laps";
        const string WearKey = "CarRace.Race.TyreWear";
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
