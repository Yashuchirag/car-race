using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The front end: the game's title, a players panel, a circuit panel with a card for each
    /// circuit in the build (its outline, name, length and surroundings), and a car panel with
    /// colour swatches and a PLAY button that loads the chosen circuit. The car turns on a platform behind the panels,
    /// repainted as a colour is picked. The players panel lists only the local player for now;
    /// it is where LAN play will list everyone who has joined before the host starts the race.
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

        GUIStyle _title, _subtitle, _header, _text, _small, _play, _cardName;
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

            // -lobbyScreenshot <file>: a picture of the lobby after a few seconds, then quit.
            int shot = Array.IndexOf(args, "-lobbyScreenshot");
            if (shot >= 0 && shot + 1 < args.Length) StartCoroutine(ScreenshotAndQuit(args[shot + 1]));
        }

        void Update()
        {
            if (displayCar != null) displayCar.Rotate(0f, turnDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            if (Input.GetKeyDown(KeyCode.Return)) Play();
        }

        void Play()
        {
            // -track "Track Royal Park Speedway" picks the circuit for one session, so the
            // benchmark always races the same one.
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-track");
            string scene = i >= 0 && i + 1 < args.Length ? args[i + 1] : ChosenScene;
            if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            yield return new WaitForSeconds(4f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        void OnGUI()
        {
            Styles();

            // Title, top left.
            float margin = Hud.Px(40f);
            GUI.Label(new Rect(margin, Hud.Px(28f), Hud.Px(700f), Hud.Px(80f)), "CAR RACE", _title);
            Hud.Fill(new Rect(margin, Hud.Px(104f), Hud.Px(120f), Hud.Px(5f)), Accent);
            Hud.Fill(new Rect(margin + Hud.Px(120f), Hud.Px(104f), Hud.Px(60f), Hud.Px(5f)), AccentLight);
            GUI.Label(new Rect(margin, Hud.Px(114f), Hud.Px(700f), Hud.Px(30f)), "LOBBY", _subtitle);

            // Players, left.
            var players = new Rect(margin, Hud.Px(180f), Hud.Px(400f), Hud.Px(230f));
            PanelWithHeader(players, "PLAYERS");
            var entry = new Rect(players.x + Hud.Px(14f), players.y + Hud.Px(52f), players.width - Hud.Px(28f), Hud.Px(48f));
            Hud.Rounded(entry, Row);
            Hud.Rounded(new Rect(entry.x + Hud.Px(10f), entry.y + Hud.Px(10f), Hud.Px(28f), Hud.Px(28f)), PlayerSetup.Colour);
            GUI.Label(new Rect(entry.x + Hud.Px(50f), entry.y, entry.width, entry.height), "Player 1", _text);
            _small.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(entry.x, entry.y, entry.width - Hud.Px(12f), entry.height), "YOU  ·  HOST", _small);
            _small.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(players.x + Hud.Px(16f), entry.yMax + Hud.Px(14f), players.width - Hud.Px(32f), Hud.Px(60f)),
                      "Local play. Players on your network will appear here once LAN racing is added.", _small);

            CircuitPanel(new Rect(margin, Hud.Px(440f), Hud.Px(400f), Hud.Px(560f)));

            // Car, right: the body designs, colour swatches, the colour's name and PLAY.
            int designs = CarDesigns.Count;
            float designRow = designs > 0 ? Hud.Px(62f) : 0f;
            float width = Hud.Px(460f), height = Hud.Px(380f) + designRow;
            var car = new Rect(Screen.width - margin - width, Screen.height - margin - height, width, height);
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
                    if (GUI.Button(button, GUIContent.none, GUIStyle.none))
                    {
                        PlayerSetup.DesignIndex = i;
                        CarDesigns.Apply(displayCar, i);
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
                if (GUI.Button(swatch, GUIContent.none, GUIStyle.none))
                {
                    PlayerSetup.ColourIndex = i;
                    PlayerSetup.Paint(displayCar != null ? displayCar.Find("Body") : null, PlayerSetup.Colour);
                }
            }
            _text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(car.x, top + 2f * size + gap + Hud.Px(8f), width, Hud.Px(30f)),
                      PlayerSetup.Colours[PlayerSetup.ColourIndex].name.ToUpperInvariant(), _text);
            _text.alignment = TextAnchor.MiddleLeft;

            var play = new Rect(car.x + Hud.Px(20f), car.yMax - Hud.Px(84f), width - Hud.Px(40f), Hud.Px(64f));
            bool over = play.Contains(Event.current.mousePosition);
            Hud.Rounded(play, over ? AccentLight : Accent);
            GUI.Label(play, "PLAY  ▶", _play);
            if (GUI.Button(play, GUIContent.none, GUIStyle.none)) Play();

            _small.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(car.x, car.yMax + Hud.Px(6f), width, Hud.Px(24f)), "Enter: play", _small);
            _small.alignment = TextAnchor.UpperLeft;
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

            string chosen = ChosenScene;
            for (int i = 0; i < _circuits.Count; i++)
            {
                var entry = _circuits[i];
                var card = new Rect(panel.x + pad + (i % 2) * (cardWidth + gap),
                                    panel.y + Hud.Px(52f) + (i / 2) * (cardHeight + gap), cardWidth, cardHeight);
                bool selected = entry.scene == chosen;
                bool hover = card.Contains(Event.current.mousePosition);
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

                if (GUI.Button(card, GUIContent.none, GUIStyle.none)) ChosenScene = entry.scene;
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
