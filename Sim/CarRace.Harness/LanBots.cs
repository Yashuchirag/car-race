using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Numerics;
using System.Threading;
using CarRace.Net;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Bot players that join a real host, the game, so its lobby and its race can be seen
    /// full without six computers. They join, one changes colour after a few seconds, and
    /// when the race starts each loads the circuit, says it is ready, and at GO drives it
    /// with the harness's AI, sending its car as a player's game would.
    ///
    ///     --lan-bots 127.0.0.1 --bots 5 --seconds 120 --tracks D:\Dev\CarRace\Assets\Tracks
    ///
    /// The bots drive the harness's flat ground, where the game's circuit has hills: the
    /// height they send is the road's there plus their own ride height, and they stay level.
    /// </summary>
    public static class LanBots
    {
        const float Dt = 1f / Rig.SubstepHz;
        const float SendHz = 30f;

        sealed class Bot
        {
            public string Name;
            public byte Colour, Design;
            public LanClient Client;
            public Rig Rig;
            public RaceDriver Driver;
            public float SimTime;
            public float NextSend;
            public readonly Dictionary<byte, int> Hint = new Dictionary<byte, int>();
        }

        public static int Run(string address, int bots, float seconds, string tracks)
        {
            if (!IPAddress.TryParse(address, out IPAddress host))
            {
                Console.WriteLine($"  \"{address}\" is not an IP address.");
                return 2;
            }

            Console.WriteLine($"=== {bots} bots joining {host}:{LanHost.DefaultPort} for {seconds:0} s ===\n");
            var list = new List<Bot>();
            for (int i = 1; i <= bots; i++)
            {
                var bot = new Bot { Name = $"Bot {i}", Colour = (byte)(i % 8), Design = (byte)(i % 4) };
                bot.Client = Join(host, bot);
                list.Add(bot);
            }

            var clock = Stopwatch.StartNew();
            var reported = new HashSet<string>();
            void Once(string line) { if (reported.Add(line)) Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0} s  {line}"); }
            bool recoloured = false;
            TrackData track = null;
            float floor = 0f;
            var config = CarConfig.ReferenceSportsCar();

            while (clock.Elapsed.TotalSeconds < seconds)
            {
                float now = (float)clock.Elapsed.TotalSeconds;
                foreach (Bot bot in list)
                {
                    LanClient c = bot.Client;
                    c.Poll(now);

                    // Until it first gets in, a bot keeps trying: the game may still be loading.
                    if (c.State == LanClient.Phase.Closed && c.Id == 0 && now < seconds - 1f)
                    {
                        c.Dispose();
                        Thread.Sleep(200);
                        bot.Client = Join(host, bot);
                        continue;
                    }
                    if (c.Id != 0 && reported.Add($"{bot.Name} joined"))
                    {
                        Console.WriteLine($"  {now,5:0.0} s  {bot.Name} joined as player {c.Id}, and is ready");
                        c.SetReady(true);
                    }
                    if (c.State == LanClient.Phase.Rejected) Once($"{bot.Name} turned away: {c.Rejected}");
                    if (c.State == LanClient.Phase.Closed) Once($"{bot.Name} lost the host");
                    if (c.Synced) Once($"{bot.Name} synced its clock");

                    if (c.Race != null && bot.Rig == null)
                    {
                        Console.WriteLine($"  {now,5:0.0} s  {bot.Name} got the start: {c.Race.Track}, {c.Race.Grid.Length} cars");
                        track ??= Load(c.Race.Track, tracks);
                        if (track == null) { Once($"no circuit file for {c.Race.Track}; pass --tracks"); continue; }
                        floor = float.MaxValue;
                        foreach (Vector3 p in track.Centre) floor = MathF.Min(floor, p.Y);

                        int slot = Array.FindIndex(c.Race.Grid, p => p.Id == c.Id && !p.Ai);
                        bot.Rig = new Rig(config);
                        bot.Driver = new RaceDriver(bot.Name, track, config, LapRun.PlanningLimits(config), 0.78f + 0.01f * slot);
                        RaceRun.PlaceOnGrid(bot.Rig, bot.Driver, track, slot);
                        bot.SimTime = now;
                        c.SendReady();
                    }
                    if (!float.IsNaN(c.GoAtHostSeconds) && reported.Add($"{bot.Name} go"))
                        Console.WriteLine($"  {now,5:0.0} s  {bot.Name} got GO, {c.GoAtHostSeconds - c.HostNow(now):0.00} s away");

                    // Back in the lobby for another race: ready again, and a fresh car for it.
                    if (c.Race == null && bot.Rig != null)
                    {
                        Console.WriteLine($"  {now,5:0.0} s  {bot.Name} is back in the lobby, and ready again");
                        bot.Rig = null;
                        bot.Driver = null;
                        track = null;
                        reported.Remove($"{bot.Name} go");
                        c.SetReady(true);
                    }

                    if (bot.Rig != null) Drive(bot, track, floor, now);
                }

                LanClient first = list[0].Client;
                if (first.Changed && first.Lobby != null && first.Race == null)
                {
                    first.Changed = false;
                    var names = new List<string>();
                    foreach (PlayerInfo p in first.Lobby.Players) names.Add($"{p.Name} (colour {p.Colour})");
                    Console.WriteLine($"  {now,5:0.0} s  lobby: {first.Lobby.Track}, {first.Lobby.AiCars} AI, "
                                    + string.Join(", ", names));
                }

                if (!recoloured && now > 5f && first.Id != 0 && first.Race == null)
                {
                    recoloured = true;
                    first.SetSetup(6, 2);
                    Once("Bot 1 changes to colour 6, design 2");
                }

                Thread.Sleep(2);
            }

            foreach (Bot bot in list)
                if (bot.Client.Synced)
                    Console.WriteLine($"  {bot.Name}: clock round trip {bot.Client.RoundTripSeconds * 1000f:0.00} ms, recent pings' "
                                    + $"offsets within {bot.Client.OffsetSpreadSeconds * 1000f:0.00} ms; "
                                    + (bot.Driver != null ? $"{bot.Driver.Path.Laps} laps, sees {Count(bot.Client.Cars)} other cars" : "did not race"));
            int joined = list.FindAll(b => b.Client.Id != 0).Count;
            foreach (Bot bot in list) bot.Client.Dispose();
            Console.WriteLine($"\n  {joined} of {bots} joined");
            return joined == bots ? 0 : 1;
        }

        static LanClient Join(IPAddress host, Bot bot) =>
            new LanClient(host, LanHost.DefaultPort, new PlayerInfo { Name = bot.Name, Colour = bot.Colour, Design = bot.Design });

        /// <summary>Catches the bot's car up to real time, held on the brakes until GO, then
        /// driven, seeing every other car as the network shows it; and sends it.</summary>
        static void Drive(Bot bot, TrackData track, float floor, float now)
        {
            LanClient c = bot.Client;
            bool go = !float.IsNaN(c.GoAtHostSeconds) && c.HostNow(now) >= c.GoAtHostSeconds;
            int steps = 0;
            while (bot.SimTime + Dt <= now)
            {
                if (go && steps % 10 == 0) bot.Driver.Observe(track, Field(bot, track, now), 0, 10 * Dt);
                bot.Rig.Step(go ? bot.Driver.Drive(bot.Rig.Body.State, Dt) : new VehicleInputs { Brake = 1f });
                bot.SimTime += Dt;
                steps++;
            }

            if (now < bot.NextSend) return;
            bot.NextSend = MathF.Max(bot.NextSend + 1f / SendHz, now);
            BodyState body = bot.Rig.Body.State;
            Vector3 position = body.Position;
            position.Y += track.Centre[track.Wrap(bot.Driver.Path.Index)].Y - floor;
            c.SendCar(new CarState
            {
                Position = position, Orientation = body.Orientation,
                Velocity = body.Velocity, AngularVelocity = body.AngularVelocity,
                Steer = bot.Rig.Sim.SteerPosition, EngineRpm = bot.Rig.Sim.Drivetrain.EngineRpm,
                Throttle = go ? 1f : 0f, Gear = (byte)Math.Max(bot.Rig.Sim.Drivetrain.Gear, 0),
            }, bot.SimTime);
        }

        /// <summary>The bot first, then every other car the network shows it.</summary>
        static RaceDriver.Seen[] Field(Bot bot, TrackData track, float now)
        {
            BodyState me = bot.Rig.Body.State;
            var field = new List<RaceDriver.Seen>
            {
                new RaceDriver.Seen
                {
                    Index = bot.Driver.Path.Index, LateralM = bot.Driver.Path.LateralFromLineM,
                    SpeedMs = Vector3.Dot(me.Velocity, me.Forward), Plan = bot.Driver.Path.Plan, Position = me.Position,
                },
            };
            float hostNow = bot.Client.HostNow(now);
            foreach (byte id in bot.Client.Cars.Ids)
            {
                if (!bot.Client.Cars.Sample(id, hostNow, out CarState seen)) continue;
                Vector3 at = seen.Position;
                at.Y = me.Position.Y;
                int index = Nearest(track, at, bot.Hint.TryGetValue(id, out int hint) ? hint : -1);
                bot.Hint[id] = index;
                var forward = Vector3.Transform(Vector3.UnitZ, seen.Orientation);
                field.Add(new RaceDriver.Seen
                {
                    Index = index, LateralM = track.LateralOffset(track.Line, index, at),
                    SpeedMs = Vector3.Dot(seen.Velocity, forward), Plan = bot.Driver.Path.Plan, Position = at,
                });
            }
            return field.ToArray();
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
                Vector3 d = track.Line[j] - position;
                d.Y = 0f;
                if (d.LengthSquared() < bestDistance) { bestDistance = d.LengthSquared(); best = j; }
            }
            return best;
        }

        /// <summary>The circuit whose name the scene carries ("Track Royal Park Speedway"),
        /// from the given folder or the usual Tracks_Data.</summary>
        static TrackData Load(string scene, string folder)
        {
            string name = scene.StartsWith("Track ") ? scene.Substring(6) : scene;
            var files = new List<string>();
            if (folder != null && Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder, "*.json"));
            foreach (string key in new[] { "bahrain", "monza", "silverstone", "spa", "suzuka", "testcircuit" }) files.Add(key);
            foreach (string file in files)
            {
                try
                {
                    TrackData track = TrackLoader.Load(file);
                    if (track.Name == name) return track;
                }
                catch (Exception) { }
            }
            return null;
        }

        static int Count(Extrapolator cars)
        {
            int n = 0;
            foreach (byte _ in cars.Ids) n++;
            return n;
        }
    }
}
