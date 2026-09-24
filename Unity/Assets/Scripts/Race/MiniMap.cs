using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The road around the player, zoomed in, in the bottom left corner: AheadMetres ahead and
    /// BehindMetres behind, turned so that the road ahead points up. It turns with the track's
    /// direction where the player is, not with the car, so a spin does not spin the map.
    /// Corners are coloured by how tight they are, and the other cars show as dots in their
    /// body colour when they are in view.
    ///
    /// A map of the whole circuit came first and drew a Monza chicane a few pixels across,
    /// which is no use for seeing what comes next. The view is drawn into a small texture
    /// every frame, a clear and a disc per road sample, which is cheap and needs no camera.
    /// </summary>
    public sealed class MiniMap : MonoBehaviour
    {
        [SerializeField] TrackPath track;
        [Tooltip("The first car is the player.")]
        [SerializeField] Transform[] cars = new Transform[0];
        [SerializeField] int sizePixels = 220;
        [SerializeField] float aheadMetres = 220f;
        [SerializeField] float behindMetres = 30f;

        [Tooltip("Corners tighter than these radii, in metres, are drawn orange and red.")]
        [SerializeField] float mediumRadius = 150f;
        [SerializeField] float tightRadius = 60f;

        static readonly Color32 Backdrop = new Color32(0, 0, 0, 120);
        static readonly Color32 Straight = new Color32(225, 225, 225, 255);
        static readonly Color32 Medium = new Color32(255, 165, 40, 255);
        static readonly Color32 Tight = new Color32(235, 45, 35, 255);
        static readonly Color32 StartLine = new Color32(40, 120, 255, 255);

        Texture2D _map;
        Color32[] _pixels;
        Color32[] _roadColour;
        Color[] _dotColours;
        int _index;
        Vector3 _last;
        Vector3 _centre, _forward, _right;
        float _scale;

        void Start()
        {
            if (track == null || track.centre.Length < 3 || cars.Length == 0 || cars[0] == null) { enabled = false; return; }

            _scale = sizePixels / (aheadMetres + behindMetres);
            _map = new Texture2D(sizePixels, sizePixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _pixels = new Color32[sizePixels * sizePixels];

            // Each sample's colour from the radius of the circle through its neighbours 6 m
            // either side, the stride TrackData uses so the sampling wiggle does not read as a bend.
            int n = track.centre.Length;
            int stride = Mathf.Max(1, Mathf.RoundToInt(6f / Mathf.Max(track.sampleSpacing, 0.1f)));
            _roadColour = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                float radius = Radius(track.centre[(i - stride + n) % n], track.centre[i], track.centre[(i + stride) % n]);
                _roadColour[i] = radius < tightRadius ? Tight : radius < mediumRadius ? Medium : Straight;
            }

            _dotColours = new Color[cars.Length];
            for (int i = 0; i < cars.Length; i++)
            {
                Transform body = cars[i] != null ? cars[i].Find("Body") : null;
                var renderer = body != null ? body.GetComponent<Renderer>() : null;
                _dotColours[i] = i == 0 || renderer == null ? Color.white : renderer.sharedMaterial.GetColor("_BaseColor");
            }

            _last = cars[0].position;
            _index = track.Nearest(_last, 0, back: 0, ahead: n - 1);
        }

        static float Radius(Vector3 a, Vector3 b, Vector3 c)
        {
            a.y = b.y = c.y = 0f;
            float cross = Mathf.Abs((b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x));
            if (cross < 1e-6f) return float.MaxValue;
            return (a - b).magnitude * (b - c).magnitude * (c - a).magnitude / (2f * cross);
        }

        void Update()
        {
            if (_map == null) return;
            int n = track.centre.Length;
            Vector3 position = cars[0].position;
            _index = (position - _last).sqrMagnitude > 25f * 25f
                ? track.Nearest(position, 0, back: 0, ahead: n - 1)
                : track.Nearest(position, _index);
            _last = position;

            // The track's direction here, over 10 samples either side so it turns smoothly.
            Vector3 along = track.centre[(_index + 10) % n] - track.centre[(_index - 10 + n) % n];
            along.y = 0f;
            _forward = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
            _right = new Vector3(_forward.z, 0f, -_forward.x);
            _centre = position;

            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Backdrop;

            int back = Mathf.CeilToInt((behindMetres + 20f) / track.sampleSpacing);
            int ahead = Mathf.CeilToInt((aheadMetres + 20f) / track.sampleSpacing);
            for (int k = -back; k <= ahead; k++)
            {
                int i = ((_index + k) % n + n) % n;
                float half = 0.5f * (track.widthLeft.Length == n ? track.widthLeft[i] + track.widthRight[i] : 12f);
                Disc(ToMap(track.centre[i]), Mathf.Max(half * _scale, 1.5f), _roadColour[i]);
            }

            // The start line across the road at sample 0, if it is in view.
            int toStart = ((0 - _index) % n + n) % n;
            if (toStart <= ahead || toStart >= n - back)
            {
                Vector3 start = track.centre[0];
                Vector3 dir = (track.centre[1] - track.centre[n - 1]).normalized;
                Vector3 across = new Vector3(dir.z, 0f, -dir.x);
                float half = track.widthLeft.Length == n ? Mathf.Max(track.widthLeft[0], track.widthRight[0]) : 6f;
                for (float s = -half; s <= half; s += 0.5f) Disc(ToMap(start + across * s), 1.2f, StartLine);
            }

            _map.SetPixels32(_pixels);
            _map.Apply();
        }

        /// <summary>Pixel coordinates in the map, x to the right, y up, the player at the
        /// middle of the bottom edge plus behindMetres.</summary>
        Vector2 ToMap(Vector3 world)
        {
            Vector3 d = world - _centre;
            return new Vector2(sizePixels * 0.5f + Vector3.Dot(d, _right) * _scale,
                               behindMetres * _scale + Vector3.Dot(d, _forward) * _scale);
        }

        void Disc(Vector2 c, float radius, Color32 colour)
        {
            int r = Mathf.CeilToInt(radius);
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y);
            if (cx < -r || cy < -r || cx >= sizePixels + r || cy >= sizePixels + r) return;
            float r2 = radius * radius;
            for (int y = -r; y <= r; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= sizePixels) continue;
                for (int x = -r; x <= r; x++)
                {
                    int px = cx + x;
                    if (px < 0 || px >= sizePixels || x * x + y * y > r2) continue;
                    _pixels[py * sizePixels + px] = colour;
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
                if (m.x < 0f || m.y < 0f || m.x > sizePixels || m.y > sizePixels) continue;
                float size = i == 0 ? 11f : 9f;
                // Texture rows count up from the bottom, the screen counts down from the top.
                var dot = new Rect(rect.x + m.x - size * 0.5f, rect.yMax - m.y - size * 0.5f, size, size);
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(dot.x - 1.5f, dot.y - 1.5f, size + 3f, size + 3f), Texture2D.whiteTexture);
                GUI.color = _dotColours[i];
                GUI.DrawTexture(dot, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
