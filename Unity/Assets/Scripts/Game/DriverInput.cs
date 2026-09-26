using System;
using UnityEngine;
using CarRace.Vehicle;
using Vec3 = System.Numerics.Vector3;

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
        [Tooltip("Recover: back onto the track where the car is, stopped and pointing the right " +
                 "way. On a scene with no track, back to the start.")]
        [SerializeField] KeyCode respawnKey = KeyCode.R;
        [Tooltip("Restart: back to the start.")]
        [SerializeField] KeyCode restartKey = KeyCode.Backspace;

        [Header("Steering assist")]
        [Tooltip("Steer for a turn rate rather than a wheel angle, and counter-steer when the car " +
                 "rotates more than asked or starts to slide. For the keyboard, whose keys are all " +
                 "or nothing. Untick for a wheel or a pad driven with care.")]
        [SerializeField] bool steeringAssist = true;
        [Tooltip("Seconds for the key's request to go from centre to full, and from full back to centre.")]
        [SerializeField] float steerRampSeconds = 0.25f;
        [SerializeField] float steerReturnSeconds = 0.12f;
        [Tooltip("Cornering acceleration the car can hold, m/s^2. The reference car measures " +
                 "0.94 g on the skidpad.")]
        [SerializeField] float lateralGripMs2 = 9.2f;
        [Tooltip("Share of that grip a full key asks for. Past about 1.2 the car corners no harder, " +
                 "it only slides more.")]
        [SerializeField] float cornerReach = 1.2f;
        [Tooltip("How hard the wheels correct a turn rate that differs from the one asked for. " +
                 "1 corrects the whole difference through the car's own steering response.")]
        [SerializeField] float yawGain = 1f;
        [Tooltip("How much the wheels turn towards where the car is actually going, per unit of " +
                 "sideslip beyond the dead band. 1 points them along the car's path.")]
        [SerializeField] float slipGain = 1f;
        [SerializeField] float slipDeadDegrees = 2f;

        [Tooltip("Ease the throttle off while the car slides, as stability control does: full power " +
                 "below the first sideslip angle, none by the second. A key cannot feed the throttle " +
                 "in, and full power on the grass spun the car every time.")]
        [SerializeField] bool throttleAssist = true;
        [SerializeField] float throttleCutStartDegrees = 3f;
        [SerializeField] float throttleCutEndDegrees = 8f;

        // Sideslip only means a slide once the car is moving forwards. Pulling away with lock
        // on, the car moves sideways against its nose while barely rolling, so the angle read
        // huge: the throttle cut left it standing, or rolling back down a slope, and the
        // counter-steer fought the turn. Both come in between these forward speeds.
        const float SlipAssistFromMs = 4f, SlipAssistFullMs = 10f;

        [Header("Gamepad buttons, XInput numbering")]
        [SerializeField] KeyCode handbrakeButton = KeyCode.JoystickButton0;   // A
        [SerializeField] KeyCode shiftUpButton = KeyCode.JoystickButton5;     // RB
        [SerializeField] KeyCode shiftDownButton = KeyCode.JoystickButton4;   // LB
        [SerializeField] KeyCode respawnButton = KeyCode.JoystickButton6;     // View
        [SerializeField] KeyCode restartButton = KeyCode.JoystickButton7;     // Menu

        /// <summary>Set the frame the key went down, cleared when the car acts on it.</summary>
        public bool ShiftUpRequested { get; private set; }
        public bool ShiftDownRequested { get; private set; }
        public bool RespawnRequested { get; private set; }
        public bool RestartRequested { get; private set; }

        float _steer, _request, _sideslipDegrees, _slipWeight;

        // -driveScript "5:1,-1,0;11:0,0,1": from 5 s after the scene loads throttle 1, steer -1
        // (left) and no brake, from 11 s the brake alone. It replaces the keyboard, so an input
        // bug can be reproduced in a run nobody is sitting at; CarController logs what the car
        // did with it.
        static readonly (float t, float throttle, float steer, float brake)[] Script = ReadScript();
        public static bool Scripted => Script.Length > 0;

        static (float, float, float, float)[] ReadScript()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-driveScript");
            if (i < 0 || i + 1 >= args.Length) return Array.Empty<(float, float, float, float)>();
            var steps = new System.Collections.Generic.List<(float, float, float, float)>();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string step in args[i + 1].Split(';'))
            {
                string[] timeAndValues = step.Split(':');
                string[] v = timeAndValues[1].Split(',');
                steps.Add((float.Parse(timeAndValues[0], culture), float.Parse(v[0], culture), float.Parse(v[1], culture), float.Parse(v[2], culture)));
            }
            return steps.ToArray();
        }

        static (float t, float throttle, float steer, float brake) Scripting()
        {
            float now = Time.timeSinceLevelLoad;
            var current = (0f, 0f, 0f, 0f);
            foreach (var step in Script) if (step.t <= now) current = step;
            return current;
        }

        static float RawSteer() => Scripted ? Scripting().steer : Input.GetAxisRaw("Horizontal");
        static float RawForward() => Scripted ? Scripting().throttle - Scripting().brake : Input.GetAxisRaw("Vertical");
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
        /// Why it works this way, from telemetry at Monza and the same inputs replayed headlessly.
        /// The first assist gave a full key the lock the corner needs plus 8 degrees of front
        /// slip. At 175 km/h that is 8 degrees at the wheels, where the corner at the grip limit
        /// needs half of one, so a 0.2 s tap put the fronts at their peak, the car yawed far past
        /// what the tyres could hold, and with the keys released it stayed in a 15 degree slide.
        /// The model is right to do that: a 1 or 2 degree pulse at that speed recovers by itself,
        /// and a real car given a quarter turn of the wheel at 175 km/h spins too.
        ///
        /// So a key now asks for a turn rate, cornerReach times the grip limit, lateral grip over
        /// speed. The wheels get the angle that turn needs, plus a correction for the difference
        /// between the turn rate asked for and the one the car has, plus a turn towards where the
        /// car is actually going once it slides past the dead band. The last two are the counter-
        /// steer a keyboard cannot give. Replayed headlessly, the old assist spun the car in 7 of
        /// 10 keyboard scenarios and this one in none, while a held key still corners at 0.77 to
        /// 0.91 g depending on speed.
        /// </summary>
        public void Tick(float dt, in BodyState body)
        {
            float speed = Vec3.Dot(body.Velocity, body.Forward);
            float sideslip = body.Velocity.LengthSquared() < 1f ? 0f
                           : MathF.Atan2(Vec3.Dot(body.Velocity, body.Right), MathF.Abs(speed));
            _sideslipDegrees = MathF.Abs(sideslip) * (180f / MathF.PI);
            // Forward speed, signed: never while reversing, where the throttle key is the brake
            // and cutting it left a car rolling backwards with the key held down.
            _slipWeight = MathF.Min(1f, MathF.Max(0f, (speed - SlipAssistFromMs) / (SlipAssistFullMs - SlipAssistFromMs)));

            float raw = RawSteer();
            if (!steeringAssist) { _steer = raw; return; }

            // The key's request ramps, so a tap asks for a little and a hold for everything.
            bool outward = MathF.Abs(raw) > MathF.Abs(_request) && raw * _request >= 0f;
            float rate = 1f / MathF.Max(outward ? steerRampSeconds : steerReturnSeconds, 0.01f);
            _request += MathF.Max(-rate * dt, MathF.Min(rate * dt, raw - _request));

            // Backing up, the car turns the other way for the same lock, so the turn rate
            // feedback below would push the wrong way. Plain ramped lock is what reversing needs.
            if (speed < -0.5f) { _steer = _request; return; }

            float v = MathF.Max(MathF.Abs(speed), 3f);
            float yawRate = Vec3.Dot(body.AngularVelocity, body.Up);

            float wantedYaw = _request * cornerReach * lateralGripMs2 / v;
            float wheel = MathF.Atan(_wheelbase * wantedYaw / v)
                        + yawGain * _wheelbase / v * (wantedYaw - yawRate);
            float dead = slipDeadDegrees * (MathF.PI / 180f);
            if (MathF.Abs(sideslip) > dead)
                wheel += _slipWeight * slipGain * (sideslip - MathF.Sign(sideslip) * dead);

            // As a share of the lock the model hands out at this speed.
            float full = _maxSteerDegrees / (1f + v / MathF.Max(_steerFalloffSpeed, 0.01f)) * (MathF.PI / 180f);
            _steer = MathF.Max(-1f, MathF.Min(1f, wheel / MathF.Max(full, 1e-4f)));
        }

        void Update()
        {
            // Latched rather than read in FixedUpdate: a physics step can run twice in one
            // frame or not at all, which would double a shift or drop it entirely.
            if (Input.GetKeyDown(shiftUpKey) || Input.GetKeyDown(shiftUpButton)) ShiftUpRequested = true;
            if (Input.GetKeyDown(shiftDownKey) || Input.GetKeyDown(shiftDownButton)) ShiftDownRequested = true;
            if (Input.GetKeyDown(respawnKey) || Input.GetKeyDown(respawnButton)) RespawnRequested = true;
            if (Input.GetKeyDown(restartKey) || Input.GetKeyDown(restartButton)) RestartRequested = true;
        }

        public void ConsumeRequests()
        {
            ShiftUpRequested = false;
            ShiftDownRequested = false;
            RespawnRequested = false;
            RestartRequested = false;
        }

        public VehicleInputs Read()
        {
            float forward = RawForward();
            float throttle = Mathf.Max(forward, 0f);
            float brake = Mathf.Max(-forward, 0f);
            if (useTriggerAxes && !Scripted)
            {
                throttle = Mathf.Max(throttle, Mathf.Clamp01(Input.GetAxisRaw(throttleAxis)));
                brake = Mathf.Max(brake, Mathf.Clamp01(Input.GetAxisRaw(brakeAxis)));
            }
            if (throttleAssist)
                throttle *= 1f - _slipWeight * Mathf.Clamp01((_sideslipDegrees - throttleCutStartDegrees)
                                                             / Mathf.Max(throttleCutEndDegrees - throttleCutStartDegrees, 0.1f));

            return new VehicleInputs
            {
                // Raw, not smoothed: the model already rate limits the steering rack at
                // SteerRatePerSecond, and smoothing here would fight it and add lag on a stick.
                Steer = steeringAssist ? _steer : RawSteer(),
                Throttle = throttle,
                Brake = brake,
                Handbrake = Input.GetKey(handbrakeKey) || Input.GetKey(handbrakeButton) ? 1f : 0f,
                Clutch = false,
            };
        }
    }
}
