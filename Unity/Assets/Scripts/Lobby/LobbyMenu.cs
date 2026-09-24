using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The front end: the game's title, a players panel, and a car panel with colour swatches
    /// and a PLAY button that loads the race. The car turns on a platform behind the panels,
    /// repainted as a colour is picked. The players panel lists only the local player for now;
    /// it is where LAN play will list everyone who has joined before the host starts the race.
    ///
    /// With -benchmark on the command line it goes straight to the race, so the benchmark
    /// still measures racing.
    /// </summary>
    public sealed class LobbyMenu : MonoBehaviour
    {
        [SerializeField] Transform displayCar;
        [SerializeField] string raceScene = "Track Royal Park Speedway";
        [SerializeField] float turnDegreesPerSecond = 18f;

        static readonly Color Panel = new Color(0.05f, 0.06f, 0.1f, 0.86f);
        static readonly Color Accent = new Color(0.9f, 0.12f, 0.1f);
        static readonly Color AccentLight = new Color(1f, 0.55f, 0.1f);
        static readonly Color Muted = new Color(0.62f, 0.64f, 0.72f);
        static readonly Color Row = new Color(0.12f, 0.13f, 0.18f, 0.95f);

        GUIStyle _title, _subtitle, _header, _text, _small, _play;

        void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") >= 0) { Play(); return; }
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

        void Play() => SceneManager.LoadScene(raceScene);

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

            // Car, right: colour swatches, the colour's name and PLAY.
            float width = Hud.Px(460f), height = Hud.Px(380f);
            var car = new Rect(Screen.width - margin - width, Screen.height - margin - height, width, height);
            PanelWithHeader(car, "YOUR CAR");
            float size = Hud.Px(80f), gap = Hud.Px(20f);
            float left = car.x + (width - (4f * size + 3f * gap)) * 0.5f, top = car.y + Hud.Px(56f);
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
            _title.fontSize = Hud.Font(72);
            _subtitle.fontSize = Hud.Font(20);
            _header.fontSize = Hud.Font(17);
            _text.fontSize = Hud.Font(18);
            _small.fontSize = Hud.Font(14);
            _play.fontSize = Hud.Font(30);
        }
    }
}
