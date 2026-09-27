using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Esc pauses the game and opens the settings: graphics quality and frame rate, with the rate
    /// actually being reached shown beside it so the effect of a choice can be seen. Pausing
    /// stops time, so the race clock, the countdown and the physics all wait, except in a LAN
    /// race, which cannot wait for one player and has no Restart race. In a race it also
    /// offers Restart race and Main menu (the lobby); the results screen uses the same two.
    /// Added to every scene at startup, so no scene has to be rebuilt for it.
    /// </summary>
    public sealed class SettingsMenu : MonoBehaviour
    {
        bool _open;
        float _smoothedFrame = 1f / 60f;
        GUIStyle _title, _text, _button;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("Settings Menu");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsMenu>();
        }

        const string LobbyScene = "Lobby";

        /// <summary>In a race, in a build that has the lobby to go back to.</summary>
        public static bool InRace => SceneManager.GetActiveScene().name != LobbyScene
                                     && SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{LobbyScene}.unity") >= 0;

        /// <summary>The same circuit from the grid, with the same car and settings.</summary>
        public static void RestartRace() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        /// <summary>Leaves the race for the lobby, to pick another circuit or colour.</summary>
        public static void MainMenu() => SceneManager.LoadScene(LobbyScene);

        void OnEnable() => SceneManager.sceneLoaded += Closed;
        void OnDisable() => SceneManager.sceneLoaded -= Closed;

        // A new scene starts unpaused, however it was reached.
        void Closed(Scene scene, LoadSceneMode mode)
        {
            _open = false;
            Time.timeScale = 1f;
            GameAudio.SetPaused(false);
        }

        void Update()
        {
            // Unscaled, so it keeps measuring while paused.
            _smoothedFrame = Mathf.Lerp(_smoothedFrame, Time.unscaledDeltaTime, 0.05f);
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            _open = !_open;

            // A LAN race cannot stop for one player: the menu opens and the race goes on.
            bool stop = _open && !LanSession.Active;
            Time.timeScale = stop ? 0f : 1f;
            GameAudio.SetPaused(stop);
            if (_open) GameAudio.Select(); else GameAudio.Back();
        }

        void OnDestroy()
        {
            if (_open) Time.timeScale = 1f;
            GameAudio.SetPaused(false);
        }

        readonly GameAudio.HoverTracker _hover = new GameAudio.HoverTracker();

        void OnGUI()
        {
            if (!_open) return;
            _hover.Begin();
            _title ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _text ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _button ??= new GUIStyle(GUI.skin.button);
            _title.fontSize = Hud.Font(34);
            _text.fontSize = Hud.Font(18);
            _button.fontSize = Hud.Font(18);

            // Restart and back to the lobby from a race, when the build has one.
            bool lobby = InRace;
            float width = Hud.Px(560f), height = Hud.Px(lobby ? 470f : 400f);
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUI.Box(panel, GUIContent.none);   // twice: one box is too faint to read over

            float y = panel.y + Hud.Px(16f);
            GUI.Label(new Rect(panel.x, y, width, Hud.Px(44f)), LanSession.Active ? "MENU (THE RACE GOES ON)" : "PAUSED", _title);
            y += Hud.Px(58f);
            GUI.Label(new Rect(panel.x, y, width, Hud.Px(28f)), "Graphics quality", _text);
            y += Hud.Px(34f);
            float gridWidth = width - Hud.Px(40f);
            int quality = GUI.SelectionGrid(new Rect(panel.x + Hud.Px(20f), y, gridWidth, Hud.Px(40f)),
                                            DisplaySettings.Quality, DisplaySettings.QualityNames, 3, _button);
            if (quality != DisplaySettings.Quality)
            {
                GameAudio.Select();
                DisplaySettings.ApplyQuality(quality, save: true);
            }
            y += Hud.Px(56f);

            GUI.Label(new Rect(panel.x, y, width, Hud.Px(28f)), "Frame rate", _text);
            y += Hud.Px(34f);

            int chosen = GUI.SelectionGrid(new Rect(panel.x + Hud.Px(20f), y, gridWidth, Hud.Px(84f)),
                                           DisplaySettings.Current, DisplaySettings.Names, 3, _button);
            if (chosen != DisplaySettings.Current)
            {
                GameAudio.Select();
                DisplaySettings.Apply(chosen, save: true);
            }
            y += Hud.Px(96f);

            GUI.Label(new Rect(panel.x, y, width, Hud.Px(28f)),
                      $"Running at {1f / Mathf.Max(_smoothedFrame, 1e-4f):0} fps        Esc: resume", _text);

            if (!lobby) return;
            float half = (width - Hud.Px(52f)) * 0.5f;
            var restart = new Rect(panel.x + Hud.Px(20f), y + Hud.Px(44f), half, Hud.Px(48f));
            var menu = new Rect(panel.x + Hud.Px(32f) + half, y + Hud.Px(44f), half, Hud.Px(48f));
            // A LAN race is the host's to start, so there is no restarting it alone; the host
            // can instead take everyone back to the lobby together.
            if (!LanSession.Active)
            {
                _hover.Watch(restart);
                if (GUI.Button(restart, "Restart race", _button))
                {
                    GameAudio.Confirm();
                    RestartRace();
                }
            }
            else if (LanSession.Current.Host != null && LanSession.Current.Race != null)
            {
                _hover.Watch(restart);
                if (GUI.Button(restart, "Lobby, all together", _button))
                {
                    GameAudio.Confirm();
                    _open = false;
                    LanSession.Current.ReturnToLobby();
                }
            }
            _hover.Watch(menu);
            if (GUI.Button(menu, LanSession.Active ? "Main menu (leave)" : "Main menu", _button))
            {
                GameAudio.Back();
                MainMenu();
            }
        }
    }
}
