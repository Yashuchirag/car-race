using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Trackside scenery in the circuit's theme (TrackSceneBuilder.Themes), from the Kenney
    /// kits (KenneyModels). Countryside so far, for Royal Park; the other themes follow once
    /// this one is agreed.
    ///
    /// Everything is placed by its distance from the circuit. Land is a grid over the ground,
    /// CellM across, holding each cell's distance beyond the barriers (a chamfer distance
    /// transform from the road, verges and barriers) and which cells a structure already
    /// uses. Nothing goes nearer a barrier than StructureClearM, so no car can reach it and
    /// nothing needs a collider.
    ///
    /// Structures go first, so the trees keep out of their way: pit garages and a covered
    /// grandstand either side of the start line, open grandstands with tents behind them on
    /// the outside of the tightest corners, banner towers where the braking for a corner
    /// starts, and billboards along the straights, angled towards the cars coming. They are
    /// GameObjects, static for batching.
    ///
    /// Trees and bushes are the terrain's trees, drawn instanced and kept in its data, not a
    /// GameObject each, all as meshes. Woods cover ForestShare of the land, where two octaves of noise are
    /// highest, with bushes on their edges, and single trees stand in the fields between.
    /// From ThinFromM out to ReachM the woods thin to nothing. Past NearM the woods use only
    /// the simplest trees (under 135 triangles against up to 400), since they are a few pixels
    /// tall there and triangles, not pixels, were what the trees cost.
    /// </summary>
    public static class SceneryBuilder
    {
        const float CellM = 2.5f;
        const float ReachM = 700f;
        const float ThinFromM = 350f;
        const float NearM = 250f;
        const float BarrierThicknessM = 0.4f;
        const float StructureClearM = 3f;
        const float TreeClearM = 6f;
        const float TreeSpacingM = 9f;
        const float ForestShare = 0.3f;

        public static void Build(GameObject root, string theme, Vector3[] centre, Vector3[] right,
                                 float[] widthLeft, float[] widthRight, float vergeWidthM, Terrain terrain)
        {
            if (theme != "Countryside") return;
            KenneyModels.Ensure();

            var circuit = new Circuit(centre, right, widthLeft, widthRight, vergeWidthM + BarrierThicknessM);
            var land = new Land(circuit, terrain);
            var parent = new GameObject("Scenery");
            parent.transform.SetParent(root.transform, false);
            var rng = new Random(1);

            var structures = new GameObject("Structures");
            structures.transform.SetParent(parent.transform, false);
            int placed = Countryside(structures, land, circuit);
            int trees = Trees(land, circuit, terrain, rng);
            Debug.Log($"Scenery, {theme}: {placed} structures, {trees} trees and bushes.");
        }

        static int Countryside(GameObject parent, Land land, Circuit circuit)
        {
            GameObject garage = KenneyModels.Load("Racing", "pitsGarage");
            GameObject covered = KenneyModels.Load("Racing", "grandStandCovered");
            GameObject stand = KenneyModels.Load("Racing", "grandStand");
            GameObject tent = KenneyModels.Load("Racing", "tent");
            GameObject billboard = KenneyModels.Load("Racing", "billboard");
            GameObject[] towers = { KenneyModels.Load("Racing", "bannerTowerRed"), KenneyModels.Load("Racing", "bannerTowerGreen") };

            // The start: garages on the right, a covered stand on the left.
            int placed = Row(parent, land, circuit, garage, 0, +1, -100f, 100f, 9f, StructureClearM);
            placed += Row(parent, land, circuit, covered, 0, -1, -110f, 110f, 12f, StructureClearM + 1f);

            // The tightest corners, a stand on the outside of each and tents behind it.
            List<int> corners = Corners(circuit, 110f, 300f);
            corners.Sort((a, b) => circuit.Radius(a).CompareTo(circuit.Radius(b)));
            var chosen = new List<int>();
            foreach (int k in corners)
            {
                if (chosen.Count == 4) break;
                if (circuit.ArcM(k, 0) < 250f || chosen.Exists(c => circuit.ArcM(c, k) < 300f)) continue;
                chosen.Add(k);
                int outside = -circuit.Turn(k);
                placed += Row(parent, land, circuit, stand, k, outside, -25f, 25f, 10f, StructureClearM + 2f);
                float behind = StructureClearM + 2f + Scaled(stand, 10f).z + 6f;
                placed += Row(parent, land, circuit, tent, k, outside, -12f, 12f, 8f, behind);
            }

            // A banner tower on the outside where the braking starts for every corner.
            List<int> braking = Corners(circuit, 150f, 150f);
            for (int i = 0; i < braking.Count; i++)
            {
                int k = circuit.Wrap(braking[i] - Mathf.RoundToInt(80f / circuit.Spacing));
                int outside = -circuit.Turn(braking[i]);
                placed += Row(parent, land, circuit, towers[i % 2], k, outside, 0f, 0f, 3f, StructureClearM);
            }

            // Billboards on the straights, alternate sides, angled to face the cars coming.
            int every = Mathf.RoundToInt(300f / circuit.Spacing);
            int side = +1;
            for (int k = every / 2; k < circuit.N; k += every)
            {
                if (circuit.Radius(k) < 300f || circuit.ArcM(k, 0) < 150f) continue;
                Vector3 toTrack = -circuit.Right[k] * side;
                Vector3 facing = (toTrack * Mathf.Cos(25f * Mathf.Deg2Rad) - circuit.Tangent(k) * Mathf.Sin(25f * Mathf.Deg2Rad)).normalized;
                float depth = Scaled(billboard, 10f).z;
                Vector3 at = circuit.Outside(k, side, StructureClearM + depth * 0.5f + 1f);
                if (Place(parent, land, billboard, at, facing, 10f) != null) placed++;
                side = -side;
            }
            return placed;
        }

        /// <summary>A row of <paramref name="model"/>, widthM each, along the side of the
        /// circuit from fromM to toM of track either side of sample k, its front clearM
        /// beyond the barrier and facing the track. Pieces that do not fit are left out.</summary>
        static int Row(GameObject parent, Land land, Circuit circuit, GameObject model, int k, int side,
                       float fromM, float toM, float widthM, float clearM)
        {
            float depth = Scaled(model, widthM).z;
            int first = Mathf.RoundToInt(fromM / circuit.Spacing), last = Mathf.RoundToInt(toM / circuit.Spacing);
            int placed = 0;
            Vector3? previous = null;
            for (int i = first; i <= last; i++)
            {
                int s = circuit.Wrap(k + i);
                Vector3 at = circuit.Outside(s, side, clearM + depth * 0.5f);
                if (previous.HasValue && Vector3.Distance(previous.Value, at) < widthM) continue;
                previous = at;
                if (Place(parent, land, model, at, -circuit.Right[s] * side, widthM) != null) placed++;
            }
            return placed;
        }

        /// <summary>The model's size when scaled to widthM across.</summary>
        static Vector3 Scaled(GameObject model, float widthM)
        {
            Bounds b = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            return b.size * (widthM / b.size.x);
        }

        /// <summary>The model, widthM across, centred on <paramref name="at"/>, its front (the
        /// kits' -Z) facing along <paramref name="facing"/>, standing on the lowest ground under
        /// it; or nothing if any of its footprint is too near a barrier or already taken.</summary>
        static GameObject Place(GameObject parent, Land land, GameObject model, Vector3 at, Vector3 facing, float widthM)
        {
            Bounds local = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            float scale = widthM / local.size.x;
            Quaternion rotation = Quaternion.LookRotation(-facing, Vector3.up);
            Vector3 across = rotation * Vector3.right * (local.size.x * scale * 0.5f);
            Vector3 along = rotation * Vector3.forward * (local.size.z * scale * 0.5f);
            Vector3[] corners = { at - across - along, at + across - along, at + across + along, at - across + along };

            float ground = float.MaxValue;
            foreach (Vector3 c in corners)
            {
                if (land.Beyond(c) < StructureClearM - 0.5f || land.Taken(c)) return null;
                ground = Mathf.Min(ground, land.Height(c));
            }
            if (land.Taken(at)) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent.transform);
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation = rotation;
            Vector3 offset = rotation * (local.center * scale);
            go.transform.position = new Vector3(at.x - offset.x, ground - local.min.y * scale, at.z - offset.z);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            land.Take(corners, 2f);
            return go;
        }

        /// <summary>Corner apexes: samples tighter than maxRadiusM and tightest within
        /// separationM either way.</summary>
        static List<int> Corners(Circuit circuit, float maxRadiusM, float separationM)
        {
            var apexes = new List<int>();
            int reach = Mathf.Max(1, Mathf.RoundToInt(separationM * 0.5f / circuit.Spacing));
            for (int k = 0; k < circuit.N; k++)
            {
                float r = circuit.Radius(k);
                if (r >= maxRadiusM) continue;
                bool tightest = true;
                for (int d = -reach; d <= reach && tightest; d++)
                {
                    float other = circuit.Radius(circuit.Wrap(k + d));
                    if (other < r || (other == r && d < 0)) tightest = false;
                }
                if (tightest) apexes.Add(k);
            }
            return apexes;
        }

        // ---------------------------------------------------------------- trees

        static readonly string[] Forest =
        {
            "tree_default", "tree_default_dark", "tree_detailed", "tree_detailed_dark",
            "tree_oak", "tree_oak_dark", "tree_cone", "tree_cone_dark", "tree_pineRoundA",
        };
        static readonly string[] FarForest = { "tree_default", "tree_default_dark", "tree_fat", "tree_cone", "tree_pineTallA" };
        static readonly string[] Lone = { "tree_oak", "tree_detailed", "tree_fat", "tree_default" };
        static readonly string[] Bushes = { "plant_bush", "plant_bushLarge", "plant_bushDetailed" };

        static int Trees(Land land, Circuit circuit, Terrain terrain, Random rng)
        {
            var names = new List<string>();
            foreach (var set in new[] { Forest, FarForest, Lone, Bushes })
                foreach (string name in set)
                    if (!names.Contains(name)) names.Add(name);
            var models = names.ConvertAll(name => KenneyModels.Load("Nature", name));
            var heights = models.ConvertAll(m => m.GetComponent<MeshFilter>().sharedMesh.bounds.size.y);

            // Candidates on a jittered grid, with their noise, so the woods' threshold can be
            // set to cover ForestShare of the land whatever the noise's spread.
            float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;
            var candidates = new List<(Vector3 p, float beyond, float noise)>();
            for (float x = land.MinX; x < land.MaxX; x += TreeSpacingM)
            for (float z = land.MinZ; z < land.MaxZ; z += TreeSpacingM)
            {
                var p = new Vector3(x + ((float)rng.NextDouble() - 0.5f) * TreeSpacingM * 0.8f, 0f,
                                    z + ((float)rng.NextDouble() - 0.5f) * TreeSpacingM * 0.8f);
                float beyond = land.Beyond(p);
                if (beyond < TreeClearM || beyond > ReachM || land.Taken(p)) continue;
                float noise = 0.7f * Mathf.PerlinNoise(p.x / 230f + ox, p.z / 230f + oz)
                            + 0.3f * Mathf.PerlinNoise(p.x / 60f + oz, p.z / 60f + ox);
                noise -= 0.2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinFromM, ReachM, beyond));
                candidates.Add((p, beyond, noise));
            }
            var sorted = candidates.ConvertAll(c => c.noise);
            sorted.Sort();
            float woods = sorted.Count > 0 ? sorted[(int)((1f - ForestShare) * (sorted.Count - 1))] : 1f;

            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            var instances = new List<TreeInstance>();
            void Add(string[] set, float minM, float maxM, Vector3 p)
            {
                int index = names.IndexOf(set[rng.Next(set.Length)]);
                float height = Mathf.Lerp(minM, maxM, (float)rng.NextDouble());
                float scale = height / heights[index];
                instances.Add(new TreeInstance
                {
                    prototypeIndex = index,
                    position = new Vector3((p.x - origin.x) / size.x, 0f, (p.z - origin.z) / size.z),
                    heightScale = scale,
                    widthScale = scale * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()),
                    rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            foreach (var (p, beyond, noise) in candidates)
            {
                double roll = rng.NextDouble();
                if (noise >= woods) { if (roll < 0.85) Add(beyond < NearM ? Forest : FarForest, 10f, 17f, p); }
                else if (noise >= woods - 0.03f) { if (roll < 0.35) Add(Bushes, 1.2f, 2.2f, p); }
                else if (roll < 0.03) Add(Lone, 8f, 14f, p);
            }

            TerrainData data = terrain.terrainData;
            data.treePrototypes = models.ConvertAll(m => new TreePrototype { prefab = m }).ToArray();
            data.SetTreeInstances(instances.ToArray(), snapToHeightmap: true);
            // Meshes all the way: Unity's defaults (50 mesh trees, billboards past 50 m) need
            // its old Nature shaders to make billboards, and with URP's they drew black slivers.
            terrain.treeDistance = 2000f;
            terrain.treeBillboardDistance = 2000f;
            terrain.treeMaximumFullLODCount = int.MaxValue;
            // The terrain draws them one call each; in play they are drawn instanced instead.
            if (terrain.GetComponent<InstancedTrees>() == null) terrain.gameObject.AddComponent<InstancedTrees>();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return instances.Count;
        }

        // ---------------------------------------------------------------- the circuit and the land

        sealed class Circuit
        {
            public readonly Vector3[] Centre, Right;
            readonly float[] _left, _right;
            readonly float _outside;
            readonly float[] _radius;
            public readonly int N;
            public readonly float Spacing;

            public Circuit(Vector3[] centre, Vector3[] right, float[] widthLeft, float[] widthRight, float outside)
            {
                Centre = centre; Right = right; _left = widthLeft; _right = widthRight; _outside = outside;
                N = centre.Length;
                float length = 0f;
                for (int i = 0; i < N; i++) length += Vector3.Distance(centre[i], centre[(i + 1) % N]);
                Spacing = length / N;
                _radius = new float[N];
                int d = Mathf.Max(1, Mathf.RoundToInt(10f / Spacing));
                for (int k = 0; k < N; k++)
                    _radius[k] = Circumradius(centre[Wrap(k - d)], centre[k], centre[Wrap(k + d)]);
            }

            public int Wrap(int k) => ((k % N) + N) % N;
            public float Radius(int k) => _radius[k];

            public Vector3 Tangent(int k)
            {
                Vector3 t = Centre[Wrap(k + 1)] - Centre[Wrap(k - 1)];
                t.y = 0f;
                return t.normalized;
            }

            /// <summary>+1 where the circuit turns right at k, -1 where it turns left.</summary>
            public int Turn(int k)
            {
                int d = Mathf.Max(1, Mathf.RoundToInt(10f / Spacing));
                return Vector3.Cross(Tangent(k - d), Tangent(k + d)).y > 0f ? +1 : -1;
            }

            /// <summary>A point beyondM outside the barrier on the given side (+1 right).</summary>
            public Vector3 Outside(int k, int side, float beyondM) =>
                Centre[k] + Right[k] * side * ((side > 0 ? _right[k] : _left[k]) + _outside + beyondM);

            /// <summary>Distance along the lap between two samples, the shorter way round.</summary>
            public float ArcM(int a, int b)
            {
                int d = Mathf.Abs(a - b);
                return Mathf.Min(d, N - d) * Spacing;
            }

            static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
            {
                a.y = b.y = c.y = 0f;
                float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c), ca = Vector3.Distance(c, a);
                float area2 = Mathf.Abs(Vector3.Cross(b - a, c - a).y);
                return area2 < 1e-4f ? float.MaxValue : ab * bc * ca / (2f * area2);
            }
        }

        sealed class Land
        {
            readonly Terrain _terrain;
            readonly float _x0, _z0;
            readonly int _w, _h;
            readonly float[] _beyond;
            readonly bool[] _taken;
            public float MinX => _x0;
            public float MinZ => _z0;
            public float MaxX => _x0 + (_w - 1) * CellM;
            public float MaxZ => _z0 + (_h - 1) * CellM;

            public Land(Circuit circuit, Terrain terrain)
            {
                _terrain = terrain;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Vector3 p in circuit.Centre)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
                float margin = ReachM + 100f;
                _x0 = minX - margin; _z0 = minZ - margin;
                _w = Mathf.CeilToInt((maxX - minX + 2f * margin) / CellM) + 1;
                _h = Mathf.CeilToInt((maxZ - minZ + 2f * margin) / CellM) + 1;
                _beyond = new float[_w * _h];
                _taken = new bool[_w * _h];
                for (int i = 0; i < _beyond.Length; i++) _beyond[i] = float.MaxValue;

                // Road, verges and barriers are the seeds: each sample's line from one barrier
                // to the other.
                for (int k = 0; k < circuit.N; k++)
                {
                    Vector3 from = circuit.Outside(k, -1, 0f), to = circuit.Outside(k, +1, 0f);
                    int steps = Mathf.CeilToInt(Vector3.Distance(from, to) / (CellM * 0.5f));
                    for (int s = 0; s <= steps; s++)
                    {
                        int c = Cell(Vector3.Lerp(from, to, (float)s / steps));
                        if (c >= 0) _beyond[c] = 0f;
                    }
                }

                // Chamfer distance, a pass each way.
                float straight = CellM, diagonal = CellM * Mathf.Sqrt(2f);
                for (int j = 0; j < _h; j++)
                for (int i = 0; i < _w; i++)
                {
                    float d = _beyond[j * _w + i];
                    if (i > 0) d = Mathf.Min(d, _beyond[j * _w + i - 1] + straight);
                    if (j > 0)
                    {
                        d = Mathf.Min(d, _beyond[(j - 1) * _w + i] + straight);
                        if (i > 0) d = Mathf.Min(d, _beyond[(j - 1) * _w + i - 1] + diagonal);
                        if (i < _w - 1) d = Mathf.Min(d, _beyond[(j - 1) * _w + i + 1] + diagonal);
                    }
                    _beyond[j * _w + i] = d;
                }
                for (int j = _h - 1; j >= 0; j--)
                for (int i = _w - 1; i >= 0; i--)
                {
                    float d = _beyond[j * _w + i];
                    if (i < _w - 1) d = Mathf.Min(d, _beyond[j * _w + i + 1] + straight);
                    if (j < _h - 1)
                    {
                        d = Mathf.Min(d, _beyond[(j + 1) * _w + i] + straight);
                        if (i < _w - 1) d = Mathf.Min(d, _beyond[(j + 1) * _w + i + 1] + diagonal);
                        if (i > 0) d = Mathf.Min(d, _beyond[(j + 1) * _w + i - 1] + diagonal);
                    }
                    _beyond[j * _w + i] = d;
                }
            }

            int Cell(Vector3 p)
            {
                int i = Mathf.RoundToInt((p.x - _x0) / CellM), j = Mathf.RoundToInt((p.z - _z0) / CellM);
                return i < 0 || j < 0 || i >= _w || j >= _h ? -1 : j * _w + i;
            }

            /// <summary>Distance beyond the nearest barrier; 0 on the circuit itself.</summary>
            public float Beyond(Vector3 p)
            {
                int c = Cell(p);
                return c < 0 ? float.MaxValue : _beyond[c];
            }

            public bool Taken(Vector3 p)
            {
                int c = Cell(p);
                return c >= 0 && _taken[c];
            }

            public float Height(Vector3 p) => _terrain.SampleHeight(p) + _terrain.transform.position.y;

            /// <summary>Marks the quadrilateral, grown by marginM, as used.</summary>
            public void Take(Vector3[] corners, float marginM)
            {
                Vector3 centre = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
                Vector3 a = corners[1] - corners[0], b = corners[3] - corners[0];
                float ha = a.magnitude * 0.5f + marginM, hb = b.magnitude * 0.5f + marginM;
                a.Normalize(); b.Normalize();
                float r = Mathf.Sqrt(ha * ha + hb * hb);
                int cells = Mathf.CeilToInt(r / CellM);
                int ci = Mathf.RoundToInt((centre.x - _x0) / CellM), cj = Mathf.RoundToInt((centre.z - _z0) / CellM);
                for (int j = Mathf.Max(0, cj - cells); j <= Mathf.Min(_h - 1, cj + cells); j++)
                for (int i = Mathf.Max(0, ci - cells); i <= Mathf.Min(_w - 1, ci + cells); i++)
                {
                    var d = new Vector3(_x0 + i * CellM - centre.x, 0f, _z0 + j * CellM - centre.z);
                    if (Mathf.Abs(Vector3.Dot(d, a)) <= ha && Mathf.Abs(Vector3.Dot(d, b)) <= hb) _taken[j * _w + i] = true;
                }
            }
        }
    }
}
