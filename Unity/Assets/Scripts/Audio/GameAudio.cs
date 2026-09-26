using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The sounds that are not a car's: the menus' hover, select, confirm and back, from
    /// Kenney's CC0 interface packs (Assets/Resources/Audio/UI, see Art/CREDITS.md), and the
    /// start countdown's beeps, made here as tones. One 2D source kept across scenes, which
    /// ignores the pause, so the pause menu's own buttons still sound.
    ///
    /// IMGUI has no hover event, so a menu calls Hover.Begin at the top of OnGUI and
    /// Hover.Watch for each button; the sound plays when the pointer moves onto a different
    /// button, not every frame it stays there.
    ///
    /// Also pauses every other sound while the game is paused, and under -recordAudio PATH
    /// writes what the listener hears to a WAV file, for checking the mix without ears on
    /// the machine.
    /// </summary>
    public static class GameAudio
    {
        static AudioSource _source;
        static AudioClip _hover, _select, _confirm, _back, _beep, _go;

        static AudioSource Source
        {
            get
            {
                if (_source != null) return _source;
                var go = new GameObject("Game Audio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.ignoreListenerPause = true;
                _hover = Resources.Load<AudioClip>("Audio/UI/hover");
                _select = Resources.Load<AudioClip>("Audio/UI/select");
                _confirm = Resources.Load<AudioClip>("Audio/UI/confirm");
                _back = Resources.Load<AudioClip>("Audio/UI/back");
                _beep = Tone("Beep", 880f, 0.16f);
                _go = Tone("Go", 1760f, 0.45f);
                return _source;
            }
        }

        public static void Hover() => Play(() => _hover, 0.3f);
        public static void Select() => Play(() => _select, 0.7f);
        public static void Confirm() => Play(() => _confirm, 0.8f);
        public static void Back() => Play(() => _back, 0.7f);
        public static void Countdown(bool go) => Play(() => go ? _go : _beep, 0.45f);

        /// <summary>The clip is asked for after Source, which loads the clips the first time.</summary>
        static void Play(Func<AudioClip> clip, float volume)
        {
            AudioSource source = Source;
            AudioClip c = clip();
            if (c != null) source.PlayOneShot(c, volume);
        }

        /// <summary>A sine tone with a quick attack and a smooth fall, for the countdown.</summary>
        static AudioClip Tone(string name, float hz, float seconds)
        {
            int rate = AudioSettings.outputSampleRate, n = (int)(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-3f * t / seconds);
                data[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * envelope * 0.8f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Hover sounds for one IMGUI menu.</summary>
        public sealed class HoverTracker
        {
            int _last = -1, _now = -1, _counter;

            public void Begin()
            {
                if (Event.current.type == EventType.Repaint)
                {
                    if (_now != _last && _now >= 0) Hover();
                    _last = _now;
                    _now = -1;
                }
                _counter = 0;
            }

            public void Watch(Rect button)
            {
                int id = _counter++;
                if (Event.current.type == EventType.Repaint && button.Contains(Event.current.mousePosition)) _now = id;
            }
        }

        /// <summary>Everything but the menus falls silent while the game is paused.</summary>
        public static void SetPaused(bool paused) => AudioListener.pause = paused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            AudioListener.pause = false;
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-recordAudio");
            if (i < 0 || i + 1 >= args.Length) return;
            string path = args[i + 1];
            void Attach(Scene scene, LoadSceneMode mode)
            {
                var listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
                if (listener != null && listener.GetComponent<AudioRecorder>() == null)
                    listener.gameObject.AddComponent<AudioRecorder>().Path = path;
            }
            SceneManager.sceneLoaded += Attach;
            Attach(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
    }
}
