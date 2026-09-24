using System.Numerics;

namespace CarRace.Vehicle
{
    /// <summary>Driver inputs for one physics step. All normalised.</summary>
    public struct VehicleInputs
    {
        public float Throttle;   // 0..1
        public float Brake;      // 0..1
        public float Steer;      // -1 full left .. +1 full right
        public float Handbrake;  // 0..1
        public bool Clutch;      // true disengages drive

        public static VehicleInputs Coasting => new VehicleInputs();
    }

    /// <summary>What a suspension probe found under one wheel.</summary>
    public struct GroundHit
    {
        public bool Hit;
        public float Distance;   // along the probe direction, metres
        public Vector3 Normal;   // unit, pointing up out of the surface
        public float Friction;   // surface multiplier: asphalt 1.0, grass 0.45
    }

    /// <summary>
    /// Surface query. The harness answers with a flat plane; in Unity this wraps
    /// Physics.SphereCast and reads the friction multiplier off the physic material.
    /// </summary>
    public interface IGround
    {
        GroundHit Probe(Vector3 origin, Vector3 direction, float maxDistance, float radius);
    }

    /// <summary>Force and torque accumulated over one step, in world space.</summary>
    public struct Wrench
    {
        public Vector3 Force;    // newtons
        public Vector3 Torque;   // newton metres, about the centre of mass

        public void AddAt(Vector3 force, Vector3 offsetFromCom)
        {
            Force += force;
            Torque += Vector3.Cross(offsetFromCom, force);
        }
    }

    public enum DriveLayout { RearWheelDrive, FrontWheelDrive, AllWheelDrive }

    /// <summary>
    /// Rigid body pose and motion the vehicle model reads. The owner integrates it.
    /// Axes follow Unity: X right, Y up, Z forward.
    /// </summary>
    public struct BodyState
    {
        public Vector3 Position;
        public Quaternion Orientation;
        public Vector3 Velocity;         // world, m/s
        public Vector3 AngularVelocity;  // world, rad/s

        public Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);
        public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);
        public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Orientation);

        /// <summary>World velocity of the point at <paramref name="worldOffset"/> from the centre of mass.</summary>
        public Vector3 PointVelocity(Vector3 worldOffset)
            => Velocity + Vector3.Cross(AngularVelocity, worldOffset);

        public Vector3 ToWorldDirection(Vector3 local) => Vector3.Transform(local, Orientation);

        public static BodyState AtRest(Vector3 position) => new BodyState
        {
            Position = position,
            Orientation = Quaternion.Identity,
            Velocity = Vector3.Zero,
            AngularVelocity = Vector3.Zero,
        };
    }
}
