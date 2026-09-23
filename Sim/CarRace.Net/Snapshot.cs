using System;
using System.Numerics;

namespace CarRace.Net
{
    /// <summary>One car as the wire carries it. World space, same axes as the simulation.</summary>
    public struct CarState
    {
        public byte Id;
        public Vector3 Position;
        public Quaternion Orientation;
        public Vector3 Velocity;
        public float Steer;        // -1..1, for turning the front wheels on the client
        public float EngineRpm;    // for engine audio, which is most of what speed feels like
        public byte Gear;          // 0 neutral, 1..N forward, 15 for reverse
        public byte Lap;
    }

    /// <summary>A whole field at one instant, with the host's clock attached.</summary>
    public sealed class Snapshot
    {
        public uint Tick;
        public float TimeSeconds;
        public CarState[] Cars = Array.Empty<CarState>();
    }

    /// <summary>
    /// Packs and unpacks snapshots.
    ///
    /// Every field is quantised to what it is worth rather than sent as a float. The ranges
    /// below are the contract: a position outside them is clamped, not wrapped, so the
    /// failure is a car pinned at the edge of the world rather than one that teleports
    /// across it. At 20 Hz with sixteen cars this is about 6 kB/s to each client, which is
    /// nothing on a LAN and leaves room for the things that matter more later, like inputs
    /// and collisions.
    /// </summary>
    public static class SnapshotCodec
    {
        public const float WorldExtentM = 4096f;     // circuits are a few km across
        public const float WorldHeightM = 1024f;
        public const float MaxSpeedMs = 150f;        // 540 km/h, well past anything drivable

        const int PositionBits = 20;                 // 8 mm over the extent
        const int HeightBits = 16;
        const int VelocityBits = 12;                 // 3.7 cm/s
        const int SteerBits = 8;
        const int RpmBits = 12;
        const int GearBits = 4;
        const int LapBits = 8;
        const int IdBits = 6;                        // 64 cars is plenty
        const int CountBits = 6;

        /// <summary>Bytes needed for a snapshot of this many cars, worst case.</summary>
        public static int MaxBytes(int cars) => 16 + cars * 16;

        public static int Write(BitWriter writer, Snapshot snapshot)
        {
            writer.Reset();
            writer.WriteBits(snapshot.Tick, 32);
            writer.WriteFloat(snapshot.TimeSeconds, 0f, 65536f, 32);
            writer.WriteBits((uint)snapshot.Cars.Length, CountBits);

            foreach (CarState car in snapshot.Cars)
            {
                writer.WriteBits(car.Id, IdBits);
                writer.WriteFloat(car.Position.X, -WorldExtentM, WorldExtentM, PositionBits);
                writer.WriteFloat(car.Position.Y, -WorldHeightM, WorldHeightM, HeightBits);
                writer.WriteFloat(car.Position.Z, -WorldExtentM, WorldExtentM, PositionBits);
                WriteRotation(writer, car.Orientation);
                writer.WriteFloat(car.Velocity.X, -MaxSpeedMs, MaxSpeedMs, VelocityBits);
                writer.WriteFloat(car.Velocity.Y, -MaxSpeedMs, MaxSpeedMs, VelocityBits);
                writer.WriteFloat(car.Velocity.Z, -MaxSpeedMs, MaxSpeedMs, VelocityBits);
                writer.WriteFloat(car.Steer, -1f, 1f, SteerBits);
                writer.WriteFloat(car.EngineRpm, 0f, 16000f, RpmBits);
                writer.WriteBits(car.Gear, GearBits);
                writer.WriteBits(car.Lap, LapBits);
            }

            return writer.BytesWritten;
        }

        public static Snapshot Read(BitReader reader)
        {
            var snapshot = new Snapshot
            {
                Tick = reader.ReadBits(32),
                TimeSeconds = reader.ReadFloat(0f, 65536f, 32),
            };

            int count = (int)reader.ReadBits(CountBits);
            snapshot.Cars = new CarState[count];

            for (int i = 0; i < count; i++)
            {
                snapshot.Cars[i] = new CarState
                {
                    Id = (byte)reader.ReadBits(IdBits),
                    Position = new Vector3(
                        reader.ReadFloat(-WorldExtentM, WorldExtentM, PositionBits),
                        reader.ReadFloat(-WorldHeightM, WorldHeightM, HeightBits),
                        reader.ReadFloat(-WorldExtentM, WorldExtentM, PositionBits)),
                    Orientation = ReadRotation(reader),
                    Velocity = new Vector3(
                        reader.ReadFloat(-MaxSpeedMs, MaxSpeedMs, VelocityBits),
                        reader.ReadFloat(-MaxSpeedMs, MaxSpeedMs, VelocityBits),
                        reader.ReadFloat(-MaxSpeedMs, MaxSpeedMs, VelocityBits)),
                    Steer = reader.ReadFloat(-1f, 1f, SteerBits),
                    EngineRpm = reader.ReadFloat(0f, 16000f, RpmBits),
                    Gear = (byte)reader.ReadBits(GearBits),
                    Lap = (byte)reader.ReadBits(LapBits),
                };
            }

            return snapshot;
        }

        /// <summary>
        /// Smallest three: a unit quaternion's largest component can be recovered from the
        /// other three, so only three of them are sent, plus two bits saying which was left
        /// out. Negating a quaternion leaves the rotation it describes unchanged, so the
        /// omitted component is made positive and its sign costs nothing either.
        ///
        /// The three that are sent can never exceed 1/sqrt(2) in magnitude, because the
        /// omitted one is the largest, so ten bits each covers that range to about a
        /// thousandth. Four bytes for an orientation, against sixteen sent raw.
        /// </summary>
        const int RotationBits = 10;
        static readonly float Limit = 1f / MathF.Sqrt(2f);

        static void WriteRotation(BitWriter writer, Quaternion q)
        {
            q = Quaternion.Normalize(q);
            Span<float> parts = stackalloc float[4] { q.X, q.Y, q.Z, q.W };

            int largest = 0;
            for (int i = 1; i < 4; i++)
                if (MathF.Abs(parts[i]) > MathF.Abs(parts[largest])) largest = i;

            float sign = parts[largest] < 0f ? -1f : 1f;

            writer.WriteBits((uint)largest, 2);
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                writer.WriteFloat(parts[i] * sign, -Limit, Limit, RotationBits);
            }
        }

        static Quaternion ReadRotation(BitReader reader)
        {
            int largest = (int)reader.ReadBits(2);
            Span<float> parts = stackalloc float[4];

            float sumOfSquares = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                parts[i] = reader.ReadFloat(-Limit, Limit, RotationBits);
                sumOfSquares += parts[i] * parts[i];
            }

            parts[largest] = MathF.Sqrt(MathF.Max(1f - sumOfSquares, 0f));
            return Quaternion.Normalize(new Quaternion(parts[0], parts[1], parts[2], parts[3]));
        }
    }
}
