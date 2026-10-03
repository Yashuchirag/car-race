using System;
using System.Collections.Generic;
using System.Numerics;
using CarRace.Vehicle;

namespace CarRace.Track
{
    /// <summary>
    /// Judges one car against the track limits, as F1 does: the car has left the track when
    /// no wheel is still on it, so kerbs, which lie outside the road edge, count as off.
    ///
    /// The test is geometric, each grounded wheel's contact point against the road edges,
    /// rather than the grip under the wheels. That is what lets one judge serve the harness,
    /// whose ground is flat and grips everywhere, and Unity, whose verges are grass.
    ///
    /// An excursion runs from the step every grounded wheel is past the edge to the step two
    /// wheels have been back on for ReturnSeconds, and is judged when it ends:
    ///
    ///   an incident, not an offence, if the car spun (pointing more than SpinDegrees away from
    ///   where it was going) or nearly stopped (under CrawlMs) on the way, since nobody gains
    ///   from that. Against its own travel, not the road's direction: a car cutting straight
    ///   across a hairpin's infield points far off the road without spinning at all, and the
    ///   first version of this called every such cut an incident;
    ///
    ///   a cut if the car drove less than CutShare of the shortest legal path between where it
    ///   left and where it came back, and at least CutMinimumM less: it went round less of the
    ///   track than anyone staying on it could have;
    ///
    ///   a track limits offence, a car that ran wide, if it was at a corner: the road turns
    ///   CornerDegrees or more from CornerLookBackM before it left to where it came back, which
    ///   takes in running wide at a corner's exit;
    ///
    ///   otherwise nothing: off on a straight, where leaving the road gains nothing, the grass
    ///   only slows the car. Kind.Straight, for the logs.
    ///
    /// A recovery while off (R) ends the excursion as Kind.Recovery, which race control
    /// penalises: the car was carried back to the road, so nobody can tell what it gained.
    /// The AI being put back on its line cancels the excursion instead (Cancel).
    /// </summary>
    public sealed class TrackLimits
    {
        public enum Kind { None, Incident, TrackLimits, Cut, Straight, Recovery }

        /// <summary>How far past the edge a wheel's contact point must be to count as off: about
        /// half a tyre's width, so a tyre still touching the line does not.</summary>
        public const float WheelMarginM = 0.15f;
        public const float ReturnSeconds = 0.2f;
        public const float SpinDegrees = 60f;
        public const float CrawlMs = 15f / 3.6f;
        public const float CutShare = 0.97f;
        public const float CutMinimumM = 3f;
        public const float CornerDegrees = 15f;
        public const float CornerLookBackM = 60f;

        /// <summary>The last excursion judged, for the HUD and the logs.</summary>
        public Kind LastKind { get; private set; }
        public float LastDrivenM { get; private set; }
        public float LastLegalM { get; private set; }
        public int LastFromIndex { get; private set; }
        public int LastToIndex { get; private set; }

        /// <summary>True from the step the car left the track until the excursion is judged.</summary>
        public bool Off => _off;

        bool _off;
        float _backFor;
        Vector3 _from, _last;
        int _fromIndex;
        float _driven, _slowest, _worstSlip;

        public void Cancel()
        {
            _off = false;
            _backFor = 0f;
        }

        /// <summary>
        /// The car has been recovered (R). Off the track, that ends the excursion as
        /// Kind.Recovery; on it, nothing has happened and it returns None.
        /// </summary>
        public Kind Recovered(int index)
        {
            if (!_off) return Kind.None;
            _off = false;
            _backFor = 0f;
            LastFromIndex = _fromIndex;
            LastToIndex = index;
            LastDrivenM = _driven;
            LastLegalM = 0f;
            LastKind = Kind.Recovery;
            return LastKind;
        }

        /// <summary>
        /// One physics step. <paramref name="index"/> is the car's sample on the lap, which the
        /// caller already tracks. Returns what the excursion that ended this step was judged to
        /// be, or None.
        /// </summary>
        public Kind Step(TrackData track, int index, Vector3 position, Vector3 forward, Vector3 velocity,
                         Wheel[] wheels, float dt)
        {
            int grounded = 0, onTrack = 0;
            foreach (Wheel w in wheels)
            {
                if (!w.Grounded) continue;
                grounded++;
                if (!Beyond(track, index, w.ContactPoint)) onTrack++;
            }
            if (grounded == 0) return Kind.None;   // in the air: nothing has changed
            float speedMs = new Vector3(velocity.X, 0f, velocity.Z).Length();

            Vector3 flat = new Vector3(position.X, 0f, position.Z);
            if (!_off)
            {
                if (onTrack > 0) return Kind.None;
                _off = true;
                _backFor = 0f;
                _from = _last = flat;
                _fromIndex = index;
                _driven = 0f;
                _slowest = speedMs;
                _worstSlip = 0f;
                return Kind.None;
            }

            _driven += (flat - _last).Length();
            _last = flat;
            _slowest = MathF.Min(_slowest, speedMs);
            _worstSlip = MathF.Max(_worstSlip, Sideslip(forward, velocity));

            _backFor = onTrack >= 2 ? _backFor + dt : 0f;
            if (_backFor < ReturnSeconds) return Kind.None;

            _off = false;
            LastFromIndex = _fromIndex;
            LastToIndex = index;
            LastDrivenM = _driven;
            LastLegalM = ShortestLegalPath(track, _fromIndex, _from, index, flat);

            if (_worstSlip > SpinDegrees || _slowest < CrawlMs) LastKind = Kind.Incident;
            else if (_driven < CutShare * LastLegalM && LastLegalM - _driven >= CutMinimumM) LastKind = Kind.Cut;
            else if (AtCorner(track, _fromIndex, index)) LastKind = Kind.TrackLimits;
            else LastKind = Kind.Straight;
            return LastKind;
        }

        /// <summary>
        /// Whether the road turns CornerDegrees or more, in total, from CornerLookBackM before
        /// <paramref name="fromIndex"/> to <paramref name="toIndex"/>. Total turning, left and
        /// right added, so a chicane counts; measured every 3 m or so, over the centreline's
        /// sample noise.
        /// </summary>
        public static bool AtCorner(TrackData track, int fromIndex, int toIndex)
        {
            int n = track.Count;
            int span = ((toIndex - fromIndex) % n + n) % n;
            if (span > n / 2) span = 0;
            int back = (int)MathF.Round(CornerLookBackM / track.SampleSpacingM);
            int stride = Math.Max(1, (int)MathF.Round(3f / track.SampleSpacingM));
            float turned = 0f;
            Vector3 previous = track.Tangent(track.Centre, track.Wrap(fromIndex - back));
            for (int k = -back + stride; k <= span; k += stride)
            {
                Vector3 tangent = track.Tangent(track.Centre, track.Wrap(fromIndex + k));
                float cos = MathF.Max(-1f, MathF.Min(1f, Vector3.Dot(Vector3.Normalize(previous), Vector3.Normalize(tangent))));
                turned += MathF.Acos(cos) * 180f / MathF.PI;
                previous = tangent;
            }
            return turned >= CornerDegrees;
        }

        /// <summary>Whether a point is more than WheelMarginM past either road edge at a sample.</summary>
        public static bool Beyond(TrackData track, int index, Vector3 point)
        {
            float lateral = track.LateralOffset(track.Centre, index, point);
            float right = track.WidthRight[index];
            // Through the pit stretch the pit lane is track as well, out to its far side: every
            // stop would otherwise be four wheels off, and void the lap it was made on.
            if (track.InPitStretch(index))
                right = MathF.Max(right, track.PitOffsetM[index] + TrackData.PitFastLaneM * 0.5f + TrackData.PitWorkingLaneM);
            return lateral > right + WheelMarginM
                || -lateral > track.WidthLeft[index] + WheelMarginM;
        }

        /// <summary>Angle between where the car points and where it is going, degrees, flat.</summary>
        static float Sideslip(Vector3 forward, Vector3 velocity)
        {
            Vector3 f = new Vector3(forward.X, 0f, forward.Z), v = new Vector3(velocity.X, 0f, velocity.Z);
            if (f.Length() < 1e-4f || v.Length() < 1f) return 0f;
            float cos = Vector3.Dot(Vector3.Normalize(f), Vector3.Normalize(v));
            return MathF.Acos(MathF.Max(-1f, MathF.Min(1f, cos))) * 180f / MathF.PI;
        }

        /// <summary>
        /// The shortest path that stays on the road from <paramref name="from"/> (near sample
        /// <paramref name="fromIndex"/>) to <paramref name="to"/>, in metres, flat. Both ends are
        /// first brought onto the road at their samples, since the car left from beyond the edge.
        ///
        /// The funnel algorithm ("string pulling"): the road between the two samples is a
        /// corridor of portals, each the road's width at one sample, and the path is the string
        /// pulled tight through them, bending only round an edge on the inside. It is exact for
        /// that corridor, and costs one pass over it.
        /// </summary>
        public static float ShortestLegalPath(TrackData track, int fromIndex, Vector3 from, int toIndex, Vector3 to)
        {
            int n = track.Count;
            int span = ((toIndex - fromIndex) % n + n) % n;
            if (span > n / 2) span = 0;   // came back behind where it left: nothing driven forward

            var left = new List<Vector3>(span + 2);
            var right = new List<Vector3>(span + 2);
            Vector3 start = OnRoad(track, fromIndex, from), end = OnRoad(track, toIndex, to);
            left.Add(start); right.Add(start);
            for (int k = 1; k < span; k++)
            {
                int i = track.Wrap(fromIndex + k);
                Vector3 r = TrackData.Right(track.Tangent(track.Centre, i));
                Vector3 c = new Vector3(track.Centre[i].X, 0f, track.Centre[i].Z);
                left.Add(c - r * track.WidthLeft[i]);
                right.Add(c + r * track.WidthRight[i]);
            }
            left.Add(end); right.Add(end);

            var path = Funnel(left, right);
            float length = 0f;
            for (int k = 1; k < path.Count; k++) length += (path[k] - path[k - 1]).Length();
            return length;
        }

        static Vector3 OnRoad(TrackData track, int index, Vector3 point)
        {
            Vector3 c = new Vector3(track.Centre[index].X, 0f, track.Centre[index].Z);
            Vector3 tangent = track.Tangent(track.Centre, index);
            Vector3 r = TrackData.Right(tangent);
            Vector3 p = new Vector3(point.X, 0f, point.Z);
            float across = MathF.Max(-track.WidthLeft[index], MathF.Min(track.WidthRight[index], Vector3.Dot(p - c, r)));
            return c + tangent * Vector3.Dot(p - c, tangent) + r * across;
        }

        /// <summary>Positive when <paramref name="b"/> lies to the left of the line from the
        /// origin along <paramref name="a"/>, seen from above (X right, Z forward).</summary>
        static float Cross(Vector3 a, Vector3 b) => a.X * b.Z - a.Z * b.X;

        /// <summary>
        /// The simple stupid funnel algorithm (Mononen), on portals given as their left and right
        /// ends, the first and last both the path's own ends. The funnel's two sides tighten
        /// portal by portal; when one side would cross the other, the corner it crosses is on the
        /// path, becomes the new apex, and the walk restarts from it.
        /// </summary>
        static List<Vector3> Funnel(List<Vector3> left, List<Vector3> right)
        {
            var path = new List<Vector3> { left[0] };
            Vector3 apex = left[0], portalLeft = left[0], portalRight = right[0];
            int apexIndex = 0, leftIndex = 0, rightIndex = 0;

            for (int i = 1; i < left.Count; i++)
            {
                Vector3 l = left[i], r = right[i];

                // The right side: tighten if the new right end is not further right.
                if (Cross(portalRight - apex, r - apex) >= 0f)
                {
                    if (Near(apex, portalRight) || Cross(portalLeft - apex, r - apex) < 0f)
                    {
                        portalRight = r;
                        rightIndex = i;
                    }
                    else
                    {
                        // Right crossed over left: the left corner is on the path.
                        path.Add(portalLeft);
                        apex = portalLeft;
                        apexIndex = leftIndex;
                        portalLeft = portalRight = apex;
                        leftIndex = rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                // The left side, the mirror of it.
                if (Cross(portalLeft - apex, l - apex) <= 0f)
                {
                    if (Near(apex, portalLeft) || Cross(portalRight - apex, l - apex) > 0f)
                    {
                        portalLeft = l;
                        leftIndex = i;
                    }
                    else
                    {
                        path.Add(portalRight);
                        apex = portalRight;
                        apexIndex = rightIndex;
                        portalLeft = portalRight = apex;
                        leftIndex = rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            Vector3 end = left[left.Count - 1];
            if (!Near(path[path.Count - 1], end)) path.Add(end);
            return path;
        }

        static bool Near(Vector3 a, Vector3 b) => (a - b).LengthSquared() < 1e-6f;
    }
}
