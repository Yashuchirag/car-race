using System.Collections.Generic;
using CarRace.Net;
using CarRace.Track;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Turns a circuit built for one player and three AI into the LAN race the host started.
    /// Set up as the scene loads, before anything in it has started:
    ///
    ///   one car per grid slot, in the host's order: the scene's own cars first, copies of
    ///   an AI car for the rest, and any left over removed;
    ///   this player's car on its slot, driven as ever;
    ///   on the host, its AI cars driven by RaceDirector as ever;
    ///   every other car a RemoteCar, moved from the network;
    ///   colours made unique the same way on every machine, so everyone sees the same field.
    ///
    /// Then it tells the host it is ready, holds the start until the host's GO, and sends
    /// this machine's cars 30 times a second: its own, and on the host the AI too.
    ///
    /// The host's RaceDirector keeps the standings for every car, and the host sends them
    /// four times a second; a client's RaceDirector shows the host's numbers, so positions,
    /// the finish and the results are the same on every screen.
    ///
    /// For testing: -lanAutopilot has the AI drive this machine's car, -lanScreenshotAt
    /// 10,25 takes screenshots that many seconds into the race scene, beside the executable,
    /// then quits, and -lanReturnAt 20 has a host take everyone back to the lobby then.
    /// </summary>
    public sealed class LanRace : MonoBehaviour
    {
        const float SendHz = 30f;
        const float StandingsHz = 4f;
        const float RowGapM = 10f, GridLateralM = 2.5f;   // as TrackSceneBuilder lays the grid

        LanSession _session;
        CarController _mine;
        readonly List<(byte Id, CarController Car)> _aiCars = new List<(byte, CarController)>();
        readonly List<RemoteCar> _remotes = new List<RemoteCar>();
        readonly Dictionary<byte, int> _slotOf = new Dictionary<byte, int>();
        RaceDirector _director;
        Standings _applied;
        float _nextSend, _nextStandings;
        bool _hostGone;

        public static void SetUp(LanSession session)
        {
            RaceDirector director = FindAnyObjectByType<RaceDirector>();
            if (director == null) return;
            var race = new GameObject("LAN Race").AddComponent<LanRace>();
            race.Build(session, director);
        }

        void Build(LanSession session, RaceDirector director)
        {
            _session = session;
            _director = director;
            RaceStart start = session.Race;
            var grid = new CarController[start.Grid.Length];
            var names = new string[start.Grid.Length];
            TrackPath track = FindAnyObjectByType<TrackPath>();
            _mine = director.Player;

            // The scene's cars, the player's first, then copies of an AI car to fill the grid,
            // made before any car is turned into a RemoteCar, so a copy is a plain car.
            var spare = new List<CarController>(director.AiCars);
            for (int k = spare.Count; k < start.Grid.Length - 1 && director.AiCars.Length > 0; k++)
            {
                CarController copy = Instantiate(director.AiCars[0].gameObject).GetComponent<CarController>();
                copy.name = $"Car {k + 2}";
                spare.Add(copy);
            }
            byte[] colours = Colours(start.Grid);
            Extrapolator source = session.Host != null ? session.Host.Cars : session.Client.Cars;
            System.Func<float, float> hostTimeOf = session.Host != null
                ? (System.Func<float, float>)(t => t)
                : t => session.Client.HostNow(t);
            var ai = new List<CarController>();
            var all = new List<Transform> { _mine.transform };

            for (int slot = 0; slot < start.Grid.Length; slot++)
            {
                PlayerInfo entry = start.Grid[slot];
                names[slot] = entry.Name;
                _slotOf[entry.Id] = slot;
                CarController car;
                if (entry.Id == session.MyId && !entry.Ai) car = _mine;
                else if (spare.Count > 0) { car = spare[0]; spare.RemoveAt(0); }
                else continue;   // a scene with no AI car to copy: that car is not shown

                var (position, rotation) = GridSlot(track, slot, car.Sim.Config.CgHeight);
                car.PlaceOnGrid(position, rotation);
                int design = entry.Ai ? (slot + 1) % Mathf.Max(CarDesigns.Count, 1) : entry.Design;
                CarDesigns.Apply(car.transform, design);
                PlayerSetup.Paint(car.transform.Find("Body"), PlayerSetup.Colours[colours[slot]].colour);
                grid[slot] = car;

                if (car == _mine) continue;
                all.Add(car.transform);
                if (entry.Ai && session.Host != null)
                {
                    ai.Add(car);
                    _aiCars.Add((entry.Id, car));
                }
                else _remotes.Add(RemoteCar.Make(car, entry.Id, source, hostTimeOf));
            }
            foreach (CarController unused in spare) Destroy(unused.gameObject);

            director.UseLan(ai.ToArray(), _remotes, start.Laps, SecondsToGo, grid, names, keepsStandings: session.Host != null);
            director.AiDrivesPlayer = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lanAutopilot") >= 0;
            string shots = LanSession.Flag("-lanScreenshotAt");
            if (!string.IsNullOrEmpty(shots)) StartCoroutine(Screenshots(shots, session.MyId));
            if (session.Host != null && float.TryParse(LanSession.Flag("-lanReturnAt"), System.Globalization.NumberStyles.Float,
                                                       System.Globalization.CultureInfo.InvariantCulture, out float returnAt))
                StartCoroutine(ReturnAfter(returnAt));
            FindAnyObjectByType<MiniMap>()?.SetCars(all.ToArray());

            if (session.Host != null) session.Host.SetReady();
            else session.Client.SendReady();
            Debug.Log($"LAN: on the grid in slot {System.Array.FindIndex(start.Grid, p => p.Id == session.MyId && !p.Ai) + 1} " +
                      $"of {start.Grid.Length}, {_aiCars.Count} AI driven here, {_remotes.Count} remote");
        }

        System.Collections.IEnumerator ReturnAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _session.ReturnToLobby();
        }

        System.Collections.IEnumerator Screenshots(string times, byte id)
        {
            float started = Time.realtimeSinceStartup;
            string folder = System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".";
            foreach (string part in times.Split(','))
            {
                if (!float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float at)) continue;
                while (Time.realtimeSinceStartup - started < at) yield return null;
                string path = System.IO.Path.Combine(folder, $"lan-race-{id}-{at:0}.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"LAN: screenshot {path}, {_remotes.Count} remote cars");
            }
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        /// <summary>Seconds to GO on this machine, or NaN while the host waits for everyone.</summary>
        float SecondsToGo()
        {
            float now = LanSession.RealTimeOf(Time.fixedTimeAsDouble);
            if (_session.Host != null)
                return float.IsNaN(_session.Host.GoAtHostSeconds) ? float.NaN : _session.Host.GoAtHostSeconds - now;
            LanClient client = _session.Client;
            return client == null || float.IsNaN(client.GoAtHostSeconds) ? float.NaN : client.GoAtHostSeconds - client.HostNow(now);
        }

        void Update()
        {
            // Going back to the lobby: the cars are cleared for the next race, nobody has left.
            if (_session.Race == null) return;

            // A player who has left: their car goes.
            for (int i = _remotes.Count - 1; i >= 0; i--)
            {
                RemoteCar remote = _remotes[i];
                bool gone = _session.Host != null ? _session.Host.Left.Contains(remote.Id)
                          : remote.Heard && !Has(_session.Client?.Cars, remote.Id);
                if (!gone) continue;
                Debug.Log($"LAN: player {remote.Id} left, their car is taken off");
                if (_slotOf.TryGetValue(remote.Id, out int slot)) _director.MarkLeft(slot);
                Destroy(remote.gameObject);
                _remotes.RemoveAt(i);
            }

            Standings();
            if (_session.State == LanSession.Mode.Off && !_hostGone)
            {
                _hostGone = true;
                Debug.Log("LAN: the host has gone");
            }

            float now = LanSession.Now;
            if (now < _nextSend) return;
            _nextSend = Mathf.Max(_nextSend + 1f / SendHz, now);

            // The cars' states are from the last physics step, which ended at this game time.
            float stateTime = LanSession.RealTimeOf(Time.fixedTimeAsDouble + Time.fixedDeltaTime);
            if (_session.Host != null)
            {
                var cars = new List<CarState> { State(0, _mine, stateTime) };
                foreach (var (id, car) in _aiCars) cars.Add(State(id, car, stateTime));
                _session.Host.SendSnapshot(cars, now);
            }
            else _session.Client?.SendCar(State(_session.MyId, _mine, stateTime), stateTime);
        }

        /// <summary>The host sends its standings; a client takes the latest into its
        /// RaceDirector, which then ranks and shows them as its own.</summary>
        void Standings()
        {
            RaceControl control = _director.Control;
            if (control == null) return;

            if (_session.Host != null)
            {
                float now = LanSession.Now;
                if (now < _nextStandings) return;
                _nextStandings = now + 1f / StandingsHz;
                var standings = new Standings { Cars = new Standing[control.Entries.Length] };
                for (int i = 0; i < control.Entries.Length; i++)
                {
                    RaceControl.Entry e = control.Entries[i];
                    standings.Cars[i] = new Standing
                    {
                        LapsComplete = (byte)Mathf.Clamp(e.LapsComplete, 0, 255),
                        ProgressM = e.ProgressM,
                        FinishedAtS = e.FinishedAtS,
                        BestLapS = e.BestLapS < float.MaxValue ? e.BestLapS : -1f,
                        LastLapS = e.LastLapS,
                        Left = _director.HasLeft(i),
                    };
                }
                _session.Host.SendStandings(standings);
                return;
            }

            Standings latest = _session.Client?.Standings;
            if (latest == null || latest == _applied || latest.Cars.Length != control.Entries.Length) return;
            _applied = latest;
            for (int i = 0; i < latest.Cars.Length; i++)
            {
                Standing s = latest.Cars[i];
                RaceControl.Entry e = control.Entries[i];
                e.LapsComplete = s.LapsComplete;
                e.ProgressM = s.ProgressM;
                e.FinishedAtS = s.FinishedAtS;
                e.BestLapS = s.BestLapS >= 0f ? s.BestLapS : float.MaxValue;
                e.LastLapS = s.LastLapS;
                if (s.Left) _director.MarkLeft(i);
            }
            control.Rank();
        }

        GUIStyle _banner;

        /// <summary>A client whose host has gone can finish driving, but nobody keeps the race.</summary>
        void OnGUI()
        {
            if (!_hostGone || Hud.Hidden) return;
            _banner ??= new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _banner.fontSize = Hud.Font(20);
            float width = Hud.Px(620f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Hud.Px(96f), width, Hud.Px(44f)),
                    "The host has left, so the race is over. Esc: main menu", _banner);
        }

        static CarState State(byte id, CarController car, float time)
        {
            Rigidbody body = car.GetComponent<Rigidbody>();
            return new CarState
            {
                Id = id,
                TimeSeconds = time,
                Position = Bridge.ToSim(body.position),
                Orientation = Bridge.ToSim(body.rotation),
                Velocity = Bridge.ToSim(body.linearVelocity),
                AngularVelocity = Bridge.ToSim(body.angularVelocity),
                Steer = car.Sim.SteerPosition,
                EngineRpm = car.Sim.Drivetrain.EngineRpm,
                Throttle = car.LastInputs.Throttle,
                Gear = (byte)Mathf.Max(car.Sim.Drivetrain.Gear, 0),
            };
        }

        static bool Has(Extrapolator cars, byte id)
        {
            if (cars == null) return false;
            foreach (byte each in cars.Ids) if (each == id) return true;
            return false;
        }

        /// <summary>
        /// A colour for every car, the same on every machine: each person keeps theirs unless
        /// someone earlier on the grid has it, and then, like the AI, takes the first colour
        /// nobody has. Eight colours, eight cars at most, so there are always enough.
        /// </summary>
        static byte[] Colours(PlayerInfo[] grid)
        {
            int palette = PlayerSetup.Colours.Length;
            var taken = new bool[palette];
            var colours = new byte[grid.Length];
            for (int pass = 0; pass < 2; pass++)
                for (int slot = 0; slot < grid.Length; slot++)
                {
                    PlayerInfo p = grid[slot];
                    if (p.Ai != (pass == 1)) continue;   // people first, then the AI
                    int c = !p.Ai && p.Colour < palette && !taken[p.Colour] ? p.Colour : System.Array.IndexOf(taken, false);
                    if (c < 0) c = slot % palette;
                    taken[c] = true;
                    colours[slot] = (byte)c;
                }
            return colours;
        }

        /// <summary>A grid slot: two abreast, rows 10 m apart behind the line, 2.5 m either
        /// side of the road's centre, as TrackSceneBuilder.GridSlot places the scene's cars.</summary>
        static (Vector3, Quaternion) GridSlot(TrackPath track, int slot, float cgHeight)
        {
            Vector3[] centre = track.centre;
            int n = centre.Length;
            float back = (slot / 2) * RowGapM + RowGapM;
            int index = ((-Mathf.RoundToInt(back / track.sampleSpacing)) % n + n) % n;
            Vector3 heading = centre[(index + 1) % n] - centre[(index - 1 + n) % n];
            heading.y = 0f;
            heading.Normalize();
            var right = new Vector3(heading.z, 0f, -heading.x);
            Vector3 position = centre[index] + right * (slot % 2 == 0 ? -GridLateralM : GridLateralM)
                               + Vector3.up * (cgHeight + 0.05f);
            return (position, Quaternion.LookRotation(heading, Vector3.up));
        }
    }
}
