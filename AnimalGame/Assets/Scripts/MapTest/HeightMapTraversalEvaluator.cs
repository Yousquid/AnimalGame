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

        [Tooltip("Maximum safe downhill angle. This is intentionally higher than the uphill limit so the robot can retreat from a steep ascent.")]
        [SerializeField, Range(0f, 89f)] private float maximumDownhillSlopeAngle = 55f;

        [Header("Movement Probe")]
        [SerializeField, Min(0.1f)] private float movementProbeDistanceMeters = 2f;
        [SerializeField, Range(2, 32)] private int movementPathSamples = 8;

        [Header("Surface Probe")]
        [Tooltip("Radius in logical map meters used by optional local-surface diagnostics.")]
        [SerializeField, Min(0.05f)] private float localSlopeSampleRadiusMeters = 0.75f;

        private MapTestSceneController map;

        public float BlockSlopeAngle => blockSlopeAngle;
        public float MaximumDownhillSlopeAngle => maximumDownhillSlopeAngle;
        public float MovementProbeDistanceMeters => movementProbeDistanceMeters;
        public float PathSampleSpacingMeters =>
            movementProbeDistanceMeters / Mathf.Max(2, movementPathSamples);
        public bool IsInitialized => map != null && map.HasGeneratedMap;

        public void Initialize(MapTestSceneController mapController)
        {
            map = mapController;
        }

        public void SetMaximumPassableSlopeAngle(float maximumSlopeAngle)
        {
            blockSlopeAngle = Mathf.Clamp(maximumSlopeAngle, 0f, 89f);
        }

        public void SetMaximumDownhillSlopeAngle(float maximumSlopeAngle)
        {
            maximumDownhillSlopeAngle = Mathf.Clamp(maximumSlopeAngle, 0f, 89f);
        }

        public SlopeTraversalResult EvaluateMovement(Vector2 startWorld, Vector2 worldDirection)
        {
            if (!IsInitialized || worldDirection.sqrMagnitude < 0.000001f)
                return SlopeTraversalResult.NoData;

            Vector2 direction = worldDirection.normalized;
            if (!map.TrySampleWorldPosition(startWorld, out Vector2 startMapPosition, out _))
                return SlopeTraversalResult.NoData;

            float worldProbeDistance = map.MapMetersToWorldDistance(
                direction,
                movementProbeDistanceMeters);
            if (worldProbeDistance <= 0.0001f)
                return SlopeTraversalResult.NoData;

            Vector2 endWorld = startWorld + direction * worldProbeDistance;
            if (!map.TrySampleWorldPosition(endWorld, out Vector2 endMapPosition, out _))
                return SlopeTraversalResult.BlockedBoundary;

            return EvaluateMapPath(startMapPosition, endMapPosition);
        }

        public SlopeTraversalResult EvaluateMapPath(
            Vector2 startMapPosition,
            Vector2 endMapPosition)
        {
            if (!IsInitialized
                || !map.TrySampleMapPosition(startMapPosition, out _))
            {
                return SlopeTraversalResult.NoData;
            }

            if (!map.TrySampleMapPosition(endMapPosition, out _))
                return SlopeTraversalResult.BlockedBoundary;

            float pathLength = Vector2.Distance(startMapPosition, endMapPosition);
            if (pathLength <= 0.0001f)
                return new SlopeTraversalResult(true, true, 0f, 0f);

            int probeCount = Mathf.Max(
                1,
                Mathf.CeilToInt(pathLength / Mathf.Max(0.1f, movementProbeDistanceMeters)));
            float maximumSlope = 0f;
            float signedSlopeAtMaximum = 0f;
            Vector2 previous = startMapPosition;

            for (int probeIndex = 1; probeIndex <= probeCount; probeIndex++)
            {
                float t = probeIndex / (float)probeCount;
                Vector2 current = Vector2.Lerp(startMapPosition, endMapPosition, t);
                SlopeTraversalResult probeResult = EvaluateMapProbe(previous, current);
                if (!probeResult.HasData)
                    return probeResult;

                if (probeResult.SlopeAngle > maximumSlope)
                {
                    maximumSlope = probeResult.SlopeAngle;
                    signedSlopeAtMaximum = probeResult.SignedSlopeAngle;
                }

                if (!probeResult.IsPassable)
                    return probeResult;

                previous = current;
            }

            return new SlopeTraversalResult(
                true,
                true,
                maximumSlope,
                signedSlopeAtMaximum);
        }

        private SlopeTraversalResult EvaluateMapProbe(
            Vector2 startMapPosition,
            Vector2 endMapPosition)
        {
            float probeLength = Vector2.Distance(startMapPosition, endMapPosition);
            if (probeLength <= 0.0001f)
                return new SlopeTraversalResult(true, true, 0f, 0f);

            int samples = Mathf.Max(
                2,
                Mathf.CeilToInt(probeLength / Mathf.Max(0.01f, PathSampleSpacingMeters)));
            float maximumSlope = 0f;
            float signedSlopeAtMaximum = 0f;
            Vector2 previousPosition = startMapPosition;

            if (!map.TrySampleMapPosition(previousPosition, out float previousHeight))
                return SlopeTraversalResult.NoData;

            for (int sampleIndex = 1; sampleIndex <= samples; sampleIndex++)
            {
                float t = sampleIndex / (float)samples;
                Vector2 currentPosition = Vector2.Lerp(
                    startMapPosition,
                    endMapPosition,
                    t);
                if (!map.TrySampleMapPosition(currentPosition, out float currentHeight))
                    return SlopeTraversalResult.BlockedBoundary;

                float horizontalDistance = Vector2.Distance(previousPosition, currentPosition);
                if (horizontalDistance > 0.0001f)
                {
                    float signedSlope = Mathf.Atan2(
                        currentHeight - previousHeight,
                        horizontalDistance) * Mathf.Rad2Deg;
                    float absoluteSlope = Mathf.Abs(signedSlope);

                    bool segmentIsPassable = signedSlope >= 0f
                        ? signedSlope <= blockSlopeAngle
                        : absoluteSlope <= maximumDownhillSlopeAngle;
                    if (!segmentIsPassable)
                    {
                        return new SlopeTraversalResult(
                            true,
                            false,
                            absoluteSlope,
                            signedSlope);
                    }

                    if (absoluteSlope > maximumSlope)
                    {
                        maximumSlope = absoluteSlope;
                        signedSlopeAtMaximum = signedSlope;
                    }
                }

                previousPosition = currentPosition;
                previousHeight = currentHeight;
            }

            return new SlopeTraversalResult(
                true,
                true,
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

    }
}
