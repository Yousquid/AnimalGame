Shader "Animal Game/Unknown Animal Static"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _DarkColor ("Dark Static", Color) = (0.005, 0.005, 0.005, 1)
        _LightColor ("Light Static", Color) = (1, 1, 1, 1)
        _NoiseCells ("Large Noise Cells", Float) = 12
        _NoiseFps ("Base Noise Frames Per Second", Float) = 11
        _ScrollRate ("Field Flow Rate", Float) = 0.22
        _FieldCoverage ("Snow Field Coverage", Range(0.1, 1)) = 0.72
        _FieldRadius ("Snow Field Radius", Range(0.2, 0.65)) = 0.46
        _FieldDrift ("Snow Field Centre Drift", Range(0, 0.3)) = 0.16
        _BlockFill ("Snow Block Fill", Range(0.35, 1)) = 0.88
        _ClusterContrast ("Snow Cluster Contrast", Range(0, 2)) = 0.8
        [PerRendererData] _Seed ("Instance Seed", Float) = 0
        [PerRendererData] _RevealProgress ("Reveal Progress", Range(0, 1)) = 0
        [PerRendererData] _AnimationPhase ("Animation Phase", Float) = 0
        [PerRendererData] _ShapePhase ("Shape Phase", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _DarkColor;
            fixed4 _LightColor;
            float _NoiseCells;
            float _NoiseFps;
            float _ScrollRate;
            float _FieldCoverage;
            float _FieldRadius;
            float _FieldDrift;
            float _BlockFill;
            float _ClusterContrast;
            float _Seed;
            float _RevealProgress;
            float _AnimationPhase;
            float _ShapePhase;
            float _PlayerUiClipEnabled;
            float4 _PlayerUiClipCenterPixels;
            float _PlayerUiClipRadiusPixels;
            float _PlayerUiClipSoftnessPixels;

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 345.45));
                value += dot(value, value + 34.345);
                return frac(value.x * value.y);
            }

            float2 Hash22(float2 value)
            {
                return float2(
                    Hash21(value + float2(17.17, 43.71)),
                    Hash21(value + float2(83.31, 29.53)));
            }

            float ValueNoise(float2 samplePoint, float seedOffset)
            {
                float2 baseCell = floor(samplePoint);
                float2 localPoint = frac(samplePoint);
                localPoint = localPoint * localPoint
                    * (3.0 - 2.0 * localPoint);
                float2 seedVector = float2(
                    seedOffset,
                    seedOffset * 1.731 + _Seed * 0.137);
                float valueA = Hash21(baseCell + seedVector);
                float valueB = Hash21(
                    baseCell + float2(1.0, 0.0) + seedVector);
                float valueC = Hash21(
                    baseCell + float2(0.0, 1.0) + seedVector);
                float valueD = Hash21(
                    baseCell + float2(1.0, 1.0) + seedVector);
                return lerp(
                    lerp(valueA, valueB, localPoint.x),
                    lerp(valueC, valueD, localPoint.x),
                    localPoint.y);
            }

            float2 FlowOffset(float animationPhase)
            {
                float flowAngle = Hash21(float2(
                    _Seed * 2.71,
                    _Seed * 5.39 + 13.7)) * 6.28318530718;
                float2 primaryDirection = float2(
                    cos(flowAngle),
                    sin(flowAngle));
                float2 wandering = float2(
                    sin(animationPhase * 0.43 + _Seed * 4.17),
                    cos(animationPhase * 0.31 + _Seed * 6.83));
                return primaryDirection
                    * animationPhase
                    * _ScrollRate
                    + wandering * _FieldDrift;
            }

            float SnowDensityField(
                float2 centredPoint,
                float patternSeed,
                float2 flowOffset)
            {
                float2 randomCentre = Hash22(float2(
                    patternSeed * 3.71,
                    _Seed * 5.29 + patternSeed)) - 0.5;
                float2 liveWander = float2(
                    sin(_AnimationPhase * 0.61 + patternSeed * 1.37),
                    cos(_AnimationPhase * 0.47 + patternSeed * 2.11));
                float2 localPoint = centredPoint
                    - randomCentre * _FieldDrift
                    - liveWander * _FieldDrift * 0.32;

                float2 warp = float2(
                    ValueNoise(
                        localPoint * 3.7 + flowOffset * 0.21,
                        patternSeed + 17.3),
                    ValueNoise(
                        localPoint * 3.7 - flowOffset * 0.19,
                        patternSeed + 43.1)) - 0.5;
                localPoint += warp * 0.22;

                float aspect = lerp(
                    0.78,
                    1.24,
                    Hash21(float2(patternSeed * 7.13, _Seed + 71.7)));
                float2 aspectPoint = float2(
                    localPoint.x / aspect,
                    localPoint.y * aspect);
                float radiusVariation = lerp(
                    0.82,
                    1.08,
                    Hash21(float2(patternSeed * 9.31, _Seed + 89.7)));
                float envelope = 1.0 - smoothstep(
                    _FieldRadius * 0.43,
                    _FieldRadius * radiusVariation,
                    length(aspectPoint));

                float broadCluster = ValueNoise(
                    localPoint * 4.3 - flowOffset * 0.29,
                    patternSeed + 101.9);
                float smallBreakup = ValueNoise(
                    localPoint * 8.1 + flowOffset * 0.17,
                    patternSeed + 131.3);
                float localCoverage = _FieldCoverage
                    + (broadCluster - 0.5) * _ClusterContrast
                    + (smallBreakup - 0.5) * 0.42;
                return envelope * saturate(localCoverage);
            }

            float RectangularBlock(
                float2 gridPoint,
                float2 cellId,
                float salt)
            {
                float2 cellPoint = frac(gridPoint) - 0.5;
                float2 randomPair = Hash22(
                    cellId + float2(_Seed * 3.17 + salt, salt));
                float2 centreJitter = (randomPair - 0.5)
                    * (1.0 - _BlockFill) * 0.55;
                float2 fillVariation = lerp(
                    float2(_BlockFill * 0.66, _BlockFill * 0.66),
                    float2(_BlockFill, _BlockFill),
                    Hash22(cellId + float2(salt * 1.37, _Seed * 2.41)));
                float2 halfSize = fillVariation * 0.5;
                float2 inside = step(
                    abs(cellPoint - centreJitter),
                    halfSize);
                return inside.x * inside.y;
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float cells = max(4.0, _NoiseCells);
                float frameProgress = _AnimationPhase
                    * max(1.0, _NoiseFps);
                float frame = floor(frameProgress);
                float frameBlend = smoothstep(
                    0.0,
                    1.0,
                    frac(frameProgress));
                float2 flowOffset = FlowOffset(_AnimationPhase);
                float2 sampledUv = input.texcoord + flowOffset;
                float row = floor(sampledUv.y * cells);
                float rowNoise = Hash21(float2(
                    row + _Seed * 1.31,
                    frame * 0.37));
                float glitchGate = step(
                    0.82,
                    Hash21(float2(frame, _Seed * 1.91)));
                float glitchRow = glitchGate * step(0.54, rowNoise);
                sampledUv.x += (rowNoise - 0.5) * 0.24 * glitchRow;

                float2 centred = input.texcoord - 0.5;
                float patternIndex = floor(_ShapePhase);
                float patternMorph = smoothstep(
                    0.0,
                    1.0,
                    frac(_ShapePhase));
                float density = lerp(
                    SnowDensityField(
                        centred,
                        patternIndex + _Seed * 31.7,
                        flowOffset),
                    SnowDensityField(
                        centred,
                        patternIndex + 1.0 + _Seed * 31.7,
                        flowOffset),
                    patternMorph);

                float2 primaryGrid = sampledUv * cells;
                float2 primaryCell = floor(primaryGrid);
                float primaryBlock = RectangularBlock(
                    primaryGrid,
                    primaryCell,
                    19.7);
                float primaryRollA = Hash21(
                    primaryCell + float2(
                        frame * 1.21,
                        _Seed * 2.17));
                float primaryRollB = Hash21(
                    primaryCell + float2(
                        (frame + 1.0) * 1.21,
                        _Seed * 2.17));
                float primaryVisibleA = primaryBlock
                    * step(1.0 - density, primaryRollA);
                float primaryVisibleB = primaryBlock
                    * step(1.0 - density, primaryRollB);
                float primaryVisible = lerp(
                    primaryVisibleA,
                    primaryVisibleB,
                    frameBlend);
                float primaryToneA = step(
                    0.48,
                    Hash21(primaryCell + float2(
                        frame * 2.37,
                        _Seed * 5.13)));
                float primaryToneB = step(
                    0.48,
                    Hash21(primaryCell + float2(
                        (frame + 1.0) * 2.37,
                        _Seed * 5.13)));
                float primaryLight = lerp(
                    primaryVisibleA * primaryToneA,
                    primaryVisibleB * primaryToneB,
                    frameBlend);

                float2 largeGrid = (
                    sampledUv + float2(0.13, -0.09))
                    * cells * 0.57;
                float2 largeCell = floor(largeGrid);
                float largeBlock = RectangularBlock(
                    largeGrid,
                    largeCell,
                    47.3);
                float largeRollA = Hash21(
                    largeCell + float2(
                        frame * 0.73,
                        _Seed * 4.31 + 11.7));
                float largeRollB = Hash21(
                    largeCell + float2(
                        (frame + 1.0) * 0.73,
                        _Seed * 4.31 + 11.7));
                float largeVisibleA = largeBlock
                    * step(1.0 - density * 0.55, largeRollA);
                float largeVisibleB = largeBlock
                    * step(1.0 - density * 0.55, largeRollB);
                float largeVisible = lerp(
                    largeVisibleA,
                    largeVisibleB,
                    frameBlend);
                float largeToneA = step(
                    0.52,
                    Hash21(largeCell + float2(
                        frame * 1.67,
                        _Seed * 7.31)));
                float largeToneB = step(
                    0.52,
                    Hash21(largeCell + float2(
                        (frame + 1.0) * 1.67,
                        _Seed * 7.31)));
                float largeLight = lerp(
                    largeVisibleA * largeToneA,
                    largeVisibleB * largeToneB,
                    frameBlend);

                float2 bridgeGrid = sampledUv * cells
                    + float2(0.5, 0.5);
                float2 bridgeCell = floor(bridgeGrid);
                float bridgeBlock = RectangularBlock(
                    bridgeGrid,
                    bridgeCell,
                    73.9);
                float bridgeDensity = saturate(
                    density * 0.58
                    + step(0.001, density) * 0.04);
                float bridgeRollA = Hash21(
                    bridgeCell + float2(
                        frame * 1.43,
                        _Seed * 8.17 + 23.1));
                float bridgeRollB = Hash21(
                    bridgeCell + float2(
                        (frame + 1.0) * 1.43,
                        _Seed * 8.17 + 23.1));
                float bridgeVisibleA = bridgeBlock
                    * step(1.0 - bridgeDensity, bridgeRollA);
                float bridgeVisibleB = bridgeBlock
                    * step(1.0 - bridgeDensity, bridgeRollB);
                float bridgeVisible = lerp(
                    bridgeVisibleA,
                    bridgeVisibleB,
                    frameBlend);
                float bridgeToneA = step(
                    0.5,
                    Hash21(bridgeCell + float2(
                        frame * 1.91,
                        _Seed * 9.71)));
                float bridgeToneB = step(
                    0.5,
                    Hash21(bridgeCell + float2(
                        (frame + 1.0) * 1.91,
                        _Seed * 9.71)));
                float bridgeLight = lerp(
                    bridgeVisibleA * bridgeToneA,
                    bridgeVisibleB * bridgeToneB,
                    frameBlend);

                float snowAlpha = saturate(
                    max(
                        primaryVisible,
                        max(
                            largeVisible * 0.8,
                            bridgeVisible * 0.55)));
                float visibleWeight = primaryVisible
                    + largeVisible * 0.8
                    + bridgeVisible * 0.55;
                float snow = (
                    primaryLight
                    + largeLight * 0.8
                    + bridgeLight * 0.55)
                    / max(0.001, visibleWeight);
                snow = abs(snow - glitchRow);
                float edgeAlpha = lerp(
                    0.22,
                    1.0,
                    saturate(density * 1.75));
                float blockAlphaNoise = lerp(
                    0.72,
                    1.0,
                    Hash21(
                        primaryCell
                        + largeCell
                        + bridgeCell
                        + _Seed));

                float dissolveNoise = Hash21(
                    floor(input.texcoord * cells)
                    + _Seed * 3.17);
                float dissolve = step(_RevealProgress, dissolveNoise);

                fixed4 output = lerp(_DarkColor, _LightColor, snow);
                output.rgb *= input.color.rgb;
                output.a *= input.color.a
                    * snowAlpha
                    * edgeAlpha
                    * blockAlphaNoise
                    * dissolve;
                float2 screenUv = input.screenPosition.xy
                                  / max(0.0001, input.screenPosition.w);
                float2 screenPixels = screenUv * _ScreenParams.xy;
                float distanceFromCenter = length(
                    screenPixels - _PlayerUiClipCenterPixels.xy);
                float clipRadius = max(
                    0.0,
                    _PlayerUiClipRadiusPixels);
                float clipSoftness = max(
                    0.001,
                    _PlayerUiClipSoftnessPixels);
                float insideVisibility = 1.0 - smoothstep(
                    max(0.0, clipRadius - clipSoftness),
                    clipRadius + clipSoftness,
                    distanceFromCenter);
                output.a *= lerp(
                    1.0,
                    insideVisibility,
                    saturate(_PlayerUiClipEnabled));
                clip(output.a - 0.001);
                return output;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
