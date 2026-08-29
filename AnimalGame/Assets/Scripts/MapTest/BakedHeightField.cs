using System;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Runtime-baked physical height field shared by rendering and gameplay.
    /// The raw-detail channel retains the resampled source precision, the detail
    /// channel can remove image quantization before step detection, and the
    /// independently smoothed surface channel is used for slopes and contours.
    /// </summary>
    public sealed class BakedHeightField : IDisposable
    {
        private readonly float[] rawDetailHeightsMeters;
        private readonly float[] detailHeightsMeters;
        private readonly float[] surfaceHeightsMeters;
        private readonly byte[] playableMask;

        public int Width { get; }
        public int Height { get; }
        public Vector2 MapSizeMeters { get; }
        public float MinimumHeightMeters { get; }
        public float MaximumHeightMeters { get; }
        public float SourceMinimum { get; }
        public float SourceMaximum { get; }
        public Texture2D SurfaceTexture { get; private set; }
        public Texture2D PlayableMaskTexture { get; private set; }

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
            float[] rawDetailHeights,
            float[] detailHeights,
            float[] surfaceHeights,
            byte[] bakedPlayableMask,
            Texture2D bakedPlayableMaskTexture,
            Texture2D surfaceTexture)
        {
            Width = width;
            Height = height;
            MapSizeMeters = mapSizeMeters;
            MinimumHeightMeters = minimumHeightMeters;
            MaximumHeightMeters = maximumHeightMeters;
            SourceMinimum = sourceMinimum;
            SourceMaximum = sourceMaximum;
            rawDetailHeightsMeters = rawDetailHeights;
            detailHeightsMeters = detailHeights;
            surfaceHeightsMeters = surfaceHeights;
            playableMask = bakedPlayableMask;
            PlayableMaskTexture = bakedPlayableMaskTexture;
            SurfaceTexture = surfaceTexture;
        }

        public static BakedHeightField Bake(
            Texture2D source,
            int requestedResolution,
            Vector2 mapSizeMeters,
            float minimumHeightMeters,
            float maximumHeightMeters,
            bool normalizeSourceRange,
            float smoothingSigmaMeters,
            float detailSmoothingSigmaMeters = 0f,
            bool useHeightMapBorderMask = false,
            float heightMapBorderMaskThreshold = 0.01f,
            float heightMapBorderInsetMeters = 0f,
            Texture2D playableAreaMask = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            int resolution = Mathf.Clamp(requestedResolution, 64, 4096);
            float[] sourcePixels = ReadSourceGrayscale(source);
            float sourceMinimum = 1f;
            float sourceMaximum = 0f;

            for (int index = 0; index < sourcePixels.Length; index++)
            {
                float gray = sourcePixels[index];
                sourceMinimum = Mathf.Min(sourceMinimum, gray);
                sourceMaximum = Mathf.Max(sourceMaximum, gray);
            }

            if (!normalizeSourceRange)
            {
                sourceMinimum = 0f;
                sourceMaximum = 1f;
            }

            float[] rawDetailHeights = new float[resolution * resolution];
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
                    rawDetailHeights[y * resolution + x] = Mathf.Lerp(
                        minimumHeightMeters,
                        maximumHeightMeters,
                        normalized);
                }
            }

            float texelSizeXMeters = mapSizeMeters.x / Mathf.Max(1, resolution - 1);
            float texelSizeYMeters = mapSizeMeters.y / Mathf.Max(1, resolution - 1);
            float detailSigmaPixelsX = detailSmoothingSigmaMeters /
                                       Mathf.Max(0.0001f, texelSizeXMeters);
            float detailSigmaPixelsY = detailSmoothingSigmaMeters /
                                       Mathf.Max(0.0001f, texelSizeYMeters);
            float[] detailHeights = detailSmoothingSigmaMeters > 0.0001f
                ? GaussianBlur(
                    rawDetailHeights,
                    resolution,
                    resolution,
                    detailSigmaPixelsX,
                    detailSigmaPixelsY)
                : rawDetailHeights;
            byte[] bakedPlayableMask = playableAreaMask != null
                ? BuildExplicitPlayableMask(
                    playableAreaMask,
                    resolution,
                    Mathf.Max(0f, heightMapBorderInsetMeters),
                    texelSizeXMeters,
                    texelSizeYMeters)
                : useHeightMapBorderMask
                    ? BuildPlayableMask(
                    sourcePixels,
                    source.width,
                    source.height,
                    resolution,
                    Mathf.Clamp01(heightMapBorderMaskThreshold),
                    Mathf.Max(0f, heightMapBorderInsetMeters),
                    texelSizeXMeters,
                    texelSizeYMeters)
                    : null;
            float sigmaPixelsX = smoothingSigmaMeters /
                                 Mathf.Max(0.0001f, texelSizeXMeters);
            float sigmaPixelsY = smoothingSigmaMeters /
                                 Mathf.Max(0.0001f, texelSizeYMeters);
            float[] surfaceHeights = GaussianBlur(
                rawDetailHeights,
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
            Texture2D playableMaskTexture = bakedPlayableMask != null
                ? CreatePlayableMaskTexture(
                    bakedPlayableMask,
                    resolution,
                    resolution)
                : null;

            return new BakedHeightField(
                resolution,
                resolution,
                mapSizeMeters,
                minimumHeightMeters,
                maximumHeightMeters,
                sourceMinimum,
                sourceMaximum,
                rawDetailHeights,
                detailHeights,
                surfaceHeights,
                bakedPlayableMask,
                playableMaskTexture,
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

        public float SampleRawDetailHeight(Vector2 uv)
        {
            return SampleBilinear(rawDetailHeightsMeters, uv);
        }

        /// <summary>
        /// Returns whether a point belongs to the authored map silhouette. Levels
        /// without a border mask keep the original rectangular playable area.
        /// </summary>
        public bool IsPlayable(Vector2 uv)
        {
            if (playableMask == null || playableMask.Length == 0)
                return true;

            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                return false;

            int x = Mathf.Clamp(
                Mathf.RoundToInt(uv.x * (Width - 1)),
                0,
                Width - 1);
            int y = Mathf.Clamp(
                Mathf.RoundToInt(uv.y * (Height - 1)),
                0,
                Height - 1);
            return playableMask[y * Width + x] != 0;
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
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(SurfaceTexture);
                else
                    UnityEngine.Object.DestroyImmediate(SurfaceTexture);
                SurfaceTexture = null;
            }

            if (PlayableMaskTexture != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(PlayableMaskTexture);
                else
                    UnityEngine.Object.DestroyImmediate(PlayableMaskTexture);
                PlayableMaskTexture = null;
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

        private static float[] ReadSourceGrayscale(Texture2D source)
        {
            int pixelCount = source.width * source.height;
            float[] samples = new float[pixelCount];

            // Unity imports an uncompressed 16-bit grayscale PNG as R16. Reading
            // its ushort payload directly avoids reducing the reconstructed DEM
            // back to the 256 levels exposed by GetPixels32().
            if (source.format == TextureFormat.R16)
            {
                var pixels = source.GetPixelData<ushort>(0);
                if (pixels.Length >= pixelCount)
                {
                    const float inverseUShort = 1f / ushort.MaxValue;
                    for (int index = 0; index < pixelCount; index++)
                        samples[index] = pixels[index] * inverseUShort;
                    return samples;
                }
            }

            if (source.format == TextureFormat.R8
                || source.format == TextureFormat.Alpha8)
            {
                var pixels = source.GetPixelData<byte>(0);
                if (pixels.Length >= pixelCount)
                {
                    const float inverseByte = 1f / byte.MaxValue;
                    for (int index = 0; index < pixelCount; index++)
                        samples[index] = pixels[index] * inverseByte;
                    return samples;
                }
            }

            Color32[] colors = source.GetPixels32();
            for (int index = 0; index < pixelCount; index++)
                samples[index] = GetGrayscale(colors[index]);
            return samples;
        }

        private static float SampleSource(
            float[] pixels,
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
                pixels[y0 * width + x0],
                pixels[y0 * width + x1],
                tx);
            float top = Mathf.Lerp(
                pixels[y1 * width + x0],
                pixels[y1 * width + x1],
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

        private static byte[] BuildPlayableMask(
            float[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            int resolution,
            float threshold,
            float insetMeters,
            float texelSizeXMeters,
            float texelSizeYMeters)
        {
            int sampleCount = resolution * resolution;
            bool[] outside = new bool[sampleCount];
            int[] queue = new int[sampleCount];
            int queueHead = 0;
            int queueTail = 0;

            // Only low-value pixels connected to an image edge count as outside.
            // This preserves legitimate dark valleys enclosed by mountain terrain.
            for (int x = 0; x < resolution; x++)
            {
                TryEnqueueBorderPixel(x, 0);
                TryEnqueueBorderPixel(x, resolution - 1);
            }

            for (int y = 1; y < resolution - 1; y++)
            {
                TryEnqueueBorderPixel(0, y);
                TryEnqueueBorderPixel(resolution - 1, y);
            }

            while (queueHead < queueTail)
            {
                int index = queue[queueHead++];
                int x = index % resolution;
                int y = index / resolution;
                TryEnqueueConnectedPixel(x - 1, y);
                TryEnqueueConnectedPixel(x + 1, y);
                TryEnqueueConnectedPixel(x, y - 1);
                TryEnqueueConnectedPixel(x, y + 1);
            }

            int insetSteps = Mathf.CeilToInt(
                insetMeters / Mathf.Max(
                    0.0001f,
                    Mathf.Min(texelSizeXMeters, texelSizeYMeters)));
            queueHead = 0;
            for (int step = 0; step < insetSteps; step++)
            {
                int layerEnd = queueTail;
                while (queueHead < layerEnd)
                {
                    int index = queue[queueHead++];
                    int x = index % resolution;
                    int y = index / resolution;
                    EnqueueInsetPixel(x - 1, y - 1);
                    EnqueueInsetPixel(x, y - 1);
                    EnqueueInsetPixel(x + 1, y - 1);
                    EnqueueInsetPixel(x - 1, y);
                    EnqueueInsetPixel(x + 1, y);
                    EnqueueInsetPixel(x - 1, y + 1);
                    EnqueueInsetPixel(x, y + 1);
                    EnqueueInsetPixel(x + 1, y + 1);
                }
            }

            byte[] mask = new byte[sampleCount];
            for (int index = 0; index < sampleCount; index++)
                mask[index] = outside[index] ? (byte)0 : byte.MaxValue;
            return mask;

            void TryEnqueueBorderPixel(int x, int y)
            {
                int index = y * resolution + x;
                if (outside[index] || !IsBelowThreshold(x, y))
                    return;

                outside[index] = true;
                queue[queueTail++] = index;
            }

            void TryEnqueueConnectedPixel(int x, int y)
            {
                if (x < 0 || x >= resolution || y < 0 || y >= resolution)
                    return;

                int index = y * resolution + x;
                if (outside[index] || !IsBelowThreshold(x, y))
                    return;

                outside[index] = true;
                queue[queueTail++] = index;
            }

            void EnqueueInsetPixel(int x, int y)
            {
                if (x < 0 || x >= resolution || y < 0 || y >= resolution)
                    return;

                int index = y * resolution + x;
                if (outside[index])
                    return;

                outside[index] = true;
                queue[queueTail++] = index;
            }

            bool IsBelowThreshold(int x, int y)
            {
                float u = x / (float)Mathf.Max(1, resolution - 1);
                float v = y / (float)Mathf.Max(1, resolution - 1);
                return SampleSource(
                    sourcePixels,
                    sourceWidth,
                    sourceHeight,
                    u,
                    v) <= threshold;
            }
        }

        private static byte[] BuildExplicitPlayableMask(
            Texture2D sourceMask,
            int resolution,
            float insetMeters,
            float texelSizeXMeters,
            float texelSizeYMeters)
        {
            float[] sourcePixels = ReadSourceGrayscale(sourceMask);
            byte[] mask = new byte[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = y / (float)Mathf.Max(1, resolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)Mathf.Max(1, resolution - 1);
                    mask[y * resolution + x] = SampleSource(
                            sourcePixels,
                            sourceMask.width,
                            sourceMask.height,
                            u,
                            v) >= 0.5f
                        ? byte.MaxValue
                        : (byte)0;
                }
            }

            int insetSteps = Mathf.CeilToInt(
                insetMeters / Mathf.Max(
                    0.0001f,
                    Mathf.Min(texelSizeXMeters, texelSizeYMeters)));
            if (insetSteps <= 0)
                return mask;

            byte[] next = new byte[mask.Length];
            for (int step = 0; step < insetSteps; step++)
            {
                Array.Copy(mask, next, mask.Length);
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        int index = y * resolution + x;
                        if (mask[index] == 0)
                            continue;

                        bool touchesOutside = false;
                        for (int offsetY = -1; offsetY <= 1 && !touchesOutside; offsetY++)
                        {
                            int sampleY = y + offsetY;
                            if (sampleY < 0 || sampleY >= resolution)
                            {
                                touchesOutside = true;
                                break;
                            }

                            for (int offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                int sampleX = x + offsetX;
                                if (sampleX < 0
                                    || sampleX >= resolution
                                    || mask[sampleY * resolution + sampleX] == 0)
                                {
                                    touchesOutside = true;
                                    break;
                                }
                            }
                        }

                        if (touchesOutside)
                            next[index] = 0;
                    }
                }

                byte[] swap = mask;
                mask = next;
                next = swap;
            }

            return mask;
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

        private static Texture2D CreatePlayableMaskTexture(
            byte[] mask,
            int width,
            int height)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.R8,
                false,
                true)
            {
                name = "Baked Playable Area Mask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            texture.SetPixelData(mask, 0);
            texture.Apply(false, true);
            return texture;
        }
    }
}
