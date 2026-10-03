using System;
using System.Numerics;

namespace CarRace.Vehicle
{
    /// <summary>
    /// Pacejka magic formula, the difference between a car that moves and a car
    /// that drives.
    /// </summary>
    public static class Pacejka
    {
        /// <summary>
        /// F = sin(C atan(B s - E (B s - atan(B s)))), normalised to peak 1.
        /// Multiply by the peak force for this load to get newtons.
        /// </summary>
        public static float Normalised(float slip, float b, float c, float e)
        {
            float bs = b * slip;
            return MathF.Sin(c * MathF.Atan(bs - e * (bs - MathF.Atan(bs))));
        }

        /// <summary>
        /// Peak force at this vertical load, including load sensitivity: grip per
        /// newton drops as load rises, which is what makes weight transfer matter.
        /// </summary>
        public static float PeakForce(in TyreConfig t, float load, float surfaceFriction)
        {
            if (load <= 0f) return 0f;
            float sensitivity = 1f - t.LoadSensitivity * (load / t.NominalLoad - 1f);
            if (sensitivity < 0.30f) sensitivity = 0.30f;
            if (sensitivity > 1.40f) sensitivity = 1.40f;
            return t.Mu * surfaceFriction * load * sensitivity;
        }
    }

    /// <summary>
    /// How grip falls as a tyre wears: 4% by WearCliff, then fast, to WornGrip when worn out.
    /// The cliff is what makes a stop worth its time: before it a worn tyre costs little, past
    /// it each lap costs more than the one before.
    /// </summary>
    public static class TyreWear
    {
        public const float WearCliff = 0.6f;
        public const float GripAtCliff = 0.96f;
        public const float WornGrip = 0.80f;

        /// <summary>Share of a new tyre's grip at <paramref name="wear"/>, 0 new to 1 worn out.
        /// Exactly 1 for a new tyre, so wear switched off changes nothing.</summary>
        public static float Grip(float wear)
        {
            if (wear <= 0f) return 1f;
            if (wear < WearCliff) return 1f - (1f - GripAtCliff) * wear / WearCliff;
            if (wear >= 1f) return WornGrip;
            return GripAtCliff - (GripAtCliff - WornGrip) * (wear - WearCliff) / (1f - WearCliff);
        }
    }

    /// <summary>One corner: suspension state, slip state and the forces it made.</summary>
    public sealed class Wheel
    {
        public Vector3 LocalAttachment;   // top of the strut, body frame
        public bool IsFront;
        public bool IsLeft;

        // --- suspension ---
        public float Compression;         // m, 0 at full droop
        public float PreviousCompression;
        public float Load;                // N along the contact normal
        public bool Grounded;
        public bool WasGrounded;
        public Vector3 ContactPoint;
        public Vector3 ContactNormal;
        public float SurfaceFriction = 1f;

        // --- rotation ---
        public float SteerAngle;          // rad, positive steers right
        public float AngularVelocity;     // rad/s, positive drives forward
        public float DriveTorque;         // N m applied this step
        public float BrakeTorque;         // N m magnitude available this step

        // --- slip and force ---
        public float SlipRatio;
        public float SlipAngle;           // rad
        public float ForceLong;           // N in the wheel's forward direction
        public float ForceLat;            // N in the wheel's right direction

        // Relaxation state: force lags the slip that causes it.
        internal float LaggedLong;
        internal float LaggedLat;

        /// <summary>Driver aid authority, 0 fully cut to 1 untouched. Smoothed, not instant.</summary>
        public float TractionScale = 1f;
        public float BrakeScale = 1f;
        public float EngineDragScale = 1f;   // share of engine braking let through, see VehicleSim.EngineDragControl
        public float GripUsage;              // combined demand over peak, friction ellipse; above 1 the tyre is saturated

        /// <summary>How worn the tyre is, 0 new to 1 worn out. Not cleared by Reset: a recovery
        /// does not fit new tyres, only VehicleSim.FitNewTyres does.</summary>
        public float Wear;
        public float GripFactor => TyreWear.Grip(Wear);

        public ref TyreConfig Tyre(CarConfig cfg)
            => ref IsFront ? ref cfg.TyreFront : ref cfg.TyreRear;

        public void Reset()
        {
            Compression = PreviousCompression = 0f;
            Load = 0f;
            Grounded = WasGrounded = false;
            SteerAngle = AngularVelocity = 0f;
            DriveTorque = BrakeTorque = 0f;
            SlipRatio = SlipAngle = 0f;
            ForceLong = ForceLat = 0f;
            LaggedLong = LaggedLat = 0f;
            TractionScale = BrakeScale = EngineDragScale = 1f;
        }
    }
}
