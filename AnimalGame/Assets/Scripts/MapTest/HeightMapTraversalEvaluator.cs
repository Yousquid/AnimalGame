using UnityEngine;

namespace AnimalGame.MapTest
{
    public struct SlopeTraversalResult
    {
        public bool HasData { get; }
        public bool IsPassable { get; }
        public float SlopeAngle { get; }
        public float SignedSlopeAngle { get; }

        public SlopeTraversalResult(
            bool hasData,
            bool isPassable,
            float slopeAngle,
            float signedSlopeAngle)
        {
            HasData = hasData;
            IsPassable = isPassable;
            SlopeAngle = slopeAngle;
            SignedSlopeAngle = signedSlopeAngle;
        }

        public static SlopeTraversalResult NoData =>
            new SlopeTraversalResult(false, false, 0f, 0f);

        public static SlopeTraversalResult BlockedBoundary =>
            new SlopeTraversalResult(true, false, 90f, 90f);
    }

    public sealed class HeightMapTraversalEvaluator : MonoBehaviour
    {
        [Header("Slope Limits")]
        [Tooltip("The robot becomes blocked when the path exceeds this slope.")]
        [SerializeField, Range(0f, 89f)] private float blockSlopeAngle = 30f;

        [Tooltip("A blocked robot becomes unblocked only after the slope falls below this value.")]
        [SerializeField, Range(0f, 89f)] private float unblockSlopeAngle = 26f;

        [Header("Movement Probe")]
        [SerializeField, Min(0.1f)] private float movementProbeDistanceMeters = 2f;
        [SerializeField, Range(2, 32)] private int movementPathSamples = 8;
        [SerializeField, Min(0f)] private float blockedBraking = 12f;

        [Header("Surface Probe")]
        [Tooltip("Radius in logical map meters used by UI point-slope checks.")]
        [SerializeField, Min(0.05f)] private float localSlopeSampleRadiusMeters = 0.75f;

        private MapTestSceneController map;

        public float BlockSlopeAngle => blockSlopeAngle;
        public float UnblockSlopeAngle => Mathf.Min(unblockSlopeAngle, blockSlopeAngle);
        public float BlockedBraking => blockedBraking;
        public bool IsInitialized => map != null && map.HasGeneratedMap;

        public void Initialize(MapTestSceneController mapController)
        {
            map = mapController;
        }

        public bool ShouldBlock(SlopeTraversalResult result, bool currentlyBlocked)
        {
            if (!result.HasData)
                return false;

            float threshold = currentlyBlocked ? UnblockSlopeAngle : BlockSlopeAngle;
            return result.SlopeAngle > threshold;
        }

        public SlopeTraversalResult EvaluateMovement(Vector2 startWorld, Vector2 worldDirection)
        {
            if (!IsInitialized || worldDirection.sqrMagnitude < 0.000001f)
                return SlopeTraversalResult.NoData;

            Vector2 direction = worldDirection.normalized;
            float worldProbeDistance = map.MapMetersToWorldDistance(
                direction,
                movementProbeDistanceMeters);
            if (worldProbeDistance <= 0.0001f)
                return SlopeTraversalResult.NoData;

            int samples = Mathf.Max(2, movementPathSamples);
            bool hasPrevious = false;
            bool hasValidSegment = false;
            Vector2 previousMapPosition = Vector2.zero;
            float previousHeight = 0f;
            float maximumSlope = 0f;
            float signedSlopeAtMaximum = 0f;

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector2 worldPosition = startWorld + direction * (worldProbeDistance * t);
                if (!map.TrySampleWorldPosition(
                        worldPosition,
                        out Vector2 mapPosition,
                        out float height))
                {
                    return i == 0
                        ? SlopeTraversalResult.NoData
                        : SlopeTraversalResult.BlockedBoundary;
                }

                if (hasPrevious)
                {
                    float horizontalDistance = Vector2.Distance(previousMapPosition, mapPosition);
                    if (horizontalDistance > 0.0001f)
                    {
                        hasValidSegment = true;
                        float signedSlope = Mathf.Atan2(
                            height - previousHeight,
                            horizontalDistance) * Mathf.Rad2Deg;
                        float absoluteSlope = Mathf.Abs(signedSlope);
                        if (absoluteSlope > maximumSlope)
                        {
                            maximumSlope = absoluteSlope;
                            signedSlopeAtMaximum = signedSlope;
                        }
                    }
                }

                hasPrevious = true;
                previousMapPosition = mapPosition;
                previousHeight = height;
            }

            if (!hasPrevious || !hasValidSegment)
                return SlopeTraversalResult.NoData;

            return new SlopeTraversalResult(
                true,
                maximumSlope <= blockSlopeAngle,
                maximumSlope,
                signedSlopeAtMaximum);
        }

        public SlopeTraversalResult EvaluateLocalSurface(Vector2 worldPosition)
        {
            if (!IsInitialized)
                return SlopeTraversalResult.NoData;

            float radius = Mathf.Max(0.05f, localSlopeSampleRadiusMeters);
            float horizontalWorldOffset = map.MapMetersToWorldDistance(Vector2.right, radius);
            float verticalWorldOffset = map.MapMetersToWorldDistance(Vector2.up, radius);

            if (!TryGetHeight(worldPosition + Vector2.left * horizontalWorldOffset, out Vector2 leftMap, out float left)
                || !TryGetHeight(worldPosition + Vector2.right * horizontalWorldOffset, out Vector2 rightMap, out float right)
                || !TryGetHeight(worldPosition + Vector2.down * verticalWorldOffset, out Vector2 downMap, out float down)
                || !TryGetHeight(worldPosition + Vector2.up * verticalWorldOffset, out Vector2 upMap, out float up))
            {
                return SlopeTraversalResult.NoData;
            }

            float horizontalMeters = Mathf.Max(0.0001f, Vector2.Distance(leftMap, rightMap));
            float verticalMeters = Mathf.Max(0.0001f, Vector2.Distance(downMap, upMap));
            float dhDx = (right - left) / horizontalMeters;
            float dhDy = (up - down) / verticalMeters;
            float gradient = Mathf.Sqrt(dhDx * dhDx + dhDy * dhDy);
            float slope = Mathf.Atan(gradient) * Mathf.Rad2Deg;

            return new SlopeTraversalResult(
                true,
                slope <= blockSlopeAngle,
                slope,
                slope);
        }

        private bool TryGetHeight(
            Vector2 worldPosition,
            out Vector2 mapPosition,
            out float height)
        {
            return map.TrySampleWorldPosition(worldPosition, out mapPosition, out height);
        }

        private void OnValidate()
        {
            unblockSlopeAngle = Mathf.Min(unblockSlopeAngle, blockSlopeAngle);
        }
    }
}
