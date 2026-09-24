using System.Collections.Generic;
using UnityEngine;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The racing line as a braking guide, the way racing games show it: bars along the line
    /// ahead of the player, each coloured by how hard the player would have to brake, from
    /// where they are and at the speed they are doing, to be at that bar's ideal speed when
    /// they reach it. Green is safe, yellow means slowing down soon, red means brake now or
    /// too fast for this point. As the player brakes towards a corner's speed its bars turn
    /// from red through yellow to green.
    ///
    ///   needed = (v^2 - ideal^2) / (2 * distance), as a share of the car's planned braking
    ///   green below GreenBelow, red from RedFrom, yellow between
    ///
    /// The ideal speed is the reference plan, SpeedPlan at IdealPace for the player's own car,
    /// the plan the "AI ref" lap time comes from. Bars are BarSamples long with a gap as long,
    /// out to AheadMetres, in three unlit colours so shade does not change what they say. The
    /// geometry for the whole lap is made once; each frame only the three colours' triangle
    /// lists are refilled for the bars in view.
    /// </summary>
    public sealed class RacingLineGuide : MonoBehaviour
    {
        [SerializeField] TrackPath track;
        [SerializeField] CarController player;

        const float IdealPace = 0.85f;
        const float GreenBelow = 0.3f;
        const float RedFrom = 0.8f;
        const float AheadMetres = 350f;
        const float BehindMetres = 10f;
        const float WidthM = 0.5f;
        const float LiftM = 0.03f;
        const int BarSamples = 1;          // a bar every other sample: 2 m on, 2 m off

        float[] _ideal;
        float _brakingMs2;
        Mesh _mesh;
        Rigidbody _body;
        int _index;
        Vector3 _last;
        readonly List<int>[] _triangles = { new List<int>(), new List<int>(), new List<int>() };

        void Start()
        {
            if (track == null || player == null || player.Sim == null || track.line.Length < 3) { enabled = false; return; }

            TrackData data = track.ToTrackData();
            CarConfig config = player.Sim.Config;
            SpeedPlan.Limits limits = RaceDirector.PlanningLimits(config);
            limits.LateralMs2 *= IdealPace;
            limits.BrakingMs2 *= IdealPace;
            limits.TractionMs2 *= IdealPace;
            _ideal = SpeedPlan.Build(data, limits);
            _brakingMs2 = limits.BrakingMs2;

            _mesh = BuildBars(track.line);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _body = player.GetComponent<Rigidbody>();
            _last = player.transform.position;
            _index = track.Nearest(_last, 0, back: 0, ahead: track.line.Length - 1);
        }

        /// <summary>Every bar of the lap, four vertices each, lying on the racing line.</summary>
        static Mesh BuildBars(Vector3[] line)
        {
            int n = line.Length;
            var vertices = new List<Vector3>(n * 2);
            for (int i = 0; i < n; i += 2 * BarSamples)
            {
                for (int k = 0; k <= BarSamples; k++)
                {
                    int s = (i + k) % n;
                    Vector3 along = line[(s + 1) % n] - line[(s - 1 + n) % n];
                    along.y = 0f;
                    Vector3 right = new Vector3(along.z, 0f, -along.x).normalized * (WidthM * 0.5f);
                    vertices.Add(line[s] - right + Vector3.up * LiftM);
                    vertices.Add(line[s] + right + Vector3.up * LiftM);
                }
            }
            var mesh = new Mesh { name = "Racing Line Guide", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, subMeshCount = 3 };
            mesh.SetVertices(vertices);
            mesh.RecalculateBounds();
            mesh.MarkDynamic();
            return mesh;
        }

        void LateUpdate()
        {
            if (_mesh == null) return;
            int n = track.line.Length;
            Vector3 position = player.transform.position;
            _index = (position - _last).sqrMagnitude > 25f * 25f
                ? track.Nearest(position, 0, back: 0, ahead: n - 1)
                : track.Nearest(position, _index);
            _last = position;

            float v = Mathf.Max(Vector3.Dot(_body.linearVelocity, player.transform.forward), 0f);
            float spacing = Mathf.Max(track.sampleSpacing, 0.1f);
            int ahead = Mathf.CeilToInt(AheadMetres / spacing), behind = Mathf.CeilToInt(BehindMetres / spacing);
            int bars = (n + 2 * BarSamples - 1) / (2 * BarSamples);
            int verticesPerBar = 2 * (BarSamples + 1);
            foreach (var list in _triangles) list.Clear();

            int step = 2 * BarSamples;
            int first = Mathf.FloorToInt((float)(_index - behind) / step);
            int last = Mathf.FloorToInt((float)(_index + ahead) / step);
            for (int b = first; b <= last; b++)
            {
                int bar = ((b % bars) + bars) % bars;
                int sample = (bar * step + BarSamples) % n;          // the bar's far end
                float distance = Mathf.Max((b * step + BarSamples - _index) * spacing, 1f);
                float ideal = _ideal[sample];
                float needed = (v * v - ideal * ideal) / (2f * distance) / Mathf.Max(_brakingMs2, 0.1f);
                int colour = needed < GreenBelow ? 0 : needed < RedFrom ? 1 : 2;

                int o = bar * verticesPerBar;
                if (o + verticesPerBar > _mesh.vertexCount) continue;   // a short last bar on some laps
                var list = _triangles[colour];
                for (int k = 0; k < BarSamples; k++)
                {
                    int a = o + 2 * k;
                    list.Add(a); list.Add(a + 2); list.Add(a + 1);
                    list.Add(a + 1); list.Add(a + 2); list.Add(a + 3);
                }
            }
            for (int c = 0; c < 3; c++) _mesh.SetTriangles(_triangles[c], c, calculateBounds: false);
        }
    }
}
