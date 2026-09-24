using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// A standalone Windows build of the Monza track scene, for measuring performance outside
    /// the editor, which adds its own overhead. From the command line, with the editor closed:
    ///
    ///   Unity.exe -batchmode -quit -projectPath D:\Dev\CarRace
    ///     -executeMethod CarRace.UnityGame.EditorTools.BuildTools.BuildWindows
    ///
    /// Writes Builds/Windows/CarRace.exe, which the project's .gitignore keeps out of Git.
    /// </summary>
    public static class BuildTools
    {
        const string Scene = "Assets/Scenes/Track Royal Park Speedway.unity";
        const string Output = "Builds/Windows/CarRace.exe";

        [MenuItem("CarRace/Build Windows Player")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
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
