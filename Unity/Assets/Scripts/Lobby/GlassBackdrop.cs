using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarRace.UnityGame
{
    /// <summary>
    /// The blurred copy of the scene the lobby's glass panels show through them: a second camera
    /// with the main camera's view renders at a quarter of the screen's size, post-processing
    /// and all so its colours match, and that is halved twice and doubled twice again with
    /// bilinear filtering, which leaves it soft. Every frame, so the turning car blurs behind the
    /// panels as it passes. Added to the main camera by LobbyMenu.
    /// </summary>
    public sealed class GlassBackdrop : MonoBehaviour
    {
        Camera _main, _copy;
        RenderTexture _source, _half, _quarter, _back, _blurred;

        /// <summary>The blurred scene, or null before the first frame.</summary>
        public Texture Blurred => _blurred;

        void Start()
        {
            _main = GetComponent<Camera>();
            var go = new GameObject("Glass Backdrop Camera");
            go.transform.SetParent(transform, false);
            _copy = go.AddComponent<Camera>();
            _copy.CopyFrom(_main);
            _copy.depth = _main.depth - 1;
            var data = _copy.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
        }

        void LateUpdate()
        {
            if (_copy == null) return;
            int w = Mathf.Max(16, Screen.width / 4), h = Mathf.Max(16, Screen.height / 4);
            if (_source == null || _source.width != w || _source.height != h)
            {
                Release();
                _source = Target(w, h);
                _half = Target(w / 2, h / 2);
                _quarter = Target(w / 4, h / 4);
                _back = Target(w / 2, h / 2);
                _blurred = Target(w, h);
                _copy.targetTexture = _source;
            }
            _copy.transform.SetPositionAndRotation(_main.transform.position, _main.transform.rotation);
            _copy.fieldOfView = _main.fieldOfView;

            // Last frame's render, down twice and up twice for the softness: a frame behind,
            // which nobody sees, and done here rather than in OnGUI so the blits cannot leave
            // the GUI drawing into a render texture.
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(_source, _half);
            Graphics.Blit(_half, _quarter);
            Graphics.Blit(_quarter, _back);
            Graphics.Blit(_back, _blurred);
            RenderTexture.active = active;
        }

        static RenderTexture Target(int w, int h)
        {
            var rt = new RenderTexture(Mathf.Max(4, w), Mathf.Max(4, h), 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void Release()
        {
            foreach (var rt in new[] { _source, _half, _quarter, _back, _blurred })
                if (rt != null) { rt.Release(); Destroy(rt); }
        }

        void OnDestroy() => Release();
    }
}
