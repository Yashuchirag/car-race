using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Trackside scenery in the circuit's theme (TrackSceneBuilder.Themes), from the Kenney
    /// kits (KenneyModels): countryside, mountains and forest, desert, coast, and a city by
    /// day and by night with neon and floodlights.
    ///
    /// Everything is placed by its distance from the circuit. Land is a grid over the ground,
    /// CellM across, holding each cell's distance beyond the barriers (a chamfer distance
    /// transform from the road, verges and barriers) and which cells a structure already
    /// uses. Nothing goes nearer a barrier than StructureClearM, so no car can reach it and
    /// nothing needs a collider.
    ///
    /// Structures go first, so the trees keep out of their way: pit garages and a covered
    /// grandstand either side of the start line, open grandstands with tents behind them on
    /// the outside of the tightest corners, banner towers where the braking for a corner
    /// starts, and billboards along the straights, angled towards the cars coming. They are
    /// GameObjects, static for batching.
    ///
    /// Trees and bushes are the terrain's trees, drawn instanced and kept in its data, not a
    /// GameObject each, all as meshes. What grows is the theme's Planting: woods cover its
    /// share of the land, where two octaves of noise are highest, with bushes on their edges,
    /// and single trees and boulders stand in the open between. Trees keep off ground steep
    /// enough to be rock; boulders do not. Where there is sea nothing grows on the beach or
    /// in the water, a few boulders lie along the shore, and within OpenShoreM of the coast
    /// there are no woods, only single trees and bushes, so the sea can be seen.
    /// From ThinFromM out to ReachM the woods thin to nothing. Past NearM the woods use only
    /// the simplest trees (under 135 triangles against up to 400), since they are a few pixels
    /// tall there and triangles, not pixels, were what the trees cost.
    /// </summary>
    public static class SceneryBuilder
    {
        const float CellM = 2.5f;
        const float ReachM = 700f;
        const float ThinFromM = 350f;
        const float NearM = 250f;
        const float BarrierThicknessM = 0.4f;
        const float StructureClearM = 3f;
        const float TreeClearM = 6f;
        const float TreeSpacingM = 9f;
        const float SteepDegrees = 24f;     // where GroundBuilder's rock starts

        /// <summary>What grows in a theme: the woods' trees near the circuit and past NearM,
        /// their share of the land and heights, and what stands at their edges and in the open.</summary>
        sealed class Planting
        {
            public string[] Near, Far, Lone, Edge, Boulders = new string[0];
            public float Share, MinM, MaxM, LoneChance = 0.03f, LoneMinM = 8f, LoneMaxM = 14f, BoulderChance;
        }

        static readonly Planting Countryside = new Planting
        {
            Near = new[] { "tree_default", "tree_default_dark", "tree_detailed", "tree_detailed_dark",
                           "tree_oak", "tree_oak_dark", "tree_cone", "tree_cone_dark", "tree_pineRoundA" },
            Far = new[] { "tree_default", "tree_default_dark", "tree_fat", "tree_cone", "tree_pineTallA" },
            Lone = new[] { "tree_oak", "tree_detailed", "tree_fat", "tree_default" },
            Edge = new[] { "plant_bush", "plant_bushLarge", "plant_bushDetailed" },
            Share = 0.3f, MinM = 10f, MaxM = 17f,
        };

        /// <summary>The Ardennes: dense conifer forest, pines at its edges, boulders.</summary>
        static readonly Planting Mountains = new Planting
        {
            Near = new[] { "tree_pineDefaultA", "tree_pineDefaultB", "tree_pineRoundA", "tree_pineRoundC",
                           "tree_pineRoundE", "tree_pineTallA_detailed", "tree_pineTallB_detailed", "tree_pineTallC_detailed" },
            Far = new[] { "tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_pineTallD", "tree_pineSmallC" },
            Lone = new[] { "tree_pineRoundD", "tree_pineTallB_detailed", "tree_pineDefaultA" },
            Edge = new[] { "tree_pineGroundA", "tree_pineGroundB", "tree_pineSmallC" },
            Boulders = new[] { "rock_tallC", "rock_tallG", "rock_tallI", "rock_largeB", "rock_largeD", "rock_largeF" },
            Share = 0.5f, MinM = 12f, MaxM = 22f, LoneChance = 0.04f, BoulderChance = 0.015f,
        };

        /// <summary>Desert Park: sand, palm groves as oases, cacti and sandstone boulders.</summary>
        static readonly Planting Desert = new Planting
        {
            Near = new[] { "tree_palm", "tree_palmTall", "tree_palmDetailedTall", "tree_palmBend" },
            Far = new[] { "tree_palm", "tree_palmTall", "tree_palmShort" },
            Lone = new[] { "cactus_tall", "cactus_short" },
            Edge = new[] { "plant_bushSmall", "plant_bushTriangle", "cactus_short" },
            Boulders = new[] { "stone_tallC", "stone_tallG", "stone_tallI", "stone_largeB", "stone_largeD" },
            Share = 0.07f, MinM = 8f, MaxM = 14f, LoneChance = 0.02f, LoneMinM = 2.5f, LoneMaxM = 4.5f, BoulderChance = 0.012f,
        };

        /// <summary>Ise Bay: coastal pines mixed with broadleaf woods, rocks on the shore.</summary>
        static readonly Planting Coast = new Planting
        {
            Near = new[] { "tree_pineRoundA", "tree_pineRoundC", "tree_pineRoundE", "tree_default", "tree_oak", "tree_detailed" },
            Far = new[] { "tree_default", "tree_default_dark", "tree_pineTallA", "tree_cone", "tree_fat" },
            Lone = new[] { "tree_pineRoundD", "tree_oak", "tree_pineRoundA" },
            Edge = new[] { "plant_bush", "plant_bushLarge", "plant_bushDetailed" },
            Boulders = new[] { "rock_largeA", "rock_largeC", "rock_largeE", "rock_largeB" },
            Share = 0.3f, MinM = 10f, MaxM = 16f, BoulderChance = 0.004f,
        };

        const float BeachAboveM = 2f;        // GroundBuilder's beach, and a little
        const float ShoreRockChance = 0.08f;
        const float OpenShoreM = 300f;

        public static void Build(GameObject root, Theme theme, Vector3[] centre, Vector3[] right,
                                 float[] widthLeft, float[] widthRight, float vergeWidthM, Terrain terrain)
        {
            Planting planting = theme.Name == "Countryside" ? Countryside : theme.Name == "Mountains" ? Mountains
                              : theme.Name == "Desert" ? Desert : theme.Name == "Coast" ? Coast : null;
            if (planting == null && !theme.City) return;
            KenneyModels.Ensure();

            var circuit = new Circuit(centre, right, widthLeft, widthRight, vergeWidthM + BarrierThicknessM);
            var land = new Land(circuit, terrain);
            var parent = new GameObject("Scenery");
            parent.transform.SetParent(root.transform, false);
            var rng = new Random(1);

            var structures = new GameObject("Structures");
            structures.transform.SetParent(parent.transform, false);
            int placed = Furniture(structures, land, circuit);
            var growth = new Growth(terrain);
            if (theme.City)
            {
                GameObject neon = null;
                if (theme.Night)
                {
                    neon = new GameObject("Neon");
                    neon.transform.SetParent(parent.transform, false);
                    placed += Floodlights(structures, land, circuit);
                    NightWindows(terrain.GetComponent<InstancedTrees>() ?? terrain.gameObject.AddComponent<InstancedTrees>());
                }
                int buildings = City(growth, land, rng, circuit, neon);
                Debug.Log($"Scenery, {theme.Name}: {placed} structures, {buildings} buildings, {growth.Count - buildings} park trees.");
            }
            else
            {
                int trees = Trees(growth, land, terrain, planting, theme, rng);
                Debug.Log($"Scenery, {theme.Name}: {placed} structures, {trees} trees and bushes.");
            }
            growth.Commit();
        }

        /// <summary>The racing circuit's own buildings, the same in every theme.</summary>
        static int Furniture(GameObject parent, Land land, Circuit circuit)
        {
            GameObject garage = KenneyModels.Load("Racing", "pitsGarage");
            GameObject covered = KenneyModels.Load("Racing", "grandStandCovered");
            GameObject stand = KenneyModels.Load("Racing", "grandStand");
            GameObject tent = KenneyModels.Load("Racing", "tent");
            GameObject billboard = KenneyModels.Load("Racing", "billboard");
            GameObject[] towers = { KenneyModels.Load("Racing", "bannerTowerRed"), KenneyModels.Load("Racing", "bannerTowerGreen") };

            // The start: garages on the right, a covered stand on the left.
            int placed = Row(parent, land, circuit, garage, 0, +1, -100f, 100f, 9f, StructureClearM);
            placed += Row(parent, land, circuit, covered, 0, -1, -110f, 110f, 12f, StructureClearM + 1f);

            // The tightest corners, a stand on the outside of each and tents behind it.
            List<int> corners = Corners(circuit, 110f, 300f);
            corners.Sort((a, b) => circuit.Radius(a).CompareTo(circuit.Radius(b)));
            var chosen = new List<int>();
            foreach (int k in corners)
            {
                if (chosen.Count == 4) break;
                if (circuit.ArcM(k, 0) < 250f || chosen.Exists(c => circuit.ArcM(c, k) < 300f)) continue;
                chosen.Add(k);
                int outside = -circuit.Turn(k);
                placed += Row(parent, land, circuit, stand, k, outside, -25f, 25f, 10f, StructureClearM + 2f);
                float behind = StructureClearM + 2f + Scaled(stand, 10f).z + 6f;
                placed += Row(parent, land, circuit, tent, k, outside, -12f, 12f, 8f, behind);
            }

            // A banner tower on the outside where the braking starts for every corner.
            List<int> braking = Corners(circuit, 150f, 150f);
            for (int i = 0; i < braking.Count; i++)
            {
                int k = circuit.Wrap(braking[i] - Mathf.RoundToInt(80f / circuit.Spacing));
                int outside = -circuit.Turn(braking[i]);
                placed += Row(parent, land, circuit, towers[i % 2], k, outside, 0f, 0f, 3f, StructureClearM);
            }

            // Billboards on the straights, alternate sides, angled to face the cars coming.
            int every = Mathf.RoundToInt(300f / circuit.Spacing);
            int side = +1;
            for (int k = every / 2; k < circuit.N; k += every)
            {
                if (circuit.Radius(k) < 300f || circuit.ArcM(k, 0) < 150f) continue;
                Vector3 toTrack = -circuit.Right[k] * side;
                Vector3 facing = (toTrack * Mathf.Cos(25f * Mathf.Deg2Rad) - circuit.Tangent(k) * Mathf.Sin(25f * Mathf.Deg2Rad)).normalized;
                float depth = Scaled(billboard, 10f).z;
                Vector3 at = circuit.Outside(k, side, StructureClearM + depth * 0.5f + 1f);
                if (Place(parent, land, billboard, at, facing, 10f) != null) placed++;
                side = -side;
            }
            return placed;
        }

        /// <summary>A row of <paramref name="model"/>, widthM each, along the side of the
        /// circuit from fromM to toM of track either side of sample k, its front clearM
        /// beyond the barrier and facing the track. Pieces that do not fit are left out.</summary>
        static int Row(GameObject parent, Land land, Circuit circuit, GameObject model, int k, int side,
                       float fromM, float toM, float widthM, float clearM)
        {
            float depth = Scaled(model, widthM).z;
            int first = Mathf.RoundToInt(fromM / circuit.Spacing), last = Mathf.RoundToInt(toM / circuit.Spacing);
            int placed = 0;
            Vector3? previous = null;
            for (int i = first; i <= last; i++)
            {
                int s = circuit.Wrap(k + i);
                Vector3 at = circuit.Outside(s, side, clearM + depth * 0.5f);
                if (previous.HasValue && Vector3.Distance(previous.Value, at) < widthM) continue;
                previous = at;
                if (Place(parent, land, model, at, -circuit.Right[s] * side, widthM) != null) placed++;
            }
            return placed;
        }

        /// <summary>The model's size when scaled to widthM across.</summary>
        static Vector3 Scaled(GameObject model, float widthM)
        {
            Bounds b = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            return b.size * (widthM / b.size.x);
        }

        /// <summary>The model, widthM across, centred on <paramref name="at"/>, its front (the
        /// kits' -Z) facing along <paramref name="facing"/>, standing on the lowest ground under
        /// it; or nothing if any of its footprint is too near a barrier or already taken.</summary>
        static GameObject Place(GameObject parent, Land land, GameObject model, Vector3 at, Vector3 facing, float widthM)
        {
            Bounds local = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            float scale = widthM / local.size.x;
            Quaternion rotation = Quaternion.LookRotation(-facing, Vector3.up);
            Vector3 across = rotation * Vector3.right * (local.size.x * scale * 0.5f);
            Vector3 along = rotation * Vector3.forward * (local.size.z * scale * 0.5f);
            Vector3[] corners = { at - across - along, at + across - along, at + across + along, at - across + along };

            float ground = float.MaxValue;
            foreach (Vector3 c in corners)
            {
                if (land.Beyond(c) < StructureClearM - 0.5f || land.Taken(c)) return null;
                ground = Mathf.Min(ground, land.Height(c));
            }
            if (land.Taken(at)) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent.transform);
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation = rotation;
            Vector3 offset = rotation * (local.center * scale);
            go.transform.position = new Vector3(at.x - offset.x, ground - local.min.y * scale, at.z - offset.z);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            land.Take(corners, 2f);
            return go;
        }

        /// <summary>Corner apexes: samples tighter than maxRadiusM and tightest within
        /// separationM either way.</summary>
        static List<int> Corners(Circuit circuit, float maxRadiusM, float separationM)
        {
            var apexes = new List<int>();
            int reach = Mathf.Max(1, Mathf.RoundToInt(separationM * 0.5f / circuit.Spacing));
            for (int k = 0; k < circuit.N; k++)
            {
                float r = circuit.Radius(k);
                if (r >= maxRadiusM) continue;
                bool tightest = true;
                for (int d = -reach; d <= reach && tightest; d++)
                {
                    float other = circuit.Radius(circuit.Wrap(k + d));
                    if (other < r || (other == r && d < 0)) tightest = false;
                }
                if (tightest) apexes.Add(k);
            }
            return apexes;
        }

        // ---------------------------------------------------------------- the city

        static readonly string[] Shops =
            { "building-a", "building-b", "building-c", "building-d", "building-e", "building-f", "building-g", "building-h" };
        static readonly string[] Towers =
            { "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c", "building-skyscraper-d", "building-skyscraper-e" };
        static readonly string[] Blocks =
        {
            "low-detail-building-a", "low-detail-building-b", "low-detail-building-c", "low-detail-building-d",
            "low-detail-building-e", "low-detail-building-f", "low-detail-building-g", "low-detail-building-h",
            "low-detail-building-i", "low-detail-building-j", "low-detail-building-k", "low-detail-building-l",
            "low-detail-building-m", "low-detail-building-wide-a", "low-detail-building-wide-b",
        };
        static readonly string[] ParkTrees = { "tree_default", "tree_oak", "tree_detailed", "tree_fat" };
        const float LotM = 44f;             // a building and its share of the streets
        const float CityClearM = 22f;       // open ground between the barriers and the first buildings
        const float ShopsWithinM = 140f;    // then towers and blocks, then only blocks
        const float TowersWithinM = 320f;
        const float ParkShare = 0.25f;
        const float NeonWithinM = 260f;
        static readonly Color[] NeonColours =
        {
            new Color(1f, 0.1f, 0.75f), new Color(0.1f, 0.85f, 1f), new Color(0.6f, 0.2f, 1f), new Color(1f, 0.8f, 0.15f),
        };

        /// <summary>
        /// City blocks: lots LotM apart on a grid squared to the start straight, out to
        /// ReachM, each a building or, where noise says so, a small park. The detailed shops
        /// (2,000 to 4,000 triangles) stand nearest the circuit, towers behind them, and the
        /// kit's low-detail blocks (under 800) make the skyline beyond. Buildings are terrain
        /// trees like the plants, so InstancedTrees draws them. Given a parent for neon, the
        /// buildings within NeonWithinM get a glowing band below the roof and a strip up one
        /// corner.
        /// </summary>
        static int City(Growth growth, Land land, Random rng, Circuit circuit, GameObject neon)
        {
            Vector3 t = circuit.Tangent(0);
            float yaw = Mathf.Atan2(t.x, t.z);
            Quaternion grid = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            Vector3 across = grid * Vector3.right, along = grid * Vector3.forward;
            var mid = new Vector3((land.MinX + land.MaxX) * 0.5f, 0f, (land.MinZ + land.MaxZ) * 0.5f);
            float half = 0.5f * Mathf.Sqrt((land.MaxX - land.MinX) * (land.MaxX - land.MinX) + (land.MaxZ - land.MinZ) * (land.MaxZ - land.MinZ));
            float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;
            Material[] neonMaterials = neon != null ? Array.ConvertAll(NeonColours, NeonMaterial) : null;

            int buildings = 0;
            for (float a = -half; a < half; a += LotM)
            for (float b = -half; b < half; b += LotM)
            {
                Vector3 c = mid + across * a + along * b;
                float beyond = land.Beyond(c);
                if (beyond < CityClearM || beyond > ReachM || land.Taken(c)) continue;

                if (Mathf.PerlinNoise(c.x / 260f + ox, c.z / 260f + oz) < 0.5f - ParkShare * 0.6f)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = c + across * (float)(rng.NextDouble() - 0.5) * LotM * 0.7f + along * (float)(rng.NextDouble() - 0.5) * LotM * 0.7f;
                        if (land.Beyond(p) < TreeClearM) continue;
                        GameObject tree = KenneyModels.Load("Nature", ParkTrees[rng.Next(ParkTrees.Length)]);
                        float scale = Mathf.Lerp(8f, 13f, (float)rng.NextDouble()) / tree.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
                        growth.Add(tree, p, scale, 1f, (float)rng.NextDouble() * Mathf.PI * 2f);
                    }
                    continue;
                }

                string[] set = beyond < ShopsWithinM ? Shops : beyond < TowersWithinM && rng.NextDouble() < 0.5 ? Towers : Blocks;
                GameObject model = KenneyModels.Load("Commercial", set[rng.Next(set.Length)]);
                Bounds bounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
                float footprint = Mathf.Lerp(22f, 30f, (float)rng.NextDouble());
                float s = footprint / Mathf.Max(bounds.size.x, bounds.size.z);
                // Every corner of it clear of the circuit, not only its middle.
                float r = 0.5f * Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z) * s;
                if (beyond - r < CityClearM * 0.5f) continue;
                float turn = yaw + rng.Next(4) * Mathf.PI * 0.5f;
                growth.Add(model, c, s, 1f, turn);
                buildings++;
                if (neonMaterials != null && beyond < NeonWithinM)
                    Neon(neon, c + Vector3.up * land.Height(c), Quaternion.Euler(0f, turn * Mathf.Rad2Deg, 0f), bounds, s,
                         neonMaterials[rng.Next(neonMaterials.Length)], neonMaterials[rng.Next(neonMaterials.Length)], rng);
            }
            return buildings;
        }

        /// <summary>A band round the building below its roof and a strip up one corner, as
        /// glowing boxes. Unlit, far brighter than white, so the bloom makes them glow.</summary>
        static void Neon(GameObject parent, Vector3 foot, Quaternion rotation, Bounds local, float scale,
                         Material band, Material strip, Random rng)
        {
            Vector3 size = local.size * scale, centre = local.center * scale;
            centre.y = 0f;
            const float Thick = 0.6f;
            Bar(parent, foot + rotation * centre + Vector3.up * size.y * 0.88f, rotation,
                new Vector3(size.x + Thick, Thick, size.z + Thick), band);
            float sx = rng.Next(2) == 0 ? -0.5f : 0.5f, sz = rng.Next(2) == 0 ? -0.5f : 0.5f;
            Vector3 corner = new Vector3(size.x * sx, size.y * 0.42f, size.z * sz);
            Bar(parent, foot + rotation * (centre + corner), rotation, new Vector3(Thick, size.y * 0.8f, Thick), strip);
        }

        static void Bar(GameObject parent, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.name = "Neon";
            bar.transform.SetParent(parent.transform, false);
            bar.transform.SetPositionAndRotation(position, rotation);
            bar.transform.localScale = size;
            var renderer = bar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(bar, StaticEditorFlags.BatchingStatic);
        }

        static Material NeonMaterial(Color colour)
        {
            string path = $"Assets/Materials/Neon {ColorUtility.ToHtmlStringRGB(colour)}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour.linear * 6f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Street lights along the circuit, alternate sides, FloodlightEveryM apart:
        /// a steel pole just behind the barrier, an arm out over the road's edge with a brace,
        /// and a glowing lamp head LampHeightM up shining straight down, in wide pools that
        /// overlap so the whole road is lit, not patches of it. The pole is built here rather
        /// than taken from the kit: the kit's post needed more room than the stands and garages
        /// on the start straight leave, and those lamps were left hanging in the air. Aimed from
        /// the post instead of down, the light reached the dark asphalt at a grazing angle and
        /// barely showed. No shadows: a lap of them would be dozens of shadow maps.</summary>
        static int Floodlights(GameObject parent, Land land, Circuit circuit)
        {
            var steel = AssetDatabase.LoadAssetAtPath<Material>($"{KenneyModels.Folder}/Racing/Materials/grey.mat");
            Material head = NeonMaterial(new Color(1f, 0.93f, 0.8f));
            int every = Mathf.RoundToInt(FloodlightEveryM / circuit.Spacing);
            int placed = 0, side = 1;
            for (int k = 0; k < circuit.N; k += every, side = -side)
            {
                Vector3 lampAt = circuit.Edge(k, side) + Vector3.up * LampHeightM;
                Vector3 foot = circuit.Outside(k, side, PoleBeyondBarrierM);
                foot.y = Mathf.Min(land.Height(foot), circuit.Centre[k].y) - 0.5f;
                Vector3 top = new Vector3(foot.x, lampAt.y + 0.4f, foot.z);
                Vector3 reach = lampAt - top;
                reach.y = 0f;

                Bar(parent, (foot + top) * 0.5f, Quaternion.identity, new Vector3(0.4f, top.y - foot.y, 0.4f), steel);
                Bar(parent, top + reach * 0.5f, Quaternion.LookRotation(reach.normalized), new Vector3(0.3f, 0.3f, reach.magnitude), steel);
                // A brace from a third of the way down the pole to a third of the way out.
                Vector3 low = top + Vector3.down * (LampHeightM / 3f), mid = top + reach / 3f;
                Bar(parent, (low + mid) * 0.5f, Quaternion.LookRotation((mid - low).normalized), new Vector3(0.2f, 0.2f, (mid - low).magnitude), steel);
                Bar(parent, lampAt, Quaternion.LookRotation(circuit.Tangent(k)), new Vector3(0.9f, 0.3f, 2.2f), head);

                var lamp = new GameObject("Street Light").AddComponent<Light>();
                lamp.transform.SetParent(parent.transform, true);
                lamp.transform.position = lampAt + Vector3.down * 0.3f;
                lamp.transform.rotation = Quaternion.LookRotation(Vector3.down, circuit.Tangent(k));
                lamp.type = LightType.Spot;
                lamp.spotAngle = 160f;
                lamp.innerSpotAngle = 120f;
                lamp.range = 60f;
                lamp.intensity = FloodlightIntensity;
                lamp.color = new Color(1f, 0.94f, 0.84f);
                lamp.shadows = LightShadows.None;
                placed++;
            }
            return placed;
        }

        const float FloodlightEveryM = 25f;
        const float FloodlightIntensity = 1400f;  // falls with distance squared: 2.5 or more anywhere on the road
        const float LampHeightM = 12f;
        const float PoleBeyondBarrierM = 0.8f;   // inside the gap StructureClearM keeps free

        /// <summary>At night the buildings' windows glow: each Commercial material has a copy
        /// that emits its own colours, faintly, and InstancedTrees draws with the copies here.</summary>
        static void NightWindows(InstancedTrees trees)
        {
            var from = new List<Material>();
            var to = new List<Material>();
            string folder = $"{KenneyModels.Folder}/Commercial/Materials";
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(" Night.mat")) continue;
                var day = AssetDatabase.LoadAssetAtPath<Material>(path);
                string nightPath = path.Replace(".mat", " Night.mat");
                var night = AssetDatabase.LoadAssetAtPath<Material>(nightPath);
                if (night == null)
                {
                    night = new Material(day);
                    AssetDatabase.CreateAsset(night, nightPath);
                }
                night.CopyPropertiesFromMaterial(day);
                night.SetTexture("_EmissionMap", day.GetTexture("_BaseMap"));
                night.SetColor("_EmissionColor", new Color(0.12f, 0.1f, 0.09f));
                night.EnableKeyword("_EMISSION");
                // Realtime rather than None: URP reads None as no emission and drops the keyword.
                night.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(night);
                from.Add(day);
                to.Add(night);
            }
            trees.SetMaterialSwaps(from.ToArray(), to.ToArray());
            EditorUtility.SetDirty(trees);
        }

        // ---------------------------------------------------------------- trees

        static int Trees(Growth growth, Land land, Terrain terrain, Planting planting, Theme theme, Random rng)
        {
            float seaLevelY = theme.SeaLevelY;
            TerrainData data = terrain.terrainData;
            int before = growth.Count;

            // Candidates on a jittered grid, with their noise, so the woods' threshold can be
            // set to cover the planting's share of the land whatever the noise's spread.
            float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;
            Vector3 origin = terrain.transform.position, size = data.size;
            var candidates = new List<(Vector3 p, float beyond, float noise, bool steep, bool shore, bool open)>();
            for (float x = land.MinX; x < land.MaxX; x += TreeSpacingM)
            for (float z = land.MinZ; z < land.MaxZ; z += TreeSpacingM)
            {
                var p = new Vector3(x + ((float)rng.NextDouble() - 0.5f) * TreeSpacingM * 0.8f, 0f,
                                    z + ((float)rng.NextDouble() - 0.5f) * TreeSpacingM * 0.8f);
                float beyond = land.Beyond(p);
                if (beyond < TreeClearM || beyond > ReachM || land.Taken(p)) continue;
                float noise = 0.7f * Mathf.PerlinNoise(p.x / 230f + ox, p.z / 230f + oz)
                            + 0.3f * Mathf.PerlinNoise(p.x / 60f + oz, p.z / 60f + ox);
                noise -= 0.2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinFromM, ReachM, beyond));
                bool steep = data.GetSteepness((p.x - origin.x) / size.x, (p.z - origin.z) / size.z) > SteepDegrees;
                float height = land.Height(p);
                bool coastal = theme.Inland != null && theme.Inland(p) < 400f;
                if (coastal && height < seaLevelY + 0.3f) continue;      // in the water
                bool open = theme.Inland != null && theme.Inland(p) < OpenShoreM;
                candidates.Add((p, beyond, noise, steep, coastal && height < seaLevelY + BeachAboveM, open));
            }
            var sorted = candidates.FindAll(c => !c.open).ConvertAll(c => c.noise);
            sorted.Sort();
            float woods = sorted.Count > 0 ? sorted[(int)((1f - planting.Share) * (sorted.Count - 1))] : 1f;

            void Add(string[] set, float minM, float maxM, Vector3 p)
            {
                GameObject model = KenneyModels.Load("Nature", set[rng.Next(set.Length)]);
                float height = Mathf.Lerp(minM, maxM, (float)rng.NextDouble());
                float scale = height / model.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
                float width = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
                growth.Add(model, p, scale, width, (float)rng.NextDouble() * Mathf.PI * 2f);
            }
            foreach (var (p, beyond, noise, steep, shore, open) in candidates)
            {
                double roll = rng.NextDouble();
                if (shore) { if (roll < ShoreRockChance) Add(planting.Boulders, 1f, 3f, p); }
                else if (roll < planting.BoulderChance) Add(planting.Boulders, 1.5f, 4f, p);
                else if (steep) continue;
                else if (open) { if (roll < planting.LoneChance * 2f) Add(planting.Lone, planting.LoneMinM, planting.LoneMaxM, p);
                                 else if (roll < planting.LoneChance * 5f) Add(planting.Edge, 1.2f, 2.2f, p); }
                else if (noise >= woods) { if (roll < 0.85) Add(beyond < NearM ? planting.Near : planting.Far, planting.MinM, planting.MaxM, p); }
                else if (noise >= woods - 0.03f) { if (roll < 0.35) Add(planting.Edge, 1.2f, 2.2f, p); }
                else if (roll < planting.LoneChance) Add(planting.Lone, planting.LoneMinM, planting.LoneMaxM, p);
            }
            return growth.Count - before;
        }

        /// <summary>What the terrain draws as its trees, plants, rocks and buildings alike:
        /// collected here and handed to the terrain once, for InstancedTrees to draw.</summary>
        sealed class Growth
        {
            readonly Terrain _terrain;
            readonly Vector3 _origin, _size;
            readonly List<GameObject> _models = new List<GameObject>();
            readonly List<TreeInstance> _instances = new List<TreeInstance>();
            public int Count => _instances.Count;

            public Growth(Terrain terrain)
            {
                _terrain = terrain;
                _origin = terrain.transform.position;
                _size = terrain.terrainData.size;
            }

            /// <summary>The model at p on the ground, scaled, widthFactor wider than tall, turned yaw radians.</summary>
            public void Add(GameObject model, Vector3 p, float scale, float widthFactor, float yaw)
            {
                int index = _models.IndexOf(model);
                if (index < 0) { _models.Add(model); index = _models.Count - 1; }
                _instances.Add(new TreeInstance
                {
                    prototypeIndex = index,
                    position = new Vector3((p.x - _origin.x) / _size.x, 0f, (p.z - _origin.z) / _size.z),
                    heightScale = scale,
                    widthScale = scale * widthFactor,
                    rotation = yaw,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }

            public void Commit()
            {
                TerrainData data = _terrain.terrainData;
                data.treePrototypes = _models.ConvertAll(m => new TreePrototype { prefab = m }).ToArray();
                data.SetTreeInstances(_instances.ToArray(), snapToHeightmap: true);
                // Meshes all the way: Unity's defaults (50 mesh trees, billboards past 50 m) need
                // its old Nature shaders to make billboards, and with URP's they drew black slivers.
                _terrain.treeDistance = 2000f;
                _terrain.treeBillboardDistance = 2000f;
                _terrain.treeMaximumFullLODCount = int.MaxValue;
                // The terrain draws them one call each; in play they are drawn instanced instead.
                if (_terrain.GetComponent<InstancedTrees>() == null) _terrain.gameObject.AddComponent<InstancedTrees>();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }
        }

        // ---------------------------------------------------------------- the circuit and the land

        sealed class Circuit
        {
            public readonly Vector3[] Centre, Right;
            readonly float[] _left, _right;
            readonly float _outside;
            readonly float[] _radius;
            public readonly int N;
            public readonly float Spacing;

            public Circuit(Vector3[] centre, Vector3[] right, float[] widthLeft, float[] widthRight, float outside)
            {
                Centre = centre; Right = right; _left = widthLeft; _right = widthRight; _outside = outside;
                N = centre.Length;
                float length = 0f;
                for (int i = 0; i < N; i++) length += Vector3.Distance(centre[i], centre[(i + 1) % N]);
                Spacing = length / N;
                _radius = new float[N];
                int d = Mathf.Max(1, Mathf.RoundToInt(10f / Spacing));
                for (int k = 0; k < N; k++)
                    _radius[k] = Circumradius(centre[Wrap(k - d)], centre[k], centre[Wrap(k + d)]);
            }

            public int Wrap(int k) => ((k % N) + N) % N;
            public float Radius(int k) => _radius[k];

            public Vector3 Tangent(int k)
            {
                Vector3 t = Centre[Wrap(k + 1)] - Centre[Wrap(k - 1)];
                t.y = 0f;
                return t.normalized;
            }

            /// <summary>+1 where the circuit turns right at k, -1 where it turns left.</summary>
            public int Turn(int k)
            {
                int d = Mathf.Max(1, Mathf.RoundToInt(10f / Spacing));
                return Vector3.Cross(Tangent(k - d), Tangent(k + d)).y > 0f ? +1 : -1;
            }

            /// <summary>The road's edge on the given side (+1 right).</summary>
            public Vector3 Edge(int k, int side) => Centre[k] + Right[k] * side * (side > 0 ? _right[k] : _left[k]);

            /// <summary>A point beyondM outside the barrier on the given side (+1 right).</summary>
            public Vector3 Outside(int k, int side, float beyondM) =>
                Centre[k] + Right[k] * side * ((side > 0 ? _right[k] : _left[k]) + _outside + beyondM);

            /// <summary>Distance along the lap between two samples, the shorter way round.</summary>
            public float ArcM(int a, int b)
            {
                int d = Mathf.Abs(a - b);
                return Mathf.Min(d, N - d) * Spacing;
            }

            static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
            {
                a.y = b.y = c.y = 0f;
                float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c), ca = Vector3.Distance(c, a);
                float area2 = Mathf.Abs(Vector3.Cross(b - a, c - a).y);
                return area2 < 1e-4f ? float.MaxValue : ab * bc * ca / (2f * area2);
            }
        }

        sealed class Land
        {
            readonly Terrain _terrain;
            readonly float _x0, _z0;
            readonly int _w, _h;
            readonly float[] _beyond;
            readonly bool[] _taken;
            public float MinX => _x0;
            public float MinZ => _z0;
            public float MaxX => _x0 + (_w - 1) * CellM;
            public float MaxZ => _z0 + (_h - 1) * CellM;

            public Land(Circuit circuit, Terrain terrain)
            {
                _terrain = terrain;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Vector3 p in circuit.Centre)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
                float margin = ReachM + 100f;
                _x0 = minX - margin; _z0 = minZ - margin;
                _w = Mathf.CeilToInt((maxX - minX + 2f * margin) / CellM) + 1;
                _h = Mathf.CeilToInt((maxZ - minZ + 2f * margin) / CellM) + 1;
                _beyond = new float[_w * _h];
                _taken = new bool[_w * _h];
                for (int i = 0; i < _beyond.Length; i++) _beyond[i] = float.MaxValue;

                // Road, verges and barriers are the seeds: each sample's line from one barrier
                // to the other.
                for (int k = 0; k < circuit.N; k++)
                {
                    Vector3 from = circuit.Outside(k, -1, 0f), to = circuit.Outside(k, +1, 0f);
                    int steps = Mathf.CeilToInt(Vector3.Distance(from, to) / (CellM * 0.5f));
                    for (int s = 0; s <= steps; s++)
                    {
                        int c = Cell(Vector3.Lerp(from, to, (float)s / steps));
                        if (c >= 0) _beyond[c] = 0f;
                    }
                }

                // Chamfer distance, a pass each way.
                float straight = CellM, diagonal = CellM * Mathf.Sqrt(2f);
                for (int j = 0; j < _h; j++)
                for (int i = 0; i < _w; i++)
                {
                    float d = _beyond[j * _w + i];
                    if (i > 0) d = Mathf.Min(d, _beyond[j * _w + i - 1] + straight);
                    if (j > 0)
                    {
                        d = Mathf.Min(d, _beyond[(j - 1) * _w + i] + straight);
                        if (i > 0) d = Mathf.Min(d, _beyond[(j - 1) * _w + i - 1] + diagonal);
                        if (i < _w - 1) d = Mathf.Min(d, _beyond[(j - 1) * _w + i + 1] + diagonal);
                    }
                    _beyond[j * _w + i] = d;
                }
                for (int j = _h - 1; j >= 0; j--)
                for (int i = _w - 1; i >= 0; i--)
                {
                    float d = _beyond[j * _w + i];
                    if (i < _w - 1) d = Mathf.Min(d, _beyond[j * _w + i + 1] + straight);
                    if (j < _h - 1)
                    {
                        d = Mathf.Min(d, _beyond[(j + 1) * _w + i] + straight);
                        if (i < _w - 1) d = Mathf.Min(d, _beyond[(j + 1) * _w + i + 1] + diagonal);
                        if (i > 0) d = Mathf.Min(d, _beyond[(j + 1) * _w + i - 1] + diagonal);
                    }
                    _beyond[j * _w + i] = d;
                }
            }

            int Cell(Vector3 p)
            {
                int i = Mathf.RoundToInt((p.x - _x0) / CellM), j = Mathf.RoundToInt((p.z - _z0) / CellM);
                return i < 0 || j < 0 || i >= _w || j >= _h ? -1 : j * _w + i;
            }

            /// <summary>Distance beyond the nearest barrier; 0 on the circuit itself.</summary>
            public float Beyond(Vector3 p)
            {
                int c = Cell(p);
                return c < 0 ? float.MaxValue : _beyond[c];
            }

            public bool Taken(Vector3 p)
            {
                int c = Cell(p);
                return c >= 0 && _taken[c];
            }

            public float Height(Vector3 p) => _terrain.SampleHeight(p) + _terrain.transform.position.y;

            /// <summary>Marks the quadrilateral, grown by marginM, as used.</summary>
            public void Take(Vector3[] corners, float marginM)
            {
                Vector3 centre = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
                Vector3 a = corners[1] - corners[0], b = corners[3] - corners[0];
                float ha = a.magnitude * 0.5f + marginM, hb = b.magnitude * 0.5f + marginM;
                a.Normalize(); b.Normalize();
                float r = Mathf.Sqrt(ha * ha + hb * hb);
                int cells = Mathf.CeilToInt(r / CellM);
                int ci = Mathf.RoundToInt((centre.x - _x0) / CellM), cj = Mathf.RoundToInt((centre.z - _z0) / CellM);
                for (int j = Mathf.Max(0, cj - cells); j <= Mathf.Min(_h - 1, cj + cells); j++)
                for (int i = Mathf.Max(0, ci - cells); i <= Mathf.Min(_w - 1, ci + cells); i++)
                {
                    var d = new Vector3(_x0 + i * CellM - centre.x, 0f, _z0 + j * CellM - centre.z);
                    if (Mathf.Abs(Vector3.Dot(d, a)) <= ha && Mathf.Abs(Vector3.Dot(d, b)) <= hb) _taken[j * _w + i] = true;
                }
            }
        }
    }
}
