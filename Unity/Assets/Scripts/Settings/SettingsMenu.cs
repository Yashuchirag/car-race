using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// Esc pauses the game and opens the settings: for now the frame rate, with the rate
    /// actually being reached shown beside it so the effect of a choice can be seen. Pausing
    /// stops time, so the race clock, the countdown and the physics all wait. Added to every
    /// scene at startup, so no scene has to be rebuilt for it.
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

        void Update()
        {
            // Unscaled, so it keeps measuring while paused.
            _smoothedFrame = Mathf.Lerp(_smoothedFrame, Time.unscaledDeltaTime, 0.05f);
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            _open = !_open;
            Time.timeScale = _open ? 0f : 1f;
        }

        void OnDestroy()
        {
            if (_open) Time.timeScale = 1f;
        }

        void OnGUI()
        {
            if (!_open) return;
            _title ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _text ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _button ??= new GUIStyle(GUI.skin.button);
            _title.fontSize = Hud.Font(34);
            _text.fontSize = Hud.Font(18);
            _button.fontSize = Hud.Font(18);

            float width = Hud.Px(560f), height = Hud.Px(300f);
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUI.Box(panel, GUIContent.none);   // twice: one box is too faint to read over

            float y = panel.y + Hud.Px(16f);
            GUI.Label(new Rect(panel.x, y, width, Hud.Px(44f)), "PAUSED", _title);
            y += Hud.Px(58f);
            GUI.Label(new Rect(panel.x, y, width, Hud.Px(28f)), "Frame rate", _text);
            y += Hud.Px(34f);

            float gridWidth = width - Hud.Px(40f);
            int chosen = GUI.SelectionGrid(new Rect(panel.x + Hud.Px(20f), y, gridWidth, Hud.Px(84f)),
                                           DisplaySettings.Current, DisplaySettings.Names, 3, _button);
            if (chosen != DisplaySettings.Current) DisplaySettings.Apply(chosen, save: true);
            y += Hud.Px(96f);

            GUI.Label(new Rect(panel.x, y, width, Hud.Px(28f)),
                      $"Running at {1f / Mathf.Max(_smoothedFrame, 1e-4f):0} fps        Esc: resume", _text);
        }
    }
}
