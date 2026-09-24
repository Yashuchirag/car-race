using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// A map of the whole circuit in the bottom left corner, north up, with a dot for every
    /// car: the player larger and white, the others in their body colour. The outline is drawn
    /// once into a texture from TrackPath's centreline, so a frame costs one texture and a dot
    /// per car. Viewed from above with +z up the screen, +x is to the right, so the map is not
    /// mirrored.
    /// </summary>
    public sealed class MiniMap : MonoBehaviour
    {
        [SerializeField] TrackPath track;
        [Tooltip("The first car is the player.")]
        [SerializeField] Transform[] cars = new Transform[0];
        [SerializeField] int sizePixels = 240;

        const int Padding = 10;
        const float RoadPixels = 2.5f;

        Texture2D _map;
        Color[] _colours;
        Vector2 _min;
        float _scale;

        void Start()
        {
            if (track == null || track.centre.Length < 3) { enabled = false; return; }

            _min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 p in track.centre)
            {
                _min = Vector2.Min(_min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            Vector2 span = max - _min;
            _scale = (sizePixels - 2 * Padding) / Mathf.Max(span.x, span.y);
            // Centre the circuit in the square along its shorter side.
            _min -= (new Vector2(Mathf.Max(span.x, span.y), Mathf.Max(span.x, span.y)) - span) * 0.5f;

            _map = new Texture2D(sizePixels, sizePixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[sizePixels * sizePixels];
            var backdrop = new Color32(0, 0, 0, 110);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = backdrop;

            int n = track.centre.Length;
            var road = new Color32(235, 235, 235, 255);
            for (int i = 0; i < n; i++)
                Line(pixels, ToMap(track.centre[i]), ToMap(track.centre[(i + 1) % n]), RoadPixels, road);

            // The start line, across the road at sample 0.
            Vector3 along = (track.centre[1] - track.centre[n - 1]).normalized;
            Vector3 across = new Vector3(along.z, 0f, -along.x) * (7f / _scale);
            Line(pixels, ToMap(track.centre[0] - across), ToMap(track.centre[0] + across), 1.5f, new Color32(220, 40, 30, 255));

            _map.SetPixels32(pixels);
            _map.Apply();

            _colours = new Color[cars.Length];
            for (int i = 0; i < cars.Length; i++)
            {
                Transform body = cars[i] != null ? cars[i].Find("Body") : null;
                var renderer = body != null ? body.GetComponent<Renderer>() : null;
                _colours[i] = i == 0 || renderer == null ? Color.white : renderer.sharedMaterial.GetColor("_BaseColor");
            }
        }

        Vector2 ToMap(Vector3 world) => (new Vector2(world.x, world.z) - _min) * _scale + new Vector2(Padding, Padding);

        /// <summary>A thick line into the pixel array, as discs stepped along it.</summary>
        void Line(Color32[] pixels, Vector2 a, Vector2 b, float radius, Color32 colour)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
            int r = Mathf.CeilToInt(radius);
            for (int s = 0; s <= steps; s++)
            {
                Vector2 c = Vector2.Lerp(a, b, s / (float)steps);
                for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int px = Mathf.RoundToInt(c.x) + x, py = Mathf.RoundToInt(c.y) + y;
                    if (px < 0 || py < 0 || px >= sizePixels || py >= sizePixels) continue;
                    pixels[py * sizePixels + px] = colour;
                }
            }
        }

        void OnGUI()
        {
            if (!enabled || _map == null) return;
            var rect = new Rect(10f, Screen.height - sizePixels - 10f, sizePixels, sizePixels);
            GUI.DrawTexture(rect, _map);

            // The player last, so it is drawn on top of anyone alongside.
            for (int i = cars.Length - 1; i >= 0; i--)
            {
                if (cars[i] == null) continue;
                Vector2 m = ToMap(cars[i].position);
                float size = i == 0 ? 11f : 8f;
                // Texture rows count up from the bottom, the screen counts down from the top.
                var dot = new Rect(rect.x + m.x - size * 0.5f, rect.yMax - m.y - size * 0.5f, size, size);
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(dot.x - 1.5f, dot.y - 1.5f, size + 3f, size + 3f), Texture2D.whiteTexture);
                GUI.color = _colours[i];
                GUI.DrawTexture(dot, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
