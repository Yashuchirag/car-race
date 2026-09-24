using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using CarRace.Net;
using CarRace.Track;
using CarRace.Vehicle;

namespace CarRace.Harness
{
    /// <summary>
    /// Runs a host simulating a field of cars and a client watching it, over real UDP
    /// sockets on the loopback interface, and measures what the client actually sees.
    ///
    /// The point is the number at the end: how far the car a player sees is from where that
    /// car really is on the host. Everything else about multiplayer follows from that being
    /// small. Latency, jitter and packet loss are added deliberately, because a LAN on a
    /// good day hides every mistake in this code and a LAN with a cheap switch does not.
    ///
    /// Only the transport and the timing are exercised here. Input from a remote player,
    /// prediction of the local car, and what happens when two cars want the same piece of
    /// road are all still to come.
    /// </summary>
    public static class NetRun
    {
        const float Dt = 1f / Rig.SubstepHz;

        public static int Run(CarConfig config, string circuit, int cars, float seconds,
                              int snapshotHz, float latencyMs, float jitterMs, float lossPct,
                              bool verbose)
        {
            TrackData track;
            try
            {
                track = TrackLoader.Load(circuit);
            }
            catch (Exception error)
            {
                Console.WriteLine($"  {error.Message}");
                return 2;
            }

            if (!Discovery(track.Name, cars)) return 1;

            SpeedPlan.Limits limits = LapRun.PlanningLimits(config);
            var drivers = new RaceDriver[cars];
            var rigs = new Rig[cars];

            for (int i = 0; i < cars; i++)
            {
                float pace = 0.84f - 0.04f * (cars > 1 ? (float)i / (cars - 1) : 0f);
                drivers[i] = new RaceDriver($"AI {i + 1:00}", track, config, limits, pace);
                rigs[i] = new Rig(config);
                RaceRun.PlaceOnGrid(rigs[i], drivers[i], track, i);
            }

            using var client = new UdpClient(0);
            using var host = new UdpClient(0);
            var clientEndPoint = (IPEndPoint)client.Client.LocalEndPoint;
            var from = new IPEndPoint(IPAddress.Any, 0);
            client.Client.ReceiveBufferSize = 1 << 20;

            var writer = new BitWriter(new byte[SnapshotCodec.MaxBytes(cars) + 64]);
            var interpolator = new Interpolator { DelaySeconds = 2f / snapshotHz };
            var random = new Random(7);
            var inFlight = new List<(float Due, byte[] Data)>();
            var truth = new List<(float Time, Vector3[] Position, Quaternion[] Rotation)>();
            var field = new RaceDriver.Seen[cars];

            Console.WriteLine($"=== LAN sync: {track.Name} ===");
            Console.WriteLine($"  {cars} cars, {seconds:0} s, snapshots at {snapshotHz} Hz, "
                            + $"client renders {interpolator.DelaySeconds * 1000f:0} ms late");
            Console.WriteLine($"  network: {latencyMs:0} ms latency, {jitterMs:0} ms jitter, "
                            + $"{lossPct:0.0}% loss, real sockets on loopback\n");

            float time = 0f;
            uint tick = 0;
            int step = 0;
            float nextSnapshot = 0f, nextSample = 0f;
            int sent = 0, delivered = 0, dropped = 0, bytesSent = 0, dryFrames = 0, samples = 0;
            int frames = 0;
            double lagSum = 0f;
            float worstLag = 0f;
            double positionErrorSum = 0f, headingErrorSum = 0f;
            float worstPosition = 0f, worstHeading = 0f;
            var positionErrors = new List<float>();

            while (time < seconds)
            {
                if (step % 10 == 0)
                {
                    for (int i = 0; i < cars; i++)
                        field[i] = new RaceDriver.Seen
                        {
                            Index = drivers[i].Path.Index,
                            LateralM = drivers[i].Path.LateralFromLineM,
                            SpeedMs = Vector3.Dot(rigs[i].Body.State.Velocity,
                                                  rigs[i].Body.State.Forward),
                            Plan = drivers[i].Path.Plan,
                            Position = rigs[i].Body.State.Position,
                        };
                    for (int i = 0; i < cars; i++) drivers[i].Observe(track, field, i, 10 * Dt);
                }

                for (int i = 0; i < cars; i++) rigs[i].Step(drivers[i].Drive(rigs[i].Body.State, Dt));
                time += Dt;
                step++;

                RecordTruth(truth, rigs, time);

                if (time >= nextSnapshot)
                {
                    nextSnapshot += 1f / snapshotHz;
                    var snapshot = Capture(rigs, drivers, tick++, time);
                    int size = SnapshotCodec.Write(writer, snapshot);
                    sent++;

                    if (random.NextDouble() * 100.0 < lossPct) dropped++;
                    else
                    {
                        var packet = new byte[size];
                        Array.Copy(writer.GetBuffer(), packet, size);
                        float jitter = (float)(random.NextDouble() * 2.0 - 1.0) * jitterMs;
                        inFlight.Add((time + (latencyMs + jitter) / 1000f, packet));
                        bytesSent += size;
                    }
                }

                for (int i = inFlight.Count - 1; i >= 0; i--)
                {
                    if (inFlight[i].Due > time) continue;
                    host.Send(inFlight[i].Data, inFlight[i].Data.Length, clientEndPoint);
                    inFlight.RemoveAt(i);
                }

                while (client.Available > 0)
                {
                    byte[] data = client.Receive(ref from);
                    interpolator.Add(SnapshotCodec.Read(new BitReader(data)));
                    delivered++;
                }

                // The client renders on its own clock, 60 times a second, from whatever has
                // arrived. It has no access to the host's clock except through the snapshots,
                // so its idea of "now" is the newest one it holds, less the render delay.
                if (time < nextSample || interpolator.Held < 2) continue;
                nextSample += 1f / 60f;

                // A client that has just joined holds a few snapshots spanning less than the
                // delay it renders at, and has to wait rather than draw. That is buffering,
                // not the buffer running dry, and counting it as the latter makes a healthy
                // connection look like a broken one for its first tenth of a second.
                if (interpolator.NewestTime - interpolator.OldestTime < interpolator.DelaySeconds)
                    continue;

                float hostTime = interpolator.NewestTime - interpolator.DelaySeconds;
                if (hostTime < interpolator.OldestTime) { dryFrames++; continue; }
                if (!TruthAt(truth, hostTime, out Vector3[] realPosition, out Quaternion[] realRotation))
                    continue;

                // How stale the view is, which is what latency actually costs. The errors
                // below are measured against where the cars were at the moment being drawn,
                // so they barely move when latency rises: interpolation does not become less
                // accurate on a slow link, it becomes further behind. Reporting only the
                // error would make a 200 ms connection look identical to a 20 ms one.
                float lag = time - hostTime;
                lagSum += lag;
                if (lag > worstLag) worstLag = lag;
                frames++;

                for (byte id = 0; id < cars; id++)
                {
                    if (!interpolator.Sample(id, hostTime, out CarState seen)) continue;

                    float positionError = Vector3.Distance(seen.Position, realPosition[id]);
                    float headingError = AngleBetween(seen.Orientation, realRotation[id]);

                    positionErrorSum += positionError;
                    headingErrorSum += headingError;
                    positionErrors.Add(positionError);
                    if (positionError > worstPosition) worstPosition = positionError;
                    if (headingError > worstHeading) worstHeading = headingError;
                    samples++;
                }
            }

            if (samples == 0)
            {
                Console.WriteLine("  nothing was measured, which is a bug in the test.");
                return 2;
            }

            positionErrors.Sort();
            float p99 = positionErrors[(int)(positionErrors.Count * 0.99f)];
            float perSecond = bytesSent / MathF.Max(time, 0.001f);

            Console.WriteLine($"  packets    {sent} sent, {delivered} arrived, {dropped} lost "
                            + $"({100f * dropped / MathF.Max(sent, 1):0.0}%)");
            Console.WriteLine($"  bandwidth  {bytesSent / MathF.Max(sent - dropped, 1)} bytes a packet, "
                            + $"{perSecond / 1024f:0.0} kB/s to each client, "
                            + $"{(float)bytesSent / MathF.Max(sent - dropped, 1) / cars:0.0} bytes a car");
            Console.WriteLine($"  position   mean {positionErrorSum / samples:0.000} m, "
                            + $"p99 {p99:0.000} m, worst {worstPosition:0.000} m");
            Console.WriteLine($"  heading    mean {headingErrorSum / samples:0.00} deg, "
                            + $"worst {worstHeading:0.00} deg");
            Console.WriteLine($"  view lag   mean {lagSum / MathF.Max(frames, 1) * 1000f:0} ms, "
                            + $"worst {worstLag * 1000f:0} ms behind the host");
            Console.WriteLine($"  buffer     ran dry on {dryFrames} of "
                            + $"{dryFrames + frames} client frames");
            Console.WriteLine($"  a host with four clients sends {perSecond * 4f / 1024f:0.0} kB/s");

            bool ok = worstPosition < 0.5f && worstHeading < 5f && dryFrames == 0;
            Console.WriteLine(ok
                ? "\n  PASS: the client's view stays within half a metre of the host's."
                : "\n  FAIL: the client's view drifts too far from the host's.");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Checks that a host's beacon reaches a listener. Broadcast is what finds a host on
        /// a real LAN with nothing typed in; loopback is what can be proved here, on a
        /// machine with one interface and no second computer on it.
        /// </summary>
        static bool Discovery(string track, int cars)
        {
            try
            {
                using var listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket,
                                                SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, Beacon.Port));

                using var announcer = new UdpClient { EnableBroadcast = true };
                var beacon = new Beacon
                {
                    HostName = Environment.MachineName, Track = track,
                    GamePort = 47902, Players = 1, Capacity = (byte)cars,
                };
                byte[] bytes = beacon.ToBytes();

                announcer.Send(bytes, bytes.Length,
                               new IPEndPoint(IPAddress.Loopback, Beacon.Port));
                try
                {
                    announcer.Send(bytes, bytes.Length,
                                   new IPEndPoint(IPAddress.Broadcast, Beacon.Port));
                }
                catch (SocketException)
                {
                    // Broadcast can be refused in a container or a restricted network. The
                    // loopback send is what this test depends on.
                }

                listener.Client.ReceiveTimeout = 2000;
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] received = listener.Receive(ref from);

                if (!Beacon.TryParse(received, out Beacon found))
                {
                    Console.WriteLine("  discovery: a packet arrived but did not parse.");
                    return false;
                }

                Console.WriteLine($"  discovery: found \"{found.HostName}\" on {found.Track}, "
                                + $"port {found.GamePort}, {found.Players}/{found.Capacity}, "
                                + $"{received.Length} byte beacon\n");
                return true;
            }
            catch (SocketException error)
            {
                Console.WriteLine($"  discovery failed: {error.Message}");
                return false;
            }
        }

        static Snapshot Capture(Rig[] rigs, RaceDriver[] drivers, uint tick, float time)
        {
            var cars = new CarState[rigs.Length];
            for (int i = 0; i < rigs.Length; i++)
            {
                BodyState body = rigs[i].Body.State;
                cars[i] = new CarState
                {
                    Id = (byte)i,
                    Position = body.Position,
                    Orientation = body.Orientation,
                    Velocity = body.Velocity,
                    Steer = rigs[i].Sim.SteerPosition,
                    EngineRpm = rigs[i].Sim.Drivetrain.EngineRpm,
                    Gear = (byte)Math.Max(rigs[i].Sim.Drivetrain.Gear, 0),
                    Lap = (byte)Math.Max(drivers[i].Path.Laps, 0),
                };
            }
            return new Snapshot { Tick = tick, TimeSeconds = time, Cars = cars };
        }

        static void RecordTruth(List<(float, Vector3[], Quaternion[])> truth, Rig[] rigs, float time)
        {
            var position = new Vector3[rigs.Length];
            var rotation = new Quaternion[rigs.Length];
            for (int i = 0; i < rigs.Length; i++)
            {
                position[i] = rigs[i].Body.State.Position;
                rotation[i] = rigs[i].Body.State.Orientation;
            }

            truth.Add((time, position, rotation));
            while (truth.Count > 0 && truth[0].Item1 < time - 3f) truth.RemoveAt(0);
        }

        static bool TruthAt(List<(float Time, Vector3[] Position, Quaternion[] Rotation)> truth,
                            float time, out Vector3[] position, out Quaternion[] rotation)
        {
            position = null;
            rotation = null;
            if (truth.Count == 0 || time < truth[0].Time) return false;

            int at = truth.Count - 1;
            while (at > 0 && truth[at].Time > time) at--;

            position = truth[at].Position;
            rotation = truth[at].Rotation;
            return true;
        }

        static float AngleBetween(Quaternion a, Quaternion b)
        {
            float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            if (dot > 1f) dot = 1f;
            return 2f * MathF.Acos(dot) * 180f / MathF.PI;
        }
    }
}
