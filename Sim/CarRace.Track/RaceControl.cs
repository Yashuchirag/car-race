using System;

namespace CarRace.Track
{
    /// <summary>
    /// Laps, positions and lap times for a field of cars. Pure bookkeeping: it is told
    /// where every car is and never asks, so the same class serves the headless harness
    /// and, later, a Unity scene where the bodies belong to the engine.
    /// </summary>
    public sealed class RaceControl
    {
        public sealed class Entry
        {
            public string Name;
            public int Grid;               // 1 is pole
            public int Position;           // 1 is leading, live
            public int LapsComplete;       // 0 until the line is crossed for the first time
            public float ProgressM;
            public float BestLapS = float.MaxValue;
            public float LastLapS;
            public float FinishedAtS = -1f;
            public int Contacts;

            public bool Finished => FinishedAtS >= 0f;
            internal float LapStartedAtS;
            internal int LastSeenLaps = int.MinValue;
        }

        public readonly Entry[] Entries;
        public readonly int RaceLaps;

        public RaceControl(string[] names, int raceLaps)
        {
            RaceLaps = raceLaps;
            Entries = new Entry[names.Length];
            for (int i = 0; i < names.Length; i++)
                Entries[i] = new Entry { Name = names[i], Grid = i + 1, Position = i + 1 };
        }

        /// <summary>
        /// Records where one car is. <paramref name="laps"/> counts crossings of the start
        /// line and begins at -1 for a car gridded behind it, so the first crossing starts
        /// its first lap rather than completing one.
        /// </summary>
        public void Update(int car, float time, int laps, float progressM)
        {
            Entry entry = Entries[car];
            entry.ProgressM = progressM;

            if (entry.LastSeenLaps == int.MinValue) entry.LastSeenLaps = laps;
            if (laps <= entry.LastSeenLaps) return;

            // Crossed the line. From -1 to 0 is the start of lap one, not the end of it.
            if (entry.LastSeenLaps >= 0)
            {
                entry.LastLapS = time - entry.LapStartedAtS;
                if (entry.LastLapS < entry.BestLapS) entry.BestLapS = entry.LastLapS;
                entry.LapsComplete = laps;
                if (laps >= RaceLaps && !entry.Finished) entry.FinishedAtS = time;
            }

            entry.LapStartedAtS = time;
            entry.LastSeenLaps = laps;
        }

        /// <summary>
        /// Orders the field: everyone who has finished, in the order they finished, then
        /// everyone still running, by how far they have gone.
        /// </summary>
        public void Rank()
        {
            var order = (Entry[])Entries.Clone();
            Array.Sort(order, (a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished && b.Finished) return a.FinishedAtS.CompareTo(b.FinishedAtS);
                return b.ProgressM.CompareTo(a.ProgressM);
            });

            for (int i = 0; i < order.Length; i++) order[i].Position = i + 1;
        }

        public bool Everyone(Func<Entry, bool> test)
        {
            foreach (Entry entry in Entries)
                if (!test(entry)) return false;
            return true;
        }

        public Entry[] Classification()
        {
            Rank();
            var order = (Entry[])Entries.Clone();
            Array.Sort(order, (a, b) => a.Position.CompareTo(b.Position));
            return order;
        }
    }
}
