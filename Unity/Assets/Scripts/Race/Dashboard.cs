using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// A rev counter and a speedometer in the bottom right corner, the way a car's instrument
    /// cluster shows them: dials with needles, the gear in the middle of the rev counter, which
    /// turns red near the limit as a shift light, and the speed in figures in the middle of the
    /// speedometer. The faces are drawn once into textures at the screen's resolution, so only
    /// the needles and figures are drawn each frame.
    ///
    /// Attached at every scene load to the player's car, the CarController with a DriverInput,
    /// so no scene has to be rebuilt for it.
    /// </summary>
    public sealed class Dashboard : MonoBehaviour
    {
        const float DiameterPx = 210f;          // at 1080p, see Hud
        const float MarginPx = 12f;
        const float Sweep = 270f;               // degrees of dial, from 135 left of straight up
        const float MaxRpm = 8000f;
        const float RedlineRpm = 7000f;
        const float MaxKph = 320f;
        const float ShiftLightShare = 0.95f;    // of the rev limit

        CarController _car;
        Texture2D _tachometer, _speedometer;
        int _facePixels;
        GUIStyle _big, _small, _tick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialise()
        {
            SceneManager.sceneLoaded += (_, __) => Attach();
            Attach();
        }

        static void Attach()
        {
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (car.GetComponent<DriverInput>() == null || car.GetComponent<Dashboard>() != null) continue;
                car.gameObject.AddComponent<Dashboard>()._car = car;
            }
        }

        void OnDestroy()
        {
            if (_tachometer != null) Destroy(_tachometer);
            if (_speedometer != null) Destroy(_speedometer);
        }

        void OnGUI()
        {
            if (Hud.Hidden || _car == null || _car.Sim == null) return;
            int size = Mathf.RoundToInt(Hud.Px(DiameterPx));
            if (size != _facePixels) BuildFaces(size);

            _big ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            _tick ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _big.fontSize = Hud.Font(40);
            _small.fontSize = Hud.Font(13);
            _tick.fontSize = Hud.Font(14);

            var drivetrain = _car.Sim.Drivetrain;
            float limit = _car.Sim.Config.RevLimitRpm;
            float margin = Hud.Px(MarginPx);
            var speedo = new Rect(Screen.width - margin - size, Screen.height - margin - size, size, size);
            var tacho = new Rect(speedo.x - margin - size, speedo.y, size, size);

            // Needles first, figures over them: drawn the other way round the needle and its
            // hub hid the gear.
            // Rev counter: gear below the centre, red near the limit.
            GUI.DrawTexture(tacho, _tachometer);
            Figures(tacho, MaxRpm, 1000f, v => (v / 1000f).ToString("0"));
            Needle(tacho, drivetrain.EngineRpm / MaxRpm);
            string gear = drivetrain.Gear switch { -1 => "R", 0 => "N", _ => drivetrain.Gear.ToString() };
            _big.normal.textColor = drivetrain.EngineRpm >= limit * ShiftLightShare ? new Color(1f, 0.25f, 0.2f) : Color.white;
            GUI.Label(Centred(tacho, Hud.Px(26f), Hud.Px(48f)), gear, _big);
            GUI.Label(Centred(tacho, Hud.Px(56f), Hud.Px(18f)), "x1000 rpm", _small);

            // Speedometer: the speed in figures below the centre.
            GUI.DrawTexture(speedo, _speedometer);
            Figures(speedo, MaxKph, 40f, v => v.ToString("0"));
            Needle(speedo, _car.SpeedKph / MaxKph);
            _big.normal.textColor = Color.white;
            GUI.Label(Centred(speedo, Hud.Px(26f), Hud.Px(48f)), _car.SpeedKph.ToString("0"), _big);
            GUI.Label(Centred(speedo, Hud.Px(56f), Hud.Px(18f)), "km/h", _small);
        }

        static Rect Centred(Rect dial, float below, float height)
            => new Rect(dial.x, dial.center.y + below - height * 0.5f, dial.width, height);

        /// <summary>Degrees clockwise from straight up for a share of full scale.</summary>
        static float Angle(float share) => -Sweep * 0.5f + Sweep * Mathf.Clamp01(share);

        /// <summary>The major tick values in figures, just inside the ring.</summary>
        void Figures(Rect dial, float max, float step, System.Func<float, string> text)
        {
            float radius = dial.width * 0.34f, box = Hud.Px(30f);
            for (float v = 0f; v <= max + 0.01f; v += step)
            {
                float a = Angle(v / max) * Mathf.Deg2Rad;
                var at = new Vector2(dial.center.x + Mathf.Sin(a) * radius, dial.center.y - Mathf.Cos(a) * radius);
                GUI.Label(new Rect(at.x - box * 0.5f, at.y - box * 0.5f, box, box), text(v), _tick);
            }
        }

        /// <summary>A needle from the centre, turned to its share of the sweep.</summary>
        static void Needle(Rect dial, float share)
        {
            Matrix4x4 before = GUI.matrix;
            GUIUtility.RotateAroundPivot(Angle(share), dial.center);
            float width = Mathf.Max(2f, dial.width * 0.018f), length = dial.width * 0.44f;
            GUI.color = new Color(1f, 0.35f, 0.15f);
            GUI.DrawTexture(new Rect(dial.center.x - width * 0.5f, dial.center.y - length, width, length), Texture2D.whiteTexture);
            GUI.matrix = before;
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            float hub = dial.width * 0.06f;
            GUI.DrawTexture(new Rect(dial.center.x - hub * 0.5f, dial.center.y - hub * 0.5f, hub, hub), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void BuildFaces(int size)
        {
            _facePixels = size;
            if (_tachometer != null) Destroy(_tachometer);
            if (_speedometer != null) Destroy(_speedometer);
            _tachometer = Face(size, MaxRpm, 1000f, 500f, RedlineRpm);
            _speedometer = Face(size, MaxKph, 40f, 20f, float.MaxValue);
        }

        /// <summary>
        /// A dial face: a translucent dark disc, a light ring along the sweep, major and minor
        /// ticks, and the ring and ticks red from redline up. Edges are faded over a pixel so
        /// they do not stair-step.
        /// </summary>
        static Texture2D Face(int size, float max, float major, float minor, float redline)
        {
            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f, outer = size * 0.5f - 1f;
            float ringInner = outer - Mathf.Max(3f, size * 0.02f);
            float majorInner = outer - size * 0.09f, minorInner = outer - size * 0.05f;
            var disc = new Color(0.05f, 0.05f, 0.07f, 0.72f);
            var light = new Color(0.92f, 0.92f, 0.92f, 1f);
            var red = new Color(0.9f, 0.15f, 0.1f, 1f);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Texture rows count up from the bottom; the dial's up is +y here.
                float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > outer + 1f) { pixels[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                Color colour = disc;
                colour.a *= Mathf.Clamp01(outer + 1f - r);

                float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;      // clockwise from up
                float share = (angle + Sweep * 0.5f) / Sweep;
                bool onSweep = share >= 0f && share <= 1f;
                if (onSweep)
                {
                    float value = share * max;
                    Color mark = value >= redline ? red : light;
                    float ring = Mathf.Clamp01(Mathf.Min(r - ringInner + 1f, outer + 1f - r));
                    // Distance along the arc, in pixels, to the nearest tick of each kind.
                    float arc = Mathf.Deg2Rad * Sweep / max * r;
                    float toMajor = Mathf.Abs(Mathf.Repeat(value + major * 0.5f, major) - major * 0.5f) * arc;
                    float toMinor = Mathf.Abs(Mathf.Repeat(value + minor * 0.5f, minor) - minor * 0.5f) * arc;
                    float half = Mathf.Max(1f, size * 0.006f);
                    float tick = Mathf.Max(r >= majorInner ? Mathf.Clamp01(half + 0.5f - toMajor) : 0f,
                                           r >= minorInner ? Mathf.Clamp01(half * 0.6f + 0.5f - toMinor) : 0f);
                    float amount = Mathf.Max(ring, tick) * Mathf.Clamp01(outer + 1f - r);
                    if (value >= redline && r >= ringInner - size * 0.04f)
                        amount = Mathf.Max(amount, 0.55f * Mathf.Clamp01(outer + 1f - r));
                    colour = Color.Lerp(colour, mark, amount);
                }
                pixels[y * size + x] = colour;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
