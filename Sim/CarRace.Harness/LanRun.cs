using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using CarRace.Net;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// A whole LAN race in one process: a host and bot players, each a separate LanHost or
    /// LanClient with its own sockets on loopback, each simulating only its own car and
    /// racing the others as the network shows them. The host adds AI cars.
    ///
    /// Every bot keeps its own clock, offset from the others by up to a hundred seconds as
    /// two computers' clocks would be, so the clock sync is what makes their views agree.
    /// Each bot's UDP goes through a proxy that delays, jitters and drops packets.
    ///
    /// What it checks, in order: a host is found by its beacon; players join and a seventh
    /// is turned away; a change of colour and the AI count reach everyone; the start reaches
    /// everyone with the same grid and GO at the same instant; during the race, how far the
    /// cars each player sees are from where they really are; a player who quits disappears
    /// from everyone's track; and nobody can join a race already running.
    /// </summary>
    public static class LanRun
    {
        const float Dt = 1f / Rig.SubstepHz;
        const float SendHz = 30f;
        const float CountdownSeconds = LanHost.CountdownSeconds;

        sealed class Machine
        {
            public string Name;
            public LanClient Client;       // null on the host
            public UdpProxy Proxy;
            public float Skew;             // this machine's clock minus the true one
            public byte Id;
            public Rig Rig;
            public RaceDriver Driver;
            public readonly Dictionary<byte, int> Hint = new Dictionary<byte, int>();
            public bool Quit;
            public float NextSend;         // each machine sends on its own beat, as real ones do
        }

        public static int Run(CarConfig config, string circuit, int players, int ai, float seconds,
                              float latencyMs, float jitterMs, float lossPct)
        {
            TrackData track;
            try { track = TrackLoader.Load(circuit); }
            catch (Exception error) { Console.WriteLine($"  {error.Message}"); return 2; }

            players = Math.Clamp(players, 1, LanHost.MaxPlayers);
            Console.WriteLine($"=== LAN race: {track.Name} ===");
            Console.WriteLine($"  {players} players and {ai} AI, {seconds:0} s of racing, cars sent at {SendHz:0} Hz");
            Console.WriteLine($"  each player's UDP: {latencyMs:0} ms latency, {jitterMs:0} ms jitter, "
                            + $"{lossPct:0.0}% loss, real sockets on loopback\n");

            var failures = new List<string>();
            void Check(bool ok, string what)
            {
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what}");
                if (!ok) failures.Add(what);
            }

            var random = new Random(11);
            float now = 0f;

            using var host = new LanHost("Harness host", new PlayerInfo { Name = "Host", Colour = 0 }, circuit);
            var hostMachine = new Machine { Name = "Host", Id = 0 };
            var machines = new List<Machine> { hostMachine };

            // Discovery, as the Join screen does it.
            using (var browser = new LanBrowser())
            {
                bool found = PollUntil(ref now, () => browser.Games.Count > 0, 2f, host, machines,
                                       extra: t => browser.Poll(t));
                LanBrowser.Game game = found ? browser.Games[0] : null;
                Check(found && game.Beacon.GamePort == host.Port && game.Beacon.Track == circuit,
                      found ? $"browser found \"{game.Beacon.HostName}\" at {game.Address}:{game.Beacon.GamePort}, "
                              + $"{game.Beacon.Players}/{game.Beacon.Capacity} players"
                            : "browser found the host");
            }

            // Joining.
            for (int i = 1; i < players; i++)
            {
                var proxy = new UdpProxy(new IPEndPoint(IPAddress.Loopback, host.Port), latencyMs, jitterMs, lossPct, random);
                var me = new PlayerInfo { Name = $"Bot {i}", Colour = (byte)i, Design = (byte)(i % 4) };
                machines.Add(new Machine
                {
                    Name = me.Name, Proxy = proxy,
                    Skew = (float)(random.NextDouble() * 200.0 - 100.0),
                    Client = new LanClient(IPAddress.Loopback, host.Port, me, proxy.Port),
                });
            }

            bool joined = PollUntil(ref now, () => machines.TrueForAll(m => m.Client == null || m.Client.Id != 0
                                                                            && m.Client.Lobby?.Players.Count == players),
                                    3f, host, machines);
            Check(joined && host.Humans == players, $"{players - 1} bots joined, host lists {host.Humans} players");
            foreach (Machine m in machines) if (m.Client != null) m.Id = m.Client.Id;

            if (players == LanHost.MaxPlayers)
            {
                using var extra = new LanClient(IPAddress.Loopback, host.Port, new PlayerInfo { Name = "Seventh" });
                PollUntil(ref now, () => extra.State == LanClient.Phase.Rejected, 2f, host, machines, t => extra.Poll(t));
                Check(extra.State == LanClient.Phase.Rejected && extra.Rejected == RejectReason.Full,
                      $"a seventh player is turned away ({extra.State}, {extra.Rejected})");
            }

            // Lobby changes reach everyone.
            host.SetAiCars(ai);
            if (players > 1) machines[1].Client.SetSetup(7, 3);
            bool agreed = PollUntil(ref now, () => machines.TrueForAll(m => m.Client == null
                                        || m.Client.Lobby.AiCars == host.Lobby.AiCars
                                           && (players == 1 || m.Client.Lobby.Players[1].Colour == 7)),
                                    2f, host, machines);
            Check(agreed, $"AI count ({host.Lobby.AiCars}) and a changed colour reach every player");

            // Clocks: pings run from joining, so a second of lobby gives each client several.
            PollUntil(ref now, () => false, 1f, host, machines);

            // The start.
            RaceStart race = host.Start(now);
            bool started = PollUntil(ref now, () => machines.TrueForAll(m => m.Client == null || m.Client.Race != null),
                                     2f, host, machines);
            bool sameGrid = started;
            foreach (Machine m in machines)
                if (m.Client?.Race != null)
                    sameGrid &= string.Join(",", Array.ConvertAll(m.Client.Race.Grid, p => p.Id))
                              == string.Join(",", Array.ConvertAll(race.Grid, p => p.Id));
            Check(started && sameGrid, $"every player has the start and the same {race.Grid.Length} car grid");

            // GO waits for the last player to load.
            host.SetReady();
            for (int i = 1; i < machines.Count - 1; i++) machines[i].Client.SendReady();
            PollUntil(ref now, () => false, 0.5f, host, machines);
            bool held = players < 2 || float.IsNaN(host.GoAtHostSeconds);
            if (players > 1) machines[machines.Count - 1].Client.SendReady();
            bool went = PollUntil(ref now, () => machines.TrueForAll(m => m.Client == null || !float.IsNaN(m.Client.GoAtHostSeconds)),
                                  2f, host, machines) && !float.IsNaN(host.GoAtHostSeconds);
            Check(held && went, "GO waits until the last player is ready, then reaches everyone");
            float goAt = host.GoAtHostSeconds, worstGo = 0f;
            foreach (Machine m in machines)
            {
                if (m.Client == null || float.IsNaN(m.Client.GoAtHostSeconds)) continue;
                // GO on this machine's clock, turned back into true time.
                float go = m.Client.GoAtHostSeconds - m.Client.OffsetSeconds - m.Skew;
                worstGo = MathF.Max(worstGo, MathF.Abs(go - goAt));
            }
            Check(worstGo < 0.01f, $"GO falls within {worstGo * 1000f:0.0} ms of the host's on every clock");

            // The cars.
            SpeedPlan.Limits limits = LapRun.PlanningLimits(config);
            var truth = new Dictionary<byte, (Rig Rig, RaceDriver Driver)>();
            var aiCars = new List<(byte Id, Rig Rig, RaceDriver Driver)>();
            for (int slot = 0; slot < race.Grid.Length; slot++)
            {
                PlayerInfo p = race.Grid[slot];
                float pace = 0.84f - 0.05f * slot / Math.Max(race.Grid.Length - 1, 1);
                var rig = new Rig(config);
                var driver = new RaceDriver(p.Name, track, config, limits, pace);
                RaceRun.PlaceOnGrid(rig, driver, track, slot);
                truth[p.Id] = (rig, driver);

                Machine owner = machines.Find(m => m.Id == p.Id && !p.Ai);
                if (owner != null) { owner.Rig = rig; owner.Driver = driver; }
                else aiCars.Add((p.Id, rig, driver));
            }

            // The race.
            float raceStart = now, quitAt = now + CountdownSeconds + seconds * 0.6f, end = now + CountdownSeconds + seconds;
            Machine quitter = players > 2 ? machines[machines.Count - 1] : null;
            float nextSend = now, nextSample = now + CountdownSeconds + 2f, nextObserve = now;
            int hostBytes = 0, snapshotsSent = 0, clientPackets = 0;
            var errors = new List<float>();
            var rawErrors = new List<float>();
            var headingErrors = new List<float>();
            var pastErrors = new List<float>();
            float goneAfter = -1f;
            foreach (Machine m in machines) m.NextSend = now + (float)random.NextDouble() / SendHz;

            while (now < end)
            {
                bool go = now >= goAt;
                bool observe = now >= nextObserve;
                if (observe) nextObserve += 10 * Dt;

                foreach (Machine m in machines)
                    if (!m.Quit) Drive(m.Rig, m.Driver, go, observe ? View(m, host, track, truth, now) : null, track);
                RaceDriver.Seen[] hostView = observe ? View(hostMachine, host, track, truth, now) : null;
                foreach (var car in aiCars) Drive(car.Rig, car.Driver, go, hostView, track);
                now += Dt;

                foreach (Machine m in machines)
                {
                    if (m.Client == null || m.Quit || now < m.NextSend) continue;
                    m.NextSend += 1f / SendHz;
                    m.Client.SendCar(State(m.Id, m.Rig, m.Driver, 0f), now + m.Skew);
                    clientPackets++;
                }

                if (now >= nextSend)
                {
                    nextSend += 1f / SendHz;
                    var hostCars = new List<CarState> { State(0, truth[0].Rig, truth[0].Driver, now) };
                    foreach (var car in aiCars) hostCars.Add(State(car.Id, car.Rig, car.Driver, now));
                    hostBytes += host.SendSnapshot(hostCars, now);
                    snapshotsSent++;
                }

                PollAll(host, machines, now);

                if (quitter != null && !quitter.Quit && now >= quitAt)
                {
                    quitter.Quit = true;
                    quitter.Client.Dispose();
                    quitter.Proxy.Dispose();
                    truth.Remove(quitter.Id);
                }
                if (quitter != null && quitter.Quit && goneAfter < 0f
                    && !Contains(host.Cars, quitter.Id)
                    && machines.TrueForAll(m => m.Quit || m.Client == null || !Contains(m.Client.Cars, quitter.Id)))
                    goneAfter = now - quitAt;

                if (now < nextSample) continue;
                nextSample += 1f / 60f;

                // What each player sees of every other car, against where that car truly is.
                foreach (Machine m in machines)
                {
                    if (m.Quit) continue;
                    Extrapolator seen = m.Client?.Cars ?? host.Cars;
                    float hostNow = m.Client != null ? m.Client.HostNow(now + m.Skew) : now;
                    foreach (var (id, real) in truth)
                    {
                        if (id == m.Id || m.Client == null && id >= LanHost.FirstAiId) continue;
                        if (!seen.Sample(id, hostNow, out CarState shown) || !seen.Raw(id, hostNow, out CarState raw)) continue;
                        BodyState body = real.Rig.Body.State;
                        errors.Add(Vector3.Distance(shown.Position, body.Position));
                        rawErrors.Add(Vector3.Distance(raw.Position, body.Position));
                        headingErrors.Add(AngleBetween(shown.Orientation, body.Orientation));

                        // Drawn in the past instead, as the Interpolator would at this rate:
                        // two send intervals late, plus the trip.
                        float behind = 2f / SendHz + latencyMs / 1000f;
                        pastErrors.Add(body.Velocity.Length() * behind);
                    }
                }
            }

            Console.WriteLine();
            if (errors.Count == 0) { Console.WriteLine("  nothing was measured, which is a bug in the test."); return 2; }
            errors.Sort(); rawErrors.Sort(); headingErrors.Sort(); pastErrors.Sort();
            Console.WriteLine($"  seen now   mean {Mean(errors):0.000} m, p99 {P(errors, 0.99f):0.000} m, "
                            + $"worst {errors[^1]:0.000} m from where the car really is");
            Console.WriteLine($"  unsmoothed mean {Mean(rawErrors):0.000} m, p99 {P(rawErrors, 0.99f):0.000} m, worst {rawErrors[^1]:0.000} m");
            Console.WriteLine($"  heading    mean {Mean(headingErrors):0.00} deg, p99 {P(headingErrors, 0.99f):0.00} deg, worst {headingErrors[^1]:0.00} deg");
            Console.WriteLine($"  drawn in the past instead: mean {Mean(pastErrors):0.00} m, worst {pastErrors[^1]:0.00} m behind");
            float raceSeconds = now - raceStart;
            Console.WriteLine($"  bandwidth  host sends {hostBytes / (float)snapshotsSent:0} bytes a snapshot, "
                            + $"{hostBytes / raceSeconds / 1024f:0.0} kB/s to each player; "
                            + $"each player sends {clientPackets / Math.Max(players - 1, 1) / raceSeconds * 35f / 1024f:0.0} kB/s");
            foreach (Machine m in machines)
                if (m.Client != null && !m.Quit)
                    Console.WriteLine($"  {m.Name,-6} clock {m.Skew,8:0.000} s off the host's, synced to within "
                                    + $"{MathF.Abs(m.Client.OffsetSeconds + m.Skew) * 1000f:0.00} ms, round trip {m.Client.RoundTripSeconds * 1000f:0.0} ms");
            Console.WriteLine();

            Check(P(errors, 0.99f) < 0.25f && errors[^1] < 1f,
                  $"cars are drawn where they are: p99 {P(errors, 0.99f):0.000} m, worst {errors[^1]:0.000} m");
            Check(headingErrors[^1] < 5f, $"and facing the right way: worst {headingErrors[^1]:0.00} deg");
            if (quitter != null)
                Check(goneAfter >= 0f && goneAfter < 1f,
                      goneAfter >= 0f ? $"a player who quits leaves every track in {goneAfter * 1000f:0} ms"
                                      : "a player who quits leaves every track");

            using (var late = new LanClient(IPAddress.Loopback, host.Port, new PlayerInfo { Name = "Late" }))
            {
                PollUntil(ref now, () => late.State == LanClient.Phase.Rejected, 2f, host, machines, t => late.Poll(t));
                Check(late.State == LanClient.Phase.Rejected && late.Rejected == RejectReason.Started,
                      $"a player arriving mid-race is turned away ({late.State}, {late.Rejected})");
            }

            foreach (Machine m in machines) { if (!m.Quit) { m.Client?.Dispose(); m.Proxy?.Dispose(); } }

            Console.WriteLine(failures.Count == 0 ? "\n  PASS" : $"\n  FAIL: {failures.Count} check(s)");
            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>The field as one machine sees it: its own car as it is, everyone else as
        /// its network view has them, and on the host the AI as they are.</summary>
        static RaceDriver.Seen[] View(Machine m, LanHost host, TrackData track,
                                      Dictionary<byte, (Rig Rig, RaceDriver Driver)> truth, float now)
        {
            var field = new List<RaceDriver.Seen>();
            Extrapolator seen = m.Client?.Cars ?? host.Cars;
            float hostNow = m.Client != null ? m.Client.HostNow(now + m.Skew) : now;

            foreach (var (id, real) in truth)
            {
                BodyState body;
                if (id == m.Id || m.Client == null && id >= LanHost.FirstAiId) body = real.Rig.Body.State;
                else if (seen.Sample(id, hostNow, out CarState shown))
                    body = new BodyState { Position = shown.Position, Orientation = shown.Orientation, Velocity = shown.Velocity };
                else continue;

                int index = Nearest(track, body.Position, m.Hint.TryGetValue(id, out int hint) ? hint : -1);
                m.Hint[id] = index;
                field.Add(new RaceDriver.Seen
                {
                    Index = index,
                    LateralM = track.LateralOffset(track.Line, index, body.Position),
                    SpeedMs = Vector3.Dot(body.Velocity, body.Forward),
                    Plan = real.Driver.Path.Plan,
                    Position = body.Position,
                });
            }
            return field.ToArray();
        }

        static void Drive(Rig rig, RaceDriver driver, bool go, RaceDriver.Seen[] field, TrackData track)
        {
            if (field != null)
            {
                int me = Array.FindIndex(field, s => Vector3.DistanceSquared(s.Position, rig.Body.State.Position) < 1e-6f);
                if (me >= 0) driver.Observe(track, field, me, 10 * Dt);
            }
            rig.Step(go ? driver.Drive(rig.Body.State, Dt) : new VehicleInputs { Brake = 1f });
        }

        static CarState State(byte id, Rig rig, RaceDriver driver, float time)
        {
            BodyState body = rig.Body.State;
            return new CarState
            {
                Id = id, TimeSeconds = time, Position = body.Position, Orientation = body.Orientation,
                Velocity = body.Velocity, AngularVelocity = body.AngularVelocity,
                Steer = rig.Sim.SteerPosition, EngineRpm = rig.Sim.Drivetrain.EngineRpm,
                Gear = (byte)Math.Max(rig.Sim.Drivetrain.Gear, 0),
                Lap = (byte)Math.Max(driver.Path.Laps, 0),
            };
        }

        static int Nearest(TrackData track, Vector3 position, int hint)
        {
            int n = track.Line.Length;
            int from = hint < 0 ? 0 : hint - 60, to = hint < 0 ? n : hint + 60;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = from; i < to; i++)
            {
                int j = track.Wrap(i);
                float d = Vector3.DistanceSquared(track.Line[j], position);
                if (d < bestDistance) { bestDistance = d; best = j; }
            }
            return best;
        }

        static void PollAll(LanHost host, List<Machine> machines, float now)
        {
            foreach (Machine m in machines)
            {
                if (m.Quit || m.Client == null) continue;
                m.Proxy.Pump(now);
                m.Client.Poll(now + m.Skew);
            }
            host.Poll(now);
            foreach (Machine m in machines) if (!m.Quit) m.Proxy?.Pump(now);
        }

        /// <summary>Runs the network, not the cars, until the condition holds. Time moves
        /// at the real rate here, since TCP and connecting take real time.</summary>
        static bool PollUntil(ref float now, Func<bool> done, float seconds, LanHost host, List<Machine> machines,
                              Action<float> extra = null)
        {
            float until = now + seconds;
            var clock = Stopwatch.StartNew();
            float start = now;
            while (now < until)
            {
                extra?.Invoke(now);
                PollAll(host, machines, now);
                if (done()) return true;
                Thread.Sleep(1);
                now = start + (float)clock.Elapsed.TotalSeconds;
            }
            return done();
        }

        static bool Contains(Extrapolator cars, byte id)
        {
            foreach (byte i in cars.Ids) if (i == id) return true;
            return false;
        }

        static float Mean(List<float> values) { double sum = 0; foreach (float v in values) sum += v; return (float)(sum / values.Count); }
        static float P(List<float> sorted, float q) => sorted[Math.Min((int)(sorted.Count * q), sorted.Count - 1)];

        static float AngleBetween(Quaternion a, Quaternion b)
        {
            float dot = MathF.Min(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 1f);
            return 2f * MathF.Acos(dot) * 180f / MathF.PI;
        }

        /// <summary>
        /// Sits between one player and the host and does to their datagrams what a bad
        /// network would: delay, jitter, which reorders, and loss. The player sends to it as
        /// if it were the host, and the host answers it as if it were the player.
        /// </summary>
        sealed class UdpProxy : IDisposable
        {
            readonly UdpClient _front = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            readonly UdpClient _back = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            readonly IPEndPoint _host;
            readonly float _latency, _jitter, _loss;
            readonly Random _random;
            readonly List<(float Due, byte[] Data, bool ToHost)> _inFlight = new List<(float, byte[], bool)>();
            IPEndPoint _player;

            public int Port => ((IPEndPoint)_front.Client.LocalEndPoint).Port;

            public UdpProxy(IPEndPoint host, float latencyMs, float jitterMs, float lossPct, Random random)
            {
                _host = host;
                _latency = latencyMs / 1000f;
                _jitter = jitterMs / 1000f;
                _loss = lossPct / 100f;
                _random = random;
            }

            public void Pump(float now)
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                while (_front.Available > 0) { byte[] d = _front.Receive(ref from); _player = from; Queue(d, true, now); }
                while (_back.Available > 0) { byte[] d = _back.Receive(ref from); Queue(d, false, now); }

                for (int i = _inFlight.Count - 1; i >= 0; i--)
                {
                    var (due, data, toHost) = _inFlight[i];
                    if (due > now) continue;
                    _inFlight.RemoveAt(i);
                    if (toHost) _back.Send(data, data.Length, _host);
                    else if (_player != null) _front.Send(data, data.Length, _player);
                }
            }

            void Queue(byte[] data, bool toHost, float now)
            {
                if (_random.NextDouble() < _loss) return;
                float delay = _latency + (float)(_random.NextDouble() * 2.0 - 1.0) * _jitter;
                _inFlight.Add((now + MathF.Max(delay, 0f), data, toHost));
            }

            public void Dispose() { _front.Dispose(); _back.Dispose(); }
        }
    }
}
