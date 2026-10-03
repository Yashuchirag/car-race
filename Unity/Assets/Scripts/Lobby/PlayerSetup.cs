using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The local player's choices from the lobby, kept between sessions, and put onto their
    /// car whenever a scene loads: the body design and colour. The player's car is the
    /// CarController with a DriverInput; its body is the child named "Body", recoloured with
    /// a property block so no material asset is changed, and reshaped by CarDesigns. When LAN play comes, this is the
    /// record each player sends the host.
    /// </summary>
    public static class PlayerSetup
    {
        public static readonly (string name, Color colour)[] Colours =
        {
            ("Racing Red", new Color(0.8f, 0.1f, 0.08f)),
            ("Sunset Orange", new Color(1f, 0.42f, 0.05f)),
            ("Signal Yellow", new Color(0.95f, 0.75f, 0.08f)),
            ("British Green", new Color(0.08f, 0.42f, 0.2f)),
            ("Ocean Blue", new Color(0.08f, 0.28f, 0.85f)),
            ("Royal Purple", new Color(0.45f, 0.18f, 0.75f)),
            ("Pearl White", new Color(0.9f, 0.9f, 0.92f)),
            ("Carbon Black", new Color(0.07f, 0.07f, 0.08f)),
        };

        const string ColourKey = "CarRace.CarColour";
        const string DesignKey = "CarRace.CarDesign";
        static readonly int CommandLineColour = ReadCommandLine("-carColour");
        static readonly int CommandLineDesign = ReadCommandLine("-carDesign");

        static int ReadCommandLine(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int chosen) ? chosen : -1;
        }
        static readonly int BaseColour = Shader.PropertyToID("_BaseColor");

        /// <summary>The saved choice, or `-carColour 4` on the command line for one session:
        /// for testing, and later for running several players on one machine.</summary>
        public static int ColourIndex
        {
            get => Mathf.Clamp(CommandLineColour >= 0 ? CommandLineColour : PlayerPrefs.GetInt(ColourKey, 0), 0, Colours.Length - 1);
            set
            {
                PlayerPrefs.SetInt(ColourKey, Mathf.Clamp(value, 0, Colours.Length - 1));
                PlayerPrefs.DeleteKey(CustomKey);
                PlayerPrefs.Save();
            }
        }

        const string CustomKey = "CarRace.CarCustomColour";

        /// <summary>
        /// The colour the car is painted: a colour of the player's own from the lobby's sliders
        /// (Custom), or else the chosen one of Colours. A LAN game still sends the palette index,
        /// the one nearest the custom colour, since the protocol carries no more.
        /// </summary>
        public static Color Colour => Custom ?? Colours[ColourIndex].colour;

        /// <summary>The player's own colour, or null when a palette colour is chosen; never under
        /// -carColour. Setting a colour also chooses the nearest palette colour, for LAN games;
        /// choosing a palette colour (ColourIndex) clears it.</summary>
        public static Color? Custom
        {
            get
            {
                if (CommandLineColour >= 0) return null;
                string saved = PlayerPrefs.GetString(CustomKey, "");
                return ColorUtility.TryParseHtmlString("#" + saved, out Color c) && saved.Length > 0 ? c : (Color?)null;
            }
            set
            {
                if (value is Color c)
                {
                    int nearest = 0;
                    float best = float.MaxValue;
                    for (int i = 0; i < Colours.Length; i++)
                    {
                        Color p = Colours[i].colour;
                        float d = (p.r - c.r) * (p.r - c.r) + (p.g - c.g) * (p.g - c.g) + (p.b - c.b) * (p.b - c.b);
                        if (d < best) { best = d; nearest = i; }
                    }
                    PlayerPrefs.SetInt(ColourKey, nearest);
                    PlayerPrefs.SetString(CustomKey, ColorUtility.ToHtmlStringRGB(c));
                }
                else PlayerPrefs.DeleteKey(CustomKey);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The colour's name, for the lobby: the palette's, or CUSTOM.</summary>
        public static string ColourName => Custom.HasValue ? "Custom" : Colours[ColourIndex].name;

        /// <summary>The saved body design, or `-carDesign 2` for one session.</summary>
        public static int DesignIndex
        {
            get => Mathf.Clamp(CommandLineDesign >= 0 ? CommandLineDesign : PlayerPrefs.GetInt(DesignKey, 0), 0, Mathf.Max(CarDesigns.Count - 1, 0));
            set
            {
                PlayerPrefs.SetInt(DesignKey, Mathf.Clamp(value, 0, Mathf.Max(CarDesigns.Count - 1, 0)));
                SetupStore.Body = CarDesigns.NameOf(DesignIndex);
                PlayerPrefs.Save();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialise()
        {
            SceneManager.sceneLoaded += (_, __) => PaintPlayer();
            PaintPlayer();
        }

        /// <summary>
        /// Paints the player's car, and repaints any other car whose colour is too close to
        /// it, so two blue cars are never on track together: each such car gets the first
        /// colour of the palette not near the player's or any other car's.
        /// </summary>
        static void PaintPlayer()
        {
            // A LAN race paints every car itself, the same way on every machine (LanRace).
            if (LanSession.Active && LanSession.Current.Race != null) return;
            var cars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
            var taken = new System.Collections.Generic.List<Color> { Colour };
            foreach (var car in cars)
                if (car.GetComponent<DriverInput>() != null)
                {
                    CarDesigns.Apply(car.transform, DesignIndex);
                    Paint(car.transform.Find("Body"), Colour);
                }
                else taken.Add(BodyColour(car.transform.Find("Body")));

            foreach (var car in cars)
            {
                if (car.GetComponent<DriverInput>() != null) continue;
                Transform body = car.transform.Find("Body");
                if (!Near(BodyColour(body), Colour)) continue;
                foreach (var (_, colour) in Colours)
                {
                    if (taken.Exists(c => Near(c, colour))) continue;
                    Paint(body, colour);
                    taken.Add(colour);
                    break;
                }
            }
        }

        static bool Near(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.45f;

        /// <summary>A body's colour as drawn: its property block's if it has been repainted,
        /// else its material's.</summary>
        public static Color BodyColour(Transform body)
        {
            var renderer = body != null ? body.GetComponent<Renderer>() : null;
            if (renderer == null) return Color.white;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.HasColor(BaseColour) ? block.GetColor(BaseColour) : renderer.sharedMaterial.GetColor(BaseColour);
        }

        /// <summary>Recolours a car body without touching its material asset.</summary>
        public static void Paint(Transform body, Color colour)
        {
            var renderer = body != null ? body.GetComponent<Renderer>() : null;
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColour, colour);
            renderer.SetPropertyBlock(block);
        }
    }
}
