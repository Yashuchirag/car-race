using System;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Closed-form expectations derived from the car's own configuration.
    ///
    /// The simulation is checked against these rather than against numbers chosen
    /// by hand. Hand-picked targets are worth little: the original 0 to 100 target
    /// of 4.2 s was below this car's traction-limited floor of 4.43 s, so it could
    /// never have been met and the failure said nothing about the model. These are
    /// independent of the simulation, so agreement means something.
    /// </summary>
    public static class Analytic
    {
        const float Hundred = 100f / 3.6f;

        /// <summary>Grip at one wheel at a given vertical load, including load sensitivity.</summary>
        static float Grip(in TyreConfig t, float load)
        {
            if (load <= 0f) return 0f;
            float sens = MathF.Max(0.3f, MathF.Min(1.4f, 1f - t.LoadSensitivity * (load / t.NominalLoad - 1f)));
            return t.Mu * load * sens;
        }

        /// <summary>
        /// Best possible 0 to 100, limited by driven-axle grip with weight transfer,
        /// by engine power, and by drag. Assumes a perfect launch, so the simulation
        /// should land a little above it.
        /// </summary>
        public static float ZeroToHundredSeconds(CarConfig c, int shifts = 1)
        {
            float power = PeakPowerWatts(c) * c.DrivetrainEfficiency;
            bool rearDriven = c.Drive != DriveLayout.FrontWheelDrive;
            float staticShare = rearDriven ? 1f - c.FrontWeightBias : c.FrontWeightBias;
            float transferSign = rearDriven ? 1f : -1f;
            int drivenWheels = c.Drive == DriveLayout.AllWheelDrive ? 4 : 2;
            ref TyreConfig tyre = ref rearDriven ? ref c.TyreRear : ref c.TyreFront;

            float v = 0f, t = 0f;
            const float Dt = 0.0005f;
            while (v < Hundred && t < 30f)
            {
                float a = 5f;
                for (int i = 0; i < 25; i++)
                {
                    float axleLoad = c.Mass * Physics.Gravity * staticShare
                                   + transferSign * c.Mass * a * c.CgHeight / c.Wheelbase;
                    if (c.Drive == DriveLayout.AllWheelDrive)
                        axleLoad = c.Mass * Physics.Gravity;
                    float traction = drivenWheels * Grip(tyre, MathF.Max(axleLoad, 0f) / drivenWheels);
                    float fromPower = power / MathF.Max(v, 3f);
                    float drag = 0.5f * Physics.AirDensity * c.DragCdA * v * v;
                    a = (MathF.Min(traction, fromPower) - drag) / c.Mass;
                }
                v += a * Dt;
                t += Dt;
            }
            return t + shifts * c.ShiftTimeSeconds;
        }

        /// <summary>Best possible 100 to 0, limited by grip on all four wheels plus drag.</summary>
        public static float BrakingMetres(CarConfig c)
        {
            float v = Hundred, d = 0f;
            const float Dt = 0.0005f;
            while (v > 0.15f)
            {
                float a = 10f;
                for (int i = 0; i < 20; i++)
                {
                    float front = (c.Mass * Physics.Gravity * c.FrontWeightBias
                                 + c.Mass * a * c.CgHeight / c.Wheelbase) * 0.5f;
                    float rear = (c.Mass * Physics.Gravity * (1f - c.FrontWeightBias)
                                - c.Mass * a * c.CgHeight / c.Wheelbase) * 0.5f;
                    float force = 2f * Grip(c.TyreFront, MathF.Max(front, 0f))
                                + 2f * Grip(c.TyreRear, MathF.Max(rear, 0f))
                                + 0.5f * Physics.AirDensity * c.DragCdA * v * v;
                    a = force / c.Mass;
                }
                d += v * Dt - 0.5f * a * Dt * Dt;
                v -= a * Dt;
            }
            return d;
        }

        /// <summary>Speed where engine power at the wheels equals aerodynamic drag.</summary>
        public static float TopSpeedKph(CarConfig c)
        {
            float power = PeakPowerWatts(c) * c.DrivetrainEfficiency;
            return MathF.Cbrt(2f * power / (Physics.AirDensity * c.DragCdA)) * 3.6f;
        }

        /// <summary>
        /// Lateral ceiling with every tyre at its peak at once. No real car reaches
        /// it, because front and rear balance means one axle saturates first, so the
        /// simulation should land somewhat under this.
        /// </summary>
        public static float SkidpadCeilingG(CarConfig c)
        {
            float front = 2f * Grip(c.TyreFront, c.StaticLoadPerWheel(true));
            float rear = 2f * Grip(c.TyreRear, c.StaticLoadPerWheel(false));
            return (front + rear) / (c.Mass * Physics.Gravity);
        }

        public static float PeakPowerWatts(CarConfig c)
        {
            float best = 0f;
            foreach (var (rpm, torque) in c.TorqueCurve)
                best = MathF.Max(best, torque * rpm * Physics.RpmToRadPerSec);
            return best;
        }
    }
}
