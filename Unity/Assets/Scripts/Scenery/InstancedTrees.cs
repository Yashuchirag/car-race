using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Draws the terrain's trees in instanced batches. The terrain draws mesh trees (anything
    /// not made with its old Nature shaders) one draw call each, which with Royal Park's
    /// 30,000 took the frame rate from 210 to 44 fps. The trees stay in the terrain's data,
    /// where the scenery builder puts them and the editor shows them; at run time the terrain
    /// stops drawing them and this draws them instead.
    ///
    /// Trees are grouped by model and by CellM square of land, at most 1023 to a batch, each
    /// batch with its bounds. A batch is only submitted when it is in the main camera's view
    /// or near enough to cast a shadow into it: even culled, thousands of submissions a frame
    /// cost more than drawing. A scene can swap materials for its own (the night city's
    /// glowing windows) without touching the models.
    ///
    /// A model with a TreeLod is drawn whole within LodM of the camera and as its far version
    /// beyond. Which of a group's trees are near is worked out again only when the camera
    /// has moved ResortM since the last time, and only for groups that reach within LodM;
    /// a group wholly beyond is all far version.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public sealed class InstancedTrees : MonoBehaviour
    {
        const float CellM = 500f;
        const float ShadowReachM = 150f;   // High quality's shadow distance
        const float DrawReachM = 1500f;    // past this a tree is a pixel or two, in fog
        const int MaxBatch = 1023;
        public const float LodM = 120f;
        const float ResortM = 8f;

        sealed class Batch
        {
            public Mesh Mesh, Far;
            public Material[] Materials, FarMaterials;
            public List<Matrix4x4> Matrices;
            public Vector3[] Positions;
            public Bounds Bounds;
            public readonly List<Matrix4x4> Near = new List<Matrix4x4>(), Beyond = new List<Matrix4x4>();
            public Vector3 SortedAt = new Vector3(float.MaxValue, 0f, 0f);
        }

        [SerializeField] Material[] swapFrom = new Material[0];
        [SerializeField] Material[] swapTo = new Material[0];

        readonly List<Batch> _batches = new List<Batch>();

        public void SetMaterialSwaps(Material[] from, Material[] to)
        {
            swapFrom = from;
            swapTo = to;
        }
        readonly Plane[] _planes = new Plane[6];

        void Start()
        {
            var terrain = GetComponent<Terrain>();
            TerrainData data = terrain.terrainData;
            TreePrototype[] prototypes = data.treePrototypes;
            if (prototypes.Length == 0) return;

            var meshes = new Mesh[prototypes.Length];
            var materials = new Material[prototypes.Length][];
            var lods = new TreeLod[prototypes.Length];
            for (int p = 0; p < prototypes.Length; p++)
            {
                GameObject prefab = prototypes[p].prefab;
                meshes[p] = prefab != null ? prefab.GetComponent<MeshFilter>()?.sharedMesh : null;
                materials[p] = prefab != null ? prefab.GetComponent<MeshRenderer>()?.sharedMaterials : null;
                lods[p] = prefab != null ? prefab.GetComponent<TreeLod>() : null;
                if (materials[p] == null) continue;
                for (int m = 0; m < materials[p].Length; m++)
                {
                    int swap = System.Array.IndexOf(swapFrom, materials[p][m]);
                    if (swap >= 0) materials[p][m] = swapTo[swap];
                }
            }

            Vector3 origin = terrain.transform.position, size = data.size;
            var groups = new Dictionary<(int, int, int), List<Matrix4x4>>();
            foreach (TreeInstance tree in data.treeInstances)
            {
                if (meshes[tree.prototypeIndex] == null) continue;
                Vector3 position = origin + Vector3.Scale(tree.position, size);
                var matrix = Matrix4x4.TRS(position, Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f),
                                           new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                var key = (tree.prototypeIndex, Mathf.FloorToInt(position.x / CellM), Mathf.FloorToInt(position.z / CellM));
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Matrix4x4>();
                list.Add(matrix);
            }

            foreach (var group in groups)
            {
                int p = group.Key.Item1;
                Mesh mesh = meshes[p];
                List<Matrix4x4> all = group.Value;
                Bounds bounds = Transformed(mesh.bounds, all[0]);
                foreach (Matrix4x4 m in all) bounds.Encapsulate(Transformed(mesh.bounds, m));
                _batches.Add(new Batch
                {
                    Mesh = mesh, Materials = materials[p], Matrices = all, Bounds = bounds,
                    Far = lods[p] != null ? lods[p].farMesh : null,
                    FarMaterials = lods[p] != null ? lods[p].farMaterials : null,
                    Positions = all.ConvertAll(m => (Vector3)m.GetColumn(3)).ToArray(),
                });
            }
            terrain.drawTreesAndFoliage = false;
        }

        static Bounds Transformed(Bounds local, Matrix4x4 m)
        {
            var b = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(local.center + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                                             (i & 2) == 0 ? -e.y : e.y,
                                                                             (i & 4) == 0 ? -e.z : e.z)));
            return b;
        }

        void Update()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            Vector3 eye = camera.transform.position;
            foreach (Batch batch in _batches)
            {
                float distance2 = batch.Bounds.SqrDistance(eye);
                if (distance2 > DrawReachM * DrawReachM) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, batch.Bounds) && distance2 > ShadowReachM * ShadowReachM) continue;
                if (batch.Far == null) Draw(batch.Mesh, batch.Materials, batch.Matrices, batch.Bounds);
                else if (distance2 > LodM * LodM) Draw(batch.Far, batch.FarMaterials, batch.Matrices, batch.Bounds);
                else
                {
                    if ((eye - batch.SortedAt).sqrMagnitude > ResortM * ResortM) Sort(batch, eye);
                    Draw(batch.Mesh, batch.Materials, batch.Near, batch.Bounds);
                    Draw(batch.Far, batch.FarMaterials, batch.Beyond, batch.Bounds);
                }
            }
        }

        static void Sort(Batch batch, Vector3 eye)
        {
            batch.Near.Clear();
            batch.Beyond.Clear();
            for (int i = 0; i < batch.Positions.Length; i++)
                ((batch.Positions[i] - eye).sqrMagnitude < LodM * LodM ? batch.Near : batch.Beyond).Add(batch.Matrices[i]);
            batch.SortedAt = eye;
        }

        static void Draw(Mesh mesh, Material[] materials, List<Matrix4x4> matrices, Bounds bounds)
        {
            for (int s = 0; s < materials.Length && s < mesh.subMeshCount; s++)
            {
                var rp = new RenderParams(materials[s])
                {
                    worldBounds = bounds,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                };
                for (int start = 0; start < matrices.Count; start += MaxBatch)
                    Graphics.RenderMeshInstanced(rp, mesh, s, matrices, Mathf.Min(MaxBatch, matrices.Count - start), start);
            }
        }
    }
}
