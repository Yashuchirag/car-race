using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using CarRace.Track;

namespace CarRace.Harness
{
    /// <summary>
    /// Reads a track file from the Python pipeline. Lives in the harness rather than in
    /// CarRace.Track because Unity brings its own JSON reader, and the geometry library
    /// has to stay free of anything the editor would have to be talked out of.
    /// </summary>
    public static class TrackLoader
    {
        public static TrackData Load(string name)
        {
            string path = Locate(name);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            JsonElement centre = root.GetProperty("centerline");
            JsonElement line = root.GetProperty("racing_line");

            var track = new TrackData
            {
                Name = root.GetProperty("name").GetString(),
                LengthM = root.GetProperty("length_m").GetSingle(),
                SampleSpacingM = root.GetProperty("sample_spacing_m").GetSingle(),
                EstimatedLapTimeS = root.GetProperty("estimated_lap_time_s").GetSingle(),

                Centre = Points(centre),
                Line = Points(line),
                WidthLeft = Floats(centre, "width_left"),
                WidthRight = Floats(centre, "width_right"),
            };

            // Curvature is recomputed from the geometry rather than read, because the sign in
            // the file is in ENU and means the opposite turn. Magnitudes should still agree
            // with the file, and a disagreement means the two are not describing the same
            // line at all, so it is worth saying out loud.
            track.LineFromCentreM = new float[track.Count];
            for (int i = 0; i < track.Count; i++)
                track.LineFromCentreM[i] = track.LateralOffset(track.Centre, i, track.Line[i]);

            int stride = Math.Max(1, (int)MathF.Round(6f / track.SampleSpacingM));
            track.LineCurvature = TrackData.SignedCurvature(track.Line, stride);
            float mine = Peak(track.LineCurvature);
            float theirs = Peak(Floats(line, "curvature"));
            if (MathF.Abs(mine - theirs) > 0.1f * MathF.Max(theirs, 1e-6f))
            {
                Console.WriteLine($"  WARNING: peak curvature {mine:0.0000} 1/m computed here "
                                + $"against {theirs:0.0000} in the file. The racing line and the "
                                + "curvature it ships are not the same curve.");
            }

            if (track.Line.Length != track.Centre.Length)
            {
                throw new InvalidDataException(
                    $"{name}: {track.Centre.Length} centreline samples but {track.Line.Length} " +
                    "racing line samples. The two are indexed together everywhere downstream.");
            }

            return track;
        }

        /// <summary>
        /// The pipeline writes ENU, where Z is up. Everything on this side has Y up, so the
        /// swap happens once, here, and no code downstream has to remember which it holds.
        /// </summary>
        static Vector3[] Points(JsonElement group)
        {
            float[] x = Floats(group, "x");
            float[] y = Floats(group, "y");
            float[] z = Floats(group, "z");

            var points = new Vector3[x.Length];
            for (int i = 0; i < x.Length; i++) points[i] = new Vector3(x[i], z[i], y[i]);
            return points;
        }

        static float Peak(float[] values)
        {
            float peak = 0f;
            foreach (float value in values) peak = MathF.Max(peak, MathF.Abs(value));
            return peak;
        }

        static float[] Floats(JsonElement group, string name)
        {
            JsonElement array = group.GetProperty(name);
            var values = new float[array.GetArrayLength()];
            int i = 0;
            foreach (JsonElement element in array.EnumerateArray()) values[i++] = element.GetSingle();
            return values;
        }

        /// <summary>
        /// Finds Tracks_Data by walking up from the working directory and from the binary.
        /// `dotnet run` and a published build disagree about what the working directory is,
        /// and a path that only works one of those two ways is worse than no path at all.
        /// </summary>
        /// <summary>Every circuit in Tracks_Data, by name, in alphabetical order.</summary>
        public static string[] Available()
        {
            string any = Locate("testcircuit");
            var files = Directory.GetFiles(Path.GetDirectoryName(any), "*.json");
            Array.Sort(files, StringComparer.Ordinal);

            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++) names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }

        static string Locate(string name)
        {
            string file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? name : name + ".json";
            if (File.Exists(file)) return file;

            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, "Tracks_Data", file);
                    if (File.Exists(candidate)) return candidate;
                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException(
                $"Could not find Tracks_Data/{file}. Generate it with " +
                $"Tools/.venv/bin/python Tools/build_track.py {name}");
        }
    }
}
