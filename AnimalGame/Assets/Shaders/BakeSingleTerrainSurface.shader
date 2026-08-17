Shader "Hidden/AnimalGame/Bake Single Terrain Surface"
{
    Properties
    {
        _PatternTex ("Pattern", 2D) = "white" {}
        _MaskTex ("Paint Mask", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.35
        _MapSizeMeters ("Map Size Metres", Vector) = (250, 250, 0, 0)
        _TileSizeMeters ("Tile Size Metres", Float) = 8
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _PatternTex;
            sampler2D _MaskTex;
            fixed4 _Tint;
            float _Opacity;
            float4 _MapSizeMeters;
            float _TileSizeMeters;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float tileSize = max(0.01, _TileSizeMeters);
                float2 patternUv = frac(
                    input.uv * _MapSizeMeters.xy / tileSize);
                fixed4 pattern = tex2D(_PatternTex, patternUv);
                // Version one intentionally keeps a binary, unblended boundary.
                float painted = step(0.5, tex2D(_MaskTex, input.uv).r);
                pattern.rgb *= _Tint.rgb;
                pattern.a *= _Tint.a * saturate(_Opacity) * painted;
                return pattern;
            }
            ENDCG
        }
    }
}
