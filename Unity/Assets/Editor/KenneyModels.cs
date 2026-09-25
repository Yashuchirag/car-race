using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The Kenney CC0 models under Assets/Art/Kenney (see Art/CREDITS.md). Each model file
    /// carries its own copy of materials the whole kit shares by name ("leafsGreen",
    /// "woodBark"), so a forest of different trees would draw with dozens of materials that
    /// all look the same. Ensure remaps every model's materials, by name, onto one shared
    /// material per name in the kit's Materials folder, made once from the first copy found.
    ///
    /// The shared materials are matte, since the kits are flat coloured, and can be drawn
    /// instanced. Their colours are Kenney's, as the kits' own previews show them. Kenney's leaves are a blue green and its bark orange, which beside the
    /// photographed grass read as toys; Recolour moves them to natural greens and browns and
    /// leaves every shape as it is.
    /// </summary>
    public static class KenneyModels
    {
        public const string Folder = "Assets/Art/Kenney";

        static readonly Dictionary<string, Color> Recolour = new Dictionary<string, Color>
        {
            ["leafsGreen"] = new Color(0.33f, 0.56f, 0.20f),
            ["leafsDark"] = new Color(0.20f, 0.40f, 0.15f),
            ["woodBark"] = new Color(0.40f, 0.29f, 0.20f),
            ["woodBarkDark"] = new Color(0.30f, 0.22f, 0.16f),
            ["grass"] = new Color(0.28f, 0.50f, 0.18f),
            ["stone"] = new Color(0.62f, 0.48f, 0.34f),      // sandstone, for the desert
        };

        public static void Ensure()
        {
            foreach (string kit in AssetDatabase.GetSubFolders(Folder))
            {
                string materials = $"{kit}/Materials";
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { kit }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    var remaps = importer.GetExternalObjectMap();
                    bool changed = false;
                    foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    {
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name);
                        if (remaps.ContainsKey(id)) continue;
                        string sharedPath = $"{materials}/{embedded.name}.mat";
                        var shared = AssetDatabase.LoadAssetAtPath<Material>(sharedPath);
                        if (shared == null)
                        {
                            Directory.CreateDirectory(materials);
                            shared = new Material(embedded);
                            // The importer reads the FBX colour as linear, which brightens it
                            // (the kit's red 0.91, 0.33, 0.33 came out pink); Kenney means it as
                            // the colour seen, so it is put back once, here.
                            shared.SetColor("_BaseColor", embedded.GetColor("_BaseColor").linear);
                            AssetDatabase.CreateAsset(shared, sharedPath);
                        }
                        importer.AddRemap(id, shared);
                        changed = true;
                    }
                    if (changed) importer.SaveAndReimport();
                }

                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { materials }))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    material.enableInstancing = true;
                    if (material.name != "glass") material.SetFloat("_Smoothness", 0.1f);
                    if (Recolour.TryGetValue(material.name, out Color colour)) material.SetColor("_BaseColor", colour);
                    EditorUtility.SetDirty(material);
                }
            }
            AssetDatabase.SaveAssets();
        }

        public static GameObject Load(string kit, string model) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{kit}/{model}.fbx")
            ?? throw new FileNotFoundException($"{Folder}/{kit}/{model}.fbx not found; see Art/CREDITS.md for the kits.");
    }
}
