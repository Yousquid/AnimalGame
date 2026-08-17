Shader "Hidden/AnimalGame/Bake Terrain Surface Palette"
{
    Properties
    {
        _PatternAtlas ("Pattern Atlas", 2D) = "black" {}
        _TerrainPairTex ("Terrain Pair Distance Map", 2D) = "black" {}
        _StaticContourAlphaTex ("Static Closed-Contour Alpha", 2D) = "white" {}
        _PaletteTintTex ("Palette Tint", 2D) = "black" {}
        _PaletteSettingsTex ("Palette Settings", 2D) = "black" {}
        _MapSizeMeters ("Map Size Metres", Vector) = (250, 250, 0, 0)
        _MaximumBoundaryDistanceMeters ("Maximum Boundary Distance", Float) = 4
        _TransitionWidthMeters ("Transition Width", Float) = 3
        _AlphaCoreWidthMeters ("Alpha Core Width", Float) = 0.8
        _AlphaBlendShare ("Alpha Blend Share", Range(0, 1)) = 0.6
        _BoundaryNoiseScaleMeters ("Boundary Noise Scale", Float) = 5
        _BoundaryNoiseAmplitudeMeters ("Boundary Noise Amplitude", Float) = 0.8
        _ScatterCellSizeMeters ("Boundary Detail Scale", Float) = 0.5
        _ScatterStrength ("Boundary Detail Strength", Range(0, 1)) = 0.35
        _NoiseSeed ("Noise Seed", Float) = 1337
        _StaticContourAlphaEnabled ("Static Contour Alpha Enabled", Float) = 0
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

            sampler2D _PatternAtlas;
            sampler2D _TerrainPairTex;
            sampler2D _StaticContourAlphaTex;
            sampler2D _PaletteTintTex;
            sampler2D _PaletteSettingsTex;
            float4 _MapSizeMeters;
            float _MaximumBoundaryDistanceMeters;
            float _TransitionWidthMeters;
            float _AlphaCoreWidthMeters;
            float _AlphaBlendShare;
            float _BoundaryNoiseScaleMeters;
            float _BoundaryNoiseAmplitudeMeters;
            float _ScatterCellSizeMeters;
            float _ScatterStrength;
            float _NoiseSeed;
            float _StaticContourAlphaEnabled;

            float2 PaletteUv(float terrainId)
            {
                return float2((terrainId + 0.5) / 256.0, 0.5);
            }

            float4 ReadPaletteTint(float terrainId)
            {
                return tex2D(_PaletteTintTex, PaletteUv(terrainId));
            }

            float4 ReadPaletteSettings(float terrainId)
            {
                return tex2D(_PaletteSettingsTex, PaletteUv(terrainId));
            }

            float Hash21(float2 value)
            {
                float3 fractional = frac(float3(value.xyx) * 0.1031);
                fractional += dot(fractional, fractional.yzx + 33.33);
                return frac((fractional.x + fractional.y) * fractional.z);
            }

            float ValueNoise(float2 position)
            {
                float2 integerPart = floor(position);
                float2 fractionalPart = frac(position);
                fractionalPart = fractionalPart * fractionalPart
                    * (3.0 - 2.0 * fractionalPart);
                float lowerLeft = Hash21(integerPart);
                float lowerRight = Hash21(integerPart + float2(1.0, 0.0));
                float upperLeft = Hash21(integerPart + float2(0.0, 1.0));
                float upperRight = Hash21(integerPart + float2(1.0, 1.0));
                return lerp(
                    lerp(lowerLeft, lowerRight, fractionalPart.x),
                    lerp(upperLeft, upperRight, fractionalPart.x),
                    fractionalPart.y);
            }

            float ResolveTransitionMode(
                float primaryId,
                float secondaryId,
                float primaryMode,
                float secondaryMode)
            {
                if (primaryId < 0.5)
                    return secondaryMode;
                if (secondaryId < 0.5)
                    return primaryMode;

                // Hard on either side remains a deliberately sharp authored edge.
                if (primaryMode < 0.5 || secondaryMode < 0.5)
                    return 0.0;
                if (primaryMode > 2.5 || secondaryMode > 2.5)
                    return 3.0;
                if (abs(primaryMode - secondaryMode) < 0.5)
                    return primaryMode;

                // Alpha meeting Noisy naturally becomes the combined mode.
                return 3.0;
            }

            float4 SampleTerrainPattern(float terrainId, float2 mapMeters)
            {
                if (terrainId < 0.5)
                    return float4(0.0, 0.0, 0.0, 0.0);

                float4 settings = ReadPaletteSettings(terrainId);
                float tileSizeMeters = max(0.01, settings.r);
                float2 repeatedUv = frac(mapMeters / tileSizeMeters);
                float cellX = fmod(terrainId, 16.0);
                float cellY = floor(terrainId / 16.0);

                // Every 128px atlas cell has two repeated edge pixels on each
                // side. Sampling its 124px interior prevents bilinear bleeding
                // between adjacent terrain entries.
                float2 atlasPixel = float2(cellX, cellY) * 128.0
                    + 2.5
                    + repeatedUv * 123.0;
                float4 pattern = tex2D(_PatternAtlas, atlasPixel / 2048.0);
                float4 tint = ReadPaletteTint(terrainId);
                pattern.rgb *= tint.rgb;
                pattern.a *= tint.a;
                return pattern;
            }

            float4 BlendPremultiplied(
                float4 primary,
                float4 secondary,
                float blendAmount)
            {
                float4 primaryPremultiplied = float4(
                    primary.rgb * primary.a,
                    primary.a);
                float4 secondaryPremultiplied = float4(
                    secondary.rgb * secondary.a,
                    secondary.a);
                float4 result = lerp(
                    primaryPremultiplied,
                    secondaryPremultiplied,
                    saturate(blendAmount));
                result.rgb = result.a > 0.00001
                    ? result.rgb / result.a
                    : float3(0.0, 0.0, 0.0);
                return result;
            }

            float4 ApplyStaticContourAlpha(float4 color, float2 uv)
            {
                float bakedMultiplier = tex2D(
                    _StaticContourAlphaTex,
                    uv).r;
                float multiplier = lerp(
                    1.0,
                    bakedMultiplier,
                    saturate(_StaticContourAlphaEnabled));
                color.a = saturate(color.a * multiplier);
                return color;
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 mapMeters = input.uv * _MapSizeMeters.xy;
                float4 pairData = tex2D(_TerrainPairTex, input.uv);
                float primaryId = floor(pairData.r * 255.0 + 0.5);
                float secondaryId = floor(pairData.g * 255.0 + 0.5);
                float hasSecondary = step(0.5, pairData.a);
                float4 primary = SampleTerrainPattern(primaryId, mapMeters);
                if (hasSecondary < 0.5)
                    return ApplyStaticContourAlpha(primary, input.uv);

                float4 primarySettings = ReadPaletteSettings(primaryId);
                float4 secondarySettings = ReadPaletteSettings(secondaryId);
                float primaryMode = floor(primarySettings.g + 0.5);
                float secondaryMode = floor(secondarySettings.g + 0.5);
                float transitionMode = ResolveTransitionMode(
                    primaryId,
                    secondaryId,
                    primaryMode,
                    secondaryMode);
                if (transitionMode < 0.5)
                    return ApplyStaticContourAlpha(primary, input.uv);

                float distanceMeters = pairData.b
                    * _MaximumBoundaryDistanceMeters;
                float averageWidthMultiplier = max(
                    0.25,
                    0.5 * (primarySettings.b + secondarySettings.b));
                float transitionWidth = max(
                    0.001,
                    _TransitionWidthMeters * averageWidthMultiplier);
                float pairMinimum = min(primaryId, secondaryId);
                float pairMaximum = max(primaryId, secondaryId);
                float2 pairOffset = float2(
                    pairMinimum * 19.19 + pairMaximum * 3.17,
                    pairMinimum * 7.73 + pairMaximum * 23.41);
                float2 seedOffset = float2(
                    _NoiseSeed * 0.071,
                    _NoiseSeed * -0.113);
                float broadNoise = ValueNoise(
                    mapMeters / max(0.1, _BoundaryNoiseScaleMeters)
                    + pairOffset
                    + seedOffset) * 2.0 - 1.0;
                float detailNoise = ValueNoise(
                    mapMeters / max(0.05, _ScatterCellSizeMeters)
                    + pairOffset * 5.31
                    + seedOffset * 11.7) * 2.0 - 1.0;
                float averageNoiseMultiplier = max(
                    0.0,
                    0.5 * (primarySettings.a + secondarySettings.a));
                // Both noise bands only displace the single terrain boundary.
                // The previous implementation used a random full-texture pick
                // in every small cell, which cut sparse patterns (such as the
                // grid) into isolated dashes and crosses.
                float combinedBoundaryNoise = broadNoise
                    + detailNoise * saturate(_ScatterStrength) * 0.35;
                float boundaryOffset = combinedBoundaryNoise
                    * _BoundaryNoiseAmplitudeMeters
                    * averageNoiseMultiplier;

                // The canonical sign makes both sides of the same ID pair use
                // the same displaced boundary instead of two unrelated edges.
                float canonicalSign = primaryId < secondaryId ? 1.0 : -1.0;
                float canonicalSignedDistance = canonicalSign * distanceMeters
                    + boundaryOffset;
                float adjustedPrimaryDistance = canonicalSign
                    * canonicalSignedDistance;
                float smoothSecondary = saturate(
                    0.5 - adjustedPrimaryDistance / transitionWidth);
                float noisyBoundarySecondary = 1.0
                    - step(0.0, adjustedPrimaryDistance);

                float blendAmount;
                if (transitionMode < 1.5)
                {
                    blendAmount = smoothSecondary;
                }
                else if (transitionMode < 2.5)
                {
                    // Noisy means one coherent, irregular hard boundary. It no
                    // longer scatters disconnected fragments through the belt.
                    blendAmount = noisyBoundarySecondary;
                }
                else
                {
                    float alphaCoreWidth = max(
                        0.001,
                        _AlphaCoreWidthMeters
                        * averageWidthMultiplier);
                    float hybridWidth = lerp(
                        alphaCoreWidth,
                        transitionWidth,
                        saturate(_AlphaBlendShare));
                    // Hybrid keeps the noisy silhouette but performs a
                    // continuous premultiplied-alpha fade across it. Because
                    // the complete pattern is sampled before this fade, its
                    // lines remain connected right up to the boundary.
                    blendAmount = saturate(
                        0.5 - adjustedPrimaryDistance / hybridWidth);
                }

                float4 secondary = SampleTerrainPattern(
                    secondaryId,
                    mapMeters);
                return ApplyStaticContourAlpha(
                    BlendPremultiplied(
                        primary,
                        secondary,
                        blendAmount),
                    input.uv);
            }
            ENDCG
        }
    }
}
