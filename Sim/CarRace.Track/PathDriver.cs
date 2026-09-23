using System;
using System.Numerics;
using CarRace.Vehicle;

namespace CarRace.Track
{
    /// <summary>
    /// Drives a car along the racing line: pure pursuit for steering, a PI controller on
    /// the speed plan for throttle and brake.
    ///
    /// This is a reference driver, not a racing driver. It has no notion of other cars,
    /// it never takes a different line, and it gives up nothing to save a tyre. What it
    /// is for is measurement: it drives a real circuit the same way every time, so a lap
    /// time means something about the car rather than about who was holding the pad.
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

        /// <summary>Seconds of travel to look ahead for a lower speed limit, covering the
        /// lag between asking for the brakes and the car slowing down.</summary>
        public float ReactionSeconds = 0.5f;

        public float SpeedGain = 0.35f;
        public float SpeedIntegralGain = 0.6f;

        /// <summary>Where on the line the car is, and how many times it has been round.</summary>
        public int Index { get; private set; }
        public int Laps { get; private set; }
        public float TargetSpeedMs { get; private set; }

        /// <summary>Signed distance from the racing line, positive to its right.</summary>
        public float LineErrorM { get; private set; }
        public float HeadingErrorDeg { get; private set; }

        float _speedIntegral;

        public PathDriver(TrackData track, float[] plan, CarConfig car)
        {
            _track = track;
            _plan = plan;
            _car = car;
        }

        /// <summary>Puts the driver at a sample without counting a lap.</summary>
        public void StartAt(int index)
        {
            Index = _track.Wrap(index);
            Laps = 0;
            _speedIntegral = 0f;
        }

        public VehicleInputs Drive(in BodyState body, float dt)
        {
            AdvanceAlongLine(body.Position);

            float speed = Vector3.Dot(body.Velocity, body.Forward);

            return new VehicleInputs
            {
                Steer = Steer(body, speed),
                Throttle = Throttle(speed, dt, out float brake),
                Brake = brake,
                Handbrake = 0f,
                Clutch = false,
            };
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
        float Steer(in BodyState body, float speed)
        {
            int lead = _track.Wrap(Index + (int)MathF.Round(
                FeedforwardSeconds * MathF.Abs(speed) / _track.SampleSpacingM));
            float roadCurvature = _track.LineCurvature[lead];

            Vector3 tangent = _track.Tangent(_track.Line, Index);

            // Error measured at the front axle, not at the centre of mass. This is the
            // Stanley form, and the axle is ahead of the mass, so the error it sees already
            // includes where the car is about to be: that lead is most of what keeps the
            // loop stable near the limit. Measured from the centre of mass, the car could
            // only be trusted with 70% of the grip it has before the correction arrived too
            // late and the front axle was already past its peak slip angle.
            Vector3 axle = body.Position + body.Forward * _car.FrontAxleToCg;
            Vector3 delta = axle - _track.Line[Index];
            delta.Y = 0f;
            float crossTrack = Vector3.Dot(delta, TrackData.Right(tangent));

            Vector3 forward = body.Forward;
            forward.Y = 0f;
            float headingError = MathF.Atan2(Vector3.Dot(forward, TrackData.Right(tangent)),
                                             Vector3.Dot(forward, tangent));

            float wheelAngle = MathF.Atan(roadCurvature * _car.Wheelbase)
                             - HeadingGain * headingError
                             - MathF.Atan(CrossTrackGain * crossTrack / (MathF.Abs(speed) + 2f));

            LineErrorM = crossTrack;
            HeadingErrorDeg = headingError * 180f / MathF.PI;

            float fraction = wheelAngle / (_car.MaxSteerAngleDegrees * MathF.PI / 180f);

            // The model gives the driver less lock as speed rises, so asking for a fraction
            // of full lock delivers less than that fraction. Undo it here, or the car
            // understeers out of every fast corner and the cause looks like the tyres.
            float falloff = 1f + MathF.Abs(speed) / MathF.Max(_car.SteerFalloffSpeed, 0.01f);

            return Clamp(fraction * falloff, -1f, 1f);
        }

        float Throttle(float speed, float dt, out float brake)
        {
            TargetSpeedMs = PlannedSpeed(speed);

            float error = TargetSpeedMs - speed;
            _speedIntegral = Clamp(_speedIntegral + error * dt * SpeedIntegralGain, -1f, 1f);
            float demand = error * SpeedGain + _speedIntegral;

            if (demand >= 0f)
            {
                brake = 0f;
                return MathF.Min(demand, 1f);
            }

            brake = MathF.Min(-demand, 1f);
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
            float lowest = _plan[Index];
            for (int step = 1; step <= reach; step++)
            {
                float candidate = _plan[_track.Wrap(Index + step)];
                if (candidate < lowest) lowest = candidate;
            }
            return lowest;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
