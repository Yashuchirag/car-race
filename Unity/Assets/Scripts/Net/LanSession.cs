using System;
using System.Net;
using System.Net.Sockets;
using CarRace.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The game's one LAN connection, if any: browsing for games, hosting one, or joined to
    /// one. It outlives scene loads, since the race scene needs the connection the lobby
    /// made, and polls the network once a frame on the game's own thread.
    ///
    /// When the host starts the race, every machine loads the circuit the lobby chose.
    /// Command line, for testing several copies on one machine: -lanStartWhen 3 has a host
    /// start as soon as three players are in and ready, -lanReady has a joined copy say
    /// ready at once, and -playerName sets the name for one session. LobbyMenu reads
    /// -lanHost and -lanJoin.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // before anything that asks it the time this frame
    public sealed class LanSession : MonoBehaviour
    {
        public enum Mode { Off, Browsing, Hosting, Joined }

        const string NameKey = "CarRace.PlayerName";

        static LanSession _current;
        public static LanSession Current
        {
            get
            {
                if (_current != null) return _current;
                var go = new GameObject("LAN Session");
                DontDestroyOnLoad(go);
                return _current = go.AddComponent<LanSession>();
            }
        }

        /// <summary>True while hosting or joined, so a scene can ask without creating one.</summary>
        public static bool Active => _current != null && (_current.State == Mode.Hosting || _current.State == Mode.Joined);
        public static bool Browsing => _current != null && _current.State == Mode.Browsing;

        /// <summary>The last session's message, without creating a session to ask.</summary>
        public static string Message => _current != null ? _current.Status : null;

        public Mode State { get; private set; }
        public LanHost Host { get; private set; }
        public LanClient Client { get; private set; }
        public LanBrowser Browser { get; private set; }

        /// <summary>Why the last attempt failed or the connection ended, for the lobby to show.</summary>
        public string Status;

        public LobbyState Lobby => Host != null ? Host.Lobby : Client?.Lobby;
        public RaceStart Race => Host != null ? Host.Race : Client?.Race;
        public byte MyId => Client != null ? Client.Id : (byte)0;

        /// <summary>The network's clock on this machine: real time, read when asked.</summary>
        public static float Now => Time.realtimeSinceStartup;

        // Game time to real time. Physics runs in game time, a step at a time, and a car's
        // state belongs to the moment its step ends; the network stamps in real time. The two
        // run at the same rate in a LAN race (nothing pauses it), apart by a constant that a
        // hitch longer than Unity's maximum step can grow. Each frame's real time less its
        // game time is at least that constant, by however late in the frame it was read, so
        // the least of the last two seconds of frames is the constant.
        static readonly double[] _gameToReal = new double[120];
        static int _gameToRealAt, _gameToRealCount;
        static double _gameToRealOffset = double.NaN;

        /// <summary>The real time, on Now's clock, that a game time corresponds to.</summary>
        public static float RealTimeOf(double gameTime)
        {
            if (double.IsNaN(_gameToRealOffset)) SampleGameToReal();
            return (float)(gameTime + _gameToRealOffset);
        }

        static void SampleGameToReal()
        {
            _gameToReal[_gameToRealAt] = Time.realtimeSinceStartupAsDouble - Time.timeAsDouble;
            _gameToRealAt = (_gameToRealAt + 1) % _gameToReal.Length;
            if (_gameToRealCount < _gameToReal.Length) _gameToRealCount++;
            double least = double.MaxValue;
            for (int i = 0; i < _gameToRealCount; i++) least = System.Math.Min(least, _gameToReal[i]);
            _gameToRealOffset = least;
        }

        void Awake() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        /// <summary>The circuit of a LAN race has loaded: set it up for everyone on the grid.</summary>
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Race != null && (State == Mode.Hosting || State == Mode.Joined)) LanRace.SetUp(this);
        }

        public static string PlayerName
        {
            get
            {
                string flag = Flag("-playerName");
                if (!string.IsNullOrEmpty(flag)) return flag;
                return PlayerPrefs.GetString(NameKey, Environment.UserName);
            }
            set
            {
                PlayerPrefs.SetString(NameKey, value);
                PlayerPrefs.Save();
            }
        }

        PlayerInfo Me => new PlayerInfo
        {
            Name = PlayerName, Colour = (byte)PlayerSetup.ColourIndex, Design = (byte)PlayerSetup.DesignIndex,
        };

        /// <summary>
        /// Unity pauses a game whose window is not in front unless told otherwise. A host in
        /// the background would stop answering and every player would time out, which is
        /// what happened with two copies of the game on one screen; a player in the
        /// background would stop sending their car. So while connected, the game keeps running.
        /// </summary>
        void LateUpdate() => Application.runInBackground = State != Mode.Off;

        public void StartBrowsing()
        {
            Leave();
            try
            {
                Browser = new LanBrowser();
                State = Mode.Browsing;
            }
            catch (SocketException e)
            {
                Status = $"Cannot listen for games: {e.Message}";
            }
        }

        public void StartHosting(string scene)
        {
            Leave();
            try
            {
                Host = new LanHost(PlayerName, Me, scene);
                // The host's own solo choices to begin with; it changes them in the lobby.
                Host.Lobby.TyreWear = (byte)RaceSettings.WearChoice;
                Host.Lobby.SafetyCar = RaceSettings.SafetyCarChoice;
                State = Mode.Hosting;
            }
            catch (SocketException e)
            {
                Host = null;
                Status = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"Port {LanHost.DefaultPort} is in use: is another copy of the game hosting?"
                    : $"Cannot host: {e.Message}";
            }
        }

        public void Join(IPAddress address, int port = LanHost.DefaultPort)
        {
            Leave();
            Client = new LanClient(address, port, Me);
            State = Mode.Joined;
            Status = $"Connecting to {address}...";
        }

        /// <summary>Joins an address typed in, or says why it cannot.</summary>
        public void Join(string typed)
        {
            if (IPAddress.TryParse(typed.Trim(), out IPAddress address) && address.AddressFamily == AddressFamily.InterNetwork)
                Join(address);
            else
                Status = $"\"{typed}\" is not an IP address, such as 192.168.1.20.";
        }

        public void Leave()
        {
            Host?.Dispose();
            Client?.Dispose();
            Browser?.Dispose();
            Host = null;
            Client = null;
            Browser = null;
            State = Mode.Off;
            Status = null;
        }

        /// <summary>This player's colour and design, to everyone.</summary>
        public void SendSetup()
        {
            Host?.SetMySetup((byte)PlayerSetup.ColourIndex, (byte)PlayerSetup.DesignIndex);
            Client?.SetSetup((byte)PlayerSetup.ColourIndex, (byte)PlayerSetup.DesignIndex);
        }

        /// <summary>A joined player who has said ready, whose car is then fixed.</summary>
        public bool IsReadyToRace => Client != null && Client.Ready;

        /// <summary>The host has someone to race, and everyone is ready.</summary>
        public bool CanStart => Host != null && !Host.Started && Host.Humans >= 2 && Host.AllReady;

        public void ToggleReady() => Client?.SetReady(!Client.Ready);

        /// <summary>The host ends the race and takes everyone back to the lobby, together, to
        /// choose the next one. The others follow when the host's word reaches them.</summary>
        public void ReturnToLobby()
        {
            if (Host == null || !Host.Started) return;
            Host.ReturnToLobby();
            Debug.Log("LAN: taking everyone back to the lobby");
            SettingsMenu.MainMenu();
        }

        /// <summary>Starts the race for everyone, once everyone is ready.</summary>
        public void StartRace()
        {
            if (!CanStart) return;
            RaceStart race = Host.Start(Now);
            Debug.Log($"LAN: hosting a race on {race.Track}, {race.Grid.Length} cars");
            SceneManager.LoadScene(race.Track);
        }

        void Update()
        {
            SampleGameToReal();
            float now = Now;
            Browser?.Poll(now);
            Host?.Poll(now);

            if (Client != null)
            {
                bool wasRacing = Client.Race != null;
                Client.Poll(now);
                if (Client.State == LanClient.Phase.Lobby && Client.Id != 0) Status = null;

                if (!wasRacing && Client.Race != null)
                {
                    Debug.Log($"LAN: the host started a race on {Client.Race.Track}, {Client.Race.Grid.Length} cars");
                    SceneManager.LoadScene(Client.Race.Track);
                }
                else if (wasRacing && Client.Race == null && Client.State == LanClient.Phase.Lobby)
                {
                    Debug.Log("LAN: the host took everyone back to the lobby");
                    SettingsMenu.MainMenu();
                }
                else if (Client.State == LanClient.Phase.Rejected || Client.State == LanClient.Phase.Closed)
                {
                    string why = Client.State == LanClient.Phase.Closed
                        ? (Client.Id == 0 ? "No game answered at that address." : "The host has gone.")
                        : Client.Rejected == RejectReason.Full ? "That game is full."
                        : Client.Rejected == RejectReason.Started ? "That race has already started."
                        : "That host is running a different version of the game.";
                    if (Client.CloseReason != null) Debug.Log($"LAN: the connection closed: {Client.CloseReason}");
                    Leave();
                    Status = why;
                }
            }

            if (Host != null && Host.Dropped.Count > 0)
            {
                foreach (string why in Host.Dropped) Debug.Log($"LAN: dropped {why}");
                Host.Dropped.Clear();
            }

            if (int.TryParse(Flag("-lanStartWhen"), out int players) && CanStart && Host.Humans >= players)
                StartRace();

            // -lanReady: a test copy says ready as soon as it is in.
            if (Client != null && Client.Id != 0 && Client.Race == null && !Client.Ready
                && Array.IndexOf(Environment.GetCommandLineArgs(), "-lanReady") >= 0)
                Client.SetReady(true);
        }

        void OnApplicationQuit() => Leave();

        public static string Flag(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
