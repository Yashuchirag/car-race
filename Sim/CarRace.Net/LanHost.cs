using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace CarRace.Net
{
    /// <summary>
    /// The machine that owns a LAN race. It is a player too, with its own car, and it also
    /// drives the AI cars, keeps the lobby and says when the race starts. Everyone else only
    /// simulates their own car: the host collects those states and passes every car on to
    /// every client.
    ///
    /// Nothing here blocks or starts a thread. The game calls Poll once a frame with the
    /// time, which keeps every state change on the game's own thread and lets the harness
    /// run it on a simulated clock.
    /// </summary>
    public sealed class LanHost : IDisposable
    {
        public const int DefaultPort = 47902;
        public const int MaxPlayers = 6;
        public const int MaxCars = 8;
        public const byte FirstAiId = 32;

        /// <summary>The safety car's id in snapshots: a car the host drives, on no grid slot. The
        /// highest the snapshot's six bit ids carry; 250 arrived as 58, and matched no car.</summary>
        public const byte SafetyCarId = 63;

        /// <summary>A client heard nothing from for this long has gone, even if its TCP
        /// connection never said so, as happens when a cable is pulled.</summary>
        public const float TimeoutSeconds = 5f;

        /// <summary>From the ready signal to GO: long enough to see a 3, 2, 1.</summary>
        public const float CountdownSeconds = 3f;

        /// <summary>How long GO waits for a player still loading before going without them.
        /// They join the race when they arrive.</summary>
        public const float ReadyTimeoutSeconds = 20f;

        sealed class Client
        {
            public FrameSocket Tcp;
            public PlayerInfo Player;          // null until Hello
            public IPEndPoint Udp;             // null until its first datagram
            public float LastHeard;
            public bool HasCar;
            public bool Loaded;            // its circuit has loaded and its car is on the grid
            public CarState Car;
        }

        readonly Socket _listener;
        readonly UdpClient _udp;
        readonly DatagramInbox _inbox;
        readonly UdpClient _beacon;
        readonly List<Client> _clients = new List<Client>();
        readonly BitWriter _writer = new BitWriter(new byte[SnapshotCodec.MaxBytes(MaxCars) + 16]);
        readonly string _hostName;
        readonly uint _session = (uint)new Random().Next();
        byte _nextId = 1;
        float _nextBeacon;
        uint _tick;

        public readonly int Port;
        public readonly LobbyState Lobby = new LobbyState();

        /// <summary>The other players' cars, carried forward to the present.</summary>
        public readonly Extrapolator Cars = new Extrapolator();

        public RaceStart Race { get; private set; }
        public bool Started => Race != null;

        /// <summary>GO on the host's clock, NaN until every player is ready.</summary>
        public float GoAtHostSeconds { get; private set; } = float.NaN;
        bool _hostReady;
        float _startedAt;

        /// <summary>Set whenever someone joins, leaves or changes the lobby, for the menu to
        /// redraw from. The host clears it.</summary>
        public bool LobbyChanged;

        /// <summary>Ids of players who have left since the host last looked, whose cars it
        /// should take off the track.</summary>
        public readonly List<byte> Left = new List<byte>();

        /// <summary>What players' machines have said about their own cars since the host last
        /// looked, oldest first; the host's game takes them and clears the list.</summary>
        public readonly List<Report> Reports = new List<Report>();

        /// <summary>Why players were dropped, since the host last looked, for its log.</summary>
        public readonly List<string> Dropped = new List<string>();

        public LanHost(string hostName, PlayerInfo me, string track, int port = DefaultPort)
        {
            _hostName = hostName;
            Port = port;

            me.Id = 0;
            me.Ready = true;   // the host's ready is pressing start
            Lobby.Track = track;
            Lobby.Players.Add(me);

            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Any, port));
            _listener.Listen(8);
            _listener.Blocking = false;

            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _udp.Client.ReceiveBufferSize = 1 << 20;
            Datagram.IgnoreConnectionReset(_udp.Client);
            _inbox = new DatagramInbox(_udp);
            _beacon = new UdpClient { EnableBroadcast = true };
        }

        public int Humans => Lobby.Players.Count;

        /// <summary>Everyone who has joined has said they are ready.</summary>
        public bool AllReady => Lobby.Players.TrueForAll(p => p.Ready);

        public void SetTrack(string track) { Lobby.Track = track; SendLobby(); }
        public void SetLaps(int laps) { Lobby.Laps = (byte)Math.Clamp(laps, 1, 99); SendLobby(); }
        public void SetTyreWear(int choice) { Lobby.TyreWear = (byte)Math.Clamp(choice, 0, 255); SendLobby(); }
        public void SetSafetyCar(bool on) { Lobby.SafetyCar = on; SendLobby(); }

        /// <summary>As many as asked for, as long as the grid does not pass MaxCars.</summary>
        public void SetAiCars(int count)
        {
            Lobby.AiCars = (byte)Math.Clamp(count, 0, MaxCars - Humans);
            SendLobby();
        }

        public void SetMySetup(byte colour, byte design)
        {
            Lobby.Players[0].Colour = colour;
            Lobby.Players[0].Design = design;
            SendLobby();
        }

        public void Poll(float now)
        {
            if (!Started && now >= _nextBeacon)
            {
                _nextBeacon = now + 1f;
                Announce();
            }

            Accept(now);
            foreach (Client client in _clients.ToArray()) ReadControl(client, now);
            ReadDatagrams(now);

            foreach (Client client in _clients.ToArray())
            {
                client.Tcp.Flush();
                if (client.Tcp.Closed || now - client.LastHeard > TimeoutSeconds)
                {
                    Dropped.Add($"{client.Player?.Name ?? "a connection"}: "
                              + (client.Tcp.Closed ? client.Tcp.Why : $"nothing for {now - client.LastHeard:0.0} s"));
                    Drop(client);
                }
            }

            if (Started && float.IsNaN(GoAtHostSeconds) && _hostReady
                && (_clients.TrueForAll(c => c.Player == null || c.Loaded) || now - _startedAt > ReadyTimeoutSeconds))
            {
                GoAtHostSeconds = now + CountdownSeconds;
                byte[] go = Control.Go(GoAtHostSeconds);
                foreach (Client client in _clients)
                    if (client.Player != null) client.Tcp.Send(go);
            }
        }

        /// <summary>
        /// Ends the race and takes everyone back to the lobby, still connected: the circuit,
        /// the AI and the laps can be chosen again, everyone changes their car if they like and
        /// says ready again, and new players can join until the next start.
        /// </summary>
        public void ReturnToLobby()
        {
            if (!Started) return;
            Race = null;
            GoAtHostSeconds = float.NaN;
            _hostReady = false;
            _nextBeacon = 0f;
            foreach (Client client in _clients)
            {
                client.Loaded = false;
                client.HasCar = false;
                if (client.Player != null) client.Player.Ready = false;
            }
            foreach (byte id in new List<byte>(Cars.Ids)) Cars.Remove(id);
            Left.Clear();
            Reports.Clear();

            byte[] message = Control.Return();
            foreach (Client client in _clients)
                if (client.Player != null) client.Tcp.Send(message);
            SendLobby();
        }

        /// <summary>The race as the host keeps it, to every player.</summary>
        public void SendStandings(Standings standings) => SendAll(Control.StandingsMessage(standings));

        /// <summary>The flags and the safety car, to every player.</summary>
        public void SendFlags(FlagsState flags) => SendAll(Control.FlagsMessage(flags));

        /// <summary>New rulings, to every player, for their banners.</summary>
        public void SendRulings(IReadOnlyList<RulingInfo> rulings)
        {
            if (rulings.Count > 0) SendAll(Control.RulingsMessage(rulings));
        }

        void SendAll(byte[] message)
        {
            foreach (Client client in _clients)
                if (client.Player != null) client.Tcp.Send(message);
        }

        /// <summary>The host's own car is on the grid. GO follows once everyone's is.</summary>
        public void SetReady() => _hostReady = true;

        /// <summary>
        /// Closes the lobby and fixes the grid: people in the order they joined, then the AI.
        /// Everyone loads the circuit; GO is set once they all say they are ready.
        /// </summary>
        public RaceStart Start(float now)
        {
            _startedAt = now;
            var grid = new List<PlayerInfo>(Lobby.Players);
            for (int i = 0; i < Lobby.AiCars; i++)
                grid.Add(new PlayerInfo { Id = (byte)(FirstAiId + i), Name = $"AI {i + 1}", Ai = true, Colour = 255 });

            Race = new RaceStart
            {
                Track = Lobby.Track, Laps = Lobby.Laps, TyreWear = Lobby.TyreWear, SafetyCar = Lobby.SafetyCar,
                Grid = grid.ToArray(),
            };
            byte[] message = Control.Start(Race);
            foreach (Client client in _clients)
                if (client.Player != null) client.Tcp.Send(message);
            return Race;
        }

        /// <summary>
        /// Sends every car to every client: the ones the host simulates, stamped by the host
        /// with the time their state was true, and each client's latest, with the time it
        /// was true, so that every machine can carry them all forward to the same present.
        /// </summary>
        public int SendSnapshot(IReadOnlyList<CarState> hostCars, float now)
        {
            var cars = new List<CarState>(hostCars.Count + _clients.Count);
            cars.AddRange(hostCars);
            foreach (Client client in _clients)
                if (client.HasCar) cars.Add(client.Car);

            var snapshot = new Snapshot { Tick = _tick++, TimeSeconds = now, Cars = cars.ToArray() };
            byte[] packet = Datagram.Pack(Datagram.Kind.Snapshot, snapshot, _writer);
            foreach (Client client in _clients)
                if (client.Udp != null) Send(packet, client.Udp);
            return packet.Length;
        }

        void Announce()
        {
            byte[] bytes = new Beacon
            {
                Session = _session, HostName = _hostName, Track = Lobby.Track, GamePort = (ushort)Port,
                Players = (byte)Humans, Capacity = MaxPlayers,
            }.ToBytes();

            // Loopback as well as broadcast, so a second copy of the game on this machine
            // finds it wherever the broadcast is filtered.
            foreach (IPAddress to in new[] { IPAddress.Broadcast, IPAddress.Loopback })
            {
                try { _beacon.Send(bytes, bytes.Length, new IPEndPoint(to, Beacon.Port)); }
                catch (SocketException) { }
            }
        }

        void Accept(float now)
        {
            while (true)
            {
                Socket socket;
                try { socket = _listener.Accept(); }
                catch (SocketException) { return; }   // nobody waiting
                socket.NoDelay = true;
                _clients.Add(new Client { Tcp = new FrameSocket(socket), LastHeard = now });
            }
        }

        void ReadControl(Client client, float now)
        {
            while (client.Tcp.TryReceive(out byte[] message))
            {
                client.LastHeard = now;
                try { Handle(client, message); }
                catch (Exception e)   // malformed: not one of ours
                {
                    Dropped.Add($"{client.Player?.Name ?? "a connection"}: a {Control.TypeOf(message)} message did not read: {e.Message}");
                    client.Tcp.Dispose();
                    return;
                }
            }
        }

        void Handle(Client client, byte[] message)
        {
            switch (Control.TypeOf(message))
            {
                case Control.Type.Hello when client.Player == null:
                {
                    PlayerInfo player = Control.ReadHello(message, out byte version);
                    RejectReason? refuse = version != Control.Version ? RejectReason.Version
                                         : Started ? RejectReason.Started
                                         : Humans >= MaxPlayers ? RejectReason.Full
                                         : (RejectReason?)null;
                    if (refuse != null)
                    {
                        client.Tcp.Send(Control.Reject(refuse.Value));
                        client.Tcp.Flush();
                        client.Tcp.Dispose();
                        return;
                    }

                    player.Id = _nextId++;
                    client.Player = player;
                    Lobby.Players.Add(player);
                    if (Humans + Lobby.AiCars > MaxCars) Lobby.AiCars = (byte)(MaxCars - Humans);
                    client.Tcp.Send(Control.Welcome(player.Id));
                    SendLobby();
                    break;
                }
                case Control.Type.Ready when client.Player != null && Started:
                    client.Loaded = true;
                    break;
                case Control.Type.Report when client.Player != null && Started:
                    Reports.Add(Control.ReadReport(message, client.Player.Id));
                    break;
                case Control.Type.Setup when client.Player != null && !Started:
                {
                    using var r = Control.Body(message);
                    client.Player.Colour = r.ReadByte();
                    client.Player.Design = r.ReadByte();
                    client.Player.Ready = r.ReadBoolean();
                    SendLobby();
                    break;
                }
            }
        }

        void ReadDatagrams(float now)
        {
            while (_inbox.TryTake(now, out byte[] data, out IPEndPoint from, out float arrived))
            {
                if (data.Length < 2) continue;

                try
                {
                    switch ((Datagram.Kind)data[0])
                    {
                        case Datagram.Kind.Ping:
                        {
                            Client client = Known(data[1], from);
                            if (client == null) break;
                            client.Udp = from;
                            client.LastHeard = now;
                            Send(Datagram.Pong(BitConverter.ToSingle(data, 2), arrived, now), from);
                            break;
                        }
                        case Datagram.Kind.Car:
                        {
                            Snapshot snapshot = Datagram.Unpack(data, data.Length);
                            if (snapshot.Cars.Length != 1) break;
                            CarState car = snapshot.Cars[0];
                            Client client = Known(car.Id, from);
                            if (client == null || !Started) break;
                            client.Udp = from;
                            client.LastHeard = now;
                            if (client.HasCar && car.TimeSeconds <= client.Car.TimeSeconds) break;
                            client.Car = car;
                            client.HasCar = true;
                            Cars.Add(car, now);
                            break;
                        }
                    }
                }
                catch (Exception) { }   // a short or garbled datagram: drop it
            }
        }

        /// <summary>The client this id belongs to, if the datagram came from its machine.
        /// Anything else on the port is ignored.</summary>
        Client Known(byte id, IPEndPoint from)
        {
            foreach (Client client in _clients)
            {
                if (client.Player == null || client.Player.Id != id) continue;
                var tcp = (IPEndPoint)client.Tcp.Socket.RemoteEndPoint;
                return tcp.Address.Equals(from.Address) ? client : null;
            }
            return null;
        }

        void Drop(Client client)
        {
            _clients.Remove(client);
            client.Tcp.Dispose();
            if (client.Player == null) return;

            Lobby.Players.Remove(client.Player);
            Cars.Remove(client.Player.Id);
            Left.Add(client.Player.Id);
            SendLobby();
        }

        void SendLobby()
        {
            LobbyChanged = true;
            byte[] message = Control.Lobby(Lobby);
            foreach (Client client in _clients)
                if (client.Player != null) client.Tcp.Send(message);
        }

        void Send(byte[] packet, IPEndPoint to)
        {
            try { _udp.Send(packet, packet.Length, to); }
            catch (SocketException) { }
        }

        public void Dispose()
        {
            foreach (Client client in _clients) client.Tcp.Dispose();
            _clients.Clear();
            _listener.Close();
            _inbox.Dispose();
            _beacon.Dispose();
        }
    }
}
