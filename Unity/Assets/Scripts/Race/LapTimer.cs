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
        GUIStyle _style;

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

        void OnGUI()
        {
            if (!enabled) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };

            float current = _running ? _clock - _lapStart : 0f;
            string text = $"{track.trackName}\nLap {_laps + 1}   {Format(current)}" +
                          $"\nLast  {(_lastLap < 0f ? "-" : Format(_lastLap))}" +
                          $"\nBest  {(_bestLap < 0f ? "-" : Format(_bestLap))}";
            if (track.referenceLapSeconds > 0f)
                text += $"\nAI ref {Format(track.referenceLapSeconds)}";

            for (int s = 0; s < 3; s++)
            {
                bool done = s < _nextGate;
                text += $"\nS{s + 1}  " + (done ? $"{_splits[s],6:0.000}{Delta(_splits[s], _bestSectors[s])}" : "  -");
            }

            float width = 290f, height = 26f * (track.referenceLapSeconds > 0f ? 8 : 7) + 12f;
            var box = new Rect(Screen.width - width - 10f, 10f, width, height);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 10f, box.y + 4f, width - 20f, height), text, _style);
        }

        static string Delta(float value, float best)
            => best == float.MaxValue || best == value ? "" : $"  {value - best:+0.000;-0.000}";

        static string Format(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
