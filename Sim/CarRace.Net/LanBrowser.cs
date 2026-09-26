using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace CarRace.Net
{
    /// <summary>
    /// Listens for hosts' beacons and keeps the list the Join screen shows. A host that has
    /// not been heard for a few seconds has started its race or gone, and drops off.
    /// </summary>
    public sealed class LanBrowser : IDisposable
    {
        public sealed class Game
        {
            public IPAddress Address;
            public Beacon Beacon;
            public float LastHeard;
        }

        const float ForgetSeconds = 3.5f;

        readonly UdpClient _udp;
        readonly List<Game> _games = new List<Game>();

        public IReadOnlyList<Game> Games => _games;

        public LanBrowser()
        {
            // Shared, so a second copy of the game on the same machine can listen too.
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Beacon.Port));
        }

        public void Poll(float now)
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_udp.Available > 0)
            {
                byte[] data;
                try { data = _udp.Receive(ref from); }
                catch (SocketException) { continue; }
                if (!Beacon.TryParse(data, out Beacon beacon)) continue;

                Game game = _games.Find(g => g.Address.Equals(from.Address) && g.Beacon.GamePort == beacon.GamePort);
                if (game == null) _games.Add(game = new Game { Address = from.Address });
                game.Beacon = beacon;
                game.LastHeard = now;
            }

            _games.RemoveAll(g => now - g.LastHeard > ForgetSeconds);
        }

        public void Dispose() => _udp.Dispose();
    }
}
