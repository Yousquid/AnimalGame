using System;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Runtime-baked physical height field shared by rendering and gameplay.
    /// The detail channel retains the resampled 8-bit source for step detection,
    /// while the surface channel removes sub-footprint quantization for slopes.
    /// </summary>
    public sealed class BakedHeightField : IDisposable
    {
        private readonly float[] detailHeightsMeters;
        private readonly float[] surfaceHeightsMeters;

        public int Width { get; }
        public int Height { get; }
        public Vector2 MapSizeMeters { get; }
        public float MinimumHeightMeters { get; }
        public float MaximumHeightMeters { get; }
        public float SourceMinimum { get; }
        public float SourceMaximum { get; }
        public Texture2D SurfaceTexture { get; private set; }

        public float TexelSizeXMeters =>
            MapSizeMeters.x / Mathf.Max(1, Width - 1);

        public float TexelSizeYMeters =>
            MapSizeMeters.y / Mathf.Max(1, Height - 1);

        private BakedHeightField(
            int width,
            int height,
            Vector2 mapSizeMeters,
            float minimumHeightMeters,
            float maximumHeightMeters,
            float sourceMinimum,
            float sourceMaximum,
            float[] detailHeights,
            float[] surfaceHeights,
            Texture2D surfaceTexture)
        {
            Width = width;
            Height = height;
            MapSizeMeters = mapSizeMeters;
            MinimumHeightMeters = minimumHeightMeters;
            MaximumHeightMeters = maximumHeightMeters;
            SourceMinimum = sourceMinimum;
            SourceMaximum = sourceMaximum;
            detailHeightsMeters = detailHeights;
            surfaceHeightsMeters = surfaceHeights;
            SurfaceTexture = surfaceTexture;
        }

        public static BakedHeightField Bake(
            Texture2D source,
            int requestedResolution,
            Vector2 mapSizeMeters,
            float minimumHeightMeters,
            float maximumHeightMeters,
            bool normalizeSourceRange,
            float smoothingSigmaMeters)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            int resolution = Mathf.Clamp(requestedResolution, 64, 4096);
            Color32[] sourcePixels = source.GetPixels32();
            float sourceMinimum = 1f;
            float sourceMaximum = 0f;

            for (int index = 0; index < sourcePixels.Length; index++)
            {
                float gray = GetGrayscale(sourcePixels[index]);
                sourceMinimum = Mathf.Min(sourceMinimum, gray);
                sourceMaximum = Mathf.Max(sourceMaximum, gray);
            }

            if (!normalizeSourceRange)
            {
                sourceMinimum = 0f;
                sourceMaximum = 1f;
            }

            float[] detailHeights = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = y / (float)Mathf.Max(1, resolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)Mathf.Max(1, resolution - 1);
                    float sourceGray = SampleSource(
                        sourcePixels,
                        source.width,
                        source.height,
                        u,
                        v);
                    float normalized = Mathf.InverseLerp(
                        sourceMinimum,
                        sourceMaximum,
                        sourceGray);
                    detailHeights[y * resolution + x] = Mathf.Lerp(
                        minimumHeightMeters,
                        maximumHeightMeters,
                        normalized);
                }
            }

            float texelSizeXMeters = mapSizeMeters.x / Mathf.Max(1, resolution - 1);
            float texelSizeYMeters = mapSizeMeters.y / Mathf.Max(1, resolution - 1);
            float sigmaPixelsX = smoothingSigmaMeters /
                                 Mathf.Max(0.0001f, texelSizeXMeters);
            float sigmaPixelsY = smoothingSigmaMeters /
                                 Mathf.Max(0.0001f, texelSizeYMeters);
            float[] surfaceHeights = GaussianBlur(
                detailHeights,
                resolution,
                resolution,
                sigmaPixelsX,
                sigmaPixelsY);

            Texture2D surfaceTexture = CreateSurfaceTexture(
                surfaceHeights,
                resolution,
                resolution,
                minimumHeightMeters,
                maximumHeightMeters);

            return new BakedHeightField(
                resolution,
                resolution,
                mapSizeMeters,
                minimumHeightMeters,
                maximumHeightMeters,
                sourceMinimum,
                sourceMaximum,
                detailHeights,
                surfaceHeights,
                surfaceTexture);
        }

        public float SampleSurfaceHeight(Vector2 uv)
        {
            return SampleBilinear(surfaceHeightsMeters, uv);
        }

        public float SampleDetailHeight(Vector2 uv)
        {
            return SampleBilinear(detailHeightsMeters, uv);
        }

        /// <summary>
        /// Reads one sample from the same smoothed physical height field used by
        /// contour rendering and traversal. This is intended for one-time spatial
        /// indexing; normal gameplay queries should continue to use bilinear samples.
        /// </summary>
        public float GetSurfaceHeightSample(int x, int y)
        {
            int clampedX = Mathf.Clamp(x, 0, Width - 1);
            int clampedY = Mathf.Clamp(y, 0, Height - 1);
            return surfaceHeightsMeters[clampedY * Width + clampedX];
        }

        public void Dispose()
        {
            if (SurfaceTexture != null)
            {
                UnityEngine.Object.Destroy(SurfaceTexture);
                SurfaceTexture = null;
            }
        }

        private float SampleBilinear(float[] values, Vector2 uv)
        {
            float pixelX = Mathf.Clamp01(uv.x) * (Width - 1);
            float pixelY = Mathf.Clamp01(uv.y) * (Height - 1);
            int x0 = Mathf.FloorToInt(pixelX);
            int y0 = Mathf.FloorToInt(pixelY);
            int x1 = Mathf.Min(x0 + 1, Width - 1);
            int y1 = Mathf.Min(y0 + 1, Height - 1);
            float tx = pixelX - x0;
            float ty = pixelY - y0;

            float bottom = Mathf.Lerp(
                values[y0 * Width + x0],
                values[y0 * Width + x1],
                tx);
            float top = Mathf.Lerp(
                values[y1 * Width + x0],
                values[y1 * Width + x1],
                tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float GetGrayscale(Color32 color)
        {
            return (color.r * 0.299f + color.g * 0.587f + color.b * 0.114f) / 255f;
        }

        private static float SampleSource(
            Color32[] pixels,
            int width,
            int height,
            float u,
            float v)
        {
            float pixelX = Mathf.Clamp01(u) * (width - 1);
            float pixelY = Mathf.Clamp01(v) * (height - 1);
            int x0 = Mathf.FloorToInt(pixelX);
            int y0 = Mathf.FloorToInt(pixelY);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = pixelX - x0;
            float ty = pixelY - y0;

            float bottom = Mathf.Lerp(
                GetGrayscale(pixels[y0 * width + x0]),
                GetGrayscale(pixels[y0 * width + x1]),
                tx);
            float top = Mathf.Lerp(
                GetGrayscale(pixels[y1 * width + x0]),
                GetGrayscale(pixels[y1 * width + x1]),
                tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float[] GaussianBlur(
            float[] source,
            int width,
            int height,
            float sigmaPixelsX,
            float sigmaPixelsY)
        {
            if (sigmaPixelsX <= 0.01f && sigmaPixelsY <= 0.01f)
                return (float[])source.Clone();

            float[] horizontalKernel = BuildGaussianKernel(sigmaPixelsX);
            float[] verticalKernel = BuildGaussianKernel(sigmaPixelsY);
            float[] horizontal = new float[source.Length];
            float[] output = new float[source.Length];
            int horizontalRadius = horizontalKernel.Length / 2;
            int verticalRadius = verticalKernel.Length / 2;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float value = 0f;
                    for (int offset = -horizontalRadius;
                         offset <= horizontalRadius;
                         offset++)
                    {
                        int sampleX = Mathf.Clamp(x + offset, 0, width - 1);
                        value += source[row + sampleX] *
                                 horizontalKernel[offset + horizontalRadius];
                    }

                    horizontal[row + x] = value;
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = 0f;
                    for (int offset = -verticalRadius;
                         offset <= verticalRadius;
                         offset++)
                    {
                        int sampleY = Mathf.Clamp(y + offset, 0, height - 1);
                        value += horizontal[sampleY * width + x] *
                                 verticalKernel[offset + verticalRadius];
                    }

                    output[y * width + x] = value;
                }
            }

            return output;
        }

        private static float[] BuildGaussianKernel(float sigmaPixels)
        {
            if (sigmaPixels <= 0.01f)
                return new[] { 1f };

            int radius = Mathf.Max(1, Mathf.CeilToInt(sigmaPixels * 3f));
            float[] kernel = new float[radius * 2 + 1];
            float weightSum = 0f;
            float denominator = 2f * sigmaPixels * sigmaPixels;

            for (int offset = -radius; offset <= radius; offset++)
            {
                float weight = Mathf.Exp(-(offset * offset) / denominator);
                kernel[offset + radius] = weight;
                weightSum += weight;
            }

            for (int index = 0; index < kernel.Length; index++)
                kernel[index] /= weightSum;

            return kernel;
        }

        private static Texture2D CreateSurfaceTexture(
            float[] heightsMeters,
            int width,
            int height,
            float minimumHeightMeters,
            float maximumHeightMeters)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RFloat,
                false,
                true)
            {
                name = "Baked Physical Height Field",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            float[] normalizedHeights = new float[heightsMeters.Length];
            for (int index = 0; index < heightsMeters.Length; index++)
            {
                normalizedHeights[index] = Mathf.InverseLerp(
                    minimumHeightMeters,
                    maximumHeightMeters,
                    heightsMeters[index]);
            }

            texture.SetPixelData(normalizedHeights, 0);
            texture.Apply(false, true);
            return texture;
        }
    }
}
