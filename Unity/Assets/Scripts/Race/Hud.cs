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

        /// <summary>Hides every readout, for measuring what drawing them costs (-noHud).</summary>
        public static bool Hidden;

        const float CornerPx = 9f;
        static GUIStyle _rounded;
        static Texture2D _roundedTexture;
        static int _roundedRadius;

        /// <summary>A rounded rectangle in a colour, drawn from one small white texture sliced
        /// nine ways so its corners keep their radius at any size; the radius scales with the
        /// screen like everything else. Draws only on the repaint pass.</summary>
        public static void Rounded(Rect rect, Color colour)
        {
            if (Event.current.type != EventType.Repaint) return;
            int radius = Mathf.Max(2, Mathf.RoundToInt(Px(CornerPx)));
            if (_rounded == null || _roundedTexture == null || radius != _roundedRadius)
            {
                if (_roundedTexture != null) Object.Destroy(_roundedTexture);
                _roundedTexture = RoundedTexture(radius);
                _roundedRadius = radius;
                _rounded = new GUIStyle { normal = { background = _roundedTexture }, border = new RectOffset(radius, radius, radius, radius) };
            }
            Color before = GUI.color;
            GUI.color = colour;
            _rounded.Draw(rect, false, false, false, false);
            GUI.color = before;
        }

        /// <summary>A plain rectangle in a colour.</summary>
        public static void Fill(Rect rect, Color colour)
        {
            Color before = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }

        /// <summary>A white square with rounded corners, the edge faded over a pixel.</summary>
        static Texture2D RoundedTexture(int radius)
        {
            int size = radius * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float distance = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(radius - distance + 0.5f)));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
