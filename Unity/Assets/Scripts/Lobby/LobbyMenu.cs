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
    /// AI cars), -lanJoin
    /// 127.0.0.1 joins that address and -lanBrowse opens the Join screen, for testing
    /// several copies on one machine.
    ///
    /// With -benchmark on the command line it goes straight to the race, so the benchmark
    /// still measures racing.
    /// </summary>
    public sealed class LobbyMenu : MonoBehaviour
    {
        [SerializeField] Transform displayCar;
        [SerializeField] TrackCatalog catalog;
        [SerializeField] float turnDegreesPerSecond = 18f;

        static readonly Color Panel = new Color(0.05f, 0.06f, 0.1f, 0.86f);
        static readonly Color Accent = new Color(0.9f, 0.12f, 0.1f);
        static readonly Color AccentLight = new Color(1f, 0.55f, 0.1f);
        static readonly Color Muted = new Color(0.62f, 0.64f, 0.72f);
        static readonly Color Row = new Color(0.12f, 0.13f, 0.18f, 0.95f);

        GUIStyle _title, _subtitle, _header, _text, _small, _play, _cardName, _field, _button;
        string _typedAddress = "";
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
            // Only circuits that are in this build can be offered.
            if (catalog == null) return;
            foreach (var entry in catalog.entries)
                if (SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{entry.scene}.unity") >= 0) _circuits.Add(entry);
        }

        void OnDestroy()
        {
            foreach (var texture in _outlines.Values) Destroy(texture);
        }

        void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") >= 0) { Play(); return; }
            CarDesigns.Apply(displayCar, PlayerSetup.DesignIndex);
            PlayerSetup.Paint(displayCar != null ? displayCar.Find("Body") : null, PlayerSetup.Colour);

            // Back from a LAN race: that game is over.
            if (LanSession.Active && LanSession.Current.Race != null) LanSession.Current.Leave();

            string join = LanSession.Flag("-lanJoin");
            if (!LanSession.Active && Array.IndexOf(args, "-lanHost") >= 0)
            {
                LanSession.Current.StartHosting(ChosenScene);
                if (int.TryParse(LanSession.Flag("-lanAi"), out int ai)) LanSession.Current.Host?.SetAiCars(ai);
            }
            else if (!LanSession.Active && !string.IsNullOrEmpty(join)) LanSession.Current.Join(join);
            else if (!LanSession.Active && Array.IndexOf(args, "-lanBrowse") >= 0) LanSession.Current.StartBrowsing();

            // -lobbyScreenshot <file>: a picture of the lobby after a few seconds (or
            // -screenshotDelay seconds), then quit.
            int shot = Array.IndexOf(args, "-lobbyScreenshot");
            if (shot >= 0 && shot + 1 < args.Length) StartCoroutine(ScreenshotAndQuit(args[shot + 1]));
        }

        void Update()
        {
            if (displayCar != null) displayCar.Rotate(0f, turnDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            if (Input.GetKeyDown(KeyCode.Return) && GUIUtility.keyboardControl == 0) Play();
        }

        /// <summary>PLAY, or Enter: a race against the AI, or in a LAN game the host's start.
        /// A client waits for the host.</summary>
        void Play()
        {
            if (LanSession.Active)
            {
                if (LanSession.Current.State == LanSession.Mode.Hosting && LanSession.Current.Host.Humans >= 2)
                {
                    GameAudio.Confirm();
                    LanSession.Current.StartRace();
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

        void OnGUI()
        {
            Styles();
            _hover.Begin();

            // Title, top left.
            float margin = Hud.Px(40f);
            GUI.Label(new Rect(margin, Hud.Px(28f), Hud.Px(700f), Hud.Px(80f)), "CAR RACE", _title);
            Hud.Fill(new Rect(margin, Hud.Px(104f), Hud.Px(120f), Hud.Px(5f)), Accent);
            Hud.Fill(new Rect(margin + Hud.Px(120f), Hud.Px(104f), Hud.Px(60f), Hud.Px(5f)), AccentLight);
            GUI.Label(new Rect(margin, Hud.Px(114f), Hud.Px(700f), Hud.Px(30f)), "LOBBY", _subtitle);

            CircuitPanel(new Rect(margin, Hud.Px(180f), Hud.Px(400f), Hud.Px(560f)));

            // Car, right: the body designs, colour swatches, the colour's name and PLAY.
            int designs = CarDesigns.Count;
            float designRow = designs > 0 ? Hud.Px(62f) : 0f;
            float width = Hud.Px(460f), height = Hud.Px(380f) + designRow;
            var car = new Rect(Screen.width - margin - width, Screen.height - margin - height, width, height);

            // Players, top right, above the car.
            PlayersPanel(new Rect(car.x, margin, width, car.y - margin - Hud.Px(20f)));

            PanelWithHeader(car, "YOUR CAR");
            float size = Hud.Px(80f), gap = Hud.Px(20f);
            float left = car.x + (width - (4f * size + 3f * gap)) * 0.5f, top = car.y + Hud.Px(56f);
            if (designs > 0)
            {
                float buttonWidth = (4f * size + 3f * gap - (designs - 1) * Hud.Px(8f)) / designs;
                for (int i = 0; i < designs; i++)
                {
                    var button = new Rect(left + i * (buttonWidth + Hud.Px(8f)), top, buttonWidth, Hud.Px(44f));
                    bool chosen = i == PlayerSetup.DesignIndex;
                    bool hover = button.Contains(Event.current.mousePosition);
                    Hud.Rounded(button, chosen ? Accent : hover ? new Color(1f, 1f, 1f, 0.16f) : Row);
                    _cardName.normal.textColor = chosen ? Color.white : new Color(1f, 1f, 1f, 0.8f);
                    GUI.Label(button, CarDesigns.NameOf(i).ToUpperInvariant(), _cardName);
                    _hover.Watch(button);
                    if (GUI.Button(button, GUIContent.none, GUIStyle.none))
                    {
                        GameAudio.Select();
                        PlayerSetup.DesignIndex = i;
                        CarDesigns.Apply(displayCar, i);
                        if (LanSession.Active) LanSession.Current.SendSetup();
                    }
                }
                top += designRow;
            }
            for (int i = 0; i < PlayerSetup.Colours.Length; i++)
            {
                var swatch = new Rect(left + (i % 4) * (size + gap), top + (i / 4) * (size + gap), size, size);
                bool chosen = i == PlayerSetup.ColourIndex;
                bool hover = swatch.Contains(Event.current.mousePosition);
                if (chosen || hover)
                {
                    float ring = Hud.Px(chosen ? 4f : 2f);
                    Hud.Rounded(new Rect(swatch.x - ring, swatch.y - ring, swatch.width + 2f * ring, swatch.height + 2f * ring),
                                chosen ? Color.white : new Color(1f, 1f, 1f, 0.45f));
                }
                else
                {
                    // A faint outline, so the darkest colours still read against the panel.
                    float line = Hud.Px(1.5f);
                    Hud.Rounded(new Rect(swatch.x - line, swatch.y - line, swatch.width + 2f * line, swatch.height + 2f * line),
                                new Color(1f, 1f, 1f, 0.18f));
                }
                Hud.Rounded(swatch, PlayerSetup.Colours[i].colour);
                _hover.Watch(swatch);
                if (GUI.Button(swatch, GUIContent.none, GUIStyle.none))
                {
                    GameAudio.Select();
                    PlayerSetup.ColourIndex = i;
                    PlayerSetup.Paint(displayCar != null ? displayCar.Find("Body") : null, PlayerSetup.Colour);
                    if (LanSession.Active) LanSession.Current.SendSetup();
                }
            }
            _text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(car.x, top + 2f * size + gap + Hud.Px(8f), width, Hud.Px(30f)),
                      PlayerSetup.Colours[PlayerSetup.ColourIndex].name.ToUpperInvariant(), _text);
            _text.alignment = TextAnchor.MiddleLeft;

            var play = new Rect(car.x + Hud.Px(20f), car.yMax - Hud.Px(84f), width - Hud.Px(40f), Hud.Px(64f));
            string label = "PLAY  ▶";
            bool ready = true;
            if (LanSession.Active && LanSession.Current.State == LanSession.Mode.Joined) { label = "WAITING FOR HOST"; ready = false; }
            else if (LanSession.Active && LanSession.Current.Host.Humans < 2) { label = "WAITING FOR PLAYERS"; ready = false; }
            else if (LanSession.Active) label = "START RACE  ▶";
            bool over = ready && play.Contains(Event.current.mousePosition);
            Hud.Rounded(play, !ready ? Row : over ? AccentLight : Accent);
            _play.fontSize = Hud.Font(ready ? 30 : 22);
            GUI.Label(play, label, _play);
            if (ready)
            {
                _hover.Watch(play);
                if (GUI.Button(play, GUIContent.none, GUIStyle.none)) Play();
            }

            _small.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(car.x, car.yMax + Hud.Px(6f), width, Hud.Px(24f)), ready ? "Enter: play" : "", _small);
            _small.alignment = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Who is racing. Off a LAN game: your name, and Host or Join. Joining: the games
        /// found on the network and a box for an address. In a game: everyone in it, the AI
        /// cars (the host sets them), and Leave.
        /// </summary>
        void PlayersPanel(Rect panel)
        {
            LanSession session = LanSession.Active || LanSession.Browsing ? LanSession.Current : null;
            LanSession.Mode mode = session != null ? session.State : LanSession.Mode.Off;
            PanelWithHeader(panel, mode == LanSession.Mode.Browsing ? "JOIN A LAN GAME" : "PLAYERS");

            float pad = Hud.Px(14f), rowHeight = Hud.Px(36f), gap = Hud.Px(6f);
            float x = panel.x + pad, width = panel.width - 2f * pad, y = panel.y + Hud.Px(52f);
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
                          "PLAY races the AI on your own. Host or join to race people on your network.", _small);
                y += Hud.Px(48f);
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
                        GUI.Label(new Rect(row.x, row.y, row.width - Hud.Px(12f), row.height), tags, _small);
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
                    y = ai.yMax + Hud.Px(10f);

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

        /// <summary>A rounded button with a label; false and greyed when not enabled.</summary>
        bool Button(Rect rect, string label, bool enabled, bool back = false)
        {
            bool hover = enabled && rect.Contains(Event.current.mousePosition);
            Hud.Rounded(rect, !enabled ? new Color(1f, 1f, 1f, 0.06f) : hover ? AccentLight : back ? Row : Accent);
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

        /// <summary>A card per circuit, two across: its outline, name, length and surroundings.</summary>
        void CircuitPanel(Rect panel)
        {
            PanelWithHeader(panel, "CIRCUIT");
            if (_circuits.Count == 0)
            {
                GUI.Label(new Rect(panel.x + Hud.Px(16f), panel.y + Hud.Px(52f), panel.width - Hud.Px(32f), Hud.Px(60f)),
                          "No circuits in this build. Build one with CarRace, Build Track Scene.", _small);
                return;
            }

            float pad = Hud.Px(14f), gap = Hud.Px(12f);
            float cardWidth = (panel.width - 2f * pad - gap) / 2f, cardHeight = Hud.Px(152f);
            int outlinePixels = Mathf.RoundToInt(Hud.Px(96f));
            if (outlinePixels != _outlinePixels)
            {
                foreach (var texture in _outlines.Values) Destroy(texture);
                _outlines.Clear();
                _outlinePixels = outlinePixels;
            }

            // In a LAN game the lobby's circuit is the one that counts, and only the host changes it.
            bool lan = LanSession.Active;
            bool mayChoose = !lan || LanSession.Current.State == LanSession.Mode.Hosting;
            string chosen = lan && LanSession.Current.Lobby != null ? LanSession.Current.Lobby.Track : ChosenScene;
            for (int i = 0; i < _circuits.Count; i++)
            {
                var entry = _circuits[i];
                var card = new Rect(panel.x + pad + (i % 2) * (cardWidth + gap),
                                    panel.y + Hud.Px(52f) + (i / 2) * (cardHeight + gap), cardWidth, cardHeight);
                bool selected = entry.scene == chosen;
                bool hover = mayChoose && card.Contains(Event.current.mousePosition);
                if (selected || hover)
                {
                    float ring = Hud.Px(selected ? 3f : 2f);
                    Hud.Rounded(new Rect(card.x - ring, card.y - ring, card.width + 2f * ring, card.height + 2f * ring),
                                selected ? Accent : new Color(1f, 1f, 1f, 0.35f));
                }
                Hud.Rounded(card, Row);

                if (!_outlines.TryGetValue(entry.scene, out Texture2D outline))
                    _outlines[entry.scene] = outline = OutlineTexture(entry.outline, outlinePixels);
                GUI.DrawTexture(new Rect(card.center.x - outlinePixels * 0.5f, card.y + Hud.Px(8f), outlinePixels, outlinePixels), outline);

                _cardName.normal.textColor = selected ? Color.white : new Color(0.85f, 0.86f, 0.9f);
                GUI.Label(new Rect(card.x, card.y + Hud.Px(106f), card.width, Hud.Px(22f)), entry.displayName, _cardName);
                _small.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(card.x, card.y + Hud.Px(126f), card.width, Hud.Px(20f)),
                          $"{entry.lengthKm:0.0} km  ·  {entry.theme}", _small);
                _small.alignment = TextAnchor.UpperLeft;

                if (!mayChoose) continue;
                _hover.Watch(card);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none))
                {
                    GameAudio.Select();
                    ChosenScene = entry.scene;
                    if (lan) LanSession.Current.Host.SetTrack(entry.scene);
                }
            }
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

        void PanelWithHeader(Rect rect, string title)
        {
            Hud.Rounded(rect, Panel);
            float header = Hud.Px(38f);
            Hud.Rounded(new Rect(rect.x, rect.y, rect.width, header), Accent);
            Hud.Fill(new Rect(rect.x, rect.y + header * 0.5f, rect.width, header * 0.5f), Accent);
            Hud.Fill(new Rect(rect.x, rect.y + header, rect.width, Hud.Px(3f)), AccentLight);
            GUI.Label(new Rect(rect.x + Hud.Px(16f), rect.y, rect.width, header), title, _header);
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
        }
    }
}
