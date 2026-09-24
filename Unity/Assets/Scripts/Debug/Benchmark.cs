using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Frame time measurement for a standalone build, started by the command line argument
    /// -benchmark and doing nothing otherwise. The player's car is driven by an AI, so the
    /// camera follows a real race; vsync and any frame cap are off, so the number is what the
    /// machine can do. Records SampleSeconds of frames starting WarmupSeconds after load, then
    /// writes a report to the path after -benchmarkOut (default: benchmark.txt beside the
    /// executable) and quits.
    ///
    ///   CarRace.exe -benchmark -screen-width 1920 -screen-height 1080 -screen-fullscreen 1
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        const float WarmupSeconds = 8f;     // the 3 s countdown, then the field spreads out
        const float SampleSeconds = 60f;

        readonly List<float> _frames = new List<float>(20000);
        string _outPath;
        float _elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfAsked()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") < 0) return;

            // After the scene's Awake and before its Start, so the director sees the flag.
            var director = FindAnyObjectByType<RaceDirector>();
            if (director != null) director.AiDrivesPlayer = true;

            var go = new GameObject("Benchmark");
            DontDestroyOnLoad(go);
            var bench = go.AddComponent<Benchmark>();
            int i = Array.IndexOf(args, "-benchmarkOut");
            bench._outPath = i >= 0 && i + 1 < args.Length
                ? args[i + 1]
                : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "benchmark.txt");
        }

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            if (_elapsed < WarmupSeconds) return;
            _frames.Add(dt);
            if (_elapsed < WarmupSeconds + SampleSeconds) return;

            File.WriteAllText(_outPath, Report());
            Application.Quit();
            enabled = false;
        }

        string Report()
        {
            var sorted = new List<float>(_frames);
            sorted.Sort();
            float total = 0f;
            foreach (float f in _frames) total += f;
            // The 1% and 0.1% lows: average frame rate over the slowest 1% and 0.1% of frames.
            float Low(float share)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(sorted.Count * share));
                float sum = 0f;
                for (int k = sorted.Count - count; k < sorted.Count; k++) sum += sorted[k];
                return count / sum;
            }

            return $"frames          {_frames.Count} over {total:0.0} s\n" +
                   $"average         {_frames.Count / total:0.0} fps ({1000f * total / _frames.Count:0.00} ms)\n" +
                   $"median          {1f / sorted[sorted.Count / 2]:0.0} fps\n" +
                   $"1% low          {Low(0.01f):0.0} fps\n" +
                   $"0.1% low        {Low(0.001f):0.0} fps\n" +
                   $"worst frame     {1000f * sorted[sorted.Count - 1]:0.0} ms\n" +
                   $"resolution      {Screen.width}x{Screen.height} {(Screen.fullScreen ? "fullscreen" : "windowed")}\n" +
                   $"quality level   {QualitySettings.names[QualitySettings.GetQualityLevel()]}\n" +
                   $"gpu             {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})\n" +
                   $"cpu             {SystemInfo.processorType}, {SystemInfo.processorCount} threads\n" +
                   $"fixed timestep  {Time.fixedDeltaTime * 1000f:0.0} ms\n" +
                   $"unity           {Application.unityVersion}\n";
        }
    }
}
