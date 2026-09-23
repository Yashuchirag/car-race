using System;
using System.Collections.Generic;
using System.Numerics;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Drives the validated car around a circuit the Python pipeline generated.
    ///
    /// This is the first thing that exercises both halves of the project at once. The
    /// five closed-form checks say the car obeys physics on an empty plane; the pipeline
    /// says a circuit is geometrically sound. Neither says the car can get round the
    /// circuit, and a track full of corners tighter than the car can turn would pass
    /// every check on both sides.
    ///
    /// The ground is still flat. Elevation is in the track file and is ignored here, so
    /// this measures cornering, braking and gearing, not hills. Spa's 17% climb out of
    /// Eau Rouge is exactly the kind of thing this cannot yet see.
    /// </summary>
    public static class LapRun
    {
        const float Dt = 1f / Rig.SubstepHz;
        const int Laps = 2;

        /// <summary>
        /// One lap on every circuit, as a regression. Passing means the validated car can get
        /// round every track the pipeline produces, without leaving the road, which is not
        /// something either half can be asked on its own.
        /// </summary>
        public static int RunAll(CarConfig config, float pace)
        {
            string[] circuits;
            try
            {
                circuits = TrackLoader.Available();
            }
            catch (Exception error)
            {
                Console.WriteLine($"  {error.Message}");
                return 2;
            }

            Console.WriteLine($"=== A lap on each circuit, pace {pace:0.00} ===\n");
            Console.WriteLine($"  {"circuit",-14}{"lap",10}{"plan bound",12}{"vs bound",10}"
                            + $"{"track edge",14}  result");
            Console.WriteLine("  " + new string('-', 68));

            int failures = 0;
            foreach (string circuit in circuits)
            {
                Summary summary = Measure(config, circuit, pace);
                if (summary == null) { failures++; continue; }

                Console.WriteLine($"  {circuit,-14}{Time(summary.Seconds),10}{Time(summary.Bound),12}"
                                + $"{summary.OverBoundPct,9:+0.0;-0.0}%{summary.EdgeM,13:0.00} m  "
                                + $"{(summary.LeftTrack ? "OFF" : "on track")}");
                if (summary.LeftTrack) failures++;
            }

            Console.WriteLine();
            if (failures > 0)
            {
                Console.WriteLine($"  {failures} of {circuits.Length} circuits failed.");
                return 1;
            }

            Console.WriteLine($"  {circuits.Length} of {circuits.Length} circuits completed on track.");
            return 0;
        }

        sealed class Summary
        {
            public float Seconds, Bound, EdgeM, OverBoundPct;
            public bool LeftTrack;
        }

        static Summary Measure(CarConfig config, string circuit, float pace)
        {
            var output = Console.Out;
            Console.SetOut(System.IO.TextWriter.Null);
            try
            {
                Run(config, circuit, false, pace, null, out Summary summary);
                return summary;
            }
            finally
            {
                Console.SetOut(output);
            }
        }

        public static int Run(CarConfig config, string circuit, bool verbose, float pace = 0.85f,
                              string csvPath = null)
            => Run(config, circuit, verbose, pace, csvPath, out _);

        static int Run(CarConfig config, string circuit, bool verbose, float pace,
                       string csvPath, out Summary summary)
        {
            summary = null;
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

            // What the car can actually hold, not what its tyres could in theory. The
            // skidpad ceiling assumes all four tyres peak at once; this car measures 85%
            // of it, because the front axle saturates first. A driver following a fixed
            // line needs margin on top of that for its own steering error, so the plan
            // takes 85% again. Planning at the limit means leaving the road at the limit.
            float holdable = Analytic.SkidpadCeilingG(config) * 0.85f * Physics.Gravity;
            float lateralLimit = holdable * pace;
            float brakeDistance = Analytic.BrakingMetres(config);
            float brakingLimit = (27.78f * 27.78f) / (2f * brakeDistance) * 0.85f;
            float topSpeed = Analytic.TopSpeedKph(config) / 3.6f;

            // Accelerating out of a corner is grip limited only while the gear is low enough
            // to have the torque for it. The car's own 0 to 100 gives the average of the two,
            // which is the right order of magnitude for a plan and needs no new model.
            float tractionLimit = (100f / 3.6f) / Analytic.ZeroToHundredSeconds(config);

            float[] plan = SpeedPlan.Build(track, new SpeedPlan.Limits
            {
                LateralMs2 = lateralLimit,
                BrakingMs2 = brakingLimit,
                TractionMs2 = tractionLimit,
                PowerW = Analytic.PeakPowerWatts(config) * config.DrivetrainEfficiency,
                MassKg = config.Mass,
                TopSpeedMs = topSpeed,
            });

            Console.WriteLine($"=== Lap: {track.Name} ===");
            Console.WriteLine($"  {track.LengthM:0.0} m, {track.Count} samples at "
                            + $"{track.SampleSpacingM:0.00} m, flat ground");
            Console.WriteLine($"  planning for {lateralLimit / Physics.Gravity:0.00} g lateral "
                            + $"(pace {pace:0.00} of the {holdable / Physics.Gravity:0.00} g it holds), "
                            + $"{brakingLimit:0.0} m/s^2 braking, {topSpeed * 3.6f:0} km/h top speed");
            Console.WriteLine($"  plan bound      {Time(PlanLapTime(track, plan))}"
                            + "   the plan driven perfectly, no driver error");
            Console.WriteLine($"  pipeline says   {Time(track.EstimatedLapTimeS)}"
                            + "   point mass GT3, not this car\n");

            var driver = new PathDriver(track, plan, config);
            var rig = new Rig(config);
            Place(rig, track, driver);

            System.IO.StreamWriter csv = null;
            if (csvPath != null)
            {
                csv = new System.IO.StreamWriter(csvPath);
                csv.WriteLine("t,s,x,z,speed_kph,target_kph,steer,throttle,brake,cross_m,"
                            + "heading_deg,sideslip_deg,yaw_rate,slip_fl,slip_fr,slip_rl,slip_rr,"
                            + "load_fl,load_fr,load_rl,load_rr,gear,lap");
            }

            var laps = new List<Lap>();
            var current = new Lap();
            float lapStart = 0f;
            float stoppedSince = -1f;
            float timeout = MathF.Max(track.EstimatedLapTimeS * 3f * Laps, 240f);
            float nextReport = 0f;
            int step = 0;
            int lastIndex = -1;
            float lastProgressAt = 0f;

            while (driver.Laps < Laps)
            {
                // Read before Drive, which is what advances the driver along the line and so
                // is what crosses the start line. Reading it after meant the crossing was
                // never seen, every lap went unrecorded, and the run ended with nothing to
                // report at all.
                int lapsBefore = driver.Laps;
                VehicleInputs input = driver.Drive(rig.Body.State, Dt);
                rig.Step(input);

                Vector3 position = rig.Body.State.Position;
                int index = driver.Index;
                float offset = track.LateralOffset(track.Centre, index, position);
                float edge = offset >= 0f ? track.WidthRight[index] : track.WidthLeft[index];
                float excursion = MathF.Abs(offset) - edge;

                current.Observe(rig, driver, excursion, index * track.SampleSpacingM);

                if (csv != null && step % 10 == 0) WriteCsvRow(csv, rig, driver, input, track, index);
                step++;

                // Reported on time, not on distance. A driver that has lost the road still
                // creeps its index forward, so a distance-triggered log goes quiet exactly
                // when something has gone wrong.
                if (verbose && rig.Time >= nextReport)
                {
                    Console.WriteLine($"    {rig.Time,6:0.0} s  s {index * track.SampleSpacingM,7:0} m  "
                                    + $"{rig.SpeedKph,6:0.0} km/h  target {driver.TargetSpeedMs * 3.6f,6:0.0}  "
                                    + $"offset {offset,8:0.00} m  steer {input.Steer,6:0.00}  "
                                    + $"gear {rig.Sim.Drivetrain.Gear}");
                    nextReport = rig.Time + 1f;
                }

                if (driver.Laps > lapsBefore)
                {
                    current.Seconds = rig.Time - lapStart;
                    laps.Add(current);
                    current = new Lap();
                    lapStart = rig.Time;
                }

                // A car that has lost the road still drags its index forward, because the far
                // end of the search window keeps getting closer. Without this the run burns
                // the whole timeout and reports nothing about where it went wrong.
                if (index != lastIndex) { lastIndex = index; lastProgressAt = rig.Time; }
                if (rig.Time - lastProgressAt > 5f)
                {
                    Console.WriteLine($"  LOST THE LINE at s = {index * track.SampleSpacingM:0} m, "
                                    + $"{offset:0.0} m from the centreline, {rig.SpeedKph:0} km/h.");
                    csv?.Dispose();
                    return 1;
                }

                if (rig.SpeedKph < 2f && rig.Time > 2f)
                {
                    if (stoppedSince < 0f) stoppedSince = rig.Time;
                    if (rig.Time - stoppedSince > 5f)
                    {
                        Console.WriteLine($"  STOPPED at s = {index * track.SampleSpacingM:0} m, "
                                        + $"{rig.Time:0.0} s in. The driver could not recover.");
                        csv?.Dispose();
                        return 1;
                    }
                }
                else stoppedSince = -1f;

                if (rig.Time > timeout)
                {
                    Console.WriteLine($"  TIMED OUT after {rig.Time:0} s at s = "
                                    + $"{index * track.SampleSpacingM:0} m, {driver.Laps} laps done.");
                    csv?.Dispose();
                    return 1;
                }
            }

            csv?.Dispose();
            if (csvPath != null) Console.WriteLine($"  telemetry written to {csvPath}");

            if (laps.Count > 0)
            {
                Lap last = laps[laps.Count - 1];
                float bound = PlanLapTime(track, plan);
                summary = new Summary
                {
                    Seconds = last.Seconds,
                    Bound = bound,
                    EdgeM = -last.MaxExcursionM,
                    OverBoundPct = 100f * (last.Seconds - bound) / bound,
                    LeftTrack = last.LeftTrack,
                };
            }

            return Report(laps, track);
        }

        static void WriteCsvRow(System.IO.StreamWriter csv, Rig rig, PathDriver driver,
                                in VehicleInputs input, TrackData track, int index)
        {
            var b = rig.Body.State;
            Wheel[] w = rig.Sim.Wheels;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            csv.WriteLine(string.Join(",", new[]
            {
                rig.Time.ToString("0.###", c),
                (index * track.SampleSpacingM).ToString("0.#", c),
                b.Position.X.ToString("0.##", c), b.Position.Z.ToString("0.##", c),
                rig.SpeedKph.ToString("0.##", c),
                (driver.TargetSpeedMs * 3.6f).ToString("0.##", c),
                input.Steer.ToString("0.####", c),
                input.Throttle.ToString("0.###", c), input.Brake.ToString("0.###", c),
                driver.LineErrorM.ToString("0.###", c),
                driver.HeadingErrorDeg.ToString("0.##", c),
                rig.SideslipDegrees.ToString("0.##", c),
                rig.YawRate.ToString("0.####", c),
                (w[0].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                (w[1].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                (w[2].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                (w[3].SlipAngle * 180f / MathF.PI).ToString("0.##", c),
                w[0].Load.ToString("0", c), w[1].Load.ToString("0", c),
                w[2].Load.ToString("0", c), w[3].Load.ToString("0", c),
                rig.Sim.Drivetrain.Gear.ToString(c),
                driver.Laps.ToString(c),
            }));
        }

        sealed class Lap
        {
            public float Seconds;
            public float MaxExcursionM = float.NegativeInfinity;
            public float MaxExcursionAtS;
            public float MaxSpeedKph;
            public float MinSpeedKph = float.MaxValue;
            public float MaxSideslipDeg;

            public void Observe(Rig rig, PathDriver driver, float excursion, float s)
            {
                if (excursion > MaxExcursionM) { MaxExcursionM = excursion; MaxExcursionAtS = s; }
                if (rig.SpeedKph > MaxSpeedKph) MaxSpeedKph = rig.SpeedKph;
                if (rig.SpeedKph < MinSpeedKph) MinSpeedKph = rig.SpeedKph;
                float sideslip = MathF.Abs(rig.SideslipDegrees);
                if (sideslip > MaxSideslipDeg) MaxSideslipDeg = sideslip;
            }

            public bool LeftTrack => MaxExcursionM > 0f;
        }

        static int Report(List<Lap> laps, TrackData track)
        {
            if (laps.Count == 0)
            {
                Console.WriteLine("  no laps recorded, which is a bug in the lap counter, "
                                + "not a slow car");
                return 2;
            }

            Console.WriteLine();
            for (int i = 0; i < laps.Count; i++)
            {
                Lap lap = laps[i];
                string kind = i == 0 ? "standing" : "flying  ";
                string verdict = lap.LeftTrack
                    ? $"OFF by {lap.MaxExcursionM:0.00} m at s = {lap.MaxExcursionAtS:0} m"
                    : $"on track, {-lap.MaxExcursionM:0.00} m to spare";
                Console.WriteLine($"  lap {i + 1} {kind}  {Time(lap.Seconds)}   {verdict}");
                Console.WriteLine($"                      {lap.MinSpeedKph:0} to {lap.MaxSpeedKph:0} km/h, "
                                + $"max sideslip {lap.MaxSideslipDeg:0.0} deg");
            }

            Lap flying = laps[laps.Count - 1];
            float versusPipeline = 100f * (flying.Seconds - track.EstimatedLapTimeS) / track.EstimatedLapTimeS;
            Console.WriteLine($"\n  flying lap is {versusPipeline:+0.0;-0.0}% against the pipeline's "
                            + "GT3 estimate, which is a different car");

            if (flying.LeftTrack)
            {
                Console.WriteLine("\n  FAIL: the car left the road. Either the plan asks for more than "
                                + "it can hold, or\n        the corner is tighter than it can turn.");
                return 1;
            }

            Console.WriteLine("\n  PASS: completed the lap inside the track edges.");
            return 0;
        }

        /// <summary>
        /// Places the car at the start of the racing line, at rest, pointing along it. The
        /// first lap therefore includes a standing start and is slower; the second is the
        /// one to compare against anything.
        /// </summary>
        static void Place(Rig rig, TrackData track, PathDriver driver)
        {
            rig.Settle();

            Vector3 start = track.Line[0];
            Vector3 forward = track.Tangent(track.Line, 0);
            float yaw = MathF.Atan2(forward.X, forward.Z);

            rig.Body.State = new BodyState
            {
                Position = new Vector3(start.X, rig.Sim.Config.CgHeight, start.Z),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
                Velocity = Vector3.Zero,
                AngularVelocity = Vector3.Zero,
            };

            driver.StartAt(0);
        }

        /// <summary>Time to drive the plan exactly, as a lower bound on any lap the driver runs.</summary>
        static float PlanLapTime(TrackData track, float[] plan)
        {
            float seconds = 0f;
            for (int i = 0; i < plan.Length; i++) seconds += track.SampleSpacingM / MathF.Max(plan[i], 1f);
            return seconds;
        }

        static string Time(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
