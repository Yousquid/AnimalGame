Shader "Hidden/AnimalGame/StaticWaterEditorPreview"
{
    Properties
    {
        _MainTex ("Water Pattern", 2D) = "white" {}
        _DepthTex ("Encoded Depth", 2D) = "black" {}
        _Tint ("Preview Tint", Color) = (0.18, 0.72, 1, 1)
        _TileScale ("Tile Scale", Vector) = (16, 16, 0, 0)
        _PassableDepthNormalized ("Passable Depth", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _DepthTex;
            float4 _Tint;
            float4 _TileScale;
            float _PassableDepthNormalized;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float depth = tex2D(_DepthTex, input.uv).r;
                clip(depth - 0.001);

                float2 patternUv = frac(input.uv * _TileScale.xy);
                float pattern = tex2D(_MainTex, patternUv).r;
                float deepWater = smoothstep(
                    max(0.001, _PassableDepthNormalized - 0.02),
                    min(1.0, _PassableDepthNormalized + 0.02),
                    depth);
                float3 shallowColour = _Tint.rgb * (0.48 + pattern * 0.52);
                float3 deepColour = lerp(
                    _Tint.rgb * 0.72,
                    float3(0.05, 0.20, 0.58),
                    0.55);
                float3 colour = lerp(shallowColour, deepColour, deepWater);
                float alpha = lerp(0.22, 0.68, depth)
                              * lerp(0.62, 1.0, pattern)
                              * _Tint.a;
                return fixed4(colour, alpha);
            }
            ENDCG
        }
    }
}
