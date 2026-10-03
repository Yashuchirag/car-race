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
    /// The race: a 3, 2, 1, GO countdown with every car, the player's included, held on its
    /// brakes; then raceLaps laps kept by the headless RaceControl, the same bookkeeping the
    /// harness race is judged by, with the clock starting at GO. The grid is behind the line,
    /// so every car starts on lap -1 and crossing the line begins lap one. When the player
    /// takes the flag a results table appears and fills in as the AI finish, with Race again
    /// (or Enter) and Main menu. A hint under the banner points at Esc, the pause menu.
    /// The AI keep driving after their flag, since a car parked on the racing line is a hazard.
    ///
    /// An AI car that has been stopped for StuckSeconds, pushed into a wall or turned round in
    /// a crash, is put back on its racing line where it was; sooner, after OffRoadStuckSeconds,
    /// if it is crawling off the road or pointing the wrong way. Each AI is told the grip under
    /// its wheels, so it drives the grass at a speed the grass will carry.
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

        /// <summary>Set before Start to have the player's car driven by an AI too, at the
        /// fastest AI pace: the benchmark's way of racing a full field with nobody at the keys.</summary>
        [System.NonSerialized] public bool AiDrivesPlayer;
        RaceDriver _playerDriver;

        const float ReactionSeconds = 0.02f;
        const float StuckSeconds = 5f;

        // Off the road or pointing the wrong way, and crawling, is stuck sooner: such a car is
        // not about to drive out of it, and the field keeps arriving.
        const float OffRoadStuckSeconds = 3f;
        const float CrawlingMs = 5f;
        const float WrongWayDegrees = 100f;
        const float CountdownSeconds = 3f;
        const float GoShownSeconds = 1f;

        TrackData _track;
        RaceDriver[] _drivers;
        RaceDriver.Seen[] _field;
        float[] _playerPlan;
        float[] _stuckFor;
        LapProgress _player;
        float _sinceReaction;
        bool _started;
        float _countdown = CountdownSeconds;
        int _beeped = int.MaxValue;
        readonly GameAudio.HoverTracker _hover = new GameAudio.HoverTracker();
        float _raceTime;
        RaceControl _control;
        CarController[] _cars;     // the cars this machine drives, whose touches it counts
        // Where each car's entry is in RaceControl: solo, the AI in grid order then the player;
        // in a LAN race, the host's grid order for everyone.
        int _playerEntry;
        int[] _aiEntry;
        bool[] _left;
        GUIStyle _style, _bigStyle, _tableStyle, _hintStyle, _buttonStyle;

        // A LAN race (LanRace): the other players' cars, and on a client the host's AI, all
        // moved from the network; the start held until the host's GO. Positions and results
        // across machines are not kept yet: each machine ranks the cars it drives.
        System.Func<float> _secondsToGo;
        System.Collections.Generic.List<RemoteCar> _remotes;
        readonly System.Collections.Generic.Dictionary<RemoteCar, int> _remoteIndex = new System.Collections.Generic.Dictionary<RemoteCar, int>();
        readonly System.Collections.Generic.Dictionary<RemoteCar, (int Entry, LapProgress Progress)> _remoteEntry
            = new System.Collections.Generic.Dictionary<RemoteCar, (int, LapProgress)>();
        CarController[] _grid;
        string[] _gridNames;
        bool _keepsStandings = true;
        bool _waiting;

        public CarController Player => player;
        public CarController[] AiCars => aiCars;
        public bool Lan => _secondsToGo != null;

        /// <summary>The race's bookkeeping. On a LAN client its numbers are the host's.</summary>
        public RaceControl Control => _control;

        /// <summary>The flags, kept where the standings are (solo and a LAN host); null on a client.</summary>
        /// <summary>The flags: kept here where the standings are (solo and a LAN host), and on a
        /// LAN client the host's, copied in as they arrive (ApplyFlags).</summary>
        public RaceFlags Flags => _flags;

        /// <summary>Your car is in the pit lane on the autopilot, and the driver doing it.</summary>
        public bool PlayerPitting => _pitting;
        public RaceDriver PitDriver => _pitDriver;
        public float TyreWearRate { get; private set; }

        // Your stop: a driver for your car that takes over at the pit entry line, in the pit lane
        // beyond the pit wall, and hands back at the exit line.
        RaceDriver _pitDriver;
        bool _pitting;

        // Flags: per race entry, where the car is, how far it has gone, whether it is in trouble
        // or still racing, its place in the field the AI see, and whether it has got going yet.
        RaceFlags _flags;
        int[] _flagIndex, _fieldOf;
        float[] _flagProgress;
        bool[] _trouble, _racing, _moving;

        // The safety car (SafetyCar), in a solo race with it switched on: a copy of an AI car in
        // the Supercar body, parked in its own box past the others, driven by its own RaceDriver
        // and outside RaceControl. Last in the field the AI see.
        SafetyCar _safety;
        CarController _safetyCar;
        SafetyCar.Phase _lanSafetyPhase;     // on a LAN client, the host's
        bool _lanSafetyCar;                  // a LAN race with the safety car switched on
        int _lanWear;                        // a LAN race's tyre wear choice
        bool[] _remotePits;                  // on a LAN host, the players whose machines say they are in the pits
        bool[] _stopped, _inPits;
        float[] _speed;
        static readonly Color SafetyCarColour = new Color(0.78f, 0.8f, 0.83f);

        /// <summary>The safety car, where the standings are kept and it is switched on; else null.</summary>
        public SafetyCar Safety => _keepsStandings ? _safety : null;

        /// <summary>Where the safety car is: the host's own, or on a LAN client the host's word.</summary>
        public SafetyCar.Phase SafetyCarPhase => _safety != null ? _safety.State : _lanSafetyPhase;
        public bool SafetyCarOut => SafetyCarPhase != SafetyCar.Phase.In;

        /// <summary>The safety car's car, which a LAN host sends with its own cars.</summary>
        public CarController SafetyCarCar => _safety != null ? _safetyCar : null;

        /// <summary>On a LAN client: tells the host something about this machine's own car, which
        /// only it can judge (Net.ReportKind and a value). Set by LanRace.</summary>
        [System.NonSerialized] public System.Action<byte, byte> ReportToHost;

        /// <summary>Seconds since GO, the clock the rulings are timed on.</summary>
        public float RaceTime => _raceTime;

        /// <summary>
        /// Set up as a LAN race, before Start: the AI this machine drives (none on a client),
        /// the cars moved from the network, whom the AI see and avoid like the player, the
        /// laps, the seconds to GO (NaN while the host waits for everyone to load), every
        /// car and name in grid order, and whether this machine keeps the standings (the
        /// host) or is sent them (a client).
        /// </summary>
        public void UseLan(CarController[] ai, System.Collections.Generic.List<RemoteCar> remotes, int laps,
                           System.Func<float> secondsToGo, CarController[] grid, string[] names, bool keepsStandings,
                           int tyreWear = 0, bool safetyCar = false, CarController safetyCarCar = null)
        {
            _lanWear = tyreWear;
            _lanSafetyCar = safetyCar;
            // The host drives the safety car: a copy LanRace made before turning any car remote.
            if (keepsStandings && safetyCar && safetyCarCar != null)
            {
                _safetyCar = safetyCarCar;
                _safetyCar.name = "Safety Car";
                DressAsSafetyCar(_safetyCar, () => SafetyCarOut);
            }
            else if (safetyCarCar != null) Destroy(safetyCarCar.gameObject);
            aiCars = ai;
            _remotes = remotes;
            raceLaps = Mathf.Max(1, laps);
            _secondsToGo = secondsToGo;
            _grid = grid;
            _gridNames = names;
            _keepsStandings = keepsStandings;
        }

        /// <summary>A player has quit: their entry stays, marked, where they got to.</summary>
        public void MarkLeft(int entry)
        {
            if (_left != null && entry >= 0 && entry < _left.Length) _left[entry] = true;
        }

        public bool HasLeft(int entry) => _left != null && entry >= 0 && entry < _left.Length && _left[entry];

        void Start()
        {
            // No GUILayout here: skip the layout pass Unity would otherwise run before each event.
            useGUILayout = false;
            if (track == null || player == null || track.line.Length < 3 || aiCars.Length == 0 && !Lan)
            {
                enabled = false;
                return;
            }

            // A solo race runs to the lobby's settings; a LAN race to the host's (UseLan).
            if (!Lan) raceLaps = RaceSettings.Laps;

            // The safety car's car, copied before anything is added to the AI car it copies.
            if (!Lan && RaceSettings.SafetyCar) MakeSafetyCar();
            if (_safetyCar != null) CarAudio.Attach(_safetyCar, player: false);
            CarAudio.Attach(player, player: true);
            foreach (CarController car in aiCars) CarAudio.Attach(car, player: false);
            if (_remotes != null)
                foreach (RemoteCar remote in _remotes) CarAudio.Attach(remote.GetComponent<CarController>(), player: false);

            _track = track.ToTrackData();
            _drivers = new RaceDriver[aiCars.Length];
            _lastInputs = new VehicleInputs[aiCars.Length + 1];
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
                aiCars[i].Autopilot = (body, dt) =>
                {
                    if (!_started) return new VehicleInputs { Brake = 1f };
                    _drivers[k].Path.SurfaceGrip = Grip(aiCars[k]);
                    _drivers[k].Path.TyreGrip = aiCars[k].Sim.TyreGrip;
                    return Logged(k, _drivers[k].Drive(body, dt));
                };
                aiCars[i].RecoveryPose = () => OnLine(k);
            }

            // Built the way RaceDriver builds its own: every limit scaled by the pace.
            CarConfig playerConfig = player.Sim.Config;
            SpeedPlan.Limits limits = PlanningLimits(playerConfig);
            limits.LateralMs2 *= playerPace;
            limits.BrakingMs2 *= playerPace;
            limits.TractionMs2 *= playerPace;
            _playerPlan = SpeedPlan.Build(_track, limits);

            _player = new LapProgress(track, player.transform.position);

            // Held on the brakes, like the AI, until GO.
            player.Autopilot = (body, dt) => new VehicleInputs { Brake = 1f };

            if (AiDrivesPlayer)
            {
                float pace = aiPace.Length > 0 ? aiPace[0] : 0.85f;
                _playerDriver = new RaceDriver("You", _track, playerConfig, PlanningLimits(playerConfig), pace);
                Vec3 at = ToNumerics(player.transform.position);
                int index = NearestOnLine(at);
                _playerDriver.Path.StartAt(index, index > n / 2 ? -1 : 0);
                _playerDriver.Path.LineOffsetM = _track.LateralOffset(_track.Line, index, at);
            }

            _cars = new CarController[aiCars.Length + 1];
            _aiEntry = new int[aiCars.Length];
            string[] names;
            if (Lan)
            {
                // The host's grid order, so every machine's entries line up.
                names = (string[])_gridNames.Clone();
                for (int slot = 0; slot < _grid.Length; slot++)
                {
                    CarController car = _grid[slot];
                    if (car == null) continue;
                    if (car == player) { _playerEntry = slot; names[slot] = "You"; }
                    int k = System.Array.IndexOf(aiCars, car);
                    if (k >= 0) _aiEntry[k] = slot;
                    var remote = car.GetComponent<RemoteCar>();
                    if (remote != null) _remoteEntry[remote] = (slot, new LapProgress(track, remote.transform.position));
                }
            }
            else
            {
                names = new string[aiCars.Length + 1];
                for (int i = 0; i < aiCars.Length; i++) { _aiEntry[i] = i; names[i] = _drivers[i].Name; }
                _playerEntry = aiCars.Length;
                names[_playerEntry] = "You";
            }
            _control = new RaceControl(names, raceLaps);
            _flags = new RaceFlags(names.Length);
            _flagIndex = new int[names.Length];
            _fieldOf = new int[names.Length];
            _flagProgress = new float[names.Length];
            _trouble = new bool[names.Length];
            _racing = new bool[names.Length];
            _moving = new bool[names.Length];
            _stopped = new bool[names.Length];
            _inPits = new bool[names.Length];
            _speed = new float[names.Length];
            if (_safetyCar != null) SetUpSafetyCar(names.Length);
            MiniMap map = FindAnyObjectByType<MiniMap>();
            if (map != null) map.YellowAt = i => Flags != null && Flags.Yellow.Exists(z => RaceFlags.Inside(_track, z, i));
            _left = new bool[names.Length];

            for (int i = 0; i < aiCars.Length; i++) _cars[i] = aiCars[i];
            _cars[aiCars.Length] = player;

            // Tyre wear for every car this machine drives, from the lobby (RaceSettings) in a solo
            // race; a LAN race has none yet. With wear on, the AI stop for tyres at their own
            // wear, each in a box of its own.
            TyreWearRate = Lan ? RaceSettings.WearRates[Mathf.Clamp(_lanWear, 0, RaceSettings.WearRates.Length - 1)] : RaceSettings.TyreWearRate;
            _remotePits = new bool[names.Length];
            foreach (CarController car in _cars) car.Sim.TyreWearRate = TyreWearRate;
            for (int i = 0; i < aiCars.Length && TyreWearRate > 0f && _track.HasPitLane; i++)
            {
                int k = i, entry = _aiEntry[i];
                _drivers[i].Box = _track.PitBoxIndex[entry % TrackData.PitBoxes];
                _drivers[i].PitWear = 0.65f + 0.1f * i / Mathf.Max(1, aiCars.Length - 1);
                _drivers[i].TyresFitted = () =>
                {
                    aiCars[k].Sim.FitNewTyres();
                    _control.Entries[entry].PitStops++;
                    if (AiLogAsked) Debug.Log($"PIT t {Time.timeSinceLevelLoad:0.00} {aiCars[k].name} new tyres, lap {_control.Entries[entry].LapsComplete + 1}");
                };
            }

            _pitDriver = new RaceDriver("Pit", _track, playerConfig, PlanningLimits(playerConfig), 0.85f);
            if (_track.HasPitLane)
            {
                _pitDriver.Box = _track.PitBoxIndex[_playerEntry % TrackData.PitBoxes];
                _pitDriver.TyresFitted = () =>
                {
                    player.Sim.FitNewTyres();
                    if (_keepsStandings) _control.Entries[_playerEntry].PitStops++;
                    else ReportToHost?.Invoke(5, 0);   // ReportKind.TyresFitted
                };
                MiniMap pitMap = FindAnyObjectByType<MiniMap>();
                if (pitMap != null)
                    pitMap.SetPitLane(System.Array.ConvertAll(_track.LanePoints[TrackData.PitLaneIndex], p => new Vector3(p.X, p.Y, p.Z)),
                                      _track.PitFrom, _track.PitTo, (TrackData.PitFastLaneM + TrackData.PitWorkingLaneM) * 0.5f);
            }
            for (int i = 0; i < _cars.Length; i++)
            {
                int entry = i < aiCars.Length ? _aiEntry[i] : _playerEntry;
                _cars[i].gameObject.AddComponent<CarContacts>().Touched = () => _control.Entries[entry].Contacts++;

                // Track limits, judged on the machine that drives the car, ruled on by whoever
                // keeps the standings. Not before GO: the grid is no place for an offence.
                var monitor = _cars[i].gameObject.AddComponent<TrackLimitsMonitor>();
                monitor.Watch(track, _track);
                monitor.PenaliseRecovery = _cars[i] == player;
                monitor.Judged = kind =>
                {
                    if (!_started) return;
                    if (_keepsStandings) _control.Judge(entry, kind, _raceTime);
                    else ReportToHost?.Invoke(2, (byte)kind);   // ReportKind.Judged
                };
                monitor.Left = () =>
                {
                    if (!_started) return;
                    if (_keepsStandings) _control.LeftTrack(entry);
                    else ReportToHost?.Invoke(1, 0);            // ReportKind.LeftTrack
                };
            }
            gameObject.AddComponent<RaceHud>().Show(this, _playerEntry);
        }

        /// <summary>The same limits the harness plans with (LapRun.PlanningLimits), from the
        /// car's own closed-form numbers.</summary>
        internal static SpeedPlan.Limits PlanningLimits(CarConfig config) => new SpeedPlan.Limits
        {
            LateralMs2 = Analytic.SkidpadCeilingG(config) * 0.85f * CarRace.Vehicle.Physics.Gravity,
            BrakingMs2 = (27.78f * 27.78f) / (2f * Analytic.BrakingMetres(config)) * 0.85f,
            TractionMs2 = (100f / 3.6f) / Analytic.ZeroToHundredSeconds(config),
            PowerW = Analytic.PeakPowerWatts(config) * config.DrivetrainEfficiency,
            MassKg = config.Mass,
            TopSpeedMs = Analytic.TopSpeedKph(config) / 3.6f,
        };

        // With -aiLog on the command line, a row per car per reaction interval to ai.csv beside
        // the executable, in the harness race CSV's terms, so the AI in Unity can be compared
        // with the AI headless. How the grid pass that crashed a car at Monza's first chicane
        // was found. Nothing is written or allocated without the switch.
        internal static readonly bool AiLogAsked = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aiLog") >= 0;
        System.IO.StreamWriter _aiLog;
        VehicleInputs[] _lastInputs = new VehicleInputs[0];

        VehicleInputs Logged(int car, VehicleInputs inputs)
        {
            if (AiLogAsked) _lastInputs[car] = inputs;
            return inputs;
        }

        void LogAi(float time)
        {
            if (!AiLogAsked) return;
            if (_aiLog == null)
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", "ai.csv");
                _aiLog = new System.IO.StreamWriter(path);
                _aiLog.WriteLine("t,car,s,lap,x,y,z,fwd_kph,target_kph,planned_kph,cap_kph,throttle,brake,steer," +
                                 "lateral_m,offset_m,wanted_m,cross_m,heading_deg,recovering,sideslip_deg,yaw_rate,gear,pitch_deg,overtaking,blocked_by,gap_m");
            }
            var c = System.Globalization.CultureInfo.InvariantCulture;
            int count = aiCars.Length + (_playerDriver != null ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                RaceDriver d = i < aiCars.Length ? _drivers[i] : _playerDriver;
                CarController car = i < aiCars.Length ? aiCars[i] : player;
                var body = car.GetComponent<Rigidbody>();
                Transform t = car.transform;
                float fwd = Vector3.Dot(body.linearVelocity, t.forward);
                float right = Vector3.Dot(body.linearVelocity, t.right);
                float slip = body.linearVelocity.sqrMagnitude > 1f ? Mathf.Atan2(right, Mathf.Abs(fwd)) * Mathf.Rad2Deg : 0f;
                PathDriver p = d.Path;
                VehicleInputs u = _lastInputs[i];
                _aiLog.WriteLine(string.Join(",", new[]
                {
                    time.ToString("0.00", c), (i + 1).ToString(c), (p.Index * _track.SampleSpacingM).ToString("0", c), p.Laps.ToString(c),
                    t.position.x.ToString("0.0", c), t.position.y.ToString("0.00", c), t.position.z.ToString("0.0", c),
                    (fwd * 3.6f).ToString("0.0", c), (p.TargetSpeedMs * 3.6f).ToString("0.0", c), (p.PlannedSpeedMs * 3.6f).ToString("0.0", c),
                    (p.SpeedCapMs < 0f ? -1f : p.SpeedCapMs * 3.6f).ToString("0.0", c),
                    u.Throttle.ToString("0.00", c), u.Brake.ToString("0.00", c), u.Steer.ToString("0.000", c),
                    p.LateralFromLineM.ToString("0.00", c), p.LineOffsetM.ToString("0.00", c), d.WantedOffsetM.ToString("0.00", c),
                    p.LineErrorM.ToString("0.00", c), p.HeadingErrorDeg.ToString("0.0", c), p.Recovering.ToString("0.00", c),
                    slip.ToString("0.0", c), Vector3.Dot(body.angularVelocity, t.up).ToString("0.000", c),
                    car.Sim.Drivetrain.Gear.ToString(c), (-Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg).ToString("0.00", c),
                    (d.IsOvertaking ? 1 : 0).ToString(c), d.BlockedBy.ToString(c), d.BlockedGapM.ToString("0.0", c),
                }));
            }
        }

        void OnDestroy() => _aiLog?.Dispose();

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            _player.Advance(player.transform.position);
            if (_keepsStandings)
                foreach (var pair in _remoteEntry)
                    if (pair.Key != null) pair.Value.Progress.Advance(pair.Key.GetComponent<Rigidbody>().position);
            if (!_started)
            {
                if (Lan)
                {
                    float left = _secondsToGo();
                    _waiting = float.IsNaN(left);
                    if (_waiting) return;
                    _countdown = left;
                }

                // A beep as each number shows, a higher one for GO.
                int showing = Mathf.CeilToInt(_countdown);
                if (showing < _beeped && showing >= 1) { GameAudio.Countdown(go: false); _beeped = showing; }
                _countdown -= dt;
                if (_countdown > 0f) return;
                _started = true;
                GameAudio.Countdown(go: true);
                HandBack();
            }
            Pits();

            // Every step rather than every reaction interval, so lap times are to 5 ms.
            _raceTime += dt;
            if (_keepsStandings)
            {
                for (int i = 0; i < aiCars.Length; i++)
                    _control.Update(_aiEntry[i], _raceTime, _drivers[i].Path.Laps, _drivers[i].Path.ProgressM);
                _control.Update(_playerEntry, _raceTime, _player.Laps, _player.ProgressM(_track));
                foreach (var pair in _remoteEntry)
                    if (pair.Key != null)
                        _control.Update(pair.Value.Entry, _raceTime, pair.Value.Progress.Laps, pair.Value.Progress.ProgressM(_track));
                _control.Rank();
                _control.Tick(_raceTime);
            }

            _sinceReaction += dt;
            if (_sinceReaction < ReactionSeconds) return;
            float elapsed = _sinceReaction;
            _sinceReaction = 0f;
            LogAi(_raceTime);

            for (int i = 0; i < aiCars.Length; i++)
            {
                PathDriver path = _drivers[i].Path;
                _field[i] = Seen(aiCars[i], path.Index, path.LateralFromLineM, path.Plan, path.Lane);
            }
            int remotes = _remotes?.Count ?? 0;
            int size = aiCars.Length + 1 + remotes + (_safety != null ? 1 : 0);
            if (_field.Length != size)
            {
                var resized = new RaceDriver.Seen[size];
                System.Array.Copy(_field, resized, Mathf.Min(_field.Length, aiCars.Length));
                _field = resized;
            }
            Vec3 playerPosition = ToNumerics(player.transform.position);
            _field[aiCars.Length] = Seen(player, _player.Index,
                                         _track.LateralOffset(_track.Line, _player.Index, playerPosition),
                                         _playerPlan);
            _field[aiCars.Length].Pitting = _pitting;
            for (int i = 0; i < aiCars.Length; i++) _field[i].Pitting = _drivers[i].Path.Pitting;
            for (int i = 0; i < aiCars.Length; i++)
            {
                if (TyreWearRate <= 0f) break;
                Wheel[] w = aiCars[i].Sim.Wheels;
                _drivers[i].Wear = Mathf.Max(w[0].Wear + w[1].Wear, w[2].Wear + w[3].Wear) * 0.5f;
                _drivers[i].LapsLeft = raceLaps - _control.Entries[_aiEntry[i]].LapsComplete - 1;
            }
            // The other players' cars, seen as the player is: the AI assume the same pace.
            for (int r = 0; r < remotes; r++) _field[aiCars.Length + 1 + r] = Seen(_remotes[r]);
            if (_safety != null)
            {
                PathDriver path = _safety.Driver.Path;
                _field[size - 1] = Seen(_safetyCar, path.Index, path.LateralFromLineM, path.Plan, path.Lane);
                _field[size - 1].Pitting = path.Pitting;
            }
            if (_keepsStandings) UpdateFlags(remotes);
            if (_safety != null && _keepsStandings) UpdateSafetyCar(size - 1, elapsed);

            _playerDriver?.Observe(_track, _field, aiCars.Length, elapsed);
            if (_pitting) _pitDriver.Observe(_track, _field, aiCars.Length, elapsed);
            for (int i = 0; i < aiCars.Length; i++)
            {
                _drivers[i].Observe(_track, _field, i, elapsed);

                float speed = _field[i].SpeedMs;
                bool stopped = speed < 1f && speed > -1f;
                bool lost = Grip(aiCars[i]) < _drivers[i].Path.OffRoadGrip
                            || Mathf.Abs(_drivers[i].Path.HeadingErrorDeg) > WrongWayDegrees;
                bool stuck = (stopped || (lost && speed < CrawlingMs && speed > -CrawlingMs)) && !_drivers[i].InPits;
                _stuckFor[i] = stuck ? _stuckFor[i] + elapsed : 0f;
                if (_stuckFor[i] < (lost ? OffRoadStuckSeconds : StuckSeconds)) continue;
                if (AiLogAsked) Debug.Log($"RECOVER t {Time.timeSinceLevelLoad:0.00} {aiCars[i].name} {(lost ? "off road or wrong way" : "stopped")}");
                aiCars[i].Recover();
                _stuckFor[i] = 0f;
            }
        }

        /// <summary>
        /// Where a car the AI do not drive is on the lap, as distance covered in samples: the
        /// player's, and on a LAN host every other player's. Each step adds the shortest way
        /// round from the last sample, so crossing the line counts a lap and a recovery or
        /// restart, which jumps, is searched for over the whole lap first. Negative on the grid.
        /// </summary>
        sealed class LapProgress
        {
            readonly TrackPath _path;
            readonly int _n;
            Vector3 _last;
            float _samples;

            public int Index { get; private set; }

            public LapProgress(TrackPath path, Vector3 start)
            {
                _path = path;
                _n = path.centre.Length;
                _last = start;
                Index = path.Nearest(start, 0, back: 0, ahead: _n - 1);
                _samples = Index > _n / 2 ? Index - _n : Index;
            }

            public void Advance(Vector3 position)
            {
                int index = (position - _last).sqrMagnitude > 25f * 25f
                    ? _path.Nearest(position, 0, back: 0, ahead: _n - 1)
                    : _path.Nearest(position, Index);
                _last = position;

                int step = ((index - Index) % _n + _n) % _n;
                if (step > _n / 2) step -= _n;
                _samples += step;
                Index = index;
            }

            public int Laps => Mathf.FloorToInt(_samples / _n);

            /// <summary>Measured the way PathDriver.ProgressM measures the AI, so the two rank together.</summary>
            public float ProgressM(TrackData track) => Laps * track.LengthM + (_samples - Laps * _n) * track.SampleSpacingM;
        }

        RaceDriver.Seen Seen(CarController car, int index, float lateral, System.Collections.Generic.IReadOnlyList<float> plan, int lane = 0)
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
                Lane = lane,
            };
        }

        RaceDriver.Seen Seen(RemoteCar remote)
        {
            Transform t = remote.transform;
            int n = _track.Count;
            int index = _remoteIndex.TryGetValue(remote, out int hint) && (t.position - track.centre[hint]).sqrMagnitude < 40f * 40f
                ? track.Nearest(t.position, hint)
                : track.Nearest(t.position, 0, back: 0, ahead: n - 1);
            _remoteIndex[remote] = index;
            Vec3 position = ToNumerics(t.position);
            return new RaceDriver.Seen
            {
                Index = index,
                LateralM = _track.LateralOffset(_track.Line, index, position),
                SpeedMs = Vector3.Dot(remote.Velocity, t.forward),
                Plan = _playerPlan,
                Position = position,
                // Not heard from yet: wherever it was put, it is not on the road. A safety car
                // copy never moved by the network was a car stood still under the circuit, and
                // the field queued behind it.
                Gone = !remote.Heard,
                // In the pit lane, by where it is: a player's stop, or the safety car in its box.
                Pitting = _track.InPitLaneAt(index, position),
            };
        }

        /// <summary>
        /// The marshals' look at the field, every reaction interval, as RaceRun does headless: each
        /// car in trouble (stopped, or pointing back along the lap, once it has got going) raises a
        /// yellow; a pass under one is penalised; and each AI driver is told the flags it is under,
        /// by its place in the field, which is how the drivers know each other.
        /// </summary>
        void UpdateFlags(int remotes)
        {
            for (int e = 0; e < _fieldOf.Length; e++) { _fieldOf[e] = -1; _racing[e] = false; _trouble[e] = false; }
            for (int k = 0; k < aiCars.Length; k++)
                Mark(_aiEntry[k], k, aiCars[k].transform, aiCars[k].GetComponent<Rigidbody>().linearVelocity,
                     _drivers[k].Path.Index, _drivers[k].Path.ProgressM, _drivers[k].InPits);
            Mark(_playerEntry, aiCars.Length, player.transform, player.GetComponent<Rigidbody>().linearVelocity,
                 _player.Index, _player.ProgressM(_track), _pitting);
            for (int r = 0; r < remotes; r++)
                if (_remotes[r] != null && _remoteEntry.TryGetValue(_remotes[r], out var remote))
                    Mark(remote.Entry, aiCars.Length + 1 + r, _remotes[r].transform, _remotes[r].Velocity,
                         _field[aiCars.Length + 1 + r].Index, remote.Progress.ProgressM(_track), _remotePits[remote.Entry]);

            foreach (var (car, passed) in _flags.Update(_track, _flagIndex, _flagProgress, _trouble, _racing, _raceTime))
                _control.YellowPass(car, passed, _raceTime);

            for (int k = 0; k < aiCars.Length; k++) Tell(_drivers[k], _aiEntry[k]);
            if (_playerDriver != null) Tell(_playerDriver, _playerEntry);
            for (int e = 0; e < _fieldOf.Length; e++)
            {
                if (_fieldOf[e] < 0) continue;
                _field[_fieldOf[e]].Yielding = BlueFor(e) >= 0;
                _field[_fieldOf[e]].YieldingTo = FieldOf(BlueFor(e));
            }
        }

        void Mark(int entry, int field, Transform car, Vector3 velocity, int index, float progressM, bool inPits)
        {
            float forward = Vector3.Dot(velocity, car.forward);
            if (forward > 10f) _moving[entry] = true;
            Vec3 along = _track.Tangent(_track.Line, index);
            bool backwards = Vector3.Dot(car.forward, new Vector3(along.X, 0f, along.Z)) < 0f;
            RaceControl.Entry e = _control.Entries[entry];
            _fieldOf[entry] = field;
            _flagIndex[entry] = index;
            _flagProgress[entry] = progressM;
            _racing[entry] = !e.Finished && !e.Disqualified && !HasLeft(entry);
            _trouble[entry] = _moving[entry] && (Mathf.Abs(forward) < 3f || backwards) && !inPits;
            _stopped[entry] = _moving[entry] && Mathf.Abs(forward) < 3f && !inPits;
            _inPits[entry] = inPits;
            _speed[entry] = forward;
        }

        void Tell(RaceDriver driver, int entry)
        {
            driver.UnderYellow = _flags.InYellow[entry] >= 0;
            driver.YellowFor = FieldOf(_flags.InYellow[entry]);
            driver.YieldTo = FieldOf(BlueFor(entry));
        }

        int FieldOf(int entry) => entry >= 0 ? _fieldOf[entry] : -1;

        /// <summary>The blue flag for an entry, none under the safety car: nobody is let by in the queue.</summary>
        int BlueFor(int entry) => _safety != null && _safety.Out ? -1 : _flags.BlueFor[entry];

        /// <summary>
        /// The safety car's car: a copy of the first AI car in the Supercar body, painted silver,
        /// with a light bar on the roof. Laid out and given its driver once the track is known
        /// (SetUpSafetyCar).
        /// </summary>
        void MakeSafetyCar()
        {
            if (aiCars.Length == 0) return;
            _safetyCar = Instantiate(aiCars[0].gameObject).GetComponent<CarController>();
            _safetyCar.name = "Safety Car";
            DressAsSafetyCar(_safetyCar, () => SafetyCarOut);
        }

        /// <summary>The safety car's look, on the host's car or a client's copy of it: the
        /// Supercar body in silver and a light bar whose lamps flash while <paramref name="flashing"/>.</summary>
        public static void DressAsSafetyCar(CarController car, System.Func<bool> flashing)
        {
            for (int d = 0; d < CarDesigns.Count; d++)
                if (CarDesigns.NameOf(d) == "Supercar") CarDesigns.Apply(car.transform, d);
            PlayerSetup.Paint(car.transform.Find("Body"), SafetyCarColour);
            Renderer[] lamps = LightBar(car.transform);
            if (lamps != null) car.gameObject.AddComponent<SafetyCarLamps>().Set(lamps, flashing);
        }

        /// <summary>A bar across the roof with an amber lamp at each end, flashed while it is out.
        /// No colliders: a collider on a child would join the car's rigid body.</summary>
        static Renderer[] LightBar(Transform car)
        {
            Renderer body = car.Find("Body")?.GetComponent<Renderer>();
            if (body == null) return null;
            Bounds b = body.bounds;
            Vector3 roof = car.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            var bar = new GameObject("Light Bar").transform;
            bar.SetParent(car, false);
            bar.localPosition = new Vector3(0f, roof.y + 0.04f, roof.z - 0.1f);

            // The primitives' own lit material, coloured per renderer: Shader.Find for the unlit one
            // came back stripped from the build, and drew the lamps magenta.
            GameObject Part(string name, Vector3 at, Vector3 size, Color colour)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(part.GetComponent<Collider>());
                part.name = name;
                part.transform.SetParent(bar, false);
                part.transform.localPosition = at;
                part.transform.localScale = size;
                PlayerSetup.Paint(part.transform, colour);
                return part;
            }
            Part("Base", new Vector3(0f, 0.03f, 0f), new Vector3(1.1f, 0.06f, 0.22f), new Color(0.08f, 0.08f, 0.09f));
            return new[]
            {
                Part("Lamp Left", new Vector3(-0.3f, 0.1f, 0f), new Vector3(0.42f, 0.09f, 0.18f), SafetyCarLamps.Off).GetComponent<Renderer>(),
                Part("Lamp Right", new Vector3(0.3f, 0.1f, 0f), new Vector3(0.42f, 0.09f, 0.18f), SafetyCarLamps.Off).GetComponent<Renderer>(),
            };
        }

        /// <summary>The safety car's box, as a pose for a car of that ride height: in the working
        /// lane past the others, pointing along the pit lane, a little above it to settle.</summary>
        public static (Vector3 position, Quaternion rotation) SafetyCarBox(TrackData track, float cgHeight)
        {
            int box = track.SafetyCarBox;
            Vec3 along = track.Tangent(track.LanePoints[TrackData.PitLaneIndex], box);
            Vec3 spot = track.LanePoints[TrackData.PitLaneIndex][box] + TrackData.Right(along) * track.PitBoxShiftM;
            return (new Vector3(spot.X, spot.Y + cgHeight + 0.3f, spot.Z), Quaternion.LookRotation(new Vector3(along.X, 0f, along.Z), Vector3.up));
        }

        /// <summary>The safety car's driver, its box, and its place in the pits before the start.</summary>
        void SetUpSafetyCar(int entries)
        {
            if (!_track.HasPitLane) { Destroy(_safetyCar.gameObject); _safetyCar = null; return; }
            CarConfig config = _safetyCar.Sim.Config;
            var driver = new RaceDriver("Safety car", _track, config, PlanningLimits(config), 0.85f);
            _safety = new SafetyCar(driver, entries) { ForceAt = RaceSettings.SafetyCarAt };
            _safety.Park(_track);
            var (position, rotation) = SafetyCarBox(_track, config.CgHeight);
            _safetyCar.PlaceOnGrid(position, rotation);
            _safetyCar.Sim.TyreWearRate = 0f;
            _safetyCar.Autopilot = (body, dt) =>
            {
                if (!_started) return new VehicleInputs { Brake = 1f };
                driver.Path.SurfaceGrip = Grip(_safetyCar);
                driver.Path.TyreGrip = _safetyCar.Sim.TyreGrip;
                return driver.Drive(body, dt);
            };
        }

        /// <summary>
        /// The safety car's look at the field, after the flags: whether it comes out, and when it
        /// goes in; passes under it penalised at the green; every AI told whether it is out and
        /// how fast to go; and its own driver shown the field, from <paramref name="slot"/>.
        /// </summary>
        void UpdateSafetyCar(int slot, float elapsed)
        {
            int leaderLaps = 0;
            foreach (RaceControl.Entry e in _control.Entries) leaderLaps = Mathf.Max(leaderLaps, e.LapsComplete);
            SafetyCar.Phase was = _safety.State;
            foreach (var (car, passed) in _safety.Update(_track, _flags, _flagIndex, _flagProgress, _speed, _trouble,
                                                         _stopped, _racing, _inPits, leaderLaps, raceLaps, _raceTime, elapsed))
                _control.SafetyCarPass(car, passed, _raceTime);
            if (_safety.State != was) Debug.Log($"SAFETY CAR t {_raceTime:0.0} {_safety.State}, leader on lap {leaderLaps + 1}");
            foreach (RaceDriver d in _drivers) { d.UnderSafetyCar = _safety.Out; d.SafetyCarShare = _safety.Share; }
            if (_playerDriver != null) { _playerDriver.UnderSafetyCar = _safety.Out; _playerDriver.SafetyCarShare = _safety.Share; }
            _safety.Driver.UnderSafetyCar = _safety.Out;
            _safety.Driver.Observe(_track, _field, slot, elapsed);
        }

        // ---------------------------------------------------------------- LAN: flags and reports

        /// <summary>
        /// On a LAN host: what another player's machine says about its own car, by its grid slot
        /// (<paramref name="entry"/>). Left the track and an offence go to race control as the
        /// host's own car's do; into and out of the pits keeps its stops from raising a yellow
        /// or the safety car; new tyres count a stop. Kinds as Net.ReportKind.
        /// </summary>
        public void OnRemoteReport(int entry, byte kind, byte value)
        {
            if (!_keepsStandings || _control == null || entry < 0 || entry >= _control.Entries.Length) return;
            int before = _control.Events.Count;
            switch (kind)
            {
                case 1: if (_started) _control.LeftTrack(entry); break;
                case 2: if (_started) _control.Judge(entry, (TrackLimits.Kind)value, _raceTime); break;
                case 3: _remotePits[entry] = true; break;
                case 4: _remotePits[entry] = false; break;
                case 5: _control.Entries[entry].PitStops++; break;
            }
            Debug.Log($"LAN: report from slot {entry}: kind {kind} value {value}"
                      + (_control.Events.Count > before ? $", ruled {_control.Events[before].Ruling} for {_control.Events[before].Cause}" : ""));
        }

        /// <summary>On a LAN host: the flags as every player's screen needs them, by grid slot.</summary>
        public CarRace.Net.FlagsState FlagsForLan()
        {
            int n = _control.Entries.Length;
            var flags = new CarRace.Net.FlagsState
            {
                SafetyCar = (byte)SafetyCarPhase,
                Zones = _flags.Yellow.ConvertAll(z => new CarRace.Net.FlagZone { Car = (byte)z.Car, From = z.From, To = z.To }).ToArray(),
                InYellow = new sbyte[n], BlueFor = new sbyte[n],
                Owed = _control.Owing.ConvertAll(o => new CarRace.Net.FlagOwed { Car = (byte)o.Car, Passed = (byte)o.Passed, DeadlineS = o.DeadlineS }).ToArray(),
            };
            for (int e = 0; e < n; e++)
            {
                flags.InYellow[e] = (sbyte)_flags.InYellow[e];
                flags.BlueFor[e] = (sbyte)BlueFor(e);
            }
            return flags;
        }

        /// <summary>On a LAN client: the host's flags, copied in, so the HUD, the minimap and the
        /// give-back countdown show them as the host's own would.</summary>
        public void ApplyFlags(CarRace.Net.FlagsState flags)
        {
            if (_flags == null || _control == null) return;
            _lanSafetyPhase = (SafetyCar.Phase)flags.SafetyCar;
            _flags.Yellow.Clear();
            foreach (var z in flags.Zones)
                _flags.Yellow.Add(new RaceFlags.Zone { Car = z.Car, From = z.From, To = z.To, Until = float.PositiveInfinity });
            for (int e = 0; e < _flags.InYellow.Length && e < flags.InYellow.Length; e++)
            {
                _flags.InYellow[e] = flags.InYellow[e];
                _flags.BlueFor[e] = flags.BlueFor[e];
            }
            _control.Owing.Clear();
            foreach (var o in flags.Owed)
                _control.Owing.Add(new RaceControl.Owed { Car = o.Car, Passed = o.Passed, DeadlineS = o.DeadlineS });
            // An AI driving this player's car (-lanAutopilot) keeps behind the safety car too.
            if (_playerDriver != null)
            {
                _playerDriver.UnderSafetyCar = SafetyCarOut;
                _playerDriver.SafetyCarShare = SafetyCarPhase == SafetyCar.Phase.Ending ? 1f : SafetyCar.FieldShare;
            }
        }

        /// <summary>
        /// Your stop, every physics step. Past the pit entry line and out beyond the pit wall, the
        /// pit driver takes the car: along the lane at the limit, into your box, four tyres, and
        /// out; at the exit line it hands the car back to you, or to the AI driving it under
        /// -benchmark. Missing the pit lane is no stop: you stay on the track.
        /// </summary>
        void Pits()
        {
            if (!_track.HasPitLane || _pitDriver == null || _player.Index < 0) return;
            int i = _player.Index;
            int into = _track.IntoPit(i), entry = _track.IntoPit(_track.PitEntryLine);
            if (!_pitting)
            {
                if (into < entry || into > entry + 3) return;
                float fromCentre = _track.LateralOffset(_track.Centre, i, ToNumerics(player.transform.position));
                if (fromCentre < _track.WidthRight[i] + TrackData.PitWallGapM) return;
                _pitDriver.EnterPitLane(i, _player.Laps);
                if (!_keepsStandings) ReportToHost?.Invoke(3, 0);   // ReportKind.PitIn
                _pitting = true;
                player.Autopilot = (body, t) =>
                {
                    _pitDriver.Path.TyreGrip = player.Sim.TyreGrip;
                    return _pitDriver.Drive(body, t);
                };
                return;
            }
            bool out_ = _pitDriver.Pit == RaceDriver.PitState.Racing
                     || _pitDriver.Pit == RaceDriver.PitState.Leaving && into >= _track.IntoPit(_track.PitExitLine);
            if (!out_) return;
            _pitDriver.EndPit();
            if (!_keepsStandings) ReportToHost?.Invoke(4, 0);       // ReportKind.PitOut
            _pitting = false;
            HandBack();
        }

        /// <summary>The car back to its driver: you, or under -benchmark the AI that drives it.</summary>
        void HandBack()
        {
            player.Autopilot = _playerDriver != null ? (body, t) =>
            {
                _playerDriver.Path.TyreGrip = player.Sim.TyreGrip;
                return Logged(aiCars.Length, _playerDriver.Drive(body, t));
            } : null;
        }

        /// <summary>The grip under a car, its wheels' average: 1 on asphalt, 0.45 on grass.</summary>
        static float Grip(CarController car)
        {
            float sum = 0f;
            foreach (var wheel in car.Sim.Wheels) sum += wheel.SurfaceFriction;
            return sum / car.Sim.Wheels.Length;
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
            // Not while paused: the settings menu outlives the scene, and so would its pause.
            if (_control != null && RaceOver && Time.timeScale > 0f && Input.GetKeyDown(KeyCode.Return))
            {
                if (!Lan)
                {
                    GameAudio.Confirm();
                    SettingsMenu.RestartRace();
                }
                else if (LanSession.Active && LanSession.Current.Host != null)
                {
                    GameAudio.Confirm();
                    LanSession.Current.ReturnToLobby();
                }
            }
        }

        RaceControl.Entry PlayerEntry => _control.Entries[_playerEntry];

        /// <summary>The player has taken the flag, or been shown the black one.</summary>
        bool RaceOver => PlayerEntry.Finished || PlayerEntry.Disqualified;

        void OnGUI()
        {
            if (!enabled || _control == null || Hud.Hidden) return;
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _bigStyle ??= new GUIStyle(_style);
            _tableStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = Font.CreateDynamicFontFromOSFont("Consolas", 18),
                normal = { textColor = Color.white }
            };

            _style.fontSize = Hud.Font(30);
            _bigStyle.fontSize = Hud.Font(120);
            _tableStyle.fontSize = Hud.Font(18);

            RaceControl.Entry me = PlayerEntry;
            int lap = Mathf.Clamp(me.LapsComplete + 1, 1, raceLaps);
            var box = new Rect(Screen.width * 0.5f - Hud.Px(160f), Hud.Px(10f), Hud.Px(320f), Hud.Px(56f));
            GUI.Box(box, GUIContent.none);
            GUI.Label(box, $"P{me.Position} / {_control.Entries.Length}    Lap {lap} / {raceLaps}", _style);
            _hintStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };
            _hintStyle.fontSize = Hud.Font(15);
            GUI.Label(new Rect(box.x, box.yMax + Hud.Px(2f), box.width, Hud.Px(22f)),
                      Lan ? "Esc: menu (the race goes on)" : "Esc: pause, restart or main menu", _hintStyle);

            if (_waiting)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, Hud.Px(60f)), "WAITING FOR EVERYONE TO LOAD", _style);
            }
            else if (!_started)
            {
                _bigStyle.normal.textColor = new Color(1f, 0.2f, 0.15f);
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, Hud.Px(160f)),
                          Mathf.CeilToInt(_countdown).ToString(), _bigStyle);
            }
            else if (_raceTime < GoShownSeconds)
            {
                _bigStyle.normal.textColor = new Color(0.2f, 1f, 0.3f);
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, Hud.Px(160f)), "GO", _bigStyle);
            }

            if (me.Finished || me.Disqualified) Results();
        }

        /// <summary>The classification, as the harness prints it: finishers in the order they
        /// took the flag, then everyone still running by distance.</summary>
        void Results()
        {
            RaceControl.Entry[] order = _control.Classification();
            float winner = order[0].Finished ? order[0].ResultS : 0f;
            float fastest = float.MaxValue;
            foreach (RaceControl.Entry e in order) fastest = Mathf.Min(fastest, e.BestLapS);

            // In a LAN race people's names are longer than "AI 1", and touches are counted
            // only for the cars this machine drives, so that column is left out.
            var text = new System.Text.StringBuilder();
            int nameWidth = Lan ? 12 : 8;
            bool pits = TyreWearRate > 0f;
            text.AppendLine($"{"Pos",-4}{"Driver".PadRight(nameWidth)}{"Grid",5}{"+/-",5}{"Best lap",11}{"Race time",11}{"Gap",10}{"Pen",6}{(pits ? $"{"Pits",5}" : "")}{(Lan ? "" : $"{"Hits",6}")}");
            text.AppendLine(new string('-', (Lan ? 64 : 66) + (pits ? 5 : 0)));
            foreach (RaceControl.Entry e in order)
            {
                int gained = e.Grid - e.Position;
                bool left = HasLeft(System.Array.IndexOf(_control.Entries, e));
                string name = e.Name.Length > nameWidth - 1 ? e.Name.Substring(0, nameWidth - 1) : e.Name;
                string best = e.BestLapS < float.MaxValue ? Format(e.BestLapS) + (e.BestLapS == fastest ? "*" : " ") : "-";
                // Race time with penalties in it, as the order is; the Pen column says how much.
                string time = e.Disqualified ? "DSQ" : e.Finished ? Format(e.ResultS) : left ? "left" : $"lap {Mathf.Clamp(e.LapsComplete + 1, 1, raceLaps)}";
                string gap = e.Disqualified ? "-" : !e.Finished ? (left ? "-" : "running") : e.Position == 1 ? "-" : $"+{e.ResultS - winner:0.000}";
                string pen = e.PenaltyS > 0f ? $"+{e.PenaltyS:0}s" : "-";
                string position = e.Disqualified ? "DSQ" : e.Position.ToString();
                text.AppendLine($"{position,-4}{name.PadRight(nameWidth)}{e.Grid,5}{(gained == 0 || e.Disqualified ? "0" : gained.ToString("+0;-0")),5}{best,11}{time,11}{gap,10}{pen,6}{(pits ? $"{e.PitStops,5}" : "")}{(Lan ? "" : $"{e.Contacts,6}")}");
            }
            text.AppendLine();
            text.Append("* fastest lap. Penalties are in the race time.");

            float buttons = Hud.Px(64f);
            float width = Hud.Px(750f), height = Hud.Px(30f + 26f * (order.Length + 5)) + buttons;
            var panel = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.5f - height * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUI.Box(panel, GUIContent.none);   // twice: one box is too faint to read a table over
            GUI.Label(new Rect(panel.x, panel.y + Hud.Px(6f), width, Hud.Px(34f)), "RESULTS", _style);
            GUI.Label(new Rect(panel.x + Hud.Px(20f), panel.y + Hud.Px(46f), width - Hud.Px(40f), height - Hud.Px(50f) - buttons),
                      text.ToString(), _tableStyle);

            // Not while paused: the pause menu is over the table then.
            if (Time.timeScale <= 0f) return;
            _buttonStyle ??= new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = Hud.Font(18);
            float half = (width - Hud.Px(52f)) * 0.5f, top = panel.yMax - buttons + Hud.Px(4f);
            _hover.Begin();
            var again = new Rect(panel.x + Hud.Px(20f), top, half, Hud.Px(46f));
            var menu = new Rect(panel.x + Hud.Px(32f) + half, top, half, Hud.Px(46f));
            // In a LAN race the host takes everyone back to the lobby together to pick the next
            // race; the others wait for that. Main menu leaves the group.
            bool lanHost = Lan && LanSession.Active && LanSession.Current.Host != null;
            if (!Lan || lanHost) _hover.Watch(again);
            if (SettingsMenu.InRace) _hover.Watch(menu);
            if (!Lan && GUI.Button(again, "Race again (Enter)", _buttonStyle))
            {
                GameAudio.Confirm();
                SettingsMenu.RestartRace();
            }
            if (lanHost && GUI.Button(again, "Race again, together (Enter)", _buttonStyle))
            {
                GameAudio.Confirm();
                LanSession.Current.ReturnToLobby();
            }
            if (Lan && !lanHost)
                GUI.Label(again, "The host picks the next race", _hintStyle);
            if (SettingsMenu.InRace && GUI.Button(menu, Lan ? "Main menu (leave the group)" : "Main menu", _buttonStyle))
            {
                GameAudio.Back();
                SettingsMenu.MainMenu();
            }
        }

        static string Format(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
