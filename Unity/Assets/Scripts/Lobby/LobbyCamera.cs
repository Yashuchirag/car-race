using System.Collections.Generic;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The lobby's camera, orbiting the car in the garage: drag with either mouse button to go
    /// round it and up and down, scroll to come closer or stand back, or glide to a view
    /// (FRONT, SIDE, REAR, TOP). Left alone for IdleSeconds it drifts round the car on its own.
    ///
    /// It never leaves the garage: at any angle the distance is cut to keep the camera inside
    /// Room, so the side view comes in closer where the walls are near. The car is drawn right of
    /// the screen's middle (ShiftRight), clear of the menu panel on the left, by aiming the
    /// camera a little left of the car rather than at it.
    ///
    /// Drags that start on the menu are the menu's: LobbyMenu lists its panels each frame
    /// (Block), and a press on one of them does not turn the camera.
    /// </summary>
    public sealed class LobbyCamera : MonoBehaviour
    {
        public enum View { Front, Side, Rear, Top }

        /// <summary>The car's heading, degrees: the views are taken from it.</summary>
        public float CarYaw = 345f;
        public Vector3 Focus = new Vector3(0f, 0.6f, 0f);
        public float ShiftRight = 0.16f;

        const float DragDegreesPerPixel = 0.25f;
        const float MinPitch = 2f, MaxPitch = 70f;
        const float MinDistance = 4f, MaxDistance = 9.5f;
        const float IdleSeconds = 6f, IdleDegreesPerSecond = 5f;
        const float GlideSharpness = 6f;

        /// <summary>Where the camera may stand: inside the garage's walls and under its lights.</summary>
        static readonly Bounds Room = new Bounds(new Vector3(0f, 2.55f, 0f), new Vector3(11.2f, 4.3f, 17.2f));

        float _yaw, _pitch = 9f, _distance = 8.5f;          // where it is going
        float _shownYaw, _shownPitch, _shownDistance;      // where it is, easing towards that
        float _idle;
        float _fov = NormalFov;
        Camera _camera;
        const float NormalFov = 44f, TopFov = 70f;   // from above the ceiling keeps it close: a wider lens
        bool _dragging;
        Vector3 _lastMouse;
        readonly List<Rect> _blocked = new List<Rect>(), _blocking = new List<Rect>();

        /// <summary>True while a drag is turning the camera, so the menu can ignore that press.</summary>
        public bool Dragging => _dragging;

        /// <summary>Set up for a car heading <paramref name="carYaw"/>, at the opening view; called
        /// by LobbyMenu straight after adding this, before anything asks for a view.</summary>
        public void Begin(float carYaw)
        {
            CarYaw = carYaw;
            _yaw = _shownYaw = CarYaw + 35f;
            _shownPitch = _pitch;
            _shownDistance = _distance;
            Place();
        }

        /// <summary>A rectangle of the menu, in GUI coordinates, for this frame: presses there are
        /// not drags. Called on each repaint by LobbyMenu.</summary>
        public void Block(Rect rect) => _blocking.Add(rect);

        /// <summary>Called by LobbyMenu at the start of each repaint, before it lists its panels.</summary>
        public void BeginBlocks()
        {
            _blocked.Clear();
            _blocked.AddRange(_blocking);
            _blocking.Clear();
        }

        /// <summary>Glides to one of the set views.</summary>
        public void Show(View view)
        {
            _idle = 0f;
            switch (view)
            {
                case View.Front: _yaw = CarYaw + 25f; _pitch = 8f; _distance = 7.5f; break;
                case View.Side: _yaw = CarYaw + 90f; _pitch = 6f; _distance = 8.5f; break;
                case View.Rear: _yaw = CarYaw + 205f; _pitch = 10f; _distance = 7.5f; break;
                case View.Top: _yaw = CarYaw + 60f; _pitch = 52f; _distance = 7f; break;
            }
            _fov = view == View.Top ? TopFov : NormalFov;
            // The short way round from where it is.
            _yaw = _shownYaw + Mathf.DeltaAngle(_shownYaw, _yaw);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 mouse = Input.mousePosition;
            var gui = new Vector2(mouse.x, Screen.height - mouse.y);
            bool pressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1);

            if (pressed && !_blocked.Exists(r => r.Contains(gui)) && GUIUtility.hotControl == 0)
            {
                _dragging = true;
                _lastMouse = mouse;
            }
            if (!held) _dragging = false;
            if (_dragging)
            {
                Vector3 moved = mouse - _lastMouse;
                _lastMouse = mouse;
                _yaw += moved.x * DragDegreesPerPixel;
                _pitch = Mathf.Clamp(_pitch - moved.y * DragDegreesPerPixel, MinPitch, MaxPitch);
                _idle = 0f;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && !_blocked.Exists(r => r.Contains(gui)))
            {
                _distance = Mathf.Clamp(_distance * Mathf.Pow(0.9f, scroll), MinDistance, MaxDistance);
                _idle = 0f;
            }

            _idle += dt;
            if (_idle > IdleSeconds) _yaw += IdleDegreesPerSecond * dt;

            float ease = 1f - Mathf.Exp(-GlideSharpness * dt);
            _shownYaw = Mathf.Lerp(_shownYaw, _yaw, _dragging ? 1f : ease);
            _shownPitch = Mathf.Lerp(_shownPitch, _pitch, _dragging ? 1f : ease);
            _shownDistance = Mathf.Lerp(_shownDistance, _distance, ease);
            if (_dragging || scroll != 0f) _fov = NormalFov;
            if (_camera == null) _camera = GetComponent<Camera>();
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _fov, ease);
            Place();
        }

        void Place()
        {
            float yaw = _shownYaw * Mathf.Deg2Rad, pitch = _shownPitch * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            float distance = Mathf.Min(_shownDistance, Inside(direction));
            Vector3 eye = Focus + direction * distance;
            Vector3 right = Vector3.Cross(Vector3.up, -direction).normalized;
            transform.position = eye;
            transform.LookAt(Focus - right * (ShiftRight * distance));
        }

        /// <summary>How far from the car along <paramref name="direction"/> the camera can go and
        /// still be inside the garage.</summary>
        float Inside(Vector3 direction)
        {
            float most = MaxDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = direction[axis];
                if (Mathf.Abs(d) < 1e-4f) continue;
                float wall = d > 0f ? Room.max[axis] : Room.min[axis];
                most = Mathf.Min(most, (wall - Focus[axis]) / d);
            }
            return Mathf.Max(most, 2.5f);
        }
    }
}
