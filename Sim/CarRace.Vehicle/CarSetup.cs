using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CarRace.Vehicle
{
    /// <summary>
    /// A driver's setup: the numbers a setup screen offers, kept as values against a car's own
    /// <see cref="CarConfig"/> and applied to a copy of it.
    ///
    /// Only what the model simulates is offered, so every setting changes something real:
    /// springs, dampers and anti-roll bars, brake bias and pressure, the gearing, the limited
    /// slip differential, downforce, and the steering: its lock, how much of it is left at
    /// speed, and how fast the rack moves. Ride height is not offered. The model keeps the car at
    /// CgHeight whatever the springs (VehicleSim.AttachmentHeight), so lowering it would only
    /// cut weight transfer, with no bottoming out and no aero cost to weigh against it, and the
    /// best setting would always be the lowest.
    ///
    /// Downforce is the one setting whose cost is written here rather than simulated: drag
    /// rises with it at LiftToDrag, as it does behind a real wing, so a big wing costs top speed.
    ///
    /// Plain data with no engine dependency, so the harness can sweep it (--setup-sweep) and the
    /// game can save it as text.
    /// </summary>
    public sealed class CarSetup
    {
        public enum Group { Suspension, Brakes, Gearbox, Differential, Aero, Steering }

        /// <summary>Downforce coefficient gained for each unit of drag coefficient added, about
        /// what a road car's rear wing manages. A racing wing does better, a lip spoiler worse.</summary>
        public const float LiftToDrag = 4f;

        /// <summary>Closest two neighbouring gear ratios may come, so the gears stay in order and
        /// an upshift always lowers the revs.</summary>
        public const float GearGap = 0.05f;

        /// <summary>Kilograms of downforce per unit of ClA at 200 km/h, how downforce is shown.
        /// A coefficient times an area means little to a driver; a weight at a speed does.</summary>
        public static readonly float KgAt200 =
            0.5f * Physics.AirDensity * (200f / 3.6f) * (200f / 3.6f) / Physics.Gravity;

        /// <summary>One adjustable number: what it is called, where it lives in the config, and how
        /// far it may move from the car's own value.</summary>
        public sealed class Setting
        {
            /// <summary>The name it is saved under. Never rename one, or saved setups lose it.</summary>
            public readonly string Key;
            public readonly Group Group;
            public readonly string Label;
            public readonly string Unit;
            /// <summary>Shown value per stored value: a spring rate stored in N/m shows in N/mm at 0.001.</summary>
            public readonly float Display;
            public readonly int Decimals;
            /// <summary>One press of minus or plus, in stored units.</summary>
            public readonly float Step;
            public readonly string Hint;

            internal readonly int Gear;      // which gear's ratio, from 0, or -1
            internal readonly Func<CarConfig, float> Read;
            internal readonly Action<CarConfig, float> Write;
            internal readonly Func<float, float> Low, High;   // given the car's own value
            internal int Index;

            internal Setting(string key, Group group, string label, string unit, float display, int decimals,
                             float step, string hint, Func<CarConfig, float> read, Action<CarConfig, float> write,
                             Func<float, float> low, Func<float, float> high, int gear = -1)
            {
                Key = key; Group = group; Label = label; Unit = unit; Display = display; Decimals = decimals;
                Step = step; Hint = hint; Read = read; Write = write; Low = low; High = high; Gear = gear;
            }

            /// <summary>A value as the screen shows it, in <see cref="Unit"/>.</summary>
            public string Format(float value)
                => (value * Display).ToString("F" + Decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>The car the setup is a change to. Never modified.</summary>
        public readonly CarConfig Baseline;
        public readonly IReadOnlyList<Setting> Settings;
        readonly float[] _values;

        public CarSetup(CarConfig baseline)
        {
            Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
            List<Setting> table = Table(baseline);
            for (int i = 0; i < table.Count; i++) table[i].Index = i;
            Settings = table;
            _values = new float[table.Count];
            Reset();
        }

        public Setting Find(string key)
        {
            foreach (Setting s in Settings)
                if (s.Key == key) return s;
            return null;
        }

        public float Default(Setting s) => s.Read(Baseline);
        public float Get(Setting s) => _values[s.Index];
        public bool IsDefault(Setting s) => Get(s) == Default(s);

        public bool AllDefault
        {
            get
            {
                foreach (Setting s in Settings)
                    if (!IsDefault(s)) return false;
                return true;
            }
        }

        /// <summary>Lowest the setting may go now: its range, and for a gear the next gear's
        /// ratio, so the gears stay in order. Never above the car's own value.</summary>
        public float Min(Setting s)
        {
            float own = Default(s);
            float min = MathF.Min(s.Low(own), own);
            if (s.Gear >= 0 && s.Gear + 1 < Baseline.GearRatios.Length)
                min = MathF.Max(min, _values[s.Index + 1] + GearGap);
            return min;
        }

        /// <summary>Highest the setting may go now: its range, and for a gear the gear before's
        /// ratio. Never below the car's own value.</summary>
        public float Max(Setting s)
        {
            float own = Default(s);
            float max = MathF.Max(s.High(own), own);
            if (s.Gear > 0) max = MathF.Min(max, _values[s.Index - 1] - GearGap);
            return max;
        }

        /// <summary>
        /// Sets a value, kept inside the range and on whole steps counted from the car's own
        /// value. Counting from there rather than adding steps up means minus then plus lands
        /// back on the default exactly, not a rounding error away from it, so the screen can
        /// still say the setting is untouched.
        /// </summary>
        public void Set(Setting s, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            float own = Default(s);
            float stepped = own + MathF.Round((value - own) / s.Step) * s.Step;
            _values[s.Index] = Clamp(stepped, Min(s), Max(s));
        }

        /// <summary>Presses of plus (positive) or minus (negative).</summary>
        public void Nudge(Setting s, int presses) => Set(s, Get(s) + presses * s.Step);

        public void Reset()
        {
            foreach (Setting s in Settings) _values[s.Index] = Default(s);
        }

        public void Reset(Group group)
        {
            foreach (Setting s in Settings)
                if (s.Group == group) _values[s.Index] = Default(s);
        }

        /// <summary>
        /// The car with this setup: a copy of the baseline with every setting written into it,
        /// and drag raised by any downforce added (or lowered by any taken away). With every
        /// setting at default the copy is the baseline value for value, so a default setup
        /// drives exactly the car the harness validates.
        /// </summary>
        public CarConfig Apply()
        {
            CarConfig car = Baseline.Clone();
            foreach (Setting s in Settings) s.Write(car, _values[s.Index]);
            float added = (car.LiftFrontClA - Baseline.LiftFrontClA) + (car.LiftRearClA - Baseline.LiftRearClA);
            car.DragCdA = MathF.Max(Baseline.DragCdA + added / LiftToDrag, 0.05f);
            return car;
        }

        /// <summary>
        /// The setup as text, one key=value a line, changed settings only. Untouched settings
        /// are left out on purpose: if the car itself is retuned later, they follow its new
        /// values instead of pinning the old ones.
        /// </summary>
        public string ToText()
        {
            var text = new StringBuilder();
            foreach (Setting s in Settings)
            {
                if (IsDefault(s)) continue;
                text.Append(s.Key).Append('=')
                    .Append(Get(s).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>
        /// A setup read back from <see cref="ToText"/>. Keys this version does not know are
        /// skipped, values outside today's ranges are brought inside them, and missing keys
        /// stay at default, so an old or hand-edited save always gives a drivable car.
        ///
        /// Values go in first and the gears are put in order afterwards. Setting them one at a
        /// time would judge each gear against its neighbours' defaults rather than their saved
        /// values, and clamp a perfectly good saved gearbox.
        /// </summary>
        public static CarSetup FromText(CarConfig baseline, string text)
        {
            var setup = new CarSetup(baseline);
            if (string.IsNullOrEmpty(text)) return setup;

            foreach (string line in text.Split('\n'))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                Setting s = setup.Find(line.Substring(0, equals).Trim());
                if (s == null) continue;
                if (!float.TryParse(line.Substring(equals + 1).Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out float value)) continue;
                if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                float own = setup.Default(s);
                setup._values[s.Index] = Clamp(value, MathF.Min(s.Low(own), own), MathF.Max(s.High(own), own));
            }

            // Gears in order, top gear up: each at least GearGap under the one before it.
            Setting previous = null;
            foreach (Setting s in setup.Settings)
            {
                if (s.Gear < 0) continue;
                if (previous != null && setup._values[s.Index] > setup._values[previous.Index] - GearGap)
                    setup._values[s.Index] = setup._values[previous.Index] - GearGap;
                previous = s;
            }
            return setup;
        }

        // ---- readouts -----------------------------------------------------------

        /// <summary>Speed at the rev limit in top gear, km/h: where the gearing rather than drag
        /// stops the car.</summary>
        public static float GearedTopSpeedKph(CarConfig car)
        {
            float radius = car.Drive == DriveLayout.FrontWheelDrive ? car.TyreFront.Radius : car.TyreRear.Radius;
            float overall = car.GearRatios[car.GearRatios.Length - 1] * car.FinalDrive;
            return car.RevLimitRpm * Physics.RpmToRadPerSec / overall * radius * 3.6f;
        }

        /// <summary>
        /// The radius the car's centre of mass turns on at full lock, metres, rolling at
        /// <paramref name="speedMs"/> (the lock left after the speed sensitivity takes its
        /// share): the rear axle's turning centre (wheelbase over the tangent of the lock) and
        /// the centre of mass's distance ahead of the rear axle, which the weight split gives.
        /// The setup screen shows it barely rolling; the sweep drives it at 4 m/s and agrees
        /// within a few per cent, the tyres' slip angles at walking pace being small.
        /// </summary>
        public static float TurningRadiusM(CarConfig car, float speedMs = 0f)
        {
            float falloff = 1f / (1f + speedMs / MathF.Max(car.SteerFalloffSpeed, 0.01f));
            float lock_ = car.MaxSteerAngleDegrees * falloff * MathF.PI / 180f;
            float toRear = car.Wheelbase * car.FrontWeightBias;
            float rear = car.Wheelbase / MathF.Tan(MathF.Max(lock_, 0.01f));
            return MathF.Sqrt(rear * rear + toRear * toRear);
        }

        /// <summary>Share of the downforce on the front axle, the high speed balance.</summary>
        public static float AeroBalanceFront(CarConfig car)
        {
            float total = car.LiftFrontClA + car.LiftRearClA;
            return total > 1e-6f ? car.LiftFrontClA / total : 0.5f;
        }

        // ---- the table ----------------------------------------------------------

        const string BumpHint = "How hard the damper resists the wheel rising: firmer settles the car sooner, but rides kerbs harder.";
        const string ReboundHint = "How hard the damper resists the wheel dropping back: firmer is steadier, but slower to follow a dip.";

        /// <summary>
        /// Every setting, with the range it may move over. The ranges scale with the car's own
        /// value, so another car gets sensible limits without a table of its own, and they are
        /// the ranges the harness sweep has checked.
        ///
        /// Springs, bars and downforce are narrower than any one of them needs, because they
        /// stack. Each alone was sound over far wider ranges (springs 0.7 to 1.6 times the car's
        /// own, bars from none to 2.5 times, downforce from none), but all of them set for
        /// oversteer at once put 18% of the roll stiffness on the front axle and all of the
        /// downforce, and the car spun in a 0.5 g corner at 180 km/h with the wheel held still.
        /// Neither half did it alone: the roll stiffness at 18% front, or the downforce at 80%
        /// front, each held that corner; together they did not. At these ranges the worst
        /// combination holds it with under 3 degrees of sideslip, against 1.3 for the car as built.
        /// </summary>
        static List<Setting> Table(CarConfig car)
        {
            var table = new List<Setting>
            {
                new Setting("springFront", Group.Suspension, "Springs, front", "N/mm", 0.001f, 1, 2500f,
                    "Stiffer: less roll and pitch, and more understeer.",
                    c => c.SpringRateFront, (c, v) => c.SpringRateFront = v, own => own * 0.8f, own => own * 1.4f),
                new Setting("springRear", Group.Suspension, "Springs, rear", "N/mm", 0.001f, 1, 2500f,
                    "Stiffer: less roll and squat, and more oversteer.",
                    c => c.SpringRateRear, (c, v) => c.SpringRateRear = v, own => own * 0.8f, own => own * 1.4f),
                new Setting("bumpFront", Group.Suspension, "Bump, front", "N s/m", 1f, 0, 200f, BumpHint,
                    c => c.DamperBumpFront, (c, v) => c.DamperBumpFront = v, own => own * 0.5f, own => own * 2f),
                new Setting("reboundFront", Group.Suspension, "Rebound, front", "N s/m", 1f, 0, 200f, ReboundHint,
                    c => c.DamperReboundFront, (c, v) => c.DamperReboundFront = v, own => own * 0.5f, own => own * 2f),
                new Setting("bumpRear", Group.Suspension, "Bump, rear", "N s/m", 1f, 0, 200f, BumpHint,
                    c => c.DamperBumpRear, (c, v) => c.DamperBumpRear = v, own => own * 0.5f, own => own * 2f),
                new Setting("reboundRear", Group.Suspension, "Rebound, rear", "N s/m", 1f, 0, 200f, ReboundHint,
                    c => c.DamperReboundRear, (c, v) => c.DamperReboundRear = v, own => own * 0.5f, own => own * 2f),
                new Setting("barFront", Group.Suspension, "Anti-roll bar, front", "N/mm", 0.001f, 0, 1000f,
                    "The main balance setting. Stiffer at the front: more understeer.",
                    c => c.AntiRollFront, (c, v) => c.AntiRollFront = v, own => own * 0.5f, own => own * 2f),
                new Setting("barRear", Group.Suspension, "Anti-roll bar, rear", "N/mm", 0.001f, 0, 1000f,
                    "The main balance setting. Stiffer at the rear: more oversteer.",
                    c => c.AntiRollRear, (c, v) => c.AntiRollRear = v, own => own * 0.5f, own => own * 2f),

                new Setting("brakeBias", Group.Brakes, "Brake bias", "% front", 100f, 0, 0.01f,
                    "Share of braking at the front. With ABS on it matters little; without ABS, too far back locks the rear first.",
                    c => c.BrakeBias, (c, v) => c.BrakeBias = v, own => 0.5f, own => 0.75f),
                new Setting("brakePressure", Group.Brakes, "Brake pressure", "%", 100f, 0, 0.05f,
                    "Braking at a full pedal. Less helps on a keyboard without ABS, where the brake is all or nothing.",
                    c => 1f, (c, v) => c.MaxBrakeTorque *= v, own => 0.6f, own => 1f),

                new Setting("finalDrive", Group.Gearbox, "Final drive", "", 1f, 2, 0.02f,
                    "Higher: harder acceleration and a lower top speed in top gear. Lower: the reverse.",
                    c => c.FinalDrive, (c, v) => c.FinalDrive = v, own => own * 0.84f, own => own * 1.22f),
            };

            for (int g = 0; g < car.GearRatios.Length; g++)
            {
                int gear = g;
                table.Add(new Setting($"gear{gear + 1}", Group.Gearbox, $"Gear {gear + 1}", "", 1f, 2, 0.02f,
                    "A higher ratio pulls harder in this gear; a lower one runs longer before the next.",
                    c => c.GearRatios[gear], (c, v) => c.GearRatios[gear] = v,
                    own => own * 0.75f, own => own * 1.25f, gear));
            }

            table.Add(new Setting("diffPreload", Group.Differential, "Preload", "N m", 1f, 0, 5f,
                "Locking that is always there: steadier on and off the throttle, more push in slow corners.",
                c => c.DiffPreloadTorque, (c, v) => c.DiffPreloadTorque = v, own => 0f, own => 200f));
            table.Add(new Setting("diffPower", Group.Differential, "Lock on power", "%", 100f, 0, 0.05f,
                "Locking under power: more traction out of corners, more push on the way out.",
                c => c.DiffPowerRamp, (c, v) => c.DiffPowerRamp = v, own => 0f, own => 1f));
            table.Add(new Setting("diffCoast", Group.Differential, "Lock off power", "%", 100f, 0, 0.05f,
                "Locking off the throttle: steadier into corners, less willing to turn in.",
                c => c.DiffCoastRamp, (c, v) => c.DiffCoastRamp = v, own => 0f, own => 1f));

            table.Add(new Setting("downforceFront", Group.Aero, "Downforce, front", "kg at 200 km/h", KgAt200, 0, 0.02f,
                "Front grip in fast corners: more than the rear turns in harder at speed. Every kilogram adds drag.",
                c => c.LiftFrontClA, (c, v) => c.LiftFrontClA = v, own => own * 0.5f, own => own * 2f));
            table.Add(new Setting("downforceRear", Group.Aero, "Downforce, rear", "kg at 200 km/h", KgAt200, 0, 0.02f,
                "Rear grip in fast corners and under braking: more than the front is steadier at speed. Every kilogram adds drag.",
                c => c.LiftRearClA, (c, v) => c.LiftRearClA = v, own => own * 0.75f, own => own * 3f));

            // Steering. Absolute ranges rather than shares of the car's own value, so every body's
            // own defaults (the game's) sit inside the same checked ranges.
            table.Add(new Setting("steerLock", Group.Steering, "Steering lock", "deg", 1f, 1, 0.5f,
                "More lock turns tighter at low speed: a smaller turning radius for hairpins. On a pad or wheel it also makes the steering quicker round the centre.",
                c => c.MaxSteerAngleDegrees, (c, v) => c.MaxSteerAngleDegrees = v, own => 26f, own => 40f));
            table.Add(new Setting("steerFalloff", Group.Steering, "Speed sensitivity", "km/h at half lock", 3.6f, 0, 5f / 3.6f,
                "The speed by which the steering has lost half its lock. Higher keeps more lock at speed: sharper turn-in in fast corners, and easier to spin.",
                c => c.SteerFalloffSpeed, (c, v) => c.SteerFalloffSpeed = v, own => 30f, own => 60f));
            table.Add(new Setting("steerRate", Group.Steering, "Steering speed", "locks/s", 1f, 1, 0.1f,
                "How fast the wheels can turn, in full locks a second. Quicker reacts sooner; slower is calmer and smooths a keyboard's jabs.",
                c => c.SteerRatePerSecond, (c, v) => c.SteerRatePerSecond = v, own => 2f, own => 5f));

            return table;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
