using System;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The frame rate the player chose, kept between sessions and applied before the first
    /// scene loads: VSync, a fixed cap, or unlimited. VSync is the default, since it suits
    /// whatever monitor the game meets and never tears; on a 60 Hz screen it is 60 fps.
    ///
    /// `-frameRate 60` (or VSync, Unlimited) on the command line overrides the saved choice
    /// for that session without changing it.
    ///
    /// Also the graphics quality, Low, Medium or High: Unity quality levels by those names,
    /// each with its own URP asset (GraphicsSetup builds them). The first time the game runs
    /// it picks from the graphics card's memory; after that the player's choice is kept.
    /// `-quality Low` overrides it for a session. A quality level carries a vSync setting of
    /// its own, so the frame rate is put back after every change of quality.
    ///
    /// Also caps how much physics a frame may catch up on. Physics runs at a fixed 200 Hz
    /// whatever the frame rate, because the vehicle model needs it. Left at Unity's default a
    /// hitch of a third of a second was followed by 66 catch-up steps in one frame, which
    /// turned a stall into a freeze; at 0.1 s a slow machine slows the game down briefly
    /// instead.
    /// </summary>
    public static class DisplaySettings
    {
        public static readonly string[] Names = { "VSync", "30 fps", "60 fps", "120 fps", "144 fps", "Unlimited" };
        static readonly int[] Caps = { 0, 30, 60, 120, 144, -1 };   // 0 means VSync

        const string Key = "CarRace.FrameRate";
        const string QualityKey = "CarRace.Quality";

        public static readonly string[] QualityNames = { "Low", "Medium", "High" };

        /// <summary>Index into QualityNames of the quality in force.</summary>
        public static int Quality { get; private set; } = 2;
        const float MaximumCatchUpSeconds = 0.1f;

        /// <summary>Index into Names of the choice in force.</summary>
        public static int Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyAtStartup()
        {
            Time.maximumDeltaTime = MaximumCatchUpSeconds;
            int quality = QualityFromCommandLine();
            ApplyQuality(quality >= 0 ? quality : PlayerPrefs.GetInt(QualityKey, DefaultQuality()), save: false);
            int choice = PlayerPrefs.GetInt(Key, 0);
            int fromCommandLine = CommandLineChoice();
            Apply(fromCommandLine >= 0 ? fromCommandLine : choice, save: false);
        }

        /// <summary>Puts a choice into force, and remembers it for next time if save.</summary>
        public static void Apply(int choice, bool save)
        {
            Current = Mathf.Clamp(choice, 0, Names.Length - 1);
            int cap = Caps[Current];
            QualitySettings.vSyncCount = cap == 0 ? 1 : 0;
            Application.targetFrameRate = cap == 0 ? -1 : cap;
            if (!save) return;
            PlayerPrefs.SetInt(Key, Current);
            PlayerPrefs.Save();
        }

        /// <summary>Puts a quality level into force, and remembers it for next time if save. A
        /// scene built before the levels existed has only the one; then nothing changes.</summary>
        public static void ApplyQuality(int quality, bool save)
        {
            Quality = Mathf.Clamp(quality, 0, QualityNames.Length - 1);
            int level = Array.IndexOf(QualitySettings.names, QualityNames[Quality]);
            if (level >= 0 && level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);
            Apply(Current, save: false);   // the level brought its own vSync with it
            if (!save) return;
            PlayerPrefs.SetInt(QualityKey, Quality);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// A first guess from the graphics card's memory, the one number that tells an old or
        /// integrated card from a gaming one: under 1.5 GB Low, under 3 GB Medium, else High.
        /// </summary>
        static int DefaultQuality()
        {
            int megabytes = SystemInfo.graphicsMemorySize;
            return megabytes < 1536 ? 0 : megabytes < 3072 ? 1 : 2;
        }

        static int QualityFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-quality");
            if (i < 0 || i + 1 >= args.Length) return -1;
            for (int q = 0; q < QualityNames.Length; q++)
                if (string.Equals(args[i + 1], QualityNames[q], StringComparison.OrdinalIgnoreCase)) return q;
            return -1;
        }

        /// <summary>True when -frameRate was given, which the benchmark leaves in force.</summary>
        public static bool SetOnCommandLine => CommandLineChoice() >= 0;

        static int CommandLineChoice()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-frameRate");
            if (i < 0 || i + 1 >= args.Length) return -1;
            string value = args[i + 1].Trim().ToLowerInvariant();
            if (value == "vsync") return 0;
            if (value == "unlimited") return Names.Length - 1;
            int k = Array.IndexOf(Caps, int.TryParse(value, out int fps) ? fps : int.MinValue);
            return k > 0 ? k : -1;
        }
    }
}
