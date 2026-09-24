using System;
using System.Numerics;

namespace CarRace.Vehicle
{
    /// <summary>
    /// The vehicle model. Given a body pose, driver inputs and a way to probe the
    /// ground, it returns the force and torque the car produces this step.
    ///
    /// It does not integrate and it does not apply gravity: the caller owns the
    /// rigid body. In Unity that caller is a Rigidbody with useGravity left on and
    /// AddForceAtPosition doing the work; in the test harness it is RigidBody.cs.
    /// That split is what lets the physics be verified without an engine.
    ///
    /// Axes follow Unity: X right, Y up, Z forward.
    /// </summary>
    public sealed class VehicleSim
    {
        public const int FL = 0, FR = 1, RL = 2, RR = 3;

        public readonly CarConfig Config;
        public readonly Wheel[] Wheels = new Wheel[4];
        public readonly Drivetrain Drivetrain;
        public bool AutomaticGearbox = true;

        /// <summary>
        /// Driver aids. On by default because published acceleration and braking
        /// figures are measured with them, so the validation targets assume them,
        /// and because a LAN guest on a gamepad wants them. Turn both off for the
        /// unassisted car.
        /// </summary>
        public bool AntiLockBrakes = true;
        public bool TractionControl = true;

        /// <summary>
        /// Engine drag torque control, the third aid: eases engine braking off a driven wheel
        /// whose slip under braking passes the aids' target, as ABS eases the brake. Engine
        /// braking acts on the driven wheels only and ABS never touches it, so on this rear
        /// driven car it was braking the rear tyres beyond what ABS allows exactly when they
        /// also had to hold the car in a corner. Headless, with the keyboard assist steering
        /// through a corner under 60% brake from 170 km/h, taking engine braking away
        /// altogether cut the peak sideslip from 48 to 34 degrees for 2 km/h less speed lost.
        /// Proportional, so a plain lift, where the rear barely slips, keeps its engine braking.
        /// </summary>
        public bool EngineDragControl = true;
        /// <summary>Share of a tyre's grip in use above which engine braking is eased off,
        /// fully gone at all of it.</summary>
        public float EngineDragUsageStart = 0.85f;
        /// <summary>Slip ratio the aids hold, just under the longitudinal peak.</summary>
        public float AssistSlipTarget = 0.13f;
        /// <summary>
        /// How fast an aid can change its authority. Real systems take tens of
        /// milliseconds; cutting instantly makes them chatter between full torque
        /// and none, which wastes most of the grip they are meant to protect.
        /// </summary>
        public float TractionResponseSeconds = 0.02f;
        public float BrakeResponseSeconds = 0.04f;

        float _steerPosition;   // rack position, -1..1, rate limited

        public float SteerPosition => _steerPosition;

        public VehicleSim(CarConfig config)
        {
            Config = config;
            Drivetrain = new Drivetrain(config);

            float halfTrack = config.TrackWidth * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                bool left = (i % 2) == 0;
                float z = front ? config.FrontAxleToCg : -config.RearAxleToCg;
                Wheels[i] = new Wheel
                {
                    IsFront = front,
                    IsLeft = left,
                    LocalAttachment = new Vector3(left ? -halfTrack : halfTrack,
                                                  AttachmentHeight(front), z),
                };
            }
        }

        /// <summary>
        /// Strut top height relative to the centre of mass, derived so the car sits
        /// at exactly CgHeight under static load. Deriving it keeps ride height,
        /// spring rate and rest length consistent instead of three numbers that
        /// have to be hand matched every time a spring changes.
        /// </summary>
        float AttachmentHeight(bool front)
        {
            float radius = (front ? Config.TyreFront : Config.TyreRear).Radius;
            return Config.SuspensionRestLength + radius
                 - StaticCompression(front) - Config.CgHeight;
        }

        float StaticCompression(bool front)
        {
            float rate = front ? Config.SpringRateFront : Config.SpringRateRear;
            return Config.StaticLoadPerWheel(front) / rate;
        }

        /// <summary>Resting ride height of the centre of mass, for spawning the car.</summary>
        public float RestingCgHeight => Config.CgHeight;

        public void Reset()
        {
            foreach (var w in Wheels) w.Reset();
            Drivetrain.Reset();
            _steerPosition = 0f;
        }

        public Wrench Step(in BodyState body, in VehicleInputs input, IGround ground, float dt)
        {
            var wrench = new Wrench();
            float speed = body.Velocity.Length();

            UpdateSteering(input, speed, dt);

            for (int i = 0; i < 4; i++) UpdateSuspension(Wheels[i], body, ground, dt);
            ApplyAntiRollBars();

            Drivetrain.Update(Wheels, input, dt, AutomaticGearbox);
            AssignBrakeTorques(input);

            for (int i = 0; i < 4; i++) UpdateTyre(Wheels[i], body, ref wrench, dt);

            ApplyAerodynamics(body, ref wrench);
            return wrench;
        }

        // ---- steering ---------------------------------------------------------

        void UpdateSteering(in VehicleInputs input, float speed, float dt)
        {
            // Less lock at speed, so a full stick deflection is not a spin at 200 km/h.
            float falloff = 1f / (1f + speed / MathF.Max(Config.SteerFalloffSpeed, 0.01f));
            float target = Clamp(input.Steer, -1f, 1f) * falloff;

            float maxDelta = Config.SteerRatePerSecond * dt;
            _steerPosition += Clamp(target - _steerPosition, -maxDelta, maxDelta);

            float angle = _steerPosition * Config.MaxSteerAngleDegrees * (MathF.PI / 180f);
            float magnitude = MathF.Abs(angle);

            if (magnitude < 1e-4f)
            {
                Wheels[FL].SteerAngle = Wheels[FR].SteerAngle = angle;
                return;
            }

            // Ackermann: the inner wheel follows a tighter radius, so it steers more.
            float radius = Config.Wheelbase / MathF.Tan(magnitude);
            float half = Config.TrackWidth * 0.5f;
            float inner = MathF.Atan(Config.Wheelbase / MathF.Max(radius - half, 0.1f));
            float outer = MathF.Atan(Config.Wheelbase / (radius + half));
            float sign = MathF.Sign(angle);

            for (int i = FL; i <= FR; i++)
            {
                var w = Wheels[i];
                bool isInner = angle > 0f ? !w.IsLeft : w.IsLeft;
                w.SteerAngle = sign * (isInner ? inner : outer);
            }
        }

        // ---- suspension -------------------------------------------------------

        void UpdateSuspension(Wheel w, in BodyState body, IGround ground, float dt)
        {
            ref TyreConfig tyre = ref w.IsFront ? ref Config.TyreFront : ref Config.TyreRear;
            Vector3 attach = body.Position + body.ToWorldDirection(w.LocalAttachment);
            Vector3 down = -body.Up;
            float reach = Config.SuspensionRestLength + tyre.Radius;

            var hit = ground.Probe(attach, down, reach, tyre.Radius);
            w.PreviousCompression = w.Compression;
            w.WasGrounded = w.Grounded;

            if (!hit.Hit)
            {
                w.Grounded = false;
                w.Compression = 0f;
                w.Load = 0f;
                return;
            }

            float compression = Clamp(reach - hit.Distance, 0f, Config.SuspensionTravel);
            w.Compression = compression;
            w.Grounded = compression > 0f;
            w.ContactNormal = hit.Normal;
            w.ContactPoint = attach + down * hit.Distance;
            w.SurfaceFriction = hit.Friction;

            if (!w.Grounded) { w.Load = 0f; return; }

            float spring = (w.IsFront ? Config.SpringRateFront : Config.SpringRateRear) * compression;

            // A wheel that has just landed went from zero compression to real
            // compression in one step. Differentiating that gives a closing speed of
            // tens of metres per second and a damper force in the hundreds of
            // kilonewtons, which throws the car into the air and reads later as an
            // impossible cornering radius. Skip damping on the landing step and cap
            // what the damper can ever contribute.
            float rate = (dt > 0f && w.WasGrounded) ? (compression - w.PreviousCompression) / dt : 0f;
            float damping = rate > 0f
                ? (w.IsFront ? Config.DamperBumpFront : Config.DamperBumpRear)
                : (w.IsFront ? Config.DamperReboundFront : Config.DamperReboundRear);

            float damperForce = Clamp(damping * rate,
                                      -Config.MaxDamperForce, Config.MaxDamperForce);
            w.Load = MathF.Max(spring + damperForce, 0f);
        }

        /// <summary>
        /// Transfers load across each axle in proportion to how differently the two
        /// sides are compressed. This is the main balance knob: stiffer front means
        /// more understeer, stiffer rear means more oversteer.
        /// </summary>
        void ApplyAntiRollBars()
        {
            ApplyBar(Wheels[FL], Wheels[FR], Config.AntiRollFront);
            ApplyBar(Wheels[RL], Wheels[RR], Config.AntiRollRear);

            static void ApplyBar(Wheel left, Wheel right, float stiffness)
            {
                if (!left.Grounded || !right.Grounded) return;

                // The bar resists roll, so it pushes up on the compressed side and
                // pulls down on the extended one: the outside wheel gains load.
                // Inverted, it becomes a pro-roll bar that feeds the roll it should
                // be fighting, and the car lets go long before the tyres are out.
                float transfer = (left.Compression - right.Compression) * stiffness;
                left.Load = MathF.Max(left.Load + transfer, 0f);
                right.Load = MathF.Max(right.Load - transfer, 0f);
            }
        }

        void AssignBrakeTorques(in VehicleInputs input)
        {
            float total = Clamp(input.Brake, 0f, 1f) * Config.MaxBrakeTorque;
            float front = total * Config.BrakeBias * 0.5f;
            float rear = total * (1f - Config.BrakeBias) * 0.5f;
            float hand = Clamp(input.Handbrake, 0f, 1f) * Config.HandbrakeTorque * 0.5f;

            Wheels[FL].BrakeTorque = front;
            Wheels[FR].BrakeTorque = front;
            Wheels[RL].BrakeTorque = rear + hand;
            Wheels[RR].BrakeTorque = rear + hand;
        }

        // ---- tyres ------------------------------------------------------------

        void UpdateTyre(Wheel w, in BodyState body, ref Wrench wrench, float dt)
        {
            ref TyreConfig tyre = ref w.IsFront ? ref Config.TyreFront : ref Config.TyreRear;
            float inertia = Drivetrain.EffectiveWheelInertia(w);

            if (!w.Grounded)
            {
                w.SlipRatio = w.SlipAngle = 0f;
                w.ForceLong = w.ForceLat = 0f;
                w.LaggedLong = w.LaggedLat = 0f;
                IntegrateWheel(w, 0f, inertia, tyre.Radius, dt);
                return;
            }

            Vector3 offset = w.ContactPoint - body.Position;

            // Sample velocity, and later apply lateral force, at the roll centre
            // rather than at the contact patch. See RollCentreHeight* for why.
            float rollCentre = w.IsFront ? Config.RollCentreHeightFront
                                         : Config.RollCentreHeightRear;
            Vector3 lateralOffset = offset + body.Up * rollCentre;
            Vector3 contactVelocity = body.PointVelocity(lateralOffset);

            // Wheel frame, flattened onto the contact plane.
            Vector3 steered = body.ToWorldDirection(new Vector3(
                MathF.Sin(w.SteerAngle), 0f, MathF.Cos(w.SteerAngle)));
            Vector3 normal = w.ContactNormal;
            Vector3 forward = steered - normal * Vector3.Dot(steered, normal);
            if (forward.LengthSquared() < 1e-8f) { w.ForceLong = w.ForceLat = 0f; return; }
            forward = Vector3.Normalize(forward);
            Vector3 right = Vector3.Cross(normal, forward);

            float vLong = Vector3.Dot(contactVelocity, forward);
            float vLat = Vector3.Dot(contactVelocity, right);

            // Slip is undefined at rest, so divide by a floored reference speed.
            const float SpeedFloor = 2.0f;
            float reference = MathF.Max(MathF.Abs(vLong), SpeedFloor);

            w.SlipRatio = (w.AngularVelocity * tyre.Radius - vLong) / reference;
            w.SlipAngle = MathF.Atan2(vLat, reference);

            float peak = Pacejka.PeakForce(tyre, w.Load, w.SurfaceFriction);
            float fx = peak * Pacejka.Normalised(w.SlipRatio, tyre.LongB, tyre.LongC, tyre.LongE);
            float fy = -peak * Pacejka.Normalised(w.SlipAngle, tyre.LatB, tyre.LatC, tyre.LatE);

            // Friction ellipse: the two demands share one contact patch, which is
            // what gives trail braking and power-on oversteer for free.
            if (peak > 1e-3f)
            {
                float nx = fx / peak, ny = fy / peak;
                float combined = MathF.Sqrt(nx * nx + ny * ny);
                w.GripUsage = combined;
                if (combined > 1f) { fx /= combined; fy /= combined; }
            }

            // Relaxation length: force builds over distance travelled, not instantly.
            // Relaxation length is a distance, so the rate at which force builds
            // depends on how fast the contact patch is working, not on how fast the
            // car is moving. Use whichever of road speed and wheel surface speed is
            // greater: a wheel spinning up at a standstill is laying down rubber
            // quickly, and keying off road speed alone lets it run away to a slip
            // ratio of seven before the tyre pushes back.
            float patchSpeed = MathF.Max(MathF.Abs(vLong),
                                         MathF.Abs(w.AngularVelocity) * tyre.Radius);
            float lag = Clamp(dt * MathF.Max(patchSpeed, 1f)
                              / MathF.Max(tyre.RelaxationLength, 1e-3f), 0f, 1f);
            w.LaggedLong += (fx - w.LaggedLong) * lag;
            w.LaggedLat += (fy - w.LaggedLat) * lag;
            w.ForceLong = w.LaggedLong;
            w.ForceLat = w.LaggedLat;

            ApplyDriverAids(w, dt);
            IntegrateWheel(w, w.ForceLong, inertia, tyre.Radius, dt);

            // Vertical and longitudinal forces act at the patch, which is what
            // gives load transfer, squat and dive. Lateral acts at the roll centre.
            wrench.AddAt(forward * w.ForceLong + normal * w.Load, offset);
            wrench.AddAt(right * w.ForceLat, lateralOffset);
        }

        /// <summary>
        /// Traction control, ABS and engine drag control, as proportional cuts once slip passes the
        /// target. Both write back to the wheel so telemetry shows what was really
        /// applied rather than what was asked for.
        /// </summary>
        void ApplyDriverAids(Wheel w, float dt)
        {
            float target = MathF.Max(AssistSlipTarget, 1e-3f);

            float tractionDemand = 1f;
            if (TractionControl && w.SlipRatio > target)
                tractionDemand = Clamp(1f - (w.SlipRatio - target) / target, 0f, 1f);
            w.TractionScale += (tractionDemand - w.TractionScale)
                             * Clamp(dt / MathF.Max(TractionResponseSeconds, 1e-4f), 0f, 1f);

            float brakeDemand = 1f;
            if (AntiLockBrakes && w.SlipRatio < -target)
                brakeDemand = Clamp(1f - (-w.SlipRatio - target) / target, 0f, 1f);
            w.BrakeScale += (brakeDemand - w.BrakeScale)
                          * Clamp(dt / MathF.Max(BrakeResponseSeconds, 1e-4f), 0f, 1f);

            // Keyed to the tyre's combined grip in use, braking and cornering together, not to
            // slip alone: braking through a corner the rear sits at a slip of 0.05 to 0.12,
            // under the target, while the friction ellipse has almost nothing left sideways.
            float dragDemand = 1f;
            if (EngineDragControl)
                dragDemand = Clamp(1f - (w.GripUsage - EngineDragUsageStart) / (1f - EngineDragUsageStart), 0f, 1f);
            w.EngineDragScale += (dragDemand - w.EngineDragScale)
                               * Clamp(dt / MathF.Max(TractionResponseSeconds, 1e-4f), 0f, 1f);

            if (TractionControl) w.DriveTorque *= w.TractionScale;
            if (EngineDragControl && w.DriveTorque < 0f) w.DriveTorque *= w.EngineDragScale;
            if (AntiLockBrakes) w.BrakeTorque *= w.BrakeScale;
        }

        /// <summary>
        /// Wheel rotation closes the loop: drive torque spins it up, tyre force
        /// spins it down. Wheelspin and lockup then emerge rather than being faked.
        /// </summary>
        static void IntegrateWheel(Wheel w, float tyreForce, float inertia, float radius, float dt)
        {
            if (inertia <= 1e-6f) return;
            float omega = w.AngularVelocity + (w.DriveTorque - tyreForce * radius) / inertia * dt;

            // Brakes may stop a wheel but must never drive it backwards.
            float brakeStep = w.BrakeTorque / inertia * dt;
            if (MathF.Abs(omega) <= brakeStep) omega = 0f;
            else omega -= MathF.Sign(omega) * brakeStep;

            w.AngularVelocity = omega;
        }

        // ---- aerodynamics -----------------------------------------------------

        void ApplyAerodynamics(in BodyState body, ref Wrench wrench)
        {
            float speed = body.Velocity.Length();
            if (speed < 0.1f) return;

            float q = 0.5f * Physics.AirDensity * speed * speed;
            wrench.Force += (-body.Velocity / speed) * (q * Config.DragCdA);

            // Front and rear downforce are separate so aero balance shifts with speed,
            // the way a real car's does.
            Vector3 down = -body.Up;
            wrench.AddAt(down * (q * Config.LiftFrontClA),
                         body.ToWorldDirection(new Vector3(0f, 0f, Config.FrontAxleToCg)));
            wrench.AddAt(down * (q * Config.LiftRearClA),
                         body.ToWorldDirection(new Vector3(0f, 0f, -Config.RearAxleToCg)));
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
