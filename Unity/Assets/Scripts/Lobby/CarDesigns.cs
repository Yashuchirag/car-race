using System.Collections.Generic;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The car body designs the lobby offers: each a name and the two meshes that make it,
    /// the painted body and its details (glass, trim, lights, wing). Built by the editor's
    /// CarModel and kept in Resources, so the game can load it from any scene. A design is
    /// looks only: the wheels, wheelbase and handling are the same whichever is chosen.
    /// </summary>
    public sealed class CarDesigns : ScriptableObject
    {
        [System.Serializable]
        public sealed class Design
        {
            public string name;
            public Mesh body;
            public Mesh details;
        }

        public List<Design> designs = new List<Design>();

        public const string ResourceName = "CarDesigns";
        static CarDesigns _loaded;

        public static CarDesigns Load() => _loaded != null ? _loaded : _loaded = Resources.Load<CarDesigns>(ResourceName);

        public static int Count => Load() != null ? Load().designs.Count : 0;

        public static string NameOf(int index) =>
            Load() != null && index >= 0 && index < Count ? Load().designs[index].name : "";

        /// <summary>Puts design <paramref name="index"/> on a car: its "Body" and "Body Details".</summary>
        public static void Apply(Transform car, int index)
        {
            if (car == null || index < 0 || index >= Count) return;
            Design design = Load().designs[index];
            Set(car.Find("Body"), design.body);
            Set(car.Find("Body Details"), design.details);
        }

        static void Set(Transform part, Mesh mesh)
        {
            var filter = part != null ? part.GetComponent<MeshFilter>() : null;
            if (filter != null && mesh != null) filter.sharedMesh = mesh;
        }
    }
}
