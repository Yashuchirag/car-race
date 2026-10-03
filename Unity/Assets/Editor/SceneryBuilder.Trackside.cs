using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// What stands along the barriers, from Blender (Tools/blender/scenery.py): a double steel
    /// guardrail on posts the length of both barriers, tyre walls faced with red and white
    /// belts on the outside of the tight corners, catch fencing where people watch (the start
    /// and the corners with stands), and marshal posts round the lap.
    ///
    /// The barriers themselves are the colliders TrackSceneBuilder makes, now unseen: the
    /// rails and tyres stand on their inner face, where a car meets them, so what the driver
    /// sees is what stops the car. Rails and posts are merged into a mesh per stretch, so a
    /// lap of them is a few dozen draw calls, each culled on its own.
    /// </summary>
    public static partial class SceneryBuilder
    {
        const float PostEveryM = 2f;
        const float TyreModuleM = 1.2f;
        const float TyreSpanM = 25f;        // either side of a tight corner's apex
        const float FenceEveryM = 4f;
        const float FenceFromM = 1.2f, FenceTopM = 3.3f;   // then leaning in to FenceLeanM
        const float MarshalEveryM = 450f;
        const int StretchSamples = 120;     // even, so every other sample lines up across stretches
        const int RailEverySamples = 2;

        /// <summary>A guardrail's two W-section beams, as (depth behind the barrier face, height)
        /// round each beam's section, front first.</summary>
        static readonly Vector2[] Beam =
        {
            new Vector2(0.06f, 0f), new Vector2(0f, 0.035f), new Vector2(0f, 0.12f), new Vector2(0.05f, 0.155f),
            new Vector2(0f, 0.19f), new Vector2(0f, 0.275f), new Vector2(0.06f, 0.31f), new Vector2(0.08f, 0.31f),
            new Vector2(0.08f, 0f),
        };
        static readonly float[] BeamHeights = { 0.42f, 0.78f };

        static int Trackside(GameObject parent, Land land, Circuit circuit, List<int> standCorners)
        {
            var root = new GameObject("Trackside");
            root.transform.SetParent(parent.transform, false);
            Mesh post = StructureModels.BlenderModel("armco_post").GetComponent<MeshFilter>().sharedMesh;
            Material[] postMaterials = StructureModels.BlenderModel("armco_post").GetComponent<MeshRenderer>().sharedMaterials;
            GameObject[] tyres = { StructureModels.BlenderModel("tyre_red"), StructureModels.BlenderModel("tyre_white") };
            GameObject fencePost = StructureModels.BlenderModel("fence_post");
            Material steel = StructureModels.Named("Steel"), chainlink = StructureModels.Chainlink();
            List<int> tight = Corners(circuit, 110f, 300f);

            int placed = 0;
            foreach (int side in new[] { -1, +1 })
            {
                // Where this side has tyres instead of rails, and where a fence behind them.
                var tyre = new bool[circuit.N];
                var fence = new bool[circuit.N];
                void Mark(bool[] marks, int k, float halfM)
                {
                    int reach = Mathf.RoundToInt(halfM / circuit.Spacing);
                    for (int d = -reach; d <= reach; d++) marks[circuit.Wrap(k + d)] = true;
                }
                foreach (int k in tight) if (-circuit.Turn(k) == side) Mark(tyre, k, TyreSpanM);
                foreach (int k in standCorners) if (-circuit.Turn(k) == side) Mark(fence, k, 50f);
                Mark(fence, 0, 120f);

                // On the right, nothing in front of the garages, where the barrier is left out, and
                // no catch fence along the pit stretch, where it stands on the pit wall instead.
                // The guardrail skips the gap as it skips tyre walls.
                var rails = (bool[])tyre.Clone();
                if (side > 0 && GarageGap.From >= 0)
                {
                    for (int k = GarageGap.From; ; k = circuit.Wrap(k + 1))
                    {
                        rails[k] = true;
                        tyre[k] = false;
                        if (k == GarageGap.To) break;
                    }
                    for (int k = PitStretch.From; ; k = circuit.Wrap(k + 1))
                    {
                        fence[k] = false;
                        if (k == PitStretch.To) break;
                    }
                }

                var group = new GameObject(side > 0 ? "Right" : "Left");
                group.transform.SetParent(root.transform, false);
                for (int start = 0; start < circuit.N; start += StretchSamples)
                {
                    int end = Mathf.Min(start + StretchSamples, circuit.N);
                    Stretch(group, circuit, side, start, end, rails, post, postMaterials, steel);
                    placed += TyreWall(group, circuit, side, start, end, tyre, tyres);
                    placed += Fence(group, circuit, side, start, end, fence, fencePost, chainlink);
                }
            }

            // Marshal posts, alternate sides, behind the barrier where there is room.
            GameObject marshal = StructureModels.BlenderModel("marshal_post");
            int every = Mathf.RoundToInt(MarshalEveryM / circuit.Spacing);
            for (int k = every / 2, n = 0; k < circuit.N; k += every, n++)
            {
                int side = n % 2 == 0 ? -circuit.Turn(k) : circuit.Turn(k);
                for (int shift = 0; shift < 60; shift += 5)
                {
                    int at = circuit.Wrap(k + Mathf.RoundToInt(shift / circuit.Spacing));
                    if (Place(root, land, marshal, circuit.Outside(at, side, StructureClearM + 1.2f), -circuit.Right[at] * side, 3f) != null)
                    {
                        placed++;
                        break;
                    }
                }
            }
            return placed;
        }

        /// <summary>Each theme's two landmarks and its horizon, if it has one (see scenery.py):
        /// Royal Park a villa like Monza's Villa Reale and a length of old banked oval under the
        /// Alps; the Ardennes a stone viaduct and a chalet before forested ridges; the desert a
        /// tower like Sakhir's and a sandstone fort before dunes; the coast a Ferris wheel and
        /// a lighthouse, the sea its horizon; the city a wartime hangar and an airfield control
        /// tower, as Silverstone was an airfield; the night city two neon towers.</summary>
        static readonly Dictionary<string, (string first, string second, string horizon)> ThemeLandmarks =
            new Dictionary<string, (string, string, string)>
            {
                ["Countryside"] = ("villa", "banking", "alps"),
                ["Mountains"] = ("viaduct", "chalet", "ridges"),   // the chalet is too small to show over a stand
                ["Desert"] = ("sakhir_tower", "fort", "dunes"),
                ["Coast"] = ("ferris_wheel", "lighthouse", null),
                ["City"] = ("hangar", "control_tower", null),
                ["Night City"] = ("neon_tower_pink", "neon_tower_cyan", null),
            };

        /// <summary>
        /// The theme's landmarks, each at the nearest open ground round the lap where it fits
        /// whole, facing the track: the first searched from the start, where it stands over
        /// the grandstand, the second from the far side of the lap, where nothing else is. Then
        /// its horizon ahead of the start straight.
        /// </summary>
        static int Landmarks(GameObject parent, Land land, Circuit circuit, Theme theme)
        {
            if (!ThemeLandmarks.TryGetValue(theme.Name, out var set)) return 0;
            int placed = 0;
            if (Landmark(parent, land, circuit, StructureModels.BlenderModel(set.first), 0)) placed++;
            if (Landmark(parent, land, circuit, StructureModels.BlenderModel(set.second), circuit.N / 2)) placed++;
            if (set.horizon != null) Horizon(parent, circuit, StructureModels.BlenderModel(set.horizon));
            return placed;
        }

        /// <summary>The model at its own size, facing the track, at the nearest open ground:
        /// tried every 40 m round the lap from sample `from`, on both sides, from 40 m beyond
        /// the barrier out.</summary>
        static bool Landmark(GameObject parent, Land land, Circuit circuit, GameObject model, int from)
        {
            Bounds local = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            int every = Mathf.Max(1, Mathf.RoundToInt(40f / circuit.Spacing));
            foreach (float beyond in new[] { 40f, 70f, 110f, 160f, 220f })
                for (int step = 0; step < circuit.N; step += every)
                    foreach (int side in new[] { -1, +1 })
                    {
                        int k = circuit.Wrap(from + step);
                        Vector3 at = circuit.Outside(k, side, StructureClearM + beyond + local.size.z * 0.5f);
                        Vector3 facing = -circuit.Right[k] * side;
                        // Place checks a footprint's corners, enough for a garage; one this size
                        // could straddle the track between them, so check across it too.
                        Quaternion turn = Quaternion.LookRotation(-facing, Vector3.up);
                        bool clear = true;
                        for (float x = -local.size.x * 0.5f; x <= local.size.x * 0.5f && clear; x += 10f)
                            for (float z = -local.size.z * 0.5f; z <= local.size.z * 0.5f && clear; z += 10f)
                            {
                                Vector3 p = at + turn * new Vector3(x, 0f, z);
                                clear = land.Beyond(p) >= StructureClearM && !land.Taken(p);
                            }
                        if (clear && Place(parent, land, model, at, facing, local.size.x) != null)
                        {
                            // Keep the ground between it and the track open, a lawn rather than
                            // woods, so it can be seen from the track.
                            Vector3 across = circuit.Tangent(k) * local.size.x * 0.5f;
                            Vector3 near = circuit.Outside(k, side, StructureClearM), far = at + facing * local.size.z * 0.5f;
                            land.Take(new[] { near - across, near + across, far + across, far - across }, 0f);
                            return true;
                        }
                    }
            Debug.LogWarning($"No open ground for {model.name} round the circuit.");
            return false;
        }

        /// <summary>A horizon model (built round its origin at a 1 km radius, its middle along
        /// -z) centred on the circuit, scaled to stand 1.6 km beyond its farthest point, which
        /// keeps it inside the fog's reach from most of the lap, and turned to lie ahead of the
        /// start straight.</summary>
        static void Horizon(GameObject parent, Circuit circuit, GameObject model)
        {
            Vector3 middle = Vector3.zero;
            foreach (Vector3 p in circuit.Centre) middle += p;
            middle /= circuit.N;
            float reach = 0f, low = float.MaxValue;
            foreach (Vector3 p in circuit.Centre)
            {
                reach = Mathf.Max(reach, new Vector2(p.x - middle.x, p.z - middle.z).magnitude);
                low = Mathf.Min(low, p.y);
            }
            Vector3 ahead = circuit.Tangent(0);
            var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model, parent.transform);
            go.transform.SetPositionAndRotation(new Vector3(middle.x, low, middle.z), Quaternion.LookRotation(-ahead));
            go.transform.localScale = Vector3.one * ((reach + 1600f) / 1000f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>The barrier's inner face, where cars meet it, at sample k.</summary>
        static Vector3 Face(Circuit circuit, int k, int side) => circuit.Outside(k, side, -BarrierThicknessM);

        /// <summary>Guardrail and posts over samples start to end, skipping tyre walls: one
        /// mesh, steel beams in the first submesh and the posts' own after it.</summary>
        static void Stretch(GameObject parent, Circuit circuit, int side, int start, int end, bool[] tyre,
                            Mesh post, Material[] postMaterials, Material steel)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var beams = new List<int>();
            float along = 0f;
            // Every other sample: 4 m straight lengths still follow the tightest corner, and a
            // lap of beams at every sample was 200,000 triangles.
            for (int k = start; k < end; k += RailEverySamples)
            {
                int next = circuit.Wrap(k + RailEverySamples);
                if (tyre[k] || tyre[next] || tyre[circuit.Wrap(k + 1)]) continue;
                foreach (float lift in BeamHeights)
                {
                    int a = vertices.Count;
                    foreach (int s in new[] { k, next })
                    {
                        Vector3 face = Face(circuit, s, side), back = circuit.Right[s] * side;
                        float u = (along + (s == k ? 0f : Vector3.Distance(Face(circuit, k, side), Face(circuit, next, side)))) / 4f;
                        foreach (Vector2 p in Beam)
                        {
                            vertices.Add(face + back * p.x + Vector3.up * (lift + p.y));
                            uvs.Add(new Vector2(u, (lift + p.y) / 1.2f));
                        }
                    }
                    int m = Beam.Length;
                    for (int j = 0; j < m; j++)
                    {
                        int j1 = (j + 1) % m;
                        beams.AddRange(new[] { a + j, a + m + j, a + j1, a + j1, a + m + j, a + m + j1 });
                    }
                }
                along += Vector3.Distance(Face(circuit, k, side), Face(circuit, next, side));
            }
            if (beams.Count == 0) return;
            // The beams' second face (Beam[1] to Beam[2]) is their front: it must face the track.
            // Which way the winding above turns depends on the side and the lap's direction, so
            // check it and flip every triangle if it faces away, as TrackSceneBuilder.Wall does.
            Vector3 frontNormal = Vector3.Cross(vertices[beams[7]] - vertices[beams[6]], vertices[beams[8]] - vertices[beams[6]]);
            if (Vector3.Dot(frontNormal, -circuit.Right[start] * side) < 0f)
                for (int t = 0; t < beams.Count; t += 3) (beams[t + 1], beams[t + 2]) = (beams[t + 2], beams[t + 1]);

            // Posts every PostEveryM behind the beams, facing the track.
            var instances = new List<CombineInstance>();
            float carried = 0f;
            for (int k = start; k < end; k++)
            {
                int next = circuit.Wrap(k + 1);
                carried += Vector3.Distance(Face(circuit, k, side), Face(circuit, next, side));
                if (tyre[k] || carried < PostEveryM) continue;
                carried = 0f;
                Vector3 back = circuit.Right[k] * side;
                Vector3 at = Face(circuit, k, side) + back * 0.16f;
                for (int sub = 0; sub < post.subMeshCount; sub++)
                    instances.Add(new CombineInstance { mesh = post, subMeshIndex = sub, transform = Matrix4x4.TRS(at, Quaternion.LookRotation(back), Vector3.one) });
            }

            var mesh = new Mesh { name = $"Guardrail {side} {start}", indexFormat = IndexFormat.UInt32 };
            var postMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            var materials = new List<Material> { steel };
            if (instances.Count > 0)
            {
                // One submesh per post material, in the order the post's materials come.
                var perMaterial = new List<CombineInstance>[post.subMeshCount];
                for (int s = 0; s < post.subMeshCount; s++) perMaterial[s] = new List<CombineInstance>();
                foreach (var ci in instances) perMaterial[ci.subMeshIndex].Add(ci);
                var parts = new CombineInstance[post.subMeshCount];
                for (int s = 0; s < post.subMeshCount; s++)
                {
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(perMaterial[s].ToArray(), true, true);
                    parts[s] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
                }
                postMesh.CombineMeshes(parts, false, false);
                materials.AddRange(postMaterials);
            }
            var combined = new List<Vector3>(vertices);
            var combinedUv = new List<Vector2>(uvs);
            int offset = combined.Count;
            combined.AddRange(postMesh.vertices);
            var postUv = postMesh.uv;
            combinedUv.AddRange(postUv.Length == postMesh.vertexCount ? postUv : new Vector2[postMesh.vertexCount]);
            mesh.SetVertices(combined);
            mesh.SetUVs(0, combinedUv);
            mesh.subMeshCount = 1 + postMesh.subMeshCount;
            mesh.SetTriangles(beams, 0);
            for (int s = 0; s < postMesh.subMeshCount; s++)
            {
                int[] tris = postMesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t++) tris[t] += offset;
                mesh.SetTriangles(tris, 1 + s);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            // Thin rails and posts: their shadows barely show on the grass, and a lap of them
            // was drawn into every shadow cascade.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// Many placed copies of prefabs as one object: a mesh with a submesh per material,
        /// whichever prefab each part came from. Hundreds of tyre modules and fence posts as
        /// objects of their own were 1,600 draw calls a frame at Royal Park, more than all the
        /// rest of the scene, and the main thread's time goes on draw calls.
        /// </summary>
        static void Merged(GameObject parent, string name, List<(GameObject prefab, Matrix4x4 at)> items, ShadowCastingMode shadows)
        {
            if (items.Count == 0) return;
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            foreach (var (prefab, at) in items)
            {
                Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                Material[] materials = prefab.GetComponent<MeshRenderer>().sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (!byMaterial.TryGetValue(materials[s], out var list))
                    {
                        byMaterial[materials[s]] = list = new List<CombineInstance>();
                        order.Add(materials[s]);
                    }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = at });
                }
            }
            var parts = new CombineInstance[order.Count];
            for (int m = 0; m < order.Count; m++)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(byMaterial[order[m]].ToArray(), true, true);
                parts[m] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
            }
            var merged = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            merged.CombineMeshes(parts, false, false);
            merged.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = merged;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = order.ToArray();
            renderer.shadowCastingMode = shadows;
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Tyre modules, alternating red and white, end to end along the marked samples,
        /// their belts on the barrier's face: laid by distance along the barrier, since a sample
        /// is longer than a module.</summary>
        static int TyreWall(GameObject parent, Circuit circuit, int side, int start, int end, bool[] tyre, GameObject[] modules)
        {
            int placed = 0;
            var items = new List<(GameObject, Matrix4x4)>();
            float next = 0f;        // distance along this run of tyres to the next module's middle
            for (int k = start; k < end; k++)
            {
                int k1 = circuit.Wrap(k + 1);
                if (!tyre[k] || !tyre[k1]) { next = TyreModuleM * 0.5f; continue; }
                Vector3 a = Face(circuit, k, side), b = Face(circuit, k1, side);
                float length = Vector3.Distance(a, b);
                for (; next < length; next += TyreModuleM)
                {
                    float t = next / length;
                    Vector3 back = Vector3.Lerp(circuit.Right[k], circuit.Right[k1], t).normalized * side;
                    items.Add((modules[placed % 2], Matrix4x4.TRS(Vector3.Lerp(a, b, t), Quaternion.LookRotation(back), Vector3.one)));
                    placed++;
                }
                next -= length;
            }
            Merged(parent, $"Tyres {side} {start}", items, ShadowCastingMode.Off);   // hundreds, and low
            return placed;
        }

        /// <summary>Catch fence along the marked samples: posts FenceEveryM apart just behind
        /// the barrier, and a chain-link sheet between them, its top leaning over the track.</summary>
        static int Fence(GameObject parent, Circuit circuit, int side, int start, int end, bool[] fence, GameObject post, Material chainlink)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int posts = 0;
            var items = new List<(GameObject, Matrix4x4)>();
            float carried = FenceEveryM, along = 0f;
            for (int k = start; k < end; k++)
            {
                int next = circuit.Wrap(k + 1);
                Vector3 back = circuit.Right[k] * side;
                Vector3 foot = Face(circuit, k, side) + back * 0.55f;
                float step = Vector3.Distance(Face(circuit, k, side), Face(circuit, next, side));
                if (fence[k])
                {
                    carried += step;
                    if (carried >= FenceEveryM)
                    {
                        carried = 0f;
                        items.Add((post, Matrix4x4.TRS(foot, Quaternion.LookRotation(back), Vector3.one)));
                        posts++;
                    }
                }
                if (fence[k] && fence[next])
                {
                    Vector3 footNext = Face(circuit, next, side) + circuit.Right[next] * side * 0.55f;
                    Vector3 lean = -back * 0.55f, leanNext = -circuit.Right[next] * side * 0.55f;
                    int a = vertices.Count;
                    vertices.Add(foot + Vector3.up * FenceFromM);
                    vertices.Add(foot + Vector3.up * FenceTopM);
                    vertices.Add(foot + Vector3.up * 4f + lean);
                    vertices.Add(footNext + Vector3.up * FenceFromM);
                    vertices.Add(footNext + Vector3.up * FenceTopM);
                    vertices.Add(footNext + Vector3.up * 4f + leanNext);
                    float u0 = along / 0.5f, u1 = (along + step) / 0.5f;
                    uvs.AddRange(new[] { new Vector2(u0, 0f), new Vector2(u0, 4.2f), new Vector2(u0, 5.9f),
                                         new Vector2(u1, 0f), new Vector2(u1, 4.2f), new Vector2(u1, 5.9f) });
                    triangles.AddRange(new[] { a, a + 1, a + 3, a + 1, a + 4, a + 3, a + 1, a + 2, a + 4, a + 2, a + 5, a + 4 });
                }
                along += step;
            }
            Merged(parent, $"Fence Posts {side} {start}", items, ShadowCastingMode.Off);
            if (triangles.Count > 0)
            {
                var mesh = new Mesh { name = $"Fence {side} {start}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = chainlink;
                renderer.shadowCastingMode = ShadowCastingMode.Off;   // a wire mesh casts next to nothing
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic);
            }
            return posts;
        }
    }
}
