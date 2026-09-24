using UnityEngine;
using UnityEngine.SceneManagement;
using CarRace.Harness;
using CarRace.Track;
using CarRace.Vehicle;
using Vec3 = System.Numerics.Vector3;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Races AI cars against the player on a built circuit, with the same RaceDriver the
    /// headless harness races sixteen of. Every physics step each AI car's CarController asks
    /// its driver for inputs through Autopilot; every ReactionSeconds this shows every driver
    /// the whole field, the player included, exactly as RaceRun does.
    ///
    /// The race: a 3, 2, 1, GO countdown with every car, the player's included, held on its
    /// brakes; then raceLaps laps kept by the headless RaceControl, the same bookkeeping the
    /// harness race is judged by, with the clock starting at GO. The grid is behind the line,
    /// so every car starts on lap -1 and crossing the line begins lap one. When the player
    /// takes the flag a results table appears and fills in as the AI finish; Enter restarts.
    /// The AI keep driving after their flag, since a car parked on the racing line is a hazard.
    ///
    /// An AI car that has been stopped for StuckSeconds, pushed into a wall or turned round in
    /// a crash, is put back on its racing line where it was.
    /// </summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        [SerializeField] TrackPath track;
        [SerializeField] CarController player;
        [SerializeField] CarController[] aiCars = new CarController[0];

        [Tooltip("Share of the car's grip each AI driver uses, one per AI car. The harness races " +
                 "0.78 to 0.85; lower is slower and more forgiving.")]
        [SerializeField] float[] aiPace = { 0.85f, 0.82f, 0.79f };

        [Tooltip("The pace the AI assume you drive at, when they judge whether a pass on you can " +
                 "work. They cannot know your plan, and with none they would never try to pass.")]
        [SerializeField] float playerPace = 0.7f;

        [SerializeField, Min(1)] int raceLaps = 3;

        const float ReactionSeconds = 0.02f;
        const float StuckSeconds = 5f;
        const float CountdownSeconds = 3f;
        const float GoShownSeconds = 1f;

        TrackData _track;
        RaceDriver[] _drivers;
        RaceDriver.Seen[] _field;
        float[] _playerPlan;
        float[] _stuckFor;
        int _playerIndex;
        float _playerProgress;     // samples since the start line, negative on the grid
        Vector3 _playerLast;
        float _sinceReaction;
        bool _started;
        float _countdown = CountdownSeconds;
        float _raceTime;
        RaceControl _control;
        CarController[] _cars;     // RaceControl's order: the AI in grid order, then the player
        GUIStyle _style, _bigStyle, _tableStyle;

        void Start()
        {
            if (track == null || player == null || track.line.Length < 3 || aiCars.Length == 0)
            {
                enabled = false;
                return;
            }

            _track = track.ToTrackData();
            _drivers = new RaceDriver[aiCars.Length];
            _field = new RaceDriver.Seen[aiCars.Length + 1];
            _stuckFor = new float[aiCars.Length];
            int n = _track.Count;

            for (int i = 0; i < aiCars.Length; i++)
            {
                CarConfig config = aiCars[i].Sim.Config;
                float pace = aiPace.Length > 0 ? aiPace[Mathf.Min(i, aiPace.Length - 1)] : 0.8f;
                var driver = new RaceDriver($"AI {i + 1}", _track, config, PlanningLimits(config), pace);

                // Gridded behind the line, as in RaceRun.PlaceOnGrid: lap -1, so crossing the
                // line starts the first lap, and on its grid slot's offset from the racing line.
                Vec3 position = ToNumerics(aiCars[i].transform.position);
                int index = NearestOnLine(position);
                driver.Path.StartAt(index, index > n / 2 ? -1 : 0);
                driver.Path.LineOffsetM = _track.LateralOffset(_track.Line, index, position);
                _drivers[i] = driver;

                int k = i;
                aiCars[i].Autopilot = (body, dt) => _started ? _drivers[k].Drive(body, dt)
                                                             : new VehicleInputs { Brake = 1f };
                aiCars[i].RecoveryPose = () => OnLine(k);
            }

            // Built the way RaceDriver builds its own: every limit scaled by the pace.
            CarConfig playerConfig = player.Sim.Config;
            SpeedPlan.Limits limits = PlanningLimits(playerConfig);
            limits.LateralMs2 *= playerPace;
            limits.BrakingMs2 *= playerPace;
            limits.TractionMs2 *= playerPace;
            _playerPlan = SpeedPlan.Build(_track, limits);

            _playerLast = player.transform.position;
            _playerIndex = track.Nearest(_playerLast, 0, back: 0, ahead: n - 1);
            _playerProgress = _playerIndex > n / 2 ? _playerIndex - n : _playerIndex;

            // Held on the brakes, like the AI, until GO.
            player.Autopilot = (body, dt) => new VehicleInputs { Brake = 1f };

            _cars = new CarController[aiCars.Length + 1];
            var names = new string[_cars.Length];
            for (int i = 0; i < aiCars.Length; i++) { _cars[i] = aiCars[i]; names[i] = _drivers[i].Name; }
            _cars[aiCars.Length] = player;
            names[aiCars.Length] = "You";
            _control = new RaceControl(names, raceLaps);
            for (int i = 0; i < _cars.Length; i++)
            {
                int k = i;
                _cars[i].gameObject.AddComponent<CarContacts>().Touched = () => _control.Entries[k].Contacts++;
            }
        }

        /// <summary>The same limits the harness plans with (LapRun.PlanningLimits), from the
        /// car's own closed-form numbers.</summary>
        static SpeedPlan.Limits PlanningLimits(CarConfig config) => new SpeedPlan.Limits
        {
            LateralMs2 = Analytic.SkidpadCeilingG(config) * 0.85f * CarRace.Vehicle.Physics.Gravity,
            BrakingMs2 = (27.78f * 27.78f) / (2f * Analytic.BrakingMetres(config)) * 0.85f,
            TractionMs2 = (100f / 3.6f) / Analytic.ZeroToHundredSeconds(config),
            PowerW = Analytic.PeakPowerWatts(config) * config.DrivetrainEfficiency,
            MassKg = config.Mass,
            TopSpeedMs = Analytic.TopSpeedKph(config) / 3.6f,
        };

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            TrackPlayer();
            if (!_started)
            {
                _countdown -= dt;
                if (_countdown > 0f) return;
                _started = true;
                player.Autopilot = null;
            }

            // Every step rather than every reaction interval, so lap times are to 5 ms.
            _raceTime += dt;
            int n = _track.Count;
            for (int i = 0; i < aiCars.Length; i++)
                _control.Update(i, _raceTime, _drivers[i].Path.Laps, _drivers[i].Path.ProgressM);
            // Measured the way PathDriver.ProgressM measures the AI, so the two rank together.
            int playerLaps = Mathf.FloorToInt(_playerProgress / n);
            float playerProgressM = playerLaps * _track.LengthM + (_playerProgress - playerLaps * n) * _track.SampleSpacingM;
            _control.Update(aiCars.Length, _raceTime, playerLaps, playerProgressM);
            _control.Rank();

            _sinceReaction += dt;
            if (_sinceReaction < ReactionSeconds) return;
            float elapsed = _sinceReaction;
            _sinceReaction = 0f;

            for (int i = 0; i < aiCars.Length; i++)
            {
                PathDriver path = _drivers[i].Path;
                _field[i] = Seen(aiCars[i], path.Index, path.LateralFromLineM, path.Plan);
            }
            Vec3 playerPosition = ToNumerics(player.transform.position);
            _field[aiCars.Length] = Seen(player, _playerIndex,
                                         _track.LateralOffset(_track.Line, _playerIndex, playerPosition),
                                         _playerPlan);

            for (int i = 0; i < aiCars.Length; i++)
            {
                _drivers[i].Observe(_track, _field, i, elapsed);

                bool stopped = _field[i].SpeedMs < 1f && _field[i].SpeedMs > -1f;
                _stuckFor[i] = stopped ? _stuckFor[i] + elapsed : 0f;
                if (_stuckFor[i] < StuckSeconds) continue;
                aiCars[i].Recover();
                _stuckFor[i] = 0f;
            }
        }

        /// <summary>
        /// Where the player is on the lap, as distance covered in samples. Each step adds the
        /// shortest way round from the last sample, so crossing the line counts a lap and a
        /// recovery or restart, which jumps, is searched for over the whole lap first.
        /// </summary>
        void TrackPlayer()
        {
            int n = _track.Count;
            Vector3 position = player.transform.position;
            int index = (position - _playerLast).sqrMagnitude > 25f * 25f
                ? track.Nearest(position, 0, back: 0, ahead: n - 1)
                : track.Nearest(position, _playerIndex);
            _playerLast = position;

            int step = ((index - _playerIndex) % n + n) % n;
            if (step > n / 2) step -= n;
            _playerProgress += step;
            _playerIndex = index;
        }

        RaceDriver.Seen Seen(CarController car, int index, float lateral, System.Collections.Generic.IReadOnlyList<float> plan)
        {
            Transform t = car.transform;
            Rigidbody body = car.GetComponent<Rigidbody>();
            return new RaceDriver.Seen
            {
                Index = index,
                LateralM = lateral,
                SpeedMs = Vector3.Dot(body.linearVelocity, t.forward),
                Plan = plan,
                Position = ToNumerics(t.position),
            };
        }

        /// <summary>Where a stuck AI car goes back to: its racing line at its place on the lap,
        /// pointing along it, a little above the road so it settles onto its wheels.</summary>
        (Vector3 position, Quaternion rotation) OnLine(int k)
        {
            PathDriver path = _drivers[k].Path;
            path.LineOffsetM = 0f;
            Vec3 at = _track.Line[path.Index];
            Vec3 along = _track.Tangent(_track.Line, path.Index);
            float lift = aiCars[k].Sim.Config.CgHeight + 0.3f;
            return (new Vector3(at.X, at.Y + lift, at.Z),
                    Quaternion.LookRotation(new Vector3(along.X, 0f, along.Z), Vector3.up));
        }

        int NearestOnLine(Vec3 position)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _track.Count; i++)
            {
                Vec3 delta = _track.Line[i] - position;
                delta.Y = 0f;
                float distance = delta.LengthSquared();
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        static Vec3 ToNumerics(Vector3 v) => new Vec3(v.x, v.y, v.z);

        void Update()
        {
            if (_control != null && PlayerEntry.Finished && Input.GetKeyDown(KeyCode.Return))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        RaceControl.Entry PlayerEntry => _control.Entries[aiCars.Length];

        void OnGUI()
        {
            if (!enabled || _control == null) return;
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _bigStyle ??= new GUIStyle(_style) { fontSize = 120 };
            _tableStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18, font = Font.CreateDynamicFontFromOSFont("Consolas", 18),
                normal = { textColor = Color.white }
            };

            RaceControl.Entry me = PlayerEntry;
            int lap = Mathf.Clamp(me.LapsComplete + 1, 1, raceLaps);
            var box = new Rect(Screen.width * 0.5f - 160f, 10f, 320f, 56f);
            GUI.Box(box, GUIContent.none);
            GUI.Label(box, $"P{me.Position} / {_cars.Length}    Lap {lap} / {raceLaps}", _style);

            if (!_started)
            {
                _bigStyle.normal.textColor = new Color(1f, 0.2f, 0.15f);
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 160f),
                          Mathf.CeilToInt(_countdown).ToString(), _bigStyle);
            }
            else if (_raceTime < GoShownSeconds)
            {
                _bigStyle.normal.textColor = new Color(0.2f, 1f, 0.3f);
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 160f), "GO", _bigStyle);
            }

            if (me.Finished) Results();
        }

        /// <summary>The classification, as the harness prints it: finishers in the order they
        /// took the flag, then everyone still running by distance.</summary>
        void Results()
        {
            RaceControl.Entry[] order = _control.Classification();
            float winner = order[0].Finished ? order[0].FinishedAtS : 0f;
            float fastest = float.MaxValue;
            foreach (RaceControl.Entry e in order) fastest = Mathf.Min(fastest, e.BestLapS);

            var text = new System.Text.StringBuilder();
            text.AppendLine($"{"Pos",-4}{"Driver",-8}{"Grid",5}{"+/-",5}{"Best lap",11}{"Race time",11}{"Gap",10}{"Hits",6}");
            text.AppendLine(new string('-', 60));
            foreach (RaceControl.Entry e in order)
            {
                int gained = e.Grid - e.Position;
                string best = e.BestLapS < float.MaxValue ? Format(e.BestLapS) + (e.BestLapS == fastest ? "*" : " ") : "-";
                string time = e.Finished ? Format(e.FinishedAtS) : $"lap {Mathf.Clamp(e.LapsComplete + 1, 1, raceLaps)}";
                string gap = !e.Finished ? "running" : e.Position == 1 ? "-" : $"+{e.FinishedAtS - winner:0.000}";
                text.AppendLine($"{e.Position,-4}{e.Name,-8}{e.Grid,5}{(gained == 0 ? "0" : gained.ToString("+0;-0")),5}{best,11}{time,11}{gap,10}{e.Contacts,6}");
            }
            text.AppendLine();
            text.Append("* fastest lap        Enter: race again");

            float width = 640f, height = 30f + 26f * (order.Length + 5);
            var panel = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.5f - height * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUI.Box(panel, GUIContent.none);   // twice: one box is too faint to read a table over
            GUI.Label(new Rect(panel.x, panel.y + 6f, width, 34f), "RESULTS", _style);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 46f, width - 40f, height - 50f), text.ToString(), _tableStyle);
        }

        static string Format(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
