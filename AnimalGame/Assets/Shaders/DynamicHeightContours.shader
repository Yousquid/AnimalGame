Shader "AnimalGame/Dynamic Height Contours"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base Map", 2D) = "white" {}
        _HeightTex ("Height Map", 2D) = "black" {}
        _ContourColor ("Contour Color", Color) = (1, 1, 1, 1)
        _MinimumHeight ("Minimum Height", Float) = 0
        _MaximumHeight ("Maximum Height", Float) = 200
        _VisibleMinimumHeight ("Visible Minimum Contour", Float) = 0
        _VisibleMaximumHeight ("Visible Maximum Contour", Float) = 200
        _ContourInterval ("Contour Interval", Float) = 10
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Range(0, 1)) = 0.15
        _MaximumOpacity ("Maximum Opacity", Range(0, 1)) = 1
        _HeightSmoothing ("Height Smoothing", Range(0, 1)) = 0.65
        _HeightMipLevel ("Height Mip Level", Float) = 0
        _MaximumCoverage ("Maximum Contour Coverage", Range(0.1, 0.7)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0.1, 1.5)) = 0.4
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
            float _MinimumHeight;
            float _MaximumHeight;
            float _VisibleMinimumHeight;
            float _VisibleMaximumHeight;
            float _ContourInterval;
            float _MinimumLineWidth;
            float _MaximumLineWidth;
            float _MinimumOpacity;
            float _MaximumOpacity;
            float _HeightSmoothing;
            float _HeightMipLevel;
            float _MaximumCoverage;
            float _EdgeSoftness;
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
                float4 heightSample = float4(input.uv, 0.0, _HeightMipLevel);
                float sourceCenter = tex2Dlod(_HeightTex, heightSample).r;
                float2 texel = _HeightTex_TexelSize.xy * exp2(_HeightMipLevel);
                float sourceCrossAverage = (
                    tex2Dlod(_HeightTex, float4(input.uv + float2(texel.x, 0.0), 0.0, _HeightMipLevel)).r
                    + tex2Dlod(_HeightTex, float4(input.uv - float2(texel.x, 0.0), 0.0, _HeightMipLevel)).r
                    + tex2Dlod(_HeightTex, float4(input.uv + float2(0.0, texel.y), 0.0, _HeightMipLevel)).r
                    + tex2Dlod(_HeightTex, float4(input.uv - float2(0.0, texel.y), 0.0, _HeightMipLevel)).r) * 0.25;
                float sourceBlurred = lerp(sourceCenter, sourceCrossAverage, 0.5);
                float sourceGray = lerp(sourceCenter, sourceBlurred, saturate(_HeightSmoothing));
                float normalizedHeight = saturate(
                    (sourceGray - _SourceMinimum) / max(0.0001, _SourceMaximum - _SourceMinimum));
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, normalizedHeight);

                float interval = max(0.0001, _ContourInterval);
                float contourCoordinate = (heightMeters - _MinimumHeight) / interval;
                float nearestContourDistance = abs(frac(contourCoordinate + 0.5) - 0.5);
                float contourHeight = round(contourCoordinate) * interval + _MinimumHeight;

                float visibleHeightRange = _VisibleMaximumHeight - _VisibleMinimumHeight;
                float visibleHeightProgress = visibleHeightRange > 0.0001
                    ? saturate((contourHeight - _VisibleMinimumHeight) / visibleHeightRange)
                    : 1.0;

                // The camera-visible lowest line is always 15% and thinnest;
                // the camera-visible highest line is always 100% and thickest.
                float lineWidth = lerp(
                    _MinimumLineWidth,
                    _MaximumLineWidth,
                    visibleHeightProgress);
                float lineOpacity = lerp(
                    _MinimumOpacity,
                    _MaximumOpacity,
                    visibleHeightProgress);
                float derivativeWidth = max(fwidth(contourCoordinate), 0.00001);
                float requestedHalfWidth = derivativeWidth * lineWidth * 0.5;
                float maximumHalfWidth = saturate(_MaximumCoverage) * 0.5;
                float widestRequestedHalfWidth =
                    derivativeWidth * max(0.0001, _MaximumLineWidth) * 0.5;
                float sharedWidthScale = min(
                    1.0,
                    maximumHalfWidth / max(0.00001, widestRequestedHalfWidth));
                float halfWidth = requestedHalfWidth * sharedWidthScale;
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
