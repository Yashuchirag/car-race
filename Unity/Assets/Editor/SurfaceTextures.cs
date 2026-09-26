using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>What ground is made of, for the terrain's layers and the verges.</summary>
    public enum Surface { Grass, Rock, Sand, Beach, DesertRock, Concrete }

    /// <summary>
    /// The surfaces' textures, set up and applied in one place so every scene gets the same:
    /// grass, ambientCG's Grass005 (CC0), on the circuits' verges, the terrain out to the
    /// horizon and the lobby's lawn; asphalt, Poly Haven's Asphalt Track (CC0), on the road; and the
    /// other surfaces a theme's ground can be (Surface), also ambientCG's. Import settings are written here too, since a normal
    /// map imported as a colour texture shades wrongly and nothing says so.
    ///
    /// The verge meshes carry texture coordinates in metres, so a texture tiles at a real
    /// size (GrassTileM) wherever it is used.
    /// </summary>
    public static class SurfaceTextures
    {
        const string GrassFolder = "Assets/Art/Ground/Grass005";
        const string GrassColour = GrassFolder + "/Grass005_2K-JPG_Color.jpg";
        const string GrassNormal = GrassFolder + "/Grass005_2K-JPG_NormalGL.jpg";
        const string GrassOcclusion = GrassFolder + "/Grass005_2K-JPG_AmbientOcclusion.jpg";
        public const float GrassTileM = 3f;
        // Grass005 photographs a bright lawn; under a full sun it read almost fluorescent.
        // Multiplied by this it is the olive of a mown verge in summer.
        static readonly Color GrassTint = new Color(0.8f, 0.84f, 0.68f);

        const string AsphaltFolder = "Assets/Art/Ground/AsphaltTrack";
        const string AsphaltColour = AsphaltFolder + "/asphalt_track_diff_2k.jpg";
        const string AsphaltNormal = AsphaltFolder + "/asphalt_track_nor_gl_2k.jpg";
        const string AsphaltOcclusion = AsphaltFolder + "/asphalt_track_ao_2k.jpg";
        // Made from the roughness map by Tools/smoothness_map.py: URP reads smoothness from
        // the metallic map's alpha. 0.12 to 0.29, a matte surface with some grain to its sheen.
        const string AsphaltSmoothness = AsphaltFolder + "/asphalt_track_metallic_smoothness_2k.png";
        const float AsphaltTileM = 2f;      // the texture's real size, 2 m square
        const float TerrainTileM = 4f;      // a little larger far away, where repeats show most

        /// <summary>The ambientCG material each surface is (all CC0), its size on the ground,
        /// and its smoothness. Rock is large since it is mostly seen on distant slopes.</summary>
        static (string id, float tileM, float smoothness) Source(Surface surface)
        {
            switch (surface)
            {
                case Surface.Rock: return ("Rock030", 12f, 0.1f);
                case Surface.Sand: return ("Ground097", 6f, 0.05f);
                case Surface.Beach: return ("Ground093A", 4f, 0.1f);
                case Surface.DesertRock: return ("Rock061", 12f, 0.08f);
                case Surface.Concrete: return ("Concrete047A", 4f, 0.15f);
                default: return ("Grass005", TerrainTileM, 0.08f);
            }
        }

        /// <summary>Grass on a material whose mesh is in metres, or whose tiling is given.</summary>
        public static void ApplyGrass(Material material, Vector2? tiling = null)
        {
            material.SetColor("_BaseColor", GrassTint);
            material.SetTexture("_BaseMap", Texture(GrassColour, TextureKind.Colour));
            material.SetTexture("_BumpMap", Texture(GrassNormal, TextureKind.Normal));
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_OcclusionMap", Texture(GrassOcclusion, TextureKind.Data));
            material.SetFloat("_OcclusionStrength", 0.8f);
            material.SetFloat("_Smoothness", 0.08f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            material.SetTextureScale("_BaseMap", tiling ?? Vector2.one / GrassTileM);
            EditorUtility.SetDirty(material);
        }

        /// <summary>Asphalt on the road material, whose mesh is in metres.</summary>
        public static void ApplyAsphalt(Material material)
        {
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", Texture(AsphaltColour, TextureKind.Colour));
            material.SetTexture("_BumpMap", Texture(AsphaltNormal, TextureKind.Normal));
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_OcclusionMap", Texture(AsphaltOcclusion, TextureKind.Data));
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetTexture("_MetallicGlossMap", Texture(AsphaltSmoothness, TextureKind.Data));
            material.SetFloat("_SmoothnessTextureChannel", 0f);   // the metallic map's alpha
            material.SetFloat("_Smoothness", 1f);                 // a multiplier once a map is set
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTextureScale("_BaseMap", Vector2.one / AsphaltTileM);
            EditorUtility.SetDirty(material);
        }

        public static void ApplyGrass(TerrainLayer layer)
        {
            layer.diffuseTexture = Texture(GrassColour, TextureKind.Colour);
            layer.normalMapTexture = Texture(GrassNormal, TextureKind.Normal);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(TerrainTileM, TerrainTileM);
            layer.diffuseRemapMax = new Vector4(GrassTint.r, GrassTint.g, GrassTint.b, 1f);
            layer.smoothness = 0.08f;
            EditorUtility.SetDirty(layer);
        }

        /// <summary>Any surface on a terrain layer.</summary>
        public static void Apply(TerrainLayer layer, Surface surface)
        {
            if (surface == Surface.Grass) { ApplyGrass(layer); return; }
            var (id, tileM, smoothness) = Source(surface);
            string folder = $"Assets/Art/Ground/{id}/{id}_2K-JPG";
            layer.diffuseTexture = Texture(folder + "_Color.jpg", TextureKind.Colour);
            layer.normalMapTexture = Texture(folder + "_NormalGL.jpg", TextureKind.Normal);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tileM, tileM);
            layer.smoothness = smoothness;
            EditorUtility.SetDirty(layer);
        }

        /// <summary>Any surface on a material whose mesh is in metres (the verges).</summary>
        public static void Apply(Material material, Surface surface)
        {
            if (surface == Surface.Grass) { ApplyGrass(material); return; }
            var (id, tileM, smoothness) = Source(surface);
            string folder = $"Assets/Art/Ground/{id}/{id}_2K-JPG";
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", Texture(folder + "_Color.jpg", TextureKind.Colour));
            material.SetTexture("_BumpMap", Texture(folder + "_NormalGL.jpg", TextureKind.Normal));
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_OcclusionMap", null);
            material.DisableKeyword("_OCCLUSIONMAP");
            material.SetFloat("_Smoothness", smoothness);
            material.EnableKeyword("_NORMALMAP");
            material.SetTextureScale("_BaseMap", Vector2.one / tileM);
            EditorUtility.SetDirty(material);
        }

        enum TextureKind { Colour, Normal, Data }

        /// <summary>Loads a texture after making its import settings right: normal maps as
        /// normal maps, data (occlusion) as linear, everything mipmapped and capped at 2048.</summary>
        static Texture2D Texture(string path, TextureKind kind)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter
                ?? throw new FileNotFoundException($"{path} not found; see Assets/Art/CREDITS.md for where it comes from.");
            var type = kind == TextureKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = kind == TextureKind.Colour;
            if (importer.textureType != type || importer.sRGBTexture != srgb || importer.maxTextureSize != 2048
                || !importer.mipmapEnabled || importer.anisoLevel != 4)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
