using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The car's looks: a low-poly GT coupe built to the car's own dimensions, in the same
    /// stylised style as the scenery. It replaced a box on four cylinders.
    ///
    /// The body is lofted through Stations, cross-sections from the rear bumper to the nose,
    /// each a half profile mirrored to both sides: the bottom edge, the widest point, the
    /// shoulder, then a top that blends from a flat deck (bonnet, boot) to the cabin (glass and
    /// roof) by Cabin, which gives the fastback rear window and the raked windscreen. Wheel
    /// arches are stations whose bottom edge rises over the tyre, placed from the wheelbase and
    /// the weight split so the wheels always sit in them. Faces are flat shaded.
    ///
    /// Only the painted panels are on "Body", so the colour picker's property block and the AI
    /// cars' materials recolour the paint and nothing else. Glass, black trim (underbody,
    /// splitter, rear wing, mirrors) and the lights are on "Body Details". Each wheel pivot,
    /// which CarController spins and steers, holds a tyre and a rim with five spokes, so the
    /// wheels are seen to turn. None of it has a collider: the car's box does that.
    /// </summary>
    public static class CarModel
    {
        const string Folder = "Assets/Cars";
        const string BodyMeshPath = Folder + "/CarBody.asset";
        const string DetailsMeshPath = Folder + "/CarDetails.asset";
        const string GlassPath = "Assets/Materials/CarGlass.mat";
        const string TrimPath = "Assets/Materials/CarTrim.mat";
        const string RimPath = "Assets/Materials/CarRim.mat";
        const string HeadlightPath = "Assets/Materials/CarHeadlight.mat";
        const string TaillightPath = "Assets/Materials/CarTaillight.mat";
        public const float PaintSmoothness = 0.62f;
        const float TyreWidthM = 0.26f;

        /// <summary>One cross-section, in metres above the road and out from the centreline.</summary>
        struct Station
        {
            public float Z, Bottom, BeltX, BeltY, ShoulderX, ShoulderY, Cabin, Roof;
            public Station(float z, float bottom, float beltX, float beltY, float shoulderX, float shoulderY)
            {
                Z = z; Bottom = bottom; BeltX = beltX; BeltY = beltY; ShoulderX = shoulderX; ShoulderY = shoulderY; Cabin = 0f; Roof = 0f;
            }
        }

        /// <summary>The coupe, rear to front, around wheels at rearAxle and frontAxle, whose
        /// arches reach archHalf either side of the axle and archTop above the road.</summary>
        static List<Station> Stations(float rearAxle, float frontAxle, float archHalf, float archTop)
        {
            // An arch: the bottom edge rises from the sill to over the tyre and back down.
            void Arch(List<Station> list, float axle, float belt, float shoulder)
            {
                list.Add(new Station(axle - archHalf - 0.08f, 0.16f, 0.97f, 0.52f, 0.93f, shoulder));
                list.Add(new Station(axle - archHalf, 0.46f, 0.97f, 0.56f, 0.93f, shoulder));
                list.Add(new Station(axle, archTop, 0.97f, belt, 0.93f, shoulder));
                list.Add(new Station(axle + archHalf, 0.46f, 0.97f, 0.56f, 0.93f, shoulder));
                list.Add(new Station(axle + archHalf + 0.08f, 0.16f, 0.97f, 0.52f, 0.93f, shoulder));
            }
            var s = new List<Station>
            {
                new Station(-2.25f, 0.24f, 0.86f, 0.46f, 0.80f, 0.74f),
                new Station(-2.12f, 0.16f, 0.94f, 0.50f, 0.88f, 0.84f),
            };
            Arch(s, rearAxle, archTop + 0.07f, 0.90f);
            s.Add(new Station(-0.50f, 0.16f, 0.95f, 0.52f, 0.90f, 0.90f));
            s.Add(new Station(0.25f, 0.16f, 0.95f, 0.52f, 0.90f, 0.88f));
            s.Add(new Station(0.62f, 0.16f, 0.95f, 0.52f, 0.90f, 0.87f));
            Arch(s, frontAxle, archTop + 0.05f, 0.82f);
            s.Add(new Station(2.10f, 0.14f, 0.90f, 0.42f, 0.82f, 0.62f));
            s.Add(new Station(2.25f, 0.18f, 0.80f, 0.34f, 0.72f, 0.52f));
            s.Sort((a, b) => a.Z.CompareTo(b.Z));

            // The cabin along the car: a fastback rear window rising to the roof from 1.35 m
            // behind the centre of mass, the roof, then the windscreen down to the scuttle.
            for (int i = 0; i < s.Count; i++)
            {
                Station st = s[i];
                st.Cabin = Mathf.Min(Mathf.InverseLerp(-1.35f, -0.5f, st.Z), Mathf.InverseLerp(0.95f, 0.25f, st.Z));
                st.Roof = RoofM;
                s[i] = st;
            }
            return s;
        }

        const float RoofM = 1.2f;

        /// <summary>The station's right half profile, bottom to centre top, in road heights.</summary>
        static Vector2[] Profile(Station st)
        {
            float c = st.Cabin;
            Vector2 Blend(Vector2 deck, Vector2 cabin) => Vector2.Lerp(deck, cabin, c);
            float sx = st.ShoulderX, sy = st.ShoulderY;
            return new[]
            {
                new Vector2(st.BeltX * 0.93f, st.Bottom),
                new Vector2(st.BeltX, st.BeltY),
                new Vector2(sx, sy),
                Blend(new Vector2(sx * 0.8f, sy + 0.03f), new Vector2(sx * 0.86f, sy + 0.04f)),
                Blend(new Vector2(sx * 0.5f, sy + 0.06f), new Vector2(0.64f, st.Roof - 0.06f)),
                Blend(new Vector2(sx * 0.25f, sy + 0.07f), new Vector2(0.40f, st.Roof)),
                Blend(new Vector2(0f, sy + 0.07f), new Vector2(0f, st.Roof + 0.01f)),
            };
        }

        /// <summary>Flat-shaded triangles in submeshes, each turned to face away from a point
        /// inside the shape, so no winding has to be worked out by hand.</summary>
        sealed class Builder
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<int>[] _triangles;
            public Builder(int submeshes)
            {
                _triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>();
            }

            public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-10f) return;
                if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f) { (b, c) = (c, b); n = -n; }
                n.Normalize();
                int i = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _normals.Add(n); _normals.Add(n); _normals.Add(n);
                _triangles[submesh].Add(i); _triangles[submesh].Add(i + 1); _triangles[submesh].Add(i + 2);
            }

            public void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
            {
                Triangle(submesh, a, b, c, inside);
                Triangle(submesh, a, c, d, inside);
            }

            /// <summary>A box, centre and size in its own axes, turned by rotation.</summary>
            public void Box(int submesh, Vector3 centre, Vector3 size, Quaternion rotation)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
                var p = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
                int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };
                foreach (int[] f in faces) Quad(submesh, p[f[0]], p[f[1]], p[f[2]], p[f[3]], centre);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = _triangles.Length };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        const int Glass = 0, Trim = 1, Headlight = 2, Taillight = 3;

        /// <summary>Body and details meshes, in the car's own space: origin at the centre of
        /// mass, cgHeight above the road.</summary>
        static (Mesh body, Mesh details) Meshes(CarDefinition definition)
        {
            float front = definition.wheelbase * (1f - definition.frontWeightBias);
            float rear = -definition.wheelbase * definition.frontWeightBias;
            float radius = definition.tyreFront.radius;
            List<Station> stations = Stations(rear, front, radius + 0.08f, radius * 2f + 0.05f);
            float down = definition.cgHeight;
            Vector3 At(Vector2 p, float z, float side) => new Vector3(p.x * side, p.y - down, z);

            var body = new Builder(1);
            var details = new Builder(4);
            var profiles = stations.ConvertAll(Profile);
            for (int i = 0; i + 1 < stations.Count; i++)
            {
                Station a = stations[i], b = stations[i + 1];
                Vector2[] pa = profiles[i], pb = profiles[i + 1];
                var inside = new Vector3(0f, (a.Bottom + b.Bottom + a.ShoulderY + b.ShoulderY) * 0.25f - down, (a.Z + b.Z) * 0.5f);
                bool cabin = Mathf.Min(a.Cabin, b.Cabin) >= 0.5f;
                bool screen = Mathf.Max(a.Cabin, b.Cabin) > 0.3f && Mathf.Min(a.Cabin, b.Cabin) < 0.95f;
                foreach (float side in new[] { 1f, -1f })
                {
                    for (int k = 0; k + 1 < pa.Length; k++)
                    {
                        bool glass = (k == 3 && cabin) || (k >= 4 && screen);
                        Vector3 q0 = At(pa[k], a.Z, side), q1 = At(pa[k + 1], a.Z, side), q2 = At(pb[k + 1], b.Z, side), q3 = At(pb[k], b.Z, side);
                        if (glass) details.Quad(Glass, q0, q1, q2, q3, inside);
                        else body.Quad(0, q0, q1, q2, q3, inside);
                    }
                }
                // The underside, black, and over the wheels the arch's roof.
                details.Quad(Trim, At(pa[0], a.Z, 1f), At(pa[0], a.Z, -1f), At(pb[0], b.Z, -1f), At(pb[0], b.Z, 1f), inside + Vector3.up * 0.3f);
            }

            // Tail and nose panels, closing the ends.
            foreach (int end in new[] { 0, stations.Count - 1 })
            {
                Station st = stations[end];
                Vector2[] p = profiles[end];
                var inside = new Vector3(0f, (st.Bottom + st.ShoulderY) * 0.5f - down, st.Z - Mathf.Sign(st.Z) * 0.5f);
                for (int k = 0; k + 1 < p.Length; k++)
                    body.Quad(0, At(p[k], st.Z, 1f), At(p[k + 1], st.Z, 1f), At(p[k + 1], st.Z, -1f), At(p[k], st.Z, -1f), inside);
            }

            // Front splitter, rear wing on two struts with end plates, mirrors.
            float y(float road) => road - down;
            details.Box(Trim, new Vector3(0f, y(0.12f), 2.18f), new Vector3(1.78f, 0.04f, 0.34f), Quaternion.identity);
            details.Box(Trim, new Vector3(0f, y(0.2f), -2.2f), new Vector3(1.5f, 0.12f, 0.12f), Quaternion.identity);   // diffuser lip
            foreach (float side in new[] { -1f, 1f })
            {
                details.Box(Trim, new Vector3(0.45f * side, y(1.0f), -1.98f), new Vector3(0.05f, 0.26f, 0.16f), Quaternion.identity);
                details.Box(Trim, new Vector3(0.86f * side, y(1.14f), -2.0f), new Vector3(0.02f, 0.2f, 0.42f), Quaternion.identity);
                details.Box(Trim, new Vector3(0.99f * side, y(0.98f), 0.5f), new Vector3(0.14f, 0.09f, 0.1f), Quaternion.identity);
            }
            details.Box(Trim, new Vector3(0f, y(1.15f), -2.0f), new Vector3(1.72f, 0.04f, 0.34f), Quaternion.Euler(-6f, 0f, 0f));

            // Lights: headlights set into the nose, tail lights across the tail.
            Station nose = stations[stations.Count - 1], tail = stations[0];
            foreach (float side in new[] { -1f, 1f })
            {
                details.Box(Headlight, new Vector3(0.52f * side, y(nose.ShoulderY - 0.1f), nose.Z - 0.12f), new Vector3(0.34f, 0.07f, 0.3f), Quaternion.Euler(-18f, 0f, 0f));
                details.Box(Taillight, new Vector3(0.58f * side, y(tail.ShoulderY - 0.1f), tail.Z - 0.005f), new Vector3(0.36f, 0.07f, 0.03f), Quaternion.identity);
            }
            return (body.Build("Car Body"), details.Build("Car Details"));
        }

        /// <summary>Builds the looks onto <paramref name="car"/> and returns the four wheel
        /// pivots, FL FR RL RR, for CarController to spin and steer.</summary>
        public static Transform[] Build(Transform car, CarDefinition definition, int layer, Material paint)
        {
            var (bodyMesh, detailsMesh) = Meshes(definition);
            bodyMesh = SaveMesh(bodyMesh, BodyMeshPath);
            detailsMesh = SaveMesh(detailsMesh, DetailsMeshPath);

            paint.SetFloat("_Smoothness", PaintSmoothness);
            Part(car, "Body", bodyMesh, layer, paint);
            Part(car, "Body Details", detailsMesh, layer,
                 Lit(GlassPath, new Color(0.06f, 0.08f, 0.11f), 0.92f, 0f),
                 Lit(TrimPath, new Color(0.035f, 0.035f, 0.04f), 0.35f, 0f),
                 Glow(HeadlightPath, new Color(1f, 0.97f, 0.9f) * 2.2f),
                 Glow(TaillightPath, new Color(1.3f, 0.02f, 0.02f)));   // brighter and the bloom turns it orange

            Material tyre = SkidpadSceneBuilder.EnsureMaterial(SkidpadSceneBuilder.TyreMaterialPath, new Color(0.08f, 0.08f, 0.08f), null, Vector2.one);
            Material rim = Lit(RimPath, new Color(0.72f, 0.73f, 0.76f), 0.7f, 0.85f);
            Material dark = Lit(TrimPath, new Color(0.035f, 0.035f, 0.04f), 0.35f, 0f);

            float halfTrack = definition.trackWidth * 0.5f;
            float front = definition.wheelbase * (1f - definition.frontWeightBias);
            float rear = -definition.wheelbase * definition.frontWeightBias;
            float radius = definition.tyreFront.radius;
            float drop = radius - definition.cgHeight;
            Vector3[] positions =
            {
                new Vector3(-halfTrack, drop, front), new Vector3(halfTrack, drop, front),
                new Vector3(-halfTrack, drop, rear), new Vector3(halfTrack, drop, rear),
            };
            string[] names = { "Wheel FL", "Wheel FR", "Wheel RL", "Wheel RR" };
            var pivots = new Transform[4];
            // Each wheel is an empty pivot: CarController sets its rotation to spin and steer
            // every frame, which on a bare cylinder wiped out the 90 degrees that lays it down.
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject(names[i]) { layer = layer };
                pivot.transform.SetParent(car, false);
                pivot.transform.localPosition = positions[i];
                float outward = Mathf.Sign(positions[i].x);

                Cylinder(pivot.transform, "Tyre", Vector3.zero, new Vector3(radius * 2f, TyreWidthM * 0.5f, radius * 2f), layer, tyre);
                float face = outward * (TyreWidthM * 0.5f + 0.004f);
                Cylinder(pivot.transform, "Rim", new Vector3(face, 0f, 0f), new Vector3(radius * 1.3f, 0.006f, radius * 1.3f), layer, dark);
                for (int spoke = 0; spoke < 5; spoke++)
                {
                    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(bar.GetComponent<Collider>());
                    bar.name = "Spoke";
                    bar.layer = layer;
                    bar.transform.SetParent(pivot.transform, false);
                    Quaternion turn = Quaternion.Euler(spoke * 72f, 0f, 0f);
                    bar.transform.localRotation = turn;
                    bar.transform.localPosition = new Vector3(face + outward * 0.008f, 0f, 0f) + turn * new Vector3(0f, radius * 0.33f, 0f);
                    bar.transform.localScale = new Vector3(0.012f, radius * 0.62f, 0.05f);
                    bar.GetComponent<MeshRenderer>().sharedMaterial = rim;
                }
                pivots[i] = pivot.transform;
            }
            return pivots;
        }

        static void Part(Transform car, string name, Mesh mesh, int layer, params Material[] materials)
        {
            var part = new GameObject(name) { layer = layer };
            part.transform.SetParent(car, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        static void Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, int layer, Material material)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            cylinder.name = name;
            cylinder.layer = layer;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = position;
            cylinder.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>Replaces the saved mesh's contents, so every car and scene shares one asset.</summary>
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            Directory.CreateDirectory(Folder);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, saved);
            EditorUtility.SetDirty(saved);
            return saved;
        }

        static Material Lit(string path, Color colour, float smoothness, float metallic)
        {
            var material = SkidpadSceneBuilder.EnsureMaterial(path, colour, null, Vector2.one);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Glow(string path, Color colour)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
