using System;
using CarRace.Vehicle;

namespace CarRace.Track
{
    /// <summary>
    /// A PathDriver with a personality and an awareness of other cars.
    ///
    /// The split is deliberate. PathDriver knows how to drive a circuit and nothing else;
    /// everything about racing one is here, and it works by moving the line the driver
    /// aims at and capping the speed it asks for. Nothing in this file touches the
    /// vehicle model, so a badly behaved AI can never be the reason the physics is wrong.
    /// </summary>
    public sealed class RaceDriver
    {
        /// <summary>What one car looks like to the others. Filled in by whoever owns the
        /// cars, once per reaction interval rather than once per physics step.</summary>
        public struct Seen
        {
            public int Index;        // where it is on the racing line
            public float LateralM;   // its offset from that line, positive to the right
            public float SpeedMs;

            /// <summary>No longer on the road: retired, recovered, or finished. Say so with
            /// this rather than by moving it somewhere harmless. Index arithmetic wraps, so a
            /// car parked at a nonsense index reappears as a stationary obstacle at a real
            /// place on the track, and the entire field crawls towards a car that is not
            /// there. That took a while to find and the symptom looked nothing like the
            /// cause.</summary>
            public bool Gone;
        }

        public readonly PathDriver Path;
        public readonly string Name;

        /// <summary>Share of the car's grip this driver is willing to use. The spread across
        /// a field is what makes a race rather than a procession.</summary>
        public readonly float Pace;

        /// <summary>How close it will sit behind a car it cannot pass, as seconds of gap.</summary>
        public float FollowSeconds = 0.9f;

        /// <summary>
        /// Closest it will sit to the car in front, centre to centre: a 4.4 m car plus two
        /// metres of daylight. Deliberately close, because this is only the floor. What
        /// actually keeps cars apart at speed is the braking bound below, which is a physical
        /// constraint rather than a number somebody liked.
        ///
        /// Setting this floor generously instead was what made a standing start fail. With
        /// the grid spaced exactly at the floor, no car may exceed the speed of the one in
        /// front, so each one lags the one ahead, and sixteen deep the back of the field was
        /// doing 7 km/h ten seconds after the start while everybody else drove into it.
        /// </summary>
        public float MinimumGapM = 6.5f;

        /// <summary>How far off line it will go to pass, and how quickly it moves there.</summary>
        public float OvertakeOffsetM = 2.8f;
        public float OffsetRateMPerS = 3.0f;

        /// <summary>Half the car's width plus what it wants between itself and the grass.</summary>
        public float HalfWidthM = 1.25f;

        /// <summary>Speed advantage it wants before it bothers pulling out, in m/s.</summary>
        public float OvertakeMargin = 1.5f;

        /// <summary>How hard it corrects the gap to the car ahead, per second.</summary>
        public float GapGain = 1.2f;

        /// <summary>Share of its braking it will plan to need to avoid the car in front.</summary>
        public float BrakingMargin = 0.7f;

        /// <summary>
        /// How far to one side a car still counts for the safety bound. Wider than the test
        /// for whether it is in the way, and deliberately so: a car three metres to one side
        /// of something doing 25 km/h cannot arrive at 85 km/h just because it is not
        /// technically behind it. Judging both questions with one narrow number is what let
        /// a whole pack drive into the back of a slow car at the first chicane.
        /// </summary>
        public float SafetyWidthM = 4f;

        /// <summary>Separation it wants from a car alongside.</summary>
        public float SideBySideGapM = 3f;

        public bool IsFollowing { get; private set; }
        public bool IsOvertaking { get; private set; }

        /// <summary>Who it is reacting to, and how far ahead, for diagnostics. -1 for nobody.</summary>
        public int BlockedBy { get; private set; } = -1;
        public float BlockedGapM { get; private set; }

        float _wantedOffset;
        float _cap = -1f;
        readonly float _brakingMs2;
        readonly float _lateralMs2;

        public RaceDriver(string name, TrackData track, CarConfig car,
                          SpeedPlan.Limits limits, float pace)
        {
            Name = name;
            Pace = pace;

            // Each driver plans for its own grip, rather than sharing one plan and scaling
            // the speeds. A driver who corners slower also has to brake earlier, and only a
            // plan built at its own limits gets that right.
            limits.LateralMs2 *= pace;
            limits.BrakingMs2 *= pace;
            limits.TractionMs2 *= pace;

            _brakingMs2 = limits.BrakingMs2;
            _lateralMs2 = limits.LateralMs2;
            Path = new PathDriver(track, SpeedPlan.Build(track, limits), car);
        }

        public VehicleInputs Drive(in BodyState body, float dt) => Path.Drive(body, dt);

        /// <summary>
        /// Looks at the other cars and decides how fast to go and where to sit. Called on a
        /// reaction interval, not every physics step: a driver who re-reads the whole field
        /// every two milliseconds is not a better driver, just a twitchier one.
        /// </summary>
        public void Observe(TrackData track, Seen[] field, int me, float dt)
        {
            Seen self = field[me];
            float ahead = float.MaxValue;
            int blocker = -1;

            for (int i = 0; i < field.Length; i++)
            {
                if (i == me || field[i].Gone) continue;

                // Distance round the track, not difference in progress: the car to worry
                // about is the one in front on the road, whether or not it is on the same lap.
                float gap = Distance(track, self.Index, field[i].Index);
                if (gap <= 0f || gap > 60f) continue;

                // Only a car actually in the way. A car is 1.9 m wide, so anything more than
                // about 2.2 m to one side is not blocking anybody. Judging this loosely is
                // what turns a whole field into a queue: a driver that has pulled out to pass
                // still counts the car it is passing as a reason to slow down, and nobody
                // ever gets by.
                if (MathF.Abs(field[i].LateralM - self.LateralM) > 2.2f) continue;
                if (gap >= ahead) continue;

                ahead = gap;
                blocker = i;
            }

            IsFollowing = false;
            IsOvertaking = false;
            BlockedBy = blocker;
            BlockedGapM = blocker >= 0 ? ahead : 0f;
            Path.SpeedCapMs = SafetyCap(track, field, me, self);

            if (blocker >= 0)
            {
                Seen front = field[blocker];
                float wanted = MathF.Max(MinimumGapM, FollowSeconds * self.SpeedMs);

                // Hold a distance, do not match a speed. Matching the speed of the car ahead
                // keeps whatever gap it happens to have at that moment, including none, and
                // the follower is then relying on reacting to the brake lights of a car it is
                // already touching. Asking for the speed that closes or opens the gap towards
                // where it should be is the same rule an adaptive cruise control uses.
                float cap = front.SpeedMs + GapGain * (ahead - wanted);
                if (cap < 0f) cap = 0f;

                // And never faster than it could still stop from. A proportional rule cannot
                // answer a closing rate of 80 m/s into a braking zone, which is exactly where
                // a quicker car catches a slower one: it brakes later by design, so the gap
                // it was holding disappears in well under a second. This is the same braking
                // arithmetic the speed plan uses, at a fraction of the car's real capability
                // so that it lifts early rather than standing on the brakes at the last
                // possible metre.
                float stoppable = Stoppable(front.SpeedMs, ahead,
                                            _brakingMs2 * BrakingMargin, MinimumGapM);
                if (stoppable < cap) cap = stoppable;

                if (cap < self.SpeedMs)
                {
                    IsFollowing = true;
                    if (Path.SpeedCapMs < 0f || cap < Path.SpeedCapMs) Path.SpeedCapMs = cap;
                }

                bool quicker = Path.PlannedSpeedMs > front.SpeedMs + OvertakeMargin;

                // Pull out as soon as it is being held up, which is anywhere inside the gap
                // it is trying to keep. Waiting for some fixed small distance means never:
                // at racing speed the gap a driver holds is longer than that distance, so it
                // sits at exactly the range where it has decided not to try, and the whole
                // field files round nose to tail with identical lap times.
                if (quicker && ahead < wanted + 10f)
                {
                    IsOvertaking = true;
                    _wantedOffset = SideToPass(track, field, me, front);
                }
                else if (!IsFollowing) _wantedOffset = 0f;
            }
            else _wantedOffset = 0f;

            // Room for anyone close, whatever else was decided, and both cars respect it.
            //
            // Two earlier versions of this were wrong in opposite directions. Letting both
            // cars aim at the separation they want makes each drive towards the other
            // whenever the gap it already has is wider than the one it is asking for. Giving
            // one of them priority instead is worse: the priority car converges onto the
            // racing line without asking whether anybody is there, so two cars gridded four
            // metres apart settle two metres apart, which on a 1.9 m wide car is ten
            // centimetres of daylight and a touch as soon as either one wobbles.
            //
            // What works is a constraint rather than a target. Neither car moves towards the
            // other past the separation, both keep whatever they wanted otherwise, and the
            // pair settles at the gap without either being given a right of way.
            for (int i = 0; i < field.Length; i++)
            {
                if (i == me || field[i].Gone) continue;

                float infront = Distance(track, self.Index, field[i].Index);
                float behind = Distance(track, field[i].Index, self.Index);
                if (MathF.Min(infront, behind) > 8f) continue;

                float side = self.LateralM - field[i].LateralM;
                if (MathF.Abs(side) > SideBySideGapM + HalfWidthM * 2f) continue;

                // Level and on the same piece of road, so which way to go is a free choice.
                // Deciding it by anything both cars compute the same way, such as which side
                // has more room, makes both of them choose the SAME side and stay welded
                // together. A car number cannot agree with itself from both points of view,
                // which is the one property this needs.
                float direction = MathF.Abs(side) > 0.2f ? MathF.Sign(side) : (me < i ? 1f : -1f);
                float separated = field[i].LateralM + direction * SideBySideGapM;

                _wantedOffset = direction > 0f
                    ? MathF.Max(_wantedOffset, separated)
                    : MathF.Min(_wantedOffset, separated);
            }

            // Clamped by the road that is actually left, not by a fixed number. The racing
            // line is already close to the edge through a corner, so a car given a modest
            // looking offset there ends up off the road, and the way that shows up is not a
            // car running wide but a car thirty metres into a field a second later.
            _wantedOffset = Clamp(_wantedOffset,
                                  -MathF.Max(MathF.Min(OvertakeOffsetM, track.RoomLeft(self.Index, HalfWidthM)), 0f),
                                  MathF.Max(MathF.Min(OvertakeOffsetM, track.RoomRight(self.Index, HalfWidthM)), 0f));

            // Move across at a rate rather than jumping, so the car is steered onto the new
            // line instead of being teleported onto it.
            float step = OffsetRateMPerS * dt;
            Path.LineOffsetM += Clamp(_wantedOffset - Path.LineOffsetM, -step, step);

            Path.SpeedCapMs = EaseCap(track, self, Path.SpeedCapMs, dt);
        }

        /// <summary>
        /// Brings the speed cap down no faster than the tyres can slow the car while it is
        /// also cornering, using the same friction ellipse the speed plan uses.
        ///
        /// A cap that drops instantly is a driver lifting off in the middle of a corner, and
        /// on a rear wheel drive car at the limit that is how you spin. Cars were arriving at
        /// a corner alongside somebody, backing off because of it, and ending up facing the
        /// wrong way, which is correct behaviour by the physics and terrible driving. A real
        /// driver gives up the corner and backs off on the straight.
        /// </summary>
        float EaseCap(TrackData track, Seen self, float wanted, float dt)
        {
            if (wanted < 0f)
            {
                _cap = -1f;
                return -1f;
            }

            if (_cap < 0f) _cap = MathF.Max(self.SpeedMs, wanted);
            if (wanted >= _cap)
            {
                _cap = wanted;
                return _cap;
            }

            float lateralUse = self.SpeedMs * self.SpeedMs
                             * MathF.Abs(track.LineCurvature[self.Index])
                             / MathF.Max(_lateralMs2, 0.1f);
            if (lateralUse > 1f) lateralUse = 1f;

            float available = _brakingMs2 * MathF.Sqrt(1f - lateralUse * lateralUse);
            _cap = MathF.Max(wanted, _cap - available * dt);
            return _cap;
        }

        /// <summary>
        /// Fastest this car may go given a car <paramref name="gapM"/> ahead of it doing
        /// <paramref name="frontSpeedMs"/>: fast enough to still stop short of it.
        ///
        /// Inside the minimum gap it asks for less than the car ahead is doing, rather than
        /// the same. Matching its speed there sounds safe and is not: the follower reaches
        /// that speed a fraction of a second late, so every time the car in front slows the
        /// gap shrinks again and never reopens, and a queue crawling through a slow corner
        /// ends up touching at walking pace. Asking for less is what makes the gap come back.
        /// </summary>
        static float Stoppable(float frontSpeedMs, float gapM, float brakingMs2, float minimumGapM = 6.5f)
        {
            float slack = gapM - minimumGapM;
            if (slack >= 0f)
                return MathF.Sqrt(frontSpeedMs * frontSpeedMs + 2f * brakingMs2 * slack);

            float backOff = 1f + slack / minimumGapM;
            return frontSpeedMs * (backOff > 0f ? backOff : 0f);
        }

        /// <summary>
        /// Slowest speed that any car ahead demands, or -1 if none do.
        ///
        /// Separate from the following logic on purpose. Following is a tactical decision
        /// about the car in the way; this is a physical one about everything on this stretch
        /// of road, and it looks as far ahead as the car would need to stop from where it is,
        /// rather than over a fixed distance. At 300 km/h that is further than any circuit's
        /// braking zone, which is exactly why real racing has flags for a stopped car.
        /// </summary>
        float SafetyCap(TrackData track, Seen[] field, int me, Seen self)
        {
            float braking = MathF.Max(_brakingMs2 * BrakingMargin, 0.5f);
            float horizon = self.SpeedMs * self.SpeedMs / (2f * braking) + MinimumGapM + 20f;

            float cap = -1f;
            for (int i = 0; i < field.Length; i++)
            {
                if (i == me || field[i].Gone) continue;
                if (MathF.Abs(field[i].LateralM - self.LateralM) > SafetyWidthM) continue;

                float gap = Distance(track, self.Index, field[i].Index);
                if (gap <= 0f || gap > horizon) continue;

                float stoppable = Stoppable(field[i].SpeedMs, gap, braking, MinimumGapM);
                if (cap < 0f || stoppable < cap) cap = stoppable;
            }

            return cap;
        }

        /// <summary>
        /// Which side to try. Prefers the side with more road left and nobody on it, and
        /// keeps to the inside of a corner when there is nothing to choose between them.
        /// </summary>
        float SideToPass(TrackData track, Seen[] field, int me, Seen front)
        {
            Seen self = field[me];
            float roomRight = track.RoomRight(self.Index, HalfWidthM);
            float roomLeft = track.RoomLeft(self.Index, HalfWidthM);

            bool rightBlocked = false, leftBlocked = false;
            for (int i = 0; i < field.Length; i++)
            {
                if (i == me || field[i].Gone) continue;
                if (MathF.Abs(Distance(track, self.Index, field[i].Index)) > 20f) continue;
                if (field[i].LateralM > self.LateralM + 1f) rightBlocked = true;
                if (field[i].LateralM < self.LateralM - 1f) leftBlocked = true;
            }

            // Enough room for a car alongside, not for the full offset: a pass down the
            // inside of a tight corner is a metre and a half, not three.
            bool canRight = roomRight > 1.5f && !rightBlocked;
            bool canLeft = roomLeft > 1.5f && !leftBlocked;

            if (canRight && !canLeft) return MathF.Min(OvertakeOffsetM, roomRight);
            if (canLeft && !canRight) return -MathF.Min(OvertakeOffsetM, roomLeft);
            if (!canLeft && !canRight) return Path.LineOffsetM;

            // Both open: take the inside, which is the shorter way round and the side the
            // car in front is not defending by being on the racing line.
            return track.LineCurvature[self.Index] >= 0f
                ? MathF.Min(OvertakeOffsetM, roomRight)
                : -MathF.Min(OvertakeOffsetM, roomLeft);
        }

        /// <summary>How far ahead <paramref name="other"/> is on the road, in metres.</summary>
        static float Distance(TrackData track, int from, int to)
        {
            int n = track.Count;
            int steps = ((to - from) % n + n) % n;
            return steps * track.SampleSpacingM;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
