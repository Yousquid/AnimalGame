Shader "AnimalGame/Player UI Clipped Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
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
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _AlphaTex;
            fixed4 _Color;
            float _EnableExternalAlpha;
            float _PlayerUiClipEnabled;
            float4 _PlayerUiClipCenterPixels;
            float _PlayerUiClipRadiusPixels;
            float _PlayerUiClipSoftnessPixels;

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                #ifdef PIXELSNAP_ON
                output.vertex = UnityPixelSnap(output.vertex);
                #endif
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 SampleSpriteTexture(float2 uv)
            {
                fixed4 color = tex2D(_MainTex, uv);
                #if ETC1_EXTERNAL_ALPHA
                fixed4 alpha = tex2D(_AlphaTex, uv);
                color.a = lerp(color.a, alpha.r, _EnableExternalAlpha);
                #endif
                return color;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = SampleSpriteTexture(input.texcoord)
                               * input.color;
                float2 screenUv = input.screenPosition.xy
                                  / max(0.0001, input.screenPosition.w);
                float2 screenPixels = screenUv * _ScreenParams.xy;
                float distanceFromCenter = length(
                    screenPixels - _PlayerUiClipCenterPixels.xy);
                float radius = max(0.0, _PlayerUiClipRadiusPixels);
                float softness = max(
                    0.001,
                    _PlayerUiClipSoftnessPixels);
                float insideVisibility = 1.0 - smoothstep(
                    max(0.0, radius - softness),
                    radius + softness,
                    distanceFromCenter);
                color.a *= lerp(
                    1.0,
                    insideVisibility,
                    saturate(_PlayerUiClipEnabled));
                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
