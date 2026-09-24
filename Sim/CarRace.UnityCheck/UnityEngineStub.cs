// Signatures only, for type-checking the Unity integration layer on a machine with no
// Unity on it. NOTHING HERE RUNS. Every method throws, deliberately, so that this can
// never be mistaken for a test of behaviour: it proves the integration scripts compile
// and that they use the vehicle model's API correctly, and it proves nothing else.
//
// Unity's real UnityEngine.dll shadows this at runtime, so a signature here that is
// subtly wrong hides a real compile error until the scripts are opened in the editor.
// When that happens, fix the signature here rather than deleting the check.
using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static Vector3 zero => default;
        public static Vector3 one => default;
        public static Vector3 up => default;
        public static Vector3 right => default;
        public static Vector3 forward => default;

        public float magnitude => throw new NotImplementedException();
        public float sqrMagnitude => throw new NotImplementedException();
        public Vector3 normalized => throw new NotImplementedException();

        public static float Distance(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => throw new NotImplementedException();
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => throw new NotImplementedException();

        public static Vector3 operator +(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 operator -(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 operator *(Vector3 v, float s) => throw new NotImplementedException();
        public static Vector3 operator *(float s, Vector3 v) => throw new NotImplementedException();
        public static Vector3 operator /(Vector3 v, float s) => throw new NotImplementedException();
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => throw new NotImplementedException();
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => throw new NotImplementedException();

        public static Vector3 operator *(Quaternion q, Vector3 v) => throw new NotImplementedException();
        public static Quaternion operator *(Quaternion a, Quaternion b) => throw new NotImplementedException();
    }

    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = 0.0174532924f;
        public static float Clamp01(float v) => throw new NotImplementedException();
        public static float Clamp(float v, float lo, float hi) => throw new NotImplementedException();
        public static float Max(float a, float b) => throw new NotImplementedException();
        public static int Max(int a, int b) => throw new NotImplementedException();
        public static float Min(float a, float b) => throw new NotImplementedException();
        public static int RoundToInt(float v) => throw new NotImplementedException();
        public static float Exp(float v) => throw new NotImplementedException();
    }

    public static class Time
    {
        public static float fixedDeltaTime => throw new NotImplementedException();
        public static float deltaTime => throw new NotImplementedException();
    }

    public static class Debug
    {
        public static void Log(object message) => throw new NotImplementedException();
        public static void LogWarning(object message) => throw new NotImplementedException();
        public static void LogError(object message) => throw new NotImplementedException();
    }

    public class Object
    {
        public string name { get; set; }
    }

    public class Component : Object
    {
        public Transform transform => throw new NotImplementedException();
        public GameObject gameObject => throw new NotImplementedException();
        public T GetComponent<T>() => throw new NotImplementedException();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour { }

    public class ScriptableObject : Object { }

    public class GameObject : Object
    {
        public T GetComponent<T>() => throw new NotImplementedException();
        public int layer { get; set; }
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 forward => throw new NotImplementedException();
        public Vector3 right => throw new NotImplementedException();
        public Vector3 up => throw new NotImplementedException();
        public Vector3 TransformPoint(Vector3 local) => throw new NotImplementedException();
        public void SetPositionAndRotation(Vector3 p, Quaternion r) => throw new NotImplementedException();
    }

    public enum ForceMode { Force, Acceleration, Impulse, VelocityChange }
    public enum RigidbodyInterpolation { None, Interpolate, Extrapolate }
    public enum CollisionDetectionMode { Discrete, Continuous, ContinuousDynamic, ContinuousSpeculative }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }

    public class Rigidbody : Component
    {
        public float mass { get; set; }
        public Vector3 centerOfMass { get; set; }
        public Vector3 worldCenterOfMass => throw new NotImplementedException();
        public Vector3 inertiaTensor { get; set; }
        public Quaternion inertiaTensorRotation { get; set; }
        public bool useGravity { get; set; }
        public float maxAngularVelocity { get; set; }
        public RigidbodyInterpolation interpolation { get; set; }
        public CollisionDetectionMode collisionDetectionMode { get; set; }

        // Unity 6 renamed Rigidbody.velocity to linearVelocity.
        public Vector3 linearVelocity { get; set; }
        public Vector3 angularVelocity { get; set; }
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }

        public void AddForce(Vector3 force, ForceMode mode) => throw new NotImplementedException();
        public void AddTorque(Vector3 torque, ForceMode mode) => throw new NotImplementedException();
    }

    // Unity 6 renamed PhysicMaterial to PhysicsMaterial.
    public class PhysicsMaterial : Object
    {
        public float dynamicFriction { get; set; }
        public float staticFriction { get; set; }
    }

    public class Collider : Component
    {
        public PhysicsMaterial sharedMaterial { get; set; }
    }

    public struct RaycastHit
    {
        public float distance => throw new NotImplementedException();
        public Vector3 normal => throw new NotImplementedException();
        public Vector3 point => throw new NotImplementedException();
        public Collider collider => throw new NotImplementedException();
        public Rigidbody rigidbody => throw new NotImplementedException();
    }

    public static class Physics
    {
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction,
                                      out RaycastHit hit, float maxDistance, int layerMask,
                                      QueryTriggerInteraction query)
            => throw new NotImplementedException();

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit,
                                   float maxDistance, int layerMask, QueryTriggerInteraction query)
            => throw new NotImplementedException();
    }

    public struct LayerMask
    {
        public int value { get; set; }
        public static implicit operator int(LayerMask mask) => throw new NotImplementedException();
        public static implicit operator LayerMask(int value) => throw new NotImplementedException();
    }

    public class Camera : Behaviour
    {
        public float fieldOfView { get; set; }
    }

    public enum KeyCode
    {
        None = 0, Space, Q, E, R, C, LeftShift,
        JoystickButton0, JoystickButton1, JoystickButton2, JoystickButton3,
        JoystickButton4, JoystickButton5, JoystickButton6, JoystickButton7,
    }

    public static class Input
    {
        public static float GetAxisRaw(string axis) => throw new NotImplementedException();
        public static float GetAxis(string axis) => throw new NotImplementedException();
        public static bool GetKey(KeyCode key) => throw new NotImplementedException();
        public static bool GetKeyDown(KeyCode key) => throw new NotImplementedException();
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; }
        public string menuName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type type) { }
    }
}
