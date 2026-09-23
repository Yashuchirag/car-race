using System;
using System.Numerics;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// A field of AI cars racing each other on a generated circuit, with no engine present.
    ///
    /// Car to car contact is detected and counted, not simulated. The vehicle model computes
    /// the forces on one car, and making two of them bounce off each other is the physics
    /// engine's job, in Unity, where the cars are rigid bodies that already collide. What
    /// this can answer is the question that comes first: do sixteen drivers get off a grid,
    /// round a lap and past each other without needing to touch.
    /// </summary>
    public static class RaceRun
    {
        const float Dt = 1f / Rig.SubstepHz;

        /// <summary>Drivers look at the field every 20 ms. Re-reading it every physics step
        /// does not make a better driver, only a twitchier one.</summary>
        const int ReactionSteps = 10;

        internal const float RowGapM = 10f;
        const float GridLateralM = 2f;
        const float CarLengthM = 4.4f;
        const float CarWidthM = 1.9f;

        public static int Run(CarConfig config, string circuit, int cars, int raceLaps,
                              int seed, bool reverseGrid, bool verbose = false)
        {
            TrackData track;
            try
            {
                track = TrackLoader.Load(circuit);
            }
            catch (Exception error)
            {
                Console.WriteLine($"  {error.Message}");
                return 2;
            }

            SpeedPlan.Limits limits = LapRun.PlanningLimits(config);
            var random = new Random(seed);

            var drivers = new RaceDriver[cars];
            var rigs = new Rig[cars];
            var names = new string[cars];
            float fastest = 0f, slowest = 1f;

            for (int i = 0; i < cars; i++)
            {
                // A field is a spread, not a ladder: the gap between the quick ones is
                // smaller than the gap to the back, and a little scatter stops the finishing
                // order being decided before the start.
                float rank = cars > 1 ? (float)i / (cars - 1) : 0f;
                float pace = 0.85f - 0.07f * rank + (float)(random.NextDouble() - 0.5) * 0.012f;

                int grid = reverseGrid ? cars - 1 - i : i;
                names[grid] = $"AI {grid + 1:00}";
                drivers[grid] = new RaceDriver(names[grid], track, config, limits, pace);
                rigs[grid] = new Rig(config);

                fastest = MathF.Max(fastest, pace);
                slowest = MathF.Min(slowest, pace);
            }

            for (int i = 0; i < cars; i++) PlaceOnGrid(rigs[i], drivers[i], track, i);

            var control = new RaceControl(names, raceLaps);
            var field = new RaceDriver.Seen[cars];
            var retired = new bool[cars];
            var contact = new bool[cars, cars];
            var stoppedSince = new float[cars];
            var offLineM = new float[cars];
            var crawlingSince = new float[cars];
            var nextCrawlLog = new float[cars];
            for (int i = 0; i < cars; i++) crawlingSince[i] = -1f;
            for (int i = 0; i < cars; i++) stoppedSince[i] = -1f;

            Console.WriteLine($"=== Race: {track.Name} ===");
            Console.WriteLine($"  {cars} cars, {raceLaps} laps, pace {slowest:0.000} to {fastest:0.000}"
                            + $"{(reverseGrid ? ", fastest gridded last" : "")}");
            Console.WriteLine($"  grid is behind the line, so every car drives the same distance\n");

            float time = 0f;
            float timeout = raceLaps * track.EstimatedLapTimeS * 4f + 120f;
            int contactsOnLapOne = 0;
            int totalContacts = 0;
            int step = 0;

            while (time < timeout)
            {
                if (step % ReactionSteps == 0)
                {
                    for (int i = 0; i < cars; i++)
                    {
                        field[i] = new RaceDriver.Seen
                        {
                            Index = drivers[i].Path.Index,
                            LateralM = drivers[i].Path.LateralFromLineM,
                            SpeedMs = Vector3.Dot(rigs[i].Body.State.Velocity,
                                                  rigs[i].Body.State.Forward),

                            // A retired car is behind the barriers, not in the middle of the
                            // road. Leaving it in the field makes everyone queue behind a
                            // parked car and the race never ends.
                            Gone = retired[i],
                        };
                    }

                    for (int i = 0; i < cars; i++)
                    {
                        if (retired[i]) continue;
                        drivers[i].Observe(track, field, i, ReactionSteps * Dt);

                        int before = control.Entries[i].LapsComplete;
                        control.Update(i, time, drivers[i].Path.Laps, drivers[i].Path.ProgressM);

                        if (verbose && control.Entries[i].LapsComplete > before)
                        {
                            control.Rank();
                            RaceControl.Entry entry = control.Entries[i];
                            Console.WriteLine($"    lap {entry.LapsComplete}  {entry.Name}  "
                                            + $"{Time(entry.LastLapS)}  P{entry.Position}  "
                                            + $"off line {offLineM[i]:0.0} m worst");
                            offLineM[i] = 0f;
                        }
                    }

                    int fresh = CountContacts(rigs, retired, contact, control,
                                              drivers, track, time, verbose);

                    if (verbose)
                    {
                        for (int i = 0; i < cars; i++)
                        {
                            if (retired[i]) continue;

                            float planned = drivers[i].Path.PlannedSpeedMs;
                            float actual = field[i].SpeedMs;
                            bool crawling = planned > 12f && actual < 0.45f * planned;

                            if (!crawling) { crawlingSince[i] = -1f; continue; }
                            if (crawlingSince[i] < 0f) { crawlingSince[i] = time; continue; }
                            if (time - crawlingSince[i] < 2f || time < nextCrawlLog[i]) continue;

                            nextCrawlLog[i] = time + 5f;
                            RaceDriver driver = drivers[i];
                            int by = driver.BlockedBy;
                            Console.WriteLine($"    SLOW    {time,7:0.0} s  s = "
                                            + $"{driver.Path.Index * track.SampleSpacingM,6:0} m  "
                                            + $"{control.Entries[i].Name}  {actual * 3.6f:0} of "
                                            + $"{planned * 3.6f:0} km/h  cap "
                                            + $"{driver.Path.SpeedCapMs * 3.6f:0}  "
                                            + $"blocked by {(by >= 0 ? control.Entries[by].Name : "nobody")}"
                                            + $" at {driver.BlockedGapM:0.0} m  off line "
                                            + $"{driver.Path.LateralFromLineM:+0.0;-0.0} m  sideslip "
                                            + $"{rigs[i].SideslipDegrees:0} deg  gear "
                                            + $"{rigs[i].Sim.Drivetrain.Gear}");
                        }
                    }
                    totalContacts += fresh;
                    if (LeaderLaps(control) < 1) contactsOnLapOne += fresh;
                }

                for (int i = 0; i < cars; i++)
                {
                    if (retired[i]) continue;

                    Rig rig = rigs[i];
                    RaceControl.Entry entry = control.Entries[i];

                    // A finished car is not driven any more, so it cannot hold anyone up while
                    // the rest of the race runs to the flag.
                    if (entry.Finished) { retired[i] = true; continue; }

                    VehicleInputs input = drivers[i].Drive(rig.Body.State, Dt);
                    rig.Step(input);

                    // How far from the centreline, so a car that loses it shows up as a number
                    // rather than as an unexplained forty seconds.
                    int sample = drivers[i].Path.Index;
                    float fromCentre = MathF.Abs(track.LateralOffset(
                        track.Centre, sample, rig.Body.State.Position));
                    if (fromCentre > offLineM[i]) offLineM[i] = fromCentre;

                    if (rig.SpeedKph < 3f && time > 3f)
                    {
                        if (stoppedSince[i] < 0f) stoppedSince[i] = time;
                        if (time - stoppedSince[i] > 8f) retired[i] = true;
                    }
                    else stoppedSince[i] = -1f;
                }

                time += Dt;
                step++;

                bool running = false;
                for (int i = 0; i < cars; i++) if (!retired[i]) running = true;
                if (!running) break;
            }

            return Report(control, retired, track, totalContacts, contactsOnLapOne, time, timeout);
        }

        static int LeaderLaps(RaceControl control)
        {
            int laps = 0;
            foreach (RaceControl.Entry entry in control.Entries)
                laps = Math.Max(laps, entry.LapsComplete);
            return laps;
        }

        /// <summary>
        /// Counts pairs of cars that have just touched. Each car's box is tested in its own
        /// frame, rather than as a circle round it, because a circle wide enough to contain a
        /// car calls two of them side by side a crash when they are a metre apart.
        /// </summary>
        static int CountContacts(Rig[] rigs, bool[] retired, bool[,] contact, RaceControl control,
                                 RaceDriver[] drivers, TrackData track, float time, bool verbose)
        {
            int fresh = 0;

            for (int a = 0; a < rigs.Length; a++)
            {
                for (int b = a + 1; b < rigs.Length; b++)
                {
                    bool touching = !retired[a] && !retired[b] && Overlapping(rigs[a], rigs[b]);

                    if (touching && !contact[a, b])
                    {
                        fresh++;
                        control.Entries[a].Contacts++;
                        control.Entries[b].Contacts++;

                        if (verbose)
                        {
                            float closing = Vector3.Dot(rigs[a].Body.State.Velocity,
                                                        rigs[a].Body.State.Forward)
                                          - Vector3.Dot(rigs[b].Body.State.Velocity,
                                                        rigs[b].Body.State.Forward);
                            float gap = (rigs[b].Body.State.Position
                                       - rigs[a].Body.State.Position).Length();
                            Console.WriteLine($"    CONTACT {time,7:0.0} s  s = "
                                            + $"{drivers[a].Path.Index * track.SampleSpacingM,6:0} m  "
                                            + $"{control.Entries[a].Name} and {control.Entries[b].Name}, "
                                            + $"{gap:0.0} m apart, closing {closing:+0.0;-0.0} m/s, "
                                            + $"{rigs[a].SpeedKph:0} vs {rigs[b].SpeedKph:0} km/h");
                        }
                    }
                    contact[a, b] = touching;
                }
            }

            return fresh;
        }

        static bool Overlapping(Rig a, Rig b)
        {
            Vector3 delta = b.Body.State.Position - a.Body.State.Position;
            delta.Y = 0f;
            if (delta.LengthSquared() > (CarLengthM * CarLengthM)) return false;

            float along = MathF.Abs(Vector3.Dot(delta, a.Body.State.Forward));
            float across = MathF.Abs(Vector3.Dot(delta, a.Body.State.Right));
            return along < CarLengthM && across < CarWidthM;
        }

        internal static void PlaceOnGrid(Rig rig, RaceDriver driver, TrackData track, int slot)
        {
            rig.Settle();

            int row = slot / 2;
            float back = row * RowGapM + RowGapM;
            float lateral = (slot % 2 == 0) ? -GridLateralM : GridLateralM;

            int index = track.Wrap(-(int)MathF.Round(back / track.SampleSpacingM));
            Vector3 tangent = track.Tangent(track.Line, index);
            Vector3 spot = track.Line[index] + TrackData.Right(tangent) * lateral;

            rig.Body.State = new BodyState
            {
                Position = new Vector3(spot.X, rig.Sim.Config.CgHeight, spot.Z),
                Orientation = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY, MathF.Atan2(tangent.X, tangent.Z)),
                Velocity = Vector3.Zero,
                AngularVelocity = Vector3.Zero,
            };

            // Lap -1: crossing the line starts the first lap, so a car at the back of the grid
            // drives the same distance as pole rather than being handed 56 metres.
            driver.Path.StartAt(index, -1);
            driver.Path.LineOffsetM = lateral;
        }

        static int Report(RaceControl control, bool[] retired, TrackData track,
                          int contacts, int contactsOnLapOne, float time, float timeout)
        {
            RaceControl.Entry[] order = control.Classification();

            Console.WriteLine($"  {"pos",3}  {"driver",-8}{"grid",6}{"best lap",12}"
                            + $"{"race time",13}{"gap",10}{"places",8}{"hits",6}");
            Console.WriteLine("  " + new string('-', 66));

            float winner = order[0].FinishedAtS;
            foreach (RaceControl.Entry entry in order)
            {
                string position = entry.Finished ? $"{entry.Position,3}" : "DNF";
                string best = entry.BestLapS < float.MaxValue ? Time(entry.BestLapS) : "-";
                string raceTime = entry.Finished ? Time(entry.FinishedAtS)
                                                 : $"lap {entry.LapsComplete + 1}";
                string gap = !entry.Finished ? "-"
                           : entry.Position == 1 ? "-"
                           : $"+{entry.FinishedAtS - winner:0.00}s";
                int places = entry.Grid - entry.Position;
                string moved = !entry.Finished ? "-" : places == 0 ? "0" : $"{places:+0;-0}";

                Console.WriteLine($"  {position}  {entry.Name,-8}{entry.Grid,6}{best,12}"
                                + $"{raceTime,13}{gap,10}{moved,8}{entry.Contacts,6}");
            }

            int finishers = 0;
            foreach (RaceControl.Entry entry in control.Entries) if (entry.Finished) finishers++;

            Console.WriteLine($"\n  {finishers} of {control.Entries.Length} finished, "
                            + $"{contacts} contacts, {contactsOnLapOne} of them on lap one");

            if (time >= timeout)
            {
                Console.WriteLine("\n  FAIL: the race ran out of time before everyone finished.");
                return 1;
            }

            if (contactsOnLapOne > 0)
            {
                Console.WriteLine("\n  FAIL: cars touched on the opening lap. That is the "
                                + "pile-up this is meant to rule out.");
                return 1;
            }

            if (finishers < control.Entries.Length)
            {
                Console.WriteLine("\n  FAIL: not everyone finished.");
                return 1;
            }

            Console.WriteLine("\n  PASS: every car finished, and nobody touched on lap one.");
            return 0;
        }

        static string Time(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
