using System;
using UnityEngine;
using CarRace.Vehicle;
using Vec3 = System.Numerics.Vector3;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Runs the vehicle model against a Unity rigidbody.
    ///
    /// The model only ever computes forces; this component owns the body. Every step
    /// it reads the rigidbody's pose, asks the model for a wrench, and hands that back
    /// to the rigidbody. Nothing here decides how the car behaves, which is the whole
    /// point: the same model is what the headless harness validates.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] CarDefinition definition;
        [SerializeField] DriverInput driver;
        [Tooltip("Surfaces the wheels stand on. Must exclude the car's own layer.")]
        [SerializeField] LayerMask groundLayers = 1;

        [Header("Wheel visuals, FL FR RL RR")]
        [SerializeField] Transform[] wheelVisuals = new Transform[4];

        [Header("Assists")]
        [SerializeField] bool automaticGearbox = true;
        [SerializeField] bool antiLockBrakes = true;
        [SerializeField] bool tractionControl = true;
        [Tooltip("Engine drag torque control: eases engine braking off a rear tyre that is near " +
                 "its grip limit, braking and cornering together, so braking into a corner does " +
                 "not push the rear out. A plain lift keeps its engine braking.")]
        [SerializeField] bool engineDragControl = true;
        [Tooltip("With the automatic gearbox: hold brake at a standstill to engage reverse, then " +
                 "brake drives backwards and throttle brakes; throttle at a standstill goes forward again.")]
        [SerializeField] bool brakeToReverse = true;
        [Tooltip("Throttle in reverse, as a share of full. Reverse is geared low and traction " +
                 "control only watches forward wheelspin, so a full key spins the rear wheels.")]
        [SerializeField, Range(0.1f, 1f)] float reverseThrottle = 0.25f;

        [Tooltip("Rate the vehicle model runs at. Below about 300 the tyre model goes unstable, " +
                 "which reads as a car that will not settle. Substepped inside each physics step.")]
        [SerializeField, Range(200, 1000)] int modelHz = 500;

        [Tooltip("Below this height the car has left the world, and is put back on the grid. " +
                 "Past the verges of a circuit there is no ground, and a car that went over the " +
                 "edge once fell for half a minute before anyone pressed respawn.")]
        [SerializeField] float fallLimitY = -30f;

        public VehicleSim Sim { get; private set; }

        /// <summary>
        /// Where Recover puts the car, if the scene knows better than the start: a circuit
        /// supplies the nearest point on the track, pointing along the lap. Null means the
        /// start. A delegate rather than a reference so this script needs nothing from the
        /// race code.
        /// </summary>
        public Func<(Vector3 position, Quaternion rotation)> RecoveryPose;

        /// <summary>
        /// Drives the car in place of the keyboard when set: given the body this step and the
        /// step length, returns the inputs. An AI car has no DriverInput and sets this instead.
        /// A delegate for the same reason as RecoveryPose.
        /// </summary>
        public Func<BodyState, float, VehicleInputs> Autopilot;

        public float SpeedKph => _body != null ? _body.linearVelocity.magnitude * 3.6f : 0f;

        Rigidbody _body;
        UnityGround _ground;
        CarConfig _config;
        readonly float[] _spinDegrees = new float[4];
        Vector3 _spawnPosition;
        float _stoppedOnBrake;
        Quaternion _spawnRotation;

        void Awake()
        {
            Bridge.VerifyConventions();

            if (definition == null)
            {
                Debug.LogError($"{name}: no Car Definition assigned, the car cannot be built.");
                enabled = false;
                return;
            }

            _config = definition.ToConfig();
            Sim = new VehicleSim(_config);
            _body = GetComponent<Rigidbody>();
            if (driver == null) driver = GetComponent<DriverInput>();
            if (driver != null)
                driver.ConfigureSteering(_config.MaxSteerAngleDegrees, _config.SteerFalloffSpeed, _config.Wheelbase);
            _ground = new UnityGround(groundLayers, _body);

            ConfigureBody();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            if (Time.fixedDeltaTime > 0.01f)
            {
                Debug.LogWarning(
                    $"{name}: Fixed Timestep is {Time.fixedDeltaTime:F4} s. The model substeps " +
                    "internally, but it reads the car's pose only once per physics step, so at " +
                    "this rate load transfer lags by 20 ms and the car feels vague. Set Project " +
                    "Settings, Time, Fixed Timestep to 0.005.");
            }
        }

        /// <summary>
        /// The model measures every wheel and aerodynamic offset from the centre of mass,
        /// so the transform origin has to BE the centre of mass. The inertia tensor is set
        /// from the config for the same reason the config carries one: the tensor Unity
        /// derives from a box collider is far too large in yaw and the car turns like a bus.
        /// </summary>
        void ConfigureBody()
        {
            _body.mass = _config.Mass;
            _body.centerOfMass = Vector3.zero;
            _body.inertiaTensor = Bridge.ToUnity(_config.Inertia);
            _body.inertiaTensorRotation = Quaternion.identity;
            _body.useGravity = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.maxAngularVelocity = 20f;
        }

        void FixedUpdate()
        {
            Sim.AutomaticGearbox = automaticGearbox;
            Sim.AntiLockBrakes = antiLockBrakes;
            Sim.TractionControl = tractionControl;
            Sim.EngineDragControl = engineDragControl;

            if (transform.position.y < fallLimitY) Recover();

            if (driver != null)
                driver.Tick(Time.fixedDeltaTime, Bridge.ReadBody(_body));
            VehicleInputs inputs = Autopilot != null ? Autopilot(Bridge.ReadBody(_body), Time.fixedDeltaTime)
                                 : driver != null ? driver.Read() : VehicleInputs.Coasting;

            if (driver != null)
            {
                if (driver.RespawnRequested) Recover();
                if (driver.RestartRequested) Respawn();
                if (!automaticGearbox)
                {
                    if (driver.ShiftUpRequested) Sim.Drivetrain.Shift(1);
                    if (driver.ShiftDownRequested) Sim.Drivetrain.Shift(-1);
                }
                driver.ConsumeRequests();
            }

            float dt = Time.fixedDeltaTime;
            if (brakeToReverse && automaticGearbox && Autopilot == null && driver != null)
                inputs = BrakeToReverse(inputs, dt);

            int substeps = Mathf.Max(1, Mathf.RoundToInt(modelHz * dt));
            float subDt = dt / substeps;

            // The pose is read once and held across the substeps. Unity integrates the body
            // itself, so there is no correct pose to read in between, and asking the model
            // to predict one would mean a second integrator disagreeing with PhysX. What
            // the substeps are for is wheel spin and tyre relaxation, which are stiff and
            // do need the rate. Holding the chassis pose for one physics step is why the
            // Fixed Timestep warning above matters.
            BodyState body = Bridge.ReadBody(_body);

            Vec3 force = Vec3.Zero;
            Vec3 torque = Vec3.Zero;
            for (int i = 0; i < substeps; i++)
            {
                Wrench w = Sim.Step(body, inputs, _ground, subDt);
                force += w.Force;
                torque += w.Torque;
            }

            // Averaged, not summed: each substep reports the forces acting at that instant,
            // and they are applied over the whole physics step.
            _body.AddForce(Bridge.ToUnity(force / substeps), ForceMode.Force);

            // A wrench is already reduced to a force through the centre of mass plus a
            // torque about it, so AddForce and AddTorque are exactly equivalent to applying
            // each wheel force at its contact patch, and cheaper.
            _body.AddTorque(Bridge.ToUnity(torque / substeps), ForceMode.Force);

            TrackWheelSpin(dt);
        }

        const float StoppedMs = 0.5f;
        const float ReverseAfterSeconds = 0.3f;

        /// <summary>
        /// Reverse on the brake key, as in most driving games. Without it a keyboard car that
        /// stopped nose first against a wall could not get away: turning needs the car to move,
        /// forward was the wall, and reverse was only on the manual gearbox's shift key.
        ///
        /// Braked to a standstill, or rolling backwards, for ReverseAfterSeconds, the car selects
        /// reverse; from then brake drives backwards at reverseThrottle and throttle brakes, and
        /// throttle once stopped selects first again. The gear is set directly rather than
        /// through Shift, which would go through neutral one shift time at a time: at a
        /// standstill no shift time is being skipped.
        /// </summary>
        VehicleInputs BrakeToReverse(VehicleInputs inputs, float dt)
        {
            float forward = Vector3.Dot(_body.linearVelocity, transform.forward);
            var gearbox = Sim.Drivetrain;

            if (gearbox.Gear > 0)
            {
                // Rolling backwards counts as stopped: braked to a standstill the model's tyres
                // rock the car back at up to 4.5 km/h for half a second (PROGRESS.md, section 6),
                // and waiting for that to settle made reverse feel late.
                bool holding = inputs.Brake > 0.5f && inputs.Throttle < 0.05f && forward < StoppedMs;
                _stoppedOnBrake = holding ? _stoppedOnBrake + dt : 0f;
                if (_stoppedOnBrake >= ReverseAfterSeconds)
                {
                    gearbox.Gear = -1;
                    _stoppedOnBrake = 0f;
                }
            }
            else if (gearbox.Gear < 0 && inputs.Throttle > 0.5f && forward > -StoppedMs)
            {
                gearbox.Gear = 1;
            }

            if (gearbox.Gear < 0)
            {
                float back = inputs.Brake;
                inputs.Brake = inputs.Throttle;
                inputs.Throttle = back * reverseThrottle;
            }
            return inputs;
        }

        void TrackWheelSpin(float dt)
        {
            for (int i = 0; i < 4; i++)
                _spinDegrees[i] += Sim.Wheels[i].AngularVelocity * dt * Mathf.Rad2Deg;
        }

        void Update()
        {
            if (Sim == null || wheelVisuals == null) return;

            for (int i = 0; i < wheelVisuals.Length && i < 4; i++)
            {
                Transform visual = wheelVisuals[i];
                if (visual == null) continue;

                Wheel wheel = Sim.Wheels[i];
                Vec3 attach = wheel.LocalAttachment;

                // The hub hangs one rest length below the strut top at full droop and rises
                // with compression. The tyre radius is not part of this: it is where the hub
                // is, not where the contact patch is.
                float drop = _config.SuspensionRestLength - wheel.Compression;
                visual.localPosition = new Vector3(attach.X, attach.Y - drop, attach.Z);

                // Euler applies Z, then X, then Y, so the steer angle wraps the spin rather
                // than the wheel spinning about an already steered axis.
                visual.localRotation = Quaternion.Euler(_spinDegrees[i],
                                                        wheel.SteerAngle * Mathf.Rad2Deg, 0f);
            }
        }

        /// <summary>Puts the car back where it started, at rest.</summary>
        public void Respawn() => PlaceAt(_spawnPosition, _spawnRotation);

        /// <summary>
        /// Puts the car back on the track where it is, at rest and pointing the right way, or
        /// at the start when the scene has no track. After a spin the car used to be sent back
        /// to the start, which threw the lap away, so a spin felt unrecoverable when it was not.
        /// </summary>
        public void Recover()
        {
            if (RecoveryPose == null) { Respawn(); return; }
            var pose = RecoveryPose();
            PlaceAt(pose.position, pose.rotation);
        }

        void PlaceAt(Vector3 position, Quaternion rotation)
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.position = position;
            _body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Sim.Reset();
            for (int i = 0; i < 4; i++) _spinDegrees[i] = 0f;
        }
    }
}
