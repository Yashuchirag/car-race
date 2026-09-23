using System;
using System.Collections.Generic;
using System.Numerics;

namespace CarRace.Net
{
    /// <summary>
    /// Holds the snapshots a client has received and answers the question it actually has:
    /// where was every car a moment ago.
    ///
    /// A client renders the world deliberately late, by about the interval between snapshots
    /// plus whatever jitter the network adds. That delay is what buys the two snapshots
    /// needed to interpolate between, and interpolating between two states the host really
    /// sent is what makes cars move smoothly instead of jumping twenty times a second.
    /// Extrapolating forward from the newest one instead guesses, and a guess about a car
    /// that has just turned in is visibly wrong.
    /// </summary>
    public sealed class Interpolator
    {
        readonly List<Snapshot> _snapshots = new List<Snapshot>();

        /// <summary>How far behind the host's clock to render. Two snapshot intervals covers
        /// one lost packet without a gap.</summary>
        public float DelaySeconds = 0.1f;

        /// <summary>How much history to keep, beyond which snapshots are of no further use.</summary>
        public float HistorySeconds = 2f;

        public int Held => _snapshots.Count;
        public float NewestTime => _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1].TimeSeconds : 0f;
        public float OldestTime => _snapshots.Count > 0 ? _snapshots[0].TimeSeconds : 0f;

        /// <summary>
        /// Files a snapshot by its host time. Out of order arrivals are inserted, not
        /// dropped: UDP reorders packets routinely and a snapshot that arrives late is still
        /// worth having if the client has not reached its time yet.
        /// </summary>
        public void Add(Snapshot snapshot)
        {
            int at = _snapshots.Count;
            while (at > 0 && _snapshots[at - 1].TimeSeconds > snapshot.TimeSeconds) at--;

            if (at > 0 && _snapshots[at - 1].Tick == snapshot.Tick) return;   // duplicate
            _snapshots.Insert(at, snapshot);

            float cutoff = NewestTime - HistorySeconds;
            while (_snapshots.Count > 2 && _snapshots[0].TimeSeconds < cutoff) _snapshots.RemoveAt(0);
        }

        /// <summary>
        /// The state of one car at a host time, blended between the snapshots either side of
        /// it. False if that time is not covered, which on a client means the buffer has run
        /// dry and the right thing to do is hold the last pose rather than invent one.
        /// </summary>
        public bool Sample(byte id, float hostTime, out CarState state)
        {
            state = default;
            if (_snapshots.Count == 0) return false;

            int after = -1;
            for (int i = 0; i < _snapshots.Count; i++)
            {
                if (_snapshots[i].TimeSeconds < hostTime) continue;
                after = i;
                break;
            }

            if (after <= 0)
            {
                // Before the oldest, or past the newest: no pair to work with.
                Snapshot edge = after == 0 ? _snapshots[0] : _snapshots[_snapshots.Count - 1];
                return Find(edge, id, out state);
            }

            Snapshot a = _snapshots[after - 1], b = _snapshots[after];
            if (!Find(a, id, out CarState from) || !Find(b, id, out CarState to)) return false;

            float span = b.TimeSeconds - a.TimeSeconds;
            float t = span > 1e-6f ? (hostTime - a.TimeSeconds) / span : 0f;
            state = Blend(from, to, t);
            return true;
        }

        static bool Find(Snapshot snapshot, byte id, out CarState state)
        {
            foreach (CarState car in snapshot.Cars)
            {
                if (car.Id != id) continue;
                state = car;
                return true;
            }
            state = default;
            return false;
        }

        static CarState Blend(in CarState a, in CarState b, float t) => new CarState
        {
            Id = a.Id,
            Position = Vector3.Lerp(a.Position, b.Position, t),

            // Slerp rather than Lerp, and Slerp takes the short way round only if the two
            // are on the same side of the hypersphere, which is what the dot product check
            // inside Quaternion.Slerp is for. Lerping raw components makes a car rotate
            // through a slightly wrong path, which reads as a twitch at high yaw rates.
            Orientation = Quaternion.Slerp(a.Orientation, b.Orientation, t),
            Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
            Steer = a.Steer + (b.Steer - a.Steer) * t,
            EngineRpm = a.EngineRpm + (b.EngineRpm - a.EngineRpm) * t,
            Gear = t < 0.5f ? a.Gear : b.Gear,
            Lap = t < 0.5f ? a.Lap : b.Lap,
        };
    }
}
