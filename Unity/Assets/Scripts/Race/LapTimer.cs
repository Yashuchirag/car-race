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

        const float WrongWaySeconds = 0.75f;

        string BestKey => $"CarRace.BestLap.{track.trackName}";

        void Start()
        {
            if (car == null || track == null || track.centre.Length < 3) { enabled = false; return; }
            _bestLap = PlayerPrefs.GetFloat(BestKey, -1f);
            _lastPosition = car.position;
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
            }
            else if (_nextGate == 1 && _index >= 2 * n / 3)
            {
                _splits[1] = lap - _splits[0];
                _nextGate = 2;
            }
            else if (_nextGate == 2 && _index < n / 3)
            {
                _splits[2] = lap - _splits[0] - _splits[1];
                for (int s = 0; s < 3; s++) _bestSectors[s] = Mathf.Min(_bestSectors[s], _splits[s]);
                _lastLap = lap;
                _laps++;
                if (_bestLap < 0f || lap < _bestLap)
                {
                    _bestLap = lap;
                    PlayerPrefs.SetFloat(BestKey, lap);
                    PlayerPrefs.Save();
                }
                _lapStart = _clock;
                _nextGate = 0;
            }
        }

        // The timing panel's colours, as broadcast timing graphics use them: purple a new best
        // sector, yellow slower than the best, green when there is no best to compare with yet.
        static readonly Color Panel = new Color(0.06f, 0.06f, 0.08f, 0.82f);
        static readonly Color Header = new Color(0.02f, 0.02f, 0.03f, 0.9f);
        static readonly Color Accent = new Color(0.9f, 0.15f, 0.1f);
        static readonly Color Muted = new Color(0.65f, 0.65f, 0.7f);
        static readonly Color Block = new Color(0.13f, 0.13f, 0.16f, 0.95f);
        static readonly Color Pending = new Color(0.3f, 0.3f, 0.35f);
        static readonly Color Purple = new Color(0.72f, 0.38f, 1f);
        static readonly Color Green = new Color(0.25f, 0.85f, 0.35f);
        static readonly Color Yellow = new Color(1f, 0.82f, 0.15f);

        GUIStyle _headerStyle, _bigStyle, _labelStyle, _valueStyle, _sectorLabel, _sectorTime, _sectorDelta;

        void OnGUI()
        {
            if (!enabled || Hud.Hidden) return;
            Styles();

            float width = Hud.Px(330f), pad = Hud.Px(14f);
            float x = Screen.width - width - Hud.Px(10f), y = Hud.Px(10f);
            float height = Hud.Px(track.referenceLapSeconds > 0f ? 250f : 226f);

            Fill(new Rect(x, y, width, height), Panel);
            Fill(new Rect(x, y, Hud.Px(4f), height), Accent);

            // Header: circuit and lap number.
            float headerHeight = Hud.Px(30f);
            Fill(new Rect(x + Hud.Px(4f), y, width - Hud.Px(4f), headerHeight), Header);
            _headerStyle.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, headerHeight), track.trackName.ToUpperInvariant(), _headerStyle);
            _headerStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, headerHeight), $"LAP {_laps + 1}", _headerStyle);

            // The lap being driven, large.
            float current = _running ? _clock - _lapStart : 0f;
            float row = y + headerHeight + Hud.Px(6f);
            GUI.Label(new Rect(x + pad, row, width - 2f * pad, Hud.Px(46f)), Format(current), _bigStyle);
            row += Hud.Px(52f);

            Row(x, pad, width, ref row, "LAST", _lastLap < 0f ? "-" : Format(_lastLap), Color.white);
            Row(x, pad, width, ref row, "BEST", _bestLap < 0f ? "-" : Format(_bestLap), _bestLap < 0f ? Color.white : Purple);
            if (track.referenceLapSeconds > 0f)
                Row(x, pad, width, ref row, "AI REF", Format(track.referenceLapSeconds), Muted);

            // Sectors: a block each, a bar in its colour along the top.
            row += Hud.Px(8f);
            float gap = Hud.Px(6f), blockWidth = (width - 2f * pad - 2f * gap) / 3f, blockHeight = Hud.Px(56f);
            for (int s = 0; s < 3; s++)
            {
                var block = new Rect(x + pad + s * (blockWidth + gap), row, blockWidth, blockHeight);
                bool done = s < _nextGate;
                float best = _bestSectors[s];
                Color colour = !done ? Pending
                             : best == float.MaxValue ? Green
                             : _splits[s] <= best ? Purple : Yellow;
                Fill(block, Block);
                Fill(new Rect(block.x, block.y, block.width, Hud.Px(4f)), colour);
                GUI.Label(new Rect(block.x, block.y + Hud.Px(6f), block.width, Hud.Px(16f)), $"S{s + 1}", _sectorLabel);
                _sectorTime.normal.textColor = done ? colour : Pending;
                GUI.Label(new Rect(block.x, block.y + Hud.Px(20f), block.width, Hud.Px(20f)),
                          done ? _splits[s].ToString("0.000") : "-", _sectorTime);
                if (done && best != float.MaxValue)
                {
                    _sectorDelta.normal.textColor = colour;
                    GUI.Label(new Rect(block.x, block.y + Hud.Px(38f), block.width, Hud.Px(16f)),
                              (_splits[s] - best).ToString("+0.000;-0.000"), _sectorDelta);
                }
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

        void Row(float x, float pad, float width, ref float y, string label, string value, Color colour)
        {
            float height = Hud.Px(24f);
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, height), label, _labelStyle);
            _valueStyle.normal.textColor = colour;
            GUI.Label(new Rect(x + pad, y, width - 2f * pad, height), value, _valueStyle);
            y += height;
        }

        static void Fill(Rect rect, Color colour)
        {
            Color before = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }

        void Styles()
        {
            _headerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.9f, 0.9f, 0.92f) } };
            _bigStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _labelStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Muted } };
            _valueStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            _sectorLabel ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Muted } };
            _sectorTime ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _sectorDelta ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _headerStyle.fontSize = Hud.Font(14);
            _bigStyle.fontSize = Hud.Font(38);
            _labelStyle.fontSize = Hud.Font(13);
            _valueStyle.fontSize = Hud.Font(18);
            _sectorLabel.fontSize = Hud.Font(12);
            _sectorTime.fontSize = Hud.Font(16);
            _sectorDelta.fontSize = Hud.Font(12);
        }

        static string Format(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
