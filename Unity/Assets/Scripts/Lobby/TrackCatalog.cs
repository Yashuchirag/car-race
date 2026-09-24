using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// What the lobby knows about each circuit without loading it: its scene, name, length,
    /// surroundings theme and a simplified outline for the card. Written by the track builder
    /// each time a circuit is built.
    /// </summary>
    public sealed class TrackCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string scene;
            public string displayName;
            public string theme;
            public float lengthKm;
            [Tooltip("The centreline fitted into a unit square, north up, a couple of hundred points.")]
            public Vector2[] outline = new Vector2[0];
        }

        public List<Entry> entries = new List<Entry>();
    }
}
