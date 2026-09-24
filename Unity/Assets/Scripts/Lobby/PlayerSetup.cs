using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The local player's choices from the lobby, kept between sessions, and put onto their
    /// car whenever a scene loads: for now the body colour. The player's car is the
    /// CarController with a DriverInput; its body is the child named "Body", recoloured with
    /// a property block so no material asset is changed. When LAN play comes, this is the
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
        static readonly int CommandLineColour = ReadCommandLineColour();

        static int ReadCommandLineColour()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-carColour");
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
                PlayerPrefs.Save();
            }
        }

        public static Color Colour => Colours[ColourIndex].colour;

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
            var cars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
            var taken = new System.Collections.Generic.List<Color> { Colour };
            foreach (var car in cars)
                if (car.GetComponent<DriverInput>() != null) Paint(car.transform.Find("Body"), Colour);
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
