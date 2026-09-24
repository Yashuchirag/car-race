using UnityEngine;

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

        [Tooltip("The headless reference driver's lap on this circuit, for comparison. Measured on " +
                 "flat ground, where this scene has the real elevation, so it is a guide, not a par.")]
        public float referenceLapSeconds;

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
