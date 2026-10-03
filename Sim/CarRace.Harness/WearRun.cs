using System;
using System.Numerics;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Tyre wear over a stint: the AI driving lap after lap at its race pace with wear on, lap
    /// time and each tyre's wear and grip per lap, until the rear tyres are worn out or the laps
    /// run out. --wear [circuit] [--laps N] [--rate R]. How TyreConfig.WearEnergy is calibrated,
    /// and the check that the AI, slowing for its tyres (PathDriver.TyreGrip), keeps the car on
    /// the road as they go.
    /// </summary>
    public static class WearRun
    {
        const float Dt = 1f / Rig.SubstepHz;
        public const float Pace = 0.85f;

        public sealed class Stint
        {
            public int Laps;
            public float LapsToWornOut = -1f;
            public int Spins;
            public float FirstLapS, LastLapS;
            public float RearWearPerLap;
        }

        public static int Run(CarConfig config, string circuit, int laps, float rate)
        {
            TrackData track = TrackLoader.Load(circuit);
            Console.WriteLine($"=== Tyre wear: {track.Name}, {laps} laps at pace {Pace:0.00}, wear x{rate:0.##} ===");
            Console.WriteLine($"  {"lap",4} {"time",9} {"wear FL",8} {"FR",6} {"RL",6} {"RR",6} {"grip",6}");
            Stint stint = Drive(config, track, laps, rate, Pace, verbose: true);
            Console.WriteLine($"\n  rear tyres worn out after {(stint.LapsToWornOut < 0f ? $"more than {stint.Laps}" : $"{stint.LapsToWornOut:0.0}")} laps, "
                            + $"{stint.RearWearPerLap * 100f:0.0}% a lap; lap one {Time(stint.FirstLapS)}, last {Time(stint.LastLapS)}; "
                            + $"{stint.Spins} spins");
            bool ok = stint.Spins == 0 && stint.Laps == laps;
            Console.WriteLine($"\n  {(ok ? "Wear run: PASS." : "Wear run: FAILED (spins, or laps not completed).")}\n");
            return ok ? 0 : 1;
        }

        /// <summary>Drives whole laps with wear at <paramref name="rate"/>, from a flying start
        /// line crossing, and says how the tyres went.</summary>
        public static Stint Drive(CarConfig config, TrackData track, int laps, float rate, float pace, bool verbose)
        {
            SpeedPlan.Limits limits = LapRun.PlanningLimits(config);
            float grip = limits.LateralMs2;
            limits.LateralMs2 *= pace;
            limits.BrakingMs2 *= pace;
            limits.TractionMs2 *= pace;
            var driver = new PathDriver(track, SpeedPlan.Build(track, limits), config) { LateralGripMs2 = grip };
            var rig = new Rig(config);
            rig.Sim.TyreWearRate = rate;
            rig.Settle();
            int start = track.Wrap(-(int)MathF.Round(200f / track.SampleSpacingM));
            Vector3 tangent = track.Tangent(track.Line, start);
            rig.Body.State = new BodyState
            {
                Position = new Vector3(track.Line[start].X, config.CgHeight, track.Line[start].Z),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(tangent.X, tangent.Z)),
            };
            driver.StartAt(start, -1);

            var stint = new Stint();
            float lapStart = -1f, spinCooldown = 0f, previousRear = 0f;
            float deadline = laps * MathF.Max(track.EstimatedLapTimeS, 60f) * 2.5f + 60f;
            int lastLaps = driver.Laps;
            while (rig.Time < deadline && stint.Laps < laps)
            {
                driver.TyreGrip = rig.Sim.TyreGrip;
                rig.Step(driver.Drive(rig.Body.State, Dt));

                spinCooldown -= Dt;
                if (MathF.Abs(rig.SideslipDegrees) > 25f && spinCooldown <= 0f) { stint.Spins++; spinCooldown = 5f; }

                if (driver.Laps == lastLaps) continue;
                lastLaps = driver.Laps;
                if (lapStart >= 0f)
                {
                    float lap = rig.Time - lapStart;
                    stint.Laps++;
                    if (stint.Laps == 1) stint.FirstLapS = lap;
                    stint.LastLapS = lap;
                    Wheel[] w = rig.Sim.Wheels;
                    float rear = MathF.Max(w[VehicleSim.RL].Wear, w[VehicleSim.RR].Wear);
                    if (stint.Laps == 1) stint.RearWearPerLap = rear;
                    if (stint.LapsToWornOut < 0f && rear >= 1f)
                        stint.LapsToWornOut = stint.Laps - 1 + (1f - previousRear) / MathF.Max(rear - previousRear, 1e-6f);
                    previousRear = rear;
                    if (verbose)
                        Console.WriteLine($"  {stint.Laps,4} {Time(lap),9} {w[0].Wear * 100f,7:0.0}% {w[1].Wear * 100f,5:0.0}% "
                                        + $"{w[2].Wear * 100f,5:0.0}% {w[3].Wear * 100f,5:0.0}% {rig.Sim.TyreGrip * 100f,5:0.0}%");
                }
                lapStart = rig.Time;
            }
            if (stint.LapsToWornOut < 0f && stint.RearWearPerLap > 0f && previousRear < 1f && stint.Laps > 0)
                stint.LapsToWornOut = -1f;
            return stint;
        }

        static string Time(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.000}";
        }
    }
}
