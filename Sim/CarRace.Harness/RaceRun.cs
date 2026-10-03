using System;
using System.Collections.Generic;
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
        const float StopSeconds = 6f;          // under the 8 s after which a stopped car is retired
        const float LappedPace = 0.55f;
        // Held up no more than this behind a car under a blue flag, from 50 m behind to past.
        // Not the 15 s first planned: lapping cars take 2 to 48 s, set by when they commit to a
        // pass (see RaceDriver.YieldTo), so this catches a car that is never let by, not a slow pass.
        const float BlueSeconds = 60f;
        const float HeldUpM = 50f;

        public static int Run(CarConfig config, string circuit, int cars, int raceLaps,
                              int seed, bool reverseGrid, bool verbose = false,
                              string csvPath = null, bool fastestLast = false,
                              int stopCar = -1, float stopAt = 0f, int lappedCar = -1, float tyreWear = 0f,
                              int stopAlso = -1, bool safetyCarOn = false, float safetyCarAt = -1f)
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

                // Fastest last on its own is the test for passing: a reversed grid is not,
                // because every car starts behind one barely slower than itself, and with
                // identical cars and no slipstream nobody can get past a neighbour.
                int grid = reverseGrid ? cars - 1 - i
                         : fastestLast ? (i == 0 ? cars - 1 : i - 1)
                         : i;
                names[grid] = $"AI {grid + 1:00}";
                // --lapped N: car N slow enough to be lapped, for the blue flags.
                if (grid + 1 == lappedCar) pace = LappedPace;
                drivers[grid] = new RaceDriver(names[grid], track, config, limits, pace);
                rigs[grid] = new Rig(config);
                rigs[grid].Sim.TyreWearRate = tyreWear;

                fastest = MathF.Max(fastest, pace);
                slowest = MathF.Min(slowest, pace);
            }

            for (int i = 0; i < cars; i++) PlaceOnGrid(rigs[i], drivers[i], track, i);


            var control = new RaceControl(names, raceLaps);

            // Pit stops, with tyre wear on: a box a car, by grid slot, and each driver's own wear
            // to come in at, spread over 0.65 to 0.75 so the field does not all stop together.
            var pitEntered = new float[cars];
            var pitSeconds = new List<float>();
            float fastestInLimit = 0f;
            int stoppedOutside = 0;
            var stillFor = new float[cars];
            for (int i = 0; i < cars; i++)
            {
                pitEntered[i] = -1f;
                if (tyreWear <= 0f) continue;
                int car = i;
                drivers[i].Box = track.PitBoxIndex[i % TrackData.PitBoxes];
                drivers[i].PitWear = 0.65f + 0.1f * (float)random.NextDouble();
                drivers[i].TyresFitted = () => { rigs[car].Sim.FitNewTyres(); control.Entries[car].PitStops++; };
            }
            // The safety car, with --safety-car or --safety-car-at: a car of the field's own, parked
            // in its box, driven like the rest but outside RaceControl, and last in the field the
            // drivers see.
            SafetyCar safety = null;
            Rig scRig = null;
            if (safetyCarOn && track.HasPitLane)
            {
                safety = new SafetyCar(new RaceDriver("Safety car", track, config, limits, 0.85f), cars) { ForceAt = safetyCarAt };
                safety.Park(track);
                scRig = new Rig(config);
                scRig.Settle();
                int box = track.SafetyCarBox;
                Vector3 tangent = track.Tangent(track.LanePoints[TrackData.PitLaneIndex], box);
                Vector3 spot = track.LanePoints[TrackData.PitLaneIndex][box] + TrackData.Right(tangent) * track.PitBoxShiftM;
                scRig.Body.State = new BodyState
                {
                    Position = new Vector3(spot.X, scRig.Sim.Config.CgHeight, spot.Z),
                    Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(tangent.X, tangent.Z)),
                    Velocity = Vector3.Zero,
                    AngularVelocity = Vector3.Zero,
                };
            }
            var stopped = new bool[cars];
            var pitting = new bool[cars];
            var speeds = new float[cars];
            int scPasses = 0, scContacts = 0;
            bool scTouching = false;
            float deployedAt = -1f, leadingAt = -1f, queuedAt = -1f, calledAt = -1f;
            float closest = float.MaxValue, scFromM = 0f, queuedLaps = -1f;
            SafetyCar.Phase lastPhase = SafetyCar.Phase.In;
            var field = new RaceDriver.Seen[safety != null ? cars + 1 : cars];
            var retired = new bool[cars];
            var contact = new bool[cars, cars];
            var stoppedSince = new float[cars];
            var offLineM = new float[cars];
            var crawlingSince = new float[cars];
            // Flags: yellow where a car is in trouble, blue for a car about to be lapped.
            var flags = new RaceFlags(cars);
            var flagIndex = new int[cars];
            var flagProgress = new float[cars];
            var trouble = new bool[cars];
            var racing = new bool[cars];
            var moving = new bool[cars];
            // For every pair, when a car a lap up came within HeldUpM behind the other, or -1:
            // how long lapping cars are held up, whichever one the blue flag names.
            var heldSince = new float[cars, cars];
            for (int a = 0; a < cars; a++) for (int b = 0; b < cars; b++) heldSince[a, b] = -1f;
            var bluePair = new int[cars];
            for (int i = 0; i < cars; i++) bluePair[i] = -1;
            float yellowSeconds = 0f, longestBlue = 0f;
            int yellowPasses = 0, blueFlags = 0;
            var judges = new TrackLimits[cars];
            for (int i = 0; i < cars; i++) judges[i] = new TrackLimits();
            var nextCrawlLog = new float[cars];
            for (int i = 0; i < cars; i++) crawlingSince[i] = -1f;
            for (int i = 0; i < cars; i++) stoppedSince[i] = -1f;

            Console.WriteLine($"=== Race: {track.Name} ===");
            Console.WriteLine($"  {cars} cars, {raceLaps} laps, pace {slowest:0.000} to {fastest:0.000}"
                            + $"{(reverseGrid ? ", fastest gridded last" : "")}"
                            + $"{(fastestLast ? ", fastest gridded last, the rest in pace order" : "")}");
            Console.WriteLine($"  grid is behind the line, so every car drives the same distance\n");

            System.IO.StreamWriter csv = null;
            if (csvPath != null)
            {
                csv = new System.IO.StreamWriter(csvPath);
                csv.WriteLine("t,car,lap,s,x,z,speed_kph,fwd_kph,target_kph,planned_kph,cap_kph,"
                            + "steer,throttle,brake,cross_m,lateral_m,offset_m,wanted_m,heading_deg,"
                            + "sideslip_deg,recovering,yaw_rate,slip_fl,slip_fr,slip_rl,slip_rr,"
                            + "load_fl,load_fr,load_rl,load_rr,gear,blocked_by,gap_m,following,overtaking,pit");
            }

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
                        float forwardSpeed = Vector3.Dot(rigs[i].Body.State.Velocity, rigs[i].Body.State.Forward);
                        if (forwardSpeed > 10f) moving[i] = true;
                        Vector3 along = track.Tangent(track.Line, drivers[i].Path.Index);
                        bool spun = Vector3.Dot(rigs[i].Body.State.Forward, along) < 0f;
                        trouble[i] = moving[i] && (MathF.Abs(forwardSpeed) < 3f || spun) && !drivers[i].InPits;
                        stopped[i] = moving[i] && MathF.Abs(forwardSpeed) < 3f && !drivers[i].InPits;
                        pitting[i] = drivers[i].Path.Pitting;
                        speeds[i] = forwardSpeed;
                        racing[i] = !retired[i] && !control.Entries[i].Disqualified;
                        flagIndex[i] = drivers[i].Path.Index;
                        flagProgress[i] = drivers[i].Path.ProgressM;
                    }
                    foreach (var (car, passed) in flags.Update(track, flagIndex, flagProgress, trouble, racing, time))
                    {
                        yellowPasses++;
                        control.YellowPass(car, passed, time);
                        if (verbose)
                            Console.WriteLine($"    YELLOW  {time,7:0.0} s  {control.Entries[car].Name} passed "
                                            + $"{control.Entries[passed].Name} under a yellow flag");
                    }
                    if (flags.Yellow.Count > 0) yellowSeconds += ReactionSteps * Dt;
                    if (safety != null)
                    {
                        int leaderLaps = LeaderLaps(control);
                        if (safety.State == SafetyCar.Phase.Leading && queuedAt < 0f && safety.Queued(track, flagProgress, speeds, racing, pitting))
                        {
                            queuedAt = time;
                            queuedLaps = (safety.Driver.Path.ProgressM - scFromM) / track.LengthM;
                        }
                        foreach (var (car, passed) in safety.Update(track, flags, flagIndex, flagProgress, speeds, trouble,
                                                                    stopped, racing, pitting, leaderLaps, raceLaps, time,
                                                                    ReactionSteps * Dt))
                        {
                            scPasses++;
                            control.SafetyCarPass(car, passed, time);
                            if (verbose)
                                Console.WriteLine($"    SC      {time,7:0.0} s  {control.Entries[car].Name} passed "
                                                + $"{control.Entries[passed].Name} under the safety car");
                        }
                        if (safety.State != lastPhase)
                        {
                            if (safety.State == SafetyCar.Phase.Deployed) deployedAt = time;
                            if (safety.State == SafetyCar.Phase.Leading) { leadingAt = time; scFromM = safety.Driver.Path.ProgressM; }
                            if (safety.State == SafetyCar.Phase.InThisLap) calledAt = time;
                            Console.WriteLine($"    SC      {time,7:0.0} s  {safety.State}, leader on lap {leaderLaps + 1}");
                            lastPhase = safety.State;
                        }
                    }
                    for (int i = 0; i < cars; i++)
                    {
                        drivers[i].UnderYellow = flags.InYellow[i] >= 0;
                        drivers[i].YellowFor = flags.InYellow[i];
                        drivers[i].YieldTo = safety != null && safety.Out ? -1 : flags.BlueFor[i];
                        if (safety != null)
                        {
                            drivers[i].UnderSafetyCar = safety.Out;
                            drivers[i].SafetyCarShare = safety.Share;
                        }
                        if (flags.BlueFor[i] >= 0 && bluePair[i] < 0) blueFlags++;
                        bluePair[i] = flags.BlueFor[i];

                        // Held up: a car at least a lap up within HeldUpM behind, until it is past
                        // or drops back past the flag's reach.
                        for (int b = 0; b < cars; b++)
                        {
                            if (b == i) continue;
                            float lead = flagProgress[b] - flagProgress[i];
                            float behind = RaceFlags.BehindOnRoad(track, flagProgress[i], flagProgress[b]);
                            // Only directly behind: a lapping car queued behind another lapping car is
                            // held up by that car, not by the one being lapped.
                            bool between = false;
                            for (int c = 0; c < cars && !between; c++)
                            {
                                if (c == i || c == b || !racing[c]) continue;
                                float back = ((flagProgress[i] - flagProgress[c]) % track.LengthM + track.LengthM) % track.LengthM;
                                between = back < behind;
                            }
                            bool held = racing[i] && racing[b] && !between && lead >= track.LengthM - HeldUpM && behind < HeldUpM;
                            bool gone = !racing[i] || !racing[b] || lead < track.LengthM - RaceFlags.BlueBehindM
                                     || behind > RaceFlags.BlueBehindM;
                            if (held && heldSince[i, b] < 0f) heldSince[i, b] = time;
                            if (heldSince[i, b] < 0f) continue;
                            longestBlue = MathF.Max(longestBlue, time - heldSince[i, b]);
                            if (!gone) continue;
                            if (verbose)
                                Console.WriteLine($"    BLUE    {time,7:0.0} s  {control.Entries[b].Name} by "
                                                + $"{control.Entries[i].Name} after {time - heldSince[i, b]:0.0} s held up");
                            heldSince[i, b] = -1f;
                        }
                    }

                    for (int i = 0; i < cars; i++)
                    {
                        field[i] = new RaceDriver.Seen
                        {
                            Index = drivers[i].Path.Index,
                            LateralM = drivers[i].Path.LateralFromLineM,
                            SpeedMs = Vector3.Dot(rigs[i].Body.State.Velocity,
                                                  rigs[i].Body.State.Forward),
                            Plan = drivers[i].Path.Plan,
                            Position = rigs[i].Body.State.Position,
                            Lane = drivers[i].Path.Lane,

                            // A retired car is behind the barriers, not in the middle of the
                            // road. Leaving it in the field makes everyone queue behind a
                            // parked car and the race never ends.
                            Gone = retired[i],
                            Pitting = drivers[i].Path.Pitting,
                            Yielding = drivers[i].YieldTo >= 0,
                            YieldingTo = drivers[i].YieldTo,
                        };
                    }
                    if (safety != null)
                    {
                        RaceDriver sc = safety.Driver;
                        field[cars] = new RaceDriver.Seen
                        {
                            Index = sc.Path.Index,
                            LateralM = sc.Path.LateralFromLineM,
                            SpeedMs = scRig.ForwardSpeed,
                            Plan = sc.Path.Plan,
                            Position = scRig.Body.State.Position,
                            Lane = sc.Path.Lane,
                            Pitting = sc.Path.Pitting,
                            YieldingTo = -1,
                        };
                        sc.UnderSafetyCar = safety.Out;
                        sc.Observe(track, field, cars, ReactionSteps * Dt);

                        // Contacts with it, counted apart from the field's.
                        bool touching = false;
                        for (int i = 0; i < cars; i++)
                        {
                            if (retired[i]) continue;
                            touching |= Overlapping(scRig, rigs[i]);
                            if (safety.Out && !sc.Path.Pitting && !drivers[i].Path.Pitting)
                            {
                                Vector3 d = rigs[i].Body.State.Position - scRig.Body.State.Position;
                                closest = MathF.Min(closest, MathF.Sqrt(d.X * d.X + d.Z * d.Z));
                            }
                        }
                        if (touching && !scTouching)
                        {
                            scContacts++;
                            if (verbose) Console.WriteLine($"    CONTACT {time,7:0.0} s  with the safety car");
                        }
                        scTouching = touching;
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

                    control.Tick(time);
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

                    drivers[i].Path.TyreGrip = rig.Sim.TyreGrip;
                    if (tyreWear > 0f && step % ReactionSteps == 0)
                    {
                        Wheel[] w = rig.Sim.Wheels;
                        drivers[i].Wear = MathF.Max(w[0].Wear + w[1].Wear, w[2].Wear + w[3].Wear) * 0.5f;
                        drivers[i].LapsLeft = raceLaps - entry.LapsComplete - 1;
                    }
                    VehicleInputs input = drivers[i].Drive(rig.Body.State, Dt);
                    // --stop-car N --stop-at S: car N stands on its brakes for StopSeconds from S,
                    // where it is, for the yellow flags.
                    if ((i + 1 == stopCar || i + 1 == stopAlso) && time >= stopAt && time < stopAt + StopSeconds)
                        input = new VehicleInputs { Brake = 1f, Steer = input.Steer };
                    rig.Step(input);

                    // Track limits, judged as in Unity. A disqualified car is out of the race.
                    bool wasOff = judges[i].Off;
                    TrackLimits.Kind offence = judges[i].Step(track, drivers[i].Path.Index, rig.Body.State.Position,
                                                              rig.Body.State.Forward, rig.Body.State.Velocity,
                                                              rig.Sim.Wheels, Dt);
                    if (!wasOff && judges[i].Off) control.LeftTrack(i);
                    RaceControl.Event ruling = control.Judge(i, offence, time);
                    if (ruling != null && verbose)
                        Console.WriteLine($"    {ruling.Ruling.ToString().ToUpperInvariant(),-7} {time,7:0.0} s  s = "
                                        + $"{drivers[i].Path.Index * track.SampleSpacingM,6:0} m  {entry.Name}  {offence}, "
                                        + $"drove {judges[i].LastDrivenM:0} m where the road needs {judges[i].LastLegalM:0} m");
                    if (entry.Disqualified) { retired[i] = true; continue; }
                    if (csv != null && step % ReactionSteps == 0)
                        WriteCsvRow(csv, i, rig, drivers[i], input, track);

                    // How far from the centreline, so a car that loses it shows up as a number
                    // rather than as an unexplained forty seconds.
                    int sample = drivers[i].Path.Index;
                    float fromCentre = MathF.Abs(track.LateralOffset(
                        track.Centre, sample, rig.Body.State.Position));
                    if (fromCentre > offLineM[i]) offLineM[i] = fromCentre;

                    // The pit lane: how long each stop takes, the fastest anyone goes under the limit,
                    // and any car standing still in it anywhere but its own box.
                    if (drivers[i].InPits && pitEntered[i] < 0f) pitEntered[i] = time;
                    if (!drivers[i].InPits && pitEntered[i] >= 0f) { pitSeconds.Add(time - pitEntered[i]); pitEntered[i] = -1f; }
                    if (drivers[i].InPits && track.InPitLimit(drivers[i].Path.Index))
                        fastestInLimit = MathF.Max(fastestInLimit, rig.ForwardSpeed);
                    bool still = drivers[i].Pit == RaceDriver.PitState.InLane && MathF.Abs(rig.ForwardSpeed) < 0.5f;
                    stillFor[i] = still ? stillFor[i] + Dt : 0f;
                    if (stillFor[i] >= 1f && stillFor[i] < 1f + Dt)
                    {
                        stoppedOutside++;
                        if (verbose)
                            Console.WriteLine($"    PIT     {time,7:0.0} s  {entry.Name} standing in the pit lane "
                                            + $"{(track.IntoPit(drivers[i].Box) - track.IntoPit(drivers[i].Path.Index)) * track.SampleSpacingM:0} m short of its box, "
                                            + $"blocked by {(drivers[i].BlockedBy >= 0 ? control.Entries[drivers[i].BlockedBy].Name : "nobody")} at {drivers[i].BlockedGapM:0.0} m");
                    }

                    if (rig.SpeedKph < 3f && time > 3f && !drivers[i].InPits)
                    {
                        if (stoppedSince[i] < 0f) stoppedSince[i] = time;
                        if (time - stoppedSince[i] > 8f) retired[i] = true;
                    }
                    else stoppedSince[i] = -1f;
                }

                if (safety != null)
                {
                    safety.Driver.Path.TyreGrip = scRig.Sim.TyreGrip;
                    scRig.Step(safety.Driver.Drive(scRig.Body.State, Dt));
                }

                time += Dt;
                step++;

                bool running = false;
                for (int i = 0; i < cars; i++) if (!retired[i]) running = true;
                if (!running) break;
            }

            csv?.Dispose();
            if (csvPath != null) Console.WriteLine($"  telemetry written to {csvPath}\n");

            bool pitsOk = true;
            if (tyreWear > 0f)
            {
                float worst = 0f;
                foreach (Rig r in rigs) foreach (Wheel w in r.Sim.Wheels) worst = MathF.Max(worst, w.Wear);
                int stops = 0, unstopped = 0;
                foreach (RaceControl.Entry e in control.Entries) { stops += e.PitStops; if (e.PitStops == 0) unstopped++; }
                float average = 0f;
                foreach (float s in pitSeconds) average += s / pitSeconds.Count;
                Console.WriteLine($"  tyres: wear x{tyreWear:0.#}, the most worn tyre at the end {worst * 100f:0}%; {stops} pit stops, "
                                + $"{unstopped} cars never stopped; {average:0.0} s in the pit lane on average; fastest under the "
                                + $"limit {fastestInLimit * 3.6f:0} km/h; {stoppedOutside} stops outside a box");
                pitsOk = fastestInLimit <= TrackData.PitLimitMs + 2f / 3.6f && stoppedOutside == 0;
            }
            Console.WriteLine($"  flags: yellow out for {yellowSeconds:0.0} s, {yellowPasses} passes under yellow; "
                            + $"{blueFlags} blue flags, the longest hold-up {longestBlue:0.0} s");
            if (safety != null)
            {
                string At(float t) => t < 0f ? "never" : $"{t:0} s";
                Console.WriteLine($"  safety car: out {safety.Deployments} time(s); deployed {At(deployedAt)}, leading {At(leadingAt)}, "
                                + $"field queued {At(queuedAt)}{(queuedLaps >= 0f ? $" ({queuedLaps:0.0} laps behind it)" : "")}, called in {At(calledAt)}, green {At(safety.GreenAt)}; "
                                + $"{scPasses} passes under it, {scContacts} contacts with it, closest car {(closest < 1e6f ? $"{closest:0.0} m" : "none")}");
            }
            int result = Report(control, retired, track, totalContacts, contactsOnLapOne, time, timeout);
            if (safety != null && safetyCarAt >= 0f && (safety.GreenAt < 0f || scPasses > 0 || scContacts > 0))
            {
                Console.WriteLine("  FAIL: the safety car never went in, a car passed under it, or a car touched it.");
                result = 1;
            }
            if (!pitsOk)
            {
                Console.WriteLine("  FAIL: a car broke the pit limit or stopped in the pit lane outside its box.");
                result = 1;
            }
            if (yellowPasses > 0)
            {
                Console.WriteLine("  FAIL: a car passed under a yellow flag.");
                result = 1;
            }
            if (longestBlue > BlueSeconds)
            {
                Console.WriteLine($"  FAIL: a car lapping another was held up {longestBlue:0.0} s; it should be by within {BlueSeconds:0} s.");
                result = 1;
            }
            return result;
        }

        /// <summary>
        /// One row per car per reaction interval: what the lap runner writes, plus what the
        /// driver decided about traffic. Reading those decisions beside the tyres is what
        /// found why cars touched; the contact log only ever showed the aftermath. The
        /// sideslip is the rig's, which folds past 90 degrees, so a negative forward speed
        /// is the column that says a car is travelling backwards.
        /// </summary>
        static void WriteCsvRow(System.IO.StreamWriter csv, int car, Rig rig, RaceDriver driver,
                                in VehicleInputs input, TrackData track)
        {
            PathDriver path = driver.Path;
            Wheel[] w = rig.Sim.Wheels;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            csv.WriteLine(string.Join(",", new[]
            {
                rig.Time.ToString("0.###", c),
                (car + 1).ToString(c),
                path.Laps.ToString(c),
                (path.Index * track.SampleSpacingM).ToString("0.#", c),
                rig.Body.State.Position.X.ToString("0.##", c),
                rig.Body.State.Position.Z.ToString("0.##", c),
                rig.SpeedKph.ToString("0.##", c),
                (rig.ForwardSpeed * 3.6f).ToString("0.##", c),
                (path.TargetSpeedMs * 3.6f).ToString("0.##", c),
                (path.PlannedSpeedMs * 3.6f).ToString("0.##", c),
                (path.SpeedCapMs < 0f ? -1f : path.SpeedCapMs * 3.6f).ToString("0.##", c),
                input.Steer.ToString("0.####", c),
                input.Throttle.ToString("0.###", c), input.Brake.ToString("0.###", c),
                path.LineErrorM.ToString("0.###", c),
                path.LateralFromLineM.ToString("0.###", c),
                path.LineOffsetM.ToString("0.###", c),
                driver.WantedOffsetM.ToString("0.###", c),
                path.HeadingErrorDeg.ToString("0.##", c),
                rig.SideslipDegrees.ToString("0.##", c),
                path.Recovering.ToString("0.##", c),
                rig.YawRate.ToString("0.####", c),
                (w[0].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                (w[1].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                (w[2].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                (w[3].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                w[0].Load.ToString("0", c), w[1].Load.ToString("0", c),
                w[2].Load.ToString("0", c), w[3].Load.ToString("0", c),
                rig.Sim.Drivetrain.Gear.ToString(c),
                (driver.BlockedBy + 1).ToString(c),
                driver.BlockedGapM.ToString("0.#", c),
                (driver.IsFollowing ? 1 : 0).ToString(c),
                (driver.IsOvertaking ? 1 : 0).ToString(c),
                driver.Pit.ToString(),
            }));
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
                            + $"{"race time",13}{"gap",10}{"places",8}{"hits",6}{"pen",6}");
            Console.WriteLine("  " + new string('-', 72));

            float winner = order[0].ResultS;
            int offences = control.Events.Count;
            foreach (RaceControl.Entry entry in order)
            {
                string position = entry.Disqualified ? "DSQ" : entry.Finished ? $"{entry.Position,3}" : "DNF";
                string best = entry.BestLapS < float.MaxValue ? Time(entry.BestLapS) : "-";
                string raceTime = entry.Finished ? Time(entry.ResultS)
                                                 : $"lap {entry.LapsComplete + 1}";
                string gap = !entry.Finished ? "-"
                           : entry.Position == 1 ? "-"
                           : $"+{entry.ResultS - winner:0.00}s";
                int places = entry.Grid - entry.Position;
                string moved = !entry.Finished ? "-" : places == 0 ? "0" : $"{places:+0;-0}";

                Console.WriteLine($"  {position}  {entry.Name,-8}{entry.Grid,6}{best,12}"
                                + $"{raceTime,13}{gap,10}{moved,8}{entry.Contacts,6}{(entry.PenaltyS > 0f ? $"+{entry.PenaltyS:0}s" : "-"),6}");
            }

            int finishers = 0;
            foreach (RaceControl.Entry entry in control.Entries) if (entry.Finished) finishers++;

            Console.WriteLine($"\n  {finishers} of {control.Entries.Length} finished, "
                            + $"{contacts} contacts, {contactsOnLapOne} of them on lap one, "
                            + $"{offences} track limits offences");

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
