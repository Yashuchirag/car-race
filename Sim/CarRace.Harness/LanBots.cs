using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using CarRace.Net;

namespace CarRace.Harness
{
    /// <summary>
    /// Bot players that join a real host, the game, so its lobby can be seen full without
    /// six computers. They join, one changes colour after a few seconds, and they report
    /// what their lobby shows and whether the start reaches them. They do not drive yet.
    ///
    ///     --lan-bots 127.0.0.1 --bots 5 --seconds 60
    /// </summary>
    public static class LanBots
    {
        public static int Run(string address, int bots, float seconds)
        {
            if (!IPAddress.TryParse(address, out IPAddress host))
            {
                Console.WriteLine($"  \"{address}\" is not an IP address.");
                return 2;
            }

            Console.WriteLine($"=== {bots} bots joining {host}:{LanHost.DefaultPort} for {seconds:0} s ===\n");
            var clients = new List<LanClient>();
            for (int i = 1; i <= bots; i++)
                clients.Add(new LanClient(host, LanHost.DefaultPort,
                                          new PlayerInfo { Name = $"Bot {i}", Colour = (byte)(i % 8), Design = (byte)(i % 4) }));

            var clock = Stopwatch.StartNew();
            var reported = new HashSet<string>();
            void Once(string line) { if (reported.Add(line)) Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0} s  {line}"); }
            bool recoloured = false;
            float allStartedAt = -1f;

            while (clock.Elapsed.TotalSeconds < seconds)
            {
                float now = (float)clock.Elapsed.TotalSeconds;
                for (int i = 0; i < clients.Count; i++)
                {
                    LanClient c = clients[i];
                    c.Poll(now);
                    string name = $"Bot {i + 1}";

                    // Until it first gets in, a bot keeps trying: the game may still be loading.
                    if (c.State == LanClient.Phase.Closed && c.Id == 0 && now < seconds - 1f)
                    {
                        c.Dispose();
                        Thread.Sleep(200);
                        clients[i] = new LanClient(host, LanHost.DefaultPort,
                                                   new PlayerInfo { Name = name, Colour = (byte)((i + 1) % 8), Design = (byte)((i + 1) % 4) });
                        continue;
                    }
                    if (c.Id != 0) Once($"{name} joined as player {c.Id}");
                    if (c.State == LanClient.Phase.Rejected) Once($"{name} turned away: {c.Rejected}");
                    if (c.State == LanClient.Phase.Closed) Once($"{name} lost the host");
                    if (c.Synced) Once($"{name} synced its clock");
                    if (c.Race != null && reported.Add($"{name} start"))
                        Console.WriteLine($"  {now,5:0.0} s  {name} got the start: {c.Race.Track}, {c.Race.Grid.Length} cars, "
                                        + $"GO in {c.Race.GoAtHostSeconds - c.HostNow(now):0.00} s");
                }

                LanClient first = clients[0];
                if (first.Changed && first.Lobby != null)
                {
                    first.Changed = false;
                    var names = new List<string>();
                    foreach (PlayerInfo p in first.Lobby.Players) names.Add($"{p.Name} (colour {p.Colour})");
                    Console.WriteLine($"  {now,5:0.0} s  lobby: {first.Lobby.Track}, {first.Lobby.AiCars} AI, "
                                    + string.Join(", ", names));
                }

                if (!recoloured && now > 5f && first.Id != 0)
                {
                    recoloured = true;
                    first.SetSetup(6, 2);
                    Once("Bot 1 changes to colour 6, design 2");
                }

                if (allStartedAt < 0f && clients.TrueForAll(c => c.Race != null)) allStartedAt = now;
                if (allStartedAt >= 0f && now > allStartedAt + 3f) break;
                Thread.Sleep(5);
            }

            foreach (LanClient c in clients)
                if (c.Synced)
                    Console.WriteLine($"  clock: round trip {c.RoundTripSeconds * 1000f:0.00} ms, "
                                    + $"recent pings' offsets within {c.OffsetSpreadSeconds * 1000f:0.00} ms of each other");
            int joined = clients.FindAll(c => c.Id != 0).Count;
            foreach (LanClient c in clients) c.Dispose();
            Console.WriteLine($"\n  {joined} of {bots} joined{(allStartedAt >= 0f ? ", all got the start" : "")}");
            return joined == bots ? 0 : 1;
        }
    }
}
