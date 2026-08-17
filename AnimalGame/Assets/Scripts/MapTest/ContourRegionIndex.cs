using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    public readonly struct ContourRegionHandle
    {
        internal int LevelIndex { get; }
        internal int ComponentId { get; }
        internal bool IsHighland { get; }
        public float BoundaryHeightMeters { get; }
        public float AreaSquareMeters { get; }
        public bool IsValid => ComponentId > 0;

        internal ContourRegionHandle(
            int levelIndex,
            int componentId,
            bool isHighland,
            float boundaryHeightMeters,
            float areaSquareMeters)
        {
            LevelIndex = levelIndex;
            ComponentId = componentId;
            IsHighland = isHighland;
            BoundaryHeightMeters = boundaryHeightMeters;
            AreaSquareMeters = areaSquareMeters;
        }
    }

    /// <summary>
    /// Lazily flood-fills the baked contour threshold currently occupied by the
    /// robot. The map's four straight sides are treated as closing boundaries,
    /// so a contour that reaches an edge can still enclose a valid region. A
    /// component must nevertheless touch a real contour transition; the bare
    /// map rectangle is not treated as a contour region by itself. Both hills
    /// and depressions are supported.
    /// </summary>
    public sealed class ContourRegionIndex
    {
        private sealed class RegionLevel
        {
            public int[] HighlandLabels;
            public int[] LowlandLabels;
            public readonly Dictionary<int, float> ClosedHighlandAreas =
                new Dictionary<int, float>();
            public readonly Dictionary<int, float> ClosedLowlandAreas =
                new Dictionary<int, float>();
        }

        private readonly BakedHeightField heightField;
        private readonly float contourIntervalMeters;
        private readonly Dictionary<int, RegionLevel> levels =
            new Dictionary<int, RegionLevel>();
        private readonly int[] floodQueue;

        public ContourRegionIndex(
            BakedHeightField bakedHeightField,
            float contourInterval)
        {
            heightField = bakedHeightField;
            contourIntervalMeters = Mathf.Max(0.01f, contourInterval);
            floodQueue = new int[Mathf.Max(1, heightField.Width * heightField.Height)];
        }

        public bool TryGetCurrentClosedRegion(
            Vector2 mapPositionMeters,
            out ContourRegionHandle region)
        {
            region = default;
            if (!TryMapPositionToCell(mapPositionMeters, out int cellIndex))
                return false;

            float height = SampleMapHeight(mapPositionMeters);
            float minimum = heightField.MinimumHeightMeters;
            float relative = Mathf.Max(0f, height - minimum);
            int lowerLevel = Mathf.FloorToInt(relative / contourIntervalMeters);
            int upperLevel = Mathf.CeilToInt(relative / contourIntervalMeters);

            ContourRegionHandle highland = GetRegion(
                lowerLevel,
                true,
                cellIndex);
            ContourRegionHandle lowland = GetRegion(
                upperLevel,
                false,
                cellIndex);

            if (!highland.IsValid)
            {
                region = lowland;
                return lowland.IsValid;
            }

            if (!lowland.IsValid)
            {
                region = highland;
                return true;
            }

            // The smaller enclosed component is the innermost closed contour.
            region = highland.AreaSquareMeters <= lowland.AreaSquareMeters
                ? highland
                : lowland;
            return true;
        }

        public bool Contains(
            ContourRegionHandle region,
            Vector2 mapPositionMeters)
        {
            if (!region.IsValid
                || !TryMapPositionToCell(mapPositionMeters, out int cellIndex))
            {
                return false;
            }

            RegionLevel level = GetOrBuildLevel(region.LevelIndex);
            int[] labels = region.IsHighland
                ? level.HighlandLabels
                : level.LowlandLabels;
            return labels != null
                   && labels[cellIndex] == region.ComponentId;
        }

        private ContourRegionHandle GetRegion(
            int levelIndex,
            bool highland,
            int cellIndex)
        {
            float boundaryHeight = heightField.MinimumHeightMeters
                                   + levelIndex * contourIntervalMeters;
            if (boundaryHeight < heightField.MinimumHeightMeters - 0.001f
                || boundaryHeight > heightField.MaximumHeightMeters + 0.001f)
            {
                return default;
            }

            RegionLevel level = GetOrBuildLevel(levelIndex);
            int[] labels = highland ? level.HighlandLabels : level.LowlandLabels;
            Dictionary<int, float> closedAreas = highland
                ? level.ClosedHighlandAreas
                : level.ClosedLowlandAreas;
            int componentId = labels[cellIndex];
            if (componentId <= 0
                || !closedAreas.TryGetValue(componentId, out float area))
            {
                return default;
            }

            return new ContourRegionHandle(
                levelIndex,
                componentId,
                highland,
                boundaryHeight,
                area);
        }

        private RegionLevel GetOrBuildLevel(int levelIndex)
        {
            if (levels.TryGetValue(levelIndex, out RegionLevel existing))
                return existing;

            float boundaryHeight = heightField.MinimumHeightMeters
                                   + levelIndex * contourIntervalMeters;
            var level = new RegionLevel();
            level.HighlandLabels = BuildLabels(
                boundaryHeight,
                true,
                level.ClosedHighlandAreas);
            level.LowlandLabels = BuildLabels(
                boundaryHeight,
                false,
                level.ClosedLowlandAreas);
            levels.Add(levelIndex, level);
            return level;
        }

        private int[] BuildLabels(
            float boundaryHeight,
            bool highland,
            Dictionary<int, float> closedAreas)
        {
            int width = heightField.Width;
            int height = heightField.Height;
            int[] labels = new int[width * height];
            int componentId = 0;
            float cellArea = heightField.TexelSizeXMeters
                             * heightField.TexelSizeYMeters;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int startIndex = y * width + x;
                    if (labels[startIndex] != 0
                        || !BelongsToRegion(x, y, boundaryHeight, highland))
                    {
                        continue;
                    }

                    componentId++;
                    int read = 0;
                    int write = 0;
                    int count = 0;
                    bool hasContourBoundary = false;
                    labels[startIndex] = componentId;
                    floodQueue[write++] = startIndex;

                    while (read < write)
                    {
                        int index = floodQueue[read++];
                        int currentX = index % width;
                        int currentY = index / width;
                        count++;
                        TryEnqueue(currentX - 1, currentY);
                        TryEnqueue(currentX + 1, currentY);
                        TryEnqueue(currentX, currentY - 1);
                        TryEnqueue(currentX, currentY + 1);
                    }

                    if (hasContourBoundary)
                        closedAreas[componentId] = count * cellArea;

                    void TryEnqueue(int nextX, int nextY)
                    {
                        if (nextX < 0 || nextX >= width
                            || nextY < 0 || nextY >= height)
                        {
                            // Leaving the sample grid reaches one of the four
                            // straight map sides. It closes the region but does
                            // not by itself prove that a contour exists.
                            return;
                        }

                        int nextIndex = nextY * width + nextX;
                        if (!BelongsToRegion(
                                nextX,
                                nextY,
                                boundaryHeight,
                                highland))
                        {
                            hasContourBoundary = true;
                            return;
                        }

                        if (labels[nextIndex] != 0)
                        {
                            return;
                        }

                        labels[nextIndex] = componentId;
                        floodQueue[write++] = nextIndex;
                    }
                }
            }

            return labels;
        }

        private bool BelongsToRegion(
            int x,
            int y,
            float boundaryHeight,
            bool highland)
        {
            float height = heightField.GetSurfaceHeightSample(x, y);
            return highland
                ? height >= boundaryHeight
                : height <= boundaryHeight;
        }

        private bool TryMapPositionToCell(
            Vector2 mapPositionMeters,
            out int cellIndex)
        {
            cellIndex = 0;
            if (mapPositionMeters.x < 0f
                || mapPositionMeters.y < 0f
                || mapPositionMeters.x > heightField.MapSizeMeters.x
                || mapPositionMeters.y > heightField.MapSizeMeters.y)
            {
                return false;
            }

            int x = Mathf.RoundToInt(
                mapPositionMeters.x
                / Mathf.Max(0.0001f, heightField.MapSizeMeters.x)
                * (heightField.Width - 1));
            int y = Mathf.RoundToInt(
                mapPositionMeters.y
                / Mathf.Max(0.0001f, heightField.MapSizeMeters.y)
                * (heightField.Height - 1));
            cellIndex = y * heightField.Width + x;
            return true;
        }

        private float SampleMapHeight(Vector2 mapPositionMeters)
        {
            Vector2 uv = new Vector2(
                mapPositionMeters.x
                / Mathf.Max(0.0001f, heightField.MapSizeMeters.x),
                mapPositionMeters.y
                / Mathf.Max(0.0001f, heightField.MapSizeMeters.y));
            return heightField.SampleSurfaceHeight(uv);
        }
    }
}
