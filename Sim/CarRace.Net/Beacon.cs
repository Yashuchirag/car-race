using System;
using System.Text;

namespace CarRace.Net
{
    /// <summary>
    /// What a host shouts onto the LAN so that a player does not have to be told an IP
    /// address. The payload only: the socket that carries it belongs to whoever is hosting,
    /// because a dedicated server, a listen server and a Unity client each want to own that
    /// differently, while all three have to agree on these bytes.
    /// </summary>
    public struct Beacon
    {
        /// <summary>Bumped whenever these bytes change meaning. A client that does not
        /// recognise the version must ignore the packet rather than guess at it.</summary>
        public const byte Version = 1;

        public const int Port = 47901;
        static readonly byte[] Magic = { (byte)'C', (byte)'R', (byte)'C', (byte)'E' };

        public string HostName;
        public string Track;
        public ushort GamePort;
        public byte Players;
        public byte Capacity;

        public byte[] ToBytes()
        {
            byte[] host = Encoding.UTF8.GetBytes(HostName ?? "host");
            byte[] track = Encoding.UTF8.GetBytes(Track ?? "unknown");
            if (host.Length > 63) Array.Resize(ref host, 63);
            if (track.Length > 63) Array.Resize(ref track, 63);

            var bytes = new byte[4 + 1 + 2 + 1 + 1 + 1 + host.Length + 1 + track.Length];
            int at = 0;
            Array.Copy(Magic, 0, bytes, at, 4); at += 4;
            bytes[at++] = Version;
            bytes[at++] = (byte)(GamePort & 0xFF);
            bytes[at++] = (byte)(GamePort >> 8);
            bytes[at++] = Players;
            bytes[at++] = Capacity;
            bytes[at++] = (byte)host.Length;
            Array.Copy(host, 0, bytes, at, host.Length); at += host.Length;
            bytes[at++] = (byte)track.Length;
            Array.Copy(track, 0, bytes, at, track.Length);
            return bytes;
        }

        /// <summary>
        /// Reads a beacon, or fails. Every length is checked against what actually arrived:
        /// this is the one place in the game that parses bytes from a machine nobody
        /// controls, and a broadcast port receives whatever else is on the network.
        /// </summary>
        public static bool TryParse(byte[] bytes, out Beacon beacon)
        {
            beacon = default;
            if (bytes == null || bytes.Length < 11) return false;
            for (int i = 0; i < 4; i++) if (bytes[i] != Magic[i]) return false;
            if (bytes[4] != Version) return false;

            int at = 5;
            ushort port = (ushort)(bytes[at] | (bytes[at + 1] << 8)); at += 2;
            byte players = bytes[at++];
            byte capacity = bytes[at++];

            int hostLength = bytes[at++];
            if (at + hostLength + 1 > bytes.Length) return false;
            string host = Encoding.UTF8.GetString(bytes, at, hostLength); at += hostLength;

            int trackLength = bytes[at++];
            if (at + trackLength > bytes.Length) return false;
            string track = Encoding.UTF8.GetString(bytes, at, trackLength);

            beacon = new Beacon
            {
                HostName = host, Track = track, GamePort = port,
                Players = players, Capacity = capacity,
            };
            return true;
        }
    }
}
