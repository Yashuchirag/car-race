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
    /// No collider: the barriers keep every car off it.
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

        const string LayerPath = "Assets/Materials/GroundGrass.terrainlayer";
        const string TexturePath = "Assets/Art/Ground/grass_flat.png";
        const string MaterialPath = "Assets/Materials/Terrain.mat";
        static readonly Color GrassColour = new Color(0.22f, 0.42f, 0.16f);   // the verges' colour

        public static Terrain Build(GameObject parent, Vector3[] centre, float[] widthLeft, float[] widthRight,
                                    float vergeWidthM, string dataPath)
        {
            int n = centre.Length;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in centre)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            var origin = new Vector3(minX - MarginM, minY - 20f, minZ - MarginM);
            var size = new Vector3(maxX - minX + 2f * MarginM, maxY - minY + 60f, maxZ - minZ + 2f * MarginM);

            // The smooth field, on a coarse grid.
            var coarse = new float[CoarseResolution, CoarseResolution];
            float softening2 = SofteningM * SofteningM;
            for (int j = 0; j < CoarseResolution; j++)
            for (int i = 0; i < CoarseResolution; i++)
            {
                float x = origin.x + size.x * i / (CoarseResolution - 1);
                float z = origin.z + size.z * j / (CoarseResolution - 1);
                double sum = 0, weights = 0;
                for (int k = 0; k < n; k += CoarseEverySamples)
                {
                    float dx = centre[k].x - x, dz = centre[k].z - z;
                    double w = 1.0 / (dx * dx + dz * dz + softening2);
                    sum += w * centre[k].y;
                    weights += w;
                }
                coarse[j, i] = (float)(sum / weights);
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
            data.terrainLayers = new[] { EnsureLayer() };
            Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

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
            return terrain;
        }

        /// <summary>One grass layer, a flat colour until there are textures to paint with.</summary>
        static TerrainLayer EnsureLayer()
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (layer != null) return layer;

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath) == null)
            {
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = GrassColour;
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
                File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(TexturePath);
            }

            layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath),
                tileSize = new Vector2(10f, 10f),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(LayerPath));
            AssetDatabase.CreateAsset(layer, LayerPath);
            return layer;
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
