using System;
using System.Collections.Generic;
using System.Numerics;
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

            /// <summary>Its driver's speed plan, known to the others the way lap times are.
            /// Whether a pass can work is a question about both plans over the road ahead.</summary>
            public IReadOnlyList<float> Plan;

            /// <summary>Where it is in the world. Close up, positions measure the gap between two
            /// cars better than their places on the racing line do; see Gap.</summary>
            public Vector3 Position;

            /// <summary>The passing lane its driver has chosen: -1 left, +1 right, 0 the racing
            /// line. A human's car is always 0. How two drivers side by side avoid choosing the
            /// same lane.</summary>
            public int Lane;

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
        /// How close it sits behind a car it is clearly quicker than, as seconds of gap, and
        /// how much quicker is clearly: this share of the other car's lap time, plan against
        /// plan over the whole lap.
        ///
        /// From FollowSeconds back no pass can start: that is thirty metres at racing speed,
        /// and even the fastest driver in the field gains at most seventeen on the slowest
        /// in ten seconds. Judged over a whole lap rather than the road just ahead, so that
        /// the verdict cannot change from one corner to the next and yank the follow gap,
        /// and the cap with it, back and forth.
        /// </summary>
        public float AttackSeconds = 0.4f;
        public float AttackAdvantage = 0.01f;

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

        /// <summary>How quickly the offset it starts with (its grid slot's, or where a
        /// recovery put it) eases back to the racing line.</summary>
        public float OffsetRateMPerS = 3.0f;

        /// <summary>
        /// How close two cars have to be, along the road and across it, to be side by side and
        /// each take a passing lane.
        /// </summary>
        public float CompanyAlongM = 12f;
        public float CompanyAcrossM = 5.5f;

        /// <summary>
        /// When it leaves a lane: nobody alongside for CompanyHoldSeconds, not passing, and the
        /// racing line within RejoinNearM of the lane, which it is in every corner, so going
        /// back is a short step. Leaving at once, wherever it was, sent a car 7 m across the
        /// road on a straight at 190 km/h, and back again when the next car came alongside:
        /// it spun.
        /// </summary>
        public float CompanyHoldSeconds = 1f;
        public float RejoinNearM = 1.5f;

        /// <summary>
        /// Side by side with a corner tighter than this coming up within braking distance and
        /// TightMarginM, the car behind gives up the corner: it treats the other as in the way
        /// and drops in behind. Dead level, the higher car number does. Two lanes through a
        /// chicane bend too hard to hold: at Royal Park's first, the pair off the front row ran
        /// 2 m wide of their lanes and touched every race.
        /// </summary>
        public float TightCornerRadiusM = 60f;
        public float TightMarginM = 40f;

        /// <summary>
        /// It moves into a lane only if it can take that lane at the speed it is doing: the
        /// lane's plan over its braking distance ahead no lower than its speed less this. Short
        /// of that it stays where it is and gives way to the car alongside instead. A car that
        /// came up alongside at 110 km/h and turned into its lane in a bend the lane plans at
        /// 88 slid 3 m off it and into the car it had come up beside.
        /// </summary>
        public float LaneEntryMarginMs = 2f;

        /// <summary>How far ahead of a car PathsMeet always looks, and how fast a speed cap
        /// that is no longer wanted rises away rather than vanishing.</summary>
        public float PathsLookM = 40f;
        public float CapReleaseMs2 = 8f;

        /// <summary>Half the car's width plus what it wants between itself and the grass.</summary>
        public float HalfWidthM = 1.25f;

        /// <summary>
        /// How far ahead it looks to decide whether a pass can work, in seconds of the other
        /// car's plan: both plans are driven over that stretch, and the pass is on only if
        /// this car gains at least the gap it has to close. The same stretch is also how long
        /// a pass may go without a new best before it is given up.
        ///
        /// One number for both on purpose. The gain is not spread evenly: out of a corner the
        /// two cars accelerate much alike, and a quicker driver's advantage arrives in the
        /// next braking zone. Giving up after a shorter spell than the one the prediction was
        /// made over killed every pass before it reached the place it was going to work.
        /// </summary>
        public float PassHorizonSeconds = 10f;

        /// <summary>A car doing less than this share of both what the plan asks for where it is
        /// and what the car behind it is doing has a problem, and is passed whoever is
        /// quicker. The same share the race log uses to call a car crawling; out of a corner
        /// a healthy car still does 80% of its plan or more.</summary>
        public float TroubleShare = 0.45f;

        /// <summary>
        /// Slowest this car may itself be going to call another car in trouble. Standing on a
        /// grid, both cars' speeds are noise, and in Unity they settle with a few millimetres a
        /// second either way: the car ahead, rolling back 0.001 m/s, counted as doing less
        /// than 45% of the 0.03 m/s of the car behind, which pulled out to pass on the grid,
        /// ran alongside the second row's other car to Monza's first chicane, and squeezed it
        /// into the wall in every race. The harness starts cars at exactly zero and never saw
        /// it. Any car worth passing for being in trouble is passed at more than this.
        /// </summary>
        public float TroubleMinSpeedMs = 5f;

        /// <summary>How long before it tries the same car again after giving up, and how far
        /// past it has to be before a pass counts as done.</summary>
        public float PassRetrySeconds = 5f;
        public float PassedM = 10f;

        /// <summary>
        /// How much faster than the car it is passing it may close on that car in the next
        /// lane, and how much sideways room that needs: this much now, and still this much
        /// SideLookaheadSeconds from now at the rate the gap is changing. Short of that it
        /// only holds alongside, the way it treats every other car in the next lane.
        ///
        /// Letting any car in the next lane be closed on, with no such check, made passing
        /// work and then crashed where lanes merge. Offsets hang off the racing line, which
        /// sweeps across the road through a corner and squeezes the car on that side
        /// towards the other faster than the other can move away.
        /// </summary>
        public float PassingClosingMs = 4f;
        public float SecureSideM = 2.7f;
        public float SideLookaheadSeconds = 0.5f;

        /// <summary>How far behind it looks before moving across to pass.</summary>
        public float LookBehindM = 10f;

        /// <summary>How hard it corrects the gap to the car ahead, per second.</summary>
        public float GapGain = 1.2f;

        /// <summary>Share of its braking it will plan to need to avoid the car in front.</summary>
        public float BrakingMargin = 0.7f;

        /// <summary>How far to one side a car still counts as in the way: a 1.9 m car and a
        /// little. Inside this, it is a car to stop behind.</summary>
        public float InTheWayM = 2.2f;

        /// <summary>
        /// How far to one side a car still counts for the safety bound. Wider than the test
        /// for whether it is in the way, and deliberately so: a car three metres to one side
        /// of something doing 25 km/h cannot arrive at 85 km/h just because it is not
        /// technically behind it. Judging both questions with one narrow number is what let
        /// a whole pack drive into the back of a slow car at the first chicane.
        /// </summary>
        public float SafetyWidthM = 4f;

        public bool IsFollowing { get; private set; }
        public bool IsOvertaking { get; private set; }

        /// <summary>Who it is reacting to, and how far ahead, for diagnostics. -1 for nobody.</summary>
        public int BlockedBy { get; private set; } = -1;
        public float BlockedGapM { get; private set; }

        /// <summary>Where across the road it has decided to be, for diagnostics: the passing
        /// lane it wants, -1 left, +1 right, 0 the racing line.</summary>
        public float WantedOffsetM => Path.Lane;

        float _cap = -1f;
        float _companyFor;          // seconds it stays in its lane after the car alongside has gone
        int _giveWayTo = -1;        // a car alongside it could not make room for, so drops behind

        int _passing = -1;          // the car it is passing, or -1
        float _passSide;            // +1 passing on the right, -1 on the left
        float _passBestM;           // that car's smallest lead so far, negative once passed
        float _passStalled;         // seconds since that last improved
        float _passSideSeen = -1f;  // sideways gap to it at the last look, -1 before one
        bool _passSecure;           // room to close on it this interval
        int _gaveUpOn = -1;
        int _passingCandidate = -1;
        float _retryIn;
        readonly Dictionary<IReadOnlyList<float>, float> _lapSeconds =
            new Dictionary<IReadOnlyList<float>, float>();

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
            track.EnsureLanes(HalfWidthM);
            Path = new PathDriver(track, SpeedPlan.Build(track, limits), car, new[]
            {
                SpeedPlan.Build(track, limits, track.LaneCurvature[0]),
                SpeedPlan.Build(track, limits, track.LaneCurvature[1]),
            });
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
                float gap = Gap(track, self, field[i]);
                if (gap <= 0f || gap > 60f) continue;

                // Only a car actually in the way. A car is 1.9 m wide, so anything more than
                // about 2.2 m to one side is not blocking anybody. Judging this loosely is
                // what turns a whole field into a queue: a driver that has pulled out to pass
                // still counts the car it is passing as a reason to slow down, and nobody
                // ever gets by.
                if (MathF.Abs(field[i].LateralM - self.LateralM) > InTheWayM
                    && i != _giveWayTo && !GiveUpCorner(track, self, field[i], me, i, gap)
                    && !PathsMeet(track, self, field[i], gap)) continue;
                if (gap >= ahead) continue;

                ahead = gap;
                blocker = i;
            }

            IsFollowing = false;
            BlockedBy = blocker;
            BlockedGapM = blocker >= 0 ? ahead : 0f;
            _passSecure = _passing >= 0 && SideHolding(self, field[_passing], dt);
            Path.SpeedCapMs = SafetyCap(track, field, me, self);
            if (_retryIn > 0f) _retryIn -= dt;

            if (blocker >= 0)
            {
                Seen front = field[blocker];
                bool attacking = front.Plan != null
                    && 1f - LapSeconds(Path.Plan, track) / LapSeconds(front.Plan, track) >= AttackAdvantage;
                float wanted = MathF.Max(MinimumGapM,
                                         (attacking ? AttackSeconds : FollowSeconds) * self.SpeedMs);

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

                // Applied even while it is above the car's speed. Applying it only once it was
                // below made following a switch: the moment the cap rose past the car's speed
                // it vanished, the target jumped to the plan, the car went to full throttle,
                // caught up, and the cap came back on the brakes. Behind one car through one
                // corner that alternated every two seconds until the car spun.
                IsFollowing = cap < self.SpeedMs;
                if (Path.SpeedCapMs < 0f || cap < Path.SpeedCapMs) Path.SpeedCapMs = cap;

                // Pull out as soon as it is being held up, which is anywhere inside the gap
                // it is trying to keep. Waiting for some fixed small distance means never:
                // at racing speed the gap a driver holds is longer than that distance, so it
                // sits at exactly the range where it has decided not to try, and the whole
                // field files round nose to tail with identical lap times.
                bool retrying = blocker == _gaveUpOn && _retryIn > 0f;
                if (_passing < 0 && !retrying && ahead < wanted + 10f
                    && WorthPassing(track, self, front, ahead))
                {
                    _passingCandidate = blocker;
                    float side = SideToPass(track, field, me);
                    if (side != 0f)
                    {
                        _passing = blocker;
                        _passSide = side;
                        _passBestM = ahead;
                        _passStalled = 0f;
                        _passSideSeen = -1f;
                    }
                }
            }

            // A pass is a commitment, not a verdict taken again every 20 ms. Deciding afresh
            // each time made the pass undo itself: once a car had pulled out, the car it was
            // passing no longer counted as in the way, so it steered back in behind, where the
            // car counted again. Held-up cars swapped lanes every second or so, sometimes
            // changing side at 160 km/h, and that is what spun them.
            if (_passing >= 0) KeepPassing(track, field, me, dt);
            IsOvertaking = _passing >= 0;

            // Where across the road: the racing line, or a passing lane. Passing, the lane on
            // the side it is passing; side by side with anyone, the lane on its own side of
            // them, so the pair runs in two lanes that cannot close on each other.
            //
            // This replaced offsets from the racing line, which were the cause of most of the
            // contacts left: the racing line sweeps across the road into a corner and carries
            // an offset with it, so the car on the inside of a pair was pushed into the other
            // faster than the other could move away.
            int lane = _passing >= 0 ? (_passSide > 0f ? 1 : -1) : 0;
            int company = CompanyLane(track, field, me, out int alongside);
            _giveWayTo = -1;
            if (company != 0 && company != Path.Lane && !LaneReachable(track, self, company))
            {
                // Cannot make that lane at this speed: stay put, and give way to the car
                // alongside until it can or the other car has gone.
                _giveWayTo = alongside;
                company = Path.Lane;
            }
            if (company != 0)
            {
                lane = company;
                _companyFor = CompanyHoldSeconds;
            }
            else if (_companyFor > 0f) _companyFor -= dt;

            if (lane == 0 && Path.Lane != 0)
            {
                float laneFromCentre = (Path.Lane > 0 ? 1f : -1f) * track.LaneHalfM[self.Index];
                bool near = MathF.Abs(laneFromCentre - track.LineFromCentreM[self.Index]) < RejoinNearM;
                if (_companyFor > 0f || !near) lane = Path.Lane;
            }
            Path.Lane = lane;

            // Whatever offset it started with, from its grid slot or a recovery, eases back
            // to the line, at a rate so the car is steered there rather than teleported; but
            // not while the car is moving into a lane, where the two moves together swung a car
            // off the grid towards the one beside it, and once fully in the lane the offset no
            // longer counts and is dropped.
            if (Path.LaneBlend >= 1f) Path.LineOffsetM = 0f;
            else if (Path.Lane == 0 && Path.LaneBlend <= 0f)
            {
                float step = OffsetRateMPerS * dt;
                Path.LineOffsetM += Clamp(-Path.LineOffsetM, -step, step);
            }

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
            // Released, the cap rises away at CapReleaseMs2 rather than vanishing. Dropping it
            // at once meant one reaction step in which the car ahead was not seen threw away
            // all the slowing down done so far: the next cap started again from the car's
            // speed, and it arrived in the other car's lane 20 km/h too fast.
            if (wanted < 0f)
            {
                if (_cap < 0f) return -1f;
                _cap += CapReleaseMs2 * dt;
                if (_cap > self.SpeedMs + 15f) _cap = -1f;
                return _cap;
            }

            if (_cap < 0f) _cap = MathF.Max(self.SpeedMs, wanted);
            if (wanted >= _cap)
            {
                _cap = MathF.Min(wanted, _cap + CapReleaseMs2 * dt);
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
        ///
        /// A car going backwards counts as standing still. Its negative speed used to come out
        /// of here as a negative cap, which means no cap at all, so the car behind a stopped
        /// car that had rolled back a few centimetres went to full throttle into it.
        /// </summary>
        static float Stoppable(float frontSpeedMs, float gapM, float brakingMs2, float minimumGapM = 6.5f)
        {
            float front = MathF.Max(frontSpeedMs, 0f);
            float slack = gapM - minimumGapM;
            if (slack >= 0f)
                return MathF.Sqrt(front * front + 2f * brakingMs2 * slack);

            float backOff = 1f + slack / minimumGapM;
            return front * (backOff > 0f ? backOff : 0f);
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
                float side = MathF.Abs(field[i].LateralM - self.LateralM);
                if (side > SafetyWidthM) continue;

                float gap = Gap(track, self, field[i]);
                if (gap <= 0f || gap > horizon) continue;

                bool nextLane = side > InTheWayM;

                // In the next lane rather than its own, a car it may sit alongside but not close
                // on: inside the minimum gap the bound asks for that car's speed, where in its
                // own lane it asks for less to open the gap back up. Asking for less in the next
                // lane made two cars running side by side brake each other every time either
                // edged ahead, and sixteen deep that stopped the back of the grid dead.
                if (nextLane) gap = MathF.Max(gap, MinimumGapM);

                float stoppable = Stoppable(field[i].SpeedMs, gap, braking, MinimumGapM);

                // Except the car it is passing, while the sideways gap to it is holding: that
                // one it may close on, by up to PassingClosingMs, or no pass would ever get
                // further than its rear wheels.
                if (i == _passing && _passSecure && nextLane)
                    stoppable = MathF.Max(stoppable,
                                          MathF.Max(field[i].SpeedMs, 0f) + PassingClosingMs);

                if (cap < 0f || stoppable < cap) cap = stoppable;
            }

            return cap;
        }

        /// <summary>
        /// Which side to try: +1 right, -1 left, 0 when neither is open. Prefers the side
        /// with more road left and nobody on it, and keeps to the inside of a corner when
        /// there is nothing to choose between them.
        ///
        /// Nobody on it means alongside and just behind as well as ahead. Looking ahead only
        /// is how a car switched its pass to the other side and moved straight across one
        /// that was a few metres behind it there, in a braking zone.
        /// </summary>
        float SideToPass(TrackData track, Seen[] field, int me)
        {
            Seen self = field[me];
            float roomRight = track.RoomRight(self.Index, HalfWidthM);
            float roomLeft = track.RoomLeft(self.Index, HalfWidthM);

            bool rightBlocked = false, leftBlocked = false;
            for (int i = 0; i < field.Length; i++)
            {
                if (i == me || field[i].Gone) continue;
                if (Distance(track, self.Index, field[i].Index) > 20f
                    && Distance(track, field[i].Index, self.Index) > LookBehindM) continue;
                if (field[i].LateralM > self.LateralM + 1f) rightBlocked = true;
                if (field[i].LateralM < self.LateralM - 1f) leftBlocked = true;
            }

            // Enough room for a car alongside, not for the full offset: a pass down the
            // inside of a tight corner is a metre and a half, not three.
            // Nor the side whose lane the car ahead is in.
            Seen front = _passingCandidate >= 0 ? field[_passingCandidate] : default;
            bool canRight = roomRight > 1.5f && !rightBlocked && front.Lane <= 0;
            bool canLeft = roomLeft > 1.5f && !leftBlocked && front.Lane >= 0;

            if (canRight && !canLeft) return 1f;
            if (canLeft && !canRight) return -1f;
            if (!canLeft && !canRight) return 0f;

            // Both open: take the inside, which is the shorter way round and the side the
            // car in front is not defending by being on the racing line.
            return track.LineCurvature[self.Index] >= 0f ? 1f : -1f;
        }

        /// <summary>
        /// Carries on with the pass it committed to, on the side it chose. Ends it when the
        /// car is clearly past, and gives it up when the other car gets away or the pass has
        /// gone on long enough that it is not working, after which it leaves that car alone for
        /// a while rather than trying again at once.
        /// </summary>
        void KeepPassing(TrackData track, Seen[] field, int me, float dt)
        {
            Seen self = field[me];
            Seen other = field[_passing];

            float itsLead = Distance(track, self.Index, other.Index);
            float myLead = Distance(track, other.Index, self.Index);
            bool past = myLead < itsLead;

            if (other.Gone || (past && myLead > PassedM))
            {
                _passing = -1;
                return;
            }

            // Gaining means a new best by at least half a metre. The lead moves in whole
            // samples, so a gap that is not really changing flickers by 2 m either way, and
            // only a new best can reset the clock; the flicker alone never does.
            float lead = past ? -myLead : itsLead;
            if (lead < _passBestM - 0.5f)
            {
                _passBestM = lead;
                _passStalled = 0f;
            }
            else _passStalled += dt;

            if (!past && (itsLead > 60f || _passStalled > PassHorizonSeconds))
            {
                _gaveUpOn = _passing;
                _retryIn = PassRetrySeconds;
                _passing = -1;
            }
        }

        /// <summary>
        /// Whether this car's path and <paramref name="other"/>'s come within InTheWayM of each
        /// other before this car would catch it: each car's lane, or the racing line if it is
        /// on none, compared sample by sample from where the other is to where this car would
        /// reach it at the speed it is closing. Where they do, the other car is in the way
        /// already, though it is well to one side now.
        ///
        /// Judged by where the cars are now, a car in a lane was not in the way of one coming up
        /// on the racing line until the line swept into its lane, 4 m away, with the car behind
        /// doing 20 to 30 km/h more: too late to stop.
        /// </summary>
        bool PathsMeet(TrackData track, in Seen self, in Seen other, float gap)
        {
            // At least PathsLookM ahead of it, closing or not: judged only while closing, the car
            // behind forgot the other as soon as it had slowed to its speed, and sped up again.
            float closing = self.SpeedMs - MathF.Max(other.SpeedMs, 0f);
            float catchM = closing > 0.5f ? MathF.Min(MathF.Max(other.SpeedMs, 0f) * gap / closing, 150f) : 0f;
            catchM = MathF.Max(catchM, PathsLookM);
            int steps = (int)(catchM / track.SampleSpacingM);
            for (int k = 0; k <= steps; k++)
            {
                int j = track.Wrap(other.Index + k);
                float mine = Path.Lane != 0 ? Path.Lane * track.LaneHalfM[j] : track.LineFromCentreM[j];
                float theirs = other.Lane != 0 ? other.Lane * track.LaneHalfM[j] : track.LineFromCentreM[j];
                if (MathF.Abs(mine - theirs) < InTheWayM) return true;
            }
            return false;
        }

        /// <summary>Whether the lane on <paramref name="side"/> can be taken at the speed the car
        /// is doing: its plan over the braking distance ahead. See LaneEntryMarginMs.</summary>
        bool LaneReachable(TrackData track, in Seen self, int side)
        {
            float braking = MathF.Max(_brakingMs2 * BrakingMargin, 0.5f);
            float reach = self.SpeedMs * self.SpeedMs / (2f * braking) + 20f;
            int steps = (int)(reach / track.SampleSpacingM);
            for (int k = 0; k <= steps; k++)
            {
                // What it could slow to by then, against what the lane allows there.
                float by = MathF.Sqrt(MathF.Max(self.SpeedMs * self.SpeedMs - 2f * braking * k * track.SampleSpacingM, 0f));
                if (Path.LanePlanAt(side, self.Index + k) < by - LaneEntryMarginMs) return false;
            }
            return true;
        }

        /// <summary>Whether to give up the corner to <paramref name="other"/>, a car alongside
        /// and ahead, however slightly, with a tight corner coming. See TightCornerRadiusM.</summary>
        bool GiveUpCorner(TrackData track, in Seen self, in Seen other, int me, int it, float gap)
        {
            if (gap > CompanyAlongM) return false;
            float across = track.FromCentre(self.Index, self.LateralM) - track.FromCentre(other.Index, other.LateralM);
            if (MathF.Abs(across) > CompanyAcrossM) return false;
            if (gap < 0.5f && me < it) return false;   // dead level: the higher number yields

            float braking = MathF.Max(_brakingMs2 * BrakingMargin, 0.5f);
            float reach = self.SpeedMs * self.SpeedMs / (2f * braking) + TightMarginM;
            float tight = 1f / TightCornerRadiusM;
            for (float d = 0f; d < reach; d += track.SampleSpacingM)
                if (MathF.Abs(track.LineCurvature[track.Wrap(self.Index + (int)(d / track.SampleSpacingM))]) > tight)
                    return true;
            return false;
        }

        /// <summary>
        /// The lane to take for the car alongside, the nearest one if several: -1 left, +1
        /// right, 0 when nobody is. The lane it is already in, if every car alongside allows it,
        /// so a third car coming close does not flick it to the other side for a moment. Its own side of that car, unless the other has already
        /// taken a lane, in which case the other one. Two cars both already in the same lane
        /// settle it by which is on that lane's side, which the two of them always answer
        /// the opposite way; and dead level, by car number, which they do too.
        /// </summary>
        int CompanyLane(TrackData track, Seen[] field, int me, out int alongside)
        {
            alongside = -1;
            Seen self = field[me];
            float mine = track.FromCentre(self.Index, self.LateralM);
            float nearest = float.MaxValue;
            int lane = 0;
            bool keepAllowed = Path.Lane != 0;
            int company = 0;
            for (int i = 0; i < field.Length; i++)
            {
                if (i == me || field[i].Gone) continue;
                float along = MathF.Min(Gap(track, self, field[i]), Gap(track, field[i], self));
                if (along > CompanyAlongM) continue;
                float theirs = track.FromCentre(field[i].Index, field[i].LateralM);
                float across = mine - theirs;
                if (MathF.Abs(across) > CompanyAcrossM) continue;

                int mySide = MathF.Abs(across) > 0.2f ? (across > 0f ? 1 : -1) : (me < i ? 1 : -1);
                int choice;
                if (i == _passing) choice = _passSide > 0f ? 1 : -1;
                else if (field[i].Lane != 0 && Path.Lane == 0) choice = -field[i].Lane;
                else if (field[i].Lane != 0 && field[i].Lane == Path.Lane) choice = mySide == Path.Lane ? Path.Lane : -Path.Lane;
                else choice = mySide;

                company++;
                if (choice != Path.Lane) keepAllowed = false;
                if (along < nearest) { nearest = along; lane = choice; alongside = i; }
            }
            return company > 0 && keepAllowed ? Path.Lane : lane;
        }

        /// <summary>
        /// Whether a pass can work. Either the car ahead is in trouble, doing far less than
        /// both the plan asks for where it is and what this car is doing; or, driving both
        /// cars' plans over the road ahead for PassHorizonSeconds, this one gains at least the
        /// gap it has to close.
        ///
        /// Plan against plan, like for like. The first version compared this driver's plan
        /// with the other car's actual speed, and since every driver runs well under its plan
        /// out of a corner, slower drivers were told they were quicker. The second compared
        /// pace alone, which is the right question asked too loosely: drivers set off after
        /// cars they would gain a few metres a lap on, and 400 attempts in four races ended
        /// with no pass. Both halves of the trouble test are needed: against the plan alone,
        /// every car on a standing start is in trouble, and against this car's speed alone,
        /// so is a car braking for a corner that this one has not reached yet.
        /// </summary>
        bool WorthPassing(TrackData track, in Seen self, in Seen other, float gapM)
        {
            if (self.SpeedMs > TroubleMinSpeedMs
                && other.SpeedMs < TroubleShare * MathF.Min(Path.PlanAt(other.Index), self.SpeedMs))
                return true;
            if (other.Plan == null) return false;

            float ds = track.SampleSpacingM;
            float mine = 0f, theirs = 0f, covered = 0f;
            for (int k = 0; theirs < PassHorizonSeconds && k < track.Count; k++)
            {
                int i = track.Wrap(other.Index + k);
                theirs += ds / MathF.Max(other.Plan[i], 1f);
                mine += ds / MathF.Max(Path.PlanAt(i), 1f);
                covered += ds;
            }

            // The time it saves over the same stretch, as distance at the other car's speed there.
            return (theirs - mine) * covered / theirs >= gapM;
        }

        /// <summary>A plan's lap time driven perfectly. Plans never change, so each is summed once.</summary>
        float LapSeconds(IReadOnlyList<float> plan, TrackData track)
        {
            if (_lapSeconds.TryGetValue(plan, out float seconds)) return seconds;

            seconds = 0f;
            for (int i = 0; i < plan.Count; i++)
                seconds += track.SampleSpacingM / MathF.Max(plan[i], 1f);
            _lapSeconds[plan] = seconds;
            return seconds;
        }

        /// <summary>
        /// Whether the sideways gap to the car it is passing is holding: at least SecureSideM
        /// now, and still that much SideLookaheadSeconds from now at the rate it is changing.
        /// A squeeze shows up as the rate before it shows up as the gap.
        /// </summary>
        bool SideHolding(in Seen self, in Seen other, float dt)
        {
            float side = MathF.Abs(other.LateralM - self.LateralM);
            float rate = _passSideSeen >= 0f ? (side - _passSideSeen) / dt : 0f;
            _passSideSeen = side;
            return side + MathF.Min(rate, 0f) * SideLookaheadSeconds >= SecureSideM;
        }

        /// <summary>
        /// How far <paramref name="other"/> is ahead of <paramref name="self"/>, for any
        /// question of whether the two might touch.
        ///
        /// Along the racing line alone this was wrong by exactly the margin that mattered. Two
        /// cars inside the line through a tight chicane are closer than the line says, because
        /// the inside is the shorter way round, and whole samples add up to 2 m of rounding on
        /// top. At Monza's first chicane it read 5.9 m with the cars 4.5 m apart, touching,
        /// when the minimum gap leaves 2.1 m of daylight. Close up, the straight line between
        /// them is the better measure, and taking the smaller of the two can only ever say
        /// closer. Further away the racing line is kept, because across a hairpin a straight
        /// line would put a car that is a hundred metres up the road right in front.
        ///
        /// Projecting the straight line onto the road's direction was tried, to get rid of the
        /// 2 m steps, and was worse on every measure: it reads two cars side by side as no
        /// distance apart at all, and every rule with a gap in it reacted to that. The steps
        /// remain, and make the side-by-side rule flicker for two cars hovering near its 8 m
        /// edge, which moves the line they drive by a few centimetres and nothing more.
        /// </summary>
        static float Gap(TrackData track, in Seen self, in Seen other)
        {
            float along = Distance(track, self.Index, other.Index);
            if (along > 20f) return along;

            Vector3 between = other.Position - self.Position;
            between.Y = 0f;
            return MathF.Min(along, between.Length());
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
