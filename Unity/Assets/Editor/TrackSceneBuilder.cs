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
    /// as a braking guide (RacingLineGuide), and a grid behind the start line: three AI cars and the player.
    /// The road follows the file's elevation, so Spa climbs its 105 m. Camber and banking ship
    /// as zero in every file, so the road is flat across; that is the data, not a shortcut.
    ///
    /// The file maps ENU to Unity as (x, y, z) to (x, z, y), the same swap the harness's
    /// TrackLoader makes, so the racing line here is the one the AI drives headlessly.
    /// Elevation is shifted so the lowest point of the lap sits at y = 0.
    ///
    /// Grip comes from the collider's physics material, which the wheels read: asphalt at 1,
    /// grass at 0.45. A wall with a little friction runs along the outside of each verge, so
    /// the car cannot leave the circuit; R recovers it onto the track where it is.
    /// </summary>
    public static class TrackSceneBuilder
    {
        const string TracksFolder = "Assets/Tracks";
        const string GrassPhysicsPath = "Assets/Physics/Grass.asset";
        const string RoadMaterialPath = "Assets/Materials/Road.mat";
        const string GrassMaterialPath = "Assets/Materials/Grass.mat";
        static readonly (string path, Color colour)[] GuideMaterials =
        {
            ("Assets/Materials/RacingLineGreen.mat", new Color(0.15f, 0.85f, 0.25f)),
            ("Assets/Materials/RacingLineYellow.mat", new Color(1f, 0.82f, 0.1f)),
            ("Assets/Materials/RacingLineRed.mat", new Color(1f, 0.15f, 0.1f)),
        };
        const string BarrierMaterialPath = "Assets/Materials/Barrier.mat";
        const string ArrowMaterialPath = "Assets/Materials/DirectionArrow.mat";
        const int ArrowEverySamples = 25;   // 50 m at 2 m spacing
        const string EdgeLineMaterialPath = "Assets/Materials/RoadLine.mat";
        const string KerbRedMaterialPath = "Assets/Materials/KerbRed.mat";
        const string KerbWhiteMaterialPath = "Assets/Materials/KerbWhite.mat";
        const float EdgeLineWidthM = 0.2f;
        const float EdgeLineInsetM = 0.25f;     // from the road's edge to the line's outer side
        const float KerbWidthM = 1.2f;
        const float KerbBelowRadiusM = 150f;    // corners tighter than this get kerbs
        const int KerbRunOnSamples = 5;         // 10 m before and after
        const float PaintLiftM = 0.02f;
        const string BarrierPhysicsPath = "Assets/Physics/Barrier.asset";
        const string BarrierLayerName = "Barrier";
        const float BarrierHeightM = 1.2f;
        const float BarrierFootM = 0.5f;
        const float VergeWidthM = 15f;

        // The grid, laid out as the harness does (RaceRun.PlaceOnGrid) but around the road centre: two abreast, rows 10 m
        // apart, the front row 10 m behind the line. The AI fill the front slots, fastest on
        // pole, and the player starts at the back.
        const int AiCars = 3;
        const float RowGapM = 10f;
        const float GridLateralM = 2.5f;   // from the road's centre
        static readonly Color[] AiColours =
        {
            new Color(0.1f, 0.3f, 0.85f), new Color(0.95f, 0.75f, 0.1f), new Color(0.15f, 0.65f, 0.25f),
        };

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
        /// `--lap all` on 2026-09-24 after the gearbox fix. On flat ground, so a guide rather
        /// than a par here.</summary>
        static readonly Dictionary<string, float> ReferenceLaps = new Dictionary<string, float>
        {
            ["bahrain"] = 160.977f, ["monza"] = 147.018f, ["silverstone"] = 175.119f,
            ["spa"] = 193.311f, ["suzuka"] = 169.529f, ["testcircuit"] = 62.260f,
        };

        const string CatalogPath = "Assets/Settings/TrackCatalog.asset";

        [Serializable] class Polyline { public float[] x, y, z, width_left, width_right; }
        [Serializable] class TrackFile { public string name; public float sample_spacing_m, length_m; public Polyline centerline, racing_line; }

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
            var roadMaterial = SkidpadSceneBuilder.EnsureMaterial(RoadMaterialPath, new Color(0.3f, 0.3f, 0.32f), null, Vector2.one);
            SurfaceTextures.ApplyAsphalt(roadMaterial);
            // The verges look like the theme's ground (sand in the desert) and still drive as grass.
            Theme theme = Theme.For(circuit);
            string vergePath = theme.Ground == Surface.Grass ? GrassMaterialPath : $"Assets/Materials/Verge{theme.Ground}.mat";
            var grassMaterial = SkidpadSceneBuilder.EnsureMaterial(vergePath, new Color(0.22f, 0.42f, 0.16f), null, Vector2.one);
            SurfaceTextures.Apply(grassMaterial, theme.Ground);

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
            RoadMarkings(root, centre, right, leftEdge, rightEdge, asphalt);
            Strip("Verge Left", root, leftOuter, leftEdge, grassMaterial, grass);
            Strip("Verge Right", root, rightEdge, rightOuter, grassMaterial, grass);

            // A wall along the outside of each verge, so the car cannot leave the circuit. It
            // sits on its own layer, which the wheel probes are told to ignore below: a wheel
            // brushing the wall would otherwise read it as ground and launch the car.
            var barrierMaterial = SkidpadSceneBuilder.EnsureMaterial(BarrierMaterialPath, new Color(0.85f, 0.85f, 0.85f), null, Vector2.one);
            Wall("Barrier Left", root, leftOuter, right, +1f, barrierMaterial, barrierSurface, barrierLayer);
            Wall("Barrier Right", root, rightOuter, right, -1f, barrierMaterial, barrierSurface, barrierLayer);
            Bridges(root, centre, right, track.centerline.width_left, track.centerline.width_right, barrierMaterial);

            // Ground out to the horizon, held under the road and verges.
            Terrain terrain = GroundBuilder.Build(root, centre, track.centerline.width_left, track.centerline.width_right,
                                                 VergeWidthM, $"Assets/Scenes/Track {track.name}/Ground.asset", theme);

            // The surroundings in the circuit's theme, on that ground and clear of the barriers.
            SceneryBuilder.Build(root, theme, centre, right,
                                 track.centerline.width_left, track.centerline.width_right, VergeWidthM, terrain);

            // The racing line is drawn by RacingLineGuide, added below once the player's car
            // exists: bars ahead of the car coloured by how hard it would have to brake.
            var lineRight = RightOf(line);

            // Arrowheads down the middle of the road, pointing the way the lap runs, so that
            // after a spin the road itself says which way to go.
            var arrowMaterial = SkidpadSceneBuilder.EnsureMaterial(ArrowMaterialPath, new Color(0.95f, 0.95f, 0.95f), null, Vector2.one);
            Arrows(root, centre, right, arrowMaterial);

            // The player in the last grid slot, pointing along the racing line, its origin
            // CgHeight above the road plus a few centimetres so it settles rather than starts
            // inside the surface on a slope.
            var (start, facing) = GridSlot(AiCars, centre, right, track.sample_spacing_m, definition.cgHeight);
            GameObject car = SkidpadSceneBuilder.PlaceCar(carLayer, definition, start, facing);
            // Lap timing: the centreline as the timer's measure of progress, start line at 0.
            var path = root.AddComponent<TrackPath>();
            path.trackName = track.name;
            path.centre = centre;
            path.line = line;
            path.widthLeft = track.centerline.width_left;
            path.widthRight = track.centerline.width_right;
            path.sampleSpacing = track.sample_spacing_m;
            path.lengthM = track.length_m;
            path.referenceLapSeconds = ReferenceLaps.TryGetValue(circuit, out float reference) ? reference : 0f;
            var timer = car.AddComponent<LapTimer>();
            var timerSettings = new SerializedObject(timer);
            timerSettings.FindProperty("car").objectReferenceValue = car.GetComponent<Rigidbody>();
            timerSettings.FindProperty("track").objectReferenceValue = path;
            timerSettings.ApplyModifiedPropertiesWithoutUndo();

            // R recovers onto the track where the car is, rather than back to the grid.
            var recovery = car.AddComponent<TrackRecovery>();
            var recoverySettings = new SerializedObject(recovery);
            recoverySettings.FindProperty("car").objectReferenceValue = car.GetComponent<CarController>();
            recoverySettings.FindProperty("track").objectReferenceValue = path;
            recoverySettings.FindProperty("cgHeight").floatValue = definition.cgHeight;
            recoverySettings.ApplyModifiedPropertiesWithoutUndo();

            // The AI, in the slots in front, each its own colour. RaceDirector drives them.
            var tyreMaterial = SkidpadSceneBuilder.EnsureMaterial(SkidpadSceneBuilder.TyreMaterialPath, new Color(0.08f, 0.08f, 0.08f), null, Vector2.one);
            var cars = new List<CarController> { car.GetComponent<CarController>() };
            var aiCars = new CarController[AiCars];
            for (int slot = 0; slot < AiCars; slot++)
            {
                GameObject ai = SkidpadSceneBuilder.BuildCar(carLayer, definition, withDriver: false);
                ai.name = $"AI {slot + 1}";
                var (position, rotation) = GridSlot(slot, centre, right, track.sample_spacing_m, definition.cgHeight);
                ai.transform.SetPositionAndRotation(position, rotation);
                var body = SkidpadSceneBuilder.EnsureMaterial($"Assets/Materials/AiBody{slot + 1}.mat", AiColours[slot % AiColours.Length], null, Vector2.one);
                foreach (var r in ai.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = r.name == "Body" ? body : tyreMaterial;
                aiCars[slot] = ai.GetComponent<CarController>();
                cars.Add(aiCars[slot]);
            }

            var director = root.AddComponent<RaceDirector>();
            var directorSettings = new SerializedObject(director);
            directorSettings.FindProperty("track").objectReferenceValue = path;
            directorSettings.FindProperty("player").objectReferenceValue = car.GetComponent<CarController>();
            var aiList = directorSettings.FindProperty("aiCars");
            aiList.arraySize = AiCars;
            for (int i = 0; i < AiCars; i++) aiList.GetArrayElementAtIndex(i).objectReferenceValue = aiCars[i];
            directorSettings.ApplyModifiedPropertiesWithoutUndo();

            // The racing line as a braking guide: green, yellow and red bars ahead of the player.
            var guide = new GameObject("Racing Line");
            guide.transform.SetParent(root.transform, false);
            guide.AddComponent<MeshFilter>();
            var guideRenderer = guide.AddComponent<MeshRenderer>();
            guideRenderer.sharedMaterials = Array.ConvertAll(GuideMaterials, m => EnsureUnlit(m.path, m.colour));
            guideRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            guideRenderer.receiveShadows = false;
            var guideSettings = new SerializedObject(guide.AddComponent<RacingLineGuide>());
            guideSettings.FindProperty("track").objectReferenceValue = path;
            guideSettings.FindProperty("player").objectReferenceValue = car.GetComponent<CarController>();
            guideSettings.ApplyModifiedPropertiesWithoutUndo();

            // The circuit map in the corner, the player first.
            var map = new SerializedObject(root.AddComponent<MiniMap>());
            map.FindProperty("track").objectReferenceValue = path;
            var mapCars = map.FindProperty("cars");
            mapCars.arraySize = cars.Count;
            for (int i = 0; i < cars.Count; i++) mapCars.GetArrayElementAtIndex(i).objectReferenceValue = cars[i].transform;
            map.ApplyModifiedPropertiesWithoutUndo();

            foreach (CarController each in cars)
            {
                var controller = new SerializedObject(each);
                var ground = controller.FindProperty("groundLayers");
                ground.intValue &= ~(1 << barrierLayer);
                controller.ApplyModifiedPropertiesWithoutUndo();
                if ((ground.intValue & (1 << barrierLayer)) != 0)
                    throw new InvalidOperationException("The wheels would read the barrier as ground.");
            }

            // A lap spans kilometres; the default 1 km far plane cuts it off in the distance.
            var camera = Camera.main;
            if (camera != null) camera.farClipPlane = 5000f;
            GraphicsSetup.AddPostProcessing(root, camera);

            // At night every car lights the road ahead of it.
            if (theme.Night)
                foreach (var each in UnityEngine.Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                    Headlight(each.transform);

            if (road.GetComponent<MeshCollider>().sharedMaterial != asphalt)
                throw new InvalidOperationException("Road has lost its Asphalt material.");

            string scenePath = $"Assets/Scenes/Track {track.name}.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);

            // The sky and sun last: the environment bake needs the scene saved, since it
            // writes the scene's lighting data beside it, and the scene is saved again after.
            // The scene's own directional light: the night city's floodlights are lights too.
            Light sun = Array.Find(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None), l => l.type == LightType.Directional);
            GraphicsSetup.SetUpSkyAndSun(sun, theme.Night);
            EditorSceneManager.SaveScene(scene, scenePath);
            SkidpadSceneBuilder.AddToBuildSettings(scenePath);
            RecordInCatalog($"Track {track.name}", track.name, circuit, Length(centre), centre);
            Selection.activeGameObject = car;
            Debug.Log($"{track.name} built at {scenePath}: {n} samples, {Length(centre):0} m of road, " +
                      $"{Max(track.centerline.z) - floor:0} m of climb. {AiCars} AI on the grid ahead of you. Press Play; the AI go when you do.");
        }

        /// <summary>This circuit's card in the lobby: name, length, theme and outline.</summary>
        static void RecordInCatalog(string scene, string name, string circuit, float lengthM, Vector3[] centre)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TrackCatalog>();
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            // Fitted into a unit square, keeping its shape, about 240 points.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in centre) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            float span = Mathf.Max(maxX - minX, maxZ - minZ);
            var offset = new Vector2((span - (maxX - minX)) * 0.5f, (span - (maxZ - minZ)) * 0.5f);
            int step = Mathf.Max(1, centre.Length / 240);
            var outline = new List<Vector2>();
            for (int i = 0; i < centre.Length; i += step)
                outline.Add((new Vector2(centre[i].x - minX, centre[i].z - minZ) + offset) / span);

            var entry = catalog.entries.Find(e => e.scene == scene);
            if (entry == null) catalog.entries.Add(entry = new TrackCatalog.Entry { scene = scene });
            entry.displayName = name;
            entry.theme = Theme.For(circuit).Name;
            entry.lengthKm = lengthM / 1000f;
            entry.outline = outline.ToArray();
            catalog.entries.Sort((a, b) => string.CompareOrdinal(a.displayName, b.displayName));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Grid slot <paramref name="slot"/>, 0 being pole: behind the start, either side of the
        /// road's centre, pointing along it, CgHeight plus a few centimetres up. The harness
        /// grids either side of the racing line, which on Monza's start straight runs near the
        /// right-hand edge, and in Unity that put the right-hand cars partly on the grass. The
        /// AI read their place from where they stand, so they need nothing else.
        /// </summary>
        /// <summary>
        /// A concrete deck under the road wherever the lap passes over itself (Ise Bay's
        /// figure of eight; the track pipeline lifts the upper road 7.5 m clear).
        /// The road and verges are one-sided, so from beneath the bridge was two walls in the
        /// air. The deck spans the lower road, its verges and a margin, as wide as the upper
        /// road with its verges, and has no collider: it is well above anything below.
        /// </summary>
        static void Bridges(GameObject root, Vector3[] centre, Vector3[] right, float[] widthLeft, float[] widthRight, Material material)
        {
            const float DeckM = 1.2f, MinGapM = 4f;
            int n = centre.Length;
            for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (!Crosses(centre[i], centre[(i + 1) % n], centre[j], centre[(j + 1) % n])) continue;
                int upper = centre[i].y > centre[j].y ? i : j, lower = upper == i ? j : i;
                if (centre[upper].y - centre[lower].y < MinGapM) continue;

                Vector3 along = centre[(upper + 1) % n] - centre[(upper - 1 + n) % n];
                along.y = 0f;
                float span = widthLeft[lower] + widthRight[lower] + 2f * VergeWidthM + 20f;
                float width = widthLeft[upper] + widthRight[upper] + 2f * VergeWidthM;
                var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                deck.name = "Bridge";
                UnityEngine.Object.DestroyImmediate(deck.GetComponent<Collider>());
                deck.transform.SetParent(root.transform, false);
                deck.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
                float offset = (widthRight[upper] - widthLeft[upper]) * 0.5f;
                deck.transform.position = centre[upper] + right[upper] * offset + Vector3.down * (DeckM * 0.5f + 0.05f);
                deck.transform.localScale = new Vector3(width, DeckM, span);
                deck.GetComponent<MeshRenderer>().sharedMaterial = material;
                GameObjectUtility.SetStaticEditorFlags(deck, StaticEditorFlags.BatchingStatic);
            }
        }

        static bool Crosses(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float d1x = b.x - a.x, d1z = b.z - a.z, d2x = d.x - c.x, d2z = d.z - c.z;
            float den = d1x * d2z - d1z * d2x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            float ex = c.x - a.x, ez = c.z - a.z;
            float t = (ex * d2z - ez * d2x) / den, u = (ex * d1z - ez * d1x) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>One spot lamp at the car's nose, a little down the road ahead: enough to
        /// read the road between the floodlights and to pick out the car itself.</summary>
        static void Headlight(Transform car)
        {
            var lamp = new GameObject("Headlight").AddComponent<Light>();
            lamp.transform.SetParent(car, false);
            lamp.transform.localPosition = new Vector3(0f, 0.6f, 2.2f);
            lamp.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            lamp.type = LightType.Spot;
            lamp.spotAngle = 70f;
            lamp.innerSpotAngle = 40f;
            lamp.range = 70f;
            lamp.intensity = 120f;
            lamp.color = new Color(1f, 0.96f, 0.88f);
            lamp.shadows = LightShadows.None;
        }

        static (Vector3, Quaternion) GridSlot(int slot, Vector3[] centre, Vector3[] right, float spacing, float cgHeight)
        {
            int n = centre.Length;
            float back = (slot / 2) * RowGapM + RowGapM;
            float lateral = slot % 2 == 0 ? -GridLateralM : GridLateralM;
            int index = ((-Mathf.RoundToInt(back / spacing)) % n + n) % n;
            Vector3 position = centre[index] + right[index] * lateral + Vector3.up * (cgHeight + 0.05f);
            Vector3 heading = centre[(index + 1) % n] - centre[(index - 1 + n) % n];
            heading.y = 0f;
            return (position, Quaternion.LookRotation(heading.normalized, Vector3.up));
        }

        /// <summary>A flat colour that ignores light and shade, for the guide's bars.</summary>
        static Material EnsureUnlit(string path, Color colour)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                    ?? throw new InvalidOperationException("URP Unlit shader not found."));
            material.SetColor("_BaseColor", colour);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            return material;
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
                // Texture coordinates in metres both ways, so a texture tiles at a real size
                // (SurfaceTextures sets the scale) instead of stretching across the strip.
                if (i > 0) along += Vector3.Distance(left[i], left[i - 1]);
                vertices[2 * i] = left[i];
                vertices[2 * i + 1] = right[i];
                uv[2 * i] = new Vector2(0f, along);
                uv[2 * i + 1] = new Vector2(Vector3.Distance(left[i], right[i]), along);
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
        /// What makes the strip of tarmac read as a circuit: a solid white line just inside
        /// each edge for the whole lap, a white start line across the road at sample 0, and
        /// red and white kerbs outside both edges wherever the road bends tighter than
        /// KerbBelowRadiusM, running on KerbRunOnSamples either side. The kerbs carry an
        /// asphalt collider, so a car riding one keeps its grip instead of dropping onto the
        /// grass beneath. The lines are paint: no collider.
        /// </summary>
        static void RoadMarkings(GameObject root, Vector3[] centre, Vector3[] right, Vector3[] leftEdge, Vector3[] rightEdge,
                                 PhysicsMaterial asphalt)
        {
            int n = centre.Length;
            Vector3 lift = Vector3.up * PaintLiftM;
            var white = SkidpadSceneBuilder.EnsureMaterial(EdgeLineMaterialPath, new Color(0.92f, 0.92f, 0.92f), null, Vector2.one);

            var outer = new Vector3[n];
            var inner = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                outer[i] = leftEdge[i] + right[i] * EdgeLineInsetM + lift;
                inner[i] = leftEdge[i] + right[i] * (EdgeLineInsetM + EdgeLineWidthM) + lift;
            }
            Strip("Edge Line Left", root, outer, inner, white, null);
            for (int i = 0; i < n; i++)
            {
                inner[i] = rightEdge[i] - right[i] * (EdgeLineInsetM + EdgeLineWidthM) + lift;
                outer[i] = rightEdge[i] - right[i] * EdgeLineInsetM + lift;
            }
            Strip("Edge Line Right", root, inner, outer, white, null);

            // The start line: half a metre deep, edge to edge, at sample 0.
            Vector3 along = new Vector3(-right[0].z, 0f, right[0].x) * 0.25f;
            var start = new Mesh { name = "Start Line" };
            start.SetVertices(new List<Vector3>
            {
                leftEdge[0] - along + lift, rightEdge[0] - along + lift, leftEdge[0] + along + lift, rightEdge[0] + along + lift,
            });
            start.SetTriangles(FacingUp(start.vertices, new List<int> { 0, 2, 1, 1, 2, 3 }), 0);
            start.RecalculateNormals();
            var startLine = new GameObject("Start Line");
            startLine.transform.SetParent(root.transform, false);
            startLine.AddComponent<MeshFilter>().sharedMesh = start;
            startLine.AddComponent<MeshRenderer>().sharedMaterial = white;

            // Kerbs where the centreline's radius, over three samples either side, is tight.
            var kerbed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (Radius(centre[(i - 3 + n) % n], centre[i], centre[(i + 3) % n]) >= KerbBelowRadiusM) continue;
                for (int k = -KerbRunOnSamples; k <= KerbRunOnSamples; k++) kerbed[(i + k + n) % n] = true;
            }
            var red = SkidpadSceneBuilder.EnsureMaterial(KerbRedMaterialPath, new Color(0.8f, 0.08f, 0.06f), null, Vector2.one);
            var kerbWhite = SkidpadSceneBuilder.EnsureMaterial(KerbWhiteMaterialPath, new Color(0.92f, 0.92f, 0.92f), null, Vector2.one);
            Kerbs("Kerbs Left", root, kerbed, i => leftEdge[i] - right[i] * KerbWidthM + lift, i => leftEdge[i] + lift, red, kerbWhite, asphalt);
            Kerbs("Kerbs Right", root, kerbed, i => rightEdge[i] + lift, i => rightEdge[i] + right[i] * KerbWidthM + lift, red, kerbWhite, asphalt);
        }

        /// <summary>A block of kerb per sample where kerbed, alternating red and white, left
        /// then right in the direction of travel.</summary>
        static void Kerbs(string name, GameObject parent, bool[] kerbed, Func<int, Vector3> left, Func<int, Vector3> right,
                          Material red, Material white, PhysicsMaterial surface)
        {
            int n = kerbed.Length;
            var vertices = new List<Vector3>();
            var redTriangles = new List<int>();
            var whiteTriangles = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (!kerbed[i] || !kerbed[j]) continue;
                int a = vertices.Count;
                vertices.Add(left(i)); vertices.Add(right(i)); vertices.Add(left(j)); vertices.Add(right(j));
                (i % 2 == 0 ? redTriangles : whiteTriangles).AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            if (vertices.Count == 0) return;

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(FacingUp(vertices, redTriangles), 0);
            mesh.SetTriangles(FacingUp(vertices, whiteTriangles), 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { red, white };
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.sharedMaterial = surface;
        }

        /// <summary>Triangles flipped if the first one faces down, as Strip does.</summary>
        static List<int> FacingUp(IList<Vector3> vertices, List<int> triangles)
        {
            if (triangles.Count < 3) return triangles;
            Vector3 normal = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]],
                                           vertices[triangles[2]] - vertices[triangles[0]]);
            if (normal.y < 0f)
                for (int t = 0; t < triangles.Count; t += 3)
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            return triangles;
        }

        static float Radius(Vector3 a, Vector3 b, Vector3 c)
        {
            a.y = b.y = c.y = 0f;
            float cross = Mathf.Abs((b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x));
            if (cross < 1e-6f) return float.MaxValue;
            return (a - b).magnitude * (b - c).magnitude * (c - a).magnitude / (2f * cross);
        }

        /// <summary>
        /// A flat arrowhead every ArrowEverySamples along the centreline, 4 m long and 3 m
        /// wide, pointing along the lap. Each corner takes its height from the sample nearest
        /// it, so the arrow lies on a sloping road instead of cutting into it, and sits 8 cm up.
        /// </summary>
        static void Arrows(GameObject parent, Vector3[] centre, Vector3[] right, Material material)
        {
            int n = centre.Length;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 lift = Vector3.up * 0.08f;
            for (int i = ArrowEverySamples; i < n - 2; i += ArrowEverySamples)
            {
                Vector3 along = new Vector3(-right[i].z, 0f, right[i].x);   // right rotated back to forward
                Vector3 tip = centre[i] + along * 2f;
                tip.y = centre[i + 1].y;
                Vector3 baseCentre = centre[i] - along * 2f;
                baseCentre.y = centre[i - 1].y;
                int first = vertices.Count;
                vertices.Add(tip + lift);
                vertices.Add(baseCentre - right[i] * 1.5f + lift);
                vertices.Add(baseCentre + right[i] * 1.5f + lift);
                triangles.AddRange(new[] { first, first + 1, first + 2 });
            }

            // Face up, whichever way the arithmetic came out in a left-handed engine.
            Vector3 normal = Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]);
            if (normal.y < 0f)
                for (int t = 0; t < triangles.Count; t += 3)
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);

            var mesh = new Mesh { name = "Direction Arrows" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Direction Arrows");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
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

        /// <summary>
        /// Some friction and no bounce, combined at the minimum. The wall started frictionless,
        /// so a car touching it glanced along instead of snagging, but then nothing slowed a car
        /// pinned to it: one ran along the Monza wall at 140 km/h for ten seconds with full lock
        /// asked for, the wall holding its rear so it could not turn away. At 0.3 sliding along
        /// the wall scrubs speed, and slower the grass can turn the car.
        ///
        /// Written on every build, not only when the asset is missing, so a change here reaches
        /// a project built before it.
        /// </summary>
        static void EnsureBarrierPhysics()
        {
            var barrier = LoadOrCreate(BarrierPhysicsPath, "Barrier");
            barrier.dynamicFriction = 0.3f;
            barrier.staticFriction = 0.3f;
            barrier.bounciness = 0f;
            barrier.frictionCombine = PhysicsMaterialCombine.Minimum;
            barrier.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(barrier);
        }

        /// <summary>Grass grips at 0.45 of asphalt: enough to steer back onto the road from
        /// the verge, which at 0.35 was a struggle, and still well short of the road.</summary>
        static void EnsureGrassPhysics()
        {
            var grass = LoadOrCreate(GrassPhysicsPath, "Grass");
            grass.dynamicFriction = 0.45f;
            grass.staticFriction = 0.45f;
            EditorUtility.SetDirty(grass);
        }

        static PhysicsMaterial LoadOrCreate(string path, string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material != null) return material;
            material = new PhysicsMaterial(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static float Min(float[] v) { float m = float.MaxValue; foreach (float x in v) m = Mathf.Min(m, x); return m; }
        static float Max(float[] v) { float m = float.MinValue; foreach (float x in v) m = Mathf.Max(m, x); return m; }
        static float Length(Vector3[] p) { float l = 0f; for (int i = 0; i < p.Length; i++) l += Vector3.Distance(p[i], p[(i + 1) % p.Length]); return l; }
    }
}
