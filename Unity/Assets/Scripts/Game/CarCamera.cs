using System;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Chase, hood and cockpit views for one car.
    ///
    /// The chase camera follows where the car is going rather than where it points. A
    /// camera rigidly parented behind the car shows a beautifully stable view in which
    /// a slide is invisible, because the car and the camera slide together. Lagging the
    /// rig and aiming it down the velocity vector is what makes oversteer readable, and
    /// being able to catch a slide is the whole exercise.
    /// </summary>
    public sealed class CarCamera : MonoBehaviour
    {
        public enum View { Chase, Hood, Cockpit }

        [SerializeField] Transform target;
        [SerializeField] Rigidbody targetBody;
        [SerializeField] View view = View.Chase;
        [SerializeField] KeyCode cycleKey = KeyCode.C;
        [SerializeField] KeyCode cycleButton = KeyCode.JoystickButton3;   // Y on an XInput pad

        [Header("Chase")]
        [SerializeField] Vector3 chaseOffset = new Vector3(0f, 1.55f, -5.4f);
        [Tooltip("Seconds for the rig to catch up. Higher lags more and reads slides more clearly.")]
        [SerializeField, Range(0.02f, 0.5f)] float followLag = 0.12f;
        [SerializeField, Range(0f, 1f)] float lookIntoCorner = 0.55f;

        [Header("Mounted views")]
        [SerializeField] Vector3 hoodOffset = new Vector3(0f, 1.15f, 0.45f);
        [SerializeField] Vector3 cockpitOffset = new Vector3(-0.36f, 1.05f, -0.25f);

        [Header("Field of view")]
        [SerializeField] float baseFov = 62f;
        [Tooltip("Degrees added at 300 km/h, scaled linearly with speed. Sells speed without " +
                 "the pumping a nonlinear curve gives on corner exit.")]
        [SerializeField] float fovGainAtTopSpeed = 22f;

        [Header("Speed feel")]
        [Tooltip("Road shake starts at this speed, m/s, and grows to shakeAtTopSpeed at 300 km/h.")]
        [SerializeField] float shakeFromSpeed = 100f / 3.6f;
        [Tooltip("Metres of shake at 300 km/h, up and down and side to side.")]
        [SerializeField] float shakeAtTopSpeed = 0.05f;
        [SerializeField] float shakeHz = 11f;

        Camera _camera;
        Vector3 _rigPosition;
        float _yaw;   // smoothed heading, radians about the vertical axis

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (target == null)
            {
                Debug.LogError($"{name}: no target assigned, the camera has nothing to follow.");
                enabled = false;
                return;
            }
            if (targetBody == null) targetBody = target.GetComponent<Rigidbody>();

            _rigPosition = target.TransformPoint(chaseOffset);
            _yaw = YawOf(target.forward);
        }

        void Update()
        {
            if (Input.GetKeyDown(cycleKey) || Input.GetKeyDown(cycleButton))
                view = (View)(((int)view + 1) % 3);
        }

        // LateUpdate, after the rigidbody's interpolated transform has been written for
        // this frame. Following it in Update would trail one frame behind and judder.
        void LateUpdate()
        {
            Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            float speed = velocity.magnitude;

            if (view == View.Chase) FollowChase(velocity, speed);
            else MountRigidly();
            transform.position += Shake(speed);

            if (_camera != null)
                _camera.fieldOfView = baseFov + fovGainAtTopSpeed * Mathf.Clamp01(speed / 83.3f);
        }

        void FollowChase(Vector3 velocity, float speed)
        {
            // Heading, not the car's facing: below walking pace there is no meaningful
            // heading, so fall back to facing or the camera swims while parked. And facing
            // again once the car is travelling backwards, after a spin: following the
            // velocity there put the camera in front of the car, looking at its nose.
            Vector3 heading = speed > 1.5f && Vector3.Dot(velocity, target.forward) > 0f
                ? velocity : target.forward;

            // Smoothed as an angle about the vertical, the short way round. Slerping
            // directions instead broke in a spin: facing and heading point opposite ways,
            // a slerp between opposites has no defined path, and it swung the camera through
            // the sky.
            float follow = 1f - Mathf.Exp(-Time.deltaTime / followLag);
            _yaw += Wrap(YawOf(heading) - _yaw) * follow;
            Vector3 smoothedHeading = new Vector3(MathF.Sin(_yaw), 0f, MathF.Cos(_yaw));

            // The smoothing below leaves the rig speed x followLag behind the car at a steady
            // speed, 7 m more at 200 km/h than at rest, and a camera that falls back shrinks
            // the car and the road around it just as the speed rises. Leading the anchor by
            // that much along the heading holds the distance; accelerating or braking still
            // swings the camera back or in.
            float along = Vector3.Dot(velocity, smoothedHeading);
            Vector3 anchor = target.position
                           + smoothedHeading * (chaseOffset.z + along * followLag)
                           + Vector3.up * chaseOffset.y
                           + target.right * chaseOffset.x;

            // Exponential smoothing framed as a half life, so the lag is the same whatever
            // the frame rate. Lerping by a raw per-frame factor makes the camera tighter at
            // 200 fps than at 60, which is why it feels different in a build than in the editor.
            _rigPosition = Vector3.Lerp(_rigPosition, anchor,
                                        1f - Mathf.Exp(-Time.deltaTime / followLag));
            transform.position = _rigPosition;

            // Aim partway from the car's facing towards the heading, by angle, level.
            float facing = YawOf(target.forward);
            float aimYaw = facing + Wrap(_yaw - facing) * lookIntoCorner;
            transform.rotation = Quaternion.LookRotation(new Vector3(MathF.Sin(aimYaw), 0f, MathF.Cos(aimYaw)), Vector3.up);
        }

        /// <summary>A fine shake from the road above shakeFromSpeed, growing with the square
        /// of the speed beyond it: nothing at 100 km/h, a quarter at 200, all of it at 300.</summary>
        Vector3 Shake(float speed)
        {
            float amount = Mathf.Clamp01((speed - shakeFromSpeed) / (83.3f - shakeFromSpeed));
            if (amount <= 0f) return Vector3.zero;
            float t = Time.time * shakeHz;
            float up = Mathf.PerlinNoise(t, 0.3f) - 0.5f, side = Mathf.PerlinNoise(0.7f, t) - 0.5f;
            return (transform.up * up + transform.right * side) * (2f * shakeAtTopSpeed * amount * amount);
        }

        static float YawOf(Vector3 direction) => MathF.Atan2(direction.x, direction.z);

        /// <summary>An angle difference brought into -pi to pi, so a turn goes the short way.</summary>
        static float Wrap(float radians)
        {
            while (radians > MathF.PI) radians -= 2f * MathF.PI;
            while (radians < -MathF.PI) radians += 2f * MathF.PI;
            return radians;
        }

        void MountRigidly()
        {
            Vector3 offset = view == View.Hood ? hoodOffset : cockpitOffset;
            transform.SetPositionAndRotation(target.TransformPoint(offset), target.rotation);
        }
    }
}
