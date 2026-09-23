using System.Numerics;

namespace CarRace.Vehicle
{
    /// <summary>
    /// Pacejka magic formula coefficients for one axle, plus the load and lag
    /// behaviour that make weight transfer feel like anything.
    /// </summary>
    public struct TyreConfig
    {
        public float Mu;               // peak friction coefficient on reference surface

        // Lateral. B is stiffness, C shape, E curvature. Peak slip angle is
        // roughly tan(pi / 2C) / B, so B ~18 puts the peak near 8 degrees.
        public float LatB, LatC, LatE;
        // Longitudinal. B ~12 with C 1.65 puts the peak near 12% slip ratio.
        public float LongB, LongC, LongE;

        /// <summary>
        /// Peak force falls off as vertical load rises, so a heavily loaded tyre
        /// makes less grip per newton. Without this term weight transfer changes
        /// nothing and the car feels dead. Typical range 0.15 to 0.30.
        /// </summary>
        public float LoadSensitivity;
        public float NominalLoad;      // N, the load the coefficients were taken at

        /// <summary>
        /// Distance over which force builds to its steady value. Forces lag the
        /// slip that causes them, which removes high frequency jitter.
        /// </summary>
        public float RelaxationLength; // m

        public float Radius;           // m, loaded rolling radius
        public float Inertia;          // kg m^2, wheel plus tyre plus brake disc
    }

    /// <summary>
    /// Every tunable number for one car. Plain data with no engine dependency, so
    /// the Unity ScriptableObject is a thin wrapper that returns one of these.
    /// SI units throughout: metres, kilograms, newtons, newton metres, radians.
    /// </summary>
    public sealed class CarConfig
    {
        public string Name = "Unnamed";

        // ---- mass and geometry ------------------------------------------------
        public float Mass = 1500f;
        public float FrontWeightBias = 0.48f;   // share of static weight on the front axle
        public float Wheelbase = 2.65f;
        public float TrackWidth = 1.60f;
        public float CgHeight = 0.45f;

        /// <summary>
        /// Diagonal inertia tensor about the centre of mass, kg m^2.
        /// Set explicitly. A tensor derived automatically from a mesh is always
        /// wrong and gives the car bad rotational feel.
        /// </summary>
        public Vector3 Inertia = new Vector3(600f, 2400f, 500f);

        // ---- suspension -------------------------------------------------------
        public float SpringRateFront = 55000f;  // N/m
        public float SpringRateRear = 62000f;
        public float DamperBumpFront = 4200f;   // N per m/s
        public float DamperReboundFront = 6300f;
        public float DamperBumpRear = 4600f;
        public float DamperReboundRear = 6900f;

        /// <summary>
        /// Extra force proportional to the left/right compression difference on an
        /// axle. This is the primary understeer and oversteer balance knob: stiffen
        /// the front for understeer, the rear for oversteer.
        /// </summary>
        public float AntiRollFront = 18000f;    // N/m of differential compression
        public float AntiRollRear = 14000f;

        /// <summary>Ceiling on damper force, so a kerb or a landing cannot launch the car.</summary>
        public float MaxDamperForce = 18000f;   // N, about five times a static corner load

        /// <summary>
        /// Height above the ground of each axle's roll centre.
        ///
        /// A real suspension keeps the contact patch roughly in place while the body
        /// rolls, and the sprung mass rolls about the roll axis rather than about
        /// the centre of mass. Model the wheel as rigidly bolted to the body instead
        /// and body roll rate swings the patch on a lever the full height of the
        /// centre of mass, which injects slip angle, which makes more lateral force,
        /// which makes more roll. That loop very nearly cancels the dampers: the car
        /// holds a steady cornering state for seconds and then diverges out of
        /// floating point noise alone.
        /// </summary>
        public float RollCentreHeightFront = 0.09f;
        public float RollCentreHeightRear = 0.13f;

        public float SuspensionRestLength = 0.25f;  // m, fully extended
        public float SuspensionTravel = 0.18f;      // m, rest to fully compressed

        // ---- tyres ------------------------------------------------------------
        public TyreConfig TyreFront;
        public TyreConfig TyreRear;

        // ---- engine -----------------------------------------------------------
        /// <summary>Torque curve as (rpm, newton metres), ascending by rpm.</summary>
        public (float Rpm, float Torque)[] TorqueCurve;
        public float IdleRpm = 900f;
        public float RevLimitRpm = 7600f;
        public float EngineInertia = 0.22f;     // kg m^2, crank plus flywheel
        public float EngineBrakingTorque = 45f; // N m at closed throttle, scaled with rpm

        // ---- transmission -----------------------------------------------------
        public float[] GearRatios = { 3.60f, 2.30f, 1.70f, 1.32f, 1.08f, 0.88f };
        public float ReverseRatio = 3.20f;
        public float FinalDrive = 3.44f;
        public float DrivetrainEfficiency = 0.88f;
        public float ShiftTimeSeconds = 0.08f;   // dual clutch
        public DriveLayout Drive = DriveLayout.RearWheelDrive;

        /// <summary>
        /// Most torque the clutch can pass while slipping, and the clutch-side speed
        /// below which it slips. Without this the engine is pinned to idle at a
        /// standstill and the car launches on idle torque, which costs about a
        /// second over 0 to 100.
        /// </summary>
        public float ClutchTorqueCapacity = 300f;   // N m, about 1.15x what the rear tyres can take
        public float ClutchLockRpm = 2600f;         // clutch-side rpm above which it locks
        public float LaunchRpm = 3600f;             // rpm the engine holds while slipping

        // ---- differential -----------------------------------------------------
        public float DiffPreloadTorque = 60f;   // N m resisting any speed difference
        public float DiffPowerRamp = 0.35f;     // locking share under power
        public float DiffCoastRamp = 0.15f;     // locking share off throttle

        // ---- brakes -----------------------------------------------------------
        public float MaxBrakeTorque = 9000f;    // N m total; must exceed the grip limit so ABS has something to modulate
        public float BrakeBias = 0.62f;         // share to the front axle
        public float HandbrakeTorque = 2600f;   // N m on the rear axle only

        // ---- aerodynamics -----------------------------------------------------
        public float DragCdA = 0.67f;           // drag coefficient times frontal area, m^2
        public float LiftFrontClA = 0.12f;      // downforce coefficient times area, front
        public float LiftRearClA = 0.20f;       // rear. Split shifts aero balance with speed.

        // ---- steering ---------------------------------------------------------
        public float MaxSteerAngleDegrees = 33f;
        /// <summary>Speed in m/s at which available steering lock has halved.</summary>
        public float SteerFalloffSpeed = 42f;
        public float SteerRatePerSecond = 3.2f; // how fast the rack can move, full lock per second

        /// <summary>
        /// Front engined rear wheel drive sports car, roughly 420 hp and 1500 kg.
        /// The targets are ordinary published figures for that class and are what
        /// the harness asserts against.
        /// </summary>
        public static CarConfig ReferenceSportsCar()
        {
            var tyre = new TyreConfig
            {
                Mu = 1.10f,
                LatB = 18f, LatC = 1.30f, LatE = 0.97f,
                LongB = 12f, LongC = 1.65f, LongE = 0.90f,
                LoadSensitivity = 0.20f,
                NominalLoad = 3700f,
                RelaxationLength = 0.40f,
                Radius = 0.34f,
                Inertia = 1.40f,
            };

            return new CarConfig
            {
                Name = "Reference Sports Car",
                TyreFront = tyre,
                TyreRear = tyre,
                TorqueCurve = new (float, float)[]
                {
                    (900f, 260f), (2000f, 380f), (3000f, 450f), (4000f, 490f),
                    (5000f, 500f), (6000f, 480f), (7000f, 430f), (7600f, 380f),
                },
            };
        }

        // ---- derived ----------------------------------------------------------
        public float FrontAxleToCg => Wheelbase * (1f - FrontWeightBias);
        public float RearAxleToCg => Wheelbase * FrontWeightBias;
        public float StaticLoadPerWheel(bool front)
            => Mass * Physics.Gravity * (front ? FrontWeightBias : 1f - FrontWeightBias) * 0.5f;

        public float TorqueAtRpm(float rpm)
        {
            var c = TorqueCurve;
            if (c == null || c.Length == 0) return 0f;
            if (rpm <= c[0].Rpm) return c[0].Torque;
            for (int i = 1; i < c.Length; i++)
            {
                if (rpm > c[i].Rpm) continue;
                float t = (rpm - c[i - 1].Rpm) / (c[i].Rpm - c[i - 1].Rpm);
                return c[i - 1].Torque + t * (c[i].Torque - c[i - 1].Torque);
            }
            return c[c.Length - 1].Torque;
        }
    }

    public static class Physics
    {
        public const float Gravity = 9.81f;
        public const float AirDensity = 1.225f;
        public const float RpmToRadPerSec = 0.104719755f;   // 2 pi / 60
        public const float RadPerSecToRpm = 9.54929658f;
    }
}
