using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Trees generated here rather than taken from a kit, since realistic CC0 trees come
    /// with hundreds of thousands to millions of triangles each and the circuits plant up to
    /// 30,000. Each is a trunk and branches as tapered tubes in a photographed bark, carrying
    /// cards of leaves: a whole twig to a card, from Tools/foliage_textures.py. The cards'
    /// normals point out from the middle of the crown, not along each card, so the crown
    /// shades as one rounded mass, light on the sun's side and dark underneath, as a real
    /// crown does, instead of as a heap of flat cards.
    ///
    /// Every model also has a far version (TreeLod) of a dozen or so large cards and a bare
    /// trunk, which InstancedTrees draws past its LOD distance.
    ///
    /// Species: broadleaf (beech and lime), conifer (a spruce: whorls of drooping boughs),
    /// palm and bush, several variants each, named Prefix + species + "_" + variant for the
    /// scenery's plantings. Built fresh the first time one is asked for in an editor session;
    /// meshes and prefabs are overwritten in place, so scenes keep their references.
    /// </summary>
    public static class TreeModels
    {
        public const string Prefix = "gen:";
        const string Folder = "Assets/Art/Trees";

        static readonly (string name, int variants)[] Species =
        {
            ("broadleaf", 4), ("conifer", 3), ("palm", 3), ("bush", 2),
        };

        static bool _built;

        public static GameObject Load(string name)
        {
            if (!_built) Build();
            string path = $"{Folder}/{name.Substring(Prefix.Length)}.prefab";
            return AssetDatabase.LoadAssetAtPath<GameObject>(path)
                ?? throw new FileNotFoundException($"{path} was not generated.");
        }

        /// <summary>Every variant's name, for a planting: "gen:broadleaf_0" and so on.</summary>
        public static string[] Names(string species, params int[] variants) =>
            Array.ConvertAll(variants, v => $"{Prefix}{species}_{v}");

        [MenuItem("CarRace/Rebuild Trees")]
        public static void Build()
        {
            var bark = BarkMaterial("Bark", "bark_brown_02");
            var barkLight = BarkMaterial("Bark Plane", "bark_platanus");
            var pineBark = BarkMaterial("Bark Pine", "pine_bark");
            var palmBark = BarkMaterial("Bark Palm", "palm_tree_bark");
            var beech = LeafMaterial("Leaves Beech", "broadleaf_a", new Color(0.86f, 0.9f, 0.8f));
            var lime = LeafMaterial("Leaves Lime", "broadleaf_b", new Color(0.9f, 0.95f, 0.85f));
            var needles = LeafMaterial("Needles", "conifer", new Color(0.75f, 0.82f, 0.75f));
            var fronds = LeafMaterial("Fronds", "palm", Color.white);

            for (int v = 0; v < 4; v++)
            {
                var rng = new Random(100 + v);
                bool isBeech = v % 2 == 0;
                Save($"broadleaf_{v}", Broadleaf(rng, far: false), Broadleaf(new Random(100 + v), far: true),
                     isBeech ? bark : barkLight, isBeech ? beech : lime);
            }
            for (int v = 0; v < 3; v++)
                Save($"conifer_{v}", Conifer(new Random(200 + v), far: false), Conifer(new Random(200 + v), far: true), pineBark, needles);
            for (int v = 0; v < 3; v++)
                Save($"palm_{v}", Palm(new Random(300 + v), far: false), Palm(new Random(300 + v), far: true), palmBark, fronds);
            for (int v = 0; v < 2; v++)
                Save($"bush_{v}", Bush(new Random(400 + v), far: false), Bush(new Random(400 + v), far: true), bark, v == 0 ? beech : lime);
            AssetDatabase.SaveAssets();
            _built = true;
        }

        /// <summary>For looking at the trees without building a circuit: every model, full
        /// and far versions, in a row on a lawn, written to Builds/trees.png. -executeMethod
        /// CarRace.UnityGame.EditorTools.TreeModels.Preview</summary>
        public static void Preview()
        {
            Build();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.44f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(0.5543f, 0.7416f, -0.3778f));
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.15f);
            var lawn = GameObject.CreatePrimitive(PrimitiveType.Plane);
            lawn.transform.localScale = new Vector3(40f, 1f, 10f);
            lawn.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Grass.mat");

            float x = 0f;
            foreach (var (species, variants) in Species)
                for (int v = 0; v < variants; v++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{species}_{v}.prefab");
                    Mesh near = prefab.GetComponent<MeshFilter>().sharedMesh;
                    float scale = (species == "bush" ? 2f : species == "conifer" ? 16f : 12f) / near.bounds.size.y;
                    foreach (bool far in new[] { false, true })
                    {
                        var tree = new GameObject(species);
                        tree.transform.position = new Vector3(x, 0f, far ? 30f : 0f);
                        tree.transform.localScale = Vector3.one * scale;
                        tree.AddComponent<MeshFilter>().sharedMesh = far ? prefab.GetComponent<TreeLod>().farMesh : near;
                        tree.AddComponent<MeshRenderer>().sharedMaterials = prefab.GetComponent<MeshRenderer>().sharedMaterials;
                    }
                    x += species == "bush" ? 6f : 13f;
                }

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(x * 0.5f - 6f, 4f, -48f);
            camera.transform.LookAt(new Vector3(x * 0.5f - 6f, 8f, 15f));
            camera.fieldOfView = 62f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.72f, 0.86f);
            var target = new RenderTexture(3000, 1100, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes("Builds/trees.png", image.EncodeToPNG());
            int near0 = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/broadleaf_0.prefab").GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
            Debug.Log($"Tree preview written to Builds/trees.png; broadleaf_0 has {near0} triangles.");
        }

        // ---------------------------------------------------------------- species

        /// <summary>A broadleaf 12 m tall: a trunk to the crown and a leader on up, main
        /// limbs from 35 to 70% of the height reaching to an ellipsoid crown, side branches off
        /// them, and twig cards along the outer half of every branch.</summary>
        static Parts Broadleaf(Random rng, bool far)
        {
            const float H = 12f;
            var parts = new Parts();
            float lean = Range(rng, -0.05f, 0.05f);
            var crown = new Vector3(lean * H * 0.6f, 0.58f * H, 0f);
            var radii = new Vector3(0.36f * H * Range(rng, 0.85f, 1.15f), 0.35f * H, 0.36f * H * Range(rng, 0.85f, 1.15f));
            parts.Centre = crown;
            parts.Radii = radii;

            var trunk = new List<Vector3>();
            for (int k = 0; k <= 6; k++)
            {
                float t = k / 6f;
                trunk.Add(new Vector3(lean * H * t + Range(rng, -0.1f, 0.1f) * t, t * 0.85f * H, Range(rng, -0.1f, 0.1f) * t));
            }
            parts.Tube(trunk, t => Mathf.Lerp(0.34f, 0.06f, t) * (t < 0.08f ? 1.25f : 1f), far ? 5 : 9, 1.1f);

            int limbs = far ? 4 : rng.Next(6, 9);
            float azimuth = Range(rng, 0f, 360f);
            for (int b = 0; b < limbs; b++)
            {
                azimuth += 137.5f + Range(rng, -20f, 20f);
                float startT = Range(rng, 0.27f, 0.62f) / 0.85f;
                Vector3 start = Along(trunk, startT);
                float rise = Range(rng, 15f, 55f);
                Vector3 direction = Quaternion.Euler(-rise, azimuth, 0f) * Vector3.forward;
                float length = ToSurface(start, direction, crown, radii) * 0.92f;
                var limb = Bend(start, direction, length, 0.12f * length);
                float r0 = Mathf.Lerp(0.2f, 0.08f, startT);
                if (!far || b < 3) parts.Tube(limb, t => Mathf.Lerp(r0, 0.03f, t), far ? 3 : 5, 0.6f);

                if (far) continue;
                parts.Twigs(limb, 0.45f, 0.95f, rng, Range(rng, 2.4f, 3.0f));
                for (int s = 0; s < 3; s++)
                {
                    Vector3 from = Along(limb, 0.35f + 0.22f * s);
                    Vector3 sideways = Quaternion.AngleAxis(Range(rng, -50f, 50f), Vector3.up) * direction + Vector3.up * Range(rng, 0f, 0.4f);
                    float reach = ToSurface(from, sideways.normalized, crown, radii) * Range(rng, 0.7f, 1f);
                    if (reach < 0.8f) continue;
                    var branch = Bend(from, sideways.normalized, reach, 0.1f * reach);
                    parts.Tube(branch, t => Mathf.Lerp(0.05f, 0.015f, t), 3, 0.4f);
                    parts.Twigs(branch, 0.3f, 1f, rng, Range(rng, 2.2f, 2.8f));
                }
            }
            if (far) parts.Shell(rng, 26, 0.6f, 5.4f);
            else parts.Fill(rng, 55, 2.8f);
            return parts;
        }

        /// <summary>A spruce 18 m tall: a straight trunk and whorls of five boughs every 60
        /// cm or so, longest at the bottom, drooping more the lower they are. Each bough is
        /// two cards, one lying along it and one standing, so it has a shape from any side.
        /// Far away it is three tall cards crossed, each one the whole tree's outline.</summary>
        static Parts Conifer(Random rng, bool far)
        {
            const float H = 18f;
            var parts = new Parts();
            parts.Centre = new Vector3(0f, 0.45f * H, 0f);
            parts.Radii = new Vector3(0.3f * H, 0.6f * H, 0.3f * H);
            var trunk = new List<Vector3>();
            float lean = Range(rng, -0.02f, 0.02f);
            for (int k = 0; k <= 6; k++) trunk.Add(new Vector3(lean * H * k / 6f, H * k / 6f, 0f));
            parts.Tube(trunk, t => Mathf.Lerp(0.36f, 0.04f, t), far ? 4 : 8, 1f);
            float spread = 0.23f * H * Range(rng, 0.85f, 1.15f);

            if (far)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector3 across = Quaternion.Euler(0f, k * 60f + Range(rng, -10f, 10f), 0f) * Vector3.right;
                    parts.Card(new Vector3(0f, 0.1f * H, 0f), Vector3.up, across, 0.92f * H, 2f * spread * 1.9f);
                }
                return parts;
            }

            float azimuth = Range(rng, 0f, 360f);
            for (float y = 0.12f * H; y < 0.96f * H; y += Range(rng, 0.5f, 0.75f))
            {
                float t = (y - 0.12f * H) / (0.84f * H);
                float length = Mathf.Pow(1f - t, 0.9f) * spread + 0.4f;
                float droop = Mathf.Lerp(28f, 8f, t);
                Vector3 at = Along(trunk, y / H);
                for (int b = 0; b < 5; b++)
                {
                    azimuth += 72f + Range(rng, -15f, 15f);
                    Vector3 direction = Quaternion.Euler(droop + Range(rng, -6f, 6f), azimuth, 0f) * Vector3.forward;
                    Vector3 flat = Vector3.Cross(direction, Vector3.up).normalized;
                    Vector3 across = Quaternion.AngleAxis(Range(rng, -20f, 20f), direction) * flat;
                    float width = length * 0.8f;
                    parts.Card(at, direction, across, length * 1.1f, width);
                    parts.Card(at, direction, Vector3.Cross(across, direction).normalized, length * 1.1f, width * 0.8f);
                }
            }
            Vector3 top = Along(trunk, 0.93f);
            parts.Card(top, Vector3.up, Vector3.right, 0.1f * H, 1.1f);
            parts.Card(top, Vector3.up, Vector3.forward, 0.1f * H, 1.1f);
            return parts;
        }

        /// <summary>A palm 12 m tall: a trunk curving away from upright, and a crown of
        /// fronds at its top, the young ones reaching up, the old ones hanging down. Each frond
        /// is an arched strip of the frond texture, folded a little along its stem.</summary>
        static Parts Palm(Random rng, bool far)
        {
            const float H = 12f;
            var parts = new Parts();
            float bend = Range(rng, 0.06f, 0.16f) * H;
            float heading = Range(rng, 0f, 360f);
            Vector3 away = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            var trunk = new List<Vector3>();
            int rings = far ? 4 : 10;
            for (int k = 0; k <= rings; k++)
            {
                float t = k / (float)rings;
                trunk.Add(away * bend * t * t + Vector3.up * H * 0.8f * t);
            }
            parts.Tube(trunk, t => Mathf.Lerp(0.3f, 0.22f, t) * (t < 0.05f ? 1.3f : 1f), far ? 5 : 8, 1.4f);
            Vector3 top = trunk[trunk.Count - 1];
            parts.Centre = top;
            parts.Radii = new Vector3(4f, 3f, 4f);

            int count = far ? 8 : rng.Next(13, 17);
            float azimuth = Range(rng, 0f, 360f);
            for (int f = 0; f < count; f++)
            {
                azimuth += 360f / count + Range(rng, -12f, 12f);
                float rise = Mathf.Lerp(55f, -25f, f / (float)(count - 1)) + Range(rng, -8f, 8f);
                float length = Range(rng, 4.8f, 6f);
                Vector3 start = top + Vector3.up * Range(rng, -0.2f, 0.2f);
                Vector3 direction = Quaternion.Euler(-rise, azimuth, 0f) * Vector3.forward;
                var spine = new List<Vector3> { start };
                int segments = far ? 3 : 6;
                Vector3 p = start, d = direction;
                for (int s = 0; s < segments; s++)
                {
                    d = (d + Vector3.down * (0.22f + 0.1f * s / segments)).normalized;
                    p += d * (length / segments);
                    spine.Add(p);
                }
                parts.Frond(spine, 2.5f, 0.25f);
            }
            return parts;
        }

        /// <summary>A bush 2 m tall: twig cards from its middle, outward and up.</summary>
        static Parts Bush(Random rng, bool far)
        {
            var parts = new Parts { Centre = new Vector3(0f, 0.7f, 0f), Radii = new Vector3(1.3f, 1f, 1.3f) };
            int cards = far ? 6 : 18;
            for (int k = 0; k < cards; k++)
            {
                Vector3 up = (Quaternion.Euler(-Range(rng, 20f, 80f), Range(rng, 0f, 360f), 0f) * Vector3.forward).normalized;
                Vector3 across = Vector3.Cross(up, Onto(rng)).normalized;
                parts.Card(new Vector3(Range(rng, -0.2f, 0.2f), 0.2f, Range(rng, -0.2f, 0.2f)), up, across,
                           far ? 1.9f : Range(rng, 1.2f, 1.6f), far ? 1.9f : Range(rng, 1.2f, 1.6f));
            }
            return parts;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>A model in the making: bark (submesh 0) and leaves (submesh 1).</summary>
        sealed class Parts
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(), Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Bark = new List<int>(), Leaves = new List<int>();
            public Vector3 Centre, Radii = Vector3.one;

            /// <summary>A tube through the points, radius(t) along it, the bark wrapping round
            /// it once every `wrap` metres of circumference so its grain keeps its size.</summary>
            public void Tube(List<Vector3> points, Func<float, float> radius, int sides, float wrap)
            {
                float total = 0f;
                for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
                float along = 0f;
                int first = Vertices.Count;
                Vector3 reference = Vector3.forward;
                for (int i = 0; i < points.Count; i++)
                {
                    if (i > 0) along += Vector3.Distance(points[i - 1], points[i]);
                    Vector3 tangent = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                    if (Mathf.Abs(Vector3.Dot(tangent, reference)) > 0.9f) reference = Vector3.right;
                    Vector3 a = Vector3.Cross(tangent, reference).normalized, b = Vector3.Cross(tangent, a);
                    float r = radius(total > 0f ? along / total : 0f);
                    float around = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * r / wrap));
                    for (int s = 0; s <= sides; s++)
                    {
                        float angle = 2f * Mathf.PI * s / sides;
                        Vector3 outward = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                        Vertices.Add(points[i] + outward * r);
                        Normals.Add(outward);
                        Uvs.Add(new Vector2(around * s / sides, along / wrap));
                    }
                }
                for (int i = 0; i + 1 < points.Count; i++)
                    for (int s = 0; s < sides; s++)
                    {
                        int k = first + i * (sides + 1) + s, up = k + sides + 1;
                        Bark.AddRange(new[] { k, k + 1, up, k + 1, up + 1, up });
                    }
            }

            /// <summary>A card of twig fixed at `at`, the twig running `length` along `up`,
            /// `width` across. Both faces show; its normals are the crown's.</summary>
            public void Card(Vector3 at, Vector3 up, Vector3 across, float length, float width)
            {
                up = up.normalized;
                across = across.normalized * (width * 0.5f);
                int first = Vertices.Count;
                Vector3[] corners = { at - across, at + across, at - across + up * length, at + across + up * length };
                Vector2[] uv = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                for (int c = 0; c < 4; c++)
                {
                    Vertices.Add(corners[c]);
                    Normals.Add(CrownNormal(corners[c]));
                    Uvs.Add(uv[c]);
                }
                Leaves.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
            }

            /// <summary>Out from the crown's middle, scaled to its shape, tipped a little up
            /// since light comes from above.</summary>
            Vector3 CrownNormal(Vector3 p)
            {
                Vector3 d = p - Centre;
                var n = new Vector3(d.x / Radii.x, d.y / Radii.y, d.z / Radii.z);
                if (n.sqrMagnitude < 1e-4f) n = Vector3.up;
                return (n.normalized + Vector3.up * 0.35f).normalized;
            }

            /// <summary>Twig cards along a branch from fraction `from` to `to` of it, one
            /// every 0.8 m or so, each pointing out from the crown and a little along the branch.</summary>
            public void Twigs(List<Vector3> branch, float from, float to, Random rng, float size)
            {
                float length = 0f;
                for (int i = 1; i < branch.Count; i++) length += Vector3.Distance(branch[i - 1], branch[i]);
                int count = Mathf.Max(1, Mathf.RoundToInt(length * (to - from) / 0.8f));
                for (int k = 0; k <= count; k++)
                {
                    Vector3 at = Along(branch, Mathf.Lerp(from, to, k / (float)count));
                    Vector3 direction = (Along(branch, Mathf.Min(1f, Mathf.Lerp(from, to, k / (float)count) + 0.05f)) - at).normalized;
                    Vector3 outward = (at - Centre).normalized;
                    Vector3 up = (outward + direction * 0.6f + Onto(rng) * 0.5f).normalized;
                    Vector3 across = Vector3.Cross(up, Onto(rng)).normalized;
                    float s = size * Range(rng, 0.85f, 1.15f);
                    Card(at - up * (0.25f * s), up, across, s, s);
                }
            }

            /// <summary>Cards inside the crown, so it does not look hollow through its gaps.</summary>
            public void Fill(Random rng, int count, float size)
            {
                for (int k = 0; k < count; k++)
                {
                    Vector3 d = Onto(rng) * Mathf.Sqrt(Range(rng, 0.2f, 0.9f));
                    Vector3 at = Centre + Vector3.Scale(d, Radii);
                    Vector3 up = (d.normalized + Onto(rng) * 0.4f).normalized;
                    Card(at - up * (0.3f * size), up, Vector3.Cross(up, Onto(rng)).normalized, size, size);
                }
            }

            /// <summary>The far version's crown: `count` large cards spread evenly over the
            /// ellipsoid at `depth` of its radius, facing out.</summary>
            public void Shell(Random rng, int count, float depth, float size)
            {
                for (int k = 0; k < count; k++)
                {
                    float y = 1f - 2f * (k + 0.5f) / count;
                    float r = Mathf.Sqrt(1f - y * y), phi = k * 2.39996f;
                    var d = new Vector3(Mathf.Cos(phi) * r, y * 0.8f, Mathf.Sin(phi) * r);
                    Vector3 at = Centre + Vector3.Scale(d * depth, Radii);
                    Vector3 up = (d * 0.6f + Vector3.up * 0.3f + Onto(rng) * 0.8f).normalized;
                    Card(at - up * (0.45f * size), up, Vector3.Cross(up, Onto(rng)).normalized, size, size);
                }
            }

            /// <summary>A frond along a spine: a strip `width` across, its two halves lifted by
            /// `fold` of the width so it is a shallow V, as a palm leaf is.</summary>
            public void Frond(List<Vector3> spine, float width, float fold)
            {
                int first = Vertices.Count;
                for (int i = 0; i < spine.Count; i++)
                {
                    Vector3 tangent = (spine[Mathf.Min(i + 1, spine.Count - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                    if (side.sqrMagnitude < 0.5f) side = Vector3.right;
                    Vector3 lift = Vector3.Cross(tangent, side).normalized * (fold * width);
                    float v = i / (float)(spine.Count - 1);
                    for (int s = -1; s <= 1; s++)
                    {
                        Vertices.Add(spine[i] + side * (s * width * 0.5f) + (s != 0 ? lift : Vector3.zero));
                        Normals.Add((Vector3.up + (spine[i] - Centre).normalized * 0.5f).normalized);
                        Uvs.Add(new Vector2(0.5f + 0.5f * s, v));
                    }
                }
                for (int i = 0; i + 1 < spine.Count; i++)
                    for (int s = 0; s < 2; s++)
                    {
                        int k = first + i * 3 + s, up = k + 3;
                        Leaves.AddRange(new[] { k, up, k + 1, k + 1, up, up + 1 });
                    }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = 2 };
                if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Bark, 0);
                mesh.SetTriangles(Leaves, 1);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }

        /// <summary>The point `t` of the way along a polyline, by length.</summary>
        static Vector3 Along(List<Vector3> line, float t)
        {
            float total = 0f;
            for (int i = 1; i < line.Count; i++) total += Vector3.Distance(line[i - 1], line[i]);
            float want = Mathf.Clamp01(t) * total;
            for (int i = 1; i < line.Count; i++)
            {
                float step = Vector3.Distance(line[i - 1], line[i]);
                if (want <= step) return Vector3.Lerp(line[i - 1], line[i], step > 0f ? want / step : 0f);
                want -= step;
            }
            return line[line.Count - 1];
        }

        /// <summary>A branch from start along direction, curving up by `lift` at its end.</summary>
        static List<Vector3> Bend(Vector3 start, Vector3 direction, float length, float lift)
        {
            var points = new List<Vector3>();
            for (int k = 0; k <= 4; k++)
            {
                float t = k / 4f;
                points.Add(start + direction * (length * t) + Vector3.up * (lift * t * t));
            }
            return points;
        }

        /// <summary>How far from `from` along `direction` the ellipsoid's surface is.</summary>
        static float ToSurface(Vector3 from, Vector3 direction, Vector3 centre, Vector3 radii)
        {
            Vector3 o = Vector3.Scale(from - centre, new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z));
            Vector3 d = Vector3.Scale(direction, new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z));
            float a = Vector3.Dot(d, d), b = 2f * Vector3.Dot(o, d), c = Vector3.Dot(o, o) - 1f;
            float disc = b * b - 4f * a * c;
            return disc < 0f ? 1f : Mathf.Max(0.5f, (-b + Mathf.Sqrt(disc)) / (2f * a));
        }

        static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static Vector3 Onto(Random rng)
        {
            float z = Range(rng, -1f, 1f), phi = Range(rng, 0f, 2f * Mathf.PI), r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(phi), z, r * Mathf.Sin(phi));
        }

        // ---------------------------------------------------------------- assets

        static void Save(string name, Parts near, Parts far, Material bark, Material leaves)
        {
            Directory.CreateDirectory($"{Folder}/Meshes");
            Mesh nearMesh = StoreMesh($"{Folder}/Meshes/{name}.asset", near.ToMesh(name));
            Mesh farMesh = StoreMesh($"{Folder}/Meshes/{name} Far.asset", far.ToMesh($"{name} Far"));

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = nearMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { bark, leaves };
            var lod = go.AddComponent<TreeLod>();
            lod.farMesh = farMesh;
            lod.farMaterials = new[] { bark, leaves };
            PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>The mesh at path, replaced in place if it exists so its references hold.</summary>
        static Mesh StoreMesh(string path, Mesh mesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Material BarkMaterial(string name, string texture)
        {
            string folder = $"{Folder}/Bark";
            Texture2D colour = Texture($"{folder}/{texture}_diff_1k.jpg", normal: false, leaves: false);
            Texture2D normal = Texture($"{folder}/{texture}_nor_gl_1k.jpg", normal: true, leaves: false);
            Material material = MaterialAt($"{Folder}/Materials/{name}.mat");
            material.SetTexture("_BaseMap", colour);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Smoothness", 0.08f);
            return material;
        }

        /// <summary>Leaves: cut out where the texture is transparent, both faces drawn.</summary>
        static Material LeafMaterial(string name, string texture, Color tint)
        {
            Texture2D colour = Texture($"{Folder}/Textures/{texture}.png", normal: false, leaves: true);
            Material material = MaterialAt($"{Folder}/Materials/{name}.mat");
            material.SetTexture("_BaseMap", colour);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            return material;
        }

        static Material MaterialAt(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A texture with the import it needs. Leaves keep their coverage in the
        /// mipmaps, or a tree thins to bare twigs as it recedes.</summary>
        static Texture2D Texture(string path, bool normal, bool leaves)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter
                ?? throw new FileNotFoundException($"{path} not found; see Art/CREDITS.md and Tools/foliage_textures.py.");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.mipMapsPreserveCoverage != leaves)
            {
                importer.textureType = type;
                importer.alphaIsTransparency = leaves;
                importer.mipMapsPreserveCoverage = leaves;
                importer.alphaTestReferenceValue = 0.45f;
                importer.wrapMode = leaves ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
