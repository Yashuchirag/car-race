using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// A standalone Windows build of the lobby and the Monza track scene, for measuring performance outside
    /// the editor, which adds its own overhead. From the command line, with the editor closed:
    ///
    ///   Unity.exe -batchmode -quit -projectPath D:\Dev\CarRace
    ///     -executeMethod CarRace.UnityGame.EditorTools.BuildTools.BuildWindows
    ///
    /// Writes Builds/Windows/CarRace.exe, which the project's .gitignore keeps out of Git.
    /// </summary>
    public static class BuildTools
    {
        // The lobby first, since a built game opens on the first scene; then the race.
        static readonly string[] Scenes = { "Assets/Scenes/Lobby.unity", "Assets/Scenes/Track Royal Park Speedway.unity" };
        const string Output = "Builds/Windows/CarRace.exe";
        const string DevelopmentOutput = "Builds/WindowsDevelopment/CarRace.exe";

        [MenuItem("CarRace/Build Windows Player")]
        public static void BuildWindows() => Build(Output, BuildOptions.None);

        /// <summary>A development build, whose profiler markers the benchmark can read to split a
        /// frame into physics, scripts and rendering. Slower than the release build, so use it
        /// for where the time goes, not for how much there is.</summary>
        [MenuItem("CarRace/Build Windows Development Player")]
        public static void BuildWindowsDevelopment() => Build(DevelopmentOutput, BuildOptions.Development);

        static void Build(string output, BuildOptions buildOptions)
        {
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = buildOptions,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"Build {summary.result}: {summary.outputPath}, {summary.totalSize / (1024 * 1024)} MB, " +
                      $"{summary.totalTime.TotalSeconds:0} s, {summary.totalErrors} errors, {summary.totalWarnings} warnings");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
