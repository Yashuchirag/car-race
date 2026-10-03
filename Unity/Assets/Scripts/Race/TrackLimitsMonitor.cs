using System;
using UnityEngine;
using CarRace.Track;
using Vec3 = System.Numerics.Vector3;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Watches one car against the track limits with the headless judge (TrackLimits), every
    /// physics step, and says when an excursion has been judged. Added to every car it drives by
    /// RaceDirector, as CarContacts is, which turns each offence into a ruling.
    ///
    /// Follows the car along the centreline with the same short forward search as TrackRecovery
    /// and LapTimer. A jump of more than 25 m in one step is a recovery or a restart, and cancels
    /// any excursion: the car was carried back, it did not drive back.
    /// </summary>
    public sealed class TrackLimitsMonitor : MonoBehaviour
    {
        /// <summary>Called with each judged excursion that is an offence or an incident.</summary>
        public Action<TrackLimits.Kind> Judged;

        /// <summary>Offences so far, cuts and track limits together: LapTimer marks a lap invalid
        /// when this changes during it.</summary>
        public int Offences { get; private set; }

        public TrackLimits Judge { get; } = new TrackLimits();

        CarController _car;
        Rigidbody _body;
        TrackPath _path;
        TrackData _track;
        int _index;
        Vector3 _last;

        public void Watch(TrackPath path, TrackData track)
        {
            _path = path;
            _track = track;
            _car = GetComponent<CarController>();
            _body = GetComponent<Rigidbody>();
            _last = _body.position;
            _index = path.Nearest(_last, 0, back: 0, ahead: path.centre.Length - 1);
        }

        void FixedUpdate()
        {
            if (_track == null || _car.Sim == null) return;
            Vector3 position = _body.position;
            if ((position - _last).sqrMagnitude > 25f * 25f)
            {
                _index = _path.Nearest(position, 0, back: 0, ahead: _path.centre.Length - 1);
                Judge.Cancel();
            }
            _last = position;
            _index = _path.Nearest(position, _index);

            TrackLimits.Kind kind = Judge.Step(_track, _index, ToSim(position), ToSim(transform.forward),
                                               ToSim(_body.linearVelocity), _car.Sim.Wheels, Time.fixedDeltaTime);
            if (kind == TrackLimits.Kind.None) return;
            if (kind != TrackLimits.Kind.Incident) Offences++;
            if (RaceDirector.AiLogAsked)
                Debug.Log($"LIMITS t {Time.timeSinceLevelLoad:0.00} {name} {kind}: drove {Judge.LastDrivenM:0.0} m " +
                          $"where the road needs {Judge.LastLegalM:0.0} m");
            Judged?.Invoke(kind);
        }

        static Vec3 ToSim(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
