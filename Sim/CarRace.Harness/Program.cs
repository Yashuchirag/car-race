using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Headless validation of the vehicle model. These are the Phase 1 exit
    /// criteria: until they pass, the car is not worth putting in an engine.
    ///
    ///     dotnet run --project Sim/CarRace.Harness
    ///     dotnet run --project Sim/CarRace.Harness -- --csv run.csv
    /// </summary>
    public static class Program
    {
        record Result(string Name, float Measured, float Expected, string Unit,
                      float LowPct, float HighPct, string Basis)
        {
            public float ErrorPct => Expected == 0f ? 0f : 100f * (Measured - Expected) / Expected;
            public bool Passed => ErrorPct >= LowPct && ErrorPct <= HighPct;
        }

        public static int Main(string[] args)
        {
            var config = CarConfig.ReferenceSportsCar();
            Console.WriteLine($"\n=== {config.Name} ===");
            Console.WriteLine($"  {config.Mass:0} kg, {config.FrontWeightBias * 100:0}/"
                            + $"{(1 - config.FrontWeightBias) * 100:0} weight split, "
                            + $"CG {config.CgHeight:0.00} m, {config.Drive}");
            Console.WriteLine($"  peak torque {PeakTorque(config):0} N m, "
                            + $"peak power {PeakPowerKw(config):0} kW, tyre mu {config.TyreFront.Mu:0.00}");
            Console.WriteLine($"  substep {Rig.SubstepHz:0} Hz\n");

            if (!ConventionCheck()) return 2;

            if (Array.IndexOf(args, "--trace") >= 0) { Trace(config); return 0; }
            if (Array.IndexOf(args, "--grip") >= 0) { GripSweep(config); return 0; }
            if (Array.IndexOf(args, "--corner") >= 0) { CornerSweep(config); return 0; }
            if (Array.IndexOf(args, "--dump") >= 0) { DumpCase(config, "/tmp/case.csv"); return 0; }

            int csvIndex = Array.IndexOf(args, "--csv");
            if (csvIndex >= 0 && csvIndex + 1 < args.Length)
            {
                WriteTelemetry(config, args[csvIndex + 1]);
                return 0;
            }

            // Every expectation is computed from the configuration in Analytic.cs,
            // so a pass means the simulation agrees with closed-form physics rather
            // than with a number somebody liked.
            var results = new List<Result>
            {
                new("0 to 100 km/h", ZeroToHundred(config), Analytic.ZeroToHundredSeconds(config), "s",
                    -2f, 15f, "perfect-launch floor; real launches lose time to tyre relaxation "
                            + "and to traction control catching the wheel"),
                new("skidpad peak", Skidpad(config), Analytic.SkidpadCeilingG(config), "g",
                    -22f, 2f, "all four tyres at peak at once; a real car saturates one axle first"),
                new("100 to 0 braking", BrakingDistance(config), Analytic.BrakingMetres(config), "m",
                    -2f, 10f, "grip limited on all four wheels, with drag"),
                new("top speed", TopSpeed(config), Analytic.TopSpeedKph(config), "km/h",
                    -8f, 4f, "power at the wheels equals drag"),
            };

            Console.WriteLine($"  {"test",-18} {"sim",8} {"analytic",9} {"error",8} {"allowed",12}   result");
            Console.WriteLine("  " + new string('-', 70));
            bool allPassed = true;
            foreach (var r in results)
            {
                Console.WriteLine($"  {r.Name,-18} {r.Measured,8:0.00} {r.Expected,9:0.00} "
                                + $"{r.ErrorPct,7:+0.0;-0.0}% {$"{r.LowPct:+0;-0}..{r.HighPct:+0;-0}%",12}   "
                                + $"{(r.Passed ? "PASS" : "FAIL")}  {r.Unit}");
                allPassed &= r.Passed;
            }
            Console.WriteLine();
            foreach (var r in results)
                Console.WriteLine($"    {r.Name}: analytic is the {r.Basis}.");
            Console.WriteLine();

            var (stable, detail) = StraightLineStability(config);
            Console.WriteLine($"  {"straight stability",-18} {detail,-44} {(stable ? "PASS" : "FAIL")}");
            allPassed &= stable;

            Console.WriteLine($"\n  {(allPassed ? "All 5 checks pass." : "Validation FAILED.")}\n");
            return allPassed ? 0 : 1;
        }

        // ---- checks -----------------------------------------------------------

        /// <summary>
        /// Confirms System.Numerics composes rotations the way the model assumes,
        /// rather than trusting memory about quaternion multiplication order.
        /// </summary>
        static bool ConventionCheck()
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);
            Vector3 rotated = Vector3.Transform(Vector3.UnitZ, yaw);
            bool axes = MathF.Abs(rotated.X - 1f) < 1e-4f && MathF.Abs(rotated.Z) < 1e-4f;
            if (!axes)
                Console.WriteLine($"  CONVENTION CHECK FAILED: +Z yawed 90 deg gave {rotated}, expected +X");

            // Composition order matters more than the single-rotation case and is
            // easy to get backwards: in System.Numerics, q1 * q2 applies q2 first.
            var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f);
            Vector3 sequential = Vector3.Transform(Vector3.Transform(Vector3.UnitZ, yaw), pitch);
            Vector3 composed = Vector3.Transform(Vector3.UnitZ, pitch * yaw);
            bool order = (sequential - composed).Length() < 1e-4f;
            if (!order)
                Console.WriteLine($"  CONVENTION CHECK FAILED: q1*q2 does not apply q2 first "
                                + $"({composed} vs {sequential})");

            return axes && order;
        }

        static float ZeroToHundred(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs { Throttle = 1f };
            while (rig.ForwardSpeed < 100f / 3.6f && rig.Time < 30f) rig.Step(input);
            return rig.Time;
        }

        /// <summary>
        /// Peak sustained lateral acceleration, measured the way a real skidpad
        /// test works: hold a fixed radius and creep the speed up until the car
        /// can no longer stay on the circle.
        ///
        /// A fixed steering angle instead of a fixed radius does not work. Past the
        /// grip limit the car understeers, then snaps, and what gets measured is a
        /// spin rather than a cornering limit.
        /// </summary>
        static float Skidpad(CarConfig config)
        {
            float best = 0f;
            foreach (float radius in new[] { 20f, 30f, 45f, 60f })
                best = MathF.Max(best, RunSkidpad(config, radius, false));
            return best;
        }

        static float RunSkidpad(CarConfig config, float radius, bool verbose)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs();
            float dt = 1f / Rig.SubstepHz;
            const float EntrySpeed = 8f;

            // Reach the entry speed in a straight line before any steering, so the
            // guards below are not judging a car that has not started moving.
            while (rig.ForwardSpeed < EntrySpeed && rig.Time < 20f)
            {
                rig.HoldSpeed(ref input, EntrySpeed);
                input.Steer = 0f;
                rig.Step(input);
            }
            if (rig.Time >= 20f) return 0f;

            float steer = 0f;
            float steerIntegral = 0f;
            float saturatedFor = 0f;
            float targetSpeed = EntrySpeed;
            float easeUntil = rig.Time + 1.5f;
            float best = 0f;
            float nextLog = rig.Time;
            string stop = "time limit";

            while (rig.Time < 90f)
            {
                float speed = rig.ForwardSpeed;
                float wantedYaw = speed / radius;

                // Steer to hold the radius by chasing the yaw rate that circle needs.
                // Proportional plus a clamped integral: a pure integrator winds up
                // to full lock on any brief disturbance and never comes back.
                float yawError = wantedYaw - rig.YawRate;
                steerIntegral = MathF.Max(-0.8f, MathF.Min(0.8f, steerIntegral + yawError * dt * 2.0f));
                steer = MathF.Max(-1f, MathF.Min(1f, yawError * 1.5f + steerIntegral));

                bool easing = rig.Time < easeUntil;
                if (!easing) targetSpeed += 0.6f * dt;

                rig.HoldSpeed(ref input, targetSpeed);
                input.Steer = steer;
                rig.Step(input);

                if (verbose && rig.Time >= nextLog)
                {
                    var wh = rig.Sim.Wheels;
                    Console.WriteLine($"  {rig.Time,5:0.0} {rig.SpeedKph,6:0.0} {rig.YawRate,6:0.00}/{wantedYaw,-5:0.00} "
                        + $"st{steer,5:0.00} {rig.LateralG,5:0.00}g ss{rig.SideslipDegrees,5:0.0} "
                        + $"aF{wh[0].SlipAngle * 180 / MathF.PI,5:0.0} aR{wh[2].SlipAngle * 180 / MathF.PI,5:0.0} "
                        + $"FyF{wh[0].ForceLat,6:0} FyR{wh[2].ForceLat,6:0} "
                        + $"FxR{wh[2].ForceLong,6:0} ld{wh[0].Load,5:0}/{wh[1].Load,-5:0} "
                        + $"thr{rig.Sim.Wheels[2].TractionScale,4:0.00} g{rig.Sim.Drivetrain.Gear}");
                    nextLog += 1f;
                }

                if (easing) continue;

                if (MathF.Abs(rig.SideslipDegrees) > 15f) { stop = $"sideslip {rig.SideslipDegrees:0.0} deg"; break; }

                // Full lock alone is not failure. It is failure only when the car
                // is also no longer making the circle, and only if it persists.
                bool struggling = MathF.Abs(steer) >= 0.995f && rig.YawRate < wantedYaw * 0.90f;
                saturatedFor = struggling ? saturatedFor + dt : 0f;
                if (saturatedFor > 0.75f) { stop = "at full lock and running wide"; break; }

                if (speed < targetSpeed - 4f) { stop = $"speed {speed:0.0} m/s fell {targetSpeed - speed:0.0} behind"; break; }

                bool tracking = MathF.Abs(rig.YawRate - wantedYaw) < wantedYaw * 0.12f;
                if (tracking) best = MathF.Max(best, rig.LateralG);
            }

            if (verbose)
                Console.WriteLine($"  stopped at t={rig.Time:0.0}: {stop};  best {best:0.00} g at radius {radius:0} m");
            return best;
        }

        static float BrakingDistance(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            var accelerate = new VehicleInputs { Throttle = 1f };
            while (rig.ForwardSpeed < 100f / 3.6f && rig.Time < 30f) rig.Step(accelerate);

            Vector3 start = rig.Body.State.Position;
            var brake = new VehicleInputs { Brake = 1f, Clutch = true };
            float deadline = rig.Time + 15f;
            while (rig.ForwardSpeed > 0.15f && rig.Time < deadline) rig.Step(brake);

            return (rig.Body.State.Position - start).Length();
        }

        static float TopSpeed(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs { Throttle = 1f };
            float previous = 0f;
            while (rig.Time < 120f)
            {
                for (int i = 0; i < (int)Rig.SubstepHz; i++) rig.Step(input);
                float now = rig.SpeedKph;
                if (MathF.Abs(now - previous) < 0.05f) break;   // settled
                previous = now;
            }
            return rig.SpeedKph;
        }

        /// <summary>
        /// A steering pulse at speed, then hands off. The car must damp its own yaw
        /// and keep going roughly straight rather than diverging into a spin.
        /// </summary>
        static (bool, string) StraightLineStability(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs { Throttle = 1f };
            while (rig.ForwardSpeed < 40f && rig.Time < 30f) rig.Step(input);

            float cruise = rig.ForwardSpeed;
            input = new VehicleInputs { Throttle = 0.35f, Steer = 0.3f };
            float until = rig.Time + 0.3f;
            while (rig.Time < until) rig.Step(input);

            input.Steer = 0f;
            float peak = 0f;
            until = rig.Time + 3f;
            while (rig.Time < until)
            {
                rig.Step(input);
                peak = MathF.Max(peak, MathF.Abs(rig.YawRate));
            }

            float settled = MathF.Abs(rig.YawRate);
            bool ok = settled < 0.08f && rig.ForwardSpeed > cruise * 0.55f && settled < peak * 0.5f;
            return (ok, $"yaw {settled:0.000} rad/s (peak {peak:0.00}), {rig.SpeedKph:0} km/h");
        }

        /// <summary>Verbose traces for diagnosing a failing check.</summary>
        static void Trace(CarConfig config)
        {
            Console.WriteLine("--- launch trace (full throttle from rest) ---");
            Console.WriteLine($"  {"t",6} {"kph",7} {"gear",5} {"rpm",6} {"slipRL",7} {"loadRL",7} {"fxRL",7} {"driveRL",8} {"accel_g",8}");
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs { Throttle = 1f };
            float lastV = 0f, nextLog = 0f;
            while (rig.ForwardSpeed < 100f / 3.6f && rig.Time < 15f)
            {
                float before = rig.ForwardSpeed;
                rig.Step(input);
                if (rig.Time >= nextLog || rig.Time < 1f)
                {
                    var w = rig.Sim.Wheels[VehicleSim.RL];
                    float acc = (rig.ForwardSpeed - lastV) / MathF.Max(rig.Time - nextLog + 0.25f, 1e-3f);
                    Console.WriteLine($"  {rig.Time,6:0.00} {rig.SpeedKph,7:0.0} {rig.Sim.Drivetrain.Gear,5} "
                        + $"{rig.Sim.Drivetrain.EngineRpm,6:0} {w.SlipRatio,7:0.000} {w.Load,7:0} "
                        + $"{w.ForceLong,7:0} {w.DriveTorque,8:0} {acc / Physics.Gravity,8:0.00}"
                        + $" slip={rig.Sim.Drivetrain.Slipping}");
                    lastV = rig.ForwardSpeed;
                    nextLog += rig.Time < 1f ? 0.1f : 0.25f;
                }
            }
            Console.WriteLine($"  reached 100 km/h at {rig.Time:0.00} s\n");

            Console.WriteLine("--- skidpad trace ---");
            Console.WriteLine($"  {"t",6} {"kph",7} {"target",7} {"yaw",7} {"wantYaw",8} {"steer",6} {"latG",6} {"sideslip",9}");
            foreach (float radius in new[] { 20f, 30f, 45f, 60f })
            {
                Console.WriteLine($"  --- radius {radius:0} m ---");
                RunSkidpad(config, radius, true);
            }
        }

        /// <summary>
        /// Force probe: holds the body at a fixed velocity and slip angle and reads
        /// the lateral force back. No controllers, no integration drift, so it
        /// answers one question only, which is what the tyre model can actually do.
        /// </summary>
        static void GripSweep(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            float dt = 1f / Rig.SubstepHz;
            var input = new VehicleInputs { Clutch = true };

            Console.WriteLine("--- lateral grip probe: body held at fixed slip angle, 25 m/s ---");
            Console.WriteLine($"  {"slip_deg",9} {"latG",7} {"slipA_FL",9} {"Fy_FL",8} {"Fy_RL",8} "
                            + $"{"load_FL",8} {"load_RL",8} {"peak_FL",8}");

            float bestG = 0f, bestAngle = 0f;
            for (float beta = 0f; beta <= 16f; beta += 1f)
            {
                rig.Reset();
                rig.Settle();
                float rad = beta * MathF.PI / 180f;
                var velocity = new Vector3(25f * MathF.Sin(rad), 0f, 25f * MathF.Cos(rad));
                var pinned = new Vector3(0f, config.CgHeight, 0f);
                Wrench last = default;

                for (int i = 0; i < 600; i++)
                {
                    last = rig.Sim.Step(rig.Body.State, input, rig.Ground, dt);
                    rig.Body.Integrate(last, dt);
                    // Pin the pose so this measures steady force, not a trajectory.
                    rig.Body.State.Velocity = velocity;
                    rig.Body.State.AngularVelocity = Vector3.Zero;
                    rig.Body.State.Orientation = Quaternion.Identity;
                    rig.Body.State.Position = pinned;
                }

                var w = rig.Sim.Wheels;
                float latG = MathF.Abs(last.Force.X) / (config.Mass * Physics.Gravity);
                float peakFL = Pacejka.PeakForce(config.TyreFront, w[0].Load, 1f);
                Console.WriteLine($"  {beta,9:0.0} {latG,7:0.000} "
                    + $"{w[0].SlipAngle * 180f / MathF.PI,9:0.0} {w[0].ForceLat,8:0} {w[2].ForceLat,8:0} "
                    + $"{w[0].Load,8:0} {w[2].Load,8:0} {peakFL,8:0}");
                if (latG > bestG) { bestG = latG; bestAngle = beta; }
            }

            float analytic = config.TyreFront.Mu;
            Console.WriteLine($"\n  peak {bestG:0.00} g at {bestAngle:0} deg body slip");
            Console.WriteLine($"  analytic ceiling from tyre mu alone: {analytic:0.00} g");
        }

        /// <summary>
        /// Steady-state cornering at a fixed steering angle and held speed. No yaw
        /// controller, so nothing here can be a controller instability: whatever the
        /// car does is the car.
        /// </summary>
        static void CornerSweep(CarConfig config)
        {
            Console.WriteLine("--- steady cornering: fixed steer, held speed ---");
            Console.WriteLine($"  {"steer",6} {"v_ms",6} {"latG",6} {"sideslip",9} {"aF",6} {"aR",6} "
                            + $"{"radius",7} {"outcome",-10}");
            float best = 0f;
            foreach (float steer in new[] { 0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f })
            foreach (float speed in new[] { 10f, 15f, 20f, 25f, 30f, 35f })
            {
                var rig = new Rig(config);
                rig.Settle();
                var input = new VehicleInputs();

                while (rig.ForwardSpeed < speed * 0.99f && rig.Time < 30f)
                {
                    rig.HoldSpeed(ref input, speed);
                    input.Steer = 0f;
                    rig.Step(input);
                }
                if (rig.Time >= 30f) continue;

                float ease = rig.Time + 2f;
                float measure = ease + 3f;
                float finish = measure + 1.5f;
                float sum = 0f; int n = 0;
                string outcome = "steady";

                while (rig.Time < finish)
                {
                    rig.HoldSpeed(ref input, speed);
                    input.Steer = steer * MathF.Min((rig.Time - (ease - 2f)) / 2f, 1f);
                    rig.Step(input);

                    if (MathF.Abs(rig.SideslipDegrees) > 25f) { outcome = "spun"; break; }
                    if (rig.Time > measure) { sum += rig.LateralG; n++; }
                }

                if (n == 0) { Console.WriteLine($"  {steer,6:0.00} {speed,6:0} {"-",6} {"-",9} {"-",6} {"-",6} {"-",7} {outcome,-10}"); continue; }
                float g = sum / n;
                float radius = rig.ForwardSpeed / MathF.Max(MathF.Abs(rig.YawRate), 1e-4f);
                Console.WriteLine($"  {steer,6:0.00} {speed,6:0} {g,6:0.00} {rig.SideslipDegrees,9:0.0} "
                    + $"{rig.Sim.Wheels[0].SlipAngle * 180 / MathF.PI,6:0.0} "
                    + $"{rig.Sim.Wheels[2].SlipAngle * 180 / MathF.PI,6:0.0} {radius,7:0.0} {outcome,-10}");
                if (outcome == "steady") best = MathF.Max(best, g);
            }
            Console.WriteLine($"\n  best steady lateral: {best:0.00} g");
        }

        /// <summary>Full-rate dump of one cornering case, for offline analysis.</summary>
        static void DumpCase(CarConfig config, string path)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs();
            var c = CultureInfo.InvariantCulture;
            using var f = new StreamWriter(path);
            f.WriteLine("t,posY,speed,yaw,sideslip,roll_deg,pitch_deg,steer,throttle,brake,"
                      + "g0,g1,g2,g3,c0,c1,c2,c3,l0,l1,l2,l3,aF,aR,fyF,fyR,fxR,w0,w2");

            float target = 10f, steer = 0.30f, ease0 = 0f;
            bool easing = false;
            while (rig.Time < 14f)
            {
                rig.HoldSpeed(ref input, target);
                if (!easing && rig.ForwardSpeed >= target * 0.99f) { easing = true; ease0 = rig.Time; }
                input.Steer = easing ? steer * MathF.Min((rig.Time - ease0) / 2f, 1f) : 0f;
                rig.Step(input);

                var b = rig.Body.State;
                var w = rig.Sim.Wheels;
                var e = ToEuler(b.Orientation);
                f.WriteLine(string.Join(",", new[]{
                    rig.Time.ToString("0.####",c), b.Position.Y.ToString("0.####",c),
                    rig.ForwardSpeed.ToString("0.###",c), rig.YawRate.ToString("0.####",c),
                    rig.SideslipDegrees.ToString("0.##",c),
                    e.X.ToString("0.##",c), e.Y.ToString("0.##",c),
                    input.Steer.ToString("0.###",c), input.Throttle.ToString("0.###",c),
                    input.Brake.ToString("0.###",c),
                    (w[0].Grounded?1:0).ToString(), (w[1].Grounded?1:0).ToString(),
                    (w[2].Grounded?1:0).ToString(), (w[3].Grounded?1:0).ToString(),
                    w[0].Compression.ToString("0.####",c), w[1].Compression.ToString("0.####",c),
                    w[2].Compression.ToString("0.####",c), w[3].Compression.ToString("0.####",c),
                    w[0].Load.ToString("0",c), w[1].Load.ToString("0",c),
                    w[2].Load.ToString("0",c), w[3].Load.ToString("0",c),
                    (w[0].SlipAngle*180/MathF.PI).ToString("0.##",c),
                    (w[2].SlipAngle*180/MathF.PI).ToString("0.##",c),
                    w[0].ForceLat.ToString("0",c), w[2].ForceLat.ToString("0",c),
                    w[2].ForceLong.ToString("0",c),
                    w[0].AngularVelocity.ToString("0.##",c), w[2].AngularVelocity.ToString("0.##",c),
                }));
            }
            Console.WriteLine($"  dumped to {path}");
        }

        static Vector2 ToEuler(Quaternion q)
        {
            float roll = MathF.Atan2(2f*(q.W*q.Z + q.X*q.Y), 1f - 2f*(q.Y*q.Y + q.Z*q.Z));
            float pitch = MathF.Asin(MathF.Max(-1f, MathF.Min(1f, 2f*(q.W*q.X - q.Y*q.Z))));
            return new Vector2(roll*180f/MathF.PI, pitch*180f/MathF.PI);
        }

        // ---- telemetry --------------------------------------------------------

        static void WriteTelemetry(CarConfig config, string path)
        {
            var rig = new Rig(config);
            rig.Settle();
            using var file = new StreamWriter(path);
            file.WriteLine("time_s,speed_kph,forward_ms,lateral_g,yaw_rate,gear,engine_rpm,"
                         + "throttle,brake,steer,"
                         + "load_fl,load_fr,load_rl,load_rr,"
                         + "slip_ratio_rl,slip_angle_fl,fx_rl,fy_fl");

            var input = new VehicleInputs();
            int stride = (int)(Rig.SubstepHz / 100f);   // log at 100 Hz
            int step = 0;

            while (rig.Time < 20f)
            {
                // Launch, cruise, then a steering input, then braking.
                if (rig.Time < 8f) { input.Throttle = 1f; input.Brake = 0f; input.Steer = 0f; }
                else if (rig.Time < 14f) { input.Throttle = 0.5f; input.Steer = 0.45f; }
                else { input.Throttle = 0f; input.Brake = 1f; input.Steer = 0f; }

                rig.Step(input);
                if (step++ % stride != 0) continue;

                var w = rig.Sim.Wheels;
                var c = CultureInfo.InvariantCulture;
                file.WriteLine(string.Join(",", new[]
                {
                    rig.Time.ToString("0.###", c), rig.SpeedKph.ToString("0.##", c),
                    rig.ForwardSpeed.ToString("0.##", c), rig.LateralG.ToString("0.###", c),
                    rig.YawRate.ToString("0.###", c), rig.Sim.Drivetrain.Gear.ToString(c),
                    rig.Sim.Drivetrain.EngineRpm.ToString("0", c),
                    input.Throttle.ToString("0.##", c), input.Brake.ToString("0.##", c),
                    input.Steer.ToString("0.##", c),
                    w[0].Load.ToString("0", c), w[1].Load.ToString("0", c),
                    w[2].Load.ToString("0", c), w[3].Load.ToString("0", c),
                    w[2].SlipRatio.ToString("0.###", c), w[0].SlipAngle.ToString("0.###", c),
                    w[2].ForceLong.ToString("0", c), w[0].ForceLat.ToString("0", c),
                }));
            }
            Console.WriteLine($"  telemetry written to {path}");
        }

        static float PeakTorque(CarConfig c)
        {
            float best = 0f;
            foreach (var (_, torque) in c.TorqueCurve) best = MathF.Max(best, torque);
            return best;
        }

        static float PeakPowerKw(CarConfig c)
        {
            float best = 0f;
            foreach (var (rpm, torque) in c.TorqueCurve)
                best = MathF.Max(best, torque * rpm * Physics.RpmToRadPerSec / 1000f);
            return best;
        }
    }
}
