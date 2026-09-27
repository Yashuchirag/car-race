using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CarRace.Net
{
    /// <summary>One entry in the lobby or on the grid: a person, or an AI car the host drives.</summary>
    public sealed class PlayerInfo
    {
        public byte Id;
        public string Name = "";
        public byte Colour;
        public byte Design;
        public bool Ai;

        /// <summary>In the lobby: happy with their car and waiting for the start. The host
        /// can start once everyone who has joined is.</summary>
        public bool Ready;
    }

    /// <summary>What everyone in the lobby sees: the circuit, the race length, how many AI
    /// cars the host is adding, and the people who have joined, the host first.</summary>
    public sealed class LobbyState
    {
        public string Track = "";
        public byte Laps = 3;
        public byte AiCars;
        public List<PlayerInfo> Players = new List<PlayerInfo>();
    }

    /// <summary>The host's word that the race is on: load the circuit. The grid is in slot
    /// order, pole first. GO comes separately, once every player has loaded (Control.Go).</summary>
    public sealed class RaceStart
    {
        public string Track = "";
        public byte Laps;
        public PlayerInfo[] Grid = Array.Empty<PlayerInfo>();
    }

    public enum RejectReason : byte { Full = 1, Started = 2, Version = 3 }

    /// <summary>
    /// The messages that have to arrive: joining, the lobby, the start. They go over TCP,
    /// each as a two byte length and then the message, whose first byte is its type. None of
    /// this is sent often, so plain BinaryWriter is used rather than bit packing.
    /// </summary>
    public static class Control
    {
        /// <summary>Bumped whenever any message here or a datagram changes meaning.</summary>
        public const byte Version = 2;

        public enum Type : byte { Hello = 1, Welcome, Reject, Lobby, Setup, Start, Ready, Go }

        public static byte[] Hello(PlayerInfo me) => Build(Type.Hello, w =>
        {
            w.Write(Version);
            WriteString(w, me.Name);
            w.Write(me.Colour);
            w.Write(me.Design);
        });

        public static byte[] Welcome(byte id) => Build(Type.Welcome, w => w.Write(id));

        public static byte[] Reject(RejectReason reason) => Build(Type.Reject, w => w.Write((byte)reason));

        public static byte[] Lobby(LobbyState lobby) => Build(Type.Lobby, w =>
        {
            WriteString(w, lobby.Track);
            w.Write(lobby.Laps);
            w.Write(lobby.AiCars);
            WritePlayers(w, lobby.Players);
        });

        /// <summary>A player's car, colour and whether they are ready, from the lobby.</summary>
        public static byte[] Setup(byte colour, byte design, bool ready) => Build(Type.Setup, w =>
        {
            w.Write(colour);
            w.Write(design);
            w.Write(ready);
        });

        public static byte[] Start(RaceStart start) => Build(Type.Start, w =>
        {
            WriteString(w, start.Track);
            w.Write(start.Laps);
            WritePlayers(w, start.Grid);
        });

        /// <summary>A player's word that the circuit has loaded and its car is on the grid.</summary>
        public static byte[] Ready() => Build(Type.Ready, w => { });

        /// <summary>The instant of GO, on the host's clock.</summary>
        public static byte[] Go(float atHostSeconds) => Build(Type.Go, w => w.Write(atHostSeconds));

        public static float ReadGo(byte[] message)
        {
            using BinaryReader r = Body(message);
            return r.ReadSingle();
        }

        public static Type TypeOf(byte[] message) => (Type)message[0];

        /// <summary>A reader positioned after the type byte.</summary>
        public static BinaryReader Body(byte[] message)
            => new BinaryReader(new MemoryStream(message, 1, message.Length - 1), Encoding.UTF8);

        public static PlayerInfo ReadHello(byte[] message, out byte version)
        {
            using BinaryReader r = Body(message);
            version = r.ReadByte();
            return new PlayerInfo { Name = ReadString(r), Colour = r.ReadByte(), Design = r.ReadByte() };
        }

        public static LobbyState ReadLobby(byte[] message)
        {
            using BinaryReader r = Body(message);
            return new LobbyState
            {
                Track = ReadString(r), Laps = r.ReadByte(), AiCars = r.ReadByte(),
                Players = new List<PlayerInfo>(ReadPlayers(r)),
            };
        }

        public static RaceStart ReadStart(byte[] message)
        {
            using BinaryReader r = Body(message);
            return new RaceStart
            {
                Track = ReadString(r), Laps = r.ReadByte(), Grid = ReadPlayers(r),
            };
        }

        static byte[] Build(Type type, Action<BinaryWriter> body)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write((byte)type);
                body(w);
            }
            return stream.ToArray();
        }

        static void WritePlayers(BinaryWriter w, IReadOnlyList<PlayerInfo> players)
        {
            w.Write((byte)players.Count);
            foreach (PlayerInfo p in players)
            {
                w.Write(p.Id);
                WriteString(w, p.Name);
                w.Write(p.Colour);
                w.Write(p.Design);
                w.Write(p.Ai);
                w.Write(p.Ready);
            }
        }

        static PlayerInfo[] ReadPlayers(BinaryReader r)
        {
            var players = new PlayerInfo[r.ReadByte()];
            for (int i = 0; i < players.Length; i++)
                players[i] = new PlayerInfo
                {
                    Id = r.ReadByte(), Name = ReadString(r),
                    Colour = r.ReadByte(), Design = r.ReadByte(), Ai = r.ReadBoolean(), Ready = r.ReadBoolean(),
                };
            return players;
        }

        /// <summary>A byte of length and at most 32 bytes of UTF-8, so a name cannot grow a
        /// message past what the lobby can draw.</summary>
        static void WriteString(BinaryWriter w, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            if (bytes.Length > 32) Array.Resize(ref bytes, 32);
            w.Write((byte)bytes.Length);
            w.Write(bytes);
        }

        static string ReadString(BinaryReader r)
        {
            int length = r.ReadByte();
            return Encoding.UTF8.GetString(r.ReadBytes(length));
        }
    }
}
