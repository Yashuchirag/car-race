using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Frame time measurement for a standalone build, started by the command line argument
    /// -benchmark and doing nothing otherwise. The player's car is driven by an AI, so the
    /// camera follows a real race; vsync and any frame cap are off, so the number is what the
    /// machine can do. Records SampleSeconds of frames starting WarmupSeconds after load, then
    /// writes a report to the path after -benchmarkOut (default: benchmark.txt beside the
    /// executable) and quits. Times count from the race scene loading, not from the lobby. Beside the report, a CSV with one row per frame: its time, the
    /// physics steps it ran, garbage collections and managed memory, for finding what the
    /// slow frames have in common. In a development build it also records, per frame, the
    /// time spent in the profiler markers named in Markers, and lists every marker the player
    /// has in a third file, so a marker that is misnamed shows up as missing rather than as 0.
    ///
    ///   CarRace.exe -benchmark -screen-width 1920 -screen-height 1080 -screen-fullscreen 1
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        const float WarmupSeconds = 8f;     // the 3 s countdown, then the field spreads out
        /// <summary>60 s unless -benchmarkSeconds says otherwise, for watching a longer race.</summary>
        static float SampleSeconds => Argument("-benchmarkSeconds", 60f);
        // In the warm-up, so its cost is not measured, unless -screenshotAt asks for other
        // moments, comma separated, for looking at the game mid race. Each is saved beside the
        // report, named by its time.
        static float[] ScreenshotTimes
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-screenshotAt");
                if (i < 0 || i + 1 >= args.Length) return new[] { 7f };
                var times = new List<float>();
                foreach (string part in args[i + 1].Split(','))
                    if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float at))
                        times.Add(at);
                return times.Count > 0 ? times.ToArray() : new[] { 7f };
            }
        }

        static float Argument(string name, float fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }
        readonly float[] _screenshotTimes = ScreenshotTimes;
        readonly float _sampleSeconds = SampleSeconds;
        int _shots;

        readonly List<float> _frames = new List<float>(20000);
        // Numbers only while sampling, formatted at the end: building the CSV line by line
        // allocated strings every frame, which is part of what this is measuring.
        readonly List<(float t, int steps, int gc, long memory)> _rows = new List<(float, int, int, long)>(20000);
        int _fixedSteps, _lastCollections;

        static readonly string[] Markers =
        {
            "FixedBehaviourUpdate", "Physics.Simulate", "Physics.Processing", "Physics.FetchResults",
            "BehaviourUpdate", "LateBehaviourUpdate", "GUIUtility.ProcessEvent", "GUI.Repaint",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.PresentFrame", "RenderPipelineManager.DoRenderLoop_Internal()",
            "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "GC.Collect", "Texture2D.Apply",
        };
        ProfilerRecorder[] _recorders = new ProfilerRecorder[0];
        readonly List<float[]> _markerMs = new List<float[]>(20000);
        long _lastMemory;
        string _outPath;
        float _elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfAsked()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") < 0) return;

            Hud.Hidden = Array.IndexOf(args, "-noHud") >= 0;

            var go = new GameObject("Benchmark");
            DontDestroyOnLoad(go);
            var bench = go.AddComponent<Benchmark>();
            // The game opens on the lobby, which goes straight on to the race under -benchmark;
            // the race is set up, and the clock started, when its scene loads.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => bench.RaceLoaded();
            bench.RaceLoaded();
            int i = Array.IndexOf(args, "-benchmarkOut");
            bench._outPath = i >= 0 && i + 1 < args.Length
                ? args[i + 1]
                : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "benchmark.txt");
        }

        /// <summary>
        /// Called as a scene loads, after its Awake and before its Start, so the director sees
        /// the flag in time. Only a race scene starts the measurement.
        /// </summary>
        void RaceLoaded()
        {
            var director = FindAnyObjectByType<RaceDirector>();
            if (director == null) return;
            // Unless a -driveScript is driving it instead, to test the inputs.
            director.AiDrivesPlayer = !DriverInput.Scripted;

            // A disabled component's Start never runs, so the recorder opens no file.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noTelemetry") >= 0)
                foreach (var recorder in FindObjectsByType<TelemetryRecorder>(FindObjectsSortMode.None)) recorder.enabled = false;
            _raceLoaded = true;
            _elapsed = 0f;
        }

        bool _raceLoaded;

        void Awake()
        {
            // Uncapped, to measure what the machine can do, unless -frameRate asks otherwise:
            // then it measures that the cap holds.
            if (!DisplaySettings.SetOnCommandLine)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            if (!Debug.isDebugBuild) return;
            // Looked up by name across every category, since guessing a marker's category wrong
            // finds nothing. Each recorder sums its marker's samples within a frame, so a
            // marker that runs once per physics step reads as that frame's total.
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            _recorders = new ProfilerRecorder[Markers.Length];
            for (int i = 0; i < Markers.Length; i++)
            {
                foreach (var h in handles)
                {
                    if (ProfilerRecorderHandle.GetDescription(h).Name != Markers[i]) continue;
                    _recorders[i] = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                    _recorders[i].Start();
                    break;
                }
            }
        }

        void OnDestroy()
        {
            foreach (var r in _recorders) r.Dispose();
        }

        void FixedUpdate() => _fixedSteps++;

        void Update()
        {
            if (!_raceLoaded) return;
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            int collections = GC.CollectionCount(0);
            long memory = GC.GetTotalMemory(false);
            if (_shots < _screenshotTimes.Length && _elapsed >= _screenshotTimes[_shots])
            {
                string suffix = _screenshotTimes.Length == 1 ? "" : $"-{_screenshotTimes[_shots]:0}";
                ScreenCapture.CaptureScreenshot(Path.ChangeExtension(_outPath, null) + suffix + ".png");
                _shots++;
            }
            if (_elapsed < WarmupSeconds)
            {
                _fixedSteps = 0;
                _lastCollections = collections;
                _lastMemory = memory;
                return;
            }
            _frames.Add(dt);
            _rows.Add((_elapsed, _fixedSteps, collections - _lastCollections, memory - _lastMemory));
            if (_recorders.Length > 0)
            {
                var ms = new float[_recorders.Length];
                for (int i = 0; i < _recorders.Length; i++)
                    ms[i] = _recorders[i].Valid ? _recorders[i].LastValue * 1e-6f : -1f;
                _markerMs.Add(ms);
            }
            _fixedSteps = 0;
            _lastCollections = collections;
            _lastMemory = memory;
            if (_elapsed < WarmupSeconds + _sampleSeconds) return;

            File.WriteAllText(_outPath, Report());
            var csv = new System.Text.StringBuilder("t,ms,fixed_steps,gc,mem_delta");
            if (_markerMs.Count > 0) foreach (string m in Markers) csv.Append(',').Append(m);
            csv.Append('\n');
            for (int k = 0; k < _rows.Count; k++)
            {
                csv.Append(_rows[k].t.ToString("0.0000")).Append(',').Append((_frames[k] * 1000f).ToString("0.000"))
                   .Append(',').Append(_rows[k].steps).Append(',').Append(_rows[k].gc).Append(',').Append(_rows[k].memory);
                if (k < _markerMs.Count) foreach (float v in _markerMs[k]) csv.Append(',').Append(v.ToString("0.000"));
                csv.Append('\n');
            }
            if (_recorders.Length > 0)
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var names = new List<string>();
                foreach (var h in handles) names.Add(ProfilerRecorderHandle.GetDescription(h).Name);
                names.Sort(StringComparer.Ordinal);
                File.WriteAllLines(Path.ChangeExtension(_outPath, ".markers.txt"), names);
            }
            File.WriteAllText(Path.ChangeExtension(_outPath, ".frames.csv"), csv.ToString());
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
