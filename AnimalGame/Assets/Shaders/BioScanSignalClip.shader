Shader "AnimalGame/Biological Signal UI Clip"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _PointCoreRatio ("Point Core Ratio", Range(0.2, 0.9)) = 0.5
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
        ZTest Always
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

            fixed4 _Color;
            float _PointCoreRatio;
            float4 _ClipCenterPixels;
            float _ClipRadiusPixels;
            float _ClipSoftnessPixels;

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float distanceFromPointCenter = length(input.texcoord - 0.5);
                float outerShape = 1.0 - smoothstep(
                    0.455,
                    0.5,
                    distanceFromPointCenter);
                float coreRadius = 0.46 * clamp(
                    _PointCoreRatio,
                    0.2,
                    0.9);
                float core = 1.0 - smoothstep(
                    max(0.0, coreRadius - 0.025),
                    coreRadius + 0.025,
                    distanceFromPointCenter);
                fixed4 color = input.color;
                color.rgb *= lerp(0.76, 1.0, core);
                color.a *= max(core, outerShape * 0.58);
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
