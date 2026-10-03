using System.Collections.Generic;
using CarRace.Harness;
using CarRace.Net;
using CarRace.Vehicle;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The lobby's layout, a game menu over the garage: the title and three tabs across the top
    /// (RACE, GARAGE, MULTIPLAYER) with SETTINGS and QUIT; one glass panel on the left holding the
    /// chosen tab, which slides and fades in when the tab changes; the view buttons for the
    /// orbiting camera (LobbyCamera) at the bottom right; and along the foot a bar with the
    /// chosen circuit and car and PLAY.
    ///
    /// RACE: the chosen circuit large, its corners numbered as the track data numbers them, with arrows and
    /// a row of every circuit to choose from; its length, corners and your best lap there; then
    /// the race settings. GARAGE: the body, the paint (the palette, or any colour from hue,
    /// saturation and brightness sliders), the car's performance worked out from its physics
    /// with your setup for the chosen circuit, and SETUP. MULTIPLAYER: LAN play.
    ///
    /// H hides the menu, to look at the car; -lobbyTab N, -lobbyView front|side|rear|top and
    /// -lobbyHide set things up for screenshots.
    /// </summary>
    public sealed partial class LobbyMenu
    {
        enum Tab { Race, Garage, Multiplayer }
        static readonly string[] TabNames = { "RACE", "GARAGE", "MULTIPLAYER" };
        const float CarYaw = 345f;
        const float SlideSeconds = 0.3f;

        Tab _tab = Tab.Race;
        float _tabChangedAt = -10f;
        float _underlineX = -1f, _underlineWidth;
        bool _menuHidden;
        LobbyCamera _orbit;
        readonly Dictionary<int, float> _glow = new Dictionary<int, float>();
        GUIStyle _tabStyle, _section, _big, _caption, _value;

        // The circuit preview: a large outline, and where its corners are, per circuit.
        readonly Dictionary<string, Texture2D> _previews = new Dictionary<string, Texture2D>();
        int _previewPixels;

        // Performance: the figures, the bars as drawn (easing to the figures), and what they are for.
        sealed class Metric
        {
            public string Label, Unit, Format;
            public float Value, Built, Fraction;
            public bool Balance;       // a front to rear share, drawn as a marker on a scale
        }
        Metric[] _stats;
        float[] _statShown;
        string _statsFor;
        bool _wasSetupOpen;

        // Paint: hue, saturation and brightness, the slider being dragged, and its bars.
        float _hue, _saturation, _brightness;
        int _slider = -1;
        Texture2D _hueBar, _saturationBar, _brightnessBar;
        Vector3 _barsFor = new Vector3(-1f, -1f, -1f);

        void StartLayout(string[] args)
        {
            if (displayCar != null) displayCar.rotation = Quaternion.Euler(0f, CarYaw, 0f);
            SetupStore.Body = CarDesigns.NameOf(PlayerSetup.DesignIndex);
            if (Camera.main != null)
            {
                _orbit = Camera.main.gameObject.AddComponent<LobbyCamera>();
                _orbit.Begin(CarYaw);
            }
            Color.RGBToHSV(PlayerSetup.Colour, out _hue, out _saturation, out _brightness);

            if (int.TryParse(LanSession.Flag("-lobbyTab"), out int tab)) _tab = (Tab)Mathf.Clamp(tab, 0, TabNames.Length - 1);
            string view = LanSession.Flag("-lobbyView");
            if (_orbit != null && !string.IsNullOrEmpty(view) && System.Enum.TryParse(view, true, out LobbyCamera.View chosen))
                _orbit.Show(chosen);
            _menuHidden = System.Array.IndexOf(args, "-lobbyHide") >= 0;
        }

        void OnGUI()
        {
            Styles();
            _hover.Begin();
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint && _orbit != null) _orbit.BeginBlocks();
            if (_wasSetupOpen && !_setupOpen) _statsFor = null;   // back from the setup: new figures
            _wasSetupOpen = _setupOpen;
            if (_setupOpen)
            {
                Block(new Rect(0f, 0f, Screen.width, Screen.height));
                SetupScreen();
                return;
            }
            if (_menuHidden)
            {
                var show = new Rect((Screen.width - Hud.Px(260f)) * 0.5f, Screen.height - Hud.Px(80f), Hud.Px(260f), Hud.Px(44f));
                Block(show);
                if (Button(show, "SHOW MENU  ·  H", true, back: true)) _menuHidden = false;
                return;
            }

            float margin = Hud.Px(40f), barHeight = Hud.Px(100f), top = Hud.Px(112f);
            var bottom = new Rect(margin, Screen.height - margin - barHeight, Screen.width - 2f * margin, barHeight);
            var panel = new Rect(margin, top, Hud.Px(560f), bottom.y - Hud.Px(20f) - top);

            TopBar(margin);
            Glass(panel, GlassTint);
            Block(panel);

            // The tab's content slides in from the left and fades up when the tab changes.
            float t = Ease(Mathf.Clamp01((Time.unscaledTime - _tabChangedAt) / SlideSeconds));
            Color before = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, t);
            var area = new Rect(panel.x + Hud.Px(22f) - (1f - t) * Hud.Px(40f), panel.y + Hud.Px(18f),
                                panel.width - Hud.Px(44f), panel.height - Hud.Px(36f));
            switch (_tab)
            {
                case Tab.Race: RaceTab(area); break;
                case Tab.Garage: GarageTab(area); break;
                default: PlayersContent(area); break;
            }
            GUI.color = before;

            ViewButtons(bottom);
            BottomBar(bottom);
        }

        static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        void Block(Rect rect)
        {
            if (_orbit != null && Event.current.type == EventType.Repaint) _orbit.Block(rect);
        }

        /// <summary>How lit something under the mouse is, 0 to 1, easing up and down over about a
        /// tenth of a second, keyed by its label and row.</summary>
        float Glow(string key, Rect rect, bool on)
        {
            int id = key.GetHashCode() ^ Mathf.RoundToInt(rect.y / 8f);
            _glow.TryGetValue(id, out float glow);
            if (Event.current.type == EventType.Repaint)
            {
                glow = Mathf.MoveTowards(glow, on ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                _glow[id] = glow;
            }
            return glow;
        }

        // ------------------------------------------------------------------ top bar

        void TopBar(float margin)
        {
            Color titleColour = _title.normal.textColor;
            _title.fontSize = Hud.Font(44);
            _title.normal.textColor = new Color(0f, 0f, 0f, 0.45f);
            GUI.Label(new Rect(margin + Hud.Px(2f), Hud.Px(24f), Hud.Px(400f), Hud.Px(60f)), "CAR RACE", _title);
            _title.normal.textColor = titleColour;
            GUI.Label(new Rect(margin, Hud.Px(22f), Hud.Px(400f), Hud.Px(60f)), "CAR RACE", _title);
            Hud.Fill(new Rect(margin, Hud.Px(80f), Hud.Px(90f), Hud.Px(4f)), Accent);
            Hud.Fill(new Rect(margin + Hud.Px(90f), Hud.Px(80f), Hud.Px(44f), Hud.Px(4f)), AccentLight);

            // The tabs, on a glass strip, the chosen one lit with an accent bar sliding under it.
            float tabWidth = Hud.Px(170f), tabHeight = Hud.Px(46f), gap = Hud.Px(6f);
            var strip = new Rect(margin + Hud.Px(330f), Hud.Px(24f), TabNames.Length * tabWidth + (TabNames.Length + 1) * gap, tabHeight + 2f * gap);
            Glass(strip, GlassTint);
            Block(strip);
            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = new Rect(strip.x + gap + i * (tabWidth + gap), strip.y + gap, tabWidth, tabHeight);
                bool chosen = (int)_tab == i;
                bool over = tab.Contains(Event.current.mousePosition);
                float glow = Glow(TabNames[i], tab, over || chosen);
                Hud.Rounded(tab, new Color(1f, 1f, 1f, 0.03f + 0.1f * glow));
                _tabStyle.normal.textColor = chosen ? Color.white : new Color(1f, 1f, 1f, 0.65f + 0.35f * glow);
                GUI.Label(tab, TabNames[i], _tabStyle);
                if (chosen)
                {
                    if (_underlineX < 0f) { _underlineX = tab.x; _underlineWidth = tab.width; }
                    if (Event.current.type == EventType.Repaint)
                    {
                        float ease = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
                        _underlineX = Mathf.Lerp(_underlineX, tab.x, ease);
                        _underlineWidth = Mathf.Lerp(_underlineWidth, tab.width, ease);
                    }
                }
                _hover.Watch(tab);
                if (GUI.Button(tab, GUIContent.none, GUIStyle.none) && !chosen)
                {
                    GameAudio.Select();
                    _tab = (Tab)i;
                    _tabChangedAt = Time.unscaledTime;
                    GUIUtility.keyboardControl = 0;
                }
            }
            Hud.Fill(new Rect(_underlineX + Hud.Px(24f), strip.yMax - gap - Hud.Px(5f), _underlineWidth - Hud.Px(48f), Hud.Px(3f)), Accent);

            float right = Screen.width - margin;
            var quit = new Rect(right - Hud.Px(110f), strip.y + gap, Hud.Px(110f), tabHeight);
            var settings = new Rect(quit.x - Hud.Px(10f) - Hud.Px(160f), quit.y, Hud.Px(160f), tabHeight);
            Block(quit);
            Block(settings);
            Glass(quit, GlassTint);
            Glass(settings, GlassTint);
            if (Button(settings, "SETTINGS", true, back: true)) SettingsMenu.Open();
            if (Button(quit, "QUIT", true, back: true)) Application.Quit();
        }

        /// <summary>A section's title in a tab: an accent tick, the title, and a note on the right.</summary>
        void Section(Rect area, ref float y, string title, string note = null)
        {
            Hud.Fill(new Rect(area.x, y + Hud.Px(5f), Hud.Px(4f), Hud.Px(16f)), Accent);
            GUI.Label(new Rect(area.x + Hud.Px(12f), y, area.width, Hud.Px(26f)), title, _section);
            if (note != null)
            {
                _caption.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(area.x, y, area.width, Hud.Px(26f)), note, _caption);
                _caption.alignment = TextAnchor.MiddleLeft;
            }
            y += Hud.Px(34f);
        }

        // ------------------------------------------------------------------ RACE

        void RaceTab(Rect area)
        {
            float y = area.y, gap = Hud.Px(8f);
            bool lan = LanSession.Active;
            bool mayChoose = !lan || LanSession.Current.State == LanSession.Mode.Hosting;
            string chosen = lan && LanSession.Current.Lobby != null ? LanSession.Current.Lobby.Track : ChosenScene;
            int index = Mathf.Max(0, _circuits.FindIndex(c => c.scene == chosen));
            Section(area, ref y, "CIRCUIT", mayChoose ? $"{index + 1} OF {_circuits.Count}" : "THE HOST CHOOSES");
            if (_circuits.Count == 0)
            {
                GUI.Label(new Rect(area.x, y, area.width, Hud.Px(60f)), "No circuits in this build. Build one with CarRace, Build Track Scene.", _small);
                return;
            }
            TrackCatalog.Entry entry = _circuits[index];

            // The chosen circuit, large, its corners numbered, with arrows either side.
            var box = new Rect(area.x, y, area.width, Hud.Px(290f));
            Hud.Rounded(box, Row);
            Preview(entry, box);
            var theme = new Rect(box.x + Hud.Px(12f), box.y + Hud.Px(12f), _caption.CalcSize(new GUIContent(entry.theme.ToUpperInvariant())).x + Hud.Px(24f), Hud.Px(26f));
            Hud.Rounded(theme, new Color(1f, 1f, 1f, 0.12f));
            _caption.alignment = TextAnchor.MiddleCenter;
            GUI.Label(theme, entry.theme.ToUpperInvariant(), _caption);
            _caption.alignment = TextAnchor.MiddleLeft;
            if (mayChoose && _circuits.Count > 1)
            {
                float arrow = Hud.Px(44f);
                if (Button(new Rect(box.x + Hud.Px(10f), box.center.y - arrow / 2f, arrow, arrow), "◀", true, back: true))
                    Choose(_circuits[(index + _circuits.Count - 1) % _circuits.Count].scene);
                if (Button(new Rect(box.xMax - Hud.Px(10f) - arrow, box.center.y - arrow / 2f, arrow, arrow), "▶", true, back: true))
                    Choose(_circuits[(index + 1) % _circuits.Count].scene);
            }
            y = box.yMax + Hud.Px(10f);

            GUI.Label(new Rect(area.x, y, area.width, Hud.Px(36f)), entry.displayName, _big);
            y += Hud.Px(40f);

            // Facts: length, corners, your best lap there.
            float best = PlayerPrefs.GetFloat($"CarRace.BestLap.{entry.displayName}", -1f);
            var facts = new (string caption, string value)[]
            {
                ("LENGTH", $"{entry.lengthKm:0.0} km"),
                ("CORNERS", entry.corners != null && entry.corners.Length > 0 ? entry.corners.Length.ToString() : "-"),
                ("YOUR BEST", best > 0f ? $"{Mathf.FloorToInt(best / 60f)}:{best % 60f:00.000}" : "NO LAP YET"),
            };
            float factWidth = (area.width - 2f * gap) / 3f;
            for (int i = 0; i < facts.Length; i++)
            {
                var fact = new Rect(area.x + i * (factWidth + gap), y, factWidth, Hud.Px(58f));
                Hud.Rounded(fact, Row);
                GUI.Label(new Rect(fact.x + Hud.Px(12f), fact.y + Hud.Px(6f), fact.width, Hud.Px(18f)), facts[i].caption, _caption);
                GUI.Label(new Rect(fact.x + Hud.Px(12f), fact.y + Hud.Px(24f), fact.width, Hud.Px(28f)), facts[i].value, _value);
            }
            y += Hud.Px(70f);

            // Every circuit, small, to jump to.
            float thumbWidth = (area.width - (_circuits.Count - 1) * gap) / Mathf.Max(1, _circuits.Count);
            int pixels = Mathf.RoundToInt(Hud.Px(52f));
            if (pixels != _outlinePixels)
            {
                foreach (var texture in _outlines.Values) Destroy(texture);
                _outlines.Clear();
                _outlinePixels = pixels;
            }
            for (int i = 0; i < _circuits.Count; i++)
            {
                var thumb = new Rect(area.x + i * (thumbWidth + gap), y, thumbWidth, Hud.Px(64f));
                bool selected = i == index;
                bool over = mayChoose && thumb.Contains(Event.current.mousePosition);
                float glow = Glow("thumb" + i, thumb, over);
                Hud.Rounded(thumb, selected ? new Color(Accent.r, Accent.g, Accent.b, 0.35f) : new Color(1f, 1f, 1f, 0.08f + 0.1f * glow));
                if (selected) Hud.Fill(new Rect(thumb.x + Hud.Px(10f), thumb.yMax - Hud.Px(5f), thumb.width - Hud.Px(20f), Hud.Px(3f)), Accent);
                if (!_outlines.TryGetValue(_circuits[i].scene, out Texture2D outline))
                    _outlines[_circuits[i].scene] = outline = OutlineTexture(_circuits[i].outline, pixels);
                GUI.DrawTexture(new Rect(thumb.center.x - pixels * 0.5f, thumb.center.y - pixels * 0.5f, pixels, pixels), outline);
                if (!mayChoose) continue;
                _hover.Watch(thumb);
                if (GUI.Button(thumb, GUIContent.none, GUIStyle.none)) Choose(_circuits[i].scene);
            }
            y += Hud.Px(82f);

            if (lan)
            {
                Section(area, ref y, "RACE SETTINGS");
                GUI.Label(new Rect(area.x, y, area.width, Hud.Px(44f)), "In a LAN game the host sets the laps and AI cars, under MULTIPLAYER.", _small);
                return;
            }
            Section(area, ref y, "RACE SETTINGS");
            SettingRow(area.x, ref y, area.width, "LAPS", RaceSettings.Laps.ToString(),
                       RaceSettings.Laps > 1, RaceSettings.Laps < RaceSettings.MaxLaps, step => RaceSettings.Laps += step);
            SettingRow(area.x, ref y, area.width, "TYRE WEAR", RaceSettings.WearLabel(RaceSettings.WearChoice),
                       RaceSettings.WearChoice > 0, RaceSettings.WearChoice < RaceSettings.WearRates.Length - 1,
                       step => RaceSettings.WearChoice += step);
            SettingRow(area.x, ref y, area.width, "SAFETY CAR", RaceSettings.SafetyCarChoice ? "ON" : "OFF",
                       RaceSettings.SafetyCarChoice, !RaceSettings.SafetyCarChoice,
                       step => RaceSettings.SafetyCarChoice = step > 0);
        }

        void Choose(string scene)
        {
            GameAudio.Select();
            ChosenScene = scene;
            if (LanSession.Active) LanSession.Current.Host?.SetTrack(scene);
            _statsFor = null;
        }

        /// <summary>The circuit drawn large in <paramref name="box"/>, a dot on the start line, and
        /// each corner's number beside it, from the track data (TrackCatalog.Entry.corners).</summary>
        void Preview(TrackCatalog.Entry entry, Rect box)
        {
            int pixels = Mathf.RoundToInt(Mathf.Min(box.height - Hud.Px(30f), box.width - Hud.Px(140f)));
            if (pixels != _previewPixels)
            {
                foreach (var texture in _previews.Values) Destroy(texture);
                _previews.Clear();
                _previewPixels = pixels;
            }
            if (!_previews.TryGetValue(entry.scene, out Texture2D preview))
                _previews[entry.scene] = preview = OutlineTexture(entry.outline, pixels);
            var at = new Rect(box.center.x - pixels * 0.5f, box.center.y - pixels * 0.5f + Hud.Px(6f), pixels, pixels);
            GUI.DrawTexture(at, preview);

            // Where a point of the outline is drawn: OutlineTexture's margin and scale, with the
            // texture's rows counted up from its foot.
            float margin = pixels * 0.08f, scale = pixels - 2f * margin;
            Vector2 OnScreen(Vector2 p) => new Vector2(at.x + margin + p.x * scale, at.yMax - margin - p.y * scale);
            if (entry.outline.Length > 0)
            {
                Vector2 start = OnScreen(entry.outline[0]);
                float dot = Hud.Px(12f);
                Hud.Rounded(new Rect(start.x - dot / 2f, start.y - dot / 2f, dot, dot), Accent);
            }
            float badge = Hud.Px(entry.corners != null && entry.corners.Length > 16 ? 17f : 20f);
            _caption.alignment = TextAnchor.MiddleCenter;
            Color captionColour = _caption.normal.textColor;
            _caption.normal.textColor = Color.white;
            for (int k = 0; entry.corners != null && k < entry.corners.Length; k++)
            {
                Vector2 c = OnScreen(entry.corners[k]);
                var r = new Rect(c.x - badge / 2f, c.y - badge / 2f, badge, badge);
                Hud.Rounded(r, new Color(0.05f, 0.06f, 0.1f, 0.85f));
                GUI.Label(r, (k + 1).ToString(), _caption);
            }
            _caption.normal.textColor = captionColour;
            _caption.alignment = TextAnchor.MiddleLeft;
        }

        // ------------------------------------------------------------------ GARAGE

        void GarageTab(Rect area)
        {
            float y = area.y, gap = Hud.Px(8f);
            bool locked = LanSession.Active && LanSession.Current.IsReadyToRace;

            // Body.
            Section(area, ref y, "BODY", locked ? "LOCKED WHILE READY" : null);
            int designs = CarDesigns.Count;
            float pill = (area.width - (designs - 1) * gap) / Mathf.Max(1, designs);
            for (int i = 0; i < designs; i++)
            {
                var button = new Rect(area.x + i * (pill + gap), y, pill, Hud.Px(46f));
                bool chosen = i == PlayerSetup.DesignIndex;
                bool over = !locked && button.Contains(Event.current.mousePosition);
                float glow = Glow("design" + i, button, over);
                Hud.Rounded(button, chosen ? Accent : new Color(1f, 1f, 1f, 0.08f + 0.12f * glow));
                _cardName.normal.textColor = chosen ? Color.white : new Color(1f, 1f, 1f, 0.75f + 0.25f * glow);
                GUI.Label(button, CarDesigns.NameOf(i).ToUpperInvariant(), _cardName);
                if (locked) continue;
                _hover.Watch(button);
                if (GUI.Button(button, GUIContent.none, GUIStyle.none) && !chosen)
                {
                    GameAudio.Select();
                    PlayerSetup.DesignIndex = i;
                    CarDesigns.Apply(displayCar, i);
                    _statsFor = null;
                    if (LanSession.Active) LanSession.Current.SendSetup();
                }
            }
            y += Hud.Px(62f);

            // Paint: the palette, then any colour.
            Section(area, ref y, "PAINT", PlayerSetup.ColourName.ToUpperInvariant());
            int count = PlayerSetup.Colours.Length;
            float size = (area.width - (count - 1) * gap) / count;
            bool custom = PlayerSetup.Custom.HasValue;
            for (int i = 0; i < count; i++)
            {
                var swatch = new Rect(area.x + i * (size + gap), y, size, size);
                bool chosen = !custom && i == PlayerSetup.ColourIndex;
                bool over = !locked && swatch.Contains(Event.current.mousePosition);
                float glow = Glow("swatch" + i, swatch, over || chosen);
                float ring = Hud.Px(1.5f + 2.5f * glow);
                Hud.Rounded(new Rect(swatch.x - ring, swatch.y - ring, swatch.width + 2f * ring, swatch.height + 2f * ring),
                            new Color(1f, 1f, 1f, chosen ? 1f : 0.2f + 0.4f * glow));
                Hud.Rounded(swatch, PlayerSetup.Colours[i].colour);
                if (locked) continue;
                _hover.Watch(swatch);
                if (GUI.Button(swatch, GUIContent.none, GUIStyle.none))
                {
                    GameAudio.Select();
                    PlayerSetup.ColourIndex = i;
                    Color.RGBToHSV(PlayerSetup.Colour, out _hue, out _saturation, out _brightness);
                    Repaint();
                }
            }
            y += size + Hud.Px(14f);

            Bars();
            bool changed = false;
            changed |= PaintSlider(area, ref y, "HUE", ref _hue, 0, _hueBar, locked);
            changed |= PaintSlider(area, ref y, "SATURATION", ref _saturation, 1, _saturationBar, locked);
            changed |= PaintSlider(area, ref y, "BRIGHTNESS", ref _brightness, 2, _brightnessBar, locked);
            if (changed)
            {
                PlayerSetup.Custom = Color.HSVToRGB(_hue, _saturation, _brightness);
                PlayerSetup.Paint(displayCar != null ? displayCar.Find("Body") : null, PlayerSetup.Colour);
            }
            y += Hud.Px(8f);

            // Performance, from the physics with this circuit's setup.
            Section(area, ref y, "PERFORMANCE", $"YOUR SETUP FOR {CircuitName(SetupScene).ToUpperInvariant()}");
            Stats();
            foreach (int k in GarageMetrics) MetricRow(area, ref y, k);
            y += Hud.Px(4f);
            if (Button(new Rect(area.x, y, area.width, Hud.Px(46f)), "TUNE THE SETUP  ▸", !locked && car != null && SetupScene.Length > 0, back: true))
                OpenSetup();
        }

        void Repaint()
        {
            PlayerSetup.Paint(displayCar != null ? displayCar.Find("Body") : null, PlayerSetup.Colour);
            if (LanSession.Active) LanSession.Current.SendSetup();
        }

        /// <summary>One of the paint sliders: a caption, a bar showing the colours it runs through,
        /// and a knob; pressed or dragged anywhere on it, it moves. Returns whether it changed,
        /// and on letting go sends a LAN game the nearest palette colour.</summary>
        bool PaintSlider(Rect area, ref float y, string caption, ref float value, int id, Texture2D bar, bool locked)
        {
            float labelWidth = Hud.Px(110f);
            GUI.Label(new Rect(area.x, y, labelWidth, Hud.Px(22f)), caption, _caption);
            var rect = new Rect(area.x + labelWidth, y + Hud.Px(4f), area.width - labelWidth - Hud.Px(10f), Hud.Px(14f));
            if (bar != null) GUI.DrawTexture(rect, bar);
            float knob = Hud.Px(20f);
            var at = new Rect(rect.x + value * rect.width - knob / 2f, rect.center.y - knob / 2f, knob, knob);
            Hud.Rounded(at, Color.white);
            Hud.Rounded(new Rect(at.x + Hud.Px(4f), at.y + Hud.Px(4f), knob - Hud.Px(8f), knob - Hud.Px(8f)),
                        Color.HSVToRGB(_hue, _saturation, _brightness));
            y += Hud.Px(30f);
            if (locked) return false;

            Event e = Event.current;
            var grab = new Rect(rect.x - knob / 2f, rect.y - Hud.Px(8f), rect.width + knob, rect.height + Hud.Px(16f));
            bool changed = false;
            if (e.type == EventType.MouseDown && e.button == 0 && grab.Contains(e.mousePosition)) _slider = id;
            if (_slider == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                value = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
                changed = true;
                e.Use();
            }
            if (_slider == id && e.type == EventType.MouseUp)
            {
                _slider = -1;
                GameAudio.Select();
                if (LanSession.Active) LanSession.Current.SendSetup();
                e.Use();
            }
            return changed;
        }

        /// <summary>The sliders' bars, remade when the colour changes: hue round the wheel, and
        /// saturation and brightness through the colour as it is.</summary>
        void Bars()
        {
            var key = new Vector3(_hue, _saturation, _brightness);
            if (key == _barsFor && _hueBar != null) return;
            _barsFor = key;
            const int W = 128;
            Texture2D Make(ref Texture2D t)
            {
                if (t == null) t = new Texture2D(W, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                return t;
            }
            var hue = new Color[W];
            var saturation = new Color[W];
            var brightness = new Color[W];
            for (int i = 0; i < W; i++)
            {
                float f = i / (W - 1f);
                hue[i] = Color.HSVToRGB(f, 1f, 1f);
                saturation[i] = Color.HSVToRGB(_hue, f, Mathf.Max(_brightness, 0.4f));
                brightness[i] = Color.HSVToRGB(_hue, _saturation, f);
            }
            Make(ref _hueBar).SetPixels(hue);
            _hueBar.Apply();
            Make(ref _saturationBar).SetPixels(saturation);
            _saturationBar.Apply();
            Make(ref _brightnessBar).SetPixels(brightness);
            _brightnessBar.Apply();
        }

        /// <summary>The figures the GARAGE tab shows; the setup screen's panel shows them all.</summary>
        static readonly int[] GarageMetrics = { 0, 1, 2, 3, 5, 6 };

        /// <summary>The car's figures with the chosen circuit's setup, and as built, worked out again
        /// whenever the setup changes or the circuit does: the closed-form numbers the harness
        /// checks the physics against (Analytic), and CarSetup's own readouts.</summary>
        void Stats()
        {
            string key = SetupScene;
            if (_stats != null && _statsFor == key) return;
            _statsFor = key;
            CarConfig built = SetupStore.Baseline(car != null ? car.ToConfig() : CarConfig.ReferenceSportsCar());
            CarConfig c = car != null ? SetupStore.Load(SetupScene, built).Apply() : built;
            float Top(CarConfig x) => Mathf.Min(CarSetup.GearedTopSpeedKph(x), Analytic.TopSpeedKph(x));
            float Downforce(CarConfig x) => (x.LiftFrontClA + x.LiftRearClA) * CarSetup.KgAt200;
            float Roll(CarConfig x)
            {
                float front = x.SpringRateFront * 0.5f + x.AntiRollFront, rear = x.SpringRateRear * 0.5f + x.AntiRollRear;
                return front / (front + rear);
            }
            Metric Bar(string label, float value, float builtValue, string unit, string format, float fraction) =>
                new Metric { Label = label, Value = value, Built = builtValue, Unit = unit, Format = format, Fraction = Mathf.Clamp01(fraction) };
            Metric Share(string label, float share, float builtShare) =>
                new Metric { Label = label, Value = share * 100f, Built = builtShare * 100f, Unit = "% front", Format = "0", Fraction = share, Balance = true };
            float sprint = Analytic.ZeroToHundredSeconds(c);
            _stats = new[]
            {
                Bar("POWER", Analytic.PeakPowerWatts(c) / 1000f, Analytic.PeakPowerWatts(built) / 1000f, "kW", "0", Analytic.PeakPowerWatts(c) / 450000f),
                Bar("WEIGHT", c.Mass, built.Mass, "kg", "0", c.Mass / 2000f),
                Bar("TOP SPEED", Top(c), Top(built), "km/h", "0", Top(c) / 360f),
                Bar("0 TO 100 KM/H", sprint, Analytic.ZeroToHundredSeconds(built), "s", "0.00", (9f - sprint) / 7f),
                Bar("DOWNFORCE AT 200 KM/H", Downforce(c), Downforce(built), "kg", "0", Downforce(c) / 400f),
                Bar("CORNERING", Analytic.SkidpadCeilingG(c), Analytic.SkidpadCeilingG(built), "g", "0.00", Analytic.SkidpadCeilingG(c) / 1.6f),
                Bar("TURNING RADIUS", CarSetup.TurningRadiusM(c), CarSetup.TurningRadiusM(built), "m", "0.0", (7f - CarSetup.TurningRadiusM(c)) / 4f),
                Share("AERO BALANCE", CarSetup.AeroBalanceFront(c), CarSetup.AeroBalanceFront(built)),
                Share("ROLL BALANCE", Roll(c), Roll(built)),
                Share("BRAKE BIAS", c.BrakeBias, built.BrakeBias),
            };
            if (_statShown == null || _statShown.Length != _stats.Length) _statShown = new float[_stats.Length];
        }

        /// <summary>
        /// One figure: its name, its value (orange, with the value as built beside it, once the
        /// setup has moved it), and under them a bar that fills to it, or for a balance a marker
        /// on a front to rear scale with the middle marked. Bars and markers ease to new values.
        /// </summary>
        void MetricRow(Rect area, ref float y, int index)
        {
            Metric m = _stats[index];
            if (Event.current.type == EventType.Repaint)
                _statShown[index] = Mathf.Lerp(_statShown[index], m.Fraction, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            bool changed = Mathf.Abs(m.Value - m.Built) > 0.0005f * Mathf.Max(1f, Mathf.Abs(m.Built));
            GUI.Label(new Rect(area.x, y, area.width, Hud.Px(22f)), m.Label, _caption);
            _value.alignment = TextAnchor.MiddleRight;
            _value.normal.textColor = changed ? AccentLight : Color.white;
            GUI.Label(new Rect(area.x, y - Hud.Px(2f), area.width, Hud.Px(24f)), m.Value.ToString(m.Format) + " " + m.Unit, _value);
            _value.normal.textColor = Color.white;
            _value.alignment = TextAnchor.MiddleLeft;
            if (changed)
            {
                float width = _value.CalcSize(new GUIContent(m.Value.ToString(m.Format) + " " + m.Unit)).x;
                _caption.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(area.x, y, area.width - width - Hud.Px(12f), Hud.Px(22f)), "BUILT " + m.Built.ToString(m.Format), _caption);
                _caption.alignment = TextAnchor.MiddleLeft;
            }
            var track = new Rect(area.x, y + Hud.Px(25f), area.width, Hud.Px(6f));
            Hud.Fill(track, new Color(1f, 1f, 1f, 0.1f));
            float shown = Mathf.Clamp01(_statShown[index]);
            if (m.Balance)
            {
                Hud.Fill(new Rect(track.center.x - Hud.Px(1f), track.y - Hud.Px(3f), Hud.Px(2f), track.height + Hud.Px(6f)), new Color(1f, 1f, 1f, 0.4f));
                // Front on the left, as the share counts it.
                float at = track.x + track.width * (1f - shown);
                Hud.Fill(new Rect(at - Hud.Px(3f), track.y - Hud.Px(4f), Hud.Px(6f), track.height + Hud.Px(8f)), changed ? AccentLight : Color.white);
            }
            else Hud.Fill(new Rect(track.x, track.y, track.width * shown, track.height), Color.Lerp(AccentLight, Accent, shown));
            y += Hud.Px(42f);
        }

        // ------------------------------------------------------------------ the camera's views

        void ViewButtons(Rect bottom)
        {
            if (_orbit == null) return;
            string[] names = { "FRONT", "SIDE", "REAR", "TOP" };
            float w = Hud.Px(92f), h = Hud.Px(38f), gap = Hud.Px(6f);
            float hideWidth = Hud.Px(150f);
            float total = names.Length * (w + gap) + hideWidth + 2f * gap;
            var strip = new Rect(bottom.xMax - total, bottom.y - Hud.Px(16f) - h - 2f * gap, total, h + 2f * gap);
            GUI.Label(new Rect(strip.x, strip.y - Hud.Px(26f), strip.width, Hud.Px(22f)), "DRAG TO ORBIT  ·  SCROLL TO ZOOM", _caption);
            Glass(strip, GlassTint);
            Block(strip);
            for (int i = 0; i < names.Length; i++)
            {
                var b = new Rect(strip.x + gap + i * (w + gap), strip.y + gap, w, h);
                if (Button(b, names[i], true, back: true)) _orbit.Show((LobbyCamera.View)i);
            }
            if (Button(new Rect(strip.xMax - gap - hideWidth, strip.y + gap, hideWidth, h), "HIDE MENU  H", true, back: true))
                _menuHidden = true;
        }

        // ------------------------------------------------------------------ the foot

        void BottomBar(Rect bar)
        {
            Glass(bar, GlassTint);
            Block(bar);
            float pad = Hud.Px(18f);

            // The circuit.
            bool lan = LanSession.Active;
            string scene = lan && LanSession.Current.Lobby != null ? LanSession.Current.Lobby.Track : ChosenScene;
            TrackCatalog.Entry entry = _circuits.Find(c => c.scene == scene);
            float x = bar.x + pad;
            if (entry != null)
            {
                float icon = bar.height - 2f * pad;
                Hud.Rounded(new Rect(x, bar.y + pad, icon, icon), Row);
                Texture2D outline = Icon(entry, Mathf.RoundToInt(icon * 0.9f));
                GUI.DrawTexture(new Rect(x + (icon - outline.width) * 0.5f, bar.y + pad + (icon - outline.height) * 0.5f, outline.width, outline.height), outline);
                x += icon + Hud.Px(14f);
                int laps = lan && LanSession.Current.Lobby != null ? LanSession.Current.Lobby.Laps : RaceSettings.Laps;
                GUI.Label(new Rect(x, bar.y + Hud.Px(16f), Hud.Px(420f), Hud.Px(34f)), entry.displayName, _big);
                GUI.Label(new Rect(x, bar.y + Hud.Px(52f), Hud.Px(420f), Hud.Px(24f)),
                          $"{laps} {(laps == 1 ? "LAP" : "LAPS")}  ·  {entry.lengthKm:0.0} KM  ·  {entry.theme.ToUpperInvariant()}", _caption);
            }

            // The car.
            float carX = bar.x + Mathf.Max(Hud.Px(560f), bar.width * 0.36f);
            float dot = Hud.Px(40f);
            Hud.Rounded(new Rect(carX - Hud.Px(2f), bar.center.y - dot / 2f - Hud.Px(2f), dot + Hud.Px(4f), dot + Hud.Px(4f)), new Color(1f, 1f, 1f, 0.5f));
            Hud.Rounded(new Rect(carX, bar.center.y - dot / 2f, dot, dot), PlayerSetup.Colour);
            string body = CarDesigns.Count > 0 ? CarDesigns.NameOf(PlayerSetup.DesignIndex) : "Car";
            GUI.Label(new Rect(carX + dot + Hud.Px(14f), bar.y + Hud.Px(16f), Hud.Px(360f), Hud.Px(34f)), body, _big);
            GUI.Label(new Rect(carX + dot + Hud.Px(14f), bar.y + Hud.Px(52f), Hud.Px(360f), Hud.Px(24f)),
                      PlayerSetup.ColourName.ToUpperInvariant(), _caption);

            // PLAY.
            var play = new Rect(bar.xMax - pad - Hud.Px(380f), bar.y + Hud.Px(14f), Hud.Px(380f), bar.height - Hud.Px(28f));
            PlayButton(play);
        }

        readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();

        /// <summary>A circuit's outline for the foot bar, at <paramref name="pixels"/>.</summary>
        Texture2D Icon(TrackCatalog.Entry entry, int pixels)
        {
            if (_icons.TryGetValue(entry.scene, out Texture2D icon) && icon.width == pixels) return icon;
            if (icon != null) Destroy(icon);
            return _icons[entry.scene] = OutlineTexture(entry.outline, pixels);
        }

        /// <summary>PLAY, or in a LAN game READY or START RACE; it breathes while it can be pressed.</summary>
        void PlayButton(Rect play)
        {
            string label = "PLAY  ▶", hint = "ENTER";
            bool ready = true, waiting = false;
            if (LanSession.Active)
            {
                LanSession session = LanSession.Current;
                LobbyState lobby = session.Lobby;
                if (session.State == LanSession.Mode.Joined)
                {
                    ready = session.Client.Id != 0;
                    waiting = session.IsReadyToRace;
                    label = waiting ? "READY  ✓  WAITING" : "READY";
                }
                else if (session.State == LanSession.Mode.Browsing) { label = "JOIN A GAME FIRST"; ready = false; }
                else if (session.Host.Humans < 2) { label = "WAITING FOR PLAYERS"; ready = false; }
                else if (!session.Host.AllReady)
                {
                    int count = lobby.Players.FindAll(p => p.Ready).Count;
                    label = $"{count} OF {lobby.Players.Count} READY";
                    ready = false;
                }
                else label = "START RACE  ▶";
            }
            bool over = ready && play.Contains(Event.current.mousePosition);
            float glow = Glow("play", play, over);
            float breathe = ready && !waiting ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f;
            if (ready && !waiting)
            {
                float halo = Hud.Px(3f + 5f * Mathf.Max(glow, breathe * 0.6f));
                Hud.Rounded(new Rect(play.x - halo, play.y - halo, play.width + 2f * halo, play.height + 2f * halo),
                            new Color(Accent.r, Accent.g, Accent.b, 0.25f + 0.2f * glow));
            }
            Color fill = !ready ? Row : waiting ? Color.Lerp(ReadyColour, ReadyLight, glow) : Color.Lerp(Accent, AccentLight, glow);
            Hud.Rounded(play, fill);
            _play.fontSize = Hud.Font(ready && !waiting ? 30 : 20);
            GUI.Label(play, label, _play);
            if (ready)
            {
                _caption.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(play.x - Hud.Px(200f), play.y, Hud.Px(186f), play.height), hint, _caption);
                _caption.alignment = TextAnchor.MiddleLeft;
                _hover.Watch(play);
                if (GUI.Button(play, GUIContent.none, GUIStyle.none)) Play();
            }
        }

        void LayoutStyles()
        {
            _tabStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter };
            _section ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _big ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _caption ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.78f, 0.8f, 0.86f) } };
            _value ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _tabStyle.fontSize = Hud.Font(18);
            _section.fontSize = Hud.Font(17);
            _big.fontSize = Hud.Font(24);
            _caption.fontSize = Hud.Font(13);
            _value.fontSize = Hud.Font(19);
        }
    }
}
