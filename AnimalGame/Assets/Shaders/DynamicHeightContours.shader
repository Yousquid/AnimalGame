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
        _WaterMaskTex ("Static Water Range And Depth", 2D) = "black" {}
        _WaterPatternTex ("Animated Water Pattern", 2D) = "black" {}
        _WaterEnabled ("Animated Water Enabled", Float) = 0
        _MapSizeMeters ("Map Size Meters", Vector) = (250, 250, 0, 0)
        _WaterTileSizeMeters ("Water Tile Size Meters", Float) = 8
        _WaterLayerOneSpeed ("Water Primary Speed", Vector) = (0.36, 0.04, 0, 0)
        _WaterLayerTwoSpeed ("Water Secondary Speed", Vector) = (-0.14, 0.22, 0, 0)
        _WaterLayerTwoScale ("Water Secondary Scale", Float) = 1.35
        _WaterWaveDistortion ("Water Wave Distortion", Float) = 0.03
        _WaterWaveSpeed ("Water Wave Speed", Float) = 0.8
        _WaterWaveLengthMeters ("Water Wave Length Meters", Float) = 12
        _WaterDeepSpeedMultiplier ("Water Deep Speed Multiplier", Float) = 0.65
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
            sampler2D _WaterMaskTex;
            sampler2D _WaterPatternTex;
            float _SurfaceEnabled;
            float _SurfaceRevealEnabled;
            float4 _SurfaceRevealCenterPixels;
            float _SurfaceRevealRadiusPixels;
            float _SurfaceRevealEdgePixels;
            float _WaterEnabled;
            float4 _MapSizeMeters;
            float _WaterTileSizeMeters;
            float4 _WaterLayerOneSpeed;
            float4 _WaterLayerTwoSpeed;
            float _WaterLayerTwoScale;
            float _WaterWaveDistortion;
            float _WaterWaveSpeed;
            float _WaterWaveLengthMeters;
            float _WaterDeepSpeedMultiplier;
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

            float2 RotateWaterUv(float2 value)
            {
                // A fixed 23-degree turn prevents the second layer from
                // repeatedly lining up with the first layer's sparse dashes.
                return float2(
                    value.x * 0.920505 - value.y * 0.390731,
                    value.x * 0.390731 + value.y * 0.920505);
            }

            fixed4 SampleWaterPattern(
                float2 mapMeters,
                float normalizedDepth,
                float animationTime)
            {
                float depth = saturate(normalizedDepth);
                float tileSize = max(0.01, _WaterTileSizeMeters);
                float waveLength = max(0.1, _WaterWaveLengthMeters);
                float2 waveCoordinate = mapMeters / waveLength;
                float2 distortion = float2(
                    sin(waveCoordinate.y * 6.283185
                        + animationTime * _WaterWaveSpeed),
                    cos(waveCoordinate.x * 6.283185
                        - animationTime * _WaterWaveSpeed * 0.83))
                    * _WaterWaveDistortion;

                float2 primaryUv = frac(
                    (mapMeters
                        + _WaterLayerOneSpeed.xy * animationTime)
                    / tileSize
                    + distortion);
                float2 secondaryUv = frac(
                    RotateWaterUv(
                        mapMeters / tileSize
                        * max(0.1, _WaterLayerTwoScale))
                    + _WaterLayerTwoSpeed.xy * animationTime / tileSize
                    - distortion * 0.73);
                fixed4 primary = tex2D(_WaterPatternTex, primaryUv);
                fixed4 secondary = tex2D(_WaterPatternTex, secondaryUv);
                float secondaryStrength = 0.65;
                float combinedAlpha = 1.0
                    - (1.0 - primary.a)
                    * (1.0 - secondary.a * secondaryStrength);
                float3 combinedRgb = max(
                    primary.rgb,
                    secondary.rgb * secondaryStrength);
                float depthOpacity = lerp(0.2, 1.0, depth);
                return fixed4(
                    combinedRgb,
                    combinedAlpha * depthOpacity);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 baseColor = tex2D(_MainTex, input.uv) * input.color;

                // Terrain remains one Editor-baked texture and is revealed only
                // inside the player UI. Water is a persistent natural feature:
                // it stays visible everywhere, while this same reveal mask only
                // selects whether its pattern is static or animated.
                if (_SurfaceEnabled > 0.5 || _WaterEnabled > 0.5)
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

                    fixed4 waterData = _WaterEnabled > 0.5
                        ? tex2D(_WaterMaskTex, input.uv)
                        : fixed4(0.0, 0.0, 0.0, 0.0);
                    float waterRange = _WaterEnabled > 0.5
                        ? smoothstep(0.02, 0.98, waterData.r)
                        : 0.0;

                    fixed4 terrainSurface = _SurfaceEnabled > 0.5
                        ? tex2D(_SurfaceTex, input.uv)
                        : fixed4(0.0, 0.0, 0.0, 0.0);
                    // Authored water owns its complete range, so terrain never
                    // leaks through transparent gaps in the water pattern.
                    float terrainBlend = saturate(
                        terrainSurface.a
                        * revealMask
                        * (1.0 - waterRange));
                    baseColor.rgb = lerp(
                        baseColor.rgb,
                        terrainSurface.rgb,
                        terrainBlend);

                    if (_WaterEnabled > 0.5)
                    {
                        // R is a binary authored range. G preserves normalized
                        // depth so deeper water can move more slowly and remain
                        // more opaque without changing traversal data.
                        float2 mapMeters = input.uv * _MapSizeMeters.xy;
                        float depthSpeed = lerp(
                            1.0,
                            saturate(_WaterDeepSpeedMultiplier),
                            saturate(waterData.g));
                        fixed4 staticWater = SampleWaterPattern(
                            mapMeters,
                            waterData.g,
                            0.0);
                        fixed4 animatedWater = SampleWaterPattern(
                            mapMeters,
                            waterData.g,
                            _Time.y * depthSpeed);
                        fixed4 visibleWater = lerp(
                            staticWater,
                            animatedWater,
                            revealMask);

                        // Water visibility is intentionally independent from the
                        // reveal mask. Outside the player's view it uses the fixed
                        // time-zero sample; inside it crossfades to moving water.
                        float waterBlend = saturate(
                            visibleWater.a * waterRange);
                        baseColor.rgb = lerp(
                            baseColor.rgb,
                            visibleWater.rgb,
                            waterBlend);
                    }
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
