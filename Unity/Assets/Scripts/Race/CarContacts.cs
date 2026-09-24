using System;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Counts this car touching another car, for the race results. Added to every car by
    /// RaceDirector. A scrape along another car fires enter and exit many times, so touches
    /// less than a second apart count once.
    /// </summary>
    public sealed class CarContacts : MonoBehaviour
    {
        public Action Touched;

        const float OnceEverySeconds = 1f;
        float _last = float.NegativeInfinity;

        void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody == null || collision.rigidbody.GetComponent<CarController>() == null) return;
            if (Time.time - _last < OnceEverySeconds) return;
            _last = Time.time;
            Touched?.Invoke();
        }
    }
}
