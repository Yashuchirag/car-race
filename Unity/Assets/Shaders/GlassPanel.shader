// A frosted glass panel for the lobby's IMGUI, drawn with Graphics.DrawTexture on the repaint
// pass. _MainTex is GlassBackdrop's blurred copy of the scene; the panel samples it at its own
// place on the screen, so the panel shows a blur of whatever is behind it. Over that: a tint,
// a soft sheen from the top, and a thin bright edge, inside a rounded rectangle cut out with a
// signed distance, a pixel of anti-aliasing at its edge.
Shader "CarRace/Glass Panel"
{
    Properties
    {
        _MainTex ("Blurred scene", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.04, 0.05, 0.08, 0.5)
        _Edge ("Edge", Color) = (1, 1, 1, 0.22)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Tint, _Edge;
            float4 _Rect;      // the panel in GUI pixels: x, y from the top left, width, height
            float4 _Screen;    // the screen's width and height in pixels, the corner radius, the edge width
            float _Sheen;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 size = _Rect.zw;
                float2 p = float2(i.uv.x, 1 - i.uv.y) * size;          // pixels from the panel's top left
                float r = _Screen.z;
                float2 q = abs(p - size * 0.5) - (size * 0.5 - r);
                float d = length(max(q, 0)) + min(max(q.x, q.y), 0) - r; // negative inside
                float alpha = saturate(0.5 - d);

                float2 screen = float2((_Rect.x + p.x) / _Screen.x, 1 - (_Rect.y + p.y) / _Screen.y);
                float3 colour = tex2D(_MainTex, screen).rgb;
                colour = lerp(colour, _Tint.rgb, _Tint.a);
                float down = p.y / size.y;
                colour += _Sheen * (1 - smoothstep(0, 0.45, down));
                float edge = saturate(d + _Screen.w + 0.5) * lerp(1, 0.45, down);
                colour = lerp(colour, _Edge.rgb, _Edge.a * edge);
                return float4(colour, alpha);
            }
            ENDCG
        }
    }
}
