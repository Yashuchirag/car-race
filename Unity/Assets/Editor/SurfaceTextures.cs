using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The surfaces' textures, set up and applied in one place so every scene gets the same:
    /// for now grass, ambientCG's Grass005 (CC0), on the circuits' verges, the terrain out to
    /// the horizon and the lobby's lawn. Import settings are written here too, since a normal
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
        const float TerrainTileM = 4f;      // a little larger far away, where repeats show most

        /// <summary>Grass on a material whose mesh is in metres, or whose tiling is given.</summary>
        public static void ApplyGrass(Material material, Vector2? tiling = null)
        {
            material.SetColor("_BaseColor", Color.white);
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

        public static void ApplyGrass(TerrainLayer layer)
        {
            layer.diffuseTexture = Texture(GrassColour, TextureKind.Colour);
            layer.normalMapTexture = Texture(GrassNormal, TextureKind.Normal);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(TerrainTileM, TerrainTileM);
            layer.smoothness = 0.08f;
            EditorUtility.SetDirty(layer);
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
