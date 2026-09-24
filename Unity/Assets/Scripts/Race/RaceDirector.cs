using UnityEngine;
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
    /// The AI hold their brakes on the grid until the player moves off, so the start is
    /// yours to take. An AI car that has been stopped for StuckSeconds, pushed into a wall or
    /// turned round in a crash, is put back on its racing line where it was.
    ///
    /// Also shows the player's place in the field, by distance covered since the start.
    /// </summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        [SerializeField] TrackPath track;
        [SerializeField] CarController player;
        [SerializeField] CarController[] aiCars = new CarController[0];

        [Tooltip("Share of the car's grip each AI driver uses, one per AI car. The harness races " +
                 "0.78 to 0.85; lower is slower and more forgiving.")]
        [SerializeField] float[] aiPace = { 0.80f, 0.77f, 0.74f };

        [Tooltip("The pace the AI assume you drive at, when they judge whether a pass on you can " +
                 "work. They cannot know your plan, and with none they would never try to pass.")]
        [SerializeField] float playerPace = 0.7f;

        const float ReactionSeconds = 0.02f;
        const float StuckSeconds = 5f;

        TrackData _track;
        RaceDriver[] _drivers;
        RaceDriver.Seen[] _field;
        float[] _playerPlan;
        float[] _stuckFor;
        Rigidbody _playerBody;
        int _playerIndex;
        float _playerProgress;     // samples since the start line, negative on the grid
        Vector3 _playerLast;
        float _sinceReaction;
        bool _started;
        GUIStyle _style;

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

            _playerBody = player.GetComponent<Rigidbody>();
            _playerLast = player.transform.position;
            _playerIndex = track.Nearest(_playerLast, 0, back: 0, ahead: n - 1);
            _playerProgress = _playerIndex > n / 2 ? _playerIndex - n : _playerIndex;
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
            if (!_started && _playerBody.linearVelocity.magnitude > 1f) _started = true;

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

                bool stopped = _started && _field[i].SpeedMs < 1f && _field[i].SpeedMs > -1f;
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

        void OnGUI()
        {
            if (!enabled || _drivers == null) return;
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            int n = _track.Count;
            int place = 1;
            foreach (RaceDriver driver in _drivers)
                if (driver.Path.Laps * n + driver.Path.Index > _playerProgress) place++;

            var box = new Rect(Screen.width * 0.5f - 90f, 10f, 180f, 60f);
            GUI.Box(box, GUIContent.none);
            GUI.Label(box, $"P{place} / {_drivers.Length + 1}", _style);
        }
    }
}
