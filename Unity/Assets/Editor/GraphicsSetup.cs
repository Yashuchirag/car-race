using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The render settings the graphics pass starts from, applied by script rather than by
    /// hand so that they are written down and can be applied again: CarRace, Apply Graphics
    /// Settings, or from the command line with the editor closed,
    ///
    ///   Unity.exe -batchmode -quit -projectPath D:\Dev\CarRace
    ///     -executeMethod CarRace.UnityGame.EditorTools.GraphicsSetup.Apply
    ///
    /// The project came from the Universal 3D template and was still on its defaults.
    /// MSAA 4x, not temporal anti-aliasing: TAA smears a scene that moves at 250 km/h, and
    /// MSAA keeps thin barriers and road lines sharp. Shadows reach 150 m, two seconds at
    /// racing speed, where 50 m let them appear just ahead of the car. Colour grading in
    /// HDR, so tone mapping sees the whole range. The opaque texture, a copy of the screen
    /// each frame for refraction effects the game does not have, is off.
    /// </summary>
    public static class GraphicsSetup
    {
        const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
        public const string TrackProfilePath = "Assets/Settings/TrackPostProcessing.asset";

        [MenuItem("CarRace/Apply Graphics Settings")]
        public static void Apply()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath)
                ?? throw new FileNotFoundException($"{PipelinePath} not found.");
            pipeline.msaaSampleCount = 4;
            pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
            pipeline.shadowDistance = 150f;
            pipeline.shadowCascadeCount = 4;
            pipeline.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipeline);

            EnsureTrackProfile();
            AssetDatabase.SaveAssets();
            Debug.Log($"Graphics settings applied to {PipelinePath}: MSAA {pipeline.msaaSampleCount}x, " +
                      $"{pipeline.colorGradingMode} grading, shadows {pipeline.shadowDistance} m in " +
                      $"{pipeline.shadowCascadeCount} cascades, opaque texture {pipeline.supportsCameraOpaqueTexture}; " +
                      $"post-processing profile at {TrackProfilePath}.");
        }

        /// <summary>
        /// A deliberate starting look for the circuits: ACES tone mapping, a light bloom that
        /// only catches what is brighter than white, and a slight vignette. Created once and
        /// then left alone, so changes made to it in the editor are kept. Motion blur is left
        /// out until there is art for it to smear.
        /// </summary>
        public static VolumeProfile EnsureTrackProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(TrackProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, TrackProfilePath);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.7f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.4f);

            // Components live inside the profile asset, as Unity's own inspector keeps them.
            foreach (VolumeComponent component in profile.components)
            {
                component.name = component.GetType().Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>A global volume with the track profile, and post-processing on for the
        /// camera. Unity's default camera has it off, so no volume would ever render.</summary>
        public static void AddPostProcessing(GameObject parent, Camera camera)
        {
            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.transform.SetParent(parent.transform, false);
            volume.isGlobal = true;
            volume.sharedProfile = EnsureTrackProfile();

            if (camera == null) return;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;   // MSAA comes from the pipeline asset
            if (!data.renderPostProcessing)
                throw new InvalidOperationException("The camera did not keep post-processing on.");
        }
    }
}
