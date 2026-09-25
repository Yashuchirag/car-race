using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Ground out to the horizon around a circuit, as a Unity Terrain. Without it the world
    /// ended at the outside of the verges, and past them the lower half of the sky showed
    /// where ground should be.
    ///
    /// The terrain reaches MarginM past the circuit on every side, beyond the fog's end, so
    /// its edge is never seen. Its height follows the circuit's: an inverse distance weighted
    /// average of the centreline's height, softened over SofteningM so it rises and falls
    /// gently between distant parts of the lap. Within CorridorM of the road it is held
    /// DropM below the verge, which is flat across at the road edge's height, so the ground
    /// can never show through the verge or the road. Its heightmap cells are several metres
    /// across, and the corridor reaches past the barrier by more than a cell, so the slope
    /// between a held cell and a free one always falls outside the barrier.
    ///
    /// The theme decides the rest. Its ground covers the land, and ground steeper than
    /// RockFromDegrees turns to its steep surface, fully so by RockFullDegrees. Given
    /// MountainsM, mountains rise from MountainFromM beyond the nearest centreline sample to
    /// their full height by MountainFullM: up to about 1.5 times MountainsM, as ridges (Perlin
    /// noise folded about its middle) with smaller hills on them. Given
    /// sea, the land off the circuit's longer side drops below the water within a short
    /// shore at a wandering coastline CoastBeyondM past it, so a long stretch of the lap runs
    /// along the sea. The water sits SeaBelowTrackM below the lowest road near the coast, not
    /// the lowest on the lap, or on a hilly circuit the land would hide it; the flat, glossy
    /// plane only covers the seaward side, so it cannot rise through a lower road elsewhere.
    /// The mountains give way before the coast, the land within NearCoastM of it flattens to
    /// a plain no higher than the lowest road there, and near it ground within BeachAboveM
    /// of the water is beach sand.
    ///
    /// No collider: the barriers keep every car off it. Surfaces from SurfaceTextures.
    /// </summary>
    public static class GroundBuilder
    {
        const float MarginM = 3000f;
        const float SofteningM = 60f;
        const float DropM = 0.4f;
        const float CorridorBeyondBarrierM = 12f;
        const int Resolution = 2049;         // heightmap samples along each side
        const int CoarseResolution = 257;    // the smooth field is computed here and interpolated
        const int CoarseEverySamples = 10;   // centreline samples used for it, 20 m apart

        const float MountainFromM = 300f;
        const float MountainFullM = 1800f;
        const float RockFromDegrees = 24f;
        const float RockFullDegrees = 34f;
        const int AlphamapResolution = 1024;
        const float CoastBeyondM = 120f;
        const float CoastWanderM = 80f;       // either way
        const float NearCoastM = 400f;
        const float SeaBelowTrackM = 1.5f;
        const float BeachAboveM = 1.5f;
        const string SeaMaterialPath = "Assets/Materials/Sea.mat";

        const string LayerFolder = "Assets/Materials";
        const string MaterialPath = "Assets/Materials/Terrain.mat";

        public static Terrain Build(GameObject parent, Vector3[] centre, float[] widthLeft, float[] widthRight,
                                    float vergeWidthM, string dataPath, Theme theme)
        {
            float mountainsM = theme.MountainsM;
            int n = centre.Length;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in centre)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            var origin = new Vector3(minX - MarginM, minY - 40f, minZ - MarginM);
            // Where the land ends: a wandering line off the circuit's longer side.
            bool wide = maxX - minX >= maxZ - minZ;
            Vector3 seaward = wide ? Vector3.back : Vector3.right, alongShore = wide ? Vector3.right : Vector3.forward;
            float furthest = float.MinValue;
            foreach (Vector3 p in centre) furthest = Mathf.Max(furthest, Vector3.Dot(p, seaward));
            float nearCoastY = float.MaxValue;
            foreach (Vector3 p in centre)
                if (Vector3.Dot(p, seaward) > furthest - NearCoastM) nearCoastY = Mathf.Min(nearCoastY, p.y);
            float seaLevel = nearCoastY - SeaBelowTrackM;
            if (theme.Sea) theme.SeaLevelY = seaLevel;
            float Inland(float x, float z)
            {
                var p = new Vector3(x, 0f, z);
                float coast = furthest + CoastBeyondM + 2f * CoastWanderM * (Mathf.PerlinNoise(Vector3.Dot(p, alongShore) / 900f + 3f, 0.5f) - 0.5f);
                return coast - Vector3.Dot(p, seaward);
            }
            if (theme.Sea) theme.Inland = p => Inland(p.x, p.z);
            var size = new Vector3(maxX - minX + 2f * MarginM, maxY - minY + 80f + 1.6f * mountainsM, maxZ - minZ + 2f * MarginM);

            // The smooth field, on a coarse grid.
            var coarse = new float[CoarseResolution, CoarseResolution];
            var rise = new float[CoarseResolution, CoarseResolution];   // 0 to 1, how far into the mountains
            float softening2 = SofteningM * SofteningM;
            for (int j = 0; j < CoarseResolution; j++)
            for (int i = 0; i < CoarseResolution; i++)
            {
                float x = origin.x + size.x * i / (CoarseResolution - 1);
                float z = origin.z + size.z * j / (CoarseResolution - 1);
                double sum = 0, weights = 0;
                float nearest2 = float.MaxValue;
                for (int k = 0; k < n; k += CoarseEverySamples)
                {
                    float dx = centre[k].x - x, dz = centre[k].z - z;
                    nearest2 = Mathf.Min(nearest2, dx * dx + dz * dz);
                    double w = 1.0 / (dx * dx + dz * dz + softening2);
                    sum += w * centre[k].y;
                    weights += w;
                }
                coarse[j, i] = (float)(sum / weights);
                rise[j, i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MountainFromM, MountainFullM, Mathf.Sqrt(nearest2)));
            }

            // Interpolated up to the heightmap.
            var heights = new float[Resolution, Resolution];
            for (int j = 0; j < Resolution; j++)
            for (int i = 0; i < Resolution; i++)
            {
                float u = (float)i / (Resolution - 1) * (CoarseResolution - 1);
                float v = (float)j / (Resolution - 1) * (CoarseResolution - 1);
                int i0 = Mathf.Min((int)u, CoarseResolution - 2), j0 = Mathf.Min((int)v, CoarseResolution - 2);
                float fu = u - i0, fv = v - j0;
                heights[j, i] = Mathf.Lerp(Mathf.Lerp(coarse[j0, i0], coarse[j0, i0 + 1], fu),
                                           Mathf.Lerp(coarse[j0 + 1, i0], coarse[j0 + 1, i0 + 1], fu), fv);
                if (mountainsM > 0f)
                {
                    float r = Mathf.Lerp(Mathf.Lerp(rise[j0, i0], rise[j0, i0 + 1], fu),
                                         Mathf.Lerp(rise[j0 + 1, i0], rise[j0 + 1, i0 + 1], fu), fv);
                    float x = origin.x + size.x * i / (Resolution - 1), z = origin.z + size.z * j / (Resolution - 1);
                    float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x / 1100f + 37f, z / 1100f + 11f) - 1f);
                    float hills = Mathf.PerlinNoise(x / 300f + 5f, z / 300f + 71f);
                    float inland = theme.Sea ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(100f, 900f, Inland(x, z))) : 1f;
                    heights[j, i] += mountainsM * r * inland * (0.3f + 0.9f * ridge + 0.3f * hills);
                }
                if (theme.Sea)
                {
                    float x = origin.x + size.x * i / (Resolution - 1), z = origin.z + size.z * j / (Resolution - 1);
                    // A coastal plain no higher than the lowest road near the coast, so the
                    // land falls away to the water rather than rising between it and the road.
                    float inlandM = Inland(x, z);
                    float plain = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearCoastM, 150f, inlandM));
                    heights[j, i] = Mathf.Lerp(heights[j, i], Mathf.Min(heights[j, i], nearCoastY - 0.5f), plain);
                    float sea = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, -120f, inlandM));
                    heights[j, i] = Mathf.Lerp(heights[j, i], seaLevel - 12f, sea);
                }
            }

            // Held under the road, the verges and a little past the barrier.
            float cellX = size.x / (Resolution - 1), cellZ = size.z / (Resolution - 1);
            for (int k = 0; k < n; k++)
            {
                float reach = Mathf.Max(widthLeft[k], widthRight[k]) + vergeWidthM + CorridorBeyondBarrierM;
                float limit = centre[k].y - DropM;
                int ci = Mathf.RoundToInt((centre[k].x - origin.x) / cellX);
                int cj = Mathf.RoundToInt((centre[k].z - origin.z) / cellZ);
                int ri = Mathf.CeilToInt(reach / cellX), rj = Mathf.CeilToInt(reach / cellZ);
                for (int j = Mathf.Max(0, cj - rj); j <= Mathf.Min(Resolution - 1, cj + rj); j++)
                for (int i = Mathf.Max(0, ci - ri); i <= Mathf.Min(Resolution - 1, ci + ri); i++)
                {
                    float dx = origin.x + i * cellX - centre[k].x, dz = origin.z + j * cellZ - centre[k].z;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    if (heights[j, i] > limit) heights[j, i] = limit;
                }
            }

            // Terrain heights are shares of size.y above the origin.
            for (int j = 0; j < Resolution; j++)
            for (int i = 0; i < Resolution; i++)
                heights[j, i] = Mathf.Clamp01((heights[j, i] - origin.y) / size.y);

            var data = new TerrainData { heightmapResolution = Resolution };
            data.size = size;
            data.SetHeights(0, 0, heights);
            data.terrainLayers = new[] { EnsureLayer(theme.Ground), EnsureLayer(theme.Steep), EnsureLayer(Surface.Beach) };
            Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);
            // After the asset exists: the paint lives in textures kept inside it, and painted
            // before, they were never saved and the rock came out as grass.
            Paint(data, origin, theme.Sea ? seaLevel - origin.y : float.NegativeInfinity, theme.Inland);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Ground";
            Object.DestroyImmediate(go.GetComponent<TerrainCollider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = EnsureMaterial();
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 1000f;
            terrain.drawInstanced = true;
            // Gentle land has little shadow worth casting, and casting it drew the whole
            // terrain into four shadow cascades every frame.
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // From just inside the nearest the coast comes to the circuit, out to the edge.
            if (theme.Sea) AddSea(parent, origin, size, seaward, furthest + CoastBeyondM - CoastWanderM - 30f, seaLevel);
            return terrain;
        }

        /// <summary>The layer for a surface, one asset per surface, shared by every circuit.</summary>
        static TerrainLayer EnsureLayer(Surface surface)
        {
            string path = $"{LayerFolder}/Ground{surface}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                Directory.CreateDirectory(LayerFolder);
                AssetDatabase.CreateAsset(layer, path);
            }
            SurfaceTextures.Apply(layer, surface);
            return layer;
        }

        /// <summary>Layer 0, the theme's ground, everywhere but: layer 1, its steep surface,
        /// where the ground is steep; layer 2, beach, near the coast within BeachAboveM of the
        /// sea.</summary>
        static void Paint(TerrainData data, Vector3 origin, float seaLocalY, System.Func<Vector3, float> inland)
        {
            Vector3 size = data.size;
            data.alphamapResolution = AlphamapResolution;
            int res = data.alphamapResolution;
            var maps = new float[res, res, 3];
            for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float u = (i + 0.5f) / res, v = (j + 0.5f) / res;
                float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RockFromDegrees, RockFullDegrees, data.GetSteepness(u, v)));
                float beach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(seaLocalY + BeachAboveM - 1f, seaLocalY + BeachAboveM + 1f,
                                                                                 data.GetInterpolatedHeight(u, v)));
                if (inland == null || inland(origin + new Vector3(u * size.x, 0f, v * size.z)) > NearCoastM) beach = 0f;
                beach *= 1f - steep;
                maps[j, i, 0] = 1f - steep - beach;
                maps[j, i, 1] = steep;
                maps[j, i, 2] = beach;
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>The sea: one flat plane over the terrain from fromU (along seaward) to its
        /// edge, glossy so it takes the sky's reflection.</summary>
        static void AddSea(GameObject parent, Vector3 origin, Vector3 size, Vector3 seaward, float fromU, float level)
        {
            var sea = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sea.name = "Sea";
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.transform.SetParent(parent.transform, false);
            // The terrain's rectangle, cut back to the seaward side of fromU.
            float x0 = origin.x, x1 = origin.x + size.x, z0 = origin.z, z1 = origin.z + size.z;
            if (seaward == Vector3.right) x0 = Mathf.Max(x0, fromU);
            else z1 = Mathf.Min(z1, -fromU);      // seaward is back: u = -z
            sea.transform.position = new Vector3((x0 + x1) * 0.5f, level, (z0 + z1) * 0.5f);
            sea.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            sea.transform.localScale = new Vector3(x1 - x0, z1 - z0, 1f);

            var material = AssetDatabase.LoadAssetAtPath<Material>(SeaMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, SeaMaterialPath);
            }
            material.SetColor("_BaseColor", new Color(0.07f, 0.24f, 0.32f));
            material.SetFloat("_Smoothness", 0.92f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            var renderer = sea.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(sea, StaticEditorFlags.BatchingStatic);
        }

        static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit")
                ?? throw new System.InvalidOperationException("URP Terrain/Lit shader not found.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
