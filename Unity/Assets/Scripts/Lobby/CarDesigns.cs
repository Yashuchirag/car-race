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
            /// <summary>The details' materials, one per submesh. A design modelled in Blender
            /// has more parts than a generated one, and clear glass with a cabin behind it
            /// where the generated ones have dark glass over nothing.</summary>
            public Material[] detailMaterials;
            /// <summary>The driver's eye in the car's space, where the cockpit camera sits: the
            /// designs' cabins differ in height and in where the seat is.</summary>
            public Vector3 eye;
        }

        public List<Design> designs = new List<Design>();

        public const string ResourceName = "CarDesigns";
        static CarDesigns _loaded;

        public static CarDesigns Load() => _loaded != null ? _loaded : _loaded = Resources.Load<CarDesigns>(ResourceName);

        public static int Count => Load() != null ? Load().designs.Count : 0;

        public static string NameOf(int index) =>
            Load() != null && index >= 0 && index < Count ? Load().designs[index].name : "";

        /// <summary>Puts design <paramref name="index"/> on a car: its "Body" and "Body Details",
        /// and the details' materials.</summary>
        public static void Apply(Transform car, int index)
        {
            if (car == null || index < 0 || index >= Count) return;
            Design design = Load().designs[index];
            Set(car.Find("Body"), design.body);
            Transform details = car.Find("Body Details");
            Set(details, design.details);
            var renderer = details != null ? details.GetComponent<MeshRenderer>() : null;
            if (renderer != null && design.detailMaterials != null && design.detailMaterials.Length > 0)
                renderer.sharedMaterials = design.detailMaterials;
            Transform cockpit = car.Find(CarCamera.CockpitName);
            if (cockpit != null) cockpit.localPosition = design.eye;
        }

        static void Set(Transform part, Mesh mesh)
        {
            var filter = part != null ? part.GetComponent<MeshFilter>() : null;
            if (filter != null && mesh != null) filter.sharedMesh = mesh;
        }
    }
}
