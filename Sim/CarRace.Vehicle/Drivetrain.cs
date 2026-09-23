using System;

namespace CarRace.Vehicle
{
    /// <summary>
    /// Engine, gearbox and differential. The clutch is modelled as a rigid
    /// coupling when engaged, which is stable and enough for this feel target.
    ///
    /// The important detail is reflected inertia: engine inertia seen at the wheel
    /// is multiplied by the square of the total gear ratio, so in first gear the
    /// engine's 0.22 kg m^2 becomes about 34 kg m^2 at the wheel and dwarfs the
    /// wheel's own 1.4. Leave it out and wheelspin becomes absurdly easy.
    /// </summary>
    public sealed class Drivetrain
    {
        public int Gear = 1;              // 0 neutral, -1 reverse, 1..N forward
        public float EngineRpm;
        public bool ShiftInProgress => _shiftTimer > 0f;

        float _shiftTimer;
        readonly CarConfig _cfg;

        /// <summary>True while the clutch is passing torque between different speeds.</summary>
        public bool Slipping { get; private set; }

        /// <summary>True while the engine is actually coupled to the driven wheels.</summary>
        public bool Engaged { get; private set; }

        /// <summary>
        /// How locked the clutch is, 0 fully slipping to 1 fully home. Ramped, not
        /// switched: reflected engine inertia is about thirteen times a wheel's own,
        /// so flipping it in a single step lands as an impulse on a tyre that may
        /// already be near its limit, and snaps the car mid corner.
        /// </summary>
        public float ClutchLock { get; private set; }
        public float ClutchLockSeconds = 0.25f;

        public Drivetrain(CarConfig cfg)
        {
            _cfg = cfg;
            EngineRpm = cfg.IdleRpm;
        }

        public float TotalRatio
        {
            get
            {
                if (Gear == 0) return 0f;
                float g = Gear < 0 ? -_cfg.ReverseRatio : _cfg.GearRatios[Gear - 1];
                return g * _cfg.FinalDrive;
            }
        }

        public bool IsDriven(Wheel w) => _cfg.Drive switch
        {
            DriveLayout.RearWheelDrive => !w.IsFront,
            DriveLayout.FrontWheelDrive => w.IsFront,
            _ => true,
        };

        public int DrivenWheelCount => _cfg.Drive == DriveLayout.AllWheelDrive ? 4 : 2;

        /// <summary>Inertia seen at one driven wheel, including the reflected engine.</summary>
        public float EffectiveWheelInertia(Wheel w)
        {
            float baseInertia = w.IsFront ? _cfg.TyreFront.Inertia : _cfg.TyreRear.Inertia;
            // A slipping clutch decouples the engine, so its inertia is not reflected.
            // Only a locked clutch reflects engine inertia. Reflecting it while the
            // clutch is out gives a driven wheel about thirteen times the inertia it
            // should have, so it lags road speed, carries a standing slip ratio, and
            // the friction ellipse then quietly eats its cornering grip.
            if (!IsDriven(w) || !Engaged) return baseInertia;
            float ratio = TotalRatio;
            return baseInertia
                 + _cfg.EngineInertia * ratio * ratio / DrivenWheelCount * ClutchLock;
        }

        /// <summary>
        /// Work out engine speed from the driven wheels, read the torque curve, and
        /// hand each driven wheel its share. Returns engine torque at the crank.
        /// </summary>
        public float Update(Wheel[] wheels, in VehicleInputs input, float dt, bool autoShift)
        {
            if (_shiftTimer > 0f) _shiftTimer -= dt;

            float ratio = TotalRatio;
            float drivenSpeed = 0f;
            int driven = 0;
            foreach (var w in wheels)
            {
                if (!IsDriven(w)) continue;
                drivenSpeed += w.AngularVelocity;
                driven++;
            }
            if (driven > 0) drivenSpeed /= driven;

            bool engaged = Gear != 0 && !input.Clutch && !ShiftInProgress;
            float clutchSideRpm = MathF.Abs(drivenSpeed * ratio) * Physics.RadPerSecToRpm;

            Engaged = engaged;
            if (!engaged)
            {
                Slipping = false;
                ClutchLock = 0f;
                float free = _cfg.IdleRpm + input.Throttle * (_cfg.RevLimitRpm - _cfg.IdleRpm);
                EngineRpm += (free - EngineRpm) * MathF.Min(dt * 4f, 1f);
                foreach (var w in wheels) w.DriveTorque = 0f;
                if (autoShift) AutoShift(input);
                return 0f;
            }

            // Launch: from rest the clutch must slip or the engine stalls, and
            // launch control holds the revs up while it passes what it can carry.
            // A rigid coupling instead pins the engine to idle at a standstill, so
            // the car pulls away on idle torque and loses about a second to 100.
            //
            // This latches. Testing the condition fresh each step re-triggers a
            // "launch" whenever the throttle is floored at moderate speed, and
            // because a slipping clutch stops reflecting engine inertia to the
            // wheels, that flips the effective wheel inertia by a factor of twenty
            // mid corner: the wheel spikes, traction control cuts, and the car
            // sheds speed for no reason the driver can see.
            float launchRpm = _cfg.IdleRpm + (_cfg.LaunchRpm - _cfg.IdleRpm) * input.Throttle;
            if (clutchSideRpm < _cfg.IdleRpm * 0.9f) Slipping = true;
            else if (Slipping && clutchSideRpm >= launchRpm - 25f) Slipping = false;

            float lockTarget = Slipping ? 0f : 1f;
            float lockStep = dt / MathF.Max(ClutchLockSeconds, 1e-4f);
            ClutchLock += MathF.Max(-lockStep, MathF.Min(lockStep, lockTarget - ClutchLock));
            if (ClutchLock < 0f) ClutchLock = 0f;
            if (ClutchLock > 1f) ClutchLock = 1f;

            EngineRpm = MathF.Max(clutchSideRpm, Slipping ? launchRpm : _cfg.IdleRpm);
            bool onLimiter = EngineRpm >= _cfg.RevLimitRpm;
            if (onLimiter) EngineRpm = _cfg.RevLimitRpm;

            float throttle = onLimiter ? 0f : input.Throttle;
            float crankTorque = _cfg.TorqueAtRpm(EngineRpm) * throttle;
            if (Slipping) crankTorque = MathF.Min(crankTorque, _cfg.ClutchTorqueCapacity * throttle);
            crankTorque -= _cfg.EngineBrakingTorque * (1f - throttle) * (EngineRpm / _cfg.RevLimitRpm);

            if (autoShift) AutoShift(input);

            foreach (var w in wheels) w.DriveTorque = 0f;
            if (driven == 0) return crankTorque;

            float wheelTorque = crankTorque * ratio * _cfg.DrivetrainEfficiency / driven;
            foreach (var w in wheels)
                if (IsDriven(w)) w.DriveTorque = wheelTorque;

            ApplyDifferential(wheels, crankTorque, input);
            return crankTorque;
        }

        /// <summary>
        /// Clutch-pack limited slip: preload resists any speed difference, and the
        /// locking torque grows with the torque being transmitted, more on power
        /// than on coast.
        /// </summary>
        void ApplyDifferential(Wheel[] wheels, float crankTorque, in VehicleInputs input)
        {
            for (int axle = 0; axle < 2; axle++)
            {
                bool front = axle == 0;
                Wheel left = null, right = null;
                foreach (var w in wheels)
                {
                    if (w.IsFront != front || !IsDriven(w)) continue;
                    if (w.IsLeft) left = w; else right = w;
                }
                if (left == null || right == null) continue;

                float ramp = crankTorque >= 0f ? _cfg.DiffPowerRamp : _cfg.DiffCoastRamp;
                float capacity = _cfg.DiffPreloadTorque
                               + MathF.Abs(crankTorque * TotalRatio) * ramp;

                float speedDiff = left.AngularVelocity - right.AngularVelocity;
                float lock_ = speedDiff * 40f;   // stiffness, N m per rad/s of difference
                if (lock_ > capacity) lock_ = capacity;
                if (lock_ < -capacity) lock_ = -capacity;

                left.DriveTorque -= lock_;
                right.DriveTorque += lock_;
            }
        }

        void AutoShift(in VehicleInputs input)
        {
            if (ShiftInProgress || Gear <= 0) return;
            if (EngineRpm >= _cfg.RevLimitRpm * 0.97f && Gear < _cfg.GearRatios.Length)
            {
                Gear++;
                _shiftTimer = _cfg.ShiftTimeSeconds;
            }
            else if (EngineRpm <= _cfg.IdleRpm * 1.45f && Gear > 1)
            {
                Gear--;
                _shiftTimer = _cfg.ShiftTimeSeconds;
            }
        }

        public void Reset()
        {
            Slipping = false;
            Engaged = false;
            ClutchLock = 0f;
            Gear = 1;
            EngineRpm = _cfg.IdleRpm;
            _shiftTimer = 0f;
        }
    }
}
