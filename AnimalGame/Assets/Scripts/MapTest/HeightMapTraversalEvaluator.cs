using UnityEngine;

namespace AnimalGame.MapTest
{
    public enum TraversalBlockReason
    {
        None,
        Slope,
        Step,
        Boundary
    }

    public struct SlopeTraversalResult
    {
        public bool HasData { get; }
        public bool IsPassable { get; }
        public float SlopeAngle { get; }
        public float SignedSlopeAngle { get; }
        public float MaximumSurfaceSlopeAngle { get; }
        public float MaximumStepHeight { get; }
        public float SurfaceRoughness { get; }
        public TraversalBlockReason BlockReason { get; }

        public SlopeTraversalResult(
            bool hasData,
            bool isPassable,
            float slopeAngle,
            float signedSlopeAngle,
            float maximumSurfaceSlopeAngle,
            float maximumStepHeight,
            float surfaceRoughness,
            TraversalBlockReason blockReason)
        {
            HasData = hasData;
            IsPassable = isPassable;
            SlopeAngle = slopeAngle;
            SignedSlopeAngle = signedSlopeAngle;
            MaximumSurfaceSlopeAngle = maximumSurfaceSlopeAngle;
            MaximumStepHeight = maximumStepHeight;
            SurfaceRoughness = surfaceRoughness;
            BlockReason = blockReason;
        }

        public static SlopeTraversalResult NoData =>
            new SlopeTraversalResult(
                false,
                false,
                0f,
                0f,
                0f,
                0f,
                0f,
                TraversalBlockReason.None);

        public static SlopeTraversalResult BlockedBoundary =>
            new SlopeTraversalResult(
                true,
                false,
                90f,
                90f,
                90f,
                0f,
                0f,
                TraversalBlockReason.Boundary);
    }

    public sealed class HeightMapTraversalEvaluator : MonoBehaviour
    {
        [Header("Robot Physical Contact Area")]
        [Tooltip("Length of the ground-contact footprint in logical map meters, measured along the requested movement direction. This defines the physical scale used to fit the ground plane.")]
        [SerializeField, Min(0.25f)] private float robotFootprintLengthMeters = 4f;

        [Tooltip("Width of the ground-contact footprint in logical map meters, measured perpendicular to the requested movement direction.")]
        [SerializeField, Min(0.25f)] private float robotFootprintWidthMeters = 3f;

        [Tooltip("Number of height samples along the footprint length. Odd values keep one sample at the footprint centre.")]
        [SerializeField, Range(3, 11)] private int footprintLongitudinalSamples = 5;

        [Tooltip("Number of height samples across the footprint width. Odd values keep one sample at the footprint centre.")]
        [SerializeField, Range(3, 11)] private int footprintLateralSamples = 5;

        [Header("Slope Ability")]
        [Tooltip("Maximum sustained uphill angle the robot can traverse. The angle comes from a least-squares plane fitted under the full contact footprint.")]
        [SerializeField, Range(0f, 89f)] private float blockSlopeAngle = 45f;

        [Tooltip("Maximum sustained downhill angle the robot can traverse. This can be higher than the uphill limit so the robot can retreat from steep terrain.")]
        [SerializeField, Range(0f, 89f)] private float maximumDownhillSlopeAngle = 55f;

        [Tooltip("Distance for which an over-limit fitted slope must continue before it blocks movement. This rejects isolated short bumps while preserving real extended slopes.")]
        [SerializeField, Min(0f)] private float minimumBlockingSlopeLengthMeters = 1.5f;

        [Header("Step and Ledge Detection")]
        [Tooltip("Maximum abrupt height residual the robot can cross, in meters. The expected rise of the fitted slope is subtracted first, so a continuous steep slope is not mistaken for a vertical step.")]
        [SerializeField, Min(0f)] private float maximumStepHeightMeters = 0.65f;

        [Tooltip("Distance between detail-height samples used to find abrupt steps. This may be smaller than the slope evaluation spacing because it measures discontinuities rather than overall inclination.")]
        [SerializeField, Min(0.1f)] private float stepProbeSpacingMeters = 0.5f;

        [Tooltip("Number of parallel step probes distributed across the robot width. Odd values include the centre line.")]
        [SerializeField, Range(1, 9)] private int stepLateralSamples = 3;

        [Header("Path Evaluation")]
        [Tooltip("Distance ahead of the robot that is evaluated before each movement update. It should normally be at least as long as the contact footprint.")]
        [SerializeField, Min(0.25f)] private float movementProbeDistanceMeters = 6f;

        [Tooltip("Spacing in logical map meters between consecutive fitted-footprint evaluations along a path. Smaller values are more precise but more expensive.")]
        [SerializeField, Min(0.1f)] private float pathEvaluationSpacingMeters = 1f;

        private MapTestSceneController map;

        public float BlockSlopeAngle => blockSlopeAngle;
        public float MaximumDownhillSlopeAngle => maximumDownhillSlopeAngle;
        public float RobotFootprintLengthMeters => robotFootprintLengthMeters;
        public float RobotFootprintWidthMeters => robotFootprintWidthMeters;
        public float MaximumStepHeightMeters => maximumStepHeightMeters;
        public float MovementProbeDistanceMeters => movementProbeDistanceMeters;
        public float PathSampleSpacingMeters => pathEvaluationSpacingMeters;
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

        public SlopeTraversalResult EvaluateMovement(
            Vector2 startWorld,
            Vector2 worldDirection)
        {
            if (!IsInitialized || worldDirection.sqrMagnitude < 0.000001f)
                return SlopeTraversalResult.NoData;

            if (!map.TrySampleWorldPosition(startWorld, out Vector2 startMapPosition, out _))
                return SlopeTraversalResult.NoData;

            Vector2 mapDirection = map.WorldDirectionToMapDirection(worldDirection);
            if (mapDirection.sqrMagnitude < 0.000001f)
                return SlopeTraversalResult.NoData;

            Vector2 endMapPosition = startMapPosition
                                     + mapDirection * movementProbeDistanceMeters;
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

            Vector2 path = endMapPosition - startMapPosition;
            float pathLength = path.magnitude;
            if (pathLength <= 0.0001f)
            {
                return new SlopeTraversalResult(
                    true,
                    true,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    TraversalBlockReason.None);
            }

            Vector2 direction = path / pathLength;
            int evaluationCount = Mathf.Max(
                1,
                Mathf.CeilToInt(pathLength /
                                Mathf.Max(0.1f, pathEvaluationSpacingMeters)));
            float segmentLength = pathLength / evaluationCount;
            float consecutiveOverLimitLength = 0f;
            float maximumDirectionalSlope = 0f;
            float signedSlopeAtMaximum = 0f;
            float maximumSurfaceSlope = 0f;
            float maximumStepHeight = 0f;
            float maximumRoughness = 0f;

            for (int evaluationIndex = 0;
                 evaluationIndex < evaluationCount;
                 evaluationIndex++)
            {
                float t = (evaluationIndex + 0.5f) / evaluationCount;
                Vector2 center = Vector2.Lerp(startMapPosition, endMapPosition, t);
                if (!TryAnalyzeSurface(center, direction, out SurfaceAnalysis analysis))
                    return SlopeTraversalResult.BlockedBoundary;

                float absoluteDirectionalSlope = Mathf.Abs(
                    analysis.SignedDirectionalSlopeAngle);
                if (absoluteDirectionalSlope > maximumDirectionalSlope)
                {
                    maximumDirectionalSlope = absoluteDirectionalSlope;
                    signedSlopeAtMaximum = analysis.SignedDirectionalSlopeAngle;
                }

                maximumSurfaceSlope = Mathf.Max(
                    maximumSurfaceSlope,
                    analysis.MaximumSurfaceSlopeAngle);
                maximumStepHeight = Mathf.Max(
                    maximumStepHeight,
                    analysis.MaximumStepResidualHeight);
                maximumRoughness = Mathf.Max(
                    maximumRoughness,
                    analysis.SurfaceRoughness);

                if (analysis.MaximumStepResidualHeight > maximumStepHeightMeters)
                {
                    return CreateResult(
                        false,
                        maximumDirectionalSlope,
                        signedSlopeAtMaximum,
                        maximumSurfaceSlope,
                        maximumStepHeight,
                        maximumRoughness,
                        TraversalBlockReason.Step);
                }

                float relevantSlopeLimit = analysis.SignedDirectionalSlopeAngle >= 0f
                    ? blockSlopeAngle
                    : maximumDownhillSlopeAngle;
                bool exceedsSlopeLimit = absoluteDirectionalSlope > relevantSlopeLimit;
                consecutiveOverLimitLength = exceedsSlopeLimit
                    ? consecutiveOverLimitLength + segmentLength
                    : 0f;

                if (exceedsSlopeLimit
                    && consecutiveOverLimitLength
                    >= Mathf.Max(0f, minimumBlockingSlopeLengthMeters))
                {
                    return CreateResult(
                        false,
                        maximumDirectionalSlope,
                        signedSlopeAtMaximum,
                        maximumSurfaceSlope,
                        maximumStepHeight,
                        maximumRoughness,
                        TraversalBlockReason.Slope);
                }
            }

            return CreateResult(
                true,
                maximumDirectionalSlope,
                signedSlopeAtMaximum,
                maximumSurfaceSlope,
                maximumStepHeight,
                maximumRoughness,
                TraversalBlockReason.None);
        }

        public SlopeTraversalResult EvaluateLocalSurface(Vector2 worldPosition)
        {
            if (!IsInitialized
                || !map.TrySampleWorldPosition(worldPosition, out Vector2 mapPosition, out _))
            {
                return SlopeTraversalResult.NoData;
            }

            if (!TryAnalyzeSurface(mapPosition, Vector2.up, out SurfaceAnalysis analysis))
                return SlopeTraversalResult.BlockedBoundary;

            bool stepBlocked = analysis.MaximumStepResidualHeight > maximumStepHeightMeters;
            bool slopeBlocked = analysis.MaximumSurfaceSlopeAngle > blockSlopeAngle;
            TraversalBlockReason reason = stepBlocked
                ? TraversalBlockReason.Step
                : slopeBlocked
                    ? TraversalBlockReason.Slope
                    : TraversalBlockReason.None;

            return new SlopeTraversalResult(
                true,
                reason == TraversalBlockReason.None,
                analysis.MaximumSurfaceSlopeAngle,
                analysis.SignedDirectionalSlopeAngle,
                analysis.MaximumSurfaceSlopeAngle,
                analysis.MaximumStepResidualHeight,
                analysis.SurfaceRoughness,
                reason);
        }

        private bool TryAnalyzeSurface(
            Vector2 center,
            Vector2 forward,
            out SurfaceAnalysis analysis)
        {
            analysis = default;
            if (forward.sqrMagnitude < 0.000001f)
                return false;

            forward.Normalize();
            Vector2 right = new Vector2(forward.y, -forward.x);
            int longitudinalSamples = EnsureOdd(footprintLongitudinalSamples, 3, 11);
            int lateralSamples = EnsureOdd(footprintLateralSamples, 3, 11);
            float halfLength = robotFootprintLengthMeters * 0.5f;
            float halfWidth = robotFootprintWidthMeters * 0.5f;
            int sampleCount = longitudinalSamples * lateralSamples;
            float heightSum = 0f;
            float heightSquaredSum = 0f;
            float longitudinalHeightSum = 0f;
            float lateralHeightSum = 0f;
            float longitudinalSquaredSum = 0f;
            float lateralSquaredSum = 0f;

            for (int longitudinalIndex = 0;
                 longitudinalIndex < longitudinalSamples;
                 longitudinalIndex++)
            {
                float longitudinal = Mathf.Lerp(
                    -halfLength,
                    halfLength,
                    longitudinalIndex / (float)(longitudinalSamples - 1));

                for (int lateralIndex = 0;
                     lateralIndex < lateralSamples;
                     lateralIndex++)
                {
                    float lateral = Mathf.Lerp(
                        -halfWidth,
                        halfWidth,
                        lateralIndex / (float)(lateralSamples - 1));
                    Vector2 samplePosition = center
                                             + forward * longitudinal
                                             + right * lateral;
                    if (!map.TrySampleMapPosition(samplePosition, out float height))
                        return false;

                    heightSum += height;
                    heightSquaredSum += height * height;
                    longitudinalHeightSum += longitudinal * height;
                    lateralHeightSum += lateral * height;
                    longitudinalSquaredSum += longitudinal * longitudinal;
                    lateralSquaredSum += lateral * lateral;
                }
            }

            float forwardGradient = longitudinalHeightSum /
                                    Mathf.Max(0.000001f, longitudinalSquaredSum);
            float lateralGradient = lateralHeightSum /
                                    Mathf.Max(0.000001f, lateralSquaredSum);
            float meanHeight = heightSum / sampleCount;
            float regressionError = heightSquaredSum
                                    - heightSum * meanHeight
                                    - forwardGradient * longitudinalHeightSum
                                    - lateralGradient * lateralHeightSum;
            float surfaceRoughness = Mathf.Sqrt(
                Mathf.Max(0f, regressionError) / sampleCount);
            float signedDirectionalSlope = Mathf.Atan(forwardGradient) * Mathf.Rad2Deg;
            float maximumSurfaceSlope = Mathf.Atan(
                Mathf.Sqrt(
                    forwardGradient * forwardGradient
                    + lateralGradient * lateralGradient)) * Mathf.Rad2Deg;

            if (!TryMeasureMaximumStepResidual(
                    center,
                    forward,
                    right,
                    forwardGradient,
                    out float maximumStepResidual))
            {
                return false;
            }

            analysis = new SurfaceAnalysis(
                signedDirectionalSlope,
                maximumSurfaceSlope,
                maximumStepResidual,
                surfaceRoughness);
            return true;
        }

        private bool TryMeasureMaximumStepResidual(
            Vector2 center,
            Vector2 forward,
            Vector2 right,
            float fittedForwardGradient,
            out float maximumResidual)
        {
            maximumResidual = 0f;
            float length = Mathf.Max(0.25f, robotFootprintLengthMeters);
            float width = Mathf.Max(0.25f, robotFootprintWidthMeters);
            int segments = Mathf.Max(
                1,
                Mathf.CeilToInt(length / Mathf.Max(0.1f, stepProbeSpacingMeters)));
            int lateralSamples = EnsureOdd(stepLateralSamples, 1, 9);
            float actualSpacing = length / segments;

            for (int lateralIndex = 0;
                 lateralIndex < lateralSamples;
                 lateralIndex++)
            {
                float lateral = lateralSamples == 1
                    ? 0f
                    : Mathf.Lerp(
                        -width * 0.5f,
                        width * 0.5f,
                        lateralIndex / (float)(lateralSamples - 1));
                Vector2 previousPosition = center
                                           - forward * (length * 0.5f)
                                           + right * lateral;
                if (!map.TrySampleDetailMapPosition(
                        previousPosition,
                        out float previousHeight))
                {
                    return false;
                }

                for (int segmentIndex = 1;
                     segmentIndex <= segments;
                     segmentIndex++)
                {
                    Vector2 currentPosition = previousPosition + forward * actualSpacing;
                    if (!map.TrySampleDetailMapPosition(
                            currentPosition,
                            out float currentHeight))
                    {
                        return false;
                    }

                    float measuredHeightChange = currentHeight - previousHeight;
                    float expectedSlopeChange = fittedForwardGradient * actualSpacing;
                    float residual = Mathf.Abs(
                        measuredHeightChange - expectedSlopeChange);
                    maximumResidual = Mathf.Max(maximumResidual, residual);
                    previousPosition = currentPosition;
                    previousHeight = currentHeight;
                }
            }

            return true;
        }

        private static SlopeTraversalResult CreateResult(
            bool isPassable,
            float slopeAngle,
            float signedSlopeAngle,
            float maximumSurfaceSlopeAngle,
            float maximumStepHeight,
            float surfaceRoughness,
            TraversalBlockReason blockReason)
        {
            return new SlopeTraversalResult(
                true,
                isPassable,
                slopeAngle,
                signedSlopeAngle,
                maximumSurfaceSlopeAngle,
                maximumStepHeight,
                surfaceRoughness,
                blockReason);
        }

        private static int EnsureOdd(int value, int minimum, int maximum)
        {
            value = Mathf.Clamp(value, minimum, maximum);
            if ((value & 1) == 0)
                value = Mathf.Min(maximum, value + 1);
            return value;
        }

        private void OnValidate()
        {
            robotFootprintLengthMeters = Mathf.Max(0.25f, robotFootprintLengthMeters);
            robotFootprintWidthMeters = Mathf.Max(0.25f, robotFootprintWidthMeters);
            footprintLongitudinalSamples = EnsureOdd(
                footprintLongitudinalSamples,
                3,
                11);
            footprintLateralSamples = EnsureOdd(footprintLateralSamples, 3, 11);
            maximumDownhillSlopeAngle = Mathf.Max(
                blockSlopeAngle,
                maximumDownhillSlopeAngle);
            minimumBlockingSlopeLengthMeters = Mathf.Max(
                0f,
                minimumBlockingSlopeLengthMeters);
            maximumStepHeightMeters = Mathf.Max(0f, maximumStepHeightMeters);
            stepProbeSpacingMeters = Mathf.Max(0.1f, stepProbeSpacingMeters);
            stepLateralSamples = EnsureOdd(stepLateralSamples, 1, 9);
            movementProbeDistanceMeters = Mathf.Max(
                robotFootprintLengthMeters,
                movementProbeDistanceMeters);
            pathEvaluationSpacingMeters = Mathf.Max(0.1f, pathEvaluationSpacingMeters);
        }

        private readonly struct SurfaceAnalysis
        {
            public float SignedDirectionalSlopeAngle { get; }
            public float MaximumSurfaceSlopeAngle { get; }
            public float MaximumStepResidualHeight { get; }
            public float SurfaceRoughness { get; }

            public SurfaceAnalysis(
                float signedDirectionalSlopeAngle,
                float maximumSurfaceSlopeAngle,
                float maximumStepResidualHeight,
                float surfaceRoughness)
            {
                SignedDirectionalSlopeAngle = signedDirectionalSlopeAngle;
                MaximumSurfaceSlopeAngle = maximumSurfaceSlopeAngle;
                MaximumStepResidualHeight = maximumStepResidualHeight;
                SurfaceRoughness = surfaceRoughness;
            }
        }
    }
}
