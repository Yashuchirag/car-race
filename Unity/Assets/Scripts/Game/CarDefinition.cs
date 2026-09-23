using System;
using UnityEngine;
using CarRace.Vehicle;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Editor-facing copy of <see cref="CarConfig"/>. Every number is mirrored as a
    /// serialized field so a car can be tuned in the inspector, and ToConfig hands
    /// the model plain data with no engine types in it.
    ///
    /// The fields are duplicated rather than serializing CarConfig directly because
    /// Unity's serializer cannot see value tuples or System.Numerics.Vector3, and
    /// because the model must stay copyable into Unity unchanged. The defaults below
    /// are CarConfig.ReferenceSportsCar, so a freshly created asset is the car the
    /// headless harness validates.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCar", menuName = "CarRace/Car Definition")]
    public sealed class CarDefinition : ScriptableObject
    {
        [Serializable]
        public struct TyreData
        {
            public float mu;
            public float latB, latC, latE;
            public float longB, longC, longE;
            public float loadSensitivity;
            public float nominalLoad;
            public float relaxationLength;
            public float radius;
            public float inertia;

            public static TyreData Reference => new TyreData
            {
                mu = 1.10f,
                latB = 18f, latC = 1.30f, latE = 0.97f,
                longB = 12f, longC = 1.65f, longE = 0.90f,
                loadSensitivity = 0.20f,
                nominalLoad = 3700f,
                relaxationLength = 0.40f,
                radius = 0.34f,
                inertia = 1.40f,
            };

            public TyreConfig ToConfig() => new TyreConfig
            {
                Mu = mu,
                LatB = latB, LatC = latC, LatE = latE,
                LongB = longB, LongC = longC, LongE = longE,
                LoadSensitivity = loadSensitivity,
                NominalLoad = nominalLoad,
                RelaxationLength = relaxationLength,
                Radius = radius,
                Inertia = inertia,
            };
        }

        [Serializable]
        public struct TorquePoint
        {
            public float rpm;
            public float newtonMetres;
        }

        [Header("Identity")]
        public string carName = "Reference Sports Car";

        [Header("Mass and geometry")]
        public float mass = 1500f;
        [Range(0.3f, 0.7f)] public float frontWeightBias = 0.48f;
        public float wheelbase = 2.65f;
        public float trackWidth = 1.60f;
        public float cgHeight = 0.45f;
        [Tooltip("Diagonal inertia tensor about the centre of mass, kg m^2. Set it by hand; " +
                 "a tensor Unity derives from a mesh gives the car bad rotational feel.")]
        public Vector3 inertia = new Vector3(600f, 2400f, 500f);

        [Header("Suspension")]
        public float springRateFront = 55000f;
        public float springRateRear = 62000f;
        public float damperBumpFront = 4200f;
        public float damperReboundFront = 6300f;
        public float damperBumpRear = 4600f;
        public float damperReboundRear = 6900f;
        [Tooltip("Primary balance knob. Stiffer front understeers, stiffer rear oversteers.")]
        public float antiRollFront = 18000f;
        public float antiRollRear = 14000f;
        public float maxDamperForce = 18000f;
        public float rollCentreHeightFront = 0.09f;
        public float rollCentreHeightRear = 0.13f;
        public float suspensionRestLength = 0.25f;
        public float suspensionTravel = 0.18f;

        [Header("Tyres")]
        public TyreData tyreFront = TyreData.Reference;
        public TyreData tyreRear = TyreData.Reference;

        [Header("Engine")]
        public TorquePoint[] torqueCurve =
        {
            new TorquePoint { rpm = 900f,  newtonMetres = 260f },
            new TorquePoint { rpm = 2000f, newtonMetres = 380f },
            new TorquePoint { rpm = 3000f, newtonMetres = 450f },
            new TorquePoint { rpm = 4000f, newtonMetres = 490f },
            new TorquePoint { rpm = 5000f, newtonMetres = 500f },
            new TorquePoint { rpm = 6000f, newtonMetres = 480f },
            new TorquePoint { rpm = 7000f, newtonMetres = 430f },
            new TorquePoint { rpm = 7600f, newtonMetres = 380f },
        };
        public float idleRpm = 900f;
        public float revLimitRpm = 7600f;
        public float engineInertia = 0.22f;
        public float engineBrakingTorque = 45f;

        [Header("Transmission")]
        public float[] gearRatios = { 3.60f, 2.30f, 1.70f, 1.32f, 1.08f, 0.88f };
        public float reverseRatio = 3.20f;
        public float finalDrive = 3.44f;
        [Range(0.7f, 1f)] public float drivetrainEfficiency = 0.88f;
        public float shiftTimeSeconds = 0.08f;
        public DriveLayout drive = DriveLayout.RearWheelDrive;
        public float clutchTorqueCapacity = 300f;
        public float clutchLockRpm = 2600f;
        public float launchRpm = 3600f;

        [Header("Differential")]
        public float diffPreloadTorque = 60f;
        [Range(0f, 1f)] public float diffPowerRamp = 0.35f;
        [Range(0f, 1f)] public float diffCoastRamp = 0.15f;

        [Header("Brakes")]
        public float maxBrakeTorque = 9000f;
        [Range(0.3f, 0.9f)] public float brakeBias = 0.62f;
        public float handbrakeTorque = 2600f;

        [Header("Aerodynamics")]
        public float dragCdA = 0.67f;
        public float liftFrontClA = 0.12f;
        public float liftRearClA = 0.20f;

        [Header("Steering")]
        public float maxSteerAngleDegrees = 33f;
        public float steerFalloffSpeed = 42f;
        public float steerRatePerSecond = 3.2f;

        public CarConfig ToConfig()
        {
            var curve = new (float Rpm, float Torque)[torqueCurve.Length];
            for (int i = 0; i < torqueCurve.Length; i++)
            {
                curve[i] = (torqueCurve[i].rpm, torqueCurve[i].newtonMetres);
                if (i > 0 && curve[i].Rpm <= curve[i - 1].Rpm)
                {
                    Debug.LogError($"{name}: torque curve must ascend by rpm. Point {i} is at " +
                                   $"{curve[i].Rpm} rpm, after {curve[i - 1].Rpm}. Interpolation " +
                                   "will return the wrong torque until this is ordered.");
                }
            }

            return new CarConfig
            {
                Name = carName,
                Mass = mass,
                FrontWeightBias = frontWeightBias,
                Wheelbase = wheelbase,
                TrackWidth = trackWidth,
                CgHeight = cgHeight,
                Inertia = Bridge.ToSim(inertia),

                SpringRateFront = springRateFront,
                SpringRateRear = springRateRear,
                DamperBumpFront = damperBumpFront,
                DamperReboundFront = damperReboundFront,
                DamperBumpRear = damperBumpRear,
                DamperReboundRear = damperReboundRear,
                AntiRollFront = antiRollFront,
                AntiRollRear = antiRollRear,
                MaxDamperForce = maxDamperForce,
                RollCentreHeightFront = rollCentreHeightFront,
                RollCentreHeightRear = rollCentreHeightRear,
                SuspensionRestLength = suspensionRestLength,
                SuspensionTravel = suspensionTravel,

                TyreFront = tyreFront.ToConfig(),
                TyreRear = tyreRear.ToConfig(),

                TorqueCurve = curve,
                IdleRpm = idleRpm,
                RevLimitRpm = revLimitRpm,
                EngineInertia = engineInertia,
                EngineBrakingTorque = engineBrakingTorque,

                GearRatios = gearRatios,
                ReverseRatio = reverseRatio,
                FinalDrive = finalDrive,
                DrivetrainEfficiency = drivetrainEfficiency,
                ShiftTimeSeconds = shiftTimeSeconds,
                Drive = drive,
                ClutchTorqueCapacity = clutchTorqueCapacity,
                ClutchLockRpm = clutchLockRpm,
                LaunchRpm = launchRpm,

                DiffPreloadTorque = diffPreloadTorque,
                DiffPowerRamp = diffPowerRamp,
                DiffCoastRamp = diffCoastRamp,

                MaxBrakeTorque = maxBrakeTorque,
                BrakeBias = brakeBias,
                HandbrakeTorque = handbrakeTorque,

                DragCdA = dragCdA,
                LiftFrontClA = liftFrontClA,
                LiftRearClA = liftRearClA,

                MaxSteerAngleDegrees = maxSteerAngleDegrees,
                SteerFalloffSpeed = steerFalloffSpeed,
                SteerRatePerSecond = steerRatePerSecond,
            };
        }
    }
}
