using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;

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

        /// <summary>
        /// On Windows, a datagram sent to a port nobody holds any more comes back as an ICMP
        /// error, and the socket's next receive throws a connection reset, for every player
        /// on the host's one socket. Turning that report off is the usual fix; elsewhere the
        /// error never reaches the socket and there is nothing to do.
        /// </summary>
        public static void IgnoreConnectionReset(Socket socket)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
            try { socket.IOControl(SIO_UDP_CONNRESET, new byte[4], null); }
            catch (Exception) { }
        }

        /// <summary>The reply to a ping: when the ping was sent on the client's clock, and
        /// when it arrived and when this reply left on the host's. The gap between those two
        /// is the host's own delay, which the client takes out of the round trip.</summary>
        public static byte[] Pong(float clientSent, float hostArrived, float hostSent)
        {
            var packet = new byte[13];
            packet[0] = (byte)Kind.Pong;
            BitConverter.GetBytes(clientSent).CopyTo(packet, 1);
            BitConverter.GetBytes(hostArrived).CopyTo(packet, 5);
            BitConverter.GetBytes(hostSent).CopyTo(packet, 9);
            return packet;
        }
    }

    /// <summary>
    /// Receives datagrams on a thread of its own, noting the moment each one arrives.
    ///
    /// The game reads the network once a frame, so a packet can wait up to a frame before
    /// anyone looks at it. For car states that does not matter, they carry their own time.
    /// For the clock sync it does: a ping answered a frame late looks like a slow network,
    /// and half of that wait lands in the clock's offset, 8 ms at 60 fps, half a metre at
    /// racing speed. With the arrival time noted here, the wait can be taken out.
    /// Nothing else happens on this thread; everything it receives is handed over in Poll.
    /// </summary>
    sealed class DatagramInbox : IDisposable
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        readonly UdpClient _udp;
        readonly ConcurrentQueue<(byte[] Data, IPEndPoint From, long Ticks)> _queue
            = new ConcurrentQueue<(byte[], IPEndPoint, long)>();
        volatile bool _closed;

        public DatagramInbox(UdpClient udp)
        {
            _udp = udp;
            new Thread(Run) { IsBackground = true, Name = "LAN receive" }.Start();
        }

        void Run()
        {
            while (!_closed)
            {
                try
                {
                    // Waits a millisecond at a time and reads only what has arrived, rather
                    // than blocking in Receive, so the game's thread can send on the same
                    // socket without ever meeting a call that is parked inside it.
                    if (!_udp.Client.Poll(1000, SelectMode.SelectRead) || _udp.Available == 0) continue;
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref from);
                    _queue.Enqueue((data, from, Clock.ElapsedTicks));
                }
                catch (SocketException) { if (_closed) return; }   // a reset from a player who left
                catch (ObjectDisposedException) { return; }
            }
        }

        /// <summary>The next datagram, and when it arrived on the caller's clock, whose time
        /// is now.</summary>
        public bool TryTake(float now, out byte[] data, out IPEndPoint from, out float arrived)
        {
            if (!_queue.TryDequeue(out var item))
            {
                data = null; from = null; arrived = now;
                return false;
            }
            data = item.Data;
            from = item.From;
            arrived = now - (float)((Clock.ElapsedTicks - item.Ticks) / (double)Stopwatch.Frequency);
            return true;
        }

        public void Dispose()
        {
            _closed = true;
            _udp.Dispose();
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

        /// <summary>Why it closed, for the logs.</summary>
        public string Why { get; private set; }

        void Close(string why)
        {
            Closed = true;
            Why ??= why;
        }

        public FrameSocket(Socket socket)
        {
            _socket = socket;
            _socket.Blocking = false;   // NoDelay is set by whoever made it: Windows refuses it mid-connect
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
            catch (Exception e) { Close($"sending failed: {e.Message}"); }
        }

        /// <summary>The next whole message, if one has arrived. Marks the socket closed when
        /// the other end has gone or has sent something no game of ours would.</summary>
        public bool TryReceive(out byte[] message)
        {
            message = null;
            if (Closed) return false;

            try
            {
                // Readable with nothing to read is the other end closing; but data can arrive
                // between asking what is available and asking whether the socket is readable, so
                // what is available is asked again after. Taken for a close, that race dropped
                // every player of a LAN race once the host sent flags ten times a second.
                if (_socket.Available == 0 && _socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                {
                    Close("the other end closed");
                    return false;
                }
                if (_socket.Available > 0 && _inCount < _in.Length)
                {
                    int read = _socket.Receive(_in, _inCount, _in.Length - _inCount, SocketFlags.None);
                    if (read == 0) { Close("the other end closed (read nothing)"); return false; }
                    _inCount += read;
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { }
            catch (Exception e) { Close($"receiving failed: {e.Message}"); return false; }

            if (_inCount < 2) return false;
            int length = _in[0] | (_in[1] << 8);
            if (length == 0 || length > MaxMessage) { Close($"a message of {length} bytes, over {MaxMessage}"); return false; }
            if (_inCount < 2 + length) return false;

            message = new byte[length];
            Array.Copy(_in, 2, message, 0, length);
            Array.Copy(_in, 2 + length, _in, 0, _inCount - 2 - length);
            _inCount -= 2 + length;
            return true;
        }

        public void Dispose()
        {
            Close("closed here");
            try { _socket.Shutdown(SocketShutdown.Both); } catch (Exception) { }
            _socket.Close();
        }
    }
}
