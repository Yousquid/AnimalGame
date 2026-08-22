Shader "UI/Photo Range Dim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TriangleA ("Triangle Apex", Vector) = (0.5, 0.5, 0, 0)
        _TriangleB ("Triangle Left", Vector) = (0.25, 1, 0, 0)
        _TriangleC ("Triangle Right", Vector) = (0.75, 1, 0, 0)
        _PlayerCenter ("Player Center", Vector) = (0.5, 0.5, 0, 0)
        _Reveal ("Reveal", Range(0, 1)) = 0
        _EdgeSoftnessPixels ("Edge Softness", Float) = 2
        _PlayerRadiusPixels ("Player Protection Radius", Float) = 48

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "PhotoRangeDim"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float4 _TriangleA;
            float4 _TriangleB;
            float4 _TriangleC;
            float4 _PlayerCenter;
            float _Reveal;
            float _EdgeSoftnessPixels;
            float _PlayerRadiusPixels;

            float Cross2D(float2 first, float2 second)
            {
                return first.x * second.y - first.y * second.x;
            }

            float EdgeDistance(
                float2 start,
                float2 end,
                float2 samplePosition)
            {
                float2 edge = end - start;
                return Cross2D(edge, samplePosition - start)
                       / max(0.0001, length(edge));
            }

            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 screenSize = _ScreenParams.xy;
                float2 samplePosition = input.texcoord * screenSize;
                float2 apex = _TriangleA.xy * screenSize;
                float2 leftPoint = _TriangleB.xy * screenSize;
                float2 rightPoint = _TriangleC.xy * screenSize;
                float orientation = Cross2D(
                    leftPoint - apex,
                    rightPoint - apex);
                float winding = orientation >= 0.0 ? 1.0 : -1.0;

                float firstEdge = winding
                                  * EdgeDistance(
                                      apex,
                                      leftPoint,
                                      samplePosition);
                float farEdge = winding
                                * EdgeDistance(
                                    leftPoint,
                                    rightPoint,
                                    samplePosition);
                float secondEdge = winding
                                   * EdgeDistance(
                                       rightPoint,
                                       apex,
                                       samplePosition);
                float triangleDistance = min(
                    firstEdge,
                    min(farEdge, secondEdge));
                float softness = max(0.001, _EdgeSoftnessPixels);
                float trianglePreserve = smoothstep(
                    -softness,
                    softness,
                    triangleDistance);

                float2 playerCenter = _PlayerCenter.xy * screenSize;
                float playerDistance = length(
                    samplePosition - playerCenter);
                float playerPreserve = 1.0 - smoothstep(
                    max(0.0, _PlayerRadiusPixels - softness),
                    _PlayerRadiusPixels + softness,
                    playerDistance);
                float outsideRange = 1.0
                                     - max(
                                         trianglePreserve,
                                         playerPreserve);

                fixed4 color = input.color;
                color.a *= saturate(_Reveal) * saturate(outsideRange);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
