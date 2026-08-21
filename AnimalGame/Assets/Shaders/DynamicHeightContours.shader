Shader "AnimalGame/Dynamic Height Contours"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base Map", 2D) = "white" {}
        _HeightTex ("Height Map", 2D) = "black" {}
        _SurfaceTex ("Editor-Baked Terrain Surface", 2D) = "black" {}
        _SurfaceEnabled ("Surface Enabled", Float) = 0
        _SurfaceRevealEnabled ("Surface UI Reveal Enabled", Float) = 0
        _SurfaceRevealCenterPixels ("Surface UI Centre", Vector) = (0, 0, 0, 0)
        _SurfaceRevealRadiusPixels ("Surface UI Radius", Float) = 430
        _SurfaceRevealEdgePixels ("Surface UI Edge", Float) = 4
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
        _MaximumCoverage ("Maximum Contour Coverage", Range(0.1, 0.7)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0.1, 1.5)) = 0.4
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
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _HeightTex;
            sampler2D _SurfaceTex;
            float _SurfaceEnabled;
            float _SurfaceRevealEnabled;
            float4 _SurfaceRevealCenterPixels;
            float _SurfaceRevealRadiusPixels;
            float _SurfaceRevealEdgePixels;
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
            float _MaximumCoverage;
            float _EdgeSoftness;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 baseColor = tex2D(_MainTex, input.uv) * input.color;

                // The terrain artwork is permanently composed into one RGBA asset
                // by the Editor. Runtime performs no tiling, painting, region lookup,
                // or texture generation: this is one static lookup in the map's
                // existing draw call. Only its player-UI cutoff remains dynamic.
                if (_SurfaceEnabled > 0.5)
                {
                    float revealMask = 1.0;
                    if (_SurfaceRevealEnabled > 0.5)
                    {
                        float2 screenUv = input.screenPosition.xy
                                          / max(input.screenPosition.w, 0.00001);
                        float2 pixelPosition = screenUv * _ScreenParams.xy;
                        float distanceFromUiCentre = distance(
                            pixelPosition,
                            _SurfaceRevealCenterPixels.xy);
                        float revealRadius = max(1.0, _SurfaceRevealRadiusPixels);
                        float revealEdge = min(
                            max(0.0, _SurfaceRevealEdgePixels),
                            revealRadius);
                        revealMask = revealEdge > 0.0001
                            ? 1.0 - smoothstep(
                                revealRadius - revealEdge,
                                revealRadius,
                                distanceFromUiCentre)
                            : step(distanceFromUiCentre, revealRadius);
                    }

                    fixed4 bakedSurface = tex2D(_SurfaceTex, input.uv);
                    float surfaceBlend = saturate(
                        bakedSurface.a * revealMask);
                    baseColor.rgb = lerp(
                        baseColor.rgb,
                        bakedSurface.rgb,
                        surfaceBlend);
                }

                // This texture is the same normalized physical surface sampled by
                // movement and traversal UI. Only screen-space antialiasing below may
                // alter presentation; contour positions never use a separate blur/LOD.
                float normalizedHeight = saturate(tex2D(_HeightTex, input.uv).r);
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
