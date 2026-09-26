using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace CarRace.Net
{
    /// <summary>
    /// What goes over UDP: car states, which are worthless once a newer one exists, and the
    /// pings that keep a client's clock on the host's. Each datagram starts with its kind.
    /// </summary>
    public static class Datagram
    {
        public enum Kind : byte { Car = 1, Snapshot = 2, Ping = 3, Pong = 4 }

        public const int MaxBytes = 1200;   // well under any LAN's MTU

        /// <summary>A snapshot, or a client's one car sent as a snapshot of one.</summary>
        public static byte[] Pack(Kind kind, Snapshot snapshot, BitWriter writer)
        {
            int size = SnapshotCodec.Write(writer, snapshot);
            var packet = new byte[size + 1];
            packet[0] = (byte)kind;
            Array.Copy(writer.GetBuffer(), 0, packet, 1, size);
            return packet;
        }

        /// <summary>Throws on a short or malformed packet; callers drop what throws.</summary>
        public static Snapshot Unpack(byte[] packet, int length)
        {
            var body = new byte[length - 1];
            Array.Copy(packet, 1, body, 0, body.Length);
            return SnapshotCodec.Read(new BitReader(body));
        }

        public static byte[] Ping(byte id, float clientTime)
        {
            var packet = new byte[6];
            packet[0] = (byte)Kind.Ping;
            packet[1] = id;
            BitConverter.GetBytes(clientTime).CopyTo(packet, 2);
            return packet;
        }

        public static byte[] Pong(float clientTime, float hostTime)
        {
            var packet = new byte[9];
            packet[0] = (byte)Kind.Pong;
            BitConverter.GetBytes(clientTime).CopyTo(packet, 1);
            BitConverter.GetBytes(hostTime).CopyTo(packet, 5);
            return packet;
        }
    }

    /// <summary>
    /// A TCP connection carrying whole messages, each behind a two byte length, without ever
    /// blocking: the game polls it once a frame, and a frame cannot wait on the network.
    /// </summary>
    sealed class FrameSocket : IDisposable
    {
        const int MaxMessage = 4096;

        readonly Socket _socket;
        byte[] _in = new byte[MaxMessage + 2];
        int _inCount;
        readonly List<byte> _out = new List<byte>();

        public bool Closed { get; private set; }
        public Socket Socket => _socket;

        public FrameSocket(Socket socket)
        {
            _socket = socket;
            _socket.Blocking = false;
            _socket.NoDelay = true;
        }

        public void Send(byte[] message)
        {
            if (Closed) return;
            _out.Add((byte)(message.Length & 0xFF));
            _out.Add((byte)(message.Length >> 8));
            _out.AddRange(message);
            Flush();
        }

        public void Flush()
        {
            if (Closed || _out.Count == 0) return;
            try
            {
                int sent = _socket.Send(_out.ToArray());
                _out.RemoveRange(0, sent);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { }
            catch (Exception) { Closed = true; }
        }

        /// <summary>The next whole message, if one has arrived. Marks the socket closed when
        /// the other end has gone or has sent something no game of ours would.</summary>
        public bool TryReceive(out byte[] message)
        {
            message = null;
            if (Closed) return false;

            try
            {
                if (_socket.Available > 0)
                {
                    int read = _socket.Receive(_in, _inCount, _in.Length - _inCount, SocketFlags.None);
                    _inCount += read;
                }
                else if (_socket.Poll(0, SelectMode.SelectRead))
                {
                    Closed = true;   // readable with nothing to read: the other end closed
                    return false;
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { }
            catch (Exception) { Closed = true; return false; }

            if (_inCount < 2) return false;
            int length = _in[0] | (_in[1] << 8);
            if (length == 0 || length > MaxMessage) { Closed = true; return false; }
            if (_inCount < 2 + length) return false;

            message = new byte[length];
            Array.Copy(_in, 2, message, 0, length);
            Array.Copy(_in, 2 + length, _in, 0, _inCount - 2 - length);
            _inCount -= 2 + length;
            return true;
        }

        public void Dispose()
        {
            Closed = true;
            try { _socket.Shutdown(SocketShutdown.Both); } catch (Exception) { }
            _socket.Close();
        }
    }
}
