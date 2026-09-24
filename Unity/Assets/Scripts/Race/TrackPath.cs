using System;
using UnityEngine;
using CarRace.Track;
using Vec3 = System.Numerics.Vector3;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The circuit's centreline, baked into the scene by TrackSceneBuilder, with the start
    /// line at sample 0 and the lap running in index order. What LapTimer measures progress
    /// along. Positions are in world space, as the road was built from them.
    /// </summary>
    public sealed class TrackPath : MonoBehaviour
    {
        public string trackName = "";
        public Vector3[] centre = new Vector3[0];

        /// <summary>The racing line and the road's width either side of the centreline, indexed
        /// with <see cref="centre"/>. What the AI drives, as it does in the harness.</summary>
        public Vector3[] line = new Vector3[0];
        public float[] widthLeft = new float[0];
        public float[] widthRight = new float[0];
        public float sampleSpacing = 2f;
        public float lengthM;

        [Tooltip("The headless reference driver's lap on this circuit, for comparison. Measured on " +
                 "flat ground, where this scene has the real elevation, so it is a guide, not a par.")]
        public float referenceLapSeconds;

        /// <summary>
        /// The circuit as the AI wants it, built the way the harness's TrackLoader builds it:
        /// the racing line's offset from the centreline, and its curvature recomputed from the
        /// geometry at a stride of about 6 m. Unity and System.Numerics share axes, so the
        /// points copy across unchanged.
        /// </summary>
        public TrackData ToTrackData()
        {
            var track = new TrackData
            {
                Name = trackName,
                LengthM = lengthM,
                SampleSpacingM = sampleSpacing,
                Centre = Array.ConvertAll(centre, p => new Vec3(p.x, p.y, p.z)),
                Line = Array.ConvertAll(line, p => new Vec3(p.x, p.y, p.z)),
                WidthLeft = widthLeft,
                WidthRight = widthRight,
            };
            track.LineFromCentreM = new float[track.Count];
            for (int i = 0; i < track.Count; i++)
                track.LineFromCentreM[i] = track.LateralOffset(track.Centre, i, track.Line[i]);
            int stride = Mathf.Max(1, Mathf.RoundToInt(6f / sampleSpacing));
            track.LineCurvature = TrackData.SignedCurvature(track.Line, stride);
            return track;
        }

        /// <summary>Nearest sample to <paramref name="position"/>, searched a short way back and a
        /// longer way forward from <paramref name="from"/>. A whole-lap search would jump to the
        /// other side of a hairpin, where the road passes within a few metres of itself.</summary>
        public int Nearest(Vector3 position, int from, int back = 5, int ahead = 40)
        {
            int n = centre.Length;
            int best = from;
            float bestDistance = float.MaxValue;
            for (int k = -back; k <= ahead; k++)
            {
                int i = ((from + k) % n + n) % n;
                float dx = centre[i].x - position.x, dz = centre[i].z - position.z;
                float d = dx * dx + dz * dz;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }
    }
}
