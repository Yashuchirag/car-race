using System;
using System.Numerics;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>Drives a VehicleSim forward at a fixed substep rate.</summary>
    public sealed class Rig
    {
        public const float SubstepHz = 500f;     // tyre models go unstable below ~300 Hz
        const float Dt = 1f / SubstepHz;

        public readonly VehicleSim Sim;
        public readonly RigidBody Body;
        public readonly FlatGround Ground = new FlatGround();
        public float Time;

        public Rig(CarConfig config)
        {
            Sim = new VehicleSim(config);
            Body = new RigidBody(config.Mass, config.Inertia,
                                 new Vector3(0f, config.CgHeight, 0f));
        }

        public float SpeedKph => Body.State.Velocity.Length() * 3.6f;
        public float ForwardSpeed => Vector3.Dot(Body.State.Velocity, Body.State.Forward);
        public float YawRate => Vector3.Dot(Body.State.AngularVelocity, Body.State.Up);
        public Vector3 Acceleration { get; private set; }

        /// <summary>
        /// True lateral acceleration in g: the part of horizontal acceleration
        /// perpendicular to where the car is actually going.
        ///
        /// Speed times yaw rate is the usual shortcut and it is wrong the moment
        /// the car slides, because the body then points somewhere other than its
        /// velocity. It reported over 2 g on a 1.1 mu tyre during a spin.
        /// </summary>
        public float LateralG
        {
            get
            {
                Vector3 v = Body.State.Velocity; v.Y = 0f;
                if (v.Length() < 0.1f) return 0f;
                Vector3 heading = Vector3.Normalize(v);
                Vector3 a = Acceleration; a.Y = 0f;
                return (a - heading * Vector3.Dot(a, heading)).Length() / Physics.Gravity;
            }
        }

        /// <summary>Angle between where the car points and where it is going, degrees.</summary>
        public float SideslipDegrees
        {
            get
            {
                var b = Body.State;
                float f = Vector3.Dot(b.Velocity, b.Forward);
                float r = Vector3.Dot(b.Velocity, b.Right);
                if (b.Velocity.Length() < 0.5f) return 0f;
                return MathF.Atan2(r, MathF.Abs(f)) * 180f / MathF.PI;
            }
        }

        public void Step(in VehicleInputs input)
        {
            Vector3 before = Body.State.Velocity;
            var wrench = Sim.Step(Body.State, input, Ground, Dt);
            Body.Integrate(wrench, Dt);
            Acceleration = (Body.State.Velocity - before) / Dt;
            Time += Dt;
        }

        /// <summary>Let the suspension settle so tests do not start mid bounce.</summary>
        public void Settle(float seconds = 1.5f)
        {
            var idle = new VehicleInputs { Clutch = true };
            int steps = (int)(seconds * SubstepHz);
            for (int i = 0; i < steps; i++) Step(idle);
            Body.State.Velocity = Vector3.Zero;
            Body.State.AngularVelocity = Vector3.Zero;
            Time = 0f;
        }

        public void Reset()
        {
            Sim.Reset();
            _speedIntegral = 0f;
            Body.State = BodyState.AtRest(new Vector3(0f, Sim.Config.CgHeight, 0f));
            Time = 0f;
        }

        float _speedIntegral;

        /// <summary>
        /// Throttle and brake that hold a target forward speed.
        ///
        /// The integral term is not optional. Holding a speed needs a non-zero
        /// throttle to balance drag, so a proportional-only controller settles
        /// wherever its output happens to match that, and never reaches target.
        /// </summary>
        public void HoldSpeed(ref VehicleInputs input, float targetMs)
        {
            float error = targetMs - ForwardSpeed;
            _speedIntegral = MathF.Max(-1f, MathF.Min(1f, _speedIntegral + error * Dt * 0.6f));
            float demand = error * 0.35f + _speedIntegral;

            if (demand >= 0f)
            {
                input.Throttle = MathF.Min(demand, 1f);
                input.Brake = 0f;
            }
            else
            {
                input.Throttle = 0f;
                input.Brake = MathF.Min(-demand, 1f);
            }
        }
    }
}
