using System;
using System.Collections.Generic;
using System.Numerics;

namespace CarRace.Net
{
    /// <summary>
    /// Where every other car is now, from the last state each one sent.
    ///
    /// The Interpolator draws the world in the past, which suits a spectator but not a race
    /// where each player simulates their own car: a friend alongside you, drawn a tenth of a
    /// second late, is drawn five metres behind where they are, and both of you would see a
    /// gap that is not there. So a car is carried forward from its last state to the present,
    /// by its velocity and its turn rate. On a LAN that last state is a few tens of
    /// milliseconds old, and over so short a time a car barely departs from a straight line
    /// at a steady turn: at 50 m/s, turning at half a radian a second, 40 ms of it is two
    /// centimetres off. Over a slow link the guess grows with the square of the age, which is
    /// why MaxAheadSeconds stops it and the car holds rather than flies off.
    ///
    /// Each new state moves the guess a little. That correction is not applied at once,
    /// which would make cars tick along the road, but decays over SmoothingSeconds.
    /// </summary>
    public sealed class Extrapolator
    {
        sealed class Car
        {
            public CarState Latest;
            public Vector3 PositionError;         // shown minus predicted, at CorrectedAt
            public Quaternion RotationError = Quaternion.Identity;
            public float CorrectedAt;
        }

        readonly Dictionary<byte, Car> _cars = new Dictionary<byte, Car>();

        public float SmoothingSeconds = 0.1f;
        public float MaxAheadSeconds = 0.25f;

        public IEnumerable<byte> Ids => _cars.Keys;
        public int Count => _cars.Count;

        /// <summary>Takes a state stamped with host time, at host time now. Older or repeated
        /// states are ignored: UDP reorders, and only the newest one says anything.</summary>
        public void Add(in CarState state, float now)
        {
            if (!_cars.TryGetValue(state.Id, out Car car))
            {
                _cars[state.Id] = new Car { Latest = state, CorrectedAt = now };
                return;
            }
            if (state.TimeSeconds <= car.Latest.TimeSeconds) return;

            Shown(car, now, out Vector3 shownPosition, out Quaternion shownRotation);
            Predict(state, now, out Vector3 position, out Quaternion rotation);

            car.Latest = state;
            car.PositionError = shownPosition - position;
            car.RotationError = Quaternion.Normalize(shownRotation * Quaternion.Inverse(rotation));
            car.CorrectedAt = now;
        }

        public void Remove(byte id) => _cars.Remove(id);

        /// <summary>A car's state at host time now, as it should be drawn: predicted, with
        /// what is left of the last correction still blended in.</summary>
        public bool Sample(byte id, float now, out CarState state)
        {
            if (!_cars.TryGetValue(id, out Car car)) { state = default; return false; }

            state = car.Latest;
            Shown(car, now, out state.Position, out state.Orientation);
            state.TimeSeconds = now;
            return true;
        }

        /// <summary>The prediction alone, without smoothing, for measuring it.</summary>
        public bool Raw(byte id, float now, out CarState state)
        {
            if (!_cars.TryGetValue(id, out Car car)) { state = default; return false; }

            state = car.Latest;
            Predict(car.Latest, now, out state.Position, out state.Orientation);
            state.TimeSeconds = now;
            return true;
        }

        void Shown(Car car, float now, out Vector3 position, out Quaternion rotation)
        {
            Predict(car.Latest, now, out position, out rotation);

            float left = MathF.Exp(-MathF.Max(now - car.CorrectedAt, 0f) / SmoothingSeconds);
            position += car.PositionError * left;
            rotation = Quaternion.Normalize(
                Quaternion.Slerp(Quaternion.Identity, car.RotationError, left) * rotation);
        }

        void Predict(in CarState state, float now, out Vector3 position, out Quaternion rotation)
        {
            float ahead = Math.Clamp(now - state.TimeSeconds, 0f, MaxAheadSeconds);
            position = state.Position + state.Velocity * ahead;

            // Angular velocity is in world space, so the turn it makes over the interval is
            // applied after the car's own orientation, on the left.
            float rate = state.AngularVelocity.Length();
            rotation = rate > 1e-5f
                ? Quaternion.Normalize(Quaternion.CreateFromAxisAngle(state.AngularVelocity / rate, rate * ahead)
                                       * state.Orientation)
                : state.Orientation;
        }
    }
}
