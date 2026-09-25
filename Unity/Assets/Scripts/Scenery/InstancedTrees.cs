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
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public sealed class InstancedTrees : MonoBehaviour
    {
        const float CellM = 500f;
        const float ShadowReachM = 150f;   // High quality's shadow distance
        const float DrawReachM = 1500f;    // past this a tree is a pixel or two, in fog
        const int MaxBatch = 1023;

        sealed class Batch
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4[] Matrices;
            public Bounds Bounds;
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
            for (int p = 0; p < prototypes.Length; p++)
            {
                GameObject prefab = prototypes[p].prefab;
                meshes[p] = prefab != null ? prefab.GetComponent<MeshFilter>()?.sharedMesh : null;
                materials[p] = prefab != null ? prefab.GetComponent<MeshRenderer>()?.sharedMaterials : null;
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
                for (int start = 0; start < all.Count; start += MaxBatch)
                {
                    var matrices = all.GetRange(start, Mathf.Min(MaxBatch, all.Count - start)).ToArray();
                    Bounds bounds = Transformed(mesh.bounds, matrices[0]);
                    foreach (Matrix4x4 m in matrices) bounds.Encapsulate(Transformed(mesh.bounds, m));
                    _batches.Add(new Batch { Mesh = mesh, Materials = materials[p], Matrices = matrices, Bounds = bounds });
                }
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
                for (int s = 0; s < batch.Materials.Length && s < batch.Mesh.subMeshCount; s++)
                {
                    var rp = new RenderParams(batch.Materials[s])
                    {
                        worldBounds = batch.Bounds,
                        shadowCastingMode = ShadowCastingMode.On,
                        receiveShadows = true,
                    };
                    Graphics.RenderMeshInstanced(rp, batch.Mesh, s, batch.Matrices);
                }
            }
        }
    }
}
