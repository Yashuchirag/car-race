using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// On a tree model: the lighter version of it that InstancedTrees draws beyond
    /// InstancedTrees.LodM, a few large cards of leaves in place of every twig. The model's
    /// own MeshFilter and MeshRenderer are the full version.
    /// </summary>
    public sealed class TreeLod : MonoBehaviour
    {
        public Mesh farMesh;
        public Material[] farMaterials;
    }
}
