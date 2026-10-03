using UnityEngine;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The safety car's light bar: its two amber lamps flash in turn, four times a second, while
    /// it is out, and stay dim while it is in. On the host's car and on a LAN client's copy of it
    /// alike; each says whether it is out (RaceDirector.SafetyCarOut).
    /// </summary>
    public sealed class SafetyCarLamps : MonoBehaviour
    {
        public static readonly Color On = new Color(1f, 0.6f, 0.05f), Off = new Color(0.25f, 0.15f, 0.05f);

        Renderer[] _lamps;
        System.Func<bool> _out;
        MaterialPropertyBlock _block;

        public void Set(Renderer[] lamps, System.Func<bool> flashing)
        {
            _lamps = lamps;
            _out = flashing;
            _block = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (_lamps == null) return;
            bool on = _out != null && _out();
            bool first = Mathf.Repeat(Time.time, 0.5f) < 0.25f;
            for (int l = 0; l < _lamps.Length; l++)
            {
                if (_lamps[l] == null) continue;
                _lamps[l].GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", on && (l == 0) == first ? On : Off);
                _lamps[l].SetPropertyBlock(_block);
            }
        }
    }
}
