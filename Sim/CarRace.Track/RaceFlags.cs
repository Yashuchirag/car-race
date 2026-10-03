using System;
using System.Collections.Generic;

namespace CarRace.Track
{
    /// <summary>
    /// The marshals: yellow flags where a car is in trouble, blue flags for a car about to be
    /// lapped, and who passed whom under a yellow. Updated every reaction interval by whoever
    /// owns the cars (RaceRun headless, RaceDirector in Unity), from what it already knows about
    /// each car: where it is on the lap, how far it has gone, and whether it is in trouble.
    ///
    /// A car is in trouble when it has stopped or spun after it got going; the owner decides
    /// that, since only it can see the bodies. Its yellow zone runs from ZoneBeforeM before it
    /// to ZoneAfterM past it, and is withdrawn ClearSeconds after the car is moving again.
    ///
    /// Inside a yellow zone nobody passes, except the car in trouble. A car that leaves a zone
    /// ahead of a car that was ahead of it when it went in has passed under the yellow: giving
    /// the place back before the end of the zone clears it, as stewards allow.
    ///
    /// A blue flag shows to a car when one at least a lap up is within BlueBehindM behind it on
    /// the road, and comes down once that car is past, or has dropped BlueHysteresisM further
    /// back: without that margin a car following at about 200 m had the flag up and down every
    /// few steps.
    /// </summary>
    public sealed class RaceFlags
    {
        public const float ZoneBeforeM = 250f;
        public const float ZoneAfterM = 30f;
        public const float ClearSeconds = 2f;
        public const float BlueBehindM = 200f;
        public const float BlueHysteresisM = 50f;

        public struct Zone
        {
            public int Car;          // the car in trouble
            public int From, To;     // samples, wrapping
            public float Until;      // race time it comes down; +inf while the car is in trouble
        }

        public readonly List<Zone> Yellow = new List<Zone>();

        /// <summary>For each car, the car about to lap it, or -1.</summary>
        public int[] BlueFor;

        /// <summary>For each car, the yellow zone it is in, as the index of the car in trouble,
        /// or -1.</summary>
        public int[] InYellow;

        readonly int _cars;
        readonly float[] _progressAtEntry;   // [car * cars + other] when car entered its zone
        bool[] _wasIn;

        public RaceFlags(int cars)
        {
            _cars = cars;
            BlueFor = new int[cars];
            InYellow = new int[cars];
            _progressAtEntry = new float[cars * cars];
            _wasIn = new bool[cars];
            for (int i = 0; i < cars; i++) { BlueFor[i] = -1; InYellow[i] = -1; }
        }

        /// <summary>
        /// One look at the field. <paramref name="index"/> is each car's sample on the lap,
        /// <paramref name="progressM"/> how far it has gone in the race, <paramref name="trouble"/>
        /// whether it has stopped or spun, and <paramref name="racing"/> whether it is still in the
        /// race at all (not finished, retired or disqualified). Returns the passes made under a
        /// yellow that were not given back, as (passer, passed).
        /// </summary>
        public List<(int Car, int Passed)> Update(TrackData track, int[] index, float[] progressM,
                                                  bool[] trouble, bool[] racing, float time)
        {
            int n = track.Count;
            int before = (int)MathF.Round(ZoneBeforeM / track.SampleSpacingM);
            int after = (int)MathF.Round(ZoneAfterM / track.SampleSpacingM);

            // Zones: one per car in trouble, kept ClearSeconds after it is going again.
            for (int car = 0; car < _cars; car++)
            {
                int z = Yellow.FindIndex(y => y.Car == car);
                bool down = trouble[car] && racing[car];
                if (down)
                {
                    var zone = new Zone { Car = car, From = track.Wrap(index[car] - before),
                                          To = track.Wrap(index[car] + after), Until = float.PositiveInfinity };
                    if (z >= 0) Yellow[z] = zone; else Yellow.Add(zone);
                }
                else if (z >= 0 && float.IsPositiveInfinity(Yellow[z].Until))
                {
                    Zone zone = Yellow[z];
                    zone.Until = time + ClearSeconds;
                    Yellow[z] = zone;
                }
            }
            Yellow.RemoveAll(y => time >= y.Until || !racing[y.Car]);

            // Who is in a zone, and who passed under one.
            var passes = new List<(int, int)>();
            for (int car = 0; car < _cars; car++)
            {
                InYellow[car] = -1;
                if (!racing[car]) { _wasIn[car] = false; continue; }
                foreach (Zone zone in Yellow)
                {
                    if (zone.Car == car || !Inside(track, zone, index[car])) continue;
                    InYellow[car] = zone.Car;
                    break;
                }
                bool inside = InYellow[car] >= 0;
                if (inside && !_wasIn[car])
                    for (int other = 0; other < _cars; other++)
                        _progressAtEntry[car * _cars + other] = progressM[other];
                if (!inside && _wasIn[car])
                {
                    for (int other = 0; other < _cars; other++)
                    {
                        if (other == car || !racing[other] || trouble[other]) continue;
                        if (Yellow.Exists(y => y.Car == other)) continue;   // passing the car in trouble is allowed
                        bool aheadThen = _progressAtEntry[car * _cars + other] > _progressAtEntry[car * _cars + car];
                        bool behindNow = progressM[other] < progressM[car];
                        if (aheadThen && behindNow) passes.Add((car, other));
                    }
                }
                _wasIn[car] = inside;
            }

            // Blue flags: a car at least a lap up, close behind on the road.
            float length = track.LengthM;
            for (int car = 0; car < _cars; car++)
            {
                int shown = BlueFor[car];
                BlueFor[car] = -1;
                if (!racing[car]) continue;
                float closest = float.MaxValue;
                for (int other = 0; other < _cars; other++)
                {
                    if (other == car || !racing[other]) continue;
                    float lead = progressM[other] - progressM[car];
                    if (lead < length - BlueBehindM) continue;
                    float behind = BehindOnRoad(track, progressM[car], progressM[other]);
                    float reach = other == shown ? BlueBehindM + BlueHysteresisM : BlueBehindM;
                    // The closest car lapping this one, always: keeping the first one shown made
                    // the car give way to it while another was right on its bumper, held up 50 s.
                    if (behind > reach || behind >= closest) continue;
                    closest = behind;
                    BlueFor[car] = other;
                }
            }
            return passes;
        }

        /// <summary>How far <paramref name="lapping"/> is behind <paramref name="car"/> on the road,
        /// in metres, given how far each has gone in the race.</summary>
        public static float BehindOnRoad(TrackData track, float carProgressM, float lappingProgressM)
            => track.LengthM - (lappingProgressM - carProgressM) % track.LengthM;

        /// <summary>Whether a sample lies inside a zone, which may wrap past the start line.</summary>
        public static bool Inside(TrackData track, Zone zone, int sample)
        {
            int n = track.Count;
            int span = ((zone.To - zone.From) % n + n) % n;
            int into = ((sample - zone.From) % n + n) % n;
            return into <= span;
        }
    }
}
