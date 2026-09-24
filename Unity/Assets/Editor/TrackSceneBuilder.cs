using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Builds a drivable scene from one of the pipeline's circuits: CarRace, Build Track Scene.
    ///
    /// Reads the track JSON from Assets/Tracks (copied from the repo's Tracks_Data) and makes
    /// the road from the centreline and its widths, a grass verge either side, the racing line
    /// painted on the road, and the car on the line at the start of the lap, pointing along it.
    /// The road follows the file's elevation, so Spa climbs its 105 m. Camber and banking ship
    /// as zero in every file, so the road is flat across; that is the data, not a shortcut.
    ///
    /// The file maps ENU to Unity as (x, y, z) to (x, z, y), the same swap the harness's
    /// TrackLoader makes, so the racing line here is the one the AI drives headlessly.
    /// Elevation is shifted so the lowest point of the lap sits at y = 0.
    ///
    /// Grip comes from the collider's physics material, which the wheels read: asphalt at 1,
    /// grass at 0.35, the value UnityGround documents. A frictionless wall runs along the
    /// outside of each verge, so the car cannot leave the circuit; R respawns it on the grid.
    /// </summary>
    public static class TrackSceneBuilder
    {
        const string TracksFolder = "Assets/Tracks";
        const string GrassPhysicsPath = "Assets/Physics/Grass.asset";
        const string RoadMaterialPath = "Assets/Materials/Road.mat";
        const string GrassMaterialPath = "Assets/Materials/Grass.mat";
        const string LineMaterialPath = "Assets/Materials/RacingLine.mat";
        const string BarrierMaterialPath = "Assets/Materials/Barrier.mat";
        const string BarrierPhysicsPath = "Assets/Physics/Barrier.asset";
        const string BarrierLayerName = "Barrier";
        const float BarrierHeightM = 1.2f;
        const float BarrierFootM = 0.5f;
        const float VergeWidthM = 15f;
        const float LineWidthM = 0.35f;

        [MenuItem("CarRace/Build Track Scene/Test Circuit")] static void TestCircuit() => Build("testcircuit");
        [MenuItem("CarRace/Build Track Scene/Monza")] static void Monza() => Build("monza");
        [MenuItem("CarRace/Build Track Scene/Spa")] static void Spa() => Build("spa");
        [MenuItem("CarRace/Build Track Scene/Silverstone")] static void Silverstone() => Build("silverstone");
        [MenuItem("CarRace/Build Track Scene/Suzuka")] static void Suzuka() => Build("suzuka");
        [MenuItem("CarRace/Build Track Scene/Bahrain")] static void Bahrain() => Build("bahrain");

        /// <summary>For a headless check: -executeMethod
        /// CarRace.UnityGame.EditorTools.TrackSceneBuilder.BuildFromCommandLine -track monza</summary>
        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-track");
            Build(i >= 0 && i + 1 < args.Length ? args[i + 1] : "testcircuit");
            AssetDatabase.SaveAssets();
        }

        /// <summary>The headless reference driver's lap on each circuit, pace 0.85, from
        /// `--lap all` on 2026-09-24. On flat ground, so a guide rather than a par here.</summary>
        static readonly Dictionary<string, float> ReferenceLaps = new Dictionary<string, float>
        {
            ["bahrain"] = 171.071f, ["monza"] = 154.490f, ["silverstone"] = 183.688f,
            ["spa"] = 202.576f, ["suzuka"] = 175.725f, ["testcircuit"] = 65.336f,
        };

        [Serializable] class Polyline { public float[] x, y, z, width_left, width_right; }
        [Serializable] class TrackFile { public string name; public float sample_spacing_m; public Polyline centerline, racing_line; }

        public static void Build(string circuit)
        {
            string jsonPath = $"{TracksFolder}/{circuit}.json";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (json == null)
                throw new FileNotFoundException(
                    $"{jsonPath} not found. Copy the repo's Tracks_Data/*.json into {TracksFolder}; see Unity/README.md.");
            var track = JsonUtility.FromJson<TrackFile>(json.text);
            Check(track, circuit);

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int carLayer = SkidpadSceneBuilder.EnsureLayer(SkidpadSceneBuilder.CarLayerName);
            int barrierLayer = SkidpadSceneBuilder.EnsureLayer(BarrierLayerName);
            SkidpadSceneBuilder.EnsureTriggerAxes();

            // Scene first, assets after: see SkidpadSceneBuilder.Build for why the order matters.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SkidpadSceneBuilder.EnsureAsphalt();
            SkidpadSceneBuilder.EnsureDefinition();
            EnsureGrassPhysics();
            EnsureBarrierPhysics();
            AssetDatabase.SaveAssets();
            var asphalt = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(SkidpadSceneBuilder.AsphaltPath);
            var grass = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(GrassPhysicsPath);
            var barrierSurface = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BarrierPhysicsPath);
            var definition = AssetDatabase.LoadAssetAtPath<CarDefinition>(SkidpadSceneBuilder.DefinitionPath);

            float floor = Min(track.centerline.z);
            Vector3[] centre = Points(track.centerline, floor);
            Vector3[] line = Points(track.racing_line, floor);
            Vector3[] right = RightOf(centre);

            var root = new GameObject(track.name);
            var roadMaterial = SkidpadSceneBuilder.EnsureMaterial(RoadMaterialPath, new Color(0.22f, 0.22f, 0.24f), null, Vector2.one);
            var grassMaterial = SkidpadSceneBuilder.EnsureMaterial(GrassMaterialPath, new Color(0.22f, 0.42f, 0.16f), null, Vector2.one);
            var lineMaterial = SkidpadSceneBuilder.EnsureMaterial(LineMaterialPath, new Color(0.95f, 0.8f, 0.1f), null, Vector2.one);

            // Road edges from the centreline and its widths. The verges start at the road edge
            // and run VergeWidthM further out, at the same height as the edge they meet.
            int n = centre.Length;
            var leftEdge = new Vector3[n];
            var rightEdge = new Vector3[n];
            var leftOuter = new Vector3[n];
            var rightOuter = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                leftEdge[i] = centre[i] - right[i] * track.centerline.width_left[i];
                rightEdge[i] = centre[i] + right[i] * track.centerline.width_right[i];
                leftOuter[i] = leftEdge[i] - right[i] * VergeWidthM;
                rightOuter[i] = rightEdge[i] + right[i] * VergeWidthM;
            }

            var road = Strip("Road", root, leftEdge, rightEdge, roadMaterial, asphalt);
            Strip("Verge Left", root, leftOuter, leftEdge, grassMaterial, grass);
            Strip("Verge Right", root, rightEdge, rightOuter, grassMaterial, grass);

            // A wall along the outside of each verge, so the car cannot leave the circuit. It
            // sits on its own layer, which the wheel probes are told to ignore below: a wheel
            // brushing the wall would otherwise read it as ground and launch the car.
            var barrierMaterial = SkidpadSceneBuilder.EnsureMaterial(BarrierMaterialPath, new Color(0.85f, 0.85f, 0.85f), null, Vector2.one);
            Wall("Barrier Left", root, leftOuter, right, +1f, barrierMaterial, barrierSurface, barrierLayer);
            Wall("Barrier Right", root, rightOuter, right, -1f, barrierMaterial, barrierSurface, barrierLayer);

            // The racing line, painted 2 cm above the road with no collider: the line the AI
            // drives in the harness, so a lap here can be compared with the plan.
            var lineRight = RightOf(line);
            var lineLeftSide = new Vector3[n];
            var lineRightSide = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                lineLeftSide[i] = line[i] - lineRight[i] * (LineWidthM * 0.5f) + Vector3.up * 0.02f;
                lineRightSide[i] = line[i] + lineRight[i] * (LineWidthM * 0.5f) + Vector3.up * 0.02f;
            }
            Strip("Racing Line", root, lineLeftSide, lineRightSide, lineMaterial, null);

            // The car on the racing line at the start of the lap, pointing along it, its origin
            // CgHeight above the road plus a few centimetres so it settles rather than starts
            // inside the surface on a slope.
            Vector3 heading = (line[1] - line[n - 1]).normalized;
            Vector3 start = line[0] + Vector3.up * (definition.cgHeight + 0.05f);
            GameObject car = SkidpadSceneBuilder.PlaceCar(carLayer, definition, start, Quaternion.LookRotation(heading, Vector3.up));
            // Lap timing: the centreline as the timer's measure of progress, start line at 0.
            var path = root.AddComponent<TrackPath>();
            path.trackName = track.name;
            path.centre = centre;
            path.referenceLapSeconds = ReferenceLaps.TryGetValue(circuit, out float reference) ? reference : 0f;
            var timer = car.AddComponent<LapTimer>();
            var timerSettings = new SerializedObject(timer);
            timerSettings.FindProperty("car").objectReferenceValue = car.GetComponent<Rigidbody>();
            timerSettings.FindProperty("track").objectReferenceValue = path;
            timerSettings.ApplyModifiedPropertiesWithoutUndo();

            var controller = new SerializedObject(car.GetComponent<CarController>());
            var ground = controller.FindProperty("groundLayers");
            ground.intValue &= ~(1 << barrierLayer);
            controller.ApplyModifiedPropertiesWithoutUndo();
            if ((ground.intValue & (1 << barrierLayer)) != 0)
                throw new InvalidOperationException("The wheels would read the barrier as ground.");

            // A lap spans kilometres; the default 1 km far plane cuts it off in the distance.
            var camera = Camera.main;
            if (camera != null) camera.farClipPlane = 5000f;

            if (road.GetComponent<MeshCollider>().sharedMaterial != asphalt)
                throw new InvalidOperationException("Road has lost its Asphalt material.");

            string scenePath = $"Assets/Scenes/Track {track.name}.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);
            SkidpadSceneBuilder.AddToBuildSettings(scenePath);
            Selection.activeGameObject = car;
            Debug.Log($"{track.name} built at {scenePath}: {n} samples, {Length(centre):0} m of road, " +
                      $"{Max(track.centerline.z) - floor:0} m of climb. Press Play; R respawns on the grid.");
        }

        static void Check(TrackFile track, string circuit)
        {
            int n = track?.centerline?.x?.Length ?? 0;
            if (n < 3)
                throw new InvalidDataException($"{circuit}.json has no centreline.");
            if (track.centerline.y.Length != n || track.centerline.z.Length != n
                || track.centerline.width_left.Length != n || track.centerline.width_right.Length != n
                || track.racing_line.x.Length != n || track.racing_line.y.Length != n || track.racing_line.z.Length != n)
                throw new InvalidDataException(
                    $"{circuit}.json: centreline and racing line arrays differ in length; they are indexed together.");
        }

        /// <summary>ENU to Unity, (x, y, z) to (x, z, y), with the lowest point of the lap at y = 0.</summary>
        static Vector3[] Points(Polyline p, float floor)
        {
            var points = new Vector3[p.x.Length];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(p.x[i], p.z[i] - floor, p.y[i]);
            return points;
        }

        /// <summary>The horizontal direction to the right of travel at each sample, from its
        /// neighbours either side, as the harness measures it (TrackData.Tangent and Right).</summary>
        static Vector3[] RightOf(Vector3[] path)
        {
            int n = path.Length;
            var right = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = path[(i + 1) % n] - path[(i - 1 + n) % n];
                d.y = 0f;
                d.Normalize();
                right[i] = new Vector3(d.z, 0f, -d.x);
            }
            return right;
        }

        /// <summary>A closed strip between two edges, left then right in the direction of
        /// travel, with its faces turned upwards and a collider if it is driven on.</summary>
        static GameObject Strip(string name, GameObject parent, Vector3[] left, Vector3[] right,
                                Material material, PhysicsMaterial surface)
        {
            int n = left.Length;
            var vertices = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) along += Vector3.Distance(left[i], left[i - 1]);
                vertices[2 * i] = left[i];
                vertices[2 * i + 1] = right[i];
                uv[2 * i] = new Vector2(0f, along * 0.1f);
                uv[2 * i + 1] = new Vector2(1f, along * 0.1f);
            }

            var triangles = new List<int>(n * 6);
            for (int i = 0; i < n; i++)
            {
                int a = 2 * i, b = 2 * i + 1, c = 2 * ((i + 1) % n), d = 2 * ((i + 1) % n) + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }

            // Winding decides which side is visible and which way the collider faces. Rather
            // than trust the arithmetic for a left-handed engine, look at the first triangle
            // and flip the lot if it points down.
            Vector3 normal = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]],
                                           vertices[triangles[2]] - vertices[triangles[0]]);
            if (normal.y < 0f)
                for (int t = 0; t < triangles.Count; t += 3)
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);

            var mesh = new Mesh { name = name, vertices = vertices, uv = uv };
            mesh.indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                                       : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (surface != null)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.sharedMaterial = surface;
            }
            return go;
        }

        /// <summary>
        /// A vertical wall along <paramref name="edge"/>, facing the track, which lies on the
        /// side <paramref name="inward"/> times <paramref name="right"/>. Its foot is sunk
        /// BarrierFootM below the edge so that no gap opens where the ground rises and falls
        /// between samples, and it has a top so it reads as a wall rather than a sheet.
        /// </summary>
        static void Wall(string name, GameObject parent, Vector3[] edge, Vector3[] right, float inward,
                         Material material, PhysicsMaterial surface, int layer)
        {
            int n = edge.Length;
            const float thickness = 0.4f;
            // Per sample: inner foot, inner top, outer top, outer foot.
            var vertices = new Vector3[n * 4];
            for (int i = 0; i < n; i++)
            {
                Vector3 outward = -right[i] * inward * thickness;
                vertices[4 * i] = edge[i] + Vector3.down * BarrierFootM;
                vertices[4 * i + 1] = edge[i] + Vector3.up * BarrierHeightM;
                vertices[4 * i + 2] = edge[i] + outward + Vector3.up * BarrierHeightM;
                vertices[4 * i + 3] = edge[i] + outward + Vector3.down * BarrierFootM;
            }

            var triangles = new List<int>(n * 18);
            for (int i = 0; i < n; i++)
            {
                int a = 4 * i, b = 4 * ((i + 1) % n);
                for (int f = 0; f < 3; f++)   // inner face, top, outer face
                    triangles.AddRange(new[] { a + f, b + f, a + f + 1, a + f + 1, b + f, b + f + 1 });
            }

            // The inner face must face the track. Check its first triangle against the inward
            // direction and flip everything if it faces away; the other faces follow.
            Vector3 normal = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]],
                                           vertices[triangles[2]] - vertices[triangles[0]]);
            if (Vector3.Dot(normal, right[0] * inward) < 0f)
                for (int t = 0; t < triangles.Count; t += 3)
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices };
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.sharedMaterial = surface;
        }

        /// <summary>No friction and no bounce, combined at the minimum, so a car that touches
        /// the wall glances along it instead of snagging or being thrown back across the road.</summary>
        static void EnsureBarrierPhysics()
        {
            if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BarrierPhysicsPath) != null) return;
            var barrier = new PhysicsMaterial("Barrier")
            {
                dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(BarrierPhysicsPath));
            AssetDatabase.CreateAsset(barrier, BarrierPhysicsPath);
        }

        static void EnsureGrassPhysics()
        {
            if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(GrassPhysicsPath) != null) return;
            var grass = new PhysicsMaterial("Grass") { dynamicFriction = 0.35f, staticFriction = 0.35f };
            Directory.CreateDirectory(Path.GetDirectoryName(GrassPhysicsPath));
            AssetDatabase.CreateAsset(grass, GrassPhysicsPath);
        }

        static float Min(float[] v) { float m = float.MaxValue; foreach (float x in v) m = Mathf.Min(m, x); return m; }
        static float Max(float[] v) { float m = float.MinValue; foreach (float x in v) m = Mathf.Max(m, x); return m; }
        static float Length(Vector3[] p) { float l = 0f; for (int i = 0; i < p.Length; i++) l += Vector3.Distance(p[i], p[(i + 1) % p.Length]); return l; }
    }
}
