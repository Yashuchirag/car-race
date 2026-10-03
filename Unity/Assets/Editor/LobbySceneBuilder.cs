using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Builds the lobby, the scene the game opens on: CarRace, Build Lobby Scene. A team's pit
    /// garage (Tools/blender/scenery.py, garage) with the car turning on its turntable, everything
    /// that drives it taken off; the roller door open onto the pit lane, the pit wall, the track
    /// and a grandstand beyond, under the circuits' sky and sun; lights under the garage's strip
    /// lights and a reflection probe for its glossy floor; the camera inside near the back,
    /// looking past the car and out through the door; and LobbyMenu, whose glass panels blur a
    /// copy of this view (GlassBackdrop). Put first in the build settings, so a built game
    /// starts here.
    /// </summary>
    public static class LobbySceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Lobby.unity";
        const string FloorMaterialPath = "Assets/Materials/LobbyFloor.mat";

        [MenuItem("CarRace/Build Lobby Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int carLayer = SkidpadSceneBuilder.EnsureLayer(SkidpadSceneBuilder.CarLayerName);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var definition = SkidpadSceneBuilder.EnsureDefinition();
            AssetDatabase.SaveAssets();

            var root = new GameObject("Lobby");

            // The garage, and outside its door the pit lane, the pit wall, the track, grass and
            // a grandstand facing the pits.
            var garage = (GameObject)PrefabUtility.InstantiatePrefab(StructureModels.BlenderModel("garage"));
            garage.transform.SetParent(root.transform, false);
            Outside(root);
            Lights(root);

            // The car, for looks only: what drives it is removed, controller before body since
            // the controller requires the body.
            GameObject car = SkidpadSceneBuilder.BuildCar(carLayer, definition, withDriver: false);
            car.name = "Display Car";
            Object.DestroyImmediate(car.GetComponent<CarController>());
            Object.DestroyImmediate(car.GetComponent<Rigidbody>());
            Object.DestroyImmediate(car.GetComponent<BoxCollider>());
            car.transform.SetParent(root.transform, false);
            car.transform.SetPositionAndRotation(new Vector3(0f, 0.045f + definition.cgHeight, 0f), Quaternion.Euler(0f, 210f, 0f));
            var body = SkidpadSceneBuilder.EnsureMaterial(SkidpadSceneBuilder.BodyMaterialPath, new Color(0.8f, 0.1f, 0.08f), null, Vector2.one);
            foreach (var r in car.GetComponentsInChildren<Renderer>()) if (r.name == "Body") r.sharedMaterial = body;

            // Camera inside near the back, a little above, looking past the car and out through
            // the door, the car in the middle of the screen between the panels.
            var camera = Camera.main;
            camera.transform.SetPositionAndRotation(CameraAt, Quaternion.identity);
            camera.transform.LookAt(CameraLooksAt);
            camera.fieldOfView = 44f;
            camera.farClipPlane = 3000f;
            GraphicsSetup.AddPostProcessing(root, camera);

            var menu = new SerializedObject(root.AddComponent<LobbyMenu>());
            menu.FindProperty("displayCar").objectReferenceValue = car.transform;
            menu.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/Settings/TrackCatalog.asset");
            menu.FindProperty("car").objectReferenceValue = definition;
            menu.FindProperty("glass").objectReferenceValue = GlassMaterial();
            menu.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            GraphicsSetup.SetUpSkyAndSun(Object.FindAnyObjectByType<Light>());
            EditorSceneManager.SaveScene(scene, ScenePath);
            PutFirstInBuild(ScenePath);
            Debug.Log($"Lobby built at {ScenePath}, first in the build settings.");
        }

        /// <summary>The lobby's glass panels' material, on Shaders/GlassPanel.shader; an asset so
        /// the shader is in the build.</summary>
        static Material GlassMaterial()
        {
            const string path = "Assets/Materials/LobbyGlass.mat";
            var shader = Shader.Find("CarRace/Glass Panel");
            if (shader == null) { Debug.LogWarning("CarRace/Glass Panel shader not found: the lobby's panels will be flat."); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            EditorUtility.SetDirty(m);
            return m;
        }

        static readonly Vector3 CameraAt = new Vector3(2.8f, 2.0f, 8.3f);
        static readonly Vector3 CameraLooksAt = new Vector3(-0.3f, 0.7f, -2.5f);
        /// <summary>The garage's door, its front wall's outside face (scenery.py, garage: D + t).</summary>
        const float FrontZ = -9.3f;

        /// <summary>Outside the door (the garage's front is -z, from FrontZ): the pit lane, the
        /// pit wall with a fence, the track, grass, and a grandstand across the track.</summary>
        static void Outside(GameObject root)
        {
            var outside = new GameObject("Outside");
            outside.transform.SetParent(root.transform, false);
            Material road = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Road.mat")
                ?? SkidpadSceneBuilder.EnsureMaterial("Assets/Materials/LobbyRoad.mat", new Color(0.2f, 0.2f, 0.21f), null, Vector2.one);
            Material lawn = SkidpadSceneBuilder.EnsureMaterial(FloorMaterialPath, new Color(0.22f, 0.4f, 0.17f), null, Vector2.one);
            SurfaceTextures.ApplyGrass(lawn, Vector2.one * (2000f / SurfaceTextures.GrassTileM));

            void Slab(string name, Vector3 centre, Vector3 size, Material material)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = name;
                Object.DestroyImmediate(slab.GetComponent<Collider>());
                slab.transform.SetParent(outside.transform, false);
                slab.transform.localPosition = centre;
                slab.transform.localScale = size;
                slab.GetComponent<Renderer>().sharedMaterial = material;
                slab.isStatic = true;
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Grass";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.SetParent(outside.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            ground.transform.localScale = new Vector3(200f, 1f, 200f);
            ground.GetComponent<Renderer>().sharedMaterial = lawn;
            float wall = FrontZ - 15f;
            Slab("Pit Lane", new Vector3(0f, -0.01f, (FrontZ + wall) / 2f), new Vector3(120f, 0.02f, 15.4f), road);
            Slab("Track", new Vector3(0f, -0.01f, wall - 7.5f), new Vector3(400f, 0.02f, 13f), road);
            Slab("Pit Wall", new Vector3(0f, 0.5f, wall), new Vector3(120f, 1f, 0.5f), StructureModels.Named("Concrete"));
            Slab("Fence Rail", new Vector3(0f, 3.2f, wall), new Vector3(120f, 0.08f, 0.08f), StructureModels.Named("Steel"));
            for (int k = -12; k <= 12; k++)
                Slab("Fence Post", new Vector3(k * 5f, 2.1f, wall), new Vector3(0.08f, 2.2f, 0.08f), StructureModels.Named("Steel"));

            // The grandstand across the track, facing the pits: its front is -z, so turned round.
            GameObject bay = StructureModels.BlenderModel("stand_bay");
            for (int k = -4; k <= 4; k++)
            {
                var stand = (GameObject)PrefabUtility.InstantiatePrefab(bay);
                stand.transform.SetParent(outside.transform, false);
                stand.transform.SetPositionAndRotation(new Vector3(k * 12f, 0f, FrontZ - 75f), Quaternion.Euler(0f, 180f, 0f));
            }
        }

        /// <summary>A light under each row of strip lights, and a reflection probe so the
        /// epoxy floor and the car's paint reflect the garage rather than the open sky.</summary>
        static void Lights(GameObject root)
        {
            var lights = new GameObject("Garage Lights");
            lights.transform.SetParent(root.transform, false);
            // Four, not one per strip light: the garage is one mesh, and URP lights a mesh with
            // only so many lights.
            foreach (float x in new[] { -2.6f, 2.6f })
            foreach (float z in new[] { -4f, 4f })
            {
                var light = new GameObject("Strip Light").AddComponent<Light>();
                light.transform.SetParent(lights.transform, false);
                light.transform.localPosition = new Vector3(x, 4.3f, z);
                light.type = LightType.Point;
                light.range = 14f;
                light.intensity = 14f;
                light.color = new Color(1f, 0.96f, 0.9f);
                light.shadows = LightShadows.None;
            }
            var probe = new GameObject("Garage Reflections").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root.transform, false);
            probe.transform.localPosition = new Vector3(0f, 2f, 0f);
            probe.size = new Vector3(12.6f, 5.2f, 18.6f);
            probe.boxProjection = true;
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 256;
        }

        public static void BuildFromCommandLine()
        {
            Build();
            AssetDatabase.SaveAssets();
        }

        static void PutFirstInBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
