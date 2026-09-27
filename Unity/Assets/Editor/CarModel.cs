using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The car's looks: bodies built to the car's own dimensions in four designs the lobby
    /// offers (Designs): a GT coupe with a big
    /// wing, a muscle car with a long bonnet and a ducktail, a wedge supercar with its cabin
    /// forward, and a tall hot hatch with a roof spoiler. They replaced a box on four cylinders.
    /// The designs are looks only: wheels, wheelbase and handling are the same for all.
    ///
    /// The body is lofted through Stations, cross-sections from the rear bumper to the nose,
    /// each a half profile mirrored to both sides: the bottom edge, the widest point, the
    /// shoulder, then a top that blends from a flat deck (bonnet, boot) to the cabin (glass and
    /// roof) by Cabin, which gives each design its rear window and windscreen. Wheel
    /// arches are stations whose bottom edge rises over the tyre, placed from the wheelbase and
    /// the weight split so the wheels always sit in them.
    ///
    /// The stations are only the design. The surface is drawn smooth through them: every
    /// station's values eased along the car by monotone cubic interpolation (no overshoot, so
    /// an arch stays an arch) every 5 cm, each cross-section a Catmull-Rom curve through its
    /// points across both sides, and normals taken from the resulting grid, so the body
    /// shades as one continuous surface. The paint is URP's Complex Lit with a clear coat
    /// (MakePaint), a glossy layer over a slightly metallic base, as car paint is.
    ///
    /// Only the painted panels are on "Body", so the colour picker's property block and the AI
    /// cars' materials recolour the paint and nothing else. Glass, black trim (underbody,
    /// splitter, intake, exhausts, rear wing, mirrors) and the lights are on "Body Details".
    /// The wheels come from Blender (Tools/blender/wheel.py, which writes Assets/Art/Wheel): a
    /// tyre whose tread, shoulder blocks and sidewall lettering are a baked normal map, a
    /// ten-spoke alloy rim with ambient occlusion baked in, a vented disc and a caliper. Each
    /// wheel pivot, which CarController steers, holds the caliper and a "Wheel" child that it
    /// spins, so the caliper steers with the wheel but stays put as it rolls. The left wheels
    /// are the right ones turned half round, not mirrored, so their lettering still reads; only
    /// the caliper is mirrored, to stay behind the axle. None of it has a collider: the car's
    /// box does that.
    ///
    /// The GT is not generated: it is modelled in Blender (Tools/blender/car.py, which writes
    /// Assets/Art/Cars/GT.fbx), with an interior behind clear glass, shut lines, mirrors, LED
    /// headlights, exhausts and a swan-neck wing, and its details in six materials to the
    /// generated designs' four. Each design carries its own detail materials for that reason.
    ///
    /// Every design's meshes are saved under Assets/Cars (or imported from Assets/Art/Cars) and
    /// listed in the CarDesigns asset in Resources, which the game uses to put the chosen design
    /// on the player's car.
    /// </summary>
    public static class CarModel
    {
        const string Folder = "Assets/Cars";
        const string CatalogPath = "Assets/Resources/" + CarDesigns.ResourceName + ".asset";
        public static readonly string[] Designs = { "GT", "Muscle", "Supercar", "Hot Hatch" };
        public const int GT = 0, Muscle = 1, Supercar = 2, HotHatch = 3;
        const string GlassPath = "Assets/Materials/CarGlass.mat";
        const string TrimPath = "Assets/Materials/CarTrim.mat";
        const string RimPath = "Assets/Materials/CarRim.mat";
        const string DarkRimPath = "Assets/Materials/CarRimDark.mat";
        const string DiscPath = "Assets/Materials/CarBrakeDisc.mat";
        const string CaliperPath = "Assets/Materials/CarCaliper.mat";
        const string ClearGlassPath = "Assets/Materials/CarGlassClear.mat";
        const string InteriorPath = "Assets/Materials/CarInterior.mat";
        const string ChromePath = "Assets/Materials/CarChrome.mat";
        const string CarArt = "Assets/Art/Cars";
        const string WheelArt = "Assets/Art/Wheel";
        /// <summary>The tyre radius wheel.py builds to; other radii scale the wheel.</summary>
        const float WheelModelRadius = 0.34f;
        const string HeadlightPath = "Assets/Materials/CarHeadlight.mat";
        const string TaillightPath = "Assets/Materials/CarTaillight.mat";
        public const float PaintSmoothness = 0.55f;

        /// <summary>One cross-section, in metres above the road and out from the centreline.</summary>
        struct Station
        {
            public float Z, Bottom, BeltX, BeltY, ShoulderX, ShoulderY, Cabin, Roof;
            public Station(float z, float bottom, float beltX, float beltY, float shoulderX, float shoulderY)
            {
                Z = z; Bottom = bottom; BeltX = beltX; BeltY = beltY; ShoulderX = shoulderX; ShoulderY = shoulderY; Cabin = 0f; Roof = 0f;
            }
        }

        /// <summary>A design's cabin along the car: the rear window rises from RearFrom to the
        /// roof at RoofFrom, the roof runs to RoofTo, the windscreen falls to the scuttle at
        /// ScreenTo; Roof is its height, Crown how far the bonnet's middle stands above the
        /// wings (below them for the supercar's scooped bonnet).</summary>
        struct Cabin
        {
            public float RearFrom, RoofFrom, RoofTo, ScreenTo, Roof, Crown;
        }

        static Cabin CabinOf(int design)
        {
            switch (design)
            {
                case Muscle: return new Cabin { RearFrom = -1.05f, RoofFrom = -0.75f, RoofTo = 0.0f, ScreenTo = 0.62f, Roof = 1.28f, Crown = 0.07f };
                case Supercar: return new Cabin { RearFrom = -1.55f, RoofFrom = -0.3f, RoofTo = 0.35f, ScreenTo = 1.1f, Roof = 1.08f, Crown = -0.03f };
                case HotHatch: return new Cabin { RearFrom = -1.93f, RoofFrom = -1.72f, RoofTo = 0.2f, ScreenTo = 0.85f, Roof = 1.38f, Crown = 0.07f };
                default: throw new System.ArgumentOutOfRangeException(nameof(design), "The GT is modelled in Blender (Tools/blender/car.py), not generated.");
            }
        }

        /// <summary>A design, rear to front, around wheels at rearAxle and frontAxle, whose
        /// arches reach archHalf either side of the axle and archTop above the road.</summary>
        static List<Station> Stations(int design, float rearAxle, float frontAxle, float archHalf, float archTop)
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
            var s = new List<Station>();
            switch (design)
            {
                case Muscle:   // long, square, a high bonnet and a blunt nose
                    s.Add(new Station(-2.35f, 0.22f, 0.90f, 0.50f, 0.86f, 0.82f));
                    s.Add(new Station(-2.25f, 0.16f, 0.96f, 0.54f, 0.92f, 0.86f));
                    Arch(s, rearAxle, archTop + 0.08f, 0.92f);
                    s.Add(new Station(-0.50f, 0.16f, 0.97f, 0.54f, 0.93f, 0.92f));
                    s.Add(new Station(0.00f, 0.16f, 0.97f, 0.54f, 0.93f, 0.91f));
                    s.Add(new Station(0.60f, 0.16f, 0.97f, 0.54f, 0.93f, 0.90f));
                    Arch(s, frontAxle, archTop + 0.08f, 0.90f);
                    s.Add(new Station(2.20f, 0.16f, 0.95f, 0.52f, 0.90f, 0.86f));
                    s.Add(new Station(2.40f, 0.20f, 0.90f, 0.50f, 0.86f, 0.82f));
                    break;
                case Supercar:   // a wedge: high tail, low sharp nose
                    s.Add(new Station(-2.30f, 0.26f, 0.90f, 0.52f, 0.86f, 0.84f));
                    s.Add(new Station(-2.18f, 0.16f, 0.97f, 0.56f, 0.93f, 0.88f));
                    Arch(s, rearAxle, archTop + 0.08f, 0.90f);
                    s.Add(new Station(-0.40f, 0.16f, 0.96f, 0.50f, 0.90f, 0.80f));
                    s.Add(new Station(0.30f, 0.16f, 0.95f, 0.48f, 0.88f, 0.76f));
                    Arch(s, frontAxle, archTop + 0.03f, 0.79f);
                    s.Add(new Station(2.00f, 0.13f, 0.93f, 0.44f, 0.86f, 0.64f));   // the bonnet falls in two steps
                    s.Add(new Station(2.16f, 0.12f, 0.88f, 0.34f, 0.80f, 0.50f));
                    s.Add(new Station(2.36f, 0.12f, 0.76f, 0.26f, 0.68f, 0.38f));
                    break;
                case HotHatch:   // short tail, tall roof, a hatch almost upright
                    s.Add(new Station(-1.95f, 0.24f, 0.90f, 0.50f, 0.86f, 0.86f));
                    s.Add(new Station(-1.88f, 0.16f, 0.96f, 0.54f, 0.92f, 0.90f));
                    Arch(s, rearAxle, archTop + 0.08f, 0.93f);
                    s.Add(new Station(-0.50f, 0.16f, 0.96f, 0.54f, 0.92f, 0.92f));
                    s.Add(new Station(0.20f, 0.16f, 0.96f, 0.54f, 0.92f, 0.90f));
                    Arch(s, frontAxle, archTop + 0.06f, 0.86f);
                    s.Add(new Station(2.00f, 0.16f, 0.94f, 0.50f, 0.88f, 0.78f));
                    s.Add(new Station(2.12f, 0.20f, 0.86f, 0.44f, 0.80f, 0.70f));
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(design), "The GT is modelled in Blender (Tools/blender/car.py), not generated.");
            }
            s.Sort((a, b) => a.Z.CompareTo(b.Z));

            Cabin cabin = CabinOf(design);
            for (int i = 0; i < s.Count; i++)
            {
                Station st = s[i];
                st.Cabin = Mathf.Min(Mathf.InverseLerp(cabin.RearFrom, cabin.RoofFrom, st.Z), Mathf.InverseLerp(cabin.ScreenTo, cabin.RoofTo, st.Z));
                st.Roof = cabin.Roof;
                s[i] = st;
            }
            return s;
        }

        /// <summary>The station's right half profile, bottom to centre top, in road heights.</summary>
        static Vector2[] Profile(Station st, float crown)
        {
            float c = st.Cabin;
            Vector2 Blend(Vector2 deck, Vector2 cabin) => Vector2.Lerp(deck, cabin, c);
            float sx = st.ShoulderX, sy = st.ShoulderY, k = crown / 0.07f;
            return new[]
            {
                new Vector2(st.BeltX * 0.93f, st.Bottom),
                new Vector2(st.BeltX, st.BeltY),
                new Vector2(sx, sy),
                Blend(new Vector2(sx * 0.8f, sy + 0.03f * k), new Vector2(sx * 0.86f, sy + 0.04f)),
                Blend(new Vector2(sx * 0.5f, sy + 0.06f * k), new Vector2(0.64f, st.Roof - 0.06f)),
                Blend(new Vector2(sx * 0.25f, sy + 0.07f * k), new Vector2(0.40f, st.Roof)),
                Blend(new Vector2(0f, sy + 0.07f * k), new Vector2(0f, st.Roof + 0.01f)),
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

            /// <summary>A triangle with its own vertex normals, wound to face the way they do.</summary>
            public void Smooth(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, na + nb + nc) < 0f) { (b, c) = (c, b); (nb, nc) = (nc, nb); }
                int i = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _normals.Add(na); _normals.Add(nb); _normals.Add(nc);
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
        static (Mesh body, Mesh details) Meshes(CarDefinition definition, int design)
        {
            float front = definition.wheelbase * (1f - definition.frontWeightBias);
            float rear = -definition.wheelbase * definition.frontWeightBias;
            float radius = definition.tyreFront.radius;
            List<Station> stations = Stations(design, rear, front, radius + 0.08f, radius * 2f + 0.05f);
            Cabin cabin = CabinOf(design);
            float down = definition.cgHeight;
            Vector3 At(Vector2 p, float z, float side) => new Vector3(p.x * side, p.y - down, z);

            var body = new Builder(1);
            var details = new Builder(4);
            Surface(stations, cabin, down, body, details);
            // Front splitter, diffuser lip, mirrors, and the design's wing or spoiler.
            float y(float road) => road - down;
            Station nose = stations[stations.Count - 1], tail = stations[0];
            float ShoulderAt(float z)
            {
                for (int i = 0; i + 1 < stations.Count; i++)
                    if (z <= stations[i + 1].Z)
                        return Mathf.Lerp(stations[i].ShoulderY, stations[i + 1].ShoulderY, Mathf.InverseLerp(stations[i].Z, stations[i + 1].Z, z));
                return nose.ShoulderY;
            }
            details.Box(Trim, new Vector3(0f, y(0.12f), nose.Z - 0.07f), new Vector3(1.78f, 0.04f, 0.34f), Quaternion.identity);
            details.Box(Trim, new Vector3(0f, y(0.2f), tail.Z + 0.05f), new Vector3(1.5f, 0.12f, 0.12f), Quaternion.identity);
            float mirrorZ = Mathf.Min(cabin.ScreenTo - 0.35f, 0.6f);
            foreach (float side in new[] { -1f, 1f })
                details.Box(Trim, new Vector3(0.99f * side, y(ShoulderAt(mirrorZ) + 0.1f), mirrorZ), new Vector3(0.14f, 0.09f, 0.1f), Quaternion.identity);
            switch (design)
            {
                case Muscle:   // a ducktail, in the body's paint
                    body.Box(0, new Vector3(0f, y(tail.ShoulderY + 0.05f), tail.Z + 0.14f), new Vector3(1.56f, 0.05f, 0.3f), Quaternion.Euler(-14f, 0f, 0f));
                    break;
                case Supercar:   // a low wing close to the engine cover
                    float deck = ShoulderAt(tail.Z + 0.3f);
                    foreach (float side in new[] { -1f, 1f })
                        details.Box(Trim, new Vector3(0.5f * side, y(deck + 0.08f), tail.Z + 0.3f), new Vector3(0.05f, 0.16f, 0.14f), Quaternion.identity);
                    details.Box(Trim, new Vector3(0f, y(deck + 0.17f), tail.Z + 0.28f), new Vector3(1.62f, 0.035f, 0.28f), Quaternion.Euler(-8f, 0f, 0f));
                    break;
                case HotHatch:   // a spoiler over the hatch, off the back of the roof
                    details.Box(Trim, new Vector3(0f, y(cabin.Roof - 0.01f), cabin.RoofFrom - 0.08f), new Vector3(1.24f, 0.04f, 0.3f), Quaternion.Euler(10f, 0f, 0f));
                    break;
            }

            // Lights: headlights set into the nose with a daytime-running strip above each,
            // tail lamps and an LED bar across the tail.
            foreach (float side in new[] { -1f, 1f })
            {
                details.Box(Headlight, new Vector3(0.52f * side, y(nose.ShoulderY - 0.1f), nose.Z - 0.12f), new Vector3(0.34f, 0.07f, 0.3f), Quaternion.Euler(-18f, 0f, 0f));
                details.Box(Headlight, new Vector3(0.55f * side, y(nose.ShoulderY - 0.04f), nose.Z - 0.2f), new Vector3(0.3f, 0.015f, 0.2f), Quaternion.Euler(-18f, 0f, 0f));
                details.Box(Taillight, new Vector3(0.58f * side, y(tail.ShoulderY - 0.1f), tail.Z - 0.005f), new Vector3(0.36f, 0.07f, 0.03f), Quaternion.identity);
            }
            details.Box(Taillight, new Vector3(0f, y(tail.ShoulderY - 0.05f), tail.Z - 0.004f), new Vector3(1.3f, 0.018f, 0.02f), Quaternion.identity);

            // The front intake, dark, low in the nose; twin exhaust tips under the tail.
            details.Box(Trim, new Vector3(0f, y(nose.Bottom + 0.14f), nose.Z - 0.04f), new Vector3(1.1f, 0.16f, 0.1f), Quaternion.identity);
            foreach (float side in new[] { -1f, 1f })
                Tube(details, Trim, new Vector3(0.42f * side, y(tail.Bottom + 0.12f), tail.Z - 0.02f), 0.045f, 0.12f);
            return (body.Build($"Car Body {Designs[design]}"), details.Build($"Car Details {Designs[design]}"));
        }

        const float StepZ = 0.05f;
        const int CurveSteps = 4;

        /// <summary>The painted body and its glass, smooth: the design's stations eased along
        /// the car every StepZ, each cross-section a Catmull-Rom curve through its points from
        /// the left sill over the top to the right sill, and the normals from the grid. Glass is
        /// where the old faces were: the side window (profile segment 3) wherever the cabin is
        /// full height, the roof segments where the cabin blends into the deck (windscreen and
        /// rear window). Underside strips and end caps close it.</summary>
        static void Surface(List<Station> stations, Cabin cabin, float down, Builder body, Builder details)
        {
            float z0 = stations[0].Z, z1 = stations[stations.Count - 1].Z;
            int rows = Mathf.CeilToInt((z1 - z0) / StepZ) + 1;
            float[] zs = stations.ConvertAll(st => st.Z).ToArray();
            float[] Field(System.Func<Station, float> f) => stations.ConvertAll(st => f(st)).ToArray();
            float[] bottom = Field(st => st.Bottom), beltX = Field(st => st.BeltX), beltY = Field(st => st.BeltY);
            float[] shoulderX = Field(st => st.ShoulderX), shoulderY = Field(st => st.ShoulderY);

            var grid = new List<Vector3[]>();
            var segmentOf = new List<int>();
            var cabinAt = new float[rows];
            for (int r = 0; r < rows; r++)
            {
                float z = Mathf.Min(z0 + r * StepZ, z1);
                var st = new Station(z, Pchip(zs, bottom, z), Pchip(zs, beltX, z), Pchip(zs, beltY, z), Pchip(zs, shoulderX, z), Pchip(zs, shoulderY, z));
                st.Cabin = Mathf.Min(Mathf.InverseLerp(cabin.RearFrom, cabin.RoofFrom, z), Mathf.InverseLerp(cabin.ScreenTo, cabin.RoofTo, z));
                st.Roof = cabin.Roof;
                cabinAt[r] = st.Cabin;
                Vector2[] half = Profile(st, cabin.Crown);
                var across = new List<Vector2>();
                for (int k = 0; k < half.Length - 1; k++) across.Add(new Vector2(-half[k].x, half[k].y));
                for (int k = half.Length - 1; k >= 0; k--) across.Add(half[k]);
                var row = new List<Vector3>();
                segmentOf.Clear();
                for (int k = 0; k + 1 < across.Count; k++)
                    for (int t = 0; t < CurveSteps; t++)
                    {
                        Vector2 p = CatmullRom(across, k, t / (float)CurveSteps);
                        row.Add(new Vector3(p.x, p.y - down, z));
                        segmentOf.Add(k < half.Length - 1 ? k : 2 * (half.Length - 1) - 1 - k);
                    }
                Vector2 last = across[across.Count - 1];
                row.Add(new Vector3(last.x, last.y - down, z));
                grid.Add(row.ToArray());
            }

            int columns = grid[0].Length;
            var normals = new Vector3[rows, columns];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    Vector3 along = grid[Mathf.Min(r + 1, rows - 1)][c] - grid[Mathf.Max(r - 1, 0)][c];
                    Vector3 around = grid[r][Mathf.Min(c + 1, columns - 1)] - grid[r][Mathf.Max(c - 1, 0)];
                    Vector3 n = Vector3.Cross(around, along).normalized;
                    Vector3 centre = new Vector3(0f, (grid[r][0].y + grid[r][columns / 2].y) * 0.5f, grid[r][c].z);
                    if (Vector3.Dot(n, grid[r][c] - centre) < 0f) n = -n;
                    normals[r, c] = n;
                }

            for (int r = 0; r + 1 < rows; r++)
            {
                bool inCabin = Mathf.Min(cabinAt[r], cabinAt[r + 1]) >= 0.5f;
                bool screen = Mathf.Max(cabinAt[r], cabinAt[r + 1]) > 0.3f && Mathf.Min(cabinAt[r], cabinAt[r + 1]) < 0.95f;
                for (int c = 0; c + 1 < columns; c++)
                {
                    int k = segmentOf[c];
                    bool glass = (k == 3 && inCabin) || (k >= 4 && screen);
                    Vector3 a = grid[r][c], b = grid[r][c + 1], cc = grid[r + 1][c + 1], d = grid[r + 1][c];
                    Vector3 na = normals[r, c], nb = normals[r, c + 1], nc = normals[r + 1, c + 1], nd = normals[r + 1, c];
                    if (glass)
                    {
                        details.Smooth(Glass, a, b, cc, na, nb, nc);
                        details.Smooth(Glass, a, cc, d, na, nc, nd);
                    }
                    else
                    {
                        body.Smooth(0, a, b, cc, na, nb, nc);
                        body.Smooth(0, a, cc, d, na, nc, nd);
                    }
                }
                // The underside, black, and over the wheels the arch's roof.
                Vector3 inside = new Vector3(0f, grid[r][columns / 2].y, grid[r][0].z);
                details.Quad(Trim, grid[r][0], grid[r][columns - 1], grid[r + 1][columns - 1], grid[r + 1][0], inside);
            }

            // Nose and tail, closed with a fan from the middle of the end section.
            foreach (int r in new[] { 0, rows - 1 })
            {
                Vector3[] row = grid[r];
                Vector3 middle = Vector3.zero;
                foreach (Vector3 v in row) middle += v;
                middle /= row.Length;
                Vector3 outward = new Vector3(0f, 0f, r == 0 ? -1f : 1f);
                for (int c = 0; c + 1 < row.Length; c++) body.Smooth(0, middle, row[c], row[c + 1], outward, outward, outward);
                body.Smooth(0, middle, row[row.Length - 1], row[0], outward, outward, outward);
            }
        }

        /// <summary>Monotone cubic interpolation (Fritsch-Carlson): smooth, and never beyond
        /// its neighbours, so a wheel arch or the nose cannot bulge past the design.</summary>
        static float Pchip(float[] xs, float[] ys, float x)
        {
            int n = xs.Length;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            int i = 0;
            while (i < n - 2 && x > xs[i + 1]) i++;
            float Slope(int j) => (ys[j + 1] - ys[j]) / Mathf.Max(xs[j + 1] - xs[j], 1e-5f);
            float Tangent(int j)
            {
                if (j == 0) return Slope(0);
                if (j == n - 1) return Slope(n - 2);
                float a = Slope(j - 1), b = Slope(j);
                return a * b <= 0f ? 0f : 2f / (1f / a + 1f / b);
            }
            float h = xs[i + 1] - xs[i], t = (x - xs[i]) / Mathf.Max(h, 1e-5f);
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * ys[i] + (t3 - 2f * t2 + t) * h * Tangent(i)
                 + (-2f * t3 + 3f * t2) * ys[i + 1] + (t3 - t2) * h * Tangent(i + 1);
        }

        /// <summary>A point t of the way from points[k] to points[k + 1] on a Catmull-Rom
        /// curve through them all, the ends held.</summary>
        static Vector2 CatmullRom(List<Vector2> points, int k, float t)
        {
            Vector2 p0 = points[Mathf.Max(k - 1, 0)], p1 = points[k], p2 = points[k + 1], p3 = points[Mathf.Min(k + 2, points.Count - 1)];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>A short tube along the car, open at the back, for an exhaust tip.</summary>
        static void Tube(Builder builder, int submesh, Vector3 centre, float radius, float length)
        {
            const int Sides = 14;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                var n0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                var n1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                Vector3 back = centre - Vector3.forward * (length * 0.5f), front = centre + Vector3.forward * (length * 0.5f);
                builder.Smooth(submesh, back + n0 * radius, front + n0 * radius, front + n1 * radius, n0, n0, n1);
                builder.Smooth(submesh, back + n0 * radius, front + n1 * radius, back + n1 * radius, n0, n1, n1);
                builder.Smooth(submesh, back + n0 * radius * 0.8f, back + n1 * radius * 0.8f, back + n1 * radius, -Vector3.forward, -Vector3.forward, -Vector3.forward);
            }
        }

        /// <summary>Every design's meshes, saved, and the catalogue the game reads them from.
        /// Made once per editor session: every car in every scene shares them.</summary>
        static CarDesigns EnsureDesigns(CarDefinition definition)
        {
            if (_designs != null) return _designs;
            var catalog = AssetDatabase.LoadAssetAtPath<CarDesigns>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<CarDesigns>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.designs.Clear();
            for (int d = 0; d < Designs.Length; d++)
            {
                if (d == GT)
                {
                    var (body, details) = BlenderDesign(definition, Designs[d]);
                    catalog.designs.Add(new CarDesigns.Design
                    {
                        name = Designs[d], body = body, details = details,
                        detailMaterials = new[]
                        {
                            ClearGlass(), Lit(TrimPath, new Color(0.035f, 0.035f, 0.04f), 0.35f, 0f),
                            Glow(HeadlightPath, new Color(1f, 0.97f, 0.9f) * 2.2f), Glow(TaillightPath, new Color(1.3f, 0.02f, 0.02f)),
                            Lit(InteriorPath, new Color(0.075f, 0.075f, 0.08f), 0.25f, 0f),
                            Lit(ChromePath, new Color(0.85f, 0.85f, 0.87f), 0.9f, 1f),
                        },
                    });
                    continue;
                }
                var (generatedBody, generatedDetails) = Meshes(definition, d);
                catalog.designs.Add(new CarDesigns.Design
                {
                    name = Designs[d],
                    body = SaveMesh(generatedBody, $"{Folder}/CarBody {Designs[d]}.asset"),
                    details = SaveMesh(generatedDetails, $"{Folder}/CarDetails {Designs[d]}.asset"),
                    detailMaterials = new[]
                    {
                        Lit(GlassPath, new Color(0.06f, 0.08f, 0.11f), 0.92f, 0f),
                        Lit(TrimPath, new Color(0.035f, 0.035f, 0.04f), 0.35f, 0f),
                        Glow(HeadlightPath, new Color(1f, 0.97f, 0.9f) * 2.2f),
                        Glow(TaillightPath, new Color(1.3f, 0.02f, 0.02f)),   // brighter and the bloom turns it orange
                    },
                });
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return _designs = catalog;
        }

        static CarDesigns _designs;

        /// <summary>Builds <paramref name="design"/>'s looks onto <paramref name="car"/> and
        /// returns the four wheel pivots, FL FR RL RR, for CarController to spin and steer.</summary>
        public static Transform[] Build(Transform car, CarDefinition definition, int layer, Material paint, int design = GT)
        {
            CarDesigns catalog = EnsureDesigns(definition);
            Mesh bodyMesh = catalog.designs[design].body, detailsMesh = catalog.designs[design].details;

            MakePaint(paint);
            Part(car, "Body", bodyMesh, layer, paint);
            Part(car, "Body Details", detailsMesh, layer, catalog.designs[design].detailMaterials);

            Material tyre = Lit(SkidpadSceneBuilder.TyreMaterialPath, new Color(0.055f, 0.055f, 0.06f), 0.28f, 0f);
            Textured(tyre, "_BumpMap", "_NORMALMAP", "Tyre Normal.png", normalMap: true);
            bool darkRims = design == Supercar || design == HotHatch;
            Material rim = darkRims ? Lit(DarkRimPath, new Color(0.16f, 0.16f, 0.17f), 0.6f, 0.9f)
                                    : Lit(RimPath, new Color(0.8f, 0.81f, 0.83f), 0.78f, 1f);
            Textured(rim, "_OcclusionMap", "_OCCLUSIONMAP", "Rim AO.png", normalMap: false);
            Material disc = Lit(DiscPath, new Color(0.42f, 0.42f, 0.43f), 0.5f, 0.9f);
            Material caliper = Lit(CaliperPath, new Color(0.62f, 0.03f, 0.02f), 0.72f, 0f);
            var wheels = EnsureWheels();
            float scale = definition.tyreFront.radius / WheelModelRadius;

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
            // Each wheel is an empty pivot that CarController steers, with the spinning "Wheel"
            // as its first child (CarController.TurnWheel relies on that order) and the caliper,
            // which does not spin, beside it.
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject(names[i]) { layer = layer };
                pivot.transform.SetParent(car, false);
                pivot.transform.localPosition = positions[i];
                float outward = Mathf.Sign(positions[i].x);

                // Built for the right side. The left wheels are turned half round rather than
                // mirrored, which would print the sidewall lettering backwards.
                var wheel = new GameObject("Wheel") { layer = layer };
                wheel.transform.SetParent(pivot.transform, false);
                wheel.transform.localScale = Vector3.one * scale;
                Quaternion side = outward > 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                Part(wheel.transform, "Tyre", wheels.tyre, layer, tyre).localRotation = side;
                Part(wheel.transform, "Rim", wheels.rim, layer, rim).localRotation = side;
                Part(wheel.transform, "Brake Disc", wheels.disc, layer, disc).localRotation = side;

                // Mirrored, so it stays behind the axle on both sides.
                Transform clamp = Part(pivot.transform, "Caliper", wheels.caliper, layer, caliper);
                clamp.localScale = new Vector3(outward, 1f, 1f) * scale;
                pivots[i] = pivot.transform;
            }
            return pivots;
        }

        static Transform Part(Transform car, string name, Mesh mesh, int layer, params Material[] materials)
        {
            var part = new GameObject(name) { layer = layer };
            part.transform.SetParent(car, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return part.transform;
        }

        static (Mesh tyre, Mesh rim, Mesh disc, Mesh caliper) _wheels;

        /// <summary>The wheel's meshes from Wheel.fbx, for the right side (outward +X). Checks
        /// the import put them the right way round: Blender to Unity changes both the up axis
        /// and the handedness, and a wrong setting in either shows as a rim facing inward or a
        /// caliper ahead of the axle rather than as an error.</summary>
        static (Mesh tyre, Mesh rim, Mesh disc, Mesh caliper) EnsureWheels()
        {
            if (_wheels.tyre != null) return _wheels;
            string path = WheelArt + "/Wheel.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"{path} is missing: run Tools/blender/wheel.py (its header has the command).");
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.isReadable)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;   // CarModel's own materials
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            Mesh Find(string name)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Mesh mesh && mesh.name == name) return mesh;
                throw new System.InvalidOperationException($"{path} has no mesh called {name}.");
            }
            _wheels = (Find("Tyre"), Find("Rim"), Find("Disc"), Find("Caliper"));

            Bounds tyre = _wheels.tyre.bounds, rim = _wheels.rim.bounds, caliper = _wheels.caliper.bounds;
            if (Mathf.Abs(tyre.extents.y - WheelModelRadius) > 0.01f || Mathf.Abs(tyre.extents.z - WheelModelRadius) > 0.01f)
                throw new System.InvalidOperationException($"Wheel.fbx tyre is {tyre.extents}, not {WheelModelRadius} m round the X axis: the import scale or axes are wrong.");
            if (rim.max.x < -rim.min.x)
                throw new System.InvalidOperationException("Wheel.fbx rim faces inward: the import flipped X.");
            if (caliper.center.z > 0f || caliper.center.y < 0f)
                throw new System.InvalidOperationException($"Wheel.fbx caliper is at {caliper.center}, not behind and above the hub.");
            return _wheels;
        }

        /// <summary>A design's Body and Details meshes from its FBX, which car.py builds round the
        /// default car: checked against the definition, since a different wheelbase or track
        /// would leave the wheels outside their arches, and against the import, whose axes can
        /// go wrong without an error (headlights must end up in front, taillights behind).</summary>
        static (Mesh body, Mesh details) BlenderDesign(CarDefinition definition, string name)
        {
            if (Mathf.Abs(definition.wheelbase - 2.65f) > 0.005f || Mathf.Abs(definition.frontWeightBias - 0.48f) > 0.005f
                || Mathf.Abs(definition.trackWidth - 1.6f) > 0.005f || Mathf.Abs(definition.cgHeight - 0.45f) > 0.005f)
                throw new System.InvalidOperationException(
                    $"The {name} body is built for a 2.65 m wheelbase, 0.48 front bias, 1.6 m track and 0.45 m CG: change KEYS and the constants in Tools/blender/car.py to match the Car Definition and rerun it.");
            string path = $"{CarArt}/{name}.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"{path} is missing: run Tools/blender/car.py (its header has the command).");
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.isReadable)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            Mesh body = null, details = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Mesh mesh)
                {
                    if (mesh.name == "Body") body = mesh;
                    if (mesh.name == "Details") details = mesh;
                }
            if (body == null || details == null)
                throw new System.InvalidOperationException($"{path} needs meshes called Body and Details.");
            if (body.subMeshCount != 1 || details.subMeshCount != 6)
                throw new System.InvalidOperationException($"{path}: Body has {body.subMeshCount} materials (want 1), Details {details.subMeshCount} (want 6).");
            float headlights = details.GetSubMesh(2).bounds.center.z, taillights = details.GetSubMesh(3).bounds.center.z;
            if (headlights < 1.5f || taillights > -1.8f)
                throw new System.InvalidOperationException($"{path}: headlights at z {headlights:0.00} and taillights at {taillights:0.00}; the import turned the car round.");
            if (Mathf.Abs(body.bounds.center.x) > 0.05f || body.bounds.size.z < 4.2f)
                throw new System.InvalidOperationException($"{path}: body bounds {body.bounds} are not a car centred on its axis.");
            return (body, details);
        }

        /// <summary>Glass you can see into: dark tinted and transparent, premultiplied so its
        /// reflections stay bright over the cabin behind it.</summary>
        static Material ClearGlass()
        {
            Material glass = Lit(ClearGlassPath, new Color(0.04f, 0.05f, 0.06f, 0.55f), 0.95f, 0f);
            glass.SetFloat("_Surface", 1f);
            glass.SetFloat("_Blend", 1f);
            glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            glass.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite", 0f);
            glass.SetOverrideTag("RenderType", "Transparent");
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(glass);
            return glass;
        }

        /// <summary>Puts one of wheel.py's baked textures on a material, marking a normal map as
        /// one and anything else as data rather than colour.</summary>
        static void Textured(Material material, string property, string keyword, string file, bool normalMap)
        {
            string path = $"{WheelArt}/{file}";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"{path} is missing: run Tools/blender/wheel.py.");
            var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture)
            {
                importer.textureType = type;
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            material.EnableKeyword(keyword);
            EditorUtility.SetDirty(material);
        }

        /// <summary>The four designs on a stretch of road under the circuits' sky, from the
        /// front three quarters and the rear, written to Builds/cars.png. -executeMethod
        /// CarRace.UnityGame.EditorTools.CarModel.Preview</summary>
        public static void Preview()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            CarDefinition definition = SkidpadSceneBuilder.EnsureDefinition();
            _designs = null;
            _wheels = default;
            RenderSettings.skybox = GraphicsSetup.EnsureSkyMaterial();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.44f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(0.5543f, 0.7416f, -0.3778f));
            var road = GameObject.CreatePrimitive(PrimitiveType.Plane);
            road.transform.localScale = new Vector3(10f, 1f, 10f);
            road.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Road.mat");
            DynamicGI.UpdateEnvironment();

            Color[] paints = { new Color(0.75f, 0.05f, 0.05f), new Color(0.05f, 0.18f, 0.55f), new Color(0.95f, 0.72f, 0.05f), new Color(0.9f, 0.9f, 0.9f) };
            for (int d = 0; d < Designs.Length; d++)
            {
                var car = new GameObject(Designs[d]);
                var paint = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = paints[d] };
                paint.SetColor("_BaseColor", paints[d]);
                Build(car.transform, definition, 0, paint, d);
                car.transform.position = new Vector3(-6.6f + d * 4.4f, definition.cgHeight, 0f);
                car.transform.rotation = Quaternion.Euler(0f, d % 2 == 0 ? 150f : 30f, 0f);
            }
            var probe = new GameObject("Reflections").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0f, 1.5f, 0f);
            probe.size = new Vector3(60f, 20f, 60f);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.RenderProbe();

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1.8f, -7f);
            camera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.Skybox;
            var target = new RenderTexture(2400, 1000, 24) { antiAliasing = 8 };
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes("Builds/cars.png", image.EncodeToPNG());
            Debug.Log("Car preview written to Builds/cars.png.");
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

        /// <summary>Turns a body material into car paint: Complex Lit's clear coat, a glossy
        /// layer with its own reflections over a base with a little metallic flake. The colour
        /// stays in _BaseColor, where the lobby and the AI liveries set it.</summary>
        public static void MakePaint(Material paint)
        {
            Shader complex = Shader.Find("Universal Render Pipeline/Complex Lit");
            if (complex != null && paint.shader != complex) paint.shader = complex;
            paint.SetFloat("_Smoothness", PaintSmoothness);
            paint.SetFloat("_Metallic", 0.3f);
            paint.SetFloat("_ClearCoat", 1f);
            paint.SetFloat("_ClearCoatMask", 1f);
            paint.SetFloat("_ClearCoatSmoothness", 0.94f);
            paint.EnableKeyword("_CLEARCOAT");
            EditorUtility.SetDirty(paint);
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
