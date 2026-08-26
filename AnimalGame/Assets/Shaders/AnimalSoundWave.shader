Shader "AnimalGame/Animal Sound Wave"
{
    Properties
    {
        _Color ("Wave Color", Color) = (0.88, 0.88, 0.88, 0.9)
        _Progress ("Expansion Progress", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _RingCount ("Ring Count", Range(1, 8)) = 5
        _InnerRadiusRatio ("Inner Radius Ratio", Range(0.05, 0.8)) = 0.23
        _LineWidth ("Normalized Line Width", Range(0.002, 0.08)) = 0.014
        _Breakup ("Irregular Gaps", Range(0, 1)) = 0.38
        _Irregularity ("Line Irregularity", Range(0, 1)) = 0.28
        _Seed ("Random Seed", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+80"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
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
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _Progress;
            float _Opacity;
            float _RingCount;
            float _InnerRadiusRatio;
            float _LineWidth;
            float _Breakup;
            float _Irregularity;
            float _Seed;

            float Hash(float value)
            {
                return frac(sin(value * 12.9898 + _Seed * 7.137) * 43758.5453);
            }

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 centered = (input.uv - 0.5) * 2.0;
                float radialDistance = length(centered);
                float angle01 = atan2(centered.y, centered.x)
                                / 6.28318530718
                                + 0.5;
                float progress = saturate(_Progress);
                float ringCount = clamp(round(_RingCount), 1.0, 8.0);
                float denominator = max(1.0, ringCount - 1.0);
                float antiAlias = max(fwidth(radialDistance), 0.0008);
                float combinedRing = 0.0;

                [unroll]
                for (int ringIndex = 0; ringIndex < 8; ringIndex++)
                {
                    float enabled = step((float)ringIndex + 0.5, ringCount);
                    float ring01 = (float)ringIndex / denominator;
                    float targetRadius = progress
                                         * lerp(
                                             saturate(_InnerRadiusRatio),
                                             1.0,
                                             ring01);

                    float broadCell = floor(angle01 * 54.0);
                    float wobbleNoise = Hash(
                        broadCell
                        + (float)ringIndex * 71.0);
                    float wobble = (wobbleNoise - 0.5)
                                   * saturate(_Irregularity)
                                   * _LineWidth
                                   * 1.75;
                    float distanceToRing = abs(
                        radialDistance - targetRadius + wobble);
                    float ringLine = 1.0 - smoothstep(
                        max(0.0005, _LineWidth),
                        max(0.0005, _LineWidth) + antiAlias,
                        distanceToRing);

                    float segmentCount = lerp(
                        62.0,
                        94.0,
                        Hash((float)ringIndex * 19.0 + 3.0));
                    float segment = floor(
                        (angle01 + Hash((float)ringIndex * 11.0) * 0.07)
                        * segmentCount);
                    float segmentNoise = Hash(
                        segment
                        + (float)ringIndex * 113.0);
                    float irregularContinuity = lerp(
                        0.18,
                        1.0,
                        smoothstep(0.08, 0.78, segmentNoise));
                    float continuity = lerp(
                        1.0,
                        irregularContinuity,
                        saturate(_Breakup));
                    float grain = lerp(
                        0.62,
                        1.0,
                        Hash(
                            floor(angle01 * 240.0)
                            + (float)ringIndex * 157.0));
                    float ringBrightness = lerp(0.7, 1.0, ring01);
                    combinedRing = max(
                        combinedRing,
                        ringLine
                        * continuity
                        * grain
                        * ringBrightness
                        * enabled);
                }

                fixed4 color = _Color;
                color.a *= saturate(_Opacity) * saturate(combinedRing);
                clip(color.a - 0.002);
                return color;
            }
            ENDCG
        }
    }
}
