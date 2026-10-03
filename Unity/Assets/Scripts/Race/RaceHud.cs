using UnityEngine;
using CarRace.Track;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The race officials, on screen: each ruling on the player as a banner with its flag for a
    /// few seconds (black and white for a track limits warning or penalty, black for the
    /// disqualification), and while any stand, the penalty seconds so far and the warnings used
    /// under the position box. Added by RaceDirector, which owns the rulings.
    /// </summary>
    public sealed class RaceHud : MonoBehaviour
    {
        const float BannerSeconds = 4f;

        RaceDirector _director;
        int _entry, _seen;
        RaceControl.Event _banner;
        float _bannerUntil;
        Texture2D _blackAndWhite, _chequered;
        bool _wasYellow;
        float _greenUntil;
        GUIStyle _title, _detail, _status;

        public void Show(RaceDirector director, int playerEntry)
        {
            _director = director;
            _entry = playerEntry;
            useGUILayout = false;
        }

        void OnDestroy()
        {
            if (_blackAndWhite != null) Destroy(_blackAndWhite);
            if (_chequered != null) Destroy(_chequered);
        }

        void Update()
        {
            RaceControl control = _director != null ? _director.Control : null;
            if (control == null) return;
            for (; _seen < control.Events.Count; _seen++)
            {
                RaceControl.Event ruling = control.Events[_seen];
                if (ruling.Car != _entry || ruling.Ruling == RaceControl.Ruling.GiveBack) continue;
                _banner = ruling;
                _bannerUntil = Time.time + BannerSeconds;
                if (ruling.Ruling == RaceControl.Ruling.Warning) GameAudio.Back(); else GameAudio.Countdown(go: false);
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Hud.Hidden || _director == null || _director.Control == null) return;
            Styles();
            RaceControl.Entry me = _director.Control.Entries[_entry];
            FlagPanel(me);

            // Penalties so far, under the position box and its hint.
            if (me.PenaltyS > 0f || me.Warnings > 0)
            {
                string standing = me.Disqualified ? "DISQUALIFIED"
                                : $"PENALTIES +{me.PenaltyS:0} s   ·   TRACK LIMITS WARNINGS {Mathf.Min(me.Warnings, RaceControl.Warnings)} OF {RaceControl.Warnings}";
                GUI.Label(new Rect(0f, Hud.Px(92f), Screen.width, Hud.Px(24f)), standing, _status);
            }

            // Places gained off the track: who to let by, and how long is left. Shown as long as
            // any are owed, over the other banners, since it is the one you can still act on.
            RaceControl.Owed owed = _director.Control.Owing.Find(o => o.Car == _entry);
            if (owed != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (RaceControl.Owed o in _director.Control.Owing)
                    if (o.Car == _entry) names.Add(_director.Control.Entries[o.Passed].Name);
                float left = Mathf.Max(0f, owed.DeadlineS - _director.RaceTime);
                DrawBanner(false, true, $"GIVE THE PLACE BACK  ·  {left:0} s",
                           $"You passed {string.Join(", ", names)} off the track. Let {(names.Count > 1 ? "them" : "it")} by, " +
                           $"or {RaceControl.KeptPlaceSeconds:0} s for each place kept.");
                return;
            }

            if (_banner == null || Time.time > _bannerUntil) return;
            RaceControl.Entry ruled = _director.Control.Entries[_entry];
            string other = _banner.Other >= 0 ? _director.Control.Entries[_banner.Other].Name : "";
            string offence = _banner.Cause == RaceControl.Cause.PlaceKept ? $"Kept the place on {other}"
                           : _banner.Cause == RaceControl.Cause.YellowFlag ? $"Passed {other} under a yellow flag"
                           : _banner.Cause == RaceControl.Cause.Cut ? "Corner cut: you gained by leaving the track"
                           : "Track limits: four wheels off";
            switch (_banner.Ruling)
            {
                case RaceControl.Ruling.Warning:
                    DrawBanner(false, false, $"TRACK LIMITS  ·  WARNING {Mathf.Min(ruled.Warnings, RaceControl.Warnings)} OF {RaceControl.Warnings}",
                               $"Four wheels off. After {RaceControl.Warnings} warnings each one costs {RaceControl.PenaltySeconds:0} s. Lap invalid.");
                    break;
                case RaceControl.Ruling.Penalty:
                    DrawBanner(false, false, $"+{_banner.Seconds:0} s PENALTY",
                               $"{offence}. Added to your race time.{(_banner.Other >= 0 ? "" : " Lap invalid.")}");
                    break;
                case RaceControl.Ruling.Disqualified:
                    DrawBanner(true, false, "BLACK FLAG  ·  DISQUALIFIED", $"{RaceControl.PenaltiesToDisqualify} penalties. Your race is over.");
                    break;
            }
        }

        /// <summary>A banner under the position box: a flag (black, black and white, or none for a
        /// place to give back, which is not a flag but a stewards' instruction), a title and a line.</summary>
        void DrawBanner(bool black, bool noFlag, string title, string detail)
        {
            float width = Hud.Px(560f), height = Hud.Px(76f);
            var banner = new Rect((Screen.width - width) * 0.5f, Hud.Px(150f), width, height);
            Hud.Rounded(banner, new Color(0.05f, 0.06f, 0.1f, 0.9f));
            float text = banner.x + Hud.Px(16f);
            if (!noFlag)
            {
                var flag = new Rect(banner.x + Hud.Px(14f), banner.y + Hud.Px(12f), Hud.Px(72f), Hud.Px(52f));
                Hud.Fill(new Rect(flag.x - 2f, flag.y - 2f, flag.width + 4f, flag.height + 4f), new Color(0.7f, 0.7f, 0.72f));
                if (black) Hud.Fill(flag, Color.black);
                else GUI.DrawTexture(flag, BlackAndWhite());
                text = flag.xMax + Hud.Px(14f);
            }
            else Hud.Fill(new Rect(banner.x, banner.y, Hud.Px(6f), banner.height), new Color(1f, 0.55f, 0.1f));
            GUI.Label(new Rect(text, banner.y + Hud.Px(8f), banner.xMax - text - Hud.Px(10f), Hud.Px(34f)), title, _title);
            GUI.Label(new Rect(text, banner.y + Hud.Px(40f), banner.xMax - text - Hud.Px(10f), Hud.Px(30f)), detail, _detail);
        }

        /// <summary>
        /// The flag being shown to you, left of the position box: yellow in a yellow zone (no
        /// overtaking), blue with the car to let by, green for GreenSeconds after a yellow, and
        /// the chequered flag once you have finished.
        /// </summary>
        void FlagPanel(RaceControl.Entry me)
        {
            RaceFlags flags = _director.Flags;
            bool yellow = flags != null && flags.InYellow[_entry] >= 0;
            int blue = flags != null ? flags.BlueFor[_entry] : -1;
            if (_wasYellow && !yellow) _greenUntil = Time.time + GreenSeconds;
            _wasYellow = yellow;

            string label;
            Color colour = Color.white;
            Texture2D pattern = null;
            if (me.Finished) { label = "CHEQUERED FLAG"; pattern = Chequered(); }
            else if (yellow) { label = "YELLOW  ·  NO OVERTAKING"; colour = new Color(1f, 0.82f, 0.05f); }
            else if (blue >= 0) { label = $"BLUE  ·  LET {_director.Control.Entries[blue].Name.ToUpperInvariant()} BY"; colour = new Color(0.1f, 0.35f, 0.95f); }
            else if (Time.time < _greenUntil) { label = "GREEN  ·  TRACK CLEAR"; colour = new Color(0.15f, 0.75f, 0.3f); }
            else return;

            float width = Hud.Px(300f), height = Hud.Px(56f);
            var panel = new Rect(Screen.width * 0.5f - Hud.Px(170f) - width, Hud.Px(10f), width, height);
            Hud.Rounded(panel, new Color(0.05f, 0.06f, 0.1f, 0.88f));
            var flag = new Rect(panel.x + Hud.Px(10f), panel.y + Hud.Px(10f), Hud.Px(50f), Hud.Px(36f));
            Hud.Fill(new Rect(flag.x - 2f, flag.y - 2f, flag.width + 4f, flag.height + 4f), new Color(0.7f, 0.7f, 0.72f));
            if (pattern != null) GUI.DrawTexture(flag, pattern);
            else Hud.Fill(flag, colour);
            _status.alignment = TextAnchor.MiddleLeft;
            _status.normal.textColor = Color.white;
            GUI.Label(new Rect(flag.xMax + Hud.Px(10f), panel.y, panel.xMax - flag.xMax - Hud.Px(14f), height), label, _status);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.normal.textColor = new Color(1f, 0.55f, 0.1f);
        }

        const float GreenSeconds = 2f;

        Texture2D Chequered()
        {
            if (_chequered != null) return _chequered;
            const int W = 8, H = 6;
            var pixels = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                pixels[y * W + x] = (x + y) % 2 == 0 ? new Color32(10, 10, 10, 255) : new Color32(245, 245, 245, 255);
            _chequered = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _chequered.SetPixels32(pixels);
            _chequered.Apply();
            return _chequered;
        }

        /// <summary>The black and white flag: split corner to corner, black above.</summary>
        Texture2D BlackAndWhite()
        {
            if (_blackAndWhite != null) return _blackAndWhite;
            const int W = 36, H = 26;
            var pixels = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                pixels[y * W + x] = (float)y / H > (float)x / W ? new Color32(10, 10, 10, 255) : new Color32(245, 245, 245, 255);
            _blackAndWhite = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _blackAndWhite.SetPixels32(pixels);
            _blackAndWhite.Apply();
            return _blackAndWhite;
        }

        void Styles()
        {
            _title ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _detail ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = true, normal = { textColor = new Color(0.8f, 0.82f, 0.88f) } };
            _status ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.55f, 0.1f) } };
            _title.fontSize = Hud.Font(22);
            _detail.fontSize = Hud.Font(14);
            _status.fontSize = Hud.Font(15);
        }
    }
}
