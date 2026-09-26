using UnityEngine;
using CarRace.UnityGame.Audio;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Plays a car's synthesised engine and tyres through the AudioSource beside it. The
    /// source plays a clip of constant 1.0, so what reaches this filter is that 1.0 already
    /// attenuated and panned for where the car is; multiplying the synthesis in here puts
    /// it in the world with Unity's own 3D sound. Runs on the audio thread: the synths'
    /// controls are plain floats the main thread writes.
    /// </summary>
    public sealed class SynthVoice : MonoBehaviour
    {
        public EngineSynth Engine;
        public TyreSynth Tyres;
        public float EngineLevel = 1f, TyreLevel = 1f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            EngineSynth engine = Engine;
            TyreSynth tyres = Tyres;
            if (engine == null || tyres == null) return;
            for (int i = 0; i < data.Length; i += channels)
            {
                float s = engine.Next() * EngineLevel + tyres.Next() * TyreLevel;
                for (int c = 0; c < channels; c++) data[i + c] *= s;
            }
        }

        /// <summary>A short loop of 1.0 for the source to play.</summary>
        public static AudioClip Ones()
        {
            int rate = AudioSettings.outputSampleRate;
            var clip = AudioClip.Create("Ones", rate / 10, 1, rate, false);
            var data = new float[rate / 10];
            System.Array.Fill(data, 1f);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
