using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// A tour of the circuit for looking at its scenery, started by -viewpoints &lt;prefix&gt; and
    /// doing nothing otherwise. Use it with -benchmark, which takes the lobby straight to the
    /// race: once the circuit has loaded, the camera leaves the car and stops at each of Views
    /// in turn, saving &lt;prefix&gt;-&lt;name&gt;.png, then the game quits. The views come from the
    /// circuit's own centreline, so every circuit gets the same set: down the grid at a
    /// driver's height, the start from the grandstand side, the outside of the three tightest
    /// corners, the whole circuit from the air, and out across the land from the track.
    ///
    /// -viewpointsOf villa,banking adds a view of each named object from the nearest point of
    /// the track, at a driver's height, as someone racing past would see it.
    ///
    ///   CarRace.exe -benchmark -noHud -track "Track Royal Park Speedway" -viewpoints D:\shots\royal
    /// </summary>
    public sealed class Viewpoints : MonoBehaviour
    {
        string _prefix;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfAsked()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-viewpoints");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("Viewpoints");
            DontDestroyOnLoad(go);
            go.AddComponent<Viewpoints>()._prefix = args[i + 1];
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) =>
            {
                var path = FindAnyObjectByType<TrackPath>();
                if (path != null) go.GetComponent<Viewpoints>().StartCoroutine(go.GetComponent<Viewpoints>().Tour(path));
            };
        }

        IEnumerator Tour(TrackPath path)
        {
            // Let the scene settle: terrain, instanced trees and the sky's first frames.
            yield return new WaitForSecondsRealtime(2f);
            Camera camera = Camera.main;
            foreach (var follow in FindObjectsByType<CarCamera>(FindObjectsSortMode.None)) follow.enabled = false;
            foreach (var (name, eye, target) in Views(path))
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                // Trees choose near or far versions from the camera, re-sorting after it moves.
                for (int f = 0; f < 4; f++) yield return null;
                ScreenCapture.CaptureScreenshot($"{_prefix}-{name}.png");
                yield return null;
                yield return null;
            }
            Debug.Log($"Viewpoints written to {_prefix}-*.png");
            Application.Quit();
        }

        static List<(string name, Vector3 eye, Vector3 target)> Views(TrackPath path)
        {
            Vector3[] c = path.centre;
            int n = c.Length;
            Vector3 At(int i) => c[((i % n) + n) % n];
            Vector3 Right(int i)
            {
                Vector3 d = At(i + 1) - At(i - 1);
                return new Vector3(d.z, 0f, -d.x).normalized;
            }
            int Ahead(float metres) => Mathf.RoundToInt(metres / Mathf.Max(path.sampleSpacing, 0.1f));

            var views = new List<(string, Vector3, Vector3)>
            {
                ("grid", At(-Ahead(40)) + Vector3.up * 1.3f, At(Ahead(60)) + Vector3.up * 1f),
                ("start", At(0) + Right(0) * 30f + Vector3.up * 12f, At(Ahead(10))),
            };

            // The pit lane, from the TrackData the AI drive: down it from the entry line at a
            // driver's height, and over the boxes from above the track.
            CarRace.Track.TrackData pit = path.ToTrackData();
            pit.EnsureLanes(CarRace.Track.RaceDriver.DefaultHalfWidthM);
            pit.EnsurePitLane();
            if (pit.HasPitLane)
            {
                Vector3 Lane(int i, float extra = 0f) => At(i) + Right(i) * (pit.PitOffsetM[((i % n) + n) % n] + extra);
                int entry = pit.PitEntryLine;
                views.Add(("pitlane", Lane(entry - Ahead(15)) + Vector3.up * 1.3f, Lane(entry + Ahead(80)) + Vector3.up * 1f));
                views.Add(("pitboxes", At(-Ahead(30)) - Right(0) * 4f + Vector3.up * 9f, Lane(0, pit.PitBoxShiftM)));
            }

            // The three tightest corners, at least 150 m apart, seen from their outside.
            var turns = new List<(float bend, int i)>();
            int span = Ahead(20);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (At(i) - At(i - span)).normalized, b = (At(i + span) - At(i)).normalized;
                turns.Add((Vector3.SignedAngle(a, b, Vector3.up), i));
            }
            turns.Sort((x, y) => Mathf.Abs(y.bend).CompareTo(Mathf.Abs(x.bend)));
            var chosen = new List<(float, int)>();
            foreach (var t in turns)
            {
                if (chosen.Count == 3) break;
                int Apart(int a, int b) => Mathf.Min(Mathf.Abs(a - b), n - Mathf.Abs(a - b));
                if (chosen.TrueForAll(o => Apart(o.Item2, t.i) * path.sampleSpacing > 150f)) chosen.Add(t);
            }
            for (int k = 0; k < chosen.Count; k++)
            {
                var (bend, i) = chosen[k];
                Vector3 outside = Right(i) * (bend > 0f ? -1f : 1f);
                views.Add(($"corner{k + 1}", At(i) + outside * 28f + Vector3.up * 3f, At(i - Ahead(15)) + Vector3.up * 1f));
            }

            // From the air, and out across the land from the track's edge.
            Vector3 middle = Vector3.zero;
            foreach (Vector3 p in c) middle += p;
            middle /= n;
            float reach = 0f;
            foreach (Vector3 p in c) reach = Mathf.Max(reach, Vector3.Distance(new Vector3(p.x, middle.y, p.z), middle));
            views.Add(("aerial", middle + new Vector3(reach * 0.7f, reach * 0.9f, -reach * 0.7f), middle));
            int far = 0;
            for (int i = 0; i < n; i++)
                if (Vector3.Distance(At(i), middle) > Vector3.Distance(At(far), middle)) far = i;
            Vector3 away = (At(far) - middle);
            away.y = 0f;
            views.Add(("horizon", At(far) + Vector3.up * 2f, At(far) + away.normalized * 500f + Vector3.up * 20f));

            string[] args = Environment.GetCommandLineArgs();
            int named = Array.IndexOf(args, "-viewpointsOf");
            if (named >= 0 && named + 1 < args.Length)
                foreach (string name in args[named + 1].Split(','))
                {
                    GameObject thing = GameObject.Find(name);
                    if (thing == null || !thing.TryGetComponent(out Renderer renderer)) continue;
                    Vector3 target = renderer.bounds.center;
                    int near = 0;
                    for (int i = 0; i < n; i++)
                        if ((At(i) - target).sqrMagnitude < (At(near) - target).sqrMagnitude) near = i;
                    views.Add((name, At(near) + Vector3.up * 2f, target));
                }
            return views;
        }
    }
}
