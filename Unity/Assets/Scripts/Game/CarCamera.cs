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
        [SerializeField] float fovGainAtTopSpeed = 14f;

        Camera _camera;
        Vector3 _rigPosition;
        Vector3 _smoothedHeading;

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
            _smoothedHeading = target.forward;
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

            if (_camera != null)
                _camera.fieldOfView = baseFov + fovGainAtTopSpeed * Mathf.Clamp01(speed / 83.3f);
        }

        void FollowChase(Vector3 velocity, float speed)
        {
            // Heading, not the car's facing: below walking pace there is no meaningful
            // heading, so fall back to facing or the camera swims while parked.
            Vector3 heading = speed > 1.5f ? velocity.normalized : target.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = target.forward;
            _smoothedHeading = Vector3.Slerp(_smoothedHeading, heading.normalized,
                                             1f - Mathf.Exp(-Time.deltaTime / followLag));

            Vector3 anchor = target.position
                           + _smoothedHeading * chaseOffset.z
                           + Vector3.up * chaseOffset.y
                           + target.right * chaseOffset.x;

            // Exponential smoothing framed as a half life, so the lag is the same whatever
            // the frame rate. Lerping by a raw per-frame factor makes the camera tighter at
            // 200 fps than at 60, which is why it feels different in a build than in the editor.
            _rigPosition = Vector3.Lerp(_rigPosition, anchor,
                                        1f - Mathf.Exp(-Time.deltaTime / followLag));
            transform.position = _rigPosition;

            Vector3 aim = Vector3.Slerp(target.forward, _smoothedHeading, lookIntoCorner);
            transform.rotation = Quaternion.LookRotation(aim, Vector3.up);
        }

        void MountRigidly()
        {
            Vector3 offset = view == View.Hood ? hoodOffset : cockpitOffset;
            transform.SetPositionAndRotation(target.TransformPoint(offset), target.rotation);
        }
    }
}
