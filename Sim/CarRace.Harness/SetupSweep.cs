using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// The setup screen's ranges, checked: every setting at both ends of its range, the rest at
    /// default, through the validation scenarios, and then the worst combinations. --setup-sweep.
    ///
    /// It asks four things. Is the car sound in normal driving with any setup a player can
    /// reach: no NaN, no spin holding a steady corner at 0.5 to 0.6 g slow or fast, none braking
    /// hard from 200 km/h, no suspension at full travel, and a gentle nudge of the wheel at
    /// 250 km/h that dies away by itself. Is any one change as forgiving as the car as built:
    /// the straight line check's hard jab of steering at 144 km/h, hands off, must still damp
    /// out. Does each setting move the car the way its hint says: a stiffer spring rolls less,
    /// a stiffer rear bar turns in more, a higher final drive tops out sooner, front downforce
    /// turns in harder at speed and rear downforce less, and downforce costs top speed. And
    /// does a setup left at default reproduce the five checks exactly, since it is meant to be
    /// the baseline value for value.
    ///
    /// The jab is not asked of the combinations on purpose. A car set up for oversteer on every
    /// setting at once may fairly spin after a 5 degree jab at 144 km/h with nobody holding the
    /// wheel, as a real one would; what it may not do is spin in a corner it is only half way
    /// to the limit of, which is how the first, wider ranges failed.
    ///
    /// A range that fails the first two is narrowed in CarSetup; a setting that fails the
    /// third is wrong or mislabelled, and the hint the screen shows would be a lie.
    /// </summary>
    public static class SetupSweep
    {
        // Steady corners well inside the limit, about 0.6 g on the baseline: slow, where the
        // springs and bars set the balance, and fast, where downforce does. Below the limit,
        // because there the yaw rate at a fixed steer measures balance directly; at the limit
        // it only says which axle let go first.
        const float SlowMs = 25f, SlowSteer = 0.08f;
        const float FastMs = 50f, FastSteer = 0.025f;

        struct Corner
        {
            public float YawRate, LateralG, RollDeg, Sideslip, Compression;
            public bool Spun;
            public float RollPerG => LateralG > 0.05f ? RollDeg / LateralG : 0f;
        }

        sealed class Measures
        {
            public float ZeroToHundred, Skidpad, Braking, TopSpeed;
            public bool StraightStable;
            public string StraightDetail;
            public Corner Slow, Fast;
            public float BrakingSideslip, BrakingCompression;
            public float NudgeYaw, NudgeSideslip;

            public float Compression => MathF.Max(BrakingCompression, MathF.Max(Slow.Compression, Fast.Compression));
            public float Sideslip => MathF.Max(MathF.Max(BrakingSideslip, NudgeSideslip), MathF.Max(Slow.Sideslip, Fast.Sideslip));

            public bool Finite => IsFinite(ZeroToHundred) && IsFinite(Skidpad) && IsFinite(Braking)
                               && IsFinite(TopSpeed) && IsFinite(Slow.YawRate) && IsFinite(Fast.YawRate)
                               && IsFinite(Slow.RollDeg) && IsFinite(BrakingSideslip) && IsFinite(NudgeYaw);
        }

        public static int Run(CarConfig baseline)
        {
            var table = new CarSetup(baseline);
            int n = table.Settings.Count;

            // Case 0 is the baseline itself, case 1 a setup left at default, then each setting at
            // its minimum and its maximum.
            var configs = new CarConfig[2 + 2 * n];
            var values = new float[2 + 2 * n];
            configs[0] = baseline;
            configs[1] = new CarSetup(baseline).Apply();
            for (int i = 0; i < n; i++)
            {
                for (int end = 0; end < 2; end++)
                {
                    var setup = new CarSetup(baseline);
                    CarSetup.Setting s = setup.Settings[i];
                    setup.Set(s, end == 0 ? setup.Min(s) : setup.Max(s));
                    configs[2 + 2 * i + end] = setup.Apply();
                    values[2 + 2 * i + end] = setup.Get(s);
                }
            }

            // Then the corners of the range a player can reach by combining settings, judged on
            // normal driving only: one setting at a time cannot show two that are each fine
            // alone and not together, which is what the first ranges hid.
            var combinations = new (string Name, (string Key, bool High)[] Ends)[]
            {
                ("all soft, most downforce", new[] { ("springFront", false), ("springRear", false), ("bumpFront", false),
                    ("reboundFront", false), ("bumpRear", false), ("reboundRear", false), ("barFront", false),
                    ("barRear", false), ("downforceFront", true), ("downforceRear", true) }),
                ("all stiff", new[] { ("springFront", true), ("springRear", true), ("bumpFront", true),
                    ("reboundFront", true), ("bumpRear", true), ("reboundRear", true), ("barFront", true),
                    ("barRear", true) }),
                ("all for oversteer", new[] { ("springFront", false), ("springRear", true), ("barFront", false),
                    ("barRear", true), ("downforceFront", true), ("downforceRear", false), ("diffPower", true),
                    ("diffCoast", false), ("diffPreload", false), ("brakeBias", false) }),
            };
            Array.Resize(ref configs, configs.Length + combinations.Length);
            for (int c = 0; c < combinations.Length; c++)
            {
                var setup = new CarSetup(baseline);
                foreach (var (key, high) in combinations[c].Ends)
                {
                    CarSetup.Setting s = setup.Find(key);
                    setup.Set(s, high ? setup.Max(s) : setup.Min(s));
                }
                configs[2 + 2 * n + c] = setup.Apply();
            }

            var started = DateTime.Now;
            var results = new Measures[configs.Length];
            Parallel.For(0, configs.Length, k => results[k] = Measure(configs[k]));
            Measures basis = results[0];

            Console.WriteLine($"  {configs.Length} cases in {(DateTime.Now - started).TotalSeconds:0} s\n");
            Console.WriteLine($"  {"setting",-22} {"end",-4} {"value",14} {"0-100",6} {"skid g",6} {"brake m",7} "
                            + $"{"top",6} {"yaw slow",8} {"roll/g",6} {"yaw fast",8} {"slip",5} {"travel",6}   result");
            Console.WriteLine("  " + new string('-', 118));

            bool allPassed = true;
            var failures = new List<string>();

            // A setup left at default must be the baseline value for value.
            Measures plain = results[1];
            bool same = plain.ZeroToHundred == basis.ZeroToHundred && plain.Skidpad == basis.Skidpad
                     && plain.Braking == basis.Braking && plain.TopSpeed == basis.TopSpeed
                     && plain.StraightDetail == basis.StraightDetail;
            Row("baseline", "", "", basis, Sound(basis, basis, out _));
            Row("default setup", "", "", plain, same);
            if (!same) failures.Add("a default setup does not reproduce the baseline's five checks exactly");
            allPassed &= same;

            for (int i = 0; i < n; i++)
            {
                CarSetup.Setting s = table.Settings[i];
                for (int end = 0; end < 2; end++)
                {
                    int k = 2 + 2 * i + end;
                    bool sound = Sound(results[k], basis, out string why);
                    if (!sound) failures.Add($"{s.Label} at its {(end == 0 ? "minimum" : "maximum")}: {why}");
                    allPassed &= sound;
                    Row(s.Label, end == 0 ? "min" : "max", $"{s.Format(values[k])} {s.Unit}".Trim(), results[k], sound);
                }
            }
            for (int c = 0; c < combinations.Length; c++)
            {
                Measures m = results[2 + 2 * n + c];
                bool sound = Normal(m, basis, out string why);
                if (!sound) failures.Add($"{combinations[c].Name}: {why}");
                allPassed &= sound;
                Row(combinations[c].Name, "", "", m, sound);
            }

            // Each setting must push the car the way its hint says it does.
            Console.WriteLine("\n  effects, minimum against maximum:");
            int lastGear = baseline.GearRatios.Length;
            for (int i = 0; i < n; i++)
            {
                CarSetup.Setting s = table.Settings[i];
                Measures low = results[2 + 2 * i], high = results[3 + 2 * i];
                string expect = null;
                bool ok = true;
                switch (s.Key)
                {
                    case "springFront":
                    case "springRear":
                        expect = $"stiffer rolls less: {low.Slow.RollPerG:0.00} to {high.Slow.RollPerG:0.00} deg/g";
                        ok = high.Slow.RollPerG < low.Slow.RollPerG;
                        break;
                    case "barFront":
                        expect = $"stiffer understeers, less yaw: {low.Slow.YawRate:0.0000} to {high.Slow.YawRate:0.0000} rad/s";
                        ok = high.Slow.YawRate < low.Slow.YawRate;
                        break;
                    case "barRear":
                        expect = $"stiffer oversteers, more yaw: {low.Slow.YawRate:0.0000} to {high.Slow.YawRate:0.0000} rad/s";
                        ok = high.Slow.YawRate > low.Slow.YawRate;
                        break;
                    case "brakePressure":
                        expect = $"less pressure stops longer: {low.Braking:0.00} to {high.Braking:0.00} m";
                        ok = low.Braking > high.Braking;
                        break;
                    case "finalDrive":
                        expect = $"higher tops out sooner: {low.TopSpeed:0.0} to {high.TopSpeed:0.0} km/h";
                        ok = high.TopSpeed < low.TopSpeed;
                        break;
                    case "downforceFront":
                        expect = $"turns in harder at speed and costs top speed: yaw {low.Fast.YawRate:0.0000} to "
                               + $"{high.Fast.YawRate:0.0000} rad/s, top {low.TopSpeed:0.0} to {high.TopSpeed:0.0} km/h";
                        ok = high.Fast.YawRate > low.Fast.YawRate && high.TopSpeed < low.TopSpeed;
                        break;
                    case "downforceRear":
                        expect = $"steadier at speed and costs top speed: yaw {low.Fast.YawRate:0.0000} to "
                               + $"{high.Fast.YawRate:0.0000} rad/s, top {low.TopSpeed:0.0} to {high.TopSpeed:0.0} km/h";
                        ok = high.Fast.YawRate < low.Fast.YawRate && high.TopSpeed < low.TopSpeed;
                        break;
                    default:
                        if (s.Key == $"gear{lastGear}")
                        {
                            expect = $"a higher top gear tops out sooner: {low.TopSpeed:0.0} to {high.TopSpeed:0.0} km/h";
                            ok = high.TopSpeed < low.TopSpeed;
                        }
                        break;
                }
                if (expect == null) continue;
                Console.WriteLine($"    {s.Label,-22} {expect,-100} {(ok ? "PASS" : "FAIL")}");
                if (!ok) failures.Add($"{s.Label} does not do what its hint says ({expect})");
                allPassed &= ok;
            }

            Console.WriteLine();
            foreach (string failure in failures) Console.WriteLine($"  FAIL: {failure}");
            Console.WriteLine($"\n  {(allPassed ? "Setup sweep: PASS." : "Setup sweep: FAILED.")}\n");
            return allPassed ? 0 : 1;
        }

        /// <summary>A single setting at an end of its range: as forgiving as the car as built, so
        /// the straight line check's jab must damp out as well as everything normal driving asks.</summary>
        static bool Sound(Measures m, Measures basis, out string why)
        {
            if (!Normal(m, basis, out why)) return false;
            if (!m.StraightStable) why = $"straight line stability lost ({m.StraightDetail})";
            return why == null;
        }

        /// <summary>Whether the car is sound in normal driving with this setup, and if not, why.
        /// Sideslip limits sit well above anything a sound car shows (1.1 to 2.4 degrees in the
        /// corners, 0.6 for the nudge) and well below a spin.</summary>
        static bool Normal(Measures m, Measures basis, out string why)
        {
            why = null;
            if (!m.Finite) why = "a NaN or infinite result";
            else if (m.Slow.Spun || m.Fast.Spun) why = "spun holding a steady corner";
            else if (MathF.Max(m.Slow.Sideslip, m.Fast.Sideslip) > 6f)
                why = $"{MathF.Max(m.Slow.Sideslip, m.Fast.Sideslip):0.0} deg of sideslip in a steady corner at 0.5 to 0.6 g";
            else if (m.BrakingSideslip > 5f) why = $"{m.BrakingSideslip:0.0} deg of sideslip braking from 200 km/h";
            else if (m.NudgeYaw > 0.01f || m.NudgeSideslip > 3f)
                why = $"a nudge at 250 km/h did not die away (yaw {m.NudgeYaw:0.000} rad/s, sideslip {m.NudgeSideslip:0.0} deg)";
            else if (m.Compression >= 0.999f) why = "suspension at full travel";
            else if (m.ZeroToHundred > basis.ZeroToHundred * 1.5f) why = $"0 to 100 in {m.ZeroToHundred:0.00} s";
            else if (m.Skidpad < basis.Skidpad * 0.8f) why = $"skidpad down to {m.Skidpad:0.00} g";
            else if (m.Braking > basis.Braking * 1.8f) why = $"braking from 100 takes {m.Braking:0.0} m";
            return why == null;
        }

        static void Row(string setting, string end, string value, Measures m, bool passed)
        {
            Console.WriteLine($"  {setting,-22} {end,-4} {value,14} {m.ZeroToHundred,6:0.00} {m.Skidpad,6:0.000} "
                            + $"{m.Braking,7:0.00} {m.TopSpeed,6:0.0} {m.Slow.YawRate,8:0.0000} {m.Slow.RollPerG,6:0.00} "
                            + $"{m.Fast.YawRate,8:0.0000} {m.Sideslip,5:0.0} "
                            + $"{m.Compression * 100f,5:0}%   {(passed ? "PASS" : "FAIL")}");
        }

        static Measures Measure(CarConfig config)
        {
            var m = new Measures
            {
                ZeroToHundred = Program.ZeroToHundred(config),
                Skidpad = Program.Skidpad(config),
                Braking = Program.BrakingDistance(config),
                TopSpeed = Program.TopSpeed(config),
                Slow = SteadyCorner(config, SlowMs, SlowSteer),
                Fast = SteadyCorner(config, FastMs, FastSteer),
            };
            (m.StraightStable, m.StraightDetail) = Program.StraightLineStability(config);
            (m.BrakingSideslip, m.BrakingCompression) = HardBraking(config);
            (m.NudgeYaw, m.NudgeSideslip) = Nudge(config);
            return m;
        }

        /// <summary>
        /// A gentle nudge of the wheel at 250 km/h, about a third of a degree at the road wheels
        /// for 0.3 s, then hands off for 4 s at a held speed: the yaw rate left at the end, and the
        /// most sideslip on the way. Small on purpose. A car that is unstable at speed diverges
        /// from any disturbance at all, so a small one tells stable from unstable without asking
        /// the tyres for more than they have, which a big one would.
        /// </summary>
        static (float yaw, float sideslip) Nudge(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs();
            const float Speed = 250f / 3.6f;
            while (rig.ForwardSpeed < Speed * 0.995f && rig.Time < 90f)
            {
                rig.HoldSpeed(ref input, Speed);
                input.Steer = 0f;
                rig.Step(input);
            }
            float pulseEnds = rig.Time + 1.3f, end = pulseEnds + 4f, sideslip = 0f;
            while (rig.Time < end)
            {
                rig.HoldSpeed(ref input, Speed);
                input.Steer = rig.Time > pulseEnds - 0.3f && rig.Time < pulseEnds ? 0.02f : 0f;
                rig.Step(input);
                sideslip = MathF.Max(sideslip, MathF.Abs(rig.SideslipDegrees));
            }
            return (MathF.Abs(rig.YawRate), sideslip);
        }

        /// <summary>
        /// A fixed steer at a held speed, eased in over 2 s and measured over the 2 s after it
        /// has settled, as CornerSweep does. No yaw controller, so whatever the car does with the
        /// steer it is given is the car.
        /// </summary>
        static Corner SteadyCorner(CarConfig config, float speed, float steer)
        {
            var rig = new Rig(config);
            rig.Settle();
            var input = new VehicleInputs();
            while (rig.ForwardSpeed < speed * 0.99f && rig.Time < 40f)
            {
                rig.HoldSpeed(ref input, speed);
                input.Steer = 0f;
                rig.Step(input);
            }

            var corner = new Corner();
            float start = rig.Time, measure = start + 4f, finish = measure + 2f;
            float yaw = 0f, g = 0f, roll = 0f;
            int samples = 0;
            while (rig.Time < finish)
            {
                rig.HoldSpeed(ref input, speed);
                input.Steer = steer * MathF.Min((rig.Time - start) / 2f, 1f);
                rig.Step(input);

                corner.Compression = MathF.Max(corner.Compression, Compression(rig));
                float slip = MathF.Abs(rig.SideslipDegrees);
                corner.Sideslip = MathF.Max(corner.Sideslip, slip);
                if (slip > 25f) { corner.Spun = true; break; }
                if (rig.Time < measure) continue;

                yaw += MathF.Abs(rig.YawRate);
                g += rig.LateralG;
                roll += RollDegrees(rig);
                samples++;
            }
            if (samples > 0)
            {
                corner.YawRate = yaw / samples;
                corner.LateralG = g / samples;
                corner.RollDeg = roll / samples;
            }
            return corner;
        }

        /// <summary>Full braking from 200 km/h with ABS: the most sideslip on the way down and the
        /// deepest any wheel's suspension goes, as a share of its travel.</summary>
        static (float sideslip, float compression) HardBraking(CarConfig config)
        {
            var rig = new Rig(config);
            rig.Settle();
            var accelerate = new VehicleInputs { Throttle = 1f };
            while (rig.ForwardSpeed < 200f / 3.6f && rig.Time < 60f) rig.Step(accelerate);

            var brake = new VehicleInputs { Brake = 1f, Clutch = true };
            float sideslip = 0f, compression = 0f, deadline = rig.Time + 20f;
            while (rig.ForwardSpeed > 0.5f && rig.Time < deadline)
            {
                rig.Step(brake);
                sideslip = MathF.Max(sideslip, MathF.Abs(rig.SideslipDegrees));
                compression = MathF.Max(compression, Compression(rig));
            }
            return (sideslip, compression);
        }

        static float Compression(Rig rig)
        {
            float deepest = 0f;
            foreach (Wheel w in rig.Sim.Wheels)
                deepest = MathF.Max(deepest, w.Compression / rig.Sim.Config.SuspensionTravel);
            return deepest;
        }

        /// <summary>Body roll, degrees either way: how far the car's right-hand side has tipped
        /// out of the horizontal.</summary>
        static float RollDegrees(Rig rig)
        {
            float tip = Vector3.Transform(Vector3.UnitX, rig.Body.State.Orientation).Y;
            return MathF.Abs(MathF.Asin(MathF.Max(-1f, MathF.Min(1f, tip)))) * 180f / MathF.PI;
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
