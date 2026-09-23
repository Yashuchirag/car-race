using System;

namespace CarRace.Net
{
    /// <summary>
    /// Writes and reads fields of arbitrary bit width into a byte buffer.
    ///
    /// Car state is mostly numbers with a known range and a known useful precision, and
    /// rounding each one to what it is worth costs nothing and saves most of the packet.
    /// A position as three floats is twelve bytes; to the centimetre, over a circuit a
    /// few kilometres across, it is seven.
    /// </summary>
    public sealed class BitWriter
    {
        readonly byte[] _bytes;
        int _bit;

        public BitWriter(byte[] bytes) { _bytes = bytes; }

        public int BitsWritten => _bit;

        /// <summary>The buffer being written into. Copy out only BytesWritten of it.</summary>
        public byte[] GetBuffer() => _bytes;
        public int BytesWritten => (_bit + 7) / 8;

        public void Reset() { _bit = 0; Array.Clear(_bytes, 0, _bytes.Length); }

        public void WriteBits(uint value, int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));
            if (bits < 32) value &= (1u << bits) - 1u;

            for (int i = 0; i < bits; i++)
            {
                if ((value & (1u << i)) != 0)
                    _bytes[(_bit + i) >> 3] |= (byte)(1 << ((_bit + i) & 7));
            }
            _bit += bits;
        }

        public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

        /// <summary>Rounds to the nearest step of (max - min) / (2^bits - 1) and clamps.</summary>
        public void WriteFloat(float value, float min, float max, int bits)
            => WriteBits(Quantise.Encode(value, min, max, bits), bits);
    }

    public sealed class BitReader
    {
        readonly byte[] _bytes;
        int _bit;

        public BitReader(byte[] bytes) { _bytes = bytes; }

        public int BitsRead => _bit;

        public uint ReadBits(int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));

            uint value = 0;
            for (int i = 0; i < bits; i++)
            {
                if ((_bytes[(_bit + i) >> 3] & (1 << ((_bit + i) & 7))) != 0) value |= 1u << i;
            }
            _bit += bits;
            return value;
        }

        public bool ReadBool() => ReadBits(1) != 0;

        public float ReadFloat(float min, float max, int bits)
            => Quantise.Decode(ReadBits(bits), min, max, bits);
    }

    public static class Quantise
    {
        public static uint Encode(float value, float min, float max, int bits)
        {
            float span = max - min;
            if (span <= 0f) return 0u;

            uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
            float t = (value - min) / span;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return (uint)(t * steps + 0.5f);
        }

        public static float Decode(uint raw, float min, float max, int bits)
        {
            uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
            return min + (max - min) * raw / steps;
        }

        /// <summary>Worst-case error of a round trip through Encode and Decode.</summary>
        public static float Resolution(float min, float max, int bits)
        {
            uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
            return (max - min) / steps * 0.5f;
        }
    }
}
