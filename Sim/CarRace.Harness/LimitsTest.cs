using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// The track limits judge, checked on every circuit with the real car following scripted
    /// lines. --limits-test [circuit|all].
    ///
    ///   A cut at every corner: the line replaced by a straight chord across the inside, from
    ///   15 m before the corner to 15 m after it. Only corners where that chord is at least
    ///   CutWorthM shorter than the shortest legal path count, judged geometrically before any
    ///   car drives, so the test cannot be passed by a judge that merely agrees with itself.
    ///   Every one must be ruled a cut.
    ///
    ///   A wide run at every corner: from the apex to 40 m past the corner, the line moved out
    ///   to 2.5 m beyond the outside edge. Every one must be a track limits offence, never a cut.
    ///
    ///   A clean lap on the racing line at AI pace: no offence at all.
    /// </summary>
    public static class LimitsTest
    {
        const float Dt = 1f / Rig.SubstepHz;
        const float CutWorthM = 10f;
        const float CornerRadiusM = 150f;

        sealed class Case
        {
            public string Circuit, Kind;
            public int Corner;
            public List<TrackLimits.Kind> Rulings = new List<TrackLimits.Kind>();
            public float SavedM;
            public bool Passed;
        }

        public static int Run(CarConfig config, string circuit)
        {
            string[] circuits = circuit == "all" ? TrackLoader.Available() : new[] { circuit };
            var cases = new List<(Case Case, TrackData Track, int Start, int End)>();
            var clean = new List<(Case Case, TrackData Track)>();

            foreach (string name in circuits)
            {
                TrackData track = TrackLoader.Load(name);
                foreach (var (from, to, apex, turn, index) in Corners(track))
                {
                    int a = track.Wrap(from - Samples(track, 15f)), b = track.Wrap(to + Samples(track, 15f));
                    TrackData chord = WithLine(track, a, b, (i, t) => Vector3.Lerp(track.Line[a], track.Line[b], t));
                    float legal = TrackLimits.ShortestLegalPath(track, a, track.Line[a], b, track.Line[b]);
                    float straight = Flat(track.Line[b] - track.Line[a]).Length();
                    if (legal - straight >= CutWorthM)
                        cases.Add((new Case { Circuit = name, Kind = "cut", Corner = index, SavedM = legal - straight }, chord, a, b));

                    // Wide: out past the edge on the corner's outside, from the apex to 40 m on.
                    int w0 = apex, w1 = track.Wrap(to + Samples(track, 40f));
                    int span = ((w1 - w0) % track.Count + track.Count) % track.Count;
                    float outward = -MathF.Sign(turn);
                    TrackData wide = WithLine(track, w0, w1, (i, t) =>
                    {
                        float edge = outward > 0f ? track.WidthRight[i] : track.WidthLeft[i];
                        float blend = MathF.Min(1f, MathF.Min(t, 1f - t) * span * track.SampleSpacingM / 20f);
                        Vector3 r = TrackData.Right(track.Tangent(track.Centre, i));
                        Vector3 outside = track.Centre[i] + r * (outward * (edge + 2.5f));
                        return Vector3.Lerp(track.Line[i], outside, blend);
                    });
                    cases.Add((new Case { Circuit = name, Kind = "wide", Corner = index }, wide, w0, w1));
                }
                clean.Add((new Case { Circuit = name, Kind = "clean" }, track));
            }

            Parallel.For(0, cases.Count, k =>
            {
                var (c, track, start, end) = cases[k];
                Drive(config, track, track.Wrap(start - Samples(track, 400f)), end, c.Rulings, laps: 0);
                c.Passed = c.Kind == "cut"
                    ? c.Rulings.Contains(TrackLimits.Kind.Cut)
                    : c.Rulings.Contains(TrackLimits.Kind.TrackLimits) && !c.Rulings.Contains(TrackLimits.Kind.Cut);
            });
            Parallel.For(0, clean.Count, k =>
            {
                var (c, track) = clean[k];
                Drive(config, track, 0, 0, c.Rulings, laps: 1);
                c.Passed = c.Rulings.TrueForAll(r => r == TrackLimits.Kind.Incident);
            });

            bool allPassed = true;
            Console.WriteLine($"  {"circuit",-12} {"cuts ruled cut",16} {"wide runs warned",18} {"clean lap offences",20}   result");
            Console.WriteLine("  " + new string('-', 80));
            foreach (string name in circuits)
            {
                var cuts = cases.FindAll(x => x.Case.Circuit == name && x.Case.Kind == "cut").ConvertAll(x => x.Case);
                var wides = cases.FindAll(x => x.Case.Circuit == name && x.Case.Kind == "wide").ConvertAll(x => x.Case);
                Case lap = clean.Find(x => x.Case.Circuit == name).Case;
                bool ok = cuts.TrueForAll(c => c.Passed) && wides.TrueForAll(c => c.Passed) && lap.Passed;
                allPassed &= ok;
                Console.WriteLine($"  {name,-12} {$"{cuts.FindAll(c => c.Passed).Count} of {cuts.Count}",16} "
                                + $"{$"{wides.FindAll(c => c.Passed).Count} of {wides.Count}",18} "
                                + $"{lap.Rulings.FindAll(r => r != TrackLimits.Kind.Incident).Count,20}   {(ok ? "PASS" : "FAIL")}");
                foreach (Case c in cuts) if (!c.Passed)
                    Console.WriteLine($"      cut at corner {c.Corner} ({c.SavedM:0} m shorter than legal) ruled {Describe(c.Rulings)}");
                foreach (Case c in wides) if (!c.Passed)
                    Console.WriteLine($"      wide at corner {c.Corner} ruled {Describe(c.Rulings)}");
                if (!lap.Passed) Console.WriteLine($"      clean lap ruled {Describe(lap.Rulings)}");
            }
            bool rules = Rules(out string why);
            string said = rules ? "two warnings then penalties, a cut penalised at once, the black flag at the fifth "
                                + "penalty, invalid laps never best, penalties at the flag   PASS"
                                : "FAIL: " + why;
            Console.WriteLine($"\n  rulings: {said}");
            allPassed &= rules;
                        Console.WriteLine($"\n  {(allPassed ? "Limits test: PASS." : "Limits test: FAILED.")}\n");
            return allPassed ? 0 : 1;
        }

        /// <summary>RaceControl's rulings on a scripted sequence of offences, against the rules
        /// as written.</summary>
        static bool Rules(out string why)
        {
            why = null;
            var control = new RaceControl(new[] { "A", "B" }, 3);
            control.Update(0, 0f, 0, 0f);
            control.Update(1, 0f, 0, 0f);
            var wide = TrackLimits.Kind.TrackLimits;
            RaceControl.Ruling[] expected =
            {
                RaceControl.Ruling.Warning, RaceControl.Ruling.Warning, RaceControl.Ruling.Penalty,
                RaceControl.Ruling.Penalty, RaceControl.Ruling.Penalty, RaceControl.Ruling.Penalty,
                RaceControl.Ruling.Disqualified,
            };
            for (int k = 0; k < expected.Length; k++)
            {
                RaceControl.Ruling got = control.Judge(0, wide, 10f + k).Ruling;
                if (got != expected[k]) { why = $"offence {k + 1} ruled {got}, expected {expected[k]}"; return false; }
            }
            if (control.Judge(0, wide, 20f) != null) { why = "a disqualified car was ruled on again"; return false; }
            if (control.Judge(1, TrackLimits.Kind.Incident, 5f) != null) { why = "an incident was ruled on"; return false; }
            if (control.Judge(1, TrackLimits.Kind.Cut, 5f).Ruling != RaceControl.Ruling.Penalty)
            { why = "a first cut was not penalised"; return false; }

            // B's first lap is invalid (the cut), its second clean and slower: only that one is best.
            control.Update(1, 60f, 1, 0f);
            control.Update(1, 130f, 2, 0f);
            if (control.Entries[1].BestLapS != 70f) { why = $"best lap {control.Entries[1].BestLapS} s, expected the valid 70 s"; return false; }

            // At the flag: B finishes first on the road but 5 s down; C-less field, so A is DSQ and last.
            control.Update(1, 200f, 3, 0f);
            RaceControl.Entry[] order = control.Classification();
            if (order[0].Name != "B" || order[1].Name != "A" || !order[1].Disqualified)
            { why = "the classification does not put the disqualified car last"; return false; }
            if (order[0].ResultS != 205f) { why = $"B's result {order[0].ResultS} s, expected 205 with its penalty"; return false; }
            return true;
        }

        static string Describe(List<TrackLimits.Kind> rulings) => rulings.Count == 0 ? "nothing" : string.Join(", ", rulings);

        /// <summary>Drives from <paramref name="start"/> at rest, judging every step, until past
        /// <paramref name="end"/> by 100 m, or for whole laps when <paramref name="laps"/> is set.</summary>
        static void Drive(CarConfig config, TrackData track, int start, int end, List<TrackLimits.Kind> rulings, int laps)
        {
            SpeedPlan.Limits limits = LapRun.PlanningLimits(config);
            limits.LateralMs2 *= 0.85f;
            var driver = new PathDriver(track, SpeedPlan.Build(track, limits), config);
            var rig = new Rig(config);
            rig.Settle();
            Vector3 tangent = track.Tangent(track.Line, start);
            rig.Body.State = new BodyState
            {
                Position = new Vector3(track.Line[start].X, config.CgHeight, track.Line[start].Z),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(tangent.X, tangent.Z)),
            };
            driver.StartAt(start, 0);
            var judge = new TrackLimits();

            int stop = track.Wrap(end + Samples(track, 100f));
            int travelled = 0, last = start, goal = laps > 0 ? laps * track.Count
                                                          : ((stop - start) % track.Count + track.Count) % track.Count;
            float deadline = laps > 0 ? 600f : 120f;
            while (rig.Time < deadline && travelled < goal)
            {
                VehicleInputs input = driver.Drive(rig.Body.State, Dt);
                rig.Step(input);
                int step = ((driver.Index - last) % track.Count + track.Count) % track.Count;
                if (step < track.Count / 2) travelled += step;
                last = driver.Index;
                TrackLimits.Kind ruled = judge.Step(track, driver.Index, rig.Body.State.Position, rig.Body.State.Forward,
                                                    rig.Body.State.Velocity, rig.Sim.Wheels, Dt);
                if (ruled != TrackLimits.Kind.None) rulings.Add(ruled);
            }
        }

        /// <summary>Corners: runs of the centreline tighter than CornerRadiusM, joined across gaps
        /// under 20 m, turning more than 20 degrees. Each: first and last sample, the tightest
        /// sample as its apex, its turn in radians with the sign of the curvature, and a number.</summary>
        static List<(int From, int To, int Apex, float Turn, int Number)> Corners(TrackData track)
        {
            int n = track.Count;
            float[] k = TrackData.SignedCurvature(track.Centre, Math.Max(1, (int)MathF.Round(6f / track.SampleSpacingM)));
            var tight = new bool[n];
            for (int i = 0; i < n; i++) tight[i] = MathF.Abs(k[i]) > 1f / CornerRadiusM;
            int gap = Samples(track, 20f);

            // Start the walk on a straight sample so no corner is split across the wrap.
            int origin = Array.FindIndex(tight, t => !t);
            var corners = new List<(int, int, int, float, int)>();
            int found = 0;
            for (int s = 0; s < n; )
            {
                int i = track.Wrap(origin + s);
                if (!tight[i]) { s++; continue; }
                int from = s, to = s, quiet = 0;
                while (s < n && quiet <= gap)
                {
                    if (tight[track.Wrap(origin + s)]) { to = s; quiet = 0; } else quiet++;
                    s++;
                }
                float turn = 0f, peak = 0f;
                int apex = from;
                for (int t = from; t <= to; t++)
                {
                    float c = k[track.Wrap(origin + t)];
                    turn += c * track.SampleSpacingM;
                    if (MathF.Abs(c) > peak) { peak = MathF.Abs(c); apex = t; }
                }
                if (MathF.Abs(turn) > 20f * MathF.PI / 180f)
                    corners.Add((track.Wrap(origin + from), track.Wrap(origin + to), track.Wrap(origin + apex), turn, ++found));
            }
            return corners;
        }

        /// <summary>A copy of the track whose racing line from <paramref name="a"/> to <paramref name="b"/>
        /// is replaced, sample by sample, by <paramref name="line"/> (sample, share of the way).</summary>
        static TrackData WithLine(TrackData track, int a, int b, Func<int, float, Vector3> line)
        {
            int n = track.Count;
            int span = ((b - a) % n + n) % n;
            var points = (Vector3[])track.Line.Clone();
            for (int s = 0; s <= span; s++)
            {
                int i = track.Wrap(a + s);
                Vector3 p = line(i, span > 0 ? (float)s / span : 0f);
                points[i] = new Vector3(p.X, track.Line[i].Y, p.Z);
            }
            var copy = new TrackData
            {
                Name = track.Name, LengthM = track.LengthM, SampleSpacingM = track.SampleSpacingM,
                EstimatedLapTimeS = track.EstimatedLapTimeS, Centre = track.Centre, Line = points,
                WidthLeft = track.WidthLeft, WidthRight = track.WidthRight,
            };
            copy.LineFromCentreM = new float[n];
            for (int i = 0; i < n; i++) copy.LineFromCentreM[i] = copy.LateralOffset(copy.Centre, i, points[i]);
            copy.LineCurvature = TrackData.SignedCurvature(points, Math.Max(1, (int)MathF.Round(6f / track.SampleSpacingM)));
            return copy;
        }

        static int Samples(TrackData track, float metres) => (int)MathF.Round(metres / track.SampleSpacingM);
        static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0f, v.Z);
    }
}
