using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Tells the car where Recover should put it on this circuit: the centreline at the car's
    /// current place on the lap, pointing along it, a little above the road so the car
    /// settles onto its wheels rather than starting inside the surface.
    ///
    /// Follows the car along the centreline every physics step with the same short forward
    /// search LapTimer uses, so a car spun into the grass beside a hairpin comes back on its
    /// own side of it, not the other.
    /// </summary>
    public sealed class TrackRecovery : MonoBehaviour
    {
        [SerializeField] CarController car;
        [SerializeField] TrackPath track;
        [SerializeField] float cgHeight = 0.45f;

        int _index;
        Vector3 _lastPosition;

        void Awake()
        {
            if (car == null || track == null || track.centre.Length < 3) { enabled = false; return; }
            car.RecoveryPose = Pose;
        }

        void FixedUpdate()
        {
            Vector3 position = car.transform.position;
            // A jump of this size is a restart to the grid; search the whole lap once.
            if ((position - _lastPosition).sqrMagnitude > 25f * 25f)
                _index = track.Nearest(position, 0, back: 0, ahead: track.centre.Length - 1);
            _lastPosition = position;
            _index = track.Nearest(position, _index);
        }

        (Vector3 position, Quaternion rotation) Pose()
        {
            int n = track.centre.Length;
            Vector3 here = track.centre[_index];
            Vector3 along = track.centre[(_index + 1) % n] - track.centre[(_index - 1 + n) % n];
            along.y = 0f;
            _lastPosition = here;
            return (here + Vector3.up * (cgHeight + 0.3f), Quaternion.LookRotation(along.normalized, Vector3.up));
        }
    }
}
