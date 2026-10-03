using System;
using System.Collections.Generic;

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

            /// <summary>Seconds of penalty, added to the race time at the flag.</summary>
            public float PenaltyS;
            public int Warnings;
            public int Penalties;
            public bool Disqualified;

            /// <summary>No offence yet on the lap being driven; only a valid lap can be a best lap.</summary>
            public bool LapValid = true;

            public bool Finished => FinishedAtS >= 0f;

            /// <summary>The time the result is ranked on: the race time plus any penalties.</summary>
            public float ResultS => FinishedAtS + PenaltyS;
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
                if (entry.LapValid && entry.LastLapS < entry.BestLapS) entry.BestLapS = entry.LastLapS;
                entry.LapsComplete = laps;
                if (laps >= RaceLaps && !entry.Finished) entry.FinishedAtS = time;
            }

            entry.LapStartedAtS = time;
            entry.LastSeenLaps = laps;
            entry.LapValid = true;
        }

        // ---- penalties ------------------------------------------------------

        public const float PenaltySeconds = 5f;
        /// <summary>Track limits offences that are only warned; the next and every one after it
        /// is a penalty.</summary>
        public const int Warnings = 2;
        /// <summary>The penalty that brings the black flag.</summary>
        public const int PenaltiesToDisqualify = 5;

        public enum Ruling { None, Warning, Penalty, Disqualified }

        public sealed class Event
        {
            public float TimeS;
            public int Car;
            public TrackLimits.Kind Offence;
            public Ruling Ruling;
        }

        /// <summary>Every ruling so far, oldest first.</summary>
        public readonly List<Event> Events = new List<Event>();

        /// <summary>
        /// Rules on a judged excursion. A cut costs PenaltySeconds at once; running wide is a
        /// warning, under the black and white flag, Warnings times, and a penalty every time
        /// after that. Either makes the lap invalid. The PenaltiesToDisqualify-th penalty is
        /// the black flag. Incidents and anything after the flag or a disqualification cost
        /// nothing.
        /// </summary>
        public Event Judge(int car, TrackLimits.Kind offence, float time)
        {
            Entry entry = Entries[car];
            if (offence != TrackLimits.Kind.Cut && offence != TrackLimits.Kind.TrackLimits) return null;
            if (entry.Finished || entry.Disqualified) return null;

            entry.LapValid = false;
            Ruling ruling;
            if (offence == TrackLimits.Kind.TrackLimits && entry.Warnings < Warnings)
            {
                entry.Warnings++;
                ruling = Ruling.Warning;
            }
            else
            {
                if (offence == TrackLimits.Kind.TrackLimits) entry.Warnings++;
                entry.Penalties++;
                entry.PenaltyS += PenaltySeconds;
                ruling = Ruling.Penalty;
                if (entry.Penalties >= PenaltiesToDisqualify)
                {
                    entry.Disqualified = true;
                    ruling = Ruling.Disqualified;
                }
            }

            var ruled = new Event { TimeS = time, Car = car, Offence = offence, Ruling = ruling };
            Events.Add(ruled);
            return ruled;
        }

        /// <summary>
        /// Orders the field: everyone who has finished, by race time plus penalties, then
        /// everyone still running, by how far they have gone, then anyone disqualified. On the
        /// road penalties change nothing; they count at the flag, as FIA time penalties do.
        /// </summary>
        public void Rank()
        {
            var order = (Entry[])Entries.Clone();
            Array.Sort(order, (a, b) =>
            {
                if (a.Disqualified != b.Disqualified) return a.Disqualified ? 1 : -1;
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished && b.Finished)
                {
                    int byResult = a.ResultS.CompareTo(b.ResultS);
                    return byResult != 0 ? byResult : a.FinishedAtS.CompareTo(b.FinishedAtS);
                }
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
