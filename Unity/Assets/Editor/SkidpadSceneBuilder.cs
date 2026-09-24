using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CarRace.UnityGame;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Builds the Phase 1 feel-test scene in one step: CarRace, Build Skidpad Scene.
    ///
    /// Everything Unity/README.md describes by hand, done the same way every time: a
    /// 1 km plane with the grip the tyre data was measured at, the car on its own layer
    /// with its origin at the centre of mass, the wheel probes told to ignore that layer,
    /// cosmetic wheels, the chase camera, and the two trigger axes a gamepad needs. The
    /// hand-built version has three steps that look like physics bugs when missed (the
    /// layer mask, the origin height, colliders on the wheels), which is why this exists.
    ///
    /// Safe to run again: it rebuilds the scene and reuses the two assets if present, so
    /// any values tuned on the Car Definition survive.
    /// </summary>
    public static class SkidpadSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Skidpad.unity";
        internal const string DefinitionPath = "Assets/Cars/ReferenceCar.asset";
        internal const string AsphaltPath = "Assets/Physics/Asphalt.asset";
        const string CheckerPath = "Assets/Materials/Checker.asset";
        const string GroundMaterialPath = "Assets/Materials/Ground.mat";
        const string BodyMaterialPath = "Assets/Materials/CarBody.mat";
        const string TyreMaterialPath = "Assets/Materials/Tyre.mat";
        internal const string CarLayerName = "Car";

        [MenuItem("CarRace/Build Skidpad Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int carLayer = EnsureLayer(CarLayerName);
            EnsureTriggerAxes();

            // The scene first, the assets after. Opening a scene in Single mode unloads every
            // asset nothing references yet, and a Car Definition created before it was
            // destroyed in between, so the car was saved pointing at nothing and refused to
            // build. Saved and loaded back from disk, the references are to the real files.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureAsphalt();
            EnsureDefinition();
            AssetDatabase.SaveAssets();
            var asphalt = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(AsphaltPath);
            var definition = AssetDatabase.LoadAssetAtPath<CarDefinition>(DefinitionPath);

            // Ground: Unity's plane is 10 m, so 100x is a kilometre square, enough for a
            // skidpad circle and a flat-out run without falling off the edge.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(100f, 1f, 100f);
            ground.GetComponent<Collider>().sharedMaterial = asphalt;

            // A checkerboard of 5 m squares. On a plain plane there is nothing to see go past,
            // so a moving car and a parked one look the same.
            ground.GetComponent<Renderer>().sharedMaterial = EnsureMaterial(
                GroundMaterialPath, new Color(0.55f, 0.55f, 0.55f), EnsureChecker(), new Vector2(100f, 100f));

            GameObject car = PlaceCar(carLayer, definition,
                                      new Vector3(0f, definition.cgHeight, 0f), Quaternion.identity);

            Verify(car, ground, asphalt, definition);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = car;
            Debug.Log($"Skidpad scene built at {ScenePath}. Press Play. Keyboard: WASD, Space handbrake, " +
                      "E/Q shift, R respawn, C camera. Gamepad: left stick steer, RT throttle, LT brake, " +
                      "A handbrake, RB/LB shift, View respawn, Y camera.");
        }

        /// <summary>For a headless check: Unity.exe -batchmode -executeMethod
        /// CarRace.UnityGame.EditorTools.SkidpadSceneBuilder.BuildFromCommandLine -quit</summary>
        public static void BuildFromCommandLine()
        {
            Build();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The drivable car, dressed and followed: built by BuildCar, placed with its origin at
        /// <paramref name="position"/> (which must be the centre of mass, CgHeight above the
        /// road), painted, given the DriveHud readout, and picked up by the scene's camera.
        /// Shared with the track scenes so there is one car setup, not two to keep in step.
        /// </summary>
        static void Verify(GameObject car, GameObject ground, PhysicsMaterial asphalt, CarDefinition definition)
        {
            if (ground.GetComponent<Collider>().sharedMaterial != asphalt)
                throw new System.InvalidOperationException("Ground has lost its Asphalt material.");
            if (!EditorUtility.IsPersistent(ground.GetComponent<Renderer>().sharedMaterial))
                throw new System.InvalidOperationException("Ground material is not saved as an asset.");
        }

        internal static GameObject PlaceCar(int carLayer, CarDefinition definition, Vector3 position, Quaternion rotation)
        {
            GameObject car = BuildCar(carLayer, definition);
            car.transform.SetPositionAndRotation(position, rotation);

            var bodyMaterial = EnsureMaterial(BodyMaterialPath, new Color(0.8f, 0.1f, 0.08f), null, Vector2.one);
            var tyreMaterial = EnsureMaterial(TyreMaterialPath, new Color(0.08f, 0.08f, 0.08f), null, Vector2.one);
            foreach (var r in car.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = r.name == "Body" ? bodyMaterial : tyreMaterial;

            var hud = car.AddComponent<DriveHud>();
            var hudSettings = new SerializedObject(hud);
            hudSettings.FindProperty("car").objectReferenceValue = car.GetComponent<CarController>();
            hudSettings.FindProperty("driver").objectReferenceValue = car.GetComponent<DriverInput>();
            hudSettings.ApplyModifiedPropertiesWithoutUndo();

            var recorder = car.AddComponent<TelemetryRecorder>();
            var recorderSettings = new SerializedObject(recorder);
            recorderSettings.FindProperty("car").objectReferenceValue = car.GetComponent<CarController>();
            recorderSettings.FindProperty("driver").objectReferenceValue = car.GetComponent<DriverInput>();
            recorderSettings.ApplyModifiedPropertiesWithoutUndo();

            var camera = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            camera.transform.SetPositionAndRotation(position + rotation * new Vector3(0f, 1.5f, -6f), rotation);
            var follow = camera.AddComponent<CarCamera>();
            var followSettings = new SerializedObject(follow);
            followSettings.FindProperty("target").objectReferenceValue = car.transform;
            followSettings.FindProperty("targetBody").objectReferenceValue = car.GetComponent<Rigidbody>();
            followSettings.ApplyModifiedPropertiesWithoutUndo();

            VerifyCar(car, definition);
            return car;
        }

        /// <summary>Checks the links the car cannot run without, so a broken build says so
        /// here rather than as a car that will not move.</summary>
        internal static void VerifyCar(GameObject car, CarDefinition definition)
        {
            var settings = new SerializedObject(car.GetComponent<CarController>());
            if (settings.FindProperty("definition").objectReferenceValue == null)
                throw new System.InvalidOperationException("Car Controller has no Car Definition after building.");
            if (settings.FindProperty("driver").objectReferenceValue == null)
                throw new System.InvalidOperationException("Car Controller has no Driver Input after building.");
            var box = car.GetComponent<BoxCollider>();
            // Measured from the road under the car, which is CgHeight below the origin, not
            // from y = 0: on a circuit the road can be a few hundred metres up.
            float clearance = definition.cgHeight + box.center.y - box.size.y * 0.5f;
            if (clearance < 0.1f)
                throw new System.InvalidOperationException(
                    $"Body collider clears the road by {clearance:0.00} m; it would drag and the car could not move.");
            if (car.GetComponent<DriveHud>() == null)
                throw new System.InvalidOperationException("Car has no DriveHud.");
            if (!EditorUtility.IsPersistent(definition))
                throw new System.InvalidOperationException("Car Definition is not saved as an asset.");
        }

        static GameObject BuildCar(int carLayer, CarDefinition definition)
        {
            // The transform origin has to be the centre of mass: the model measures every
            // wheel and aero offset from it. So the car sits at its CG height, not at 0.
            var car = new GameObject("Car");
            car.transform.position = new Vector3(0f, definition.cgHeight, 0f);
            car.layer = carLayer;

            car.AddComponent<Rigidbody>();   // mass, damping and inertia come from the definition
            // The body collider must clear the ground. The origin is at the centre of mass,
            // 0.45 m up, so a 1.2 m box centred on it reached 15 cm into the tarmac: the car
            // sat on its own collider with asphalt friction, and the tyres pushed against a
            // block that would not slide. Only the simulated tyres may touch the road.
            var box = car.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.3f, 0f);
            box.size = new Vector3(1.9f, 0.8f, 4.4f);
            var driver = car.AddComponent<DriverInput>();
            var controller = car.AddComponent<CarController>();

            var driverSettings = new SerializedObject(driver);
            driverSettings.FindProperty("useTriggerAxes").boolValue = true;
            driverSettings.ApplyModifiedPropertiesWithoutUndo();

            // Wheels are cosmetic, and must have no collider: the model does the suspension,
            // and a collider on a wheel would be a second, fighting suspension.
            float halfTrack = definition.trackWidth * 0.5f;
            float front = definition.wheelbase * (1f - definition.frontWeightBias);
            float rear = -definition.wheelbase * definition.frontWeightBias;
            float radius = definition.tyreFront.radius;
            float drop = radius - definition.cgHeight;
            var positions = new[]
            {
                new Vector3(-halfTrack, drop, front), new Vector3(halfTrack, drop, front),
                new Vector3(-halfTrack, drop, rear), new Vector3(halfTrack, drop, rear),
            };
            string[] names = { "Wheel FL", "Wheel FR", "Wheel RL", "Wheel RR" };
            var visuals = new Transform[4];
            // Each wheel is an empty pivot holding a cylinder laid on its side. CarController
            // sets the pivot's rotation to spin and steer every frame, which on a bare
            // cylinder wiped out the 90 degrees that lays it down and stood the wheels up.
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject(names[i]) { layer = carLayer };
                pivot.transform.SetParent(car.transform, false);
                pivot.transform.localPosition = positions[i];

                var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tyre.name = "Tyre";
                Object.DestroyImmediate(tyre.GetComponent<Collider>());
                tyre.layer = carLayer;
                tyre.transform.SetParent(pivot.transform, false);
                tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tyre.transform.localScale = new Vector3(radius * 2f, 0.12f, radius * 2f);
                visuals[i] = pivot.transform;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.layer = carLayer;
            body.transform.SetParent(car.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            body.transform.localScale = new Vector3(1.8f, 0.7f, 4.3f);

            var settings = new SerializedObject(controller);
            settings.FindProperty("definition").objectReferenceValue = definition;
            settings.FindProperty("driver").objectReferenceValue = driver;
            // Everything except the car's own layer: the wheel probes start inside the body,
            // and a mask that includes it reports every wheel fully compressed.
            settings.FindProperty("groundLayers").intValue = ~(1 << carLayer) & ~(1 << 2);
            var wheels = settings.FindProperty("wheelVisuals");
            wheels.arraySize = 4;
            for (int i = 0; i < 4; i++) wheels.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            return car;
        }

        /// <summary>A two by two checker, point filtered so the squares stay sharp.</summary>
        static Texture2D EnsureChecker()
        {
            var checker = AssetDatabase.LoadAssetAtPath<Texture2D>(CheckerPath);
            if (checker != null) return checker;

            checker = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "Checker", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat,
            };
            var light = new Color(0.85f, 0.85f, 0.85f);
            var dark = new Color(0.35f, 0.35f, 0.35f);
            checker.SetPixels(new[] { light, dark, dark, light });
            checker.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(CheckerPath));
            AssetDatabase.CreateAsset(checker, CheckerPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(CheckerPath);
        }

        /// <summary>A URP Lit material, made once and reused, so edits to it survive rebuilds.</summary>
        internal static Material EnsureMaterial(string path, Color colour, Texture2D texture, Vector2 tiling)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Lit shader not found; is URP installed?");
            material = new Material(shader);
            material.SetColor("_BaseColor", colour);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", tiling);
            }
            material.SetFloat("_Smoothness", 0.2f);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        internal static PhysicsMaterial EnsureAsphalt()
        {
            var asphalt = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(AsphaltPath);
            if (asphalt != null) return asphalt;

            // Friction 1 is the grip multiplier the tyre coefficients were measured at.
            // The model reads the surface's friction, so anything else scales every tyre.
            asphalt = new PhysicsMaterial("Asphalt") { dynamicFriction = 1f, staticFriction = 1f };
            Directory.CreateDirectory(Path.GetDirectoryName(AsphaltPath));
            AssetDatabase.CreateAsset(asphalt, AsphaltPath);
            return asphalt;
        }

        internal static CarDefinition EnsureDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<CarDefinition>(DefinitionPath);
            if (definition != null) return definition;

            // A fresh asset holds the reference car the headless harness validates.
            definition = ScriptableObject.CreateInstance<CarDefinition>();
            Directory.CreateDirectory(Path.GetDirectoryName(DefinitionPath));
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            return definition;
        }

        internal static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = layerName;
                tags.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            throw new System.InvalidOperationException("No free user layer for the car.");
        }

        /// <summary>
        /// Adds the two trigger axes DriverInput reads. On Windows an XInput pad reports the
        /// left trigger as the 9th joystick axis and the right as the 10th, each 0 at rest
        /// and 1 fully pressed. Pads that differ need only the axis number changed here or
        /// in Project Settings, Input Manager.
        /// </summary>
        internal static void EnsureTriggerAxes()
        {
            var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
            var axes = manager.FindProperty("m_Axes");
            AddJoystickAxis(axes, "Throttle", 9);
            AddJoystickAxis(axes, "Brake", 8);
            manager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddJoystickAxis(SerializedProperty axes, string name, int zeroBasedAxis)
        {
            for (int i = 0; i < axes.arraySize; i++)
                if (axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue == name) return;

            axes.arraySize++;
            var axis = axes.GetArrayElementAtIndex(axes.arraySize - 1);
            axis.FindPropertyRelative("m_Name").stringValue = name;
            axis.FindPropertyRelative("descriptiveName").stringValue = "";
            axis.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
            axis.FindPropertyRelative("negativeButton").stringValue = "";
            axis.FindPropertyRelative("positiveButton").stringValue = "";
            axis.FindPropertyRelative("altNegativeButton").stringValue = "";
            axis.FindPropertyRelative("altPositiveButton").stringValue = "";
            axis.FindPropertyRelative("gravity").floatValue = 0f;
            axis.FindPropertyRelative("dead").floatValue = 0.05f;
            axis.FindPropertyRelative("sensitivity").floatValue = 1f;
            axis.FindPropertyRelative("snap").boolValue = false;
            axis.FindPropertyRelative("invert").boolValue = false;
            axis.FindPropertyRelative("type").intValue = 2;   // joystick axis
            axis.FindPropertyRelative("axis").intValue = zeroBasedAxis;
            axis.FindPropertyRelative("joyNum").intValue = 0; // any pad
        }

        internal static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes) if (s.path == path) return;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(path, true),
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
