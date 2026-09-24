using System;
using UnityEngine;
using CarRace.Vehicle;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Turns keyboard and gamepad state into the normalised inputs the model takes.
    ///
    /// Uses the old input manager, because "Horizontal" and "Vertical" exist in every
    /// project without authoring an asset first, and both already read the left stick,
    /// so a car drives the moment the scene runs. Analogue triggers are a separate path:
    /// Windows reports them as joystick axes that have to be added to the Input Manager,
    /// and reading an axis name that does not exist throws, so they stay behind a flag.
    /// The scene builder in Assets/Editor adds them and sets the flag. When on, the
    /// triggers add to the keyboard rather than replace it, so both keep working.
    /// Every action has a key and a gamepad button; the defaults are an XInput pad.
    /// Project Settings, Player, Active Input Handling must include the old manager.
    /// </summary>
    public sealed class DriverInput : MonoBehaviour
    {
        [Tooltip("Read throttle and brake from separate analogue trigger axes. Requires the two " +
                 "axis names below to exist in Project Settings, Input Manager. See Unity/README.md.")]
        [SerializeField] bool useTriggerAxes;
        [SerializeField] string throttleAxis = "Throttle";
        [SerializeField] string brakeAxis = "Brake";

        [SerializeField] KeyCode handbrakeKey = KeyCode.Space;
        [SerializeField] KeyCode shiftUpKey = KeyCode.E;
        [SerializeField] KeyCode shiftDownKey = KeyCode.Q;
        [SerializeField] KeyCode respawnKey = KeyCode.R;

        [Header("Steering assist")]
        [Tooltip("Scale steering to what the front tyres can use at the current speed, and ramp it " +
                 "in rather than jumping. For the keyboard, whose keys are all or nothing. Untick " +
                 "for a wheel or a pad driven with care.")]
        [SerializeField] bool steeringAssist = true;
        [Tooltip("Seconds from centre to full, and from full back to centre.")]
        [SerializeField] float steerRampSeconds = 0.25f;
        [SerializeField] float steerReturnSeconds = 0.12f;
        [Tooltip("Front slip angle a full input asks for, on top of the angle the corner needs. " +
                 "The reference tyre makes 95% of its grip at 8 degrees; beyond that more lock " +
                 "adds almost nothing and the front just pushes on.")]
        [SerializeField] float usefulSlipDegrees = 8f;
        [Tooltip("Cornering acceleration the car can hold, m/s^2. The reference car measures " +
                 "0.94 g on the skidpad.")]
        [SerializeField] float lateralGripMs2 = 9.2f;

        [Header("Gamepad buttons, XInput numbering")]
        [SerializeField] KeyCode handbrakeButton = KeyCode.JoystickButton0;   // A
        [SerializeField] KeyCode shiftUpButton = KeyCode.JoystickButton5;     // RB
        [SerializeField] KeyCode shiftDownButton = KeyCode.JoystickButton4;   // LB
        [SerializeField] KeyCode respawnButton = KeyCode.JoystickButton6;     // View

        /// <summary>Set the frame the key went down, cleared when the car acts on it.</summary>
        public bool ShiftUpRequested { get; private set; }
        public bool ShiftDownRequested { get; private set; }
        public bool RespawnRequested { get; private set; }

        float _steer;
        float _maxSteerDegrees = 33f, _steerFalloffSpeed = 42f, _wheelbase = 2.65f;

        /// <summary>The car's steering geometry, which the assist needs to know how much lock
        /// a full input gives at a given speed. CarController passes it in from the config.</summary>
        public void ConfigureSteering(float maxSteerDegrees, float steerFalloffSpeed, float wheelbase)
        {
            _maxSteerDegrees = maxSteerDegrees;
            _steerFalloffSpeed = steerFalloffSpeed;
            _wheelbase = wheelbase;
        }

        /// <summary>
        /// Advances the assisted steering by one physics step. Called once per step by the car,
        /// before Read, so that Read itself has no side effects and the readout and telemetry
        /// can call it too.
        ///
        /// Why it exists, from telemetry at Monza: a 0.1 s key tap at 150 km/h swung the front
        /// wheels 10 to 13 degrees, when a driver there uses two or three, and four such taps
        /// in two seconds built a weave that ended on the grass. The car itself settled from a
        /// single tap in half a second; the input was the problem, not the physics.
        /// </summary>
        public void Tick(float dt, float speedMs)
        {
            float raw = Input.GetAxisRaw("Horizontal");
            if (!steeringAssist) { _steer = raw; return; }

            // What a full input should give at this speed: the wheel angle the corner needs at
            // the grip limit, radius v^2 / a, plus the useful slip. As a share of the lock the
            // model hands out at this speed, never more than all of it.
            float v = MathF.Max(MathF.Abs(speedMs), 1f);
            float kinematicDegrees = _wheelbase * lateralGripMs2 / (v * v) * (180f / MathF.PI);
            float fullDegrees = _maxSteerDegrees / (1f + v / MathF.Max(_steerFalloffSpeed, 0.01f));
            float limit = MathF.Min(1f, (kinematicDegrees + usefulSlipDegrees) / MathF.Max(fullDegrees, 0.01f));

            // The ramps are measured against that limit, not against the whole input: a quarter
            // second to reach whatever full is at this speed. Measured against the whole input,
            // a 0.1 s tap at 150 km/h still reached three quarters of the limit.
            float target = raw * limit;
            bool outward = MathF.Abs(target) > MathF.Abs(_steer) && target * _steer >= 0f;
            float span = MathF.Max(limit, MathF.Abs(_steer));
            float rate = span / MathF.Max(outward ? steerRampSeconds : steerReturnSeconds, 0.01f);
            float step = rate * dt;
            _steer += MathF.Max(-step, MathF.Min(step, target - _steer));
        }

        void Update()
        {
            // Latched rather than read in FixedUpdate: a physics step can run twice in one
            // frame or not at all, which would double a shift or drop it entirely.
            if (Input.GetKeyDown(shiftUpKey) || Input.GetKeyDown(shiftUpButton)) ShiftUpRequested = true;
            if (Input.GetKeyDown(shiftDownKey) || Input.GetKeyDown(shiftDownButton)) ShiftDownRequested = true;
            if (Input.GetKeyDown(respawnKey) || Input.GetKeyDown(respawnButton)) RespawnRequested = true;
        }

        public void ConsumeRequests()
        {
            ShiftUpRequested = false;
            ShiftDownRequested = false;
            RespawnRequested = false;
        }

        public VehicleInputs Read()
        {
            float forward = Input.GetAxisRaw("Vertical");
            float throttle = Mathf.Max(forward, 0f);
            float brake = Mathf.Max(-forward, 0f);
            if (useTriggerAxes)
            {
                throttle = Mathf.Max(throttle, Mathf.Clamp01(Input.GetAxisRaw(throttleAxis)));
                brake = Mathf.Max(brake, Mathf.Clamp01(Input.GetAxisRaw(brakeAxis)));
            }

            return new VehicleInputs
            {
                // Raw, not smoothed: the model already rate limits the steering rack at
                // SteerRatePerSecond, and smoothing here would fight it and add lag on a stick.
                Steer = steeringAssist ? _steer : Input.GetAxisRaw("Horizontal"),
                Throttle = throttle,
                Brake = brake,
                Handbrake = Input.GetKey(handbrakeKey) || Input.GetKey(handbrakeButton) ? 1f : 0f,
                Clutch = false,
            };
        }
    }
}
