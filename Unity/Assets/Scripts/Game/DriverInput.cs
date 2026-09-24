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

        [Header("Gamepad buttons, XInput numbering")]
        [SerializeField] KeyCode handbrakeButton = KeyCode.JoystickButton0;   // A
        [SerializeField] KeyCode shiftUpButton = KeyCode.JoystickButton5;     // RB
        [SerializeField] KeyCode shiftDownButton = KeyCode.JoystickButton4;   // LB
        [SerializeField] KeyCode respawnButton = KeyCode.JoystickButton6;     // View

        /// <summary>Set the frame the key went down, cleared when the car acts on it.</summary>
        public bool ShiftUpRequested { get; private set; }
        public bool ShiftDownRequested { get; private set; }
        public bool RespawnRequested { get; private set; }

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
                Steer = Input.GetAxisRaw("Horizontal"),
                Throttle = throttle,
                Brake = brake,
                Handbrake = Input.GetKey(handbrakeKey) || Input.GetKey(handbrakeButton) ? 1f : 0f,
                Clutch = false,
            };
        }
    }
}
