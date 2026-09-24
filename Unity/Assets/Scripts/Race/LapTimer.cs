using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Times laps on a built circuit: current, last and best lap, three sectors against your
    /// best sectors, a lap count, and the headless reference driver's time to aim at.
    ///
    /// A lap counts only when the car has passed the two sector gates, a third and two thirds
    /// of the way round, in order, and then crossed the line. Reversing over the line or
    /// cutting across can therefore never complete one. The clock starts when the car first
    /// moves off the grid, so lap one includes the standing start, as it does in a race. A
    /// respawn, which shows up as the car jumping more than 25 m in one physics step, abandons
    /// the lap in progress. The best lap is kept per circuit between sessions.
    ///
    /// Also shows WRONG WAY across the screen when the car has pointed back along the lap
    /// for WrongWaySeconds, so that after a spin it is clear which way to turn.
    /// </summary>
    public sealed class LapTimer : MonoBehaviour
    {
        [SerializeField] Rigidbody car;
        [SerializeField] TrackPath track;

        float _clock, _lapStart;
        bool _running;
        int _index, _nextGate, _laps;
        Vector3 _lastPosition;
        readonly float[] _splits = new float[3];
        readonly float[] _bestSectors = { float.MaxValue, float.MaxValue, float.MaxValue };
        float _lastLap = -1f, _bestLap = -1f;
        GUIStyle _warningStyle;
        float _wrongWayFor;

        // For the live delta: the lap time at each centreline sample on this lap, and on the
        // best lap of this session. Overwritten on every pass, so the grid, which lap one
        // drives through at its start and its end, holds the end.
        float[] _trace, _bestTrace;
        float _bestTraceLap = float.MaxValue;
        readonly float[] _sectorDoneAt = { -10f, -10f, -10f };
        float _bannerUntil = -1f;
        string _banner = "";

        const float WrongWaySeconds = 0.75f;

        string BestKey => $"CarRace.BestLap.{track.trackName}";

        void Start()
        {
            if (car == null || track == null || track.centre.Length < 3) { enabled = false; return; }
            _bestLap = PlayerPrefs.GetFloat(BestKey, -1f);
            _lastPosition = car.position;
            _trace = new float[track.centre.Length];
            System.Array.Fill(_trace, float.NaN);
        }

        void FixedUpdate()
        {
            _clock += Time.fixedDeltaTime;
            Vector3 position = car.position;
            int n = track.centre.Length;

            if ((position - _lastPosition).sqrMagnitude > 25f * 25f)
            {
                _index = track.Nearest(position, 0, back: 0, ahead: n - 1);
                _running = false;
                _nextGate = 0;
                System.Array.Fill(_trace, float.NaN);
            }
            _lastPosition = position;
            _index = track.Nearest(position, _index);

            Vector3 along = track.centre[(_index + 1) % n] - track.centre[(_index - 1 + n) % n];
            Vector3 facing = car.transform.forward;
            along.y = 0f;
            facing.y = 0f;
            bool backwards = Vector3.Dot(along.normalized, facing.normalized) < -0.2f;
            _wrongWayFor = backwards ? _wrongWayFor + Time.fixedDeltaTime : 0f;

            if (!_running)
            {
                if (car.linearVelocity.magnitude < 1f) return;
                _running = true;
                _lapStart = _clock;
                _nextGate = 0;
            }

            float lap = _clock - _lapStart;
            if (_nextGate == 0 && _index >= n / 3 && _index < 2 * n / 3)
            {
                _splits[0] = lap;
                _nextGate = 1;
                _sectorDoneAt[0] = _clock;
            }
            else if (_nextGate == 1 && _index >= 2 * n / 3)
            {
                _splits[1] = lap - _splits[0];
                _nextGate = 2;
                _sectorDoneAt[1] = _clock;
            }
            else if (_nextGate == 2 && _index < n / 3)
            {
                _splits[2] = lap - _splits[0] - _splits[1];
                _sectorDoneAt[2] = _clock;
                if (lap < _bestTraceLap)
                {
                    // Measured from the line. Lap one starts on the grid behind it, so its
                    // trace reads the seconds to the line everywhere; without this, the lap
                    // after it would show that much gained all the way round.
                    float atLine = float.IsNaN(_trace[0]) ? 0f : _trace[0];
                    _bestTrace = new float[_trace.Length];
                    for (int i = 0; i < _trace.Length; i++) _bestTrace[i] = _trace[i] - atLine;
                    _bestTraceLap = lap;
                }
                System.Array.Fill(_trace, float.NaN);
                for (int s = 0; s < 3; s++) _bestSectors[s] = Mathf.Min(_bestSectors[s], _splits[s]);
                _lastLap = lap;
                _laps++;
                if (_bestLap < 0f || lap < _bestLap)
                {
                    _banner = $"NEW BEST LAP   {Format(lap)}";
                    _bannerUntil = _clock + BannerSeconds;
                    _bestLap = lap;
                    PlayerPrefs.SetFloat(BestKey, lap);
                    PlayerPrefs.Save();
                }
                _lapStart = _clock;
                _nextGate = 0;
            }

            // After the gates, so the step that ends a lap records the new lap's 0.
            _trace[_index] = _clock - _lapStart;
        }

        // The timing panel, in the manner of broadcast graphics: purple a new best sector or
        // lap, yellow slower than the best, green when there is no best to compare with yet.
        static readonly Color Panel = new Color(0.05f, 0.06f, 0.1f, 0.88f);
        static readonly Color Accent = new Color(0.9f, 0.12f, 0.1f);
        static readonly Color AccentLight = new Color(1f, 0.55f, 0.1f);
        static readonly Color Muted = new Color(0.62f, 0.64f, 0.72f);
        static readonly Color Block = new Color(0.12f, 0.13f, 0.18f, 0.95f);
        static readonly Color Pending = new Color(0.28f, 0.29f, 0.35f);
        static readonly Color Purple = new Color(0.66f, 0.32f, 1f);
        static readonly Color Green = new Color(0.2f, 0.8f, 0.35f);
        static readonly Color Yellow = new Color(1f, 0.78f, 0.1f);
        static readonly Color Ahead = new Color(0.25f, 0.9f, 0.4f);
        static readonly Color Behind = new Color(1f, 0.3f, 0.25f);
        const float BannerSeconds = 3f;
        const float PulseSeconds = 0.8f;

        GUIStyle _headerStyle, _badgeStyle, _bigStyle, _deltaStyle, _labelStyle, _valueStyle,
                 _sectorLabel, _sectorTime, _sectorDelta, _bannerStyle;

        void OnGUI()
        {
            if (!enabled || Hud.Hidden) return;
            Styles();

            float width = Hud.Px(360f), pad = Hud.Px(16f);
            float x = Screen.width - width - Hud.Px(12f), y = Hud.Px(12f);
            bool reference = track.referenceLapSeconds > 0f;
            float height = Hud.Px(reference ? 318f : 294f);
            int n = track.centre.Length;
            float current = _running ? _clock - _lapStart : 0f;

            Hud.Rounded(new Rect(x, y, width, height), Panel);

            // Header band: rounded on top, square below, a lighter stripe under it.
            float header = Hud.Px(34f);
            Hud.Rounded(new Rect(x, y, width, header), Accent);
            Hud.Fill(new Rect(x, y + header * 0.5f, width, header * 0.5f), Accent);
            Hud.Fill(new Rect(x, y + header, width, Hud.Px(3f)), AccentLight);
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, header), track.trackName.ToUpperInvariant(), _headerStyle);
            string lapText = $"LAP {_laps + 1}";
            float badgeWidth = Hud.Px(64f);
            var badge = new Rect(x + width - pad - badgeWidth, y + Hud.Px(6f), badgeWidth, header - Hud.Px(12f));
            Hud.Rounded(badge, Color.white);
            GUI.Label(badge, lapText, _badgeStyle);

            // The lap being driven, large and shadowed, and the live gap to the best lap.
            float row = y + header + Hud.Px(10f);
            var timeRect = new Rect(x + pad, row, width - 2f * pad, Hud.Px(50f));
            _bigStyle.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(timeRect.x + Hud.Px(2f), timeRect.y + Hud.Px(2f), timeRect.width, timeRect.height), Format(current), _bigStyle);
            _bigStyle.normal.textColor = Color.white;
            GUI.Label(timeRect, Format(current), _bigStyle);

            if (_running && _bestTrace != null && !float.IsNaN(_bestTrace[_index]))
            {
                float delta = current - _bestTrace[_index];
                _deltaStyle.normal.textColor = delta <= 0f ? Ahead : Behind;
                GUI.Label(timeRect, (delta <= 0f ? "\u25BC " : "\u25B2 ") + delta.ToString("+0.000;-0.000"), _deltaStyle);
            }
            row += Hud.Px(56f);

            // Lap progress, a segment per sector, each in its sector's colour once done.
            float barHeight = Hud.Px(6f), gap = Hud.Px(4f);
            float segment = (width - 2f * pad - 2f * gap) / 3f;
            float progress = !_running || (_nextGate == 0 && _index >= 2 * n / 3) ? 0f : (float)_index / n;
            for (int sct = 0; sct < 3; sct++)
            {
                var bar = new Rect(x + pad + sct * (segment + gap), row, segment, barHeight);
                Hud.Fill(bar, Pending);
                float filled = Mathf.Clamp01(progress * 3f - sct);
                if (filled > 0f) Hud.Fill(new Rect(bar.x, bar.y, bar.width * filled, bar.height), sct < _nextGate ? SectorColour(sct) : Color.white);
            }
            row += barHeight + Hud.Px(10f);

            Row(x, pad, width, ref row, "LAST", _lastLap < 0f ? "-" : Format(_lastLap), _lastLap > 0f && _lastLap <= _bestLap ? Purple : Color.white);
            Row(x, pad, width, ref row, "BEST", _bestLap < 0f ? "-" : Format(_bestLap), _bestLap < 0f ? Color.white : Purple);
            if (reference) Row(x, pad, width, ref row, "AI REF", Format(track.referenceLapSeconds), Muted);

            // Sector blocks: filled in their colour once done, with a short pulse as they finish.
            row += Hud.Px(10f);
            float blockWidth = (width - 2f * pad - 2f * gap) / 3f, blockHeight = Hud.Px(64f);
            for (int sct = 0; sct < 3; sct++)
            {
                var block = new Rect(x + pad + sct * (blockWidth + gap), row, blockWidth, blockHeight);
                bool done = sct < _nextGate;
                Color colour = done ? SectorColour(sct) : Block;
                float pulse = done ? Mathf.Clamp01(1f - (_clock - _sectorDoneAt[sct]) / PulseSeconds) : 0f;
                if (pulse > 0f)
                {
                    float grow = Hud.Px(4f) * pulse;
                    Hud.Rounded(new Rect(block.x - grow, block.y - grow, block.width + 2f * grow, block.height + 2f * grow),
                            new Color(1f, 1f, 1f, 0.7f * pulse));
                }
                Hud.Rounded(block, done ? new Color(colour.r, colour.g, colour.b, 0.9f) : Block);
                Color text = done && colour == Yellow ? new Color(0.1f, 0.08f, 0.02f) : Color.white;
                _sectorLabel.normal.textColor = done ? text : Muted;
                _sectorTime.normal.textColor = text;
                _sectorDelta.normal.textColor = text;
                GUI.Label(new Rect(block.x, block.y + Hud.Px(4f), block.width, Hud.Px(16f)), $"SECTOR {sct + 1}", _sectorLabel);
                GUI.Label(new Rect(block.x, block.y + Hud.Px(20f), block.width, Hud.Px(24f)),
                          done ? _splits[sct].ToString("0.000") : "-", _sectorTime);
                if (done && _bestSectors[sct] != float.MaxValue)
                    GUI.Label(new Rect(block.x, block.y + Hud.Px(43f), block.width, Hud.Px(16f)),
                              (_splits[sct] - _bestSectors[sct]).ToString("+0.000;-0.000"), _sectorDelta);
            }

            // A new best lap: a purple banner under the position box, flashing for a few seconds.
            if (_clock < _bannerUntil)
            {
                float left = _bannerUntil - _clock;
                float alpha = Mathf.Clamp01(left / 0.5f) * (0.75f + 0.25f * Mathf.Sin(_clock * 12f));
                float bannerWidth = Hud.Px(420f), bannerHeight = Hud.Px(52f);
                var banner = new Rect((Screen.width - bannerWidth) * 0.5f, Hud.Px(84f), bannerWidth, bannerHeight);
                Hud.Rounded(banner, new Color(Purple.r, Purple.g, Purple.b, 0.92f * alpha));
                _bannerStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
                GUI.Label(banner, _banner, _bannerStyle);
            }

            if (_wrongWayFor >= WrongWaySeconds)
            {
                _warningStyle ??= new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.15f, 0.1f) }
                };
                _warningStyle.fontSize = Hud.Font(56);
                GUI.Label(new Rect(0f, Screen.height * 0.25f, Screen.width, Hud.Px(80f)), "WRONG WAY", _warningStyle);
            }
        }

        Color SectorColour(int sector)
        {
            float best = _bestSectors[sector];
            return best == float.MaxValue ? Green : _splits[sector] <= best ? Purple : Yellow;
        }

        void Row(float x, float pad, float width, ref float y, string label, string value, Color colour)
        {
            float height = Hud.Px(25f);
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, height), label, _labelStyle);
            _valueStyle.normal.textColor = colour;
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, height), value, _valueStyle);
            y += height;
        }

        void Styles()
        {
            _headerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _badgeStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.1f, 0.1f, 0.14f) } };
            _bigStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _deltaStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _labelStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Muted } };
            _valueStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _sectorLabel ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _sectorTime ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _sectorDelta ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _bannerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter };
            _headerStyle.fontSize = Hud.Font(15);
            _badgeStyle.fontSize = Hud.Font(13);
            _bigStyle.fontSize = Hud.Font(44);
            _deltaStyle.fontSize = Hud.Font(20);
            _labelStyle.fontSize = Hud.Font(13);
            _valueStyle.fontSize = Hud.Font(18);
            _sectorLabel.fontSize = Hud.Font(10);
            _sectorTime.fontSize = Hud.Font(18);
            _sectorDelta.fontSize = Hud.Font(12);
            _bannerStyle.fontSize = Hud.Font(26);
        }


        static string Format(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
