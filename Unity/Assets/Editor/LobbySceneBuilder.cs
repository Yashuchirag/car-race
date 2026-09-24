using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Builds the lobby, the scene the game opens on: CarRace, Build Lobby Scene. The circuits'
    /// sky, sun and post-processing, a dark platform on open ground, the car turning on it with
    /// everything that drives it taken off, a camera looking at it from the front quarter, and
    /// LobbyMenu. Put first in the build settings, so a built game starts here.
    /// </summary>
    public static class LobbySceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Lobby.unity";
        const string FloorMaterialPath = "Assets/Materials/LobbyFloor.mat";
        const string PlatformMaterialPath = "Assets/Materials/LobbyPlatform.mat";

        [MenuItem("CarRace/Build Lobby Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int carLayer = SkidpadSceneBuilder.EnsureLayer(SkidpadSceneBuilder.CarLayerName);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var definition = SkidpadSceneBuilder.EnsureDefinition();
            AssetDatabase.SaveAssets();

            var root = new GameObject("Lobby");

            // Open ground and a low platform for the car.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            var lawn = SkidpadSceneBuilder.EnsureMaterial(FloorMaterialPath, new Color(0.22f, 0.4f, 0.17f), null, Vector2.one);
            // A plane's texture coordinates run 0 to 1 over its 2 km, so the tiling is given.
            SurfaceTextures.ApplyGrass(lawn, Vector2.one * (2000f / SurfaceTextures.GrassTileM));
            floor.GetComponent<Renderer>().sharedMaterial = lawn;
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Platform";
            platform.transform.SetParent(root.transform, false);
            platform.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            platform.transform.localScale = new Vector3(7f, 0.05f, 7f);
            platform.GetComponent<Renderer>().sharedMaterial =
                SkidpadSceneBuilder.EnsureMaterial(PlatformMaterialPath, new Color(0.12f, 0.12f, 0.14f), null, Vector2.one);

            // The car, for looks only: what drives it is removed, controller before body since
            // the controller requires the body.
            GameObject car = SkidpadSceneBuilder.BuildCar(carLayer, definition, withDriver: false);
            car.name = "Display Car";
            Object.DestroyImmediate(car.GetComponent<CarController>());
            Object.DestroyImmediate(car.GetComponent<Rigidbody>());
            Object.DestroyImmediate(car.GetComponent<BoxCollider>());
            car.transform.SetParent(root.transform, false);
            car.transform.SetPositionAndRotation(new Vector3(0f, 0.1f + definition.cgHeight, 0f), Quaternion.Euler(0f, 210f, 0f));
            var body = SkidpadSceneBuilder.EnsureMaterial(SkidpadSceneBuilder.BodyMaterialPath, new Color(0.8f, 0.1f, 0.08f), null, Vector2.one);
            var tyre = SkidpadSceneBuilder.EnsureMaterial(SkidpadSceneBuilder.TyreMaterialPath, new Color(0.08f, 0.08f, 0.08f), null, Vector2.one);
            foreach (var r in car.GetComponentsInChildren<Renderer>()) r.sharedMaterial = r.name == "Body" ? body : tyre;

            // Camera at the front quarter, a little above, looking at the car, left of centre
            // on screen so the car panel on the right does not cover it.
            var camera = Camera.main;
            camera.transform.SetPositionAndRotation(new Vector3(6.8f, 2.1f, 8.4f), Quaternion.identity);
            camera.transform.LookAt(new Vector3(-1.4f, 0.2f, 0f));
            camera.fieldOfView = 40f;
            camera.farClipPlane = 3000f;
            GraphicsSetup.AddPostProcessing(root, camera);

            var menu = new SerializedObject(root.AddComponent<LobbyMenu>());
            menu.FindProperty("displayCar").objectReferenceValue = car.transform;
            menu.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/Settings/TrackCatalog.asset");
            menu.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            GraphicsSetup.SetUpSkyAndSun(Object.FindAnyObjectByType<Light>());
            EditorSceneManager.SaveScene(scene, ScenePath);
            PutFirstInBuild(ScenePath);
            Debug.Log($"Lobby built at {ScenePath}, first in the build settings.");
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
