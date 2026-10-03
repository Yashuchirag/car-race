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
        /// <summary>The tyre wear choice, a place in the game's list of rates (0 is off).</summary>
        public byte TyreWear;
        public bool SafetyCar = true;
        public List<PlayerInfo> Players = new List<PlayerInfo>();
    }

    /// <summary>The host's word that the race is on: load the circuit. The grid is in slot
    /// order, pole first. GO comes separately, once every player has loaded (Control.Go).</summary>
    public sealed class RaceStart
    {
        public string Track = "";
        public byte Laps;
        public byte TyreWear;
        public bool SafetyCar;
        public PlayerInfo[] Grid = Array.Empty<PlayerInfo>();
    }

    /// <summary>One car's place in the race, as the host keeps it.</summary>
    public struct Standing
    {
        public byte LapsComplete;
        public float ProgressM;        // distance since the start line, for ranking cars still running
        public float FinishedAtS;      // race time at the flag, or -1
        public float BestLapS;         // or -1 before a lap is complete
        public float LastLapS;
        public bool Left;              // the player quit the race
        public float PenaltyS;         // seconds added at the flag
        public byte Warnings, Penalties, PitStops;
        public bool Disqualified;
        public bool LapValid, LastLapValid;
    }

    /// <summary>The whole field, in grid order, at one race time. Everyone ranks it the same
    /// way, so the host sends the numbers and not the order.</summary>
    public sealed class Standings
    {
        public float RaceTimeS;
        public Standing[] Cars = Array.Empty<Standing>();
    }

    public enum RejectReason : byte { Full = 1, Started = 2, Version = 3 }

    /// <summary>What a player's machine tells the host about its own car, which only it can
    /// judge: it has the wheels. The host keeps the rulings.</summary>
    public enum ReportKind : byte { LeftTrack = 1, Judged = 2, PitIn = 3, PitOut = 4, TyresFitted = 5 }

    /// <summary>One report from a player: what, and for Judged the judge's verdict (the game's
    /// TrackLimits.Kind, as a byte).</summary>
    public struct Report
    {
        public byte Id;
        public ReportKind Kind;
        public byte Value;
    }

    /// <summary>A yellow zone: the car in trouble (its grid slot) and the samples it runs between.</summary>
    public struct FlagZone
    {
        public byte Car;
        public int From, To;
    }

    /// <summary>A place gained off the track and owed back: who owes it, to whom, by when.</summary>
    public struct FlagOwed
    {
        public byte Car, Passed;
        public float DeadlineS;
    }

    /// <summary>
    /// The marshals, as the host sees them, for everyone: the yellow zones, the flag each car is
    /// shown (the car in trouble whose zone it is in, the car about to lap it; -1 for none),
    /// places owed back, and the safety car's phase (the game's SafetyCar.Phase; 0 is in).
    /// Cars by grid slot.
    /// </summary>
    public sealed class FlagsState
    {
        public byte SafetyCar;
        public FlagZone[] Zones = Array.Empty<FlagZone>();
        public sbyte[] InYellow = Array.Empty<sbyte>();
        public sbyte[] BlueFor = Array.Empty<sbyte>();
        public FlagOwed[] Owed = Array.Empty<FlagOwed>();
    }

    /// <summary>A ruling by the host, for every screen's banners: the game's RaceControl.Event.</summary>
    public struct RulingInfo
    {
        public float TimeS;
        public byte Car, Cause, Ruling;
        public float Seconds;
        public sbyte Other;
    }

    /// <summary>
    /// The messages that have to arrive: joining, the lobby, the start. They go over TCP,
    /// each as a two byte length and then the message, whose first byte is its type. None of
    /// this is sent often, so plain BinaryWriter is used rather than bit packing.
    /// </summary>
    public static class Control
    {
        /// <summary>Bumped whenever any message here or a datagram changes meaning.</summary>
        public const byte Version = 3;

        public enum Type : byte { Hello = 1, Welcome, Reject, Lobby, Setup, Start, Ready, Go, Standings, Return, Report, Flags, Rulings }

        /// <summary>Hello, as this version says it; <paramref name="version"/> only for testing
        /// that an older one is turned away.</summary>
        public static byte[] Hello(PlayerInfo me, byte version = Version) => Build(Type.Hello, w =>
        {
            w.Write(version);
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
            w.Write(lobby.TyreWear);
            w.Write(lobby.SafetyCar);
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
            w.Write(start.TyreWear);
            w.Write(start.SafetyCar);
            WritePlayers(w, start.Grid);
        });

        /// <summary>The host is taking everyone back to the lobby, still together.</summary>
        public static byte[] Return() => Build(Type.Return, w => { });

        /// <summary>A player's word that the circuit has loaded and its car is on the grid.</summary>
        public static byte[] Ready() => Build(Type.Ready, w => { });

        /// <summary>The instant of GO, on the host's clock.</summary>
        public static byte[] Go(float atHostSeconds) => Build(Type.Go, w => w.Write(atHostSeconds));

        public static byte[] StandingsMessage(Standings standings) => Build(Type.Standings, w =>
        {
            w.Write(standings.RaceTimeS);
            w.Write((byte)standings.Cars.Length);
            foreach (Standing s in standings.Cars)
            {
                w.Write(s.LapsComplete);
                w.Write(s.ProgressM);
                w.Write(s.FinishedAtS);
                w.Write(s.BestLapS);
                w.Write(s.LastLapS);
                w.Write(s.Left);
                w.Write(s.PenaltyS);
                w.Write(s.Warnings);
                w.Write(s.Penalties);
                w.Write(s.PitStops);
                w.Write(s.Disqualified);
                w.Write(s.LapValid);
                w.Write(s.LastLapValid);
            }
        });

        public static byte[] ReportMessage(ReportKind kind, byte value) => Build(Type.Report, w =>
        {
            w.Write((byte)kind);
            w.Write(value);
        });

        public static Report ReadReport(byte[] message, byte id)
        {
            using BinaryReader r = Body(message);
            return new Report { Id = id, Kind = (ReportKind)r.ReadByte(), Value = r.ReadByte() };
        }

        public static byte[] FlagsMessage(FlagsState flags) => Build(Type.Flags, w =>
        {
            w.Write(flags.SafetyCar);
            w.Write((byte)flags.Zones.Length);
            foreach (FlagZone z in flags.Zones) { w.Write(z.Car); w.Write(z.From); w.Write(z.To); }
            w.Write((byte)flags.InYellow.Length);
            for (int i = 0; i < flags.InYellow.Length; i++) { w.Write(flags.InYellow[i]); w.Write(flags.BlueFor[i]); }
            w.Write((byte)flags.Owed.Length);
            foreach (FlagOwed o in flags.Owed) { w.Write(o.Car); w.Write(o.Passed); w.Write(o.DeadlineS); }
        });

        public static FlagsState ReadFlags(byte[] message)
        {
            using BinaryReader r = Body(message);
            var flags = new FlagsState { SafetyCar = r.ReadByte(), Zones = new FlagZone[r.ReadByte()] };
            for (int i = 0; i < flags.Zones.Length; i++)
                flags.Zones[i] = new FlagZone { Car = r.ReadByte(), From = r.ReadInt32(), To = r.ReadInt32() };
            int cars = r.ReadByte();
            flags.InYellow = new sbyte[cars];
            flags.BlueFor = new sbyte[cars];
            for (int i = 0; i < cars; i++) { flags.InYellow[i] = r.ReadSByte(); flags.BlueFor[i] = r.ReadSByte(); }
            flags.Owed = new FlagOwed[r.ReadByte()];
            for (int i = 0; i < flags.Owed.Length; i++)
                flags.Owed[i] = new FlagOwed { Car = r.ReadByte(), Passed = r.ReadByte(), DeadlineS = r.ReadSingle() };
            return flags;
        }

        public static byte[] RulingsMessage(IReadOnlyList<RulingInfo> rulings) => Build(Type.Rulings, w =>
        {
            w.Write((byte)rulings.Count);
            foreach (RulingInfo e in rulings)
            {
                w.Write(e.TimeS); w.Write(e.Car); w.Write(e.Cause); w.Write(e.Ruling); w.Write(e.Seconds); w.Write(e.Other);
            }
        });

        public static RulingInfo[] ReadRulings(byte[] message)
        {
            using BinaryReader r = Body(message);
            var rulings = new RulingInfo[r.ReadByte()];
            for (int i = 0; i < rulings.Length; i++)
                rulings[i] = new RulingInfo
                {
                    TimeS = r.ReadSingle(), Car = r.ReadByte(), Cause = r.ReadByte(), Ruling = r.ReadByte(),
                    Seconds = r.ReadSingle(), Other = r.ReadSByte(),
                };
            return rulings;
        }

        public static Standings ReadStandings(byte[] message)
        {
            using BinaryReader r = Body(message);
            var standings = new Standings { RaceTimeS = r.ReadSingle(), Cars = new Standing[r.ReadByte()] };
            for (int i = 0; i < standings.Cars.Length; i++)
                standings.Cars[i] = new Standing
                {
                    LapsComplete = r.ReadByte(), ProgressM = r.ReadSingle(), FinishedAtS = r.ReadSingle(),
                    BestLapS = r.ReadSingle(), LastLapS = r.ReadSingle(), Left = r.ReadBoolean(),
                    PenaltyS = r.ReadSingle(), Warnings = r.ReadByte(), Penalties = r.ReadByte(), PitStops = r.ReadByte(),
                    Disqualified = r.ReadBoolean(), LapValid = r.ReadBoolean(), LastLapValid = r.ReadBoolean(),
                };
            return standings;
        }

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
                TyreWear = r.ReadByte(), SafetyCar = r.ReadBoolean(),
                Players = new List<PlayerInfo>(ReadPlayers(r)),
            };
        }

        public static RaceStart ReadStart(byte[] message)
        {
            using BinaryReader r = Body(message);
            return new RaceStart
            {
                Track = ReadString(r), Laps = r.ReadByte(), TyreWear = r.ReadByte(), SafetyCar = r.ReadBoolean(),
                Grid = ReadPlayers(r),
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
