using CarRace.Harness;
using CarRace.Vehicle;
using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The setup screen, opened by SETUP in the YOUR CAR panel: the chosen circuit's setup, a tab
    /// for each group of CarSetup's settings and one for the assists. Each setting is a row with
    /// its value and unit, minus and plus, and its value in orange once it differs from the car
    /// as built; the row under the mouse explains itself at the foot of the panel. Every press
    /// saves at once (SetupStore), so DONE only closes. Esc is left alone: it opens the settings
    /// menu, in the lobby as everywhere.
    /// </summary>
    public sealed partial class LobbyMenu
    {
        static readonly string[] SetupTabs = { "SUSPENSION", "BRAKES", "GEARBOX", "DIFFERENTIAL", "AERO", "ASSISTS" };
        const int AssistsTab = 5;

        static readonly (SetupStore.Assist assist, string label, string hint)[] Assists =
        {
            (SetupStore.Assist.Abs, "ABS",
             "Eases the brake off a wheel about to lock, so a full pedal never locks the tyres."),
            (SetupStore.Assist.TractionControl, "Traction control",
             "Cuts power to a driven wheel that spins, so full throttle never lights up the rear tyres."),
            (SetupStore.Assist.EngineBrakingControl, "Engine braking control",
             "Eases engine braking off a rear tyre near its limit, so braking into a corner does not push the rear out."),
            (SetupStore.Assist.AutomaticGearbox, "Automatic gearbox",
             "Off: change gear with E and Q, or the bumpers; reverse is Q from first. On: holding the brake at a standstill selects reverse."),
        };

        bool _setupOpen;
        int _setupTab;
        CarSetup _setup;
        string _setupHint;

        /// <summary>The circuit a setup is for: in a LAN game the lobby's, otherwise the chosen one.</summary>
        string SetupScene => LanSession.Active && LanSession.Current.Lobby != null ? LanSession.Current.Lobby.Track : ChosenScene;

        void OpenSetup()
        {
            _setup = SetupStore.Load(SetupScene, car.ToConfig());
            _setupOpen = true;
        }

        void SetupScreen()
        {
            // While READY in a LAN lobby the setup is locked, as the car is.
            if (LanSession.Active && LanSession.Current.IsReadyToRace) { _setupOpen = false; return; }

            Hud.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            // The setup, and beside it the car's figures, live: each press shows what it did.
            float side = Hud.Px(400f), between = Hud.Px(16f);
            float width = Mathf.Min(Hud.Px(940f), Screen.width - Hud.Px(40f) - side - between);
            float height = Mathf.Min(Hud.Px(700f), Screen.height - Hud.Px(40f));
            float left = (Screen.width - width - between - side) * 0.5f;
            var panel = new Rect(left, (Screen.height - height) * 0.5f, width, height);
            PanelWithHeader(panel, $"SETUP  ·  {CircuitName(SetupScene).ToUpperInvariant()}");
            var figures = new Rect(panel.xMax + between, panel.y, side, height);
            PanelWithHeader(figures, "PERFORMANCE");
            Stats();
            var inside = new Rect(figures.x + Hud.Px(18f), figures.y + Hud.Px(58f), figures.width - Hud.Px(36f), figures.height - Hud.Px(70f));
            float fy = inside.y;
            for (int k = 0; k < _stats.Length; k++)
            {
                if (k == 6) fy += Hud.Px(10f);   // the balances, apart from the figures
                MetricRow(inside, ref fy, k);
            }
            _small.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(inside.x, fy + Hud.Px(2f), inside.width, Hud.Px(44f)),
                      "Orange: changed from the car as built. Balances read front on the left.", _small);

            float pad = Hud.Px(16f), gap = Hud.Px(6f);
            float x = panel.x + pad, inner = panel.width - 2f * pad, y = panel.y + Hud.Px(52f);

            // Tabs.
            float tabWidth = (inner - (SetupTabs.Length - 1) * gap) / SetupTabs.Length;
            for (int t = 0; t < SetupTabs.Length; t++)
            {
                var tab = new Rect(x + t * (tabWidth + gap), y, tabWidth, Hud.Px(38f));
                bool chosen = t == _setupTab;
                bool hover = tab.Contains(Event.current.mousePosition);
                Hud.Rounded(tab, chosen ? Accent : hover ? new Color(1f, 1f, 1f, 0.16f) : Row);
                _cardName.normal.textColor = chosen ? Color.white : new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(tab, SetupTabs[t], _cardName);
                _hover.Watch(tab);
                if (!chosen && GUI.Button(tab, GUIContent.none, GUIStyle.none)) { GameAudio.Select(); _setupTab = t; }
            }
            y += Hud.Px(52f);

            float footer = Hud.Px(110f);
            float rowHeight = Hud.Px(40f);
            string hovered = null;
            if (_setupTab == AssistsTab)
            {
                foreach (var (assist, label, hint) in Assists)
                {
                    var row = new Rect(x, y, inner, rowHeight);
                    if (row.Contains(Event.current.mousePosition)) hovered = hint;
                    Hud.Rounded(row, Row);
                    GUI.Label(new Rect(row.x + Hud.Px(12f), row.y, row.width * 0.6f, row.height), label, _text);
                    bool on = SetupStore.Get(assist);
                    if (Button(new Rect(row.xMax - Hud.Px(96f), row.y + Hud.Px(5f), Hud.Px(88f), row.height - Hud.Px(10f)),
                               on ? "ON" : "OFF", true, back: !on))
                        SetupStore.Set(assist, !on);
                    y += rowHeight + gap;
                }
            }
            else
            {
                var group = (CarSetup.Group)_setupTab;
                foreach (CarSetup.Setting s in _setup.Settings)
                {
                    if (s.Group != group) continue;
                    var row = new Rect(x, y, inner, rowHeight);
                    if (row.Contains(Event.current.mousePosition)) hovered = s.Hint;
                    Hud.Rounded(row, Row);
                    GUI.Label(new Rect(row.x + Hud.Px(12f), row.y, row.width * 0.45f, row.height), s.Label, _text);

                    float value = _setup.Get(s);
                    bool changed = !_setup.IsDefault(s);
                    _text.alignment = TextAnchor.MiddleRight;
                    _text.normal.textColor = changed ? AccentLight : Color.white;
                    GUI.Label(new Rect(row.x, row.y, row.width - Hud.Px(150f), row.height),
                              $"{s.Format(value)} {s.Unit}".Trim(), _text);
                    _text.normal.textColor = Color.white;
                    _text.alignment = TextAnchor.MiddleLeft;
                    if (changed)
                    {
                        _small.alignment = TextAnchor.MiddleLeft;
                        GUI.Label(new Rect(row.x + row.width * 0.45f, row.y, row.width * 0.25f, row.height),
                                  $"built {s.Format(_setup.Default(s))}", _small);
                        _small.alignment = TextAnchor.UpperLeft;
                    }

                    var minus = new Rect(row.xMax - Hud.Px(132f), row.y + Hud.Px(5f), Hud.Px(58f), row.height - Hud.Px(10f));
                    var plus = new Rect(row.xMax - Hud.Px(66f), row.y + Hud.Px(5f), Hud.Px(58f), row.height - Hud.Px(10f));
                    if (Button(minus, "−", value > _setup.Min(s))) Nudge(s, -1);
                    if (Button(plus, "+", value < _setup.Max(s))) Nudge(s, +1);
                    y += rowHeight + gap;
                }
                y += Hud.Px(4f);
                GUI.Label(new Rect(x + Hud.Px(2f), y, inner, Hud.Px(28f)), Readout(group), _small);
            }
            if (hovered != null) _setupHint = hovered;

            // Foot: what the row under the mouse does, then the buttons.
            float foot = panel.yMax - footer;
            Hud.Fill(new Rect(x, foot, inner, Hud.Px(2f)), new Color(1f, 1f, 1f, 0.1f));
            GUI.Label(new Rect(x + Hud.Px(2f), foot + Hud.Px(8f), inner, Hud.Px(40f)),
                      _setupHint ?? "Point at a setting to see what it does. Changes are saved for this circuit as you make them.", _small);
            float buttonY = panel.yMax - Hud.Px(56f);
            if (_setupTab != AssistsTab
                && Button(new Rect(x, buttonY, Hud.Px(150f), Hud.Px(42f)), "RESET TAB", true, back: true))
            {
                _setup.Reset((CarSetup.Group)_setupTab);
                SetupStore.Save(SetupScene, _setup);
                _statsFor = null;
            }
            // The resets are the car's; the assists are switched one by one.
            if (_setupTab != AssistsTab
                && Button(new Rect(x + Hud.Px(160f), buttonY, Hud.Px(150f), Hud.Px(42f)), "RESET ALL", !_setup.AllDefault, back: true))
            {
                _setup.Reset();
                SetupStore.Save(SetupScene, _setup);
                _statsFor = null;
            }
            if (Button(new Rect(panel.xMax - pad - Hud.Px(160f), buttonY, Hud.Px(160f), Hud.Px(42f)), "DONE", true))
                _setupOpen = false;
        }

        void Nudge(CarSetup.Setting s, int presses)
        {
            _setup.Nudge(s, presses);
            SetupStore.Save(SetupScene, _setup);
            _statsFor = null;
        }

        /// <summary>What a group's settings come to, in the terms a driver thinks in.</summary>
        string Readout(CarSetup.Group group)
        {
            CarConfig built = _setup.Apply();
            switch (group)
            {
                case CarSetup.Group.Suspension:
                    float front = built.SpringRateFront * 0.5f + built.AntiRollFront;
                    float rear = built.SpringRateRear * 0.5f + built.AntiRollRear;
                    return $"Roll stiffness {100f * front / (front + rear):0}% front. More at the front understeers, more at the rear oversteers.";
                case CarSetup.Group.Gearbox:
                    return $"Top speed: {CarSetup.GearedTopSpeedKph(built):0} km/h at the rev limit in top gear, " +
                           $"{Analytic.TopSpeedKph(built):0} km/h where power meets drag. The car reaches the lower.";
                case CarSetup.Group.Aero:
                    return $"Aero balance {100f * CarSetup.AeroBalanceFront(built):0}% front. " +
                           $"Top speed where power meets drag: {Analytic.TopSpeedKph(built):0} km/h.";
                default:
                    return "";
            }
        }
    }
}
