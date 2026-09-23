using System;
using System.Numerics;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Six degree of freedom rigid body, semi-implicit Euler. Stands in for a
    /// Unity Rigidbody so the vehicle model can be exercised with no engine
    /// present. Unity will integrate slightly differently, but both apply the same
    /// forces from the same model, so what this validates is the vehicle physics.
    /// </summary>
    public sealed class RigidBody
    {
        public BodyState State;
        public float Mass;
        public Vector3 Inertia;          // diagonal, body frame
        public bool ApplyGravity = true;

        public RigidBody(float mass, Vector3 inertia, Vector3 position)
        {
            Mass = mass;
            Inertia = inertia;
            State = BodyState.AtRest(position);
        }

        public void Integrate(in Wrench wrench, float dt)
        {
            Vector3 acceleration = wrench.Force / Mass;
            if (ApplyGravity) acceleration.Y -= Physics.Gravity;
            State.Velocity += acceleration * dt;
            State.Position += State.Velocity * dt;

            // Euler's equations in the body frame, where the inertia tensor is diagonal.
            Quaternion toBody = Quaternion.Conjugate(State.Orientation);
            Vector3 torqueBody = Vector3.Transform(wrench.Torque, toBody);
            Vector3 omegaBody = Vector3.Transform(State.AngularVelocity, toBody);
            Vector3 gyroscopic = Vector3.Cross(omegaBody, omegaBody * Inertia);
            omegaBody += (torqueBody - gyroscopic) / Inertia * dt;

            State.AngularVelocity = Vector3.Transform(omegaBody, State.Orientation);

            float rate = State.AngularVelocity.Length();
            if (rate > 1e-9f)
            {
                // Angular velocity is in world space, so its increment composes
                // AFTER the current attitude. In System.Numerics, q1 * q2 applies
                // q2 first, so that is delta * orientation, not orientation * delta.
                // The wrong order applies the spin in the body frame instead: the
                // per-step error is tiny, but it quietly couples yaw into roll and
                // pulls a steady cornering state apart after a few seconds.
                var delta = Quaternion.CreateFromAxisAngle(State.AngularVelocity / rate, rate * dt);
                State.Orientation = Quaternion.Normalize(delta * State.Orientation);
            }
        }
    }

    /// <summary>Infinite horizontal plane at y = 0.</summary>
    public sealed class FlatGround : IGround
    {
        public float Friction = 1f;

        public GroundHit Probe(Vector3 origin, Vector3 direction, float maxDistance, float radius)
        {
            var miss = new GroundHit { Hit = false };
            if (origin.Y <= 0f)
                return new GroundHit { Hit = true, Distance = 0f, Normal = Vector3.UnitY, Friction = Friction };
            if (direction.Y >= -1e-6f) return miss;

            float distance = -origin.Y / direction.Y;
            if (distance < 0f || distance > maxDistance) return miss;

            return new GroundHit
            {
                Hit = true,
                Distance = distance,
                Normal = Vector3.UnitY,
                Friction = Friction,
            };
        }
    }
}
