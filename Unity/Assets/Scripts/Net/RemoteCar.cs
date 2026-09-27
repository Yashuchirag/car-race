using CarRace.Net;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Another machine's car on this one: a car with its own model switched off, moved each
    /// physics step to where the network says it is now (Extrapolator), as a kinematic body.
    /// Kinematic, so it is solid to this machine's car and pushes it as a real car would,
    /// but is itself pushed only by its owner, who sees the same touch from their side.
    ///
    /// Its engine note, speed and wheels come from the state it sends, for CarAudio and
    /// RaceDirector, which ask it rather than a model that is not running.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class RemoteCar : MonoBehaviour
    {
        public byte Id { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Rpm { get; private set; }
        public float Throttle { get; private set; }

        /// <summary>True once its first state has arrived; before then it waits on its grid slot.</summary>
        public bool Heard { get; private set; }

        Extrapolator _source;
        System.Func<float, float> _hostTimeOf;
        Rigidbody _body;
        CarController _car;
        float _spinDegrees;

        /// <param name="hostTimeOf">This machine's real time to the host's clock.</param>
        public static RemoteCar Make(CarController car, byte id, Extrapolator source, System.Func<float, float> hostTimeOf)
        {
            car.enabled = false;
            var remote = car.gameObject.AddComponent<RemoteCar>();
            remote.Id = id;
            remote._source = source;
            remote._hostTimeOf = hostTimeOf;
            remote._car = car;
            remote._body = car.GetComponent<Rigidbody>();
            remote._body.isKinematic = true;
            remote._body.interpolation = RigidbodyInterpolation.Interpolate;
            return remote;
        }

        void FixedUpdate()
        {
            // The pose for the end of this step, which is when the body reaches it.
            float hostTime = _hostTimeOf(LanSession.RealTimeOf(Time.fixedTimeAsDouble + Time.fixedDeltaTime));
            if (!_source.Sample(Id, hostTime, out CarState state)) return;

            Heard = true;
            _body.MovePosition(Bridge.ToUnity(state.Position));
            _body.MoveRotation(Bridge.ToUnity(state.Orientation));
            Velocity = Bridge.ToUnity(state.Velocity);
            Rpm = state.EngineRpm;
            Throttle = state.Throttle;
            _steer = state.Steer;
        }

        float _steer;

        /// <summary>Wheels turning with the road speed, and the fronts steered, since the model
        /// that would draw them is off.</summary>
        void Update()
        {
            Transform[] wheels = _car.WheelVisuals;
            if (wheels == null || _car.Sim == null) return;
            float radius = _car.Sim.Config.TyreFront.Radius;
            float forward = Vector3.Dot(Velocity, transform.forward);
            _spinDegrees += forward / Mathf.Max(radius, 0.1f) * Time.deltaTime * Mathf.Rad2Deg;
            float steer = _steer * _car.Sim.Config.MaxSteerAngleDegrees;
            for (int i = 0; i < wheels.Length && i < 4; i++)
                if (wheels[i] != null)
                    wheels[i].localRotation = Quaternion.Euler(_spinDegrees, _car.Sim.Wheels[i].IsFront ? steer : 0f, 0f);
        }
    }
}
