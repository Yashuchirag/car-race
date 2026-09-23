using System;

namespace CarRace.Track
{
    /// <summary>
    /// The speed the car may carry at each sample around the lap.
    ///
    /// The track files already ship a speed profile, but it belongs to the car the
    /// pipeline planned for, a GT3 with real downforce. Handing those numbers to a
    /// different car asks it to corner at speeds its tyres cannot hold, and it would
    /// leave the road at the first fast corner. The plan has to come from the limits of
    /// the car that is driving.
    /// </summary>
    public static class SpeedPlan
    {
        /// <summary>What the car can do, as the plan needs it. All positive.</summary>
        public struct Limits
        {
            /// <summary>Cornering acceleration the car can actually hold. Not its theoretical
            /// ceiling: that assumes all four tyres peak together, and a real car saturates
            /// one axle first.</summary>
            public float LateralMs2;

            /// <summary>Deceleration to plan for, under what it can do, so that arriving
            /// slightly hot still makes the corner.</summary>
            public float BrakingMs2;

            /// <summary>Acceleration off a corner while grip, not power, is the limit.</summary>
            public float TractionMs2;

            public float PowerW;
            public float MassKg;
            public float TopSpeedMs;
        }

        public static float[] Build(TrackData track, Limits limits)
        {
            float lateralLimitMs2 = limits.LateralMs2;
            float brakingLimitMs2 = limits.BrakingMs2;
            float topSpeedMs = limits.TopSpeedMs;

            int n = track.Count;
            var speed = new float[n];

            // Cornering limit. Downforce is ignored, which understates what the car can
            // carry through a fast corner: on this car it is 8% of the vertical load at
            // 80 m/s, so the plan is a little slow there and never optimistic.
            for (int i = 0; i < n; i++)
            {
                float curvature = MathF.Abs(track.LineCurvature[i]);
                float corner = curvature > 1e-5f
                    ? MathF.Sqrt(lateralLimitMs2 / curvature)
                    : topSpeedMs;
                speed[i] = MathF.Min(corner, topSpeedMs);
            }

            // Backward pass: every sample is capped at the speed it can still brake from
            // to reach the next one. Run twice, because the first pass cannot see through
            // the start line, and a braking zone that begins before it and ends after it
            // would otherwise be planned as if the lap started on the brakes.
            //
            // Braking is limited by the friction ellipse, not by the braking figure alone.
            // A tyre already using most of its grip sideways has almost none left for
            // slowing down, so a plan that allows full braking into a corner is asking for
            // something no tyre can do. It is not a small effect and it does not show up as
            // a slightly late corner entry: the driver arrives carrying speed, is still on
            // the brakes past turn-in, the load leaves the rear axle exactly as it is asked
            // for lateral grip, and the car spins. That is what this car did, every time,
            // in the same corner.
            float ds = track.SampleSpacingM;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = n - 1; i >= 0; i--)
                {
                    float entry = BrakingEntrySpeed(speed[track.Wrap(i + 1)],
                                                    MathF.Abs(track.LineCurvature[i]),
                                                    lateralLimitMs2, brakingLimitMs2, ds);
                    if (speed[i] > entry) speed[i] = entry;
                }
            }

            // Forward pass, the same ellipse the other way round: a car leaving a corner
            // cannot reach the next sample's cornering speed instantly, and low gears run out
            // of power long before they run out of grip. Without this the plan asks for corner
            // exit speeds no engine could deliver, the throttle controller sits saturated with
            // a wound-up integral, and the lap time it is compared against is a fiction.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    int next = track.Wrap(i + 1);

                    // Grip off a slow corner, power everywhere after that. Drag is left out:
                    // it matters at the top end, where the top speed cap already binds.
                    float accel = limits.TractionMs2;
                    if (speed[i] > 1f)
                    {
                        float powerLimited = limits.PowerW / (limits.MassKg * speed[i]);
                        if (powerLimited < accel) accel = powerLimited;
                    }

                    float exit = BrakingEntrySpeed(speed[i], MathF.Abs(track.LineCurvature[next]),
                                                   lateralLimitMs2, accel, ds);
                    if (speed[next] > exit) speed[next] = exit;
                }
            }

            return speed;
        }

        /// <summary>
        /// Fastest a car may be at one sample given the speed at the sample next to it, with
        /// the longitudinal and lateral demands sharing one friction ellipse. Used both ways
        /// round: backwards it is braking into a corner, forwards it is accelerating out of
        /// one, and the equation does not care which.
        ///
        /// Solved rather than iterated. Writing it the obvious way, taking the lateral demand
        /// at the cornering-limit speed and braking with whatever is left, is wrong in a way
        /// that flatters itself: at any sample where the corner is the binding limit the
        /// lateral demand is the whole ellipse by definition, so the plan allows no braking
        /// at all through the entire turn-in, and the car is made to reach apex speed the
        /// moment the road starts bending. That cost this circuit half a minute a lap and
        /// looked like a slow car rather than a bad plan.
        ///
        /// Writing v squared as u, with A the lateral limit and B the braking limit, the
        /// ellipse condition is (u k / A)^2 + ((u - u_next) / (2 ds B))^2 = 1, a quadratic
        /// in u whose larger root is the answer.
        /// </summary>
        static float BrakingEntrySpeed(float nextSpeed, float curvature,
                                       float lateralLimitMs2, float brakingLimitMs2, float ds)
        {
            float cornerLimit = curvature > 1e-5f
                ? MathF.Sqrt(lateralLimitMs2 / curvature)
                : float.MaxValue;

            float uNext = nextSpeed * nextSpeed;
            float p = curvature / lateralLimitMs2;
            float q = 1f / (2f * ds * brakingLimitMs2);

            float a = p * p + q * q;
            float b = -2f * q * q * uNext;
            float c = q * q * uNext * uNext - 1f;
            float discriminant = b * b - 4f * a * c;

            // No root means even coasting cannot satisfy the ellipse at the next sample's
            // speed, which only happens where the corner itself is the limit.
            if (discriminant <= 0f || a <= 0f) return cornerLimit;

            float u = (-b + MathF.Sqrt(discriminant)) / (2f * a);
            if (u <= 0f) return 0f;

            float entry = MathF.Sqrt(u);
            return MathF.Min(entry, cornerLimit);
        }

    }
}
