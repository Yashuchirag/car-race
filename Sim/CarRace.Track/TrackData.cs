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

        public int Count => Line != null ? Line.Length : 0;

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
