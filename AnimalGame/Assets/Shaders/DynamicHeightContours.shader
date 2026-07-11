Shader "AnimalGame/Dynamic Height Contours"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base Map", 2D) = "white" {}
        _HeightTex ("Height Map", 2D) = "black" {}
        _ContourColor ("Contour Color", Color) = (1, 1, 1, 1)
        _ReferenceHeight ("Reference Height", Float) = 0
        _MinimumHeight ("Minimum Height", Float) = 0
        _MaximumHeight ("Maximum Height", Float) = 200
        _ContourInterval ("Contour Interval", Float) = 10
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Range(0, 1)) = 0.03
        _CurrentHeightOpacity ("Current Height Opacity", Range(0, 1)) = 0.28
        _MaximumOpacity ("Maximum Opacity", Range(0, 1)) = 1
        _HeightSmoothing ("Height Smoothing", Range(0, 1)) = 0.65
        _MaximumCoverage ("Maximum Contour Coverage", Range(0.1, 0.7)) = 0.45
        _BelowOpacityCurve ("Below Player Opacity Curve", Range(0.25, 8)) = 1
        _OpacityCurve ("Opacity Curve", Range(0.25, 8)) = 2.5
        _EdgeSoftness ("Edge Softness", Range(0.1, 1.5)) = 0.4
        _LevelSeparation ("Contour Level Separation", Range(0, 1)) = 0.6
        _SourceMinimum ("Source Minimum", Float) = 0
        _SourceMaximum ("Source Maximum", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
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
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            sampler2D _HeightTex;
            float4 _HeightTex_TexelSize;
            fixed4 _ContourColor;
            float _ReferenceHeight;
            float _MinimumHeight;
            float _MaximumHeight;
            float _ContourInterval;
            float _MinimumLineWidth;
            float _MaximumLineWidth;
            float _MinimumOpacity;
            float _CurrentHeightOpacity;
            float _MaximumOpacity;
            float _HeightSmoothing;
            float _MaximumCoverage;
            float _BelowOpacityCurve;
            float _OpacityCurve;
            float _EdgeSoftness;
            float _LevelSeparation;
            float _SourceMinimum;
            float _SourceMaximum;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 baseColor = tex2D(_MainTex, input.uv) * input.color;
                float sourceCenter = tex2D(_HeightTex, input.uv).r;
                float2 texel = _HeightTex_TexelSize.xy;
                float sourceCrossAverage = (
                    tex2D(_HeightTex, input.uv + float2(texel.x, 0.0)).r
                    + tex2D(_HeightTex, input.uv - float2(texel.x, 0.0)).r
                    + tex2D(_HeightTex, input.uv + float2(0.0, texel.y)).r
                    + tex2D(_HeightTex, input.uv - float2(0.0, texel.y)).r) * 0.25;
                float sourceBlurred = lerp(sourceCenter, sourceCrossAverage, 0.5);
                float sourceGray = lerp(sourceCenter, sourceBlurred, saturate(_HeightSmoothing));
                float normalizedHeight = saturate(
                    (sourceGray - _SourceMinimum) / max(0.0001, _SourceMaximum - _SourceMinimum));
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, normalizedHeight);

                float interval = max(0.0001, _ContourInterval);
                float contourCoordinate = (heightMeters - _MinimumHeight) / interval;
                float nearestContourDistance = abs(frac(contourCoordinate + 0.5) - 0.5);
                float contourHeight = round(contourCoordinate) * interval + _MinimumHeight;

                float distanceAbove = max(0.0, contourHeight - _ReferenceHeight);
                float distanceBelow = max(0.0, _ReferenceHeight - contourHeight);
                float aboveRatio = saturate(
                    distanceAbove / max(0.0001, _MaximumHeight - _ReferenceHeight));
                float belowRatio = saturate(
                    distanceBelow / max(0.0001, _ReferenceHeight - _MinimumHeight));
                float relativeHeight = contourHeight >= _ReferenceHeight
                    ? lerp(0.5, 1.0, aboveRatio)
                    : lerp(0.5, 0.0, belowRatio);

                // Lower lines: thin and transparent. Higher lines: thick and opaque.
                float lineWidth = lerp(_MinimumLineWidth, _MaximumLineWidth, relativeHeight);
                float aboveOpacityProgress = pow(aboveRatio, max(0.01, _OpacityCurve));
                float belowOpacityProgress = pow(belowRatio, max(0.01, _BelowOpacityCurve));
                float relativeLineOpacity = contourHeight >= _ReferenceHeight
                    ? lerp(_CurrentHeightOpacity, _MaximumOpacity, aboveOpacityProgress)
                    : lerp(_CurrentHeightOpacity, _MinimumOpacity, belowOpacityProgress);
                float absoluteHeightProgress = saturate(
                    (contourHeight - _MinimumHeight) / max(0.0001, _MaximumHeight - _MinimumHeight));
                float absoluteLineOpacity = lerp(
                    _MinimumOpacity,
                    _MaximumOpacity,
                    absoluteHeightProgress);
                float lineOpacity = lerp(
                    relativeLineOpacity,
                    absoluteLineOpacity,
                    saturate(_LevelSeparation));
                float derivativeWidth = max(fwidth(contourCoordinate), 0.00001);
                float requestedHalfWidth = derivativeWidth * lineWidth * 0.5;
                float maximumHalfWidth = saturate(_MaximumCoverage) * 0.5;
                float halfWidth = min(requestedHalfWidth, maximumHalfWidth);
                float antiAliasWidth = min(derivativeWidth * _EdgeSoftness, 0.035);
                float lineMask = 1.0 - smoothstep(
                    halfWidth,
                    min(halfWidth + antiAliasWidth, 0.48),
                    nearestContourDistance);

                float blendAmount = saturate(lineMask * lineOpacity * _ContourColor.a);
                baseColor.rgb = lerp(baseColor.rgb, _ContourColor.rgb, blendAmount);
                return baseColor;
            }
            ENDCG
        }
    }
}
