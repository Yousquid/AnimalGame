using UnityEngine;
using UnityEngine.Serialization;

namespace AnimalGame.MapTest
{
    public enum TraversalBlockReason
    {
        None,
        Slope,
        Step,
        UnsafeDownhill,
        Boundary
    }

    public enum UphillSlopeLevel
    {
        LevelOne,
        LevelTwo,
        LevelThree
    }

    public struct SlopeTraversalResult
    {
        public bool HasData { get; }
        public bool IsPassable { get; }
        public bool RequiresHardStop { get; }
        public UphillSlopeLevel UphillLevel { get; }
        public float SlopeAngle { get; }
        public float SignedSlopeAngle { get; }
        public float MaximumUphillAngle { get; }
        public float MaximumDownhillAngle { get; }
        public float MaximumSurfaceSlopeAngle { get; }
        public float MaximumStepHeight { get; }
        public float SurfaceRoughness { get; }
        public Vector2 DownhillWorldDirection { get; }
        public TraversalBlockReason BlockReason { get; }

        public SlopeTraversalResult(
            bool hasData,
            bool isPassable,
            bool requiresHardStop,
            UphillSlopeLevel uphillLevel,
            float slopeAngle,
            float signedSlopeAngle,
            float maximumUphillAngle,
            float maximumDownhillAngle,
            float maximumSurfaceSlopeAngle,
            float maximumStepHeight,
            float surfaceRoughness,
            Vector2 downhillWorldDirection,
            TraversalBlockReason blockReason)
        {
            HasData = hasData;
            IsPassable = isPassable;
            RequiresHardStop = requiresHardStop;
            UphillLevel = uphillLevel;
            SlopeAngle = slopeAngle;
            SignedSlopeAngle = signedSlopeAngle;
            MaximumUphillAngle = maximumUphillAngle;
            MaximumDownhillAngle = maximumDownhillAngle;
            MaximumSurfaceSlopeAngle = maximumSurfaceSlopeAngle;
            MaximumStepHeight = maximumStepHeight;
            SurfaceRoughness = surfaceRoughness;
            DownhillWorldDirection = downhillWorldDirection;
            BlockReason = blockReason;
        }

        public static SlopeTraversalResult NoData =>
            new SlopeTraversalResult(
                false,
                false,
                false,
                UphillSlopeLevel.LevelOne,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                Vector2.zero,
                TraversalBlockReason.None);

        public static SlopeTraversalResult BlockedBoundary =>
            new SlopeTraversalResult(
                true,
                false,
                true,
                UphillSlopeLevel.LevelOne,
                90f,
                90f,
                0f,
                0f,
                90f,
                0f,
                0f,
                Vector2.zero,
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
        [Tooltip("Largest uphill angle treated as Level One. Level One does not change top speed and does not make the robot slide while idle.")]
        [SerializeField, Range(0f, 89f)] private float levelOneMaximumUphillAngle = 12f;

        [Tooltip("Uphill angle at which Level Three begins. Between the Level One limit and this value is Level Two. Level Three no longer hard-stops the robot; it produces strong downhill and lateral instability instead.")]
        [FormerlySerializedAs("blockSlopeAngle")]
        [SerializeField, Range(0f, 89f)] private float levelThreeUphillAngle = 45f;

        [Tooltip("Maximum downhill angle that remains controllable. A downhill steeper than this still performs a hard stop for safety.")]
        [SerializeField, Range(0f, 89f)] private float maximumDownhillSlopeAngle = 55f;

        [Tooltip("Distance for which an unsafe downhill must continue before it hard-stops movement. This rejects isolated short noisy samples.")]
        [FormerlySerializedAs("minimumBlockingSlopeLengthMeters")]
        [SerializeField, Min(0f)] private float minimumUnsafeDownhillLengthMeters = 1.5f;

        [Header("Step and Ledge Detection")]
        [Tooltip("Maximum abrupt height residual the robot can cross, in meters. The expected rise of the fitted slope is subtracted first, so a continuous steep slope is not mistaken for a vertical step.")]
        [SerializeField, Min(0f)] private float maximumStepHeightMeters = 0.65f;

        [Tooltip("Distance between detail-height samples used to find abrupt steps. This may be smaller than the slope evaluation spacing because it measures discontinuities rather than overall inclination.")]
        [SerializeField, Min(0.1f)] private float stepProbeSpacingMeters = 0.5f;

        [Tooltip("Number of parallel step probes distributed across the robot width. Odd values include the centre line. Each longitudinal slice uses the median residual across these probes, so an isolated noisy lane cannot hard-stop the robot.")]
        [SerializeField, Range(1, 9)] private int stepLateralSamples = 3;

        [Header("Path Evaluation")]
        [Tooltip("Distance ahead of the robot that is evaluated before each movement update. It should normally be at least as long as the contact footprint.")]
        [SerializeField, Min(0.25f)] private float movementProbeDistanceMeters = 6f;

        [Tooltip("Spacing in logical map meters between consecutive fitted-footprint evaluations along a path. Smaller values are more precise but more expensive.")]
        [SerializeField, Min(0.1f)] private float pathEvaluationSpacingMeters = 1f;

        [Tooltip("Short look-ahead used only for hard blockers (steps, unsafe drops and map boundaries) during real movement. Keep this near the robot footprint so a blocker several meters ahead does not feel like an invisible wall.")]
        [SerializeField, Min(0.25f)] private float hardStopProbeDistanceMeters = 2f;

        private MapTestSceneController map;
        private readonly float[] stepPreviousHeightScratch = new float[9];
        private readonly float[] stepResidualScratch = new float[9];

        public float LevelOneMaximumUphillAngle => levelOneMaximumUphillAngle;
        public float LevelThreeUphillAngle => levelThreeUphillAngle;
        public float BlockSlopeAngle => levelThreeUphillAngle;
        public float MaximumDownhillSlopeAngle => maximumDownhillSlopeAngle;
        public float RobotFootprintLengthMeters => robotFootprintLengthMeters;
        public float RobotFootprintWidthMeters => robotFootprintWidthMeters;
        public float MaximumStepHeightMeters => maximumStepHeightMeters;
        public float MovementProbeDistanceMeters => movementProbeDistanceMeters;
        public float HardStopProbeDistanceMeters => hardStopProbeDistanceMeters;
        public float PathSampleSpacingMeters => pathEvaluationSpacingMeters;
        public bool IsInitialized => map != null && map.HasGeneratedMap;

        public void Initialize(MapTestSceneController mapController)
        {
            map = mapController;
        }

        public void SetMaximumPassableSlopeAngle(float maximumSlopeAngle)
        {
            levelThreeUphillAngle = Mathf.Clamp(
                maximumSlopeAngle,
                levelOneMaximumUphillAngle + 0.1f,
                89f);
        }

        public UphillSlopeLevel ClassifyUphillSlope(float uphillAngle)
        {
            float angle = Mathf.Max(0f, uphillAngle);
            if (angle <= levelOneMaximumUphillAngle)
                return UphillSlopeLevel.LevelOne;
            return angle < levelThreeUphillAngle
                ? UphillSlopeLevel.LevelTwo
                : UphillSlopeLevel.LevelThree;
        }

        public void SetMaximumDownhillSlopeAngle(float maximumSlopeAngle)
        {
            maximumDownhillSlopeAngle = Mathf.Clamp(maximumSlopeAngle, 0f, 89f);
        }

        public SlopeTraversalResult EvaluateMovement(
            Vector2 startWorld,
            Vector2 worldDirection)
        {
            return EvaluateMovement(
                startWorld,
                worldDirection,
                movementProbeDistanceMeters);
        }

        public SlopeTraversalResult EvaluateImmediateSafety(
            Vector2 startWorld,
            Vector2 worldDirection)
        {
            return EvaluateMovement(
                startWorld,
                worldDirection,
                hardStopProbeDistanceMeters);
        }

        private SlopeTraversalResult EvaluateMovement(
            Vector2 startWorld,
            Vector2 worldDirection,
            float probeDistanceMeters)
        {
            if (!IsInitialized || worldDirection.sqrMagnitude < 0.000001f)
                return SlopeTraversalResult.NoData;

            if (!map.TrySampleWorldPosition(startWorld, out Vector2 startMapPosition, out _))
                return SlopeTraversalResult.NoData;

            Vector2 mapDirection = map.WorldDirectionToMapDirection(worldDirection);
            if (mapDirection.sqrMagnitude < 0.000001f)
                return SlopeTraversalResult.NoData;

            Vector2 endMapPosition = startMapPosition
                                     + mapDirection * Mathf.Max(
                                         0.25f,
                                         probeDistanceMeters);
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
                return CreateResult(
                    true,
                    false,
                    UphillSlopeLevel.LevelOne,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    Vector2.zero,
                    TraversalBlockReason.None);
            }

            Vector2 direction = path / pathLength;
            int evaluationCount = Mathf.Max(
                1,
                Mathf.CeilToInt(pathLength /
                                Mathf.Max(0.1f, pathEvaluationSpacingMeters)));
            float segmentLength = pathLength / evaluationCount;
            float consecutiveUnsafeDownhillLength = 0f;
            float maximumDirectionalSlope = 0f;
            float signedSlopeAtMaximum = 0f;
            float maximumUphillSlope = 0f;
            float maximumDownhillSlope = 0f;
            float maximumSurfaceSlope = 0f;
            float maximumStepHeight = 0f;
            float maximumRoughness = 0f;
            Vector2 downhillMapDirectionAtMaximum = Vector2.zero;

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

                maximumUphillSlope = Mathf.Max(
                    maximumUphillSlope,
                    Mathf.Max(0f, analysis.SignedDirectionalSlopeAngle));
                maximumDownhillSlope = Mathf.Max(
                    maximumDownhillSlope,
                    Mathf.Max(0f, -analysis.SignedDirectionalSlopeAngle));

                if (analysis.MaximumSurfaceSlopeAngle > maximumSurfaceSlope)
                {
                    maximumSurfaceSlope = analysis.MaximumSurfaceSlopeAngle;
                    downhillMapDirectionAtMaximum = analysis.DownhillMapDirection;
                }
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
                        true,
                        ClassifyUphillSlope(maximumUphillSlope),
                        maximumDirectionalSlope,
                        signedSlopeAtMaximum,
                        maximumUphillSlope,
                        maximumDownhillSlope,
                        maximumSurfaceSlope,
                        maximumStepHeight,
                        maximumRoughness,
                        downhillMapDirectionAtMaximum,
                        TraversalBlockReason.Step);
                }

                bool unsafeDownhill = analysis.SignedDirectionalSlopeAngle
                                      < -maximumDownhillSlopeAngle;
                consecutiveUnsafeDownhillLength = unsafeDownhill
                    ? consecutiveUnsafeDownhillLength + segmentLength
                    : 0f;

                if (unsafeDownhill
                    && consecutiveUnsafeDownhillLength
                    >= Mathf.Max(0f, minimumUnsafeDownhillLengthMeters))
                {
                    return CreateResult(
                        false,
                        true,
                        ClassifyUphillSlope(maximumUphillSlope),
                        maximumDirectionalSlope,
                        signedSlopeAtMaximum,
                        maximumUphillSlope,
                        maximumDownhillSlope,
                        maximumSurfaceSlope,
                        maximumStepHeight,
                        maximumRoughness,
                        downhillMapDirectionAtMaximum,
                        TraversalBlockReason.UnsafeDownhill);
                }
            }

            UphillSlopeLevel uphillLevel = ClassifyUphillSlope(maximumUphillSlope);
            bool deliberatelyPassable = uphillLevel != UphillSlopeLevel.LevelThree;
            return CreateResult(
                deliberatelyPassable,
                false,
                uphillLevel,
                maximumDirectionalSlope,
                signedSlopeAtMaximum,
                maximumUphillSlope,
                maximumDownhillSlope,
                maximumSurfaceSlope,
                maximumStepHeight,
                maximumRoughness,
                downhillMapDirectionAtMaximum,
                deliberatelyPassable
                    ? TraversalBlockReason.None
                    : TraversalBlockReason.Slope);
        }

        public SlopeTraversalResult EvaluateLocalSurface(Vector2 worldPosition)
        {
            return EvaluateCurrentSurface(worldPosition, Vector2.up);
        }

        public SlopeTraversalResult EvaluateCurrentSurface(
            Vector2 worldPosition,
            Vector2 worldDirection)
        {
            if (!IsInitialized
                || worldDirection.sqrMagnitude < 0.000001f
                || !map.TrySampleWorldPosition(worldPosition, out Vector2 mapPosition, out _))
            {
                return SlopeTraversalResult.NoData;
            }

            Vector2 mapDirection = map.WorldDirectionToMapDirection(worldDirection);
            if (mapDirection.sqrMagnitude < 0.000001f
                || !TryAnalyzeSurface(mapPosition, mapDirection, out SurfaceAnalysis analysis))
            {
                return SlopeTraversalResult.BlockedBoundary;
            }

            bool stepBlocked = analysis.MaximumStepResidualHeight > maximumStepHeightMeters;
            bool unsafeDownhill = analysis.SignedDirectionalSlopeAngle
                                  < -maximumDownhillSlopeAngle;
            float uphillAngle = Mathf.Max(0f, analysis.SignedDirectionalSlopeAngle);
            float downhillAngle = Mathf.Max(0f, -analysis.SignedDirectionalSlopeAngle);
            UphillSlopeLevel uphillLevel = ClassifyUphillSlope(uphillAngle);
            bool deliberatelyPassable = uphillLevel != UphillSlopeLevel.LevelThree;
            TraversalBlockReason reason = stepBlocked
                ? TraversalBlockReason.Step
                : unsafeDownhill
                    ? TraversalBlockReason.UnsafeDownhill
                    : deliberatelyPassable
                        ? TraversalBlockReason.None
                        : TraversalBlockReason.Slope;

            return CreateResult(
                deliberatelyPassable && !stepBlocked && !unsafeDownhill,
                stepBlocked || unsafeDownhill,
                uphillLevel,
                Mathf.Abs(analysis.SignedDirectionalSlopeAngle),
                analysis.SignedDirectionalSlopeAngle,
                uphillAngle,
                downhillAngle,
                analysis.MaximumSurfaceSlopeAngle,
                analysis.MaximumStepResidualHeight,
                analysis.SurfaceRoughness,
                analysis.DownhillMapDirection,
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
            Vector2 heightGradient = forward * forwardGradient
                                     + right * lateralGradient;
            Vector2 downhillMapDirection = heightGradient.sqrMagnitude > 0.000001f
                ? -heightGradient.normalized
                : Vector2.zero;

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
                surfaceRoughness,
                downhillMapDirection);
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

            Vector2 footprintBack = center - forward * (length * 0.5f);
            for (int lateralIndex = 0; lateralIndex < lateralSamples; lateralIndex++)
            {
                float lateral = lateralSamples == 1
                    ? 0f
                    : Mathf.Lerp(
                        -width * 0.5f,
                        width * 0.5f,
                        lateralIndex / (float)(lateralSamples - 1));
                Vector2 previousPosition = footprintBack + right * lateral;
                if (!map.TrySampleDetailMapPosition(
                        previousPosition,
                        out stepPreviousHeightScratch[lateralIndex]))
                {
                    return false;
                }
            }

            for (int segmentIndex = 1; segmentIndex <= segments; segmentIndex++)
            {
                float forwardOffset = segmentIndex * actualSpacing;
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
                    Vector2 currentPosition = footprintBack
                                              + forward * forwardOffset
                                              + right * lateral;
                    if (!map.TrySampleDetailMapPosition(
                            currentPosition,
                            out float currentHeight))
                    {
                        return false;
                    }

                    float measuredHeightChange = currentHeight
                                                 - stepPreviousHeightScratch[lateralIndex];
                    float expectedSlopeChange = fittedForwardGradient * actualSpacing;
                    stepResidualScratch[lateralIndex] = Mathf.Abs(
                        measuredHeightChange - expectedSlopeChange);
                    stepPreviousHeightScratch[lateralIndex] = currentHeight;
                }

                SortAscending(stepResidualScratch, lateralSamples);
                float medianResidual = stepResidualScratch[lateralSamples / 2];
                maximumResidual = Mathf.Max(maximumResidual, medianResidual);
            }

            return true;
        }

        private static void SortAscending(float[] values, int count)
        {
            for (int index = 1; index < count; index++)
            {
                float value = values[index];
                int insertionIndex = index - 1;
                while (insertionIndex >= 0 && values[insertionIndex] > value)
                {
                    values[insertionIndex + 1] = values[insertionIndex];
                    insertionIndex--;
                }

                values[insertionIndex + 1] = value;
            }
        }

        private SlopeTraversalResult CreateResult(
            bool isPassable,
            bool requiresHardStop,
            UphillSlopeLevel uphillLevel,
            float slopeAngle,
            float signedSlopeAngle,
            float maximumUphillAngle,
            float maximumDownhillAngle,
            float maximumSurfaceSlopeAngle,
            float maximumStepHeight,
            float surfaceRoughness,
            Vector2 downhillMapDirection,
            TraversalBlockReason blockReason)
        {
            return new SlopeTraversalResult(
                true,
                isPassable,
                requiresHardStop,
                uphillLevel,
                slopeAngle,
                signedSlopeAngle,
                maximumUphillAngle,
                maximumDownhillAngle,
                maximumSurfaceSlopeAngle,
                maximumStepHeight,
                surfaceRoughness,
                map != null
                    ? map.MapDirectionToWorldDirection(downhillMapDirection)
                    : Vector2.zero,
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
            levelOneMaximumUphillAngle = Mathf.Clamp(
                levelOneMaximumUphillAngle,
                0f,
                88f);
            levelThreeUphillAngle = Mathf.Clamp(
                levelThreeUphillAngle,
                levelOneMaximumUphillAngle + 0.1f,
                89f);
            maximumDownhillSlopeAngle = Mathf.Clamp(
                maximumDownhillSlopeAngle,
                0f,
                89f);
            minimumUnsafeDownhillLengthMeters = Mathf.Max(
                0f,
                minimumUnsafeDownhillLengthMeters);
            maximumStepHeightMeters = Mathf.Max(0f, maximumStepHeightMeters);
            stepProbeSpacingMeters = Mathf.Max(0.1f, stepProbeSpacingMeters);
            stepLateralSamples = EnsureOdd(stepLateralSamples, 1, 9);
            movementProbeDistanceMeters = Mathf.Max(
                robotFootprintLengthMeters,
                movementProbeDistanceMeters);
            pathEvaluationSpacingMeters = Mathf.Max(0.1f, pathEvaluationSpacingMeters);
            hardStopProbeDistanceMeters = Mathf.Max(
                0.25f,
                hardStopProbeDistanceMeters);
        }

        private readonly struct SurfaceAnalysis
        {
            public float SignedDirectionalSlopeAngle { get; }
            public float MaximumSurfaceSlopeAngle { get; }
            public float MaximumStepResidualHeight { get; }
            public float SurfaceRoughness { get; }
            public Vector2 DownhillMapDirection { get; }

            public SurfaceAnalysis(
                float signedDirectionalSlopeAngle,
                float maximumSurfaceSlopeAngle,
                float maximumStepResidualHeight,
                float surfaceRoughness,
                Vector2 downhillMapDirection)
            {
                SignedDirectionalSlopeAngle = signedDirectionalSlopeAngle;
                MaximumSurfaceSlopeAngle = maximumSurfaceSlopeAngle;
                MaximumStepResidualHeight = maximumStepResidualHeight;
                SurfaceRoughness = surfaceRoughness;
                DownhillMapDirection = downhillMapDirection;
            }
        }
    }
}
