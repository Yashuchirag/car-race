using System;

namespace CarRace.UnityGame.Audio
{
    // Small signal-processing parts for the synthesised car sounds. Plain C#, no Unity, so
    // Sim/CarRace.AudioRender can render the same code to a WAV file outside the game.

    /// <summary>White noise, -1 to 1, from a xorshift generator: cheap and allocation free
    /// on the audio thread, where System.Random is neither.</summary>
    public struct Noise
    {
        uint _state;
        public Noise(uint seed) { _state = seed == 0 ? 0x9E3779B9u : seed; }

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFFFF) / 8388608f - 1f;
        }

        /// <summary>0 to 1.</summary>
        public float Unit() => (Next() + 1f) * 0.5f;
    }

    /// <summary>A second order filter (Robert Bristow-Johnson's cookbook). Its frequency can
    /// be moved every sample; the coefficients are only worked out again when it changes.</summary>
    public struct Biquad
    {
        public enum Kind { LowPass, HighPass, BandPass }

        float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;
        float _frequency, _q;
        Kind _kind;
        float _sampleRate;

        public static Biquad Make(Kind kind, float frequency, float q, float sampleRate)
        {
            var f = new Biquad { _kind = kind, _sampleRate = sampleRate, _q = q };
            f.Tune(frequency);
            return f;
        }

        public void Tune(float frequency)
        {
            frequency = MathF.Min(MathF.Max(frequency, 10f), _sampleRate * 0.45f);
            if (MathF.Abs(frequency - _frequency) < 0.5f) return;
            _frequency = frequency;
            float w = 2f * MathF.PI * frequency / _sampleRate;
            float cos = MathF.Cos(w), alpha = MathF.Sin(w) / (2f * _q);
            float a0 = 1f + alpha;
            switch (_kind)
            {
                case Kind.LowPass:
                    _b0 = (1f - cos) * 0.5f; _b1 = 1f - cos; _b2 = _b0; break;
                case Kind.HighPass:
                    _b0 = (1f + cos) * 0.5f; _b1 = -(1f + cos); _b2 = _b0; break;
                default:
                    _b0 = alpha; _b1 = 0f; _b2 = -alpha; break;
            }
            _b0 /= a0; _b1 /= a0; _b2 /= a0;
            _a1 = -2f * cos / a0;
            _a2 = (1f - alpha) / a0;
        }

        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }

    /// <summary>A pipe: what goes in comes back after the delay, times the reflection. A
    /// negative reflection is an open end, a positive one a closed end or a chamber.</summary>
    public sealed class Pipe
    {
        readonly float[] _line;
        int _at;
        readonly float _reflection;

        public Pipe(float seconds, float reflection, float sampleRate)
        {
            _line = new float[Math.Max(1, (int)(seconds * sampleRate))];
            _reflection = reflection;
        }

        public float Process(float x)
        {
            float y = x + _reflection * _line[_at];
            _line[_at] = y;
            _at = (_at + 1) % _line.Length;
            return y;
        }
    }

    /// <summary>Takes out the DC a pulse train leaves, which would otherwise push the
    /// speaker to one side and eat headroom.</summary>
    public struct DcBlocker
    {
        float _x1, _y1;
        public float Process(float x)
        {
            float y = x - _x1 + 0.995f * _y1;
            _x1 = x; _y1 = y;
            return y;
        }
    }

    /// <summary>Eases a value towards its target with a time constant, per sample, so a
    /// control set 50 times a second does not step audibly.</summary>
    public struct Smoothed
    {
        public float Value;
        float _k;
        public Smoothed(float value, float seconds, float sampleRate)
        {
            Value = value;
            _k = 1f - MathF.Exp(-1f / (seconds * sampleRate));
        }
        public float Toward(float target) => Value += (target - Value) * _k;
    }
}
