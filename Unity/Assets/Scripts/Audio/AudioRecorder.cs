using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Under -recordAudio PATH (GameAudio), on each scene's listener: everything heard, from
    /// the first scene to the last, written to a 16-bit stereo WAV once, when the game quits,
    /// so the mix can be measured and listened to afterwards.
    /// </summary>
    public sealed class AudioRecorder : MonoBehaviour
    {
        public string Path;
        // Shared by the recorder on every scene's listener, so a scene change does not
        // start the file again.
        static readonly List<short> _samples = new List<short>(48000 * 2 * 300);
        static int _channels = 2;
        static readonly object _lock = new object();

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                _channels = channels;
                foreach (float s in data) _samples.Add((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
        }

        void OnApplicationQuit() => Write();

        void Write()
        {
            short[] samples;
            int channels;
            lock (_lock)
            {
                if (_samples.Count == 0 || string.IsNullOrEmpty(Path)) return;
                samples = _samples.ToArray();
                channels = _channels;
                _samples.Clear();
            }
            int rate = AudioSettings.outputSampleRate, bytes = samples.Length * 2;
            using var w = new BinaryWriter(File.Create(Path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2);
            w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (short s in samples) w.Write(s);
            Debug.Log($"Audio recorded to {Path}: {samples.Length / channels / (float)rate:0.0} s.");
        }
    }
}
