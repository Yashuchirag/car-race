using System;
using System.Net;
using System.Net.Sockets;

namespace CarRace.Net
{
    /// <summary>
    /// A player who joined someone else's race. It simulates its own car and sends it to the
    /// host; from the host it gets the lobby, the start, and every other car.
    ///
    /// All times given to it are its own clock; HostNow turns one into the host's, which is
    /// the clock every car state and the start are stamped in. Like LanHost it never blocks,
    /// and it is driven by Poll once a frame.
    /// </summary>
    public sealed class LanClient : IDisposable
    {
        public enum Phase { Connecting, Lobby, Racing, Rejected, Closed }

        const float ConnectTimeoutSeconds = 5f;
        const float PingInterval = 0.5f;

        readonly FrameSocket _tcp;
        readonly UdpClient _udp;
        readonly DatagramInbox _inbox;
        readonly IPEndPoint _hostUdp;
        readonly PlayerInfo _me;
        readonly BitWriter _writer = new BitWriter(new byte[SnapshotCodec.MaxBytes(1) + 16]);
        float _startedConnecting = float.NaN;
        float _lastHeard;
        float _nextPing = float.NegativeInfinity;
        uint _sent;
        uint _newestTick;
        bool _anySnapshot;

        // Clock sync: of the recent pings, the one with the shortest round trip gives the
        // best offset, because its reply spent the least time queued somewhere.
        readonly (float Rtt, float Offset)[] _samples = new (float, float)[16];
        int _sampleCount, _sampleAt;

        public Phase State { get; private set; } = Phase.Connecting;
        public RejectReason Rejected { get; private set; }
        public byte Id { get; private set; }
        public LobbyState Lobby { get; private set; }
        public RaceStart Race { get; private set; }

        /// <summary>The race as the host last sent it, null before the first.</summary>
        public Standings Standings { get; private set; }

        /// <summary>GO on the host's clock, NaN until the host has it.</summary>
        public float GoAtHostSeconds { get; private set; } = float.NaN;

        /// <summary>Every other car, carried forward to the present.</summary>
        public readonly Extrapolator Cars = new Extrapolator();

        public bool Synced => _sampleCount > 0;
        public float OffsetSeconds { get; private set; }
        public float RoundTripSeconds { get; private set; }

        /// <summary>How far apart the offsets of the recent pings are: how well the clock is
        /// known. A millisecond or two on a LAN.</summary>
        public float OffsetSpreadSeconds { get; private set; }
        public float HostNow(float now) => now + OffsetSeconds;

        /// <summary>Set when the lobby or the start changes, for the menu to redraw from.</summary>
        public bool Changed;

        /// <param name="udpPort">Where to send datagrams, if not the host's port: the harness
        /// points it at a proxy that delays and drops them.</param>
        public LanClient(IPAddress host, int port, PlayerInfo me, int udpPort = 0)
        {
            _me = me;
            _hostUdp = new IPEndPoint(host, udpPort != 0 ? udpPort : port);

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false, NoDelay = true };
            try { socket.Connect(host, port); }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock
                                            || e.SocketErrorCode == SocketError.InProgress) { }
            _tcp = new FrameSocket(socket);

            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            _udp.Client.ReceiveBufferSize = 1 << 20;
            Datagram.IgnoreConnectionReset(_udp.Client);
            _inbox = new DatagramInbox(_udp);
        }

        public void Poll(float now)
        {
            if (State == Phase.Rejected || State == Phase.Closed) return;
            if (float.IsNaN(_startedConnecting)) _startedConnecting = now;

            if (State == Phase.Connecting && !Connected(now)) return;

            while (_tcp.TryReceive(out byte[] message))
            {
                _lastHeard = now;
                try { Handle(message); }
                catch (Exception) { Close(); return; }
            }
            _tcp.Flush();

            if (Id != 0)
            {
                if (now >= _nextPing)
                {
                    _nextPing = now + PingInterval;
                    Send(Datagram.Ping(Id, now));
                }
                ReadDatagrams(now);
            }

            if (_tcp.Closed || now - _lastHeard > LanHost.TimeoutSeconds) Close();
        }

        public void SetSetup(byte colour, byte design)
        {
            _me.Colour = colour;
            _me.Design = design;
            if (Id != 0) _tcp.Send(Control.Setup(colour, design, _me.Ready));
        }

        /// <summary>Ready in the lobby, or not: the host starts once everyone is.</summary>
        public void SetReady(bool ready)
        {
            _me.Ready = ready;
            if (Id != 0) _tcp.Send(Control.Setup(_me.Colour, _me.Design, ready));
        }

        public bool Ready => _me.Ready;

        /// <summary>This player's car is on the grid.</summary>
        public void SendReady()
        {
            if (State == Phase.Racing) _tcp.Send(Control.Ready());
        }

        /// <summary>Sends this player's car, as it was at stateTime on this machine's clock.</summary>
        public void SendCar(CarState car, float stateTime)
        {
            if (State != Phase.Racing || !Synced) return;
            car.Id = Id;
            car.TimeSeconds = HostNow(stateTime);
            var snapshot = new Snapshot { Tick = _sent++, TimeSeconds = car.TimeSeconds, Cars = new[] { car } };
            Send(Datagram.Pack(Datagram.Kind.Car, snapshot, _writer));
        }

        bool Connected(float now)
        {
            if (_tcp.Socket.Poll(0, SelectMode.SelectError))
            {
                Close();
                return false;
            }
            if (!_tcp.Socket.Poll(0, SelectMode.SelectWrite))
            {
                if (now - _startedConnecting > ConnectTimeoutSeconds) Close();
                return false;
            }

            _lastHeard = now;
            _tcp.Send(Control.Hello(_me));
            State = Phase.Lobby;
            return true;
        }

        void Handle(byte[] message)
        {
            switch (Control.TypeOf(message))
            {
                case Control.Type.Welcome:
                    Id = message[1];
                    _me.Id = Id;
                    break;
                case Control.Type.Reject:
                    Rejected = (RejectReason)message[1];
                    State = Phase.Rejected;
                    Changed = true;
                    _tcp.Dispose();
                    break;
                case Control.Type.Lobby:
                    Lobby = Control.ReadLobby(message);
                    Changed = true;
                    break;
                case Control.Type.Start:
                    Race = Control.ReadStart(message);
                    State = Phase.Racing;
                    Changed = true;
                    break;
                case Control.Type.Go:
                    GoAtHostSeconds = Control.ReadGo(message);
                    break;
                case Control.Type.Standings:
                    Standings = Control.ReadStandings(message);
                    break;
                case Control.Type.Return:
                    // Back to the lobby, still together: everyone says ready again.
                    Race = null;
                    GoAtHostSeconds = float.NaN;
                    Standings = null;
                    foreach (byte id in new System.Collections.Generic.List<byte>(Cars.Ids)) Cars.Remove(id);
                    _me.Ready = false;
                    State = Phase.Lobby;
                    Changed = true;
                    break;
            }
        }

        void ReadDatagrams(float now)
        {
            while (_inbox.TryTake(now, out byte[] data, out IPEndPoint from, out float arrived))
            {
                if (!from.Address.Equals(_hostUdp.Address) || data.Length < 2) continue;

                try
                {
                    switch ((Datagram.Kind)data[0])
                    {
                        case Datagram.Kind.Pong when data.Length >= 13:
                            _lastHeard = now;
                            Synchronise(BitConverter.ToSingle(data, 1), BitConverter.ToSingle(data, 5),
                                        BitConverter.ToSingle(data, 9), arrived);
                            break;
                        case Datagram.Kind.Snapshot:
                            Snapshot snapshot = Datagram.Unpack(data, data.Length);
                            _lastHeard = now;
                            if (_anySnapshot && snapshot.Tick <= _newestTick) break;   // late: a newer one is in
                            _anySnapshot = true;
                            _newestTick = snapshot.Tick;
                            Take(snapshot, HostNow(now));
                            break;
                    }
                }
                catch (Exception) { }
            }
        }

        /// <summary>The snapshot lists every car still racing. One that is missing has left.</summary>
        void Take(Snapshot snapshot, float hostNow)
        {
            foreach (CarState car in snapshot.Cars)
                if (car.Id != Id) Cars.Add(car, hostNow);

            foreach (byte id in new System.Collections.Generic.List<byte>(Cars.Ids))
                if (Array.FindIndex(snapshot.Cars, c => c.Id == id) < 0) Cars.Remove(id);
        }

        /// <summary>
        /// NTP's arithmetic. The round trip is the time away less the time the host held the
        /// ping; the offset assumes the rest was split evenly between the two directions,
        /// which on a LAN is true to well under a millisecond.
        /// </summary>
        void Synchronise(float sent, float hostArrived, float hostSent, float arrived)
        {
            float rtt = (arrived - sent) - (hostSent - hostArrived);
            if (rtt < 0f || rtt > 2f) return;

            _samples[_sampleAt] = (rtt, ((hostArrived - sent) + (hostSent - arrived)) * 0.5f);
            _sampleAt = (_sampleAt + 1) % _samples.Length;
            if (_sampleCount < _samples.Length) _sampleCount++;

            int best = 0;
            for (int i = 1; i < _sampleCount; i++)
                if (_samples[i].Rtt < _samples[best].Rtt) best = i;
            RoundTripSeconds = _samples[best].Rtt;
            OffsetSeconds = _samples[best].Offset;

            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < _sampleCount; i++)
            {
                low = Math.Min(low, _samples[i].Offset);
                high = Math.Max(high, _samples[i].Offset);
            }
            OffsetSpreadSeconds = high - low;
        }

        void Send(byte[] packet)
        {
            try { _udp.Send(packet, packet.Length, _hostUdp); }
            catch (SocketException) { }
        }

        void Close()
        {
            if (State != Phase.Rejected) State = Phase.Closed;
            Changed = true;
            _tcp.Dispose();
        }

        public void Dispose()
        {
            _tcp.Dispose();
            _inbox.Dispose();
        }
    }
}
