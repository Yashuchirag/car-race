using UnityEngine;
using CarRace.Vehicle;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Turns keyboard and gamepad state into the normalised inputs the model takes.
    ///
    /// Uses the old input manager, because "Horizontal", "Vertical" and "Jump" exist in
    /// every project without authoring an asset first, so a car drives the moment the
    /// scene runs. Analogue triggers are a separate path: Windows reports them as
    /// joystick axes that have to be added by hand, and reading an axis name that does
    /// not exist throws, so they stay behind a flag until those entries are made.
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

        /// <summary>Set the frame the key went down, cleared when the car acts on it.</summary>
        public bool ShiftUpRequested { get; private set; }
        public bool ShiftDownRequested { get; private set; }
        public bool RespawnRequested { get; private set; }

        void Update()
        {
            // Latched rather than read in FixedUpdate: a physics step can run twice in one
            // frame or not at all, which would double a shift or drop it entirely.
            if (Input.GetKeyDown(shiftUpKey)) ShiftUpRequested = true;
            if (Input.GetKeyDown(shiftDownKey)) ShiftDownRequested = true;
            if (Input.GetKeyDown(respawnKey)) RespawnRequested = true;
        }

        public void ConsumeRequests()
        {
            ShiftUpRequested = false;
            ShiftDownRequested = false;
            RespawnRequested = false;
        }

        public VehicleInputs Read()
        {
            float throttle, brake;
            if (useTriggerAxes)
            {
                throttle = Mathf.Clamp01(Input.GetAxisRaw(throttleAxis));
                brake = Mathf.Clamp01(Input.GetAxisRaw(brakeAxis));
            }
            else
            {
                float forward = Input.GetAxisRaw("Vertical");
                throttle = Mathf.Max(forward, 0f);
                brake = Mathf.Max(-forward, 0f);
            }

            return new VehicleInputs
            {
                // Raw, not smoothed: the model already rate limits the steering rack at
                // SteerRatePerSecond, and smoothing here would fight it and add lag on a stick.
                Steer = Input.GetAxisRaw("Horizontal"),
                Throttle = throttle,
                Brake = brake,
                Handbrake = Input.GetKey(handbrakeKey) ? 1f : 0f,
                Clutch = false,
            };
        }
    }
}
