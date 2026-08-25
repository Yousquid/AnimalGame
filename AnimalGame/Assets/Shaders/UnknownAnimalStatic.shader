Shader "Animal Game/Unknown Animal Static"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _DarkColor ("Dark Static", Color) = (0.015, 0.025, 0.035, 1)
        _LightColor ("Light Static", Color) = (0.78, 0.94, 1, 1)
        _NoiseCells ("Noise Cells", Float) = 20
        _NoiseFps ("Noise Frames Per Second", Float) = 18
        _EdgeDistortion ("Edge Distortion", Range(0, 0.3)) = 0.12
        [PerRendererData] _Seed ("Instance Seed", Float) = 0
        [PerRendererData] _RevealProgress ("Reveal Progress", Range(0, 1)) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _DarkColor;
            fixed4 _LightColor;
            float _NoiseCells;
            float _NoiseFps;
            float _EdgeDistortion;
            float _Seed;
            float _RevealProgress;

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 345.45));
                value += dot(value, value + 34.345);
                return frac(value.x * value.y);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float cells = max(4.0, _NoiseCells);
                float frame = floor(_Time.y * max(1.0, _NoiseFps)
                    * (1.0 + _RevealProgress * 2.0));

                float2 sampledUv = input.texcoord;
                float row = floor(sampledUv.y * 30.0);
                float rowNoise = Hash21(float2(row + _Seed, frame));
                float glitchRow = step(0.87, rowNoise);
                sampledUv.x += (rowNoise - 0.5) * 0.05 * glitchRow;

                float coarse = Hash21(
                    floor(sampledUv * cells) + float2(frame, _Seed));
                float fineNoise = Hash21(
                    floor(sampledUv * cells * 2.35)
                    + float2(frame * 1.73, _Seed * 2.11));
                float snow = saturate(lerp(coarse, fineNoise, 0.38)
                    + glitchRow * 0.18);

                fixed4 spriteSample = tex2D(_MainTex, input.texcoord);
                float2 centred = input.texcoord - 0.5;
                float radial = length(centred) * 2.0;
                float edgeNoise = (Hash21(
                    floor(input.texcoord * 14.0) + _Seed) - 0.5)
                    * _EdgeDistortion;
                float irregularMask = 1.0 - smoothstep(
                    0.80 + edgeNoise,
                    1.02 + edgeNoise,
                    radial);

                float dissolveNoise = Hash21(
                    floor(input.texcoord * 25.0) + _Seed * 3.17);
                float dissolve = step(_RevealProgress, dissolveNoise);

                fixed4 output = lerp(_DarkColor, _LightColor, snow);
                output.rgb *= input.color.rgb;
                output.a *= input.color.a
                    * spriteSample.a
                    * irregularMask
                    * dissolve
                    * (0.72 + snow * 0.28);
                return output;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
