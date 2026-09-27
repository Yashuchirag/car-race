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
    /// The circuit's buildings, generated at real sizes in photographed CC0 materials
    /// (ambientCG, see Art/CREDITS.md) in place of the Kenney kits: open scaffold stands with
    /// aluminium benches, hospitality marquees, billboards carrying made-up sponsors
    /// (Tools/structure_textures.py), braking distance boards, and city buildings clad in
    /// photographed facades whose lit windows glow at night. The pit building and the main
    /// grandstand, and the trackside pieces and landmarks, are modelled in Blender instead
    /// (Tools/blender/scenery.py): BlenderModel makes them prefabs in these materials, which
    /// their parts are named after (Named).
    ///
    /// Each model is one mesh, a submesh per material, its front (the side facing the
    /// track) towards -Z and its base at y 0, the same conventions as the kits, so the
    /// scenery places them the same way. Texture coordinates are in metres over each
    /// material's real size, taken in the model's own space, so a facade's floors line up
    /// with the storeys whatever the building's height and textures run on across a face.
    /// Built fresh the first time one is asked for in an editor session; meshes and
    /// prefabs are overwritten in place.
    /// </summary>
    public static class StructureModels
    {
        const string Folder = "Assets/Art/Structures";
        const float Storey = 3.6f;

        /// <summary>A facade texture and how many metres tall one repeat of it is.</summary>
        static readonly (string id, float tileM, bool lit)[] Facades =
        {
            ("Facade018A", 18f, true), ("Facade019A", 25.2f, true), ("Facade020B", 21.6f, true),
            ("Facade017", 32.4f, true), ("Facade006", 28.8f, false), ("Facade001", 36f, false),
        };

        public static readonly string[] Shops, Towers, Blocks;
        static StructureModels()
        {
            var shops = new List<string>();
            var towers = new List<string>();
            var blocks = new List<string>();
            for (int f = 0; f < Facades.Length; f++)
                for (int v = 0; v < 2; v++)
                {
                    shops.Add($"building_shop_{f}_{v}");
                    towers.Add($"building_tower_{f}_{v}");
                    blocks.Add($"building_block_{f}_{v}");
                }
            Shops = shops.ToArray();
            Towers = towers.ToArray();
            Blocks = blocks.ToArray();
        }

        static bool _built;
        static Material _concrete, _steel, _darkSteel, _corrugated, _fabric, _seats, _roof, _white;
        static Material[] _ads, _boards, _facades;

        public static GameObject Load(string name)
        {
            if (!_built) Build();
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{name}.prefab")
                ?? throw new FileNotFoundException($"{Folder}/{name}.prefab was not generated.");
        }

        /// <summary>Day facade materials and their lit-window copies, for the night city.</summary>
        public static (Material day, Material night)[] FacadeNights()
        {
            if (!_built) Build();
            var pairs = new List<(Material, Material)>();
            for (int f = 0; f < Facades.Length; f++)
            {
                Material day = _facades[f];
                string path = $"{Folder}/Materials/{day.name} Night.mat";
                var night = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (night == null)
                {
                    night = new Material(day);
                    AssetDatabase.CreateAsset(night, path);
                }
                night.CopyPropertiesFromMaterial(day);
                string emission = $"{Folder}/{Facades[f].id}/{Facades[f].id}_1K-JPG_Emission.jpg";
                night.SetTexture("_EmissionMap", Facades[f].lit ? Texture(emission, false) : day.GetTexture("_BaseMap"));
                night.SetColor("_EmissionColor", Facades[f].lit ? new Color(1.6f, 1.45f, 1.2f) : new Color(0.1f, 0.1f, 0.09f));
                night.EnableKeyword("_EMISSION");
                night.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(night);
                pairs.Add((day, night));
            }
            return pairs.ToArray();
        }

        [MenuItem("CarRace/Rebuild Structures")]
        public static void Build()
        {
            Materials();
            Save("stand_open", OpenStand(), _steel, _darkSteel);
            Save("marquee", Marquee(), _fabric, _steel);
            for (int a = 0; a < _ads.Length; a++) Save($"billboard_{a}", Billboard(), _darkSteel, _ads[a]);
            int[] distances = { 150, 100, 50 };
            for (int b = 0; b < distances.Length; b++) Save($"board_{distances[b]}", DistanceBoard(), _steel, _boards[b], _white);

            for (int f = 0; f < Facades.Length; f++)
                for (int v = 0; v < 2; v++)
                {
                    var rng = new Random(1000 + f * 10 + v);
                    Save($"building_shop_{f}_{v}", Building(rng, Facades[f].tileM, rng.Next(3, 7), 1), _facades[f], _roof, _darkSteel);
                    Save($"building_tower_{f}_{v}", Building(rng, Facades[f].tileM, rng.Next(14, 30), 2), _facades[f], _roof, _darkSteel);
                    Save($"building_block_{f}_{v}", Building(rng, Facades[f].tileM, rng.Next(6, 13), 1), _facades[f], _roof, _darkSteel);
                }
            AssetDatabase.SaveAssets();
            _built = true;
        }

        // ---------------------------------------------------------------- the models

        /// <summary>An open stand, 10 m wide: eight rows of aluminium benches and footboards
        /// on a galvanised scaffold of posts and braces, with a rail round the top.</summary>
        static Parts OpenStand()
        {
            var p = new Parts(2f, 1f);
            const int Rows = 8;
            const float Tread = 0.8f, Rise = 0.5f, First = 0.8f, Front = -3.5f;
            for (int r = 0; r < Rows; r++)
            {
                float y = First + r * Rise, z = Front + (r + 0.5f) * Tread;
                p.Box(0, new Vector3(0f, y, z), new Vector3(10f, 0.05f, Tread));
                p.Box(0, new Vector3(0f, y + 0.44f, z + 0.15f), new Vector3(10f, 0.05f, 0.32f));
                foreach (float x in new[] { -4.9f, -2.45f, 0f, 2.45f, 4.9f })
                    p.Box(1, new Vector3(x, y * 0.5f, z), new Vector3(0.08f, y, 0.08f));
            }
            float topY = First + (Rows - 1) * Rise, backZ = Front + Rows * Tread;
            foreach (float x in new[] { -4.9f, -2.45f, 0f, 2.45f, 4.9f })
            {
                p.Box(1, new Vector3(x, topY + 0.55f, backZ), new Vector3(0.06f, 1.1f, 0.06f));
                p.Beam(1, new Vector3(x, 0f, Front + 0.4f), new Vector3(x, topY, backZ - 0.1f), 0.06f, 0.06f);
            }
            p.Box(1, new Vector3(0f, topY + 1.1f, backZ), new Vector3(10f, 0.06f, 0.06f));
            return p;
        }

        /// <summary>A marquee 8 m square: white fabric walls on corner poles and a pyramid
        /// roof peaking at 4.6 m.</summary>
        static Parts Marquee()
        {
            var p = new Parts(1.5f, 1f);
            const float H = 2.6f, Peak = 4.6f, S = 4f;
            Vector3[] c = { new Vector3(-S, 0f, -S), new Vector3(S, 0f, -S), new Vector3(S, 0f, S), new Vector3(-S, 0f, S) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % 4];
                p.Quad(0, a, b, b + Vector3.up * H, a + Vector3.up * H);
                p.Triangle(0, a + Vector3.up * H, b + Vector3.up * H, Vector3.up * Peak);
                p.Box(1, a + Vector3.up * (H * 0.5f), new Vector3(0.1f, H, 0.1f));
            }
            return p;
        }

        /// <summary>A billboard 10 m wide: a printed face 10 by 3.5 m in a dark steel frame
        /// on two legs.</summary>
        static Parts Billboard()
        {
            var p = new Parts(2f, 1f);
            foreach (float x in new[] { -3.5f, 3.5f }) p.Box(0, new Vector3(x, 1.6f, 0.1f), new Vector3(0.3f, 3.2f, 0.3f));
            p.Box(0, new Vector3(0f, 4.35f, 0.05f), new Vector3(10.3f, 3.8f, 0.25f));
            p.Picture(1, new Vector3(-5f, 2.6f, -0.09f), new Vector3(5f, 2.6f, -0.09f), new Vector3(5f, 6.1f, -0.09f), new Vector3(-5f, 6.1f, -0.09f));
            return p;
        }

        /// <summary>A braking board: 1.5 by 1.1 m on a post, its number facing the cars.</summary>
        static Parts DistanceBoard()
        {
            var p = new Parts(1f, 1f, 1f);
            p.Box(0, new Vector3(0f, 0.8f, 0.06f), new Vector3(0.1f, 1.6f, 0.1f));
            p.Box(2, new Vector3(0f, 1.65f, 0f), new Vector3(1.5f, 1.1f, 0.05f));
            p.Picture(1, new Vector3(-0.75f, 1.1f, -0.03f), new Vector3(0.75f, 1.1f, -0.03f), new Vector3(0.75f, 2.2f, -0.03f), new Vector3(-0.75f, 2.2f, -0.03f));
            return p;
        }

        /// <summary>A city building: a footprint 20 to 30 m, `floors` storeys clad in a facade,
        /// in one block or with a setback tier above, a concrete roof behind a parapet and
        /// plant on the roof.</summary>
        static Parts Building(Random rng, float facadeTileM, int floors, int tiers)
        {
            var p = new Parts(facadeTileM, 4f, 2f);
            float w = Range(rng, 20f, 30f), d = Range(rng, 20f, 30f);
            float y = 0f;
            int remaining = floors;
            for (int t = 0; t < tiers; t++)
            {
                int storeys = t == tiers - 1 ? remaining : Mathf.Max(3, (int)(remaining * Range(rng, 0.5f, 0.7f)));
                remaining -= storeys;
                float h = storeys * Storey;
                p.Walls(0, new Vector3(0f, y, 0f), w, d, h);
                y += h;
                p.Roof(1, new Vector3(0f, y, 0f), w, d);
                if (t < tiers - 1) { w *= Range(rng, 0.6f, 0.8f); d *= Range(rng, 0.6f, 0.8f); }
            }
            int units = rng.Next(2, 5);
            for (int u = 0; u < units; u++)
            {
                var size = new Vector3(Range(rng, 2f, 5f), Range(rng, 1.5f, 3.5f), Range(rng, 2f, 5f));
                var at = new Vector3(Range(rng, -w * 0.3f, w * 0.3f), y + size.y * 0.5f, Range(rng, -d * 0.3f, d * 0.3f));
                p.Box(2, at, size);
            }
            return p;
        }

        static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // ---------------------------------------------------------------- geometry

        /// <summary>A model in the making, a submesh per material, each material with the
        /// size in metres its texture repeats at.</summary>
        sealed class Parts
        {
            readonly List<Vector3> _vertices = new List<Vector3>(), _normals = new List<Vector3>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly List<int>[] _triangles;
            readonly float[] _tile;

            public Parts(params float[] tileMetres)
            {
                _tile = tileMetres;
                _triangles = new List<int>[tileMetres.Length];
                for (int i = 0; i < _triangles.Length; i++) _triangles[i] = new List<int>();
            }

            /// <summary>A flat quad: a bottom left, b bottom right, c top right, d top left,
            /// as seen from its front. Texture in metres from the model's origin.</summary>
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool swapUv = false)
            {
                Vector3 across = (b - a).normalized, up = (d - a).normalized;
                Vector3 normal = Vector3.Cross(up, across).normalized;
                int first = _vertices.Count;
                foreach (Vector3 v in new[] { a, b, c, d })
                {
                    _vertices.Add(v);
                    _normals.Add(normal);
                    var uv = new Vector2(Vector3.Dot(v, across), Vector3.Dot(v, up)) / _tile[sub];
                    _uvs.Add(swapUv ? new Vector2(uv.y, uv.x) : uv);
                }
                _triangles[sub].AddRange(new[] { first, first + 3, first + 2, first, first + 2, first + 1 });
            }

            /// <summary>A quad showing its whole texture once, for a printed face.</summary>
            public void Picture(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Vector3 normal = Vector3.Cross((d - a).normalized, (b - a).normalized).normalized;
                int first = _vertices.Count;
                Vector2[] uv = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                Vector3[] corners = { a, b, c, d };
                for (int i = 0; i < 4; i++) { _vertices.Add(corners[i]); _normals.Add(normal); _uvs.Add(uv[i]); }
                _triangles[sub].AddRange(new[] { first, first + 3, first + 2, first, first + 2, first + 1 });
            }

            /// <summary>A triangle, a b along its base as seen from the front, c its peak.</summary>
            public void Triangle(int sub, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 across = (b - a).normalized;
                Vector3 normal = Vector3.Cross((c - a).normalized, across).normalized;
                Vector3 up = Vector3.Cross(across, normal).normalized * -1f;
                int first = _vertices.Count;
                foreach (Vector3 v in new[] { a, b, c })
                {
                    _vertices.Add(v);
                    _normals.Add(normal);
                    _uvs.Add(new Vector2(Vector3.Dot(v, across), Vector3.Dot(v, up)) / _tile[sub]);
                }
                _triangles[sub].AddRange(new[] { first, first + 2, first + 1 });
            }

            /// <summary>A box, turned by rotation about its centre.</summary>
            public void Box(int sub, Vector3 centre, Vector3 size, Quaternion? rotation = null)
            {
                Quaternion q = rotation ?? Quaternion.identity;
                Vector3 r = q * Vector3.right * (size.x * 0.5f), u = q * Vector3.up * (size.y * 0.5f), f = q * Vector3.forward * (size.z * 0.5f);
                Vector3 c = centre;
                Quad(sub, c - r - u - f, c + r - u - f, c + r + u - f, c - r + u - f);   // front, -Z
                Quad(sub, c + r - u + f, c - r - u + f, c - r + u + f, c + r + u + f);   // back
                Quad(sub, c + r - u - f, c + r - u + f, c + r + u + f, c + r + u - f);   // right
                Quad(sub, c - r - u + f, c - r - u - f, c - r + u - f, c - r + u + f);   // left
                Quad(sub, c - r + u - f, c + r + u - f, c + r + u + f, c - r + u + f);   // top
                Quad(sub, c + r - u - f, c - r - u - f, c - r - u + f, c + r - u + f);   // bottom
            }

            /// <summary>A beam of the given width and depth from one point to another.</summary>
            public void Beam(int sub, Vector3 from, Vector3 to, float width, float depth)
            {
                Vector3 along = to - from;
                Quaternion q = Quaternion.LookRotation(along.normalized, Mathf.Abs(along.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
                Box(sub, (from + to) * 0.5f, new Vector3(width, depth, along.magnitude), q);
            }

            /// <summary>Four walls round a rectangle, from base up by height.</summary>
            public void Walls(int sub, Vector3 base_, float w, float d, float height)
            {
                Vector3 up = Vector3.up * height;
                Vector3 a = base_ + new Vector3(-w / 2, 0f, -d / 2), b = base_ + new Vector3(w / 2, 0f, -d / 2);
                Vector3 c = base_ + new Vector3(w / 2, 0f, d / 2), e = base_ + new Vector3(-w / 2, 0f, d / 2);
                Quad(sub, a, b, b + up, a + up);
                Quad(sub, b, c, c + up, b + up);
                Quad(sub, c, e, e + up, c + up);
                Quad(sub, e, a, a + up, e + up);
            }

            /// <summary>A flat roof at `at` with a parapet 0.9 m high round its edge.</summary>
            public void Roof(int sub, Vector3 at, float w, float d)
            {
                Quad(sub, at + new Vector3(-w / 2, 0.02f, -d / 2), at + new Vector3(w / 2, 0.02f, -d / 2),
                     at + new Vector3(w / 2, 0.02f, d / 2), at + new Vector3(-w / 2, 0.02f, d / 2));
                const float T = 0.3f, H = 0.9f;
                Box(sub, at + new Vector3(0f, H / 2, -d / 2 + T / 2), new Vector3(w, H, T));
                Box(sub, at + new Vector3(0f, H / 2, d / 2 - T / 2), new Vector3(w, H, T));
                Box(sub, at + new Vector3(-w / 2 + T / 2, H / 2, 0f), new Vector3(T, H, d - 2 * T));
                Box(sub, at + new Vector3(w / 2 - T / 2, H / 2, 0f), new Vector3(T, H, d - 2 * T));
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = _triangles.Length };
                if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }

        // ---------------------------------------------------------------- assets

        static void Save(string name, Parts parts, params Material[] materials)
        {
            Directory.CreateDirectory($"{Folder}/Meshes");
            string meshPath = $"{Folder}/Meshes/{name}.asset";
            Mesh mesh = parts.ToMesh(name);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); mesh = existing; }

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }

        static void Materials()
        {
            _concrete = Pbr("Concrete", "Concrete034", Color.white, 0.12f, 0f);
            _corrugated = Pbr("Corrugated Steel", "CorrugatedSteel005", new Color(0.9f, 0.92f, 0.95f), 0.45f, 0.6f);
            _steel = Pbr("Galvanised Steel", "CorrugatedSteel005", new Color(0.85f, 0.87f, 0.9f), 0.4f, 0.7f, normal: false);
            _darkSteel = Pbr("Dark Steel", "Metal028", Color.white, 0.35f, 0.5f);
            _fabric = Pbr("Marquee Fabric", "Fabric036", new Color(1f, 1f, 1f), 0.1f, 0f);
            _roof = Pbr("Roof", "Concrete034", new Color(0.62f, 0.62f, 0.6f), 0.08f, 0f);
            _white = Flat("Painted White", new Color(0.9f, 0.9f, 0.88f), 0.35f);
            _seats = Flat("Stand Seats", new Color(0.08f, 0.22f, 0.62f), 0.5f);
            _ads = new Material[4];
            for (int a = 0; a < 4; a++) _ads[a] = Printed($"Advert {a}", $"{Folder}/Printed/ad_{a}.png");
            int[] distances = { 150, 100, 50 };
            _boards = new Material[3];
            for (int b = 0; b < 3; b++) _boards[b] = Printed($"Board {distances[b]}", $"{Folder}/Printed/board_{distances[b]}.png");
            _facades = new Material[Facades.Length];
            for (int f = 0; f < Facades.Length; f++)
                _facades[f] = Pbr(Facades[f].id, Facades[f].id, Color.white, 0.45f, 0.1f);
        }

        static Material Pbr(string name, string id, Color tint, float smoothness, float metallic, bool normal = true)
        {
            Material m = MaterialAt(name);
            m.SetTexture("_BaseMap", Texture($"{Folder}/{id}/{id}_1K-JPG_Color.jpg", false));
            m.SetColor("_BaseColor", tint);
            if (normal)
            {
                m.SetTexture("_BumpMap", Texture($"{Folder}/{id}/{id}_1K-JPG_NormalGL.jpg", true));
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        static Material Flat(string name, Color colour, float smoothness)
        {
            Material m = MaterialAt(name);
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static Material Printed(string name, string texture)
        {
            Material m = MaterialAt(name);
            m.SetTexture("_BaseMap", Texture(texture, false, clamp: true));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.25f);
            return m;
        }

        // ---------------------------------------------------------------- Blender scenery

        const string SceneryArt = "Assets/Art/Scenery";

        /// <summary>The material a Blender scenery model (Tools/blender/scenery.py) names a part
        /// after: this class's own where they are the same thing, new ones otherwise.</summary>
        public static Material Named(string key)
        {
            if (!_built) Build();
            switch (key)
            {
                case "Concrete": return _concrete;
                case "Steel": return _steel;
                case "DarkSteel": return _darkSteel;
                case "White": return _white;
                case "Roof": return _roof;
                case "Corrugated": return _corrugated;
                case "Seats": return _seats;
                case "Orange": return Flat("Painted Orange", new Color(0.95f, 0.4f, 0.05f), 0.35f);
                case "Rubber": return Flat("Rubber", new Color(0.035f, 0.035f, 0.035f), 0.15f);
                case "BeltRed": return Flat("Belt Red", new Color(0.62f, 0.05f, 0.04f), 0.3f);
                case "BeltWhite": return Flat("Belt White", new Color(0.85f, 0.85f, 0.83f), 0.3f);
                case "Plaster": return Pbr("Plaster", "Concrete034", new Color(0.97f, 0.82f, 0.58f), 0.1f, 0f);
                case "Stone": return Pbr("Stone", "Concrete034", new Color(1f, 0.97f, 0.9f), 0.12f, 0f);
                case "RoofTiles": return Flat("Roof Tiles", new Color(0.5f, 0.22f, 0.13f), 0.2f);
                case "Glass": return Flat("Window Glass", new Color(0.06f, 0.08f, 0.1f), 0.9f);
                case "Alps":
                {
                    Material m = MaterialAt("Alps");
                    m.SetTexture("_BaseMap", Texture($"{SceneryArt}/Alps.png", false));
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Smoothness", 0f);
                    return m;
                }
                default: throw new InvalidOperationException($"No material for a scenery part named {key}; add it to StructureModels.Named.");
            }
        }

        /// <summary>A Blender scenery model as a prefab: the FBX's mesh with the materials its
        /// parts are named after. Made once per editor session and overwritten in place.</summary>
        public static GameObject BlenderModel(string name)
        {
            string prefab = $"{SceneryArt}/{name}.prefab";
            if (_blender.Contains(name)) return AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            string fbx = $"{SceneryArt}/{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx)
                ?? throw new FileNotFoundException($"{fbx} is missing: run Tools/blender/scenery.py (its header has the command).");
            var filter = model.GetComponentInChildren<MeshFilter>();
            var renderer = model.GetComponentInChildren<MeshRenderer>();
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = Array.ConvertAll(renderer.sharedMaterials, m => Named(m.name));
            PrefabUtility.SaveAsPrefabAsset(go, prefab);
            UnityEngine.Object.DestroyImmediate(go);
            _blender.Add(name);
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        }

        static readonly HashSet<string> _blender = new HashSet<string>();

        /// <summary>Chain-link fence: scenery.py's wire texture, cut out where it is clear and
        /// drawn from both sides, since the fence is a single sheet.</summary>
        public static Material Chainlink()
        {
            string path = $"{SceneryArt}/Chainlink.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter
                ?? throw new FileNotFoundException($"{path} is missing: run Tools/blender/scenery.py.");
            if (!importer.alphaIsTransparency || !importer.mipMapsPreserveCoverage)
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true;      // or the wire thins away with distance
                importer.alphaTestReferenceValue = 0.4f;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            Material m = MaterialAt("Chain-link Fence");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_Metallic", 0.6f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.4f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return m;
        }

        static Material MaterialAt(string name)
        {
            string path = $"{Folder}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Texture(string path, bool normal, bool clamp = false)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter
                ?? throw new FileNotFoundException($"{path} not found; see Art/CREDITS.md.");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            var wrap = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (importer.textureType != type || importer.wrapMode != wrap || importer.anisoLevel != 4)
            {
                importer.textureType = type;
                importer.wrapMode = wrap;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Every structure in a row on a lawn, for looking at without a circuit,
        /// written to Builds/structures.png. -executeMethod
        /// CarRace.UnityGame.EditorTools.StructureModels.Preview</summary>
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
            lawn.transform.localScale = new Vector3(40f, 1f, 20f);
            lawn.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Grass.mat");

            string[] front = { "pit_bay", "pit_bay", "stand_bay", "stand_open", "marquee", "billboard_0", "billboard_1", "board_150", "board_100", "board_50" };
            float x = -60f;
            foreach (string name in front)
            {
                var model = name.EndsWith("_bay") ? BlenderModel(name) : Load(name);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                float width = model.GetComponent<MeshFilter>().sharedMesh.bounds.size.x;
                go.transform.position = new Vector3(x + width * 0.5f, 0f, 0f);
                x += width + (name == "pit_bay" ? 0f : 2f);
            }
            x = -80f;
            foreach (string name in new[] { Shops[0], Shops[2], Towers[1], Towers[3], Blocks[4], Blocks[10] })
            {
                var model = Load(name);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                float width = model.GetComponent<MeshFilter>().sharedMesh.bounds.size.x;
                go.transform.position = new Vector3(x + width * 0.5f, 0f, 60f);
                x += width + 6f;
            }

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(-8f, 9f, -42f);
            camera.transform.LookAt(new Vector3(-8f, 10f, 20f));
            camera.fieldOfView = 75f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.72f, 0.86f);
            var target = new RenderTexture(2400, 1200, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes("Builds/structures.png", image.EncodeToPNG());
            Debug.Log("Structure preview written to Builds/structures.png.");
        }
    }
}
