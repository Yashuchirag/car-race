using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The car's looks, in four designs the lobby offers (Designs), each in the manner of a real
    /// car without copying one: a rear-engined GT with a swan-neck wing, a muscle car with a
    /// long bonnet and a ducktail, a mid-engined wedge supercar with a low wing, and a tall hot
    /// hatch with a roof spoiler. They replaced a box on four cylinders.
    /// The designs are looks only: wheels, wheelbase and handling are the same for all.
    ///
    /// Every design is modelled in Blender (Tools/blender/car.py, which writes one FBX per
    /// design to Assets/Art/Cars): a subdivision body with shut lines, mirrors, LED lamps,
    /// exhausts and its wing or spoiler, and an interior behind clear glass that the cockpit
    /// camera sits in, at the design's eye (Eyes), marked by a "Cockpit" child of the car. The
    /// paint is URP's Complex Lit with a clear coat (MakePaint), a glossy layer over a slightly
    /// metallic base, as car paint is.
    ///
    /// Only the painted panels are on "Body", so the colour picker's property block and the AI
    /// cars' materials recolour the paint and nothing else. Glass, black trim, the lights, the
    /// interior and chrome are on "Body Details", in that order of materials.
    /// The wheels come from Blender (Tools/blender/wheel.py, which writes Assets/Art/Wheel): a
    /// tyre whose tread, shoulder blocks and sidewall lettering are a baked normal map, a
    /// ten-spoke alloy rim with ambient occlusion baked in, a vented disc and a caliper. Each
    /// wheel pivot, which CarController steers, holds the caliper and a "Wheel" child that it
    /// spins, so the caliper steers with the wheel but stays put as it rolls. The left wheels
    /// are the right ones turned half round, not mirrored, so their lettering still reads; only
    /// the caliper is mirrored, to stay behind the axle. None of it has a collider: the car's
    /// box does that.
    ///
    /// The designs' meshes are imported from Assets/Art/Cars and listed in the CarDesigns asset
    /// in Resources, which the game uses to put the chosen design on the player's car.
    /// </summary>
    public static class CarModel
    {
        const string CatalogPath = "Assets/Resources/" + CarDesigns.ResourceName + ".asset";
        public static readonly string[] Designs = { "GT", "Muscle", "Supercar", "Hot Hatch" };
        public const int GT = 0, Muscle = 1, Supercar = 2, HotHatch = 3;
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

        /// <summary>Each design's driver's eye, in metres above the road: the "eye" of its
        /// cabin in Tools/blender/car.py, round which that design's seat, wheel and dash are
        /// laid out. The cockpit camera sits there.</summary>
        static readonly Vector3[] Eyes =
        {
            new Vector3(-0.37f, 1.02f, -0.38f), new Vector3(-0.37f, 1.05f, -0.62f),
            new Vector3(-0.37f, 0.98f, -0.30f), new Vector3(-0.37f, 1.18f, -0.25f),
        };

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
            Material[] detailMaterials =
            {
                ClearGlass(), Lit(TrimPath, new Color(0.035f, 0.035f, 0.04f), 0.35f, 0f),
                Glow(HeadlightPath, new Color(1f, 0.97f, 0.9f) * 2.2f),
                Glow(TaillightPath, new Color(1.3f, 0.02f, 0.02f)),   // brighter and the bloom turns it orange
                Lit(InteriorPath, new Color(0.075f, 0.075f, 0.08f), 0.25f, 0f),
                Lit(ChromePath, new Color(0.85f, 0.85f, 0.87f), 0.9f, 1f),
            };
            for (int d = 0; d < Designs.Length; d++)
            {
                var (body, details) = BlenderDesign(definition, Designs[d]);
                Vector3 eye = Eyes[d];
                catalog.designs.Add(new CarDesigns.Design
                {
                    name = Designs[d], body = body, details = details, detailMaterials = detailMaterials,
                    eye = new Vector3(eye.x, eye.y - definition.cgHeight, eye.z),
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
            var cockpit = new GameObject(CarCamera.CockpitName) { layer = layer };
            cockpit.transform.SetParent(car, false);
            cockpit.transform.localPosition = catalog.designs[design].eye;

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
