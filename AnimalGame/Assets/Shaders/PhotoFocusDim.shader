Shader "UI/Photo Focus Dim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _CornerA ("Bottom Left", Vector) = (0.25, 0.25, 0, 0)
        _CornerB ("Top Left", Vector) = (0.25, 0.75, 0, 0)
        _CornerC ("Top Right", Vector) = (0.75, 0.75, 0, 0)
        _CornerD ("Bottom Right", Vector) = (0.75, 0.25, 0, 0)
        _Reveal ("Reveal", Range(0, 1)) = 0
        _EdgeSoftnessPixels ("Edge Softness", Float) = 3

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
            Name "PhotoFocusDim"

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
            float4 _CornerA;
            float4 _CornerB;
            float4 _CornerC;
            float4 _CornerD;
            float _Reveal;
            float _EdgeSoftnessPixels;

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
                float2 cornerA = _CornerA.xy * screenSize;
                float2 cornerB = _CornerB.xy * screenSize;
                float2 cornerC = _CornerC.xy * screenSize;
                float2 cornerD = _CornerD.xy * screenSize;

                float orientation = Cross2D(
                    cornerB - cornerA,
                    cornerC - cornerB);
                float winding = orientation >= 0.0 ? 1.0 : -1.0;
                float firstEdge = winding
                                  * EdgeDistance(
                                      cornerA,
                                      cornerB,
                                      samplePosition);
                float secondEdge = winding
                                   * EdgeDistance(
                                       cornerB,
                                       cornerC,
                                       samplePosition);
                float thirdEdge = winding
                                  * EdgeDistance(
                                      cornerC,
                                      cornerD,
                                      samplePosition);
                float fourthEdge = winding
                                   * EdgeDistance(
                                       cornerD,
                                       cornerA,
                                       samplePosition);
                float frameDistance = min(
                    min(firstEdge, secondEdge),
                    min(thirdEdge, fourthEdge));
                float softness = max(0.001, _EdgeSoftnessPixels);
                float insideFrame = smoothstep(
                    -softness,
                    softness,
                    frameDistance);

                fixed4 color = input.color;
                color.a *= saturate(_Reveal)
                           * (1.0 - saturate(insideFrame));

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
