using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// A circuit's surroundings, as you chose them on 2026-09-24, following the real
    /// locations, with a night neon city for the test circuit: what the ground is made of,
    /// how high its mountains rise, whether there is sea, and whether it is night. The
    /// ground builder, the scenery builder and the sky read it; what grows and stands there is
    /// SceneryBuilder's, by Name.
    /// </summary>
    public sealed class Theme
    {
        public string Name = "";
        public Surface Ground = Surface.Grass;   // the terrain and the verges
        public Surface Steep = Surface.Rock;     // steep terrain, where there are mountains
        public float MountainsM;                 // see GroundBuilder
        public bool Sea;                         // along the circuit's east side
        public bool Night;
        public bool City;                        // streets and buildings, see SceneryBuilder

        /// <summary>Set by GroundBuilder when there is sea: the water's height.</summary>
        public float SeaLevelY = float.NegativeInfinity;

        /// <summary>Set by GroundBuilder when there is sea: how far inland of the coastline a
        /// point is, negative out at sea.</summary>
        public System.Func<Vector3, float> Inland;

        public static Theme For(string circuit)
        {
            switch (circuit)
            {
                case "monza": return new Theme { Name = "Countryside" };
                case "spa": return new Theme { Name = "Mountains", MountainsM = 260f };
                case "bahrain": return new Theme { Name = "Desert", Ground = Surface.Sand, Steep = Surface.DesertRock, MountainsM = 90f };
                case "suzuka": return new Theme { Name = "Coast", MountainsM = 140f, Sea = true };
                case "silverstone": return new Theme { Name = "City", Ground = Surface.Concrete, City = true };
                case "testcircuit": return new Theme { Name = "Night City", Ground = Surface.Concrete, City = true, Night = true };
                default: return new Theme();
            }
        }
    }
}
