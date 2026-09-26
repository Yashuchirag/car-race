using System;
using System.Collections.Generic;
using System.Numerics;
using CarRace.Vehicle;

namespace CarRace.Track
{
    /// <summary>
    /// Drives a car along the racing line, or a passing lane (Lane): pure pursuit for
    /// steering, a PI controller on the speed plan for throttle and brake.
    ///
    /// It knows nothing about other cars. Everything to do with racing one lives in
    /// RaceDriver, which steers this by moving the line it aims at and capping the speed it
    /// asks for. Kept apart on purpose: this one drives a circuit the same way every time,
    /// so a lap time means something about the car rather than about the traffic.
    /// </summary>
    public sealed class PathDriver
    {
        readonly TrackData _track;
        readonly float[] _plan;
        readonly CarConfig _car;

        /// <summary>How far ahead to read the road's own curvature, as seconds of travel.
        /// Some lead is needed because the steering rack takes time to move.</summary>
        public float FeedforwardSeconds = 0.25f;

        /// <summary>Cross-track gain, in the Stanley sense: metres of error become an angle
        /// that shrinks with speed, so one metre off line is a big correction at walking pace
        /// and a small one at 250 km/h. A fixed gain cannot be both.</summary>
        public float CrossTrackGain = 0.9f;

        /// <summary>How hard to correct pointing the wrong way, as a fraction of the heading
        /// error fed straight to the wheel.</summary>
        public float HeadingGain = 0.7f;

        /// <summary>
        /// A slow correction for a steady offset from the line, radians of lock per metre-second,
        /// capped at CrossTrackIntegralMaxRad. The feedforward steers for the road's curvature
        /// alone, and at speed a car needs a little more lock than that: in Unity every AI ran a
        /// steady metre wide through every fast bend, and at Ise Bay's fastest that grew to four,
        /// onto the verge, where it spun. Small and slow on purpose: at five times this, capped
        /// at 2 degrees, it wound the car into a weave of 6 m either side at 180 km/h.
        /// </summary>
        public float CrossTrackIntegralGain = 0.004f;
        public float CrossTrackIntegralMaxRad = 0.01f;

        /// <summary>
        /// Running wide, on the outside of a bend by more than a metre, it asks for this much
        /// less speed per metre beyond that, as a driver lifts to tighten the line.
        /// </summary>
        public float WideLiftPerM = 0.04f;

        /// <summary>Seconds of travel to look ahead for a lower speed limit, covering the
        /// lag between asking for the brakes and the car slowing down.</summary>
        public float ReactionSeconds = 0.5f;

        /// <summary>Sideslip angle at which it starts giving up on the line and starts
        /// catching the car instead, and the angle by which it is doing nothing else.</summary>
        public float RecoveryStartDeg = 12f;
        public float RecoveryFullDeg = 32f;

        public float SpeedGain = 0.35f;
        public float SpeedIntegralGain = 0.6f;

        /// <summary>Sideways shift of the line being driven, positive to its right. How a
        /// second car is given somewhere else to be.</summary>
        public float LineOffsetM;

        /// <summary>
        /// A passing lane to drive instead of the racing line: -1 the left lane, +1 the right,
        /// 0 the racing line. The car moves between them with the point it aims at going no
        /// faster sideways than LaneRateMs, blending the curvature it steers for and the plan
        /// it keeps to with it, and on the way out of a lane back onto the line keeps the
        /// lane's side until it is there. A fixed time for the change instead moved the aim
        /// 7 m in 1.4 s where a lane was far from the line, and at 190 km/h the car could not
        /// follow it. At 2.2 m/s the headless races had 40 spins, at 1.5 m/s 22, with more
        /// passes made.
        /// </summary>
        public int Lane;
        public float LaneRateMs = 1.5f;

        /// <summary>How far into a lane the car is, 0 on the racing line, 1 in the lane.</summary>
        public float LaneBlend => _laneBlend;
        public int LaneSide => _laneSide;

        /// <summary>Ceiling on the speed asked for, whatever the plan says. Negative means
        /// no cap. This is how a car behind is made to sit behind.</summary>
        public float SpeedCapMs = -1f;

        /// <summary>
        /// Grip of the surface under the car, its wheels' average, 1 on asphalt and 0.45 on
        /// grass; set by whoever owns the car before each Drive. Below OffRoadGrip the car is
        /// off the road: it asks for the plan's speed scaled to what the grip will carry round
        /// the same curve, the square root of the grip, and steers back towards its line at no
        /// more than RejoinAngleDeg. On the plan's asphalt speed an AI on the grass could not
        /// turn and slid on into the wall; aimed straight back at the road it spun where the
        /// grass met the asphalt.
        /// </summary>
        public float SurfaceGrip = 1f;
        public float RejoinAngleDeg = 20f;

        /// <summary>
        /// Off the road means three wheels or more on the grass: an average grip under this.
        /// At 0.9, two wheels over the edge of a fast bend counted, the speed asked for fell by
        /// a sixth in a few metres, and the full brake that asked for spun the car.
        /// </summary>
        public float OffRoadGrip = 0.65f;

        /// <summary>
        /// The car's cornering grip, m/s^2, what it can actually hold rather than what the
        /// plan uses; set by the driver that owns it. With it the brake is kept within the
        /// friction ellipse: while the tyres are cornering at a share u of this, the pedal goes
        /// no further than the square root of 1 - u^2.
        /// </summary>
        public float LateralGripMs2 = 9f;

        /// <summary>How fast the brake pedal may go down, per second. A driver squeezes the
        /// brake rather than stamping on it: from nothing to full in a step, mid-bend, is how
        /// a car at the limit loses its rear.</summary>
        public float BrakeSqueezePerS = 6f;

        /// <summary>Where on the line the car is, and how many times it has been round.</summary>
        public int Index { get; private set; }
        public int Laps { get; private set; }

        /// <summary>The speed asked for this step, after any cap.</summary>
        public float TargetSpeedMs { get; private set; }

        /// <summary>The speed the plan alone would have asked for, ignoring the cap. What a
        /// driver held up behind someone would be doing with clear road, and so the only
        /// honest way to ask whether it is quicker than the car in front.</summary>
        public float PlannedSpeedMs { get; private set; }

        /// <summary>
        /// Signed distance from the line the car is AIMING at, positive to its right. Near
        /// zero whenever the car is tracking well, so it is a measure of driving quality and
        /// NOT of where the car is across the road. For that, use LateralFromLineM.
        /// </summary>
        public float LineErrorM { get; private set; }

        /// <summary>
        /// Where the car actually is across the road, relative to the racing line, positive to
        /// its right. This is what other cars need to know about it.
        ///
        /// Passing LineErrorM around instead is a mistake that hides well: every car reports
        /// itself as sitting on the racing line, because that is what it is trying to do, so
        /// no car can tell whether another is alongside it or half the track away. A whole
        /// field's worth of overtaking and avoidance logic ran on zeros and looked merely
        /// badly tuned.
        /// </summary>
        public float LateralFromLineM { get; private set; }
        public float HeadingErrorDeg { get; private set; }

        /// <summary>How much of the driver is currently busy catching a slide, 0 to 1.</summary>
        public float Recovering { get; private set; }

        float _speedIntegral;
        float _crossIntegral;
        float _wide;
        float _brake;
        float _laneBlend;
        int _laneSide = 1;
        readonly float[][] _lanePlans;

        public PathDriver(TrackData track, float[] plan, CarConfig car, float[][] lanePlans = null)
        {
            _track = track;
            _plan = plan;
            _car = car;
            _lanePlans = lanePlans;
        }

        /// <summary>Distance covered since the start, in metres, laps included.</summary>
        public float ProgressM => Laps * _track.LengthM + Index * _track.SampleSpacingM;

        /// <summary>What the plan asks for at a sample, before any cap or offset.</summary>
        public float PlanAt(int index) => _plan[_track.Wrap(index)];

        /// <summary>What a passing lane's plan asks for at a sample: side -1 left, +1 right.</summary>
        public float LanePlanAt(int side, int index) =>
            _lanePlans != null ? _lanePlans[side > 0 ? 1 : 0][_track.Wrap(index)] : PlanAt(index);

        /// <summary>The whole plan, read only, for a driver weighing its plan against this one.</summary>
        public IReadOnlyList<float> Plan => _plan;

        /// <summary>
        /// Puts the driver at a sample without counting a lap. A car on a grid behind the
        /// start line starts on lap -1, so that crossing the line is what begins its first
        /// lap and every car drives the same distance however far back it started.
        /// </summary>
        public void StartAt(int index, int laps = 0)
        {
            Index = _track.Wrap(index);
            Laps = laps;
            _speedIntegral = 0f;
        }

        float _dt;

        public VehicleInputs Drive(in BodyState body, float dt)
        {
            _dt = dt;
            AdvanceAlongLine(body.Position);
            MoveBetweenLanes(dt);
            LateralFromLineM = _track.LateralOffset(_track.Line, Index, body.Position);

            float speed = Vector3.Dot(body.Velocity, body.Forward);
            float sideslip = Sideslip(body);
            Recovering = Clamp((MathF.Abs(sideslip) * 180f / MathF.PI - RecoveryStartDeg)
                               / MathF.Max(RecoveryFullDeg - RecoveryStartDeg, 1f), 0f, 1f);

            float throttle = Throttle(speed, dt, out float brake);

            // Sliding: steer where the car is actually going and stop asking for power.
            //
            // Without this a spin is the end of the car's race. It ends up pointing somewhere
            // other than where it is travelling, the line controller asks for lock that makes
            // the slide worse, and the speed controller keeps demanding throttle, which for a
            // rear wheel drive car keeps the rear spinning. Cars sat at walking pace and 80
            // degrees of sideslip for lap after lap, and everyone else drove into them.
            float wheelAngle = Lerp(PathWheelAngle(body, speed), sideslip, Recovering);

            return new VehicleInputs
            {
                Steer = SteerInput(wheelAngle, speed),
                Throttle = throttle * (1f - Recovering) * TractionShare(sideslip),
                Brake = brake * (1f - 0.5f * Recovering),
                Handbrake = 0f,
                Clutch = false,
            };
        }

        /// <summary>Angle between where the car points and where it is going, radians,
        /// positive when it is travelling to the right of its nose.</summary>
        static float Sideslip(in BodyState body)
        {
            if (body.Velocity.LengthSquared() < 1f) return 0f;
            float forward = Vector3.Dot(body.Velocity, body.Forward);
            float right = Vector3.Dot(body.Velocity, body.Right);
            return MathF.Atan2(right, MathF.Abs(forward));
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// Power eased off as the car starts to slide, all of it below TractionFromDeg of
        /// sideslip and none by RecoveryStartDeg, where catching the slide takes over: the
        /// traction control a player has. Between the two the driver used to floor it, and a
        /// car sliding 9 degrees at 150 km/h on full throttle fishtailed into a spin in the
        /// middle of a sixteen car pack.
        /// </summary>
        float TractionShare(float sideslip)
        {
            float degrees = MathF.Abs(sideslip) * 180f / MathF.PI;
            return 1f - Clamp((degrees - TractionFromDeg) / MathF.Max(RecoveryStartDeg - TractionFromDeg, 1f), 0f, 1f);
        }

        public float TractionFromDeg = 4f;

        /// <summary>Into the lane asked for, or back out to the line, at a steady rate. A
        /// change of side goes back to the line first.</summary>
        void MoveBetweenLanes(float dt)
        {
            if (_lanePlans == null || _track.LanePoints == null) { _laneBlend = 0f; return; }
            int i = Index;
            Vector3 racing = _track.Line[i] + TrackData.Right(_track.Tangent(_track.Line, i)) * LineOffsetM;
            Vector3 apart = _track.LanePoints[LaneArray][i] - racing;
            apart.Y = 0f;
            float step = LaneRateMs * dt / MathF.Max(apart.Length(), 0.5f);
            bool wanted = Lane != 0 && (Lane > 0 ? 1 : -1) == _laneSide;
            if (Lane != 0 && _laneBlend <= 0f) { _laneSide = Lane > 0 ? 1 : -1; wanted = true; }
            _laneBlend = Clamp(_laneBlend + (wanted ? step : -step), 0f, 1f);
        }

        int LaneArray => _laneSide > 0 ? 1 : 0;

        /// <summary>The point the car aims at for a sample: the racing line, shifted by
        /// LineOffsetM, blended towards the lane it is moving into.</summary>
        Vector3 AimAt(int index)
        {
            index = _track.Wrap(index);
            Vector3 racing = _track.Line[index] + TrackData.Right(_track.Tangent(_track.Line, index)) * LineOffsetM;
            return _laneBlend > 0f ? Vector3.Lerp(racing, _track.LanePoints[LaneArray][index], _laneBlend) : racing;
        }

        /// <summary>The plan where the car is going: the racing line's, the lane's once in it,
        /// and in between the slower of the two, since the car is on neither.</summary>
        float PlanFor(int index)
        {
            index = _track.Wrap(index);
            if (_laneBlend <= 0f) return _plan[index];
            float lane = _lanePlans[LaneArray][index];
            return _laneBlend >= 1f ? lane : MathF.Min(_plan[index], lane);
        }

        /// <summary>
        /// Nearest sample, searched forward only from the last one. A search over the whole
        /// lap would snap to the wrong side of a hairpin, where the road doubles back within
        /// a few metres of itself, and the car would be told it had already been round.
        /// </summary>
        void AdvanceAlongLine(Vector3 position)
        {
            const int window = 25;      // 50 m at 2 m spacing, far more than one step moves
            int best = Index;
            float bestDistance = float.MaxValue;

            for (int step = 0; step <= window; step++)
            {
                int i = _track.Wrap(Index + step);
                Vector3 delta = _track.Line[i] - position;
                delta.Y = 0f;
                float distance = delta.LengthSquared();
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }

            if (best < Index) Laps++;    // the search wrapped past the start line
            Index = best;
        }

        /// <summary>
        /// Feedforward on the road's curvature, plus feedback on how far off the line the car
        /// is and how far from parallel it is pointing.
        ///
        /// Pure pursuit alone was tried first and is not good enough. Aiming at a point some
        /// distance ahead cuts the corner, so the error grows through the turn, and the
        /// correction arrives when the front tyres are already near their limit: the car runs
        /// wide, the controller answers with more lock, the front slip angle goes past the
        /// peak where more steering means less grip, and it leaves the road. Steering for the
        /// curvature the road actually has means the feedback only has to trim.
        /// </summary>
        float PathWheelAngle(in BodyState body, float speed)
        {
            int lead = _track.Wrap(Index + (int)MathF.Round(
                FeedforwardSeconds * MathF.Abs(speed) / _track.SampleSpacingM));
            float roadCurvature = _laneBlend > 0f
                ? Lerp(_track.LineCurvature[lead], _track.LaneCurvature[LaneArray][lead], _laneBlend)
                : _track.LineCurvature[lead];

            Vector3 tangent = AimAt(Index + 1) - AimAt(Index - 1);
            tangent.Y = 0f;
            tangent = tangent.LengthSquared() > 1e-8f ? Vector3.Normalize(tangent) : _track.Tangent(_track.Line, Index);
            Vector3 aimPoint = AimAt(Index);

            // Error measured at the front axle, not at the centre of mass. This is the
            // Stanley form, and the axle is ahead of the mass, so the error it sees already
            // includes where the car is about to be: that lead is most of what keeps the
            // loop stable near the limit. Measured from the centre of mass, the car could
            // only be trusted with 70% of the grip it has before the correction arrived too
            // late and the front axle was already past its peak slip angle.
            Vector3 axle = body.Position + body.Forward * _car.FrontAxleToCg;
            Vector3 delta = axle - aimPoint;
            delta.Y = 0f;
            float crossTrack = Vector3.Dot(delta, TrackData.Right(tangent));

            Vector3 forward = body.Forward;
            forward.Y = 0f;
            float headingError = MathF.Atan2(Vector3.Dot(forward, TrackData.Right(tangent)),
                                             Vector3.Dot(forward, tangent));

            float correction = MathF.Atan(CrossTrackGain * crossTrack / (MathF.Abs(speed) + 2f));
            if (SurfaceGrip < OffRoadGrip)
            {
                float most = RejoinAngleDeg * MathF.PI / 180f;
                correction = Clamp(correction, -most, most);
            }
            // The integral only while driving normally at speed, and dropped while catching a
            // slide, so a spin does not wind it up.
            if (Recovering > 0f || MathF.Abs(speed) < 10f) _crossIntegral = 0f;
            else _crossIntegral = Clamp(_crossIntegral + CrossTrackIntegralGain * crossTrack * _dt,
                                        -CrossTrackIntegralMaxRad, CrossTrackIntegralMaxRad);

            // How far wide of the line it is, on the outside of the bend: a right-hand bend's
            // outside is the left, which is a negative cross-track.
            _wide = MathF.Max(0f, -MathF.Sign(roadCurvature) * crossTrack - 1f);

            float wheelAngle = MathF.Atan(roadCurvature * _car.Wheelbase)
                             - HeadingGain * headingError
                             - correction
                             - _crossIntegral;

            LineErrorM = crossTrack;
            HeadingErrorDeg = headingError * 180f / MathF.PI;
            return wheelAngle;
        }

        /// <summary>Turns a wanted front wheel angle into a steering input.</summary>
        float SteerInput(float wheelAngle, float speed)
        {
            float fraction = wheelAngle / (_car.MaxSteerAngleDegrees * MathF.PI / 180f);

            // The model gives the driver less lock as speed rises, so asking for a fraction
            // of full lock delivers less than that fraction. Undo it here, or the car
            // understeers out of every fast corner and the cause looks like the tyres.
            float falloff = 1f + MathF.Abs(speed) / MathF.Max(_car.SteerFalloffSpeed, 0.01f);

            return Clamp(fraction * falloff, -1f, 1f);
        }

        float Throttle(float speed, float dt, out float brake)
        {
            PlannedSpeedMs = PlannedSpeed(speed) * OffsetSpeedScale()
                           * MathF.Max(0.7f, 1f - WideLiftPerM * _wide)
                           * (SurfaceGrip < OffRoadGrip ? MathF.Sqrt(Clamp(SurfaceGrip, 0.1f, 1f)) : 1f);
            TargetSpeedMs = SpeedCapMs >= 0f && PlannedSpeedMs > SpeedCapMs
                ? SpeedCapMs : PlannedSpeedMs;

            float error = TargetSpeedMs - speed;

            // No integrating while the pedal is already pinned the way the error pushes. A
            // full throttle climb out of a corner otherwise stores up to a whole pedal of
            // demand, which then held the throttle open for half a second after the car
            // reached a lower target: 7 km/h over a follow cap in a chicane, and a touch.
            float unclamped = error * SpeedGain + _speedIntegral;
            bool pinned = (unclamped >= 1f && error > 0f) || (unclamped <= -1f && error < 0f);
            if (!pinned)
                _speedIntegral = Clamp(_speedIntegral + error * dt * SpeedIntegralGain, -1f, 1f);
            float demand = error * SpeedGain + _speedIntegral;

            if (demand >= 0f)
            {
                brake = 0f;
                _brake = 0f;
                return MathF.Min(demand, 1f);
            }

            brake = MathF.Min(-demand, 1f);

            // For the road only. Braking for a car ahead, held to a speed cap, is not
            // squeezed: kept gentle there it arrived in the back of the other car.
            bool forTheRoad = !(SpeedCapMs >= 0f && SpeedCapMs < PlannedSpeedMs);
            if (forTheRoad)
            {
                float lateralUse = speed * speed * MathF.Abs(_track.LineCurvature[Index]) / MathF.Max(LateralGripMs2, 1f);
                if (lateralUse > 0.95f) lateralUse = 0.95f;
                brake = MathF.Min(brake, MathF.Sqrt(1f - lateralUse * lateralUse));
                brake = MathF.Min(brake, _brake + BrakeSqueezePerS * dt);
            }
            _brake = brake;
            return 0f;
        }

        /// <summary>
        /// Lowest planned speed between here and a reaction time ahead. Reading the plan at
        /// the car's own position alone means the brakes come on at the point where the
        /// slower speed was already required.
        /// </summary>
        float PlannedSpeed(float speed)
        {
            int reach = (int)MathF.Round(ReactionSeconds * MathF.Abs(speed) / _track.SampleSpacingM);
            float lowest = PlanFor(Index);
            for (int step = 1; step <= reach; step++)
            {
                float candidate = PlanFor(Index + step);
                if (candidate < lowest) lowest = candidate;
            }
            return lowest;
        }

        /// <summary>
        /// How much of the plan's speed is safe on a line shifted off the racing line.
        ///
        /// The plan belongs to the racing line. A car passing on the inside of a corner is on
        /// a smaller radius than the one the plan was written for, and carrying the plan's
        /// speed there simply will not make it round: this showed up as cars completing a
        /// clean overtake and then arriving thirty metres into a field. Moving in by o on a
        /// corner of curvature k turns radius R into R minus o, so the speed available goes
        /// with the square root of one minus k times o.
        ///
        /// The outside line gets no bonus, though the same arithmetic offers one. It is a
        /// longer way round and it is where the grip runs out first.
        /// </summary>
        float OffsetSpeedScale()
        {
            if (MathF.Abs(LineOffsetM) < 0.05f) return 1f;

            float tightening = 1f - _track.LineCurvature[Index] * LineOffsetM;
            if (tightening >= 1f) return 1f;
            return MathF.Sqrt(Clamp(tightening, 0.25f, 1f));
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
