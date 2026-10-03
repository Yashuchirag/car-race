using System;
using System.Collections.Generic;

namespace CarRace.Track
{
    /// <summary>
    /// The safety car: when it comes out, how it runs, and who passed under it. Updated every
    /// reaction interval by whoever owns the cars (RaceRun headless, RaceDirector in Unity), as
    /// RaceFlags is. Its car is an ordinary RaceDriver, Driver, which the owner drives like any
    /// other and adds to the field the drivers see, last; it is kept out of RaceControl.
    ///
    /// It comes out when two or more cars are in trouble in one yellow zone at once, or one car
    /// has stood still on the road for StoodSeconds; never on the first or last lap, and at most
    /// once every LapsBetween laps. ForceAt brings it out at a race time whatever is going on.
    ///
    /// Phases:
    /// Deployed: it waits in its box while the field, passing nobody, closes up at FieldShare of
    /// its plan; it is let go when the leader will reach the pit exit a little after it does,
    /// within ReleaseWindowS.
    /// Leading: it runs at PaceShare of its plan with the field queued behind it, and at
    /// PickupShare until the leader has caught it.
    /// InThisLap: the incident is clear and the field has queued, so it is called in, and turns
    /// into the pit lane at the end of the lap.
    /// Ending: it is in the pit lane; the leader sets the pace, still with no passing, and green
    /// comes as the leader crosses the line. Passes made from deployment to green are penalised
    /// then, leaving aside any car that pitted or was in trouble meanwhile.
    /// </summary>
    public sealed class SafetyCar
    {
        public enum Phase { In, Deployed, Leading, InThisLap, Ending }

        public const float FieldShare = 0.8f;
        public const float PaceShare = 0.6f;
        /// <summary>Slower still until the leader has caught it, within PickedUpM.</summary>
        public const float PickupShare = 0.5f;
        public const float PickedUpM = 150f;
        /// <summary>Let go when the leader will reach the pit exit between 1 and 1 + ReleaseWindowS
        /// seconds after it does: a car let go any later comes out behind the leader.</summary>
        public const float ReleaseWindowS = 30f;
        /// <summary>The least warning of it coming in: beyond the 400 m before the pit entry where
        /// its driver lines up for the pit lane.</summary>
        public const float CallNoticeM = 1000f;
        public const float StoodSeconds = 10f;
        public const int LapsBetween = 3;
        /// <summary>Queued: within this many seconds, plus QueueSlackM, of the car ahead.</summary>
        public const float QueueSeconds = 2f;
        public const float QueueSlackM = 15f;

        public readonly RaceDriver Driver;
        public Phase State { get; private set; } = Phase.In;
        public bool Enabled = true;
        public float ForceAt = -1f;

        /// <summary>How many times it has come out, and the race time of the last green.</summary>
        public int Deployments;
        public float GreenAt = -1f;

        readonly int _cars;
        readonly float[] _stood;
        readonly float[] _progressAtStart;
        readonly bool[] _excused;
        int _lapOut = -LapsBetween;
        bool _forced, _queuedOnce;
        int _lapsAtEnding;
        int _lastIndex = -1;

        public SafetyCar(RaceDriver driver, int cars)
        {
            Driver = driver;
            _cars = cars;
            _stood = new float[cars];
            _progressAtStart = new float[cars];
            _excused = new bool[cars];
        }

        /// <summary>Whether the field is under it: no passing from deployment to green.</summary>
        public bool Out => State != Phase.In;

        /// <summary>The share of its plan each car in the field may run at.</summary>
        public float Share => State == Phase.Ending ? 1f : FieldShare;

        /// <summary>Into its box, parked, before the start.</summary>
        public void Park(TrackData track)
        {
            Driver.Box = track.SafetyCarBox;
            Driver.ParkInBox(track, 0);
        }

        /// <summary>
        /// One look at the field. <paramref name="stopped"/> is each car standing still on the road
        /// (not in the pit lane), <paramref name="trouble"/> each car stopped or spun, as for
        /// RaceFlags; <paramref name="leaderLaps"/> the laps the leader has completed. Returns the
        /// passes made under it, as (passer, passed), at the green and only then.
        /// </summary>
        public List<(int Car, int Passed)> Update(TrackData track, RaceFlags flags, int[] index, float[] progressM,
                                                  float[] speedMs, bool[] trouble, bool[] stopped, bool[] racing,
                                                  bool[] pitting, int leaderLaps, int raceLaps, float time, float dt)
        {
            var passes = new List<(int, int)>();
            int leader = -1;
            for (int i = 0; i < _cars; i++)
            {
                _stood[i] = stopped[i] && racing[i] ? _stood[i] + dt : 0f;
                if (racing[i] && (leader < 0 || progressM[i] > progressM[leader])) leader = i;
            }
            if (Out) for (int i = 0; i < _cars; i++) _excused[i] |= trouble[i] || pitting[i] || !racing[i];
            int scIndex = Driver.Path.Index;
            bool crossed = _lastIndex >= 0 && scIndex < _lastIndex - track.Count / 2;
            _lastIndex = scIndex;

            switch (State)
            {
                case Phase.In:
                    if (!Enabled || leader < 0) break;
                    bool force = ForceAt >= 0f && time >= ForceAt && !_forced;
                    bool allowed = leaderLaps >= 1 && leaderLaps < raceLaps - 1 && leaderLaps - _lapOut >= LapsBetween;
                    if (force || allowed && Incident(track, flags, index, trouble, racing))
                    {
                        _forced |= force;
                        Deploy(progressM, trouble, pitting, racing, leaderLaps);
                    }
                    break;

                case Phase.Deployed:
                    // Let go so it reaches the pit exit a second or more before the leader.
                    if (leader >= 0 && Driver.Pit == RaceDriver.PitState.Stopped && Driver.Hold)
                    {
                        int n = track.Count;
                        float spacing = track.SampleSpacingM;
                        float scToExit = ((track.PitTo - track.SafetyCarBox) % n + n) % n * spacing;
                        float scSeconds = scToExit / (TrackData.PitLimitMs * 0.9f) + 4f;
                        float leaderToExit = ((track.PitTo - index[leader]) % n + n) % n * spacing;
                        float leaderSeconds = leaderToExit / MathF.Max(speedMs[leader], 20f);
                        if (leaderSeconds > scSeconds + 1f && leaderSeconds < scSeconds + 1f + ReleaseWindowS) Driver.Hold = false;
                    }
                    if (Driver.Pit == RaceDriver.PitState.Racing)
                    {
                        State = Phase.Leading;
                        _queuedOnce = false;
                    }
                    break;

                case Phase.Leading:
                    // Called in once the road is clear and the field has queued, unless it is
                    // already too near the pit entry to give the field CallNoticeM of warning, in
                    // which case it goes round once more; or at the line if the leader is
                    // starting the last lap.
                    bool clear = flags.Yellow.Count == 0;
                    if (clear && Queued(track, progressM, speedMs, racing, pitting)) _queuedOnce = true;
                    int count = track.Count;
                    float toEntry = ((track.PitFrom - scIndex) % count + count) % count * track.SampleSpacingM;
                    if (_queuedOnce && toEntry > CallNoticeM || crossed && leaderLaps >= raceLaps - 1)
                    {
                        State = Phase.InThisLap;
                        Driver.PitRequested = true;
                        Driver.Hold = true;
                    }
                    break;

                case Phase.InThisLap:
                    if (Driver.Pit == RaceDriver.PitState.InLane || Driver.Pit == RaceDriver.PitState.Stopped)
                    {
                        State = Phase.Ending;
                        _lapsAtEnding = leaderLaps;
                    }
                    break;

                case Phase.Ending:
                    if (leaderLaps > _lapsAtEnding)
                    {
                        State = Phase.In;
                        GreenAt = time;
                        for (int car = 0; car < _cars; car++)
                        {
                            if (_excused[car]) continue;
                            for (int other = 0; other < _cars; other++)
                            {
                                if (other == car || _excused[other]) continue;
                                if (_progressAtStart[other] > _progressAtStart[car] && progressM[other] < progressM[car])
                                    passes.Add((car, other));
                            }
                        }
                    }
                    break;
            }
            bool pickedUp = leader >= 0
                && ((scIndex * track.SampleSpacingM - progressM[leader]) % track.LengthM + track.LengthM) % track.LengthM < PickedUpM;
            Driver.PaceCap = State == Phase.Leading || State == Phase.InThisLap ? (pickedUp ? PaceShare : PickupShare) : 1f;
            return passes;
        }

        void Deploy(float[] progressM, bool[] trouble, bool[] pitting, bool[] racing, int leaderLaps)
        {
            State = Phase.Deployed;
            Deployments++;
            _lapOut = leaderLaps;
            for (int i = 0; i < _cars; i++)
            {
                _progressAtStart[i] = progressM[i];
                _excused[i] = trouble[i] || pitting[i] || !racing[i];
            }
        }

        /// <summary>Two or more cars in trouble in one yellow zone, or one stood still too long.</summary>
        bool Incident(TrackData track, RaceFlags flags, int[] index, bool[] trouble, bool[] racing)
        {
            for (int i = 0; i < _cars; i++) if (_stood[i] >= StoodSeconds) return true;
            foreach (RaceFlags.Zone zone in flags.Yellow)
            {
                int down = 0;
                for (int i = 0; i < _cars; i++)
                    if (trouble[i] && racing[i] && RaceFlags.Inside(track, zone, index[i])) down++;
                if (down >= 2) return true;
            }
            return false;
        }

        /// <summary>
        /// Every car still racing, out of the pit lane, close behind the one ahead of it on the
        /// road, back from the safety car: one queue, not a field strung out round the lap.
        /// Close is QueueSeconds at its own speed, or at 10 m/s, whichever is more, plus
        /// QueueSlackM: in a hairpin the queue concertinas, and nobody there is out of it.
        /// </summary>
        public bool Queued(TrackData track, float[] progressM, float[] speedMs, bool[] racing, bool[] pitting)
        {
            float length = track.LengthM;
            float sc = Driver.Path.Index * track.SampleSpacingM;
            var behind = new List<(float M, float Speed)>();
            for (int i = 0; i < _cars; i++)
            {
                if (!racing[i] || pitting[i]) continue;
                behind.Add((((sc - progressM[i]) % length + length) % length, speedMs[i]));
            }
            behind.Sort((a, b) => a.M.CompareTo(b.M));
            float last = 0f;
            foreach (var (m, speed) in behind)
            {
                if (m - last > QueueSeconds * MathF.Max(speed, 10f) + QueueSlackM) return false;
                last = m;
            }
            return true;
        }
    }
}
