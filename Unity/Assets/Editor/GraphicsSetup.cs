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

        // The sky: Poly Haven's "Kloofendal 48d Partly Cloudy (Pure Sky)" by Greg Zaal and
        // Jarod Guest, CC0, 4096 x 2048. A pure sky has no ground in it, so our own track and
        // verges meet it at the horizon.
        const string SkyTexturePath = "Assets/Art/Sky/kloofendal_48d_partly_cloudy_puresky_4k.hdr";
        const string SkyMaterialPath = "Assets/Art/Sky/Sky.mat";
        const string LightingSettingsPath = "Assets/Settings/TrackLighting.lighting";

        // Measured from the image rather than set by eye, by Tools/sky_analysis.py on the 1k
        // version of the sky: the centroid of the sun's pixels, turned into a direction with the same
        // formula the Skybox/Panoramic shader uses, so the light comes from where the sun is
        // drawn. 47.9 degrees up. Its colour is the sun pixels' average, and its intensity
        // the sun's illuminance over pi, which sits it right against the ambient light the
        // sky itself gives at exposure 1: on level ground the sun is 2.3 times the sky.
        static readonly Vector3 TowardsSun = new Vector3(0.5543f, 0.7416f, -0.3778f);
        static readonly Color SunColour = new Color(0.974f, 1f, 0.936f);
        const float SunIntensity = 1.44f;

        // The sky's mean colour in the 5 degrees above the horizon, linear. Distance fog fades
        // the far ground to this, so it meets the sky rather than ending against it.
        static readonly Color HorizonLinear = new Color(0.439f, 0.472f, 0.573f);
        // Night, for the neon city: set by eye, since there is no sky image to measure. A dark
        // procedural sky, a dim blue moon where the sun would be, fixed ambient colours (the
        // dark sky alone would leave everything unlit black), and a navy haze.
        const string NightSkyMaterialPath = "Assets/Materials/NightSky.mat";
        static readonly Vector3 TowardsMoon = new Vector3(-0.4f, 0.6f, 0.5f);
        static readonly Color MoonColour = new Color(0.62f, 0.72f, 1f);
        const float MoonIntensity = 0.18f;
        static readonly Color NightFog = new Color(0.035f, 0.04f, 0.085f);

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
            EnsureSkyMaterial();
            EnsureQualityLevels(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log($"Graphics settings applied to {PipelinePath}: MSAA {pipeline.msaaSampleCount}x, " +
                      $"{pipeline.colorGradingMode} grading, shadows {pipeline.shadowDistance} m in " +
                      $"{pipeline.shadowCascadeCount} cascades, opaque texture {pipeline.supportsCameraOpaqueTexture}; " +
                      $"post-processing profile at {TrackProfilePath}.");
        }

        // Quality levels: the one above is High; Medium and Low are copies of it with less to
        // draw, for machines slower than the development laptop. Names are what the game
        // looks the levels up by (DisplaySettings.QualityNames).
        const string PipelineMediumPath = "Assets/Settings/Medium_RPAsset.asset";
        const string PipelineLowPath = "Assets/Settings/Low_RPAsset.asset";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string RendererNoAoPath = "Assets/Settings/NoAO_Renderer.asset";

        /// <summary>
        /// Low, Medium and High quality levels, each with its own URP asset, in place of the
        /// template's single "PC" level, which becomes High. Separate assets rather than one
        /// changed while the game runs, because changing an asset in play mode overwrites it
        /// in the project. Low and Medium share a renderer with ambient occlusion off. Run
        /// again, it updates the levels instead of adding more. The template's Mobile level is
        /// left alone; it is excluded from PC builds.
        ///
        ///            MSAA  shadows                       AO   render scale        LOD bias
        ///   High     4x    150 m, 4 cascades, 2048, soft  on   100%                2
        ///   Medium   2x    100 m, 2 cascades, 2048, soft  off  100%                1.5
        ///   Low      off    60 m, 1 cascade, 1024, hard   off  80%, FSR upscaled   1
        /// </summary>
        static void EnsureQualityLevels(UniversalRenderPipelineAsset high)
        {
            if (AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererNoAoPath) == null)
                AssetDatabase.CopyAsset(RendererPath, RendererNoAoPath);
            var noAo = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererNoAoPath);
            foreach (ScriptableRendererFeature feature in noAo.rendererFeatures)
                if (feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion") feature.SetActive(false);
            EditorUtility.SetDirty(noAo);

            var medium = PipelineCopy(PipelineMediumPath, noAo);
            medium.msaaSampleCount = 2;
            medium.shadowDistance = 100f;
            medium.shadowCascadeCount = 2;
            medium.renderScale = 1f;
            SetShadowDetail(medium, 2048, soft: true);

            var low = PipelineCopy(PipelineLowPath, noAo);
            low.msaaSampleCount = 1;
            low.shadowDistance = 60f;
            low.shadowCascadeCount = 1;
            low.renderScale = 0.8f;
            low.upscalingFilter = UpscalingFilterSelection.FSR;
            SetShadowDetail(low, 1024, soft: false);

            SetShadowDetail(high, 2048, soft: true);

            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = settings.FindProperty("m_QualitySettings");
            int highIndex = LevelIndex(levels, "High");
            if (highIndex < 0)
            {
                int pc = LevelIndex(levels, "PC");
                if (pc < 0) throw new InvalidOperationException("Neither a High nor a PC quality level was found.");
                levels.InsertArrayElementAtIndex(pc);   // each insert copies the level at pc
                levels.InsertArrayElementAtIndex(pc);
                levels.GetArrayElementAtIndex(pc).FindPropertyRelative("name").stringValue = "Low";
                levels.GetArrayElementAtIndex(pc + 1).FindPropertyRelative("name").stringValue = "Medium";
                levels.GetArrayElementAtIndex(pc + 2).FindPropertyRelative("name").stringValue = "High";
                highIndex = pc + 2;
            }
            Level(levels, "Low", low, lodBias: 1f, anisotropic: 1);
            Level(levels, "Medium", medium, lodBias: 1.5f, anisotropic: 2);
            Level(levels, "High", high, lodBias: 2f, anisotropic: 2);

            settings.FindProperty("m_CurrentQuality").intValue = highIndex;
            var defaults = settings.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
            {
                var pair = defaults.GetArrayElementAtIndex(i);
                if (pair.FindPropertyRelative("first").stringValue == "Standalone")
                    pair.FindPropertyRelative("second").intValue = highIndex;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static UniversalRenderPipelineAsset PipelineCopy(string path, ScriptableRendererData renderer)
        {
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path) == null)
                AssetDatabase.CopyAsset(PipelinePath, path);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            var serialized = new SerializedObject(asset);
            var list = serialized.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.colorGradingMode = ColorGradingMode.HighDynamicRange;
            asset.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>Shadow map resolution and soft shadows have no public setters.</summary>
        static void SetShadowDetail(UniversalRenderPipelineAsset asset, int resolution, bool soft)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_MainLightShadowmapResolution").intValue = resolution;
            serialized.FindProperty("m_SoftShadowsSupported").boolValue = soft;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static int LevelIndex(SerializedProperty levels, string name)
        {
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
            return -1;
        }

        /// <summary>A level's pipeline and the few settings URP leaves to the level. Its vSync is
        /// left at 0: the player's frame rate choice (DisplaySettings) owns that.</summary>
        static void Level(SerializedProperty levels, string name, UniversalRenderPipelineAsset pipeline,
                          float lodBias, int anisotropic)
        {
            var level = levels.GetArrayElementAtIndex(LevelIndex(levels, name));
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
            level.FindPropertyRelative("lodBias").floatValue = lodBias;
            level.FindPropertyRelative("anisotropicTextures").intValue = anisotropic;
            level.FindPropertyRelative("vSyncCount").intValue = 0;
            level.FindPropertyRelative("antiAliasing").intValue = 0;   // MSAA is the pipeline's
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

        /// <summary>
        /// The HDR sky, imported as one 2D latitude-longitude image for Skybox/Panoramic: no
        /// mipmaps, which make a seam where the image wraps, and BC6H, the compression made
        /// for HDR. The material is created once and left alone after.
        /// </summary>
        public static Material EnsureSkyMaterial()
        {
            var importer = AssetImporter.GetAtPath(SkyTexturePath) as TextureImporter
                ?? throw new FileNotFoundException($"{SkyTexturePath} not found; download it from Poly Haven, see Unity/README.md.");
            bool changed = importer.textureShape != TextureImporterShape.Texture2D || importer.mipmapEnabled
                           || importer.maxTextureSize != 4096 || importer.textureCompression != TextureImporterCompression.CompressedHQ;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.mipmapEnabled = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (changed) importer.SaveAndReimport();

            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (material != null) return material;
            material = new Material(Shader.Find("Skybox/Panoramic")
                                    ?? throw new InvalidOperationException("Skybox/Panoramic shader not found."));
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexturePath));
            material.SetFloat("_Mapping", 1f);      // latitude-longitude layout
            material.SetFloat("_ImageType", 0f);    // 360 degrees
            material.SetFloat("_Exposure", 1f);
            material.SetFloat("_Rotation", 0f);     // TowardsSun assumes no rotation
            AssetDatabase.CreateAsset(material, SkyMaterialPath);
            return material;
        }

        /// <summary>
        /// The sky, a sun lined up with the one in it, light and reflections from the sky, and
        /// distance fog in the horizon's colour, for the open scene. Then bakes the environment
        /// lighting: with baked and realtime GI both off, that is only the ambient probe and the
        /// reflection cubemap, a few seconds, and nothing is lightmapped yet. At night, the
        /// night sky, moon, ambient and haze instead.
        /// </summary>
        public static void SetUpSkyAndSun(Light sun, bool night = false)
        {
            if (night) { SetUpNight(sun); return; }
            RenderSettings.skybox = EnsureSkyMaterial();

            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.LookRotation(-TowardsSun.normalized, Vector3.up);
                sun.color = SunColour;
                sun.intensity = SunIntensity;
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 3500f;
            RenderSettings.fogColor = HorizonLinear.gamma;   // colours are authored in sRGB
            BakeEnvironment();
        }

        static void BakeEnvironment()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "TrackLighting", bakedGI = false, realtimeGI = false };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }
            Lightmapping.lightingSettings = settings;
            if (!Lightmapping.Bake())
                Debug.LogWarning("Environment lighting bake did not complete; ambient light will be flat until it is baked.");
        }

        static void SetUpNight(Light moon)
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(NightSkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")
                                   ?? throw new InvalidOperationException("Skybox/Procedural shader not found."));
                AssetDatabase.CreateAsset(sky, NightSkyMaterialPath);
            }
            sky.SetFloat("_SunDisk", 0f);
            sky.SetFloat("_AtmosphereThickness", 0.35f);
            sky.SetColor("_SkyTint", new Color(0.25f, 0.3f, 0.6f));
            sky.SetColor("_GroundColor", new Color(0.05f, 0.05f, 0.08f));
            sky.SetFloat("_Exposure", 0.12f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            if (moon != null)
            {
                moon.type = LightType.Directional;
                moon.transform.rotation = Quaternion.LookRotation(-TowardsMoon.normalized, Vector3.up);
                moon.color = MoonColour;
                moon.intensity = MoonIntensity;
                moon.shadows = LightShadows.Soft;
                RenderSettings.sun = moon;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.18f, 0.32f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.10f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.04f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 2000f;
            RenderSettings.fogColor = NightFog;
            BakeEnvironment();
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
