using UnityEngine;
using CarRace.Vehicle;
using Vec3 = System.Numerics.Vector3;
using Quat = System.Numerics.Quaternion;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Converts between Unity's vector types and the ones the vehicle model uses.
    ///
    /// The conversion is component for component, including quaternions, which looks
    /// wrong at a glance because Unity is usually described as left handed and
    /// System.Numerics as right handed. The handedness only describes how the axes
    /// are drawn. Unity defines its cross product by the left hand rule so that
    /// Cross(right, up) is forward, which makes the component arithmetic identical to
    /// the right handed formulas System.Numerics uses. A positive rotation about +Y
    /// takes +Z to +X in both. Both also compose as "q1 * q2 applies q2 first".
    ///
    /// That argument is worth exactly nothing if it is wrong, so VerifyConventions
    /// checks it against the real engine at startup rather than trusting the comment.
    /// </summary>
    public static class Bridge
    {
        public static Vec3 ToSim(Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Quat ToSim(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
        public static Quaternion ToUnity(Quat q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        /// <summary>
        /// Reads the body state the model wants out of a Unity rigidbody. Position is
        /// the centre of mass, because every offset in the model is measured from
        /// there, and velocities are world space in both.
        /// </summary>
        public static BodyState ReadBody(Rigidbody rb) => new BodyState
        {
            Position = ToSim(rb.worldCenterOfMass),
            Orientation = ToSim(rb.rotation),
            Velocity = ToSim(rb.linearVelocity),
            AngularVelocity = ToSim(rb.angularVelocity),
        };

        static bool _verified;

        /// <summary>
        /// Confirms that a Unity rotation and the same components read as a
        /// System.Numerics rotation turn a vector the same way. If this ever fails,
        /// the two libraries disagree about handedness or composition order and every
        /// force the model produces is mirrored. Cheap, runs once, and turns a subtle
        /// wrong-way-round car into a line in the console.
        /// </summary>
        public static void VerifyConventions()
        {
            if (_verified) return;
            _verified = true;

            Quaternion q = Quaternion.Euler(20f, 40f, 60f);
            Vector3 byUnity = q * Vector3.forward;
            Vector3 bySim = ToUnity(Vec3.Transform(Vec3.UnitZ, ToSim(q)));

            if (Vector3.Distance(byUnity, bySim) > 1e-4f)
            {
                Debug.LogError(
                    "CarRace: Unity and System.Numerics disagree about rotation. " +
                    $"Unity gives {byUnity:F4}, the model gives {bySim:F4}. " +
                    "Every force from the vehicle model will be mirrored until Bridge is fixed.");
            }
        }
    }
}
