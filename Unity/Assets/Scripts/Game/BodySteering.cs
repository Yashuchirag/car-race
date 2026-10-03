using CarRace.Vehicle;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Each body's own steering, its defaults on the setup screen: the bodies share one chassis
    /// (CarDefinition), and the steering is where they differ, to suit their kind. The GT keeps
    /// the car as built. The Hot Hatch turns tightest and quickest, a small car for hairpins;
    /// the Supercar keeps more lock at speed with a quick rack; the Muscle car has the least lock
    /// and the slowest, heaviest steering. All inside the ranges the setup sweep checks
    /// (CarSetup: lock 26 to 40 degrees, half lock by 108 to 216 km/h, 2 to 5 locks a second).
    /// Only the player's car: the AI drive the car as built, whatever their body.
    /// </summary>
    public static class BodySteering
    {
        static readonly (string body, float lockDegrees, float halfLockMs, float locksPerSecond)[] Table =
        {
            ("GT", 33f, 42f, 3.2f),
            ("Supercar", 31f, 46f, 3.6f),
            ("Muscle", 30f, 38f, 2.8f),
            ("Hot Hatch", 37f, 40f, 3.8f),
        };

        /// <summary>Writes <paramref name="body"/>'s steering into <paramref name="car"/>; a body
        /// not in the table keeps the car's own.</summary>
        public static void Apply(CarConfig car, string body)
        {
            foreach (var (name, lockDegrees, halfLockMs, locksPerSecond) in Table)
            {
                if (name != body) continue;
                car.MaxSteerAngleDegrees = lockDegrees;
                car.SteerFalloffSpeed = halfLockMs;
                car.SteerRatePerSecond = locksPerSecond;
                return;
            }
        }
    }
}
