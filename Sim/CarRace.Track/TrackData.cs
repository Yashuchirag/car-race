using System;
using System.Numerics;

namespace CarRace.Track
{
    /// <summary>
    /// One circuit, as the simulation wants it: parallel arrays of samples at a fixed
    /// spacing around the lap, in world coordinates with Y up.
    ///
    /// The pipeline writes ENU, where Z is up. Whoever loads the file does that swap,
    /// so nothing downstream has to remember which convention it is holding. The ring
    /// is open: the last sample is one spacing short of the first, and index arithmetic
    /// wraps, so <see cref="Wrap"/> is the only correct way to step an index.
    /// </summary>
    public sealed class TrackData
    {
        public string Name = "unnamed";
        public float LengthM;
        public float SampleSpacingM = 2f;

        /// <summary>The pipeline's own lap estimate, for comparison. Not a target.</summary>
        public float EstimatedLapTimeS;

        public Vector3[] Centre;
        public Vector3[] Line;
        public float[] LineCurvature;       // 1/m, signed
        public float[] WidthLeft;           // m from the centreline to the left edge
        public float[] WidthRight;

        /// <summary>
        /// Where the racing line sits across the road, positive to the right of the
        /// centreline. Needed by anything that wants to move off the line: the line is
        /// already most of the way to the edge through a corner, so an offset that ignores
        /// this puts a car in the gravel while the number it was given looked modest.
        /// </summary>
        public float[] LineFromCentreM;

        /// <summary>Road left to the right of the racing line, less what a car needs.</summary>
        public float RoomRight(int index, float halfWidthM)
            => WidthRight[index] - LineFromCentreM[index] - halfWidthM;

        public float RoomLeft(int index, float halfWidthM)
            => WidthLeft[index] + LineFromCentreM[index] - halfWidthM;

        public int Count => Line != null ? Line.Length : 0;

        /// <summary>
        /// Two passing lanes, fixed on the road rather than hung off the racing line: [0] left
        /// of the centreline, [1] right, each LaneHalfM[i] from it. Two cars side by side take
        /// one each, and since the lanes run parallel to the road they never close on each other
        /// the way two offsets from a racing line do when the line sweeps across the road into a
        /// corner and carries the inside car into the outside one. Every pair off Desert Park's
        /// grid touched in its first hairpin that way.
        ///
        /// A lane is LaneHalfWantedM out, less where the road is too narrow for that and a car,
        /// taken as the narrowest the road gets over LaneSmoothM either way so a lane never
        /// jinks for a local pinch, then smoothed. Built on first use by EnsureLanes.
        /// </summary>
        public float[] LaneHalfM;
        public Vector3[][] LanePoints;
        public float[][] LaneCurvature;
        public const float LaneHalfWantedM = 2.4f;
        const float LaneSmoothM = 40f;

        public void EnsureLanes(float carHalfWidthM)
        {
            if (LaneHalfM != null) return;
            int n = Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++)
                raw[i] = MathF.Max(MathF.Min(LaneHalfWantedM, MathF.Min(WidthLeft[i], WidthRight[i]) - carHalfWidthM - 0.25f), 0f);

            int reach = Math.Max(1, (int)MathF.Round(LaneSmoothM / SampleSpacingM));
            var narrowest = new float[n];
            for (int i = 0; i < n; i++)
            {
                float m = raw[i];
                for (int k = -reach; k <= reach; k++) m = MathF.Min(m, raw[Wrap(i + k)]);
                narrowest[i] = m;
            }
            LaneHalfM = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int k = -reach; k <= reach; k++) sum += narrowest[Wrap(i + k)];
                LaneHalfM[i] = sum / (2 * reach + 1);
            }

            int stride = Math.Max(1, (int)MathF.Round(6f / SampleSpacingM));
            LanePoints = new Vector3[2][];
            LaneCurvature = new float[2][];
            for (int lane = 0; lane < 2; lane++)
            {
                float side = lane == 0 ? -1f : 1f;
                var points = new Vector3[n];
                for (int i = 0; i < n; i++)
                    points[i] = Centre[i] + Right(Tangent(Centre, i)) * (side * LaneHalfM[i]);
                LanePoints[lane] = points;
                LaneCurvature[lane] = SignedCurvature(points, stride);
            }
        }

        // ---- the pit lane --------------------------------------------------------

        /// <summary>
        /// The pit lane, on the right of the start straight, built from the centreline so the
        /// harness, the AI and the scene builder share one definition (EnsurePitLane). To the
        /// AI it is a third lane: LanePoints[PitLaneIndex] is the right passing lane everywhere
        /// outside the pit, and inside it runs out to the pit lane and back, so a car already in
        /// the right lane can take it with no step.
        ///
        /// Along the lap, in order: PitFrom, where it leaves the right lane; PitEntryLine, the
        /// end of the entry ramp, where the limit starts and the pit wall begins; the boxes;
        /// PitExitLine, where the limit ends; PitTo, where it is back in the right lane.
        /// </summary>
        public const int PitLaneIndex = 2;
        public const float PitHalfSpanWantedM = 250f;
        public const float PitRampM = 60f;
        public const float PitWallGapM = 1.5f;     // road edge to the fast lane's near side
        public const float PitFastLaneM = 6f;
        public const float PitWorkingLaneM = 5f;
        public const float PitBoxSpacingM = 16f;
        public const int PitBoxes = 8;
        public const float PitLimitMs = 60f / 3.6f;

        public int PitFrom = -1, PitEntryLine, PitExitLine, PitTo;
        public int[] PitBoxIndex;
        public int SafetyCarBox = -1;
        /// <summary>The fast lane's centre from the centreline at each sample in the pit, and how
        /// far further out a box is than that.</summary>
        public float[] PitOffsetM;
        public float PitBoxShiftM;
        public bool HasPitLane => PitFrom >= 0;

        /// <summary>Samples from PitFrom to <paramref name="index"/> along the lap; more than
        /// PitSpan when it is not in the pit stretch.</summary>
        public int IntoPit(int index) => ((index - PitFrom) % Count + Count) % Count;
        public int PitSpan => IntoPit(PitTo);
        public bool InPitStretch(int index) => HasPitLane && IntoPit(index) <= PitSpan;
        public bool InPitLimit(int index) => HasPitLane && IntoPit(index) >= IntoPit(PitEntryLine) && IntoPit(index) <= IntoPit(PitExitLine);

        /// <summary>
        /// Builds the pit lane after EnsureLanes. Its half span is PitHalfSpanWantedM, or less
        /// where the start straight (the stretch round sample 0 straighter than 500 m radius) is
        /// shorter, 10 m short of where it bends: the test circuit's straight runs 192 m either
        /// side of the line, the shortest of the six. The fast lane's centre sits PitWallGapM plus
        /// half its width past the right edge, ramping out from the right lane over PitRampM, and
        /// the boxes are PitBoxSpacingM apart in the working lane beyond it, centred on the line,
        /// in front of the garages.
        /// </summary>
        public void EnsurePitLane()
        {
            if (HasPitLane || LanePoints == null) return;
            int n = Count;
            int stride = Math.Max(1, (int)MathF.Round(6f / SampleSpacingM));
            float[] centreCurvature = SignedCurvature(Centre, stride);
            int forward = 0, back = 0;
            while (forward < n / 2 && MathF.Abs(centreCurvature[Wrap(forward)]) < 1f / 500f) forward++;
            while (back < n / 2 && MathF.Abs(centreCurvature[Wrap(-back)]) < 1f / 500f) back++;
            float half = MathF.Min(PitHalfSpanWantedM, MathF.Min(forward, back) * SampleSpacingM - 10f);
            int halfSamples = (int)MathF.Round(half / SampleSpacingM);
            int ramp = (int)MathF.Round(PitRampM / SampleSpacingM);

            PitFrom = Wrap(-halfSamples);
            PitTo = Wrap(halfSamples);
            PitEntryLine = Wrap(-halfSamples + ramp);
            PitExitLine = Wrap(halfSamples - ramp);

            PitOffsetM = new float[n];
            var points = (Vector3[])LanePoints[1].Clone();
            for (int k = -halfSamples; k <= halfSamples; k++)
            {
                int i = Wrap(k);
                float fast = WidthRight[i] + PitWallGapM + PitFastLaneM * 0.5f;
                int fromEnd = Math.Min(k + halfSamples, halfSamples - k);
                float t = Math.Min(1f, fromEnd / (float)ramp);
                t = t * t * (3f - 2f * t);   // smoothstep: no kink where it leaves the lane
                float offset = LaneHalfM[i] + (fast - LaneHalfM[i]) * t;
                PitOffsetM[i] = offset;
                points[i] = Centre[i] + Right(Tangent(Centre, i)) * offset;
            }
            PitBoxShiftM = (PitFastLaneM + PitWorkingLaneM) * 0.5f;

            PitBoxIndex = new int[PitBoxes];
            for (int b = 0; b < PitBoxes; b++)
                PitBoxIndex[b] = Wrap((int)MathF.Round(((b - (PitBoxes - 1) * 0.5f) * PitBoxSpacingM) / SampleSpacingM));

            // The safety car's, one box past the last, nearer the exit than any car's.
            SafetyCarBox = Wrap((int)MathF.Round(((PitBoxes + 1) * 0.5f * PitBoxSpacingM) / SampleSpacingM));

            LanePoints = new[] { LanePoints[0], LanePoints[1], points };
            LaneCurvature = new[] { LaneCurvature[0], LaneCurvature[1], SignedCurvature(points, stride) };
        }

        /// <summary>
        /// How sharply the road curves up or down along the racing line, 1/m, negative over a
        /// crest; smoothed over CrestSpanM so a single sample's height noise is not a crest.
        /// Over a crest at speed v the tyres carry 1 + v^2 k / g of the car's weight, and grip
        /// goes with it: the speed plan needs that, or it asks for full grip where there is
        /// far less. Built on first use.
        /// </summary>
        public float[] VerticalCurvature
        {
            get
            {
                if (_vertical != null) return _vertical;
                int n = Count, k = Math.Max(1, (int)MathF.Round(CrestSpanM / SampleSpacingM));
                float span = k * SampleSpacingM;
                var raw = new float[n];
                for (int i = 0; i < n; i++)
                    raw[i] = (Line[Wrap(i + k)].Y - 2f * Line[i].Y + Line[Wrap(i - k)].Y) / (span * span);
                _vertical = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int j = -k; j <= k; j++) sum += raw[Wrap(i + j)];
                    _vertical[i] = sum / (2 * k + 1);
                }
                return _vertical;
            }
        }
        float[] _vertical;
        const float CrestSpanM = 10f;

        /// <summary>Where a car at sample <paramref name="index"/>, <paramref name="fromLineM"/>
        /// off the racing line, is across the road: positive to the right of the centreline.</summary>
        public float FromCentre(int index, float fromLineM) => LineFromCentreM[index] + fromLineM;

        public int Wrap(int index)
        {
            int n = Count;
            if (n == 0) return 0;
            index %= n;
            return index < 0 ? index + n : index;
        }

        /// <summary>
        /// Direction of travel at a sample, from its neighbours, flattened. Taking the
        /// difference to the next sample alone leans half a sample into the corner,
        /// which is enough to bias a lateral offset measurement at 2 m spacing.
        /// </summary>
        public Vector3 Tangent(Vector3[] path, int index)
        {
            Vector3 delta = path[Wrap(index + 1)] - path[Wrap(index - 1)];
            delta.Y = 0f;
            float length = delta.Length();
            return length > 1e-6f ? delta / length : Vector3.UnitZ;
        }

        /// <summary>
        /// Signed curvature at every sample, computed from the geometry rather than read
        /// from the file: positive means the road turns right, in the same sense as a
        /// positive steering input. The pipeline writes a curvature too, in ENU, where the
        /// sign means the opposite thing. Recomputing it here costs nothing and removes a
        /// convention that has to be got right by reasoning alone.
        ///
        /// Menger curvature, the circle through three samples, because the samples are
        /// evenly spaced along the arc and not along X.
        ///
        /// The three samples are a stride apart, not adjacent. At 2 m spacing, adjacent
        /// samples measure the wiggle in the sampling as much as the bend in the road:
        /// peak curvature came out 30 to 50% high on three of the six circuits, which put
        /// corners in the plan that are not there and cost Bahrain nearly a minute a lap.
        /// A stride spanning about 6 m of road reproduces the pipeline's own curvature to
        /// four figures, correlation 1.0000 on every circuit tested.
        /// </summary>
        public static float[] SignedCurvature(Vector3[] path, int stride)
        {
            int n = path.Length;
            var curvature = new float[n];
            if (stride < 1) stride = 1;

            for (int i = 0; i < n; i++)
            {
                Vector3 a = path[((i - stride) % n + n) % n];
                Vector3 b = path[i];
                Vector3 c = path[(i + stride) % n];
                a.Y = b.Y = c.Y = 0f;

                Vector3 d1 = b - a, d2 = c - b, span = c - a;
                float lengths = d1.Length() * d2.Length() * span.Length();
                if (lengths < 1e-9f) continue;

                // Positive when d2 turns toward the right of d1, which is the direction a
                // positive steering input takes the car.
                float cross = d1.Z * d2.X - d1.X * d2.Z;
                curvature[i] = 2f * cross / lengths;
            }

            return curvature;
        }

        /// <summary>Signed distance from the path at <paramref name="index"/>, positive to its right.</summary>
        public float LateralOffset(Vector3[] path, int index, Vector3 position)
        {
            Vector3 forward = Tangent(path, index);
            Vector3 delta = position - path[index];
            delta.Y = 0f;
            return Vector3.Dot(delta, Right(forward));
        }

        /// <summary>The direction 90 degrees to the right of a flattened forward vector.</summary>
        public static Vector3 Right(Vector3 forward) => new Vector3(forward.Z, 0f, -forward.X);
    }
}
