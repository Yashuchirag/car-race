using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CarRace.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The front end: the game's title, a players panel, a circuit panel with a card for each
    /// circuit in the build (its outline, name, length and surroundings), and a car panel with
    /// colour swatches and a PLAY button that loads the chosen circuit. The car turns on a platform behind the panels,
    /// repainted as a colour is picked. The players panel is also LAN play's: host a game or
    /// join one, see everyone who has joined, and as host set the AI cars and start the race.
    /// In a LAN game only the host picks the circuit. -lanHost hosts at once (with -lanAi N
    /// AI cars, -lanLaps N laps, and -track for the circuit), -lanJoin
    /// 127.0.0.1 joins that address and -lanBrowse opens the Join screen, for testing
    /// several copies on one machine.
    ///
    /// With -benchmark on the command line it goes straight to the race, so the benchmark
    /// still measures racing.
    /// </summary>
    public sealed partial class LobbyMenu : MonoBehaviour
    {
        [SerializeField] Transform displayCar;
        [SerializeField] TrackCatalog catalog;
        [Tooltip("The car as built, which the setup screen's settings are changes to.")]
        [SerializeField] CarDefinition car;
        [Tooltip("The glass panels' material (Shaders/GlassPanel.shader); without it the panels are flat.")]
        [SerializeField] Material glass;

        // Glass: smoky panels over a blur of the garage (GlassBackdrop), rows and secondary
        // buttons a lighter pane on them, the accent kept for what is chosen and for PLAY.
        static readonly Color Panel = new Color(0.03f, 0.04f, 0.07f, 0.8f);
        static readonly Color GlassTint = new Color(0.03f, 0.04f, 0.07f, 0.6f);
        static readonly Color Accent = new Color(0.9f, 0.12f, 0.1f);
        static readonly Color AccentLight = new Color(1f, 0.55f, 0.1f);
        static readonly Color Muted = new Color(0.8f, 0.82f, 0.88f);
        static readonly Color Row = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color Divider = new Color(1f, 1f, 1f, 0.14f);
        GlassBackdrop _backdrop;
        static readonly Color ReadyColour = new Color(0.12f, 0.6f, 0.28f);
        static readonly Color ReadyLight = new Color(0.2f, 0.72f, 0.36f);

        GUIStyle _title, _subtitle, _header, _text, _small, _play, _cardName, _field, _button;
        string _typedAddress = "";
        const int MaxLaps = 20;
        string _localAddresses;
        readonly List<TrackCatalog.Entry> _circuits = new List<TrackCatalog.Entry>();
        readonly Dictionary<string, Texture2D> _outlines = new Dictionary<string, Texture2D>();
        int _outlinePixels;

        const string TrackKey = "CarRace.Track";

        /// <summary>The chosen circuit's scene, kept between sessions; the first on offer if
        /// the saved one is no longer built.</summary>
        string ChosenScene
        {
            get
            {
                string saved = PlayerPrefs.GetString(TrackKey, "");
                return _circuits.Exists(c => c.scene == saved) ? saved : _circuits.Count > 0 ? _circuits[0].scene : "";
            }
            set
            {
                PlayerPrefs.SetString(TrackKey, value);
                PlayerPrefs.Save();
            }
        }

        void Awake()
        {
            // No GUILayout here: skip the layout pass Unity would otherwise run before each event.
            useGUILayout = false;
            // Only circuits that are in this build can be offered.
            if (catalog == null) return;
            foreach (var entry in catalog.entries)
                if (SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{entry.scene}.unity") >= 0) _circuits.Add(entry);
        }

        void OnDestroy()
        {
            foreach (var texture in _outlines.Values) Destroy(texture);
            foreach (var texture in _previews.Values) Destroy(texture);
            foreach (var texture in _icons.Values) Destroy(texture);
            foreach (var texture in new[] { _hueBar, _saturationBar, _brightnessBar }) if (texture != null) Destroy(texture);
        }

        void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") >= 0) { Play(); return; }
            if (glass != null && Camera.main != null) _backdrop = Camera.main.gameObject.AddComponent<GlassBackdrop>();
            CarDesigns.Apply(displayCar, PlayerSetup.DesignIndex);
            StartLayout(args);
            PlayerSetup.Paint(displayCar != null ? displayCar.Find("Body") : null, PlayerSetup.Colour);

            // Back from a LAN race: that game is over.
            if (LanSession.Active && LanSession.Current.Race != null) LanSession.Current.Leave();

            string join = LanSession.Flag("-lanJoin");
            if (!LanSession.Active && Array.IndexOf(args, "-lanHost") >= 0)
            {
                LanSession.Current.StartHosting(LanSession.Flag("-track") ?? ChosenScene);
                if (int.TryParse(LanSession.Flag("-lanAi"), out int ai)) LanSession.Current.Host?.SetAiCars(ai);
                if (int.TryParse(LanSession.Flag("-lanLaps"), out int laps)) LanSession.Current.Host?.SetLaps(laps);
            }
            else if (!LanSession.Active && !string.IsNullOrEmpty(join)) LanSession.Current.Join(join);
            else if (!LanSession.Active && Array.IndexOf(args, "-lanBrowse") >= 0) LanSession.Current.StartBrowsing();

            // -openSetup <tab>: the setup screen open on that tab (0 suspension to 5 assists),
            // for screenshots.
            if (int.TryParse(LanSession.Flag("-openSetup"), out int tab) && car != null)
            {
                OpenSetup();
                _setupTab = Mathf.Clamp(tab, 0, SetupTabs.Length - 1);
            }

            // -lobbyScreenshot <file>: a picture of the lobby after a few seconds (or
            // -screenshotDelay seconds), then quit.
            int shot = Array.IndexOf(args, "-lobbyScreenshot");
            if (shot >= 0 && shot + 1 < args.Length) StartCoroutine(ScreenshotAndQuit(args[shot + 1]));
        }

        void Update()
        {
            if (_setupOpen) return;
            if (Input.GetKeyDown(KeyCode.H) && GUIUtility.keyboardControl == 0)
            {
                _menuHidden = !_menuHidden;
                GameAudio.Select();
            }
            if (Input.GetKeyDown(KeyCode.Return) && GUIUtility.keyboardControl == 0) Play();
        }

        /// <summary>PLAY, or Enter: a race against the AI, or in a LAN game the host's start.
        /// A client waits for the host.</summary>
        void Play()
        {
            if (LanSession.Active)
            {
                LanSession session = LanSession.Current;
                if (session.State == LanSession.Mode.Joined && session.Client.Id != 0)
                {
                    GameAudio.Select();
                    session.ToggleReady();
                }
                else if (session.CanStart)
                {
                    GameAudio.Confirm();
                    session.StartRace();
                }
                return;
            }
            if (LanSession.Browsing) LanSession.Current.Leave();

            // -track "Track Royal Park Speedway" picks the circuit for one session, so the
            // benchmark always races the same one.
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-track");
            string scene = i >= 0 && i + 1 < args.Length ? args[i + 1] : ChosenScene;
            if (string.IsNullOrEmpty(scene)) return;
            GameAudio.Confirm();
            SceneManager.LoadScene(scene);
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            yield return new WaitForSeconds(float.TryParse(LanSession.Flag("-screenshotDelay"), out float delay) ? delay : 4f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        readonly GameAudio.HoverTracker _hover = new GameAudio.HoverTracker();

        /// <summary>
        /// Who is racing. Off a LAN game: your name, and Host or Join. Joining: the games
        /// found on the network and a box for an address. In a game: everyone in it, the AI
        /// cars (the host sets them), and Leave.
        /// </summary>
        void PlayersContent(Rect area)
        {
            LanSession session = LanSession.Active || LanSession.Browsing ? LanSession.Current : null;
            LanSession.Mode mode = session != null ? session.State : LanSession.Mode.Off;
            float rowHeight = Hud.Px(40f), gap = Hud.Px(6f);
            float x = area.x, width = area.width, y = area.y;
            Section(area, ref y, mode == LanSession.Mode.Browsing ? "JOIN A LAN GAME" : mode == LanSession.Mode.Off ? "LAN RACING" : "PLAYERS");
            string message = LanSession.Message;

            if (mode == LanSession.Mode.Off)
            {
                var row = new Rect(x, y, width, Hud.Px(44f));
                Hud.Rounded(row, Row);
                _small.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(row.x + Hud.Px(12f), row.y, Hud.Px(80f), row.height), "NAME", _small);
                _small.alignment = TextAnchor.UpperLeft;
                string name = GUI.TextField(new Rect(row.x + Hud.Px(76f), row.y, row.width - Hud.Px(80f), row.height),
                                            LanSession.PlayerName, 24, _field);
                if (name != LanSession.PlayerName && name.Trim().Length > 0) LanSession.PlayerName = name;
                y = row.yMax + Hud.Px(12f);

                float half = (width - gap) * 0.5f;
                if (Button(new Rect(x, y, half, Hud.Px(48f)), "HOST LAN GAME", true))
                    LanSession.Current.StartHosting(ChosenScene);
                if (Button(new Rect(x + half + gap, y, half, Hud.Px(48f)), "JOIN LAN GAME", true))
                    LanSession.Current.StartBrowsing();
                y += Hud.Px(62f);

                GUI.Label(new Rect(x + Hud.Px(2f), y, width, Hud.Px(60f)),
                          "PLAY races the AI on your own. Host a game for people on your network to join, " +
                          "or join one: the host picks the circuit, the laps and the AI cars.", _small);
                y += Hud.Px(60f);
            }
            else if (mode == LanSession.Mode.Browsing)
            {
                IReadOnlyList<LanBrowser.Game> games = session.Browser.Games;
                if (games.Count == 0)
                {
                    GUI.Label(new Rect(x + Hud.Px(2f), y, width, rowHeight), "Looking for games on your network...", _small);
                    y += rowHeight + gap;
                }
                for (int i = 0; i < games.Count && i < 5; i++)
                {
                    LanBrowser.Game game = games[i];
                    var row = new Rect(x, y, width, rowHeight);
                    bool hover = row.Contains(Event.current.mousePosition);
                    Hud.Rounded(row, hover ? new Color(1f, 1f, 1f, 0.16f) : Row);
                    GUI.Label(new Rect(row.x + Hud.Px(12f), row.y, row.width, row.height), game.Beacon.HostName, _text);
                    _small.alignment = TextAnchor.MiddleRight;
                    GUI.Label(new Rect(row.x, row.y, row.width - Hud.Px(12f), row.height),
                              $"{CircuitName(game.Beacon.Track)}  ·  {game.Beacon.Players}/{game.Beacon.Capacity}", _small);
                    _small.alignment = TextAnchor.UpperLeft;
                    _hover.Watch(row);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                    {
                        GameAudio.Confirm();
                        session.Join(game.Address, game.Beacon.GamePort);
                    }
                    y += rowHeight + gap;
                }

                y += Hud.Px(6f);
                var field = new Rect(x, y, width - Hud.Px(96f), Hud.Px(44f));
                Hud.Rounded(field, Row);
                if (_typedAddress.Length == 0 && GUIUtility.keyboardControl == 0)
                {
                    _small.alignment = TextAnchor.MiddleLeft;
                    GUI.Label(new Rect(field.x + Hud.Px(10f), field.y, field.width, field.height), "Or type the host's IP address", _small);
                    _small.alignment = TextAnchor.UpperLeft;
                }
                _typedAddress = GUI.TextField(field, _typedAddress, 40, _field);
                bool typedEnter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                                  && GUIUtility.keyboardControl != 0;
                if (Button(new Rect(field.xMax + gap, y, Hud.Px(90f), field.height), "JOIN", _typedAddress.Trim().Length > 0)
                    || typedEnter && _typedAddress.Trim().Length > 0)
                {
                    GUIUtility.keyboardControl = 0;
                    session.Join(_typedAddress);
                }
                y = field.yMax + Hud.Px(12f);

                if (Button(new Rect(x, y, Hud.Px(120f), Hud.Px(40f)), "BACK", true, back: true)) session.Leave();
                y += Hud.Px(52f);
            }
            else
            {
                LobbyState lobby = session.Lobby;
                bool hosting = mode == LanSession.Mode.Hosting;
                if (lobby == null)
                {
                    GUI.Label(new Rect(x + Hud.Px(2f), y, width, rowHeight), message ?? "Connecting...", _small);
                    message = null;
                    y += rowHeight + gap;
                }
                else
                {
                    foreach (PlayerInfo player in lobby.Players)
                    {
                        var row = new Rect(x, y, width, rowHeight);
                        Hud.Rounded(row, Row);
                        Color colour = PlayerSetup.Colours[Mathf.Clamp(player.Colour, 0, PlayerSetup.Colours.Length - 1)].colour;
                        Hud.Rounded(new Rect(row.x + Hud.Px(8f), row.y + Hud.Px(7f), Hud.Px(22f), Hud.Px(22f)), colour);
                        GUI.Label(new Rect(row.x + Hud.Px(40f), row.y, row.width, row.height), player.Name, _text);
                        string tags = CarDesigns.Count > 0 ? CarDesigns.NameOf(Mathf.Clamp(player.Design, 0, CarDesigns.Count - 1)).ToUpperInvariant() : "";
                        if (player.Id == 0) tags += "  ·  HOST";
                        if (player.Id == session.MyId) tags += "  ·  YOU";
                        _small.alignment = TextAnchor.MiddleRight;
                        GUI.Label(new Rect(row.x, row.y, row.width - Hud.Px(100f), row.height), tags, _small);

                        // Ready or not, for everyone but the host, whose ready is the start.
                        if (player.Id != 0)
                        {
                            _small.normal.textColor = player.Ready ? new Color(0.35f, 0.9f, 0.45f) : AccentLight;
                            GUI.Label(new Rect(row.x, row.y, row.width - Hud.Px(12f), row.height), player.Ready ? "READY" : "NOT READY", _small);
                            _small.normal.textColor = Muted;
                        }
                        _small.alignment = TextAnchor.UpperLeft;
                        y += rowHeight + gap;
                    }

                    // The AI cars: the host sets how many, up to a grid of eight.
                    y += Hud.Px(4f);
                    var ai = new Rect(x, y, width, Hud.Px(40f));
                    Hud.Rounded(ai, Row);
                    GUI.Label(new Rect(ai.x + Hud.Px(12f), ai.y, ai.width, ai.height), "AI CARS", _text);
                    int most = LanHost.MaxCars - lobby.Players.Count;
                    _text.alignment = TextAnchor.MiddleCenter;
                    var count = new Rect(ai.xMax - Hud.Px(100f), ai.y, Hud.Px(40f), ai.height);
                    GUI.Label(hosting ? count : new Rect(ai.xMax - Hud.Px(60f), ai.y, Hud.Px(40f), ai.height), lobby.AiCars.ToString(), _text);
                    _text.alignment = TextAnchor.MiddleLeft;
                    if (hosting)
                    {
                        if (Button(new Rect(count.x - Hud.Px(36f), ai.y + Hud.Px(5f), Hud.Px(30f), Hud.Px(30f)), "−", lobby.AiCars > 0))
                            session.Host.SetAiCars(lobby.AiCars - 1);
                        if (Button(new Rect(count.xMax + Hud.Px(6f), ai.y + Hud.Px(5f), Hud.Px(30f), Hud.Px(30f)), "+", lobby.AiCars < most))
                            session.Host.SetAiCars(lobby.AiCars + 1);
                    }
                    y = ai.yMax + Hud.Px(6f);

                    // The laps, likewise the host's to set.
                    var laps = new Rect(x, y, width, Hud.Px(40f));
                    Hud.Rounded(laps, Row);
                    GUI.Label(new Rect(laps.x + Hud.Px(12f), laps.y, laps.width, laps.height), "LAPS", _text);
                    _text.alignment = TextAnchor.MiddleCenter;
                    var lapCount = new Rect(laps.xMax - Hud.Px(100f), laps.y, Hud.Px(40f), laps.height);
                    GUI.Label(hosting ? lapCount : new Rect(laps.xMax - Hud.Px(60f), laps.y, Hud.Px(40f), laps.height), lobby.Laps.ToString(), _text);
                    _text.alignment = TextAnchor.MiddleLeft;
                    if (hosting)
                    {
                        if (Button(new Rect(lapCount.x - Hud.Px(36f), laps.y + Hud.Px(5f), Hud.Px(30f), Hud.Px(30f)), "−", lobby.Laps > 1))
                            session.Host.SetLaps(lobby.Laps - 1);
                        if (Button(new Rect(lapCount.xMax + Hud.Px(6f), laps.y + Hud.Px(5f), Hud.Px(30f), Hud.Px(30f)), "+", lobby.Laps < MaxLaps))
                            session.Host.SetLaps(lobby.Laps + 1);
                    }
                    y = laps.yMax + Hud.Px(10f);

                    string status = hosting
                        ? $"{lobby.Players.Count} of {LanHost.MaxPlayers} players, {lobby.Players.Count + lobby.AiCars} of {LanHost.MaxCars} cars. Others join from your IP: {LocalAddresses()}"
                        : $"{lobby.Players.Count + lobby.AiCars} cars. Waiting for the host to start the race.";
                    GUI.Label(new Rect(x + Hud.Px(2f), y, width, Hud.Px(40f)), status, _small);
                    y += Hud.Px(44f);
                }

                if (Button(new Rect(x, y, Hud.Px(120f), Hud.Px(40f)), "LEAVE", true, back: true)) session.Leave();
                y += Hud.Px(52f);
            }

            if (!string.IsNullOrEmpty(message))
            {
                _small.normal.textColor = AccentLight;
                GUI.Label(new Rect(x + Hud.Px(2f), y, width, Hud.Px(44f)), message, _small);
                _small.normal.textColor = Muted;
            }
        }

        /// <summary>A row with a value and minus and plus, as the LAN host's AI CARS and LAPS.</summary>
        void SettingRow(float x, ref float y, float width, string label, string value, bool down, bool up, Action<int> change)
        {
            var row = new Rect(x, y, width, Hud.Px(40f));
            Hud.Rounded(row, Row);
            GUI.Label(new Rect(row.x + Hud.Px(12f), row.y, row.width, row.height), label, _text);
            _text.alignment = TextAnchor.MiddleCenter;
            var shown = new Rect(row.xMax - Hud.Px(110f), row.y, Hud.Px(60f), row.height);
            GUI.Label(shown, value, _text);
            _text.alignment = TextAnchor.MiddleLeft;
            if (Button(new Rect(shown.x - Hud.Px(36f), row.y + Hud.Px(5f), Hud.Px(30f), Hud.Px(30f)), "−", down)) change(-1);
            if (Button(new Rect(shown.xMax + Hud.Px(6f), row.y + Hud.Px(5f), Hud.Px(30f), Hud.Px(30f)), "+", up)) change(+1);
            y = row.yMax + Hud.Px(6f);
        }

        /// <summary>A rounded button with a label, lighting up as the mouse comes over it; false and
        /// greyed when not enabled. A back button is a pane of glass, the others the accent.</summary>
        bool Button(Rect rect, string label, bool enabled, bool back = false)
        {
            bool hover = enabled && rect.Contains(Event.current.mousePosition);
            float glow = Glow(label, rect, hover);
            Hud.Rounded(rect, !enabled ? new Color(1f, 1f, 1f, 0.06f)
                            : back ? Color.Lerp(Row, new Color(1f, 1f, 1f, 0.22f), glow) : Color.Lerp(Accent, AccentLight, glow));
            _button.normal.textColor = enabled ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            GUI.Label(rect, label, _button);
            if (!enabled) return false;
            _hover.Watch(rect);
            if (!GUI.Button(rect, GUIContent.none, GUIStyle.none)) return false;
            if (back) GameAudio.Back(); else GameAudio.Select();
            return true;
        }

        string CircuitName(string scene)
        {
            var entry = _circuits.Find(c => c.scene == scene);
            return entry != null ? entry.displayName : scene;
        }

        /// <summary>This machine's IPv4 addresses on the network, for the host to read out:
        /// those on an adapter with a gateway, which leaves out virtual ones such as WSL's.</summary>
        string LocalAddresses()
        {
            if (_localAddresses != null) return _localAddresses;
            var found = new List<string>();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    bool routed = false;
                    foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                        routed |= gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any);
                    if (!routed) continue;
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                            found.Add(address.Address.ToString());
                }
            }
            catch (Exception) { }   // not every platform can list its adapters
            if (found.Count == 0)
            {
                try
                {
                    foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                        if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                            found.Add(address.ToString());
                }
                catch (SocketException) { }
            }
            return _localAddresses = found.Count > 0 ? string.Join(", ", found) : "unknown";
        }

        /// <summary>A circuit's outline in white on a clear square, drawn as discs along it.</summary>
        static Texture2D OutlineTexture(Vector2[] outline, int size)
        {
            var pixels = new Color32[size * size];
            float radius = Mathf.Max(1.2f, size * 0.018f), margin = size * 0.08f, scale = size - 2f * margin;
            int n = outline.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i] * scale + Vector2.one * margin, b = outline[(i + 1) % n] * scale + Vector2.one * margin;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int k = 0; k <= steps; k++)
                {
                    Vector2 c = Vector2.Lerp(a, b, k / (float)steps);
                    int r = Mathf.CeilToInt(radius);
                    for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        int px = Mathf.RoundToInt(c.x) + x, py = Mathf.RoundToInt(c.y) + y;
                        if (px < 0 || py < 0 || px >= size || py >= size) continue;
                        float d = Mathf.Sqrt(x * x + y * y);
                        byte alpha = (byte)(255f * Mathf.Clamp01(radius + 0.5f - d));
                        int at = py * size + px;
                        if (alpha > pixels[at].a) pixels[at] = new Color32(255, 255, 255, alpha);
                    }
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>A glass panel with its title: an accent tick, the title, and a hairline under
        /// it. The content starts 52 px down, as it did under the old solid header.</summary>
        void PanelWithHeader(Rect rect, string title)
        {
            Glass(rect, GlassTint);
            float header = Hud.Px(42f);
            Hud.Rounded(new Rect(rect.x + Hud.Px(16f), rect.y + Hud.Px(13f), Hud.Px(5f), Hud.Px(18f)), Accent);
            GUI.Label(new Rect(rect.x + Hud.Px(30f), rect.y + Hud.Px(2f), rect.width, header), title, _header);
            Hud.Fill(new Rect(rect.x + Hud.Px(16f), rect.y + header, rect.width - Hud.Px(32f), Mathf.Max(1f, Hud.Px(1f))), Divider);
        }

        /// <summary>
        /// Frosted glass over <paramref name="rect"/>: the blurred garage behind it
        /// (GlassBackdrop) through the glass shader, tinted, with a sheen from the top and a
        /// bright edge. Flat in the panel colour until the first blurred frame, or without the
        /// material.
        /// </summary>
        void Glass(Rect rect, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            Texture blurred = _backdrop != null ? _backdrop.Blurred : null;
            if (glass == null || blurred == null) { Hud.Rounded(rect, Panel); return; }
            glass.SetVector("_Rect", new Vector4(rect.x, rect.y, rect.width, rect.height));
            glass.SetVector("_Screen", new Vector4(Screen.width, Screen.height, Hud.Px(16f), Mathf.Max(1f, Hud.Px(1.3f))));
            glass.SetColor("_Tint", tint);
            glass.SetColor("_Edge", new Color(1f, 1f, 1f, 0.28f));
            glass.SetFloat("_Sheen", 0.07f);
            Graphics.DrawTexture(rect, blurred, glass);
        }

        void Styles()
        {
            _title ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, normal = { textColor = Color.white } };
            _subtitle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Muted } };
            _header ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _text ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _small ??= new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = Muted } };
            _play ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _cardName ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _field ??= new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold,
                normal = { background = null, textColor = Color.white },
                focused = { background = null, textColor = Color.white },
                hover = { background = null, textColor = Color.white },
            };
            _button ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _field.fontSize = Hud.Font(17);
            _field.padding = new RectOffset(Mathf.RoundToInt(Hud.Px(10f)), 4, 0, 0);
            _button.fontSize = Hud.Font(16);
            _cardName.fontSize = Hud.Font(14);
            _title.fontSize = Hud.Font(72);
            _subtitle.fontSize = Hud.Font(20);
            _header.fontSize = Hud.Font(17);
            _text.fontSize = Hud.Font(18);
            _small.fontSize = Hud.Font(14);
            _play.fontSize = Hud.Font(30);
            LayoutStyles();
        }
    }
}
