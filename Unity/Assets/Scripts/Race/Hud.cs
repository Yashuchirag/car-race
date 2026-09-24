using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Sizes for the on-screen readouts, which are laid out in pixels for a 1080 line screen.
    /// Everything is scaled by the screen's height against that, so the HUD takes the same
    /// share of a QHD or UHD screen as of a 1080p one. Font sizes and rectangles are scaled
    /// rather than the whole GUI stretched with GUI.matrix, which draws text at its 1080p size
    /// and blows it up, blurred, at 4K.
    /// </summary>
    public static class Hud
    {
        const float ReferenceHeight = 1080f;

        /// <summary>1 at 1080p, 1.33 at 1440p, 2 at 2160p. Never below a half, so a small
        /// editor Game view keeps its text readable.</summary>
        public static float Scale => Mathf.Max(Screen.height / ReferenceHeight, 0.5f);

        /// <summary>A length given in 1080p pixels, in this screen's pixels.</summary>
        public static float Px(float pixels) => pixels * Scale;

        public static int Font(int size) => Mathf.Max(1, Mathf.RoundToInt(size * Scale));
    }
}
