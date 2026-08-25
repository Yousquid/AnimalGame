Shader "AnimalGame/Biological Signal UI Clip"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ClipCenterPixels ("UI Clip Center Pixels", Vector) = (960, 540, 0, 0)
        _ClipRadiusPixels ("UI Clip Radius Pixels", Float) = 430
        _ClipSoftnessPixels ("UI Clip Edge Softness Pixels", Float) = 1.5
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
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipCenterPixels;
            float _ClipRadiusPixels;
            float _ClipSoftnessPixels;

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.texcoord) * input.color;
                float2 screenUv = input.screenPosition.xy
                                  / max(0.0001, input.screenPosition.w);
                float2 screenPixels = screenUv * _ScreenParams.xy;
                float radius = max(0.0, _ClipRadiusPixels);
                float softness = max(0.001, _ClipSoftnessPixels);
                float distanceFromCenter = length(
                    screenPixels - _ClipCenterPixels.xy);
                float insideVisibility = 1.0 - smoothstep(
                    max(0.0, radius - softness),
                    radius + softness,
                    distanceFromCenter);
                color.a *= insideVisibility;
                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }
}
