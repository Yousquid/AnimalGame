using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Motor")]
    public sealed class AnimalMotor : MonoBehaviour
    {
        private static readonly float[] avoidanceAngles =
        {
            0f,
            30f,
            -30f,
            60f,
            -60f,
            90f,
            -90f,
            120f,
            -120f
        };

        public bool HasTarget { get; private set; }
        public bool HasArrived { get; private set; }
        public float CurrentSpeedMetersPerSecond { get; private set; }
        public Vector2 TargetMapPosition { get; private set; }
        public Vector2 FacingMapDirection { get; private set; } = Vector2.up;
        public Vector2 CurrentMapPosition
        {
            get
            {
                if (map != null
                    && map.TrySampleWorldPosition(
                        transform.position,
                        out Vector2 mapPosition,
                        out _))
                {
                    return mapPosition;
                }

                return Vector2.zero;
            }
        }

        private MapTestSceneController map;
        private AnimalSpeciesConfig config;
        private float targetSpeedMetersPerSecond;
        private Vector2 desiredFacingMapDirection = Vector2.up;

        public void Initialize(
            MapTestSceneController mapController,
            AnimalSpeciesConfig speciesConfig)
        {
            map = mapController;
            config = speciesConfig;
            FacingMapDirection = map != null
                ? map.WorldDirectionToMapDirection(transform.up)
                : Vector2.up;
            if (FacingMapDirection.sqrMagnitude < 0.000001f)
                FacingMapDirection = Vector2.up;
            desiredFacingMapDirection = FacingMapDirection;
            Stop();
        }

        public bool SetTarget(
            Vector2 mapPositionMeters,
            float speedMetersPerSecond)
        {
            if (map == null || !map.TrySampleMapPosition(mapPositionMeters, out _))
                return false;

            TargetMapPosition = mapPositionMeters;
            targetSpeedMetersPerSecond = Mathf.Max(0f, speedMetersPerSecond);
            HasTarget = true;
            HasArrived = false;
            return true;
        }

        public void Stop()
        {
            HasTarget = false;
            HasArrived = false;
            CurrentSpeedMetersPerSecond = 0f;
        }

        public void FaceMapPosition(Vector2 mapPositionMeters)
        {
            Vector2 direction = mapPositionMeters - CurrentMapPosition;
            FaceMapDirection(direction);
        }

        public void FaceMapDirection(Vector2 mapDirection)
        {
            if (mapDirection.sqrMagnitude <= 0.000001f)
                return;

            desiredFacingMapDirection = mapDirection.normalized;
        }

        public void Tick(float deltaTime)
        {
            if (map == null || config == null || deltaTime <= 0f)
                return;

            Vector2 currentMapPosition = CurrentMapPosition;
            if (HasTarget)
            {
                Vector2 toTarget = TargetMapPosition - currentMapPosition;
                float distance = toTarget.magnitude;
                if (distance <= config.ArrivalDistanceMeters)
                {
                    HasTarget = false;
                    HasArrived = true;
                    CurrentSpeedMetersPerSecond = 0f;
                }
                else
                {
                    float step = Mathf.Min(
                        distance,
                        targetSpeedMetersPerSecond * deltaTime);
                    if (step > 0f
                        && TryChooseMovementDirection(
                            currentMapPosition,
                            toTarget / distance,
                            step,
                            out Vector2 movementDirection))
                    {
                        desiredFacingMapDirection = movementDirection;
                        Vector2 nextMapPosition = currentMapPosition
                                                  + movementDirection * step;
                        ApplyMapPosition(nextMapPosition);
                        CurrentSpeedMetersPerSecond = step / deltaTime;
                    }
                    else
                    {
                        CurrentSpeedMetersPerSecond = 0f;
                    }
                }
            }
            else
            {
                CurrentSpeedMetersPerSecond = 0f;
            }

            RotateTowardsDesiredFacing(deltaTime);
        }

        public bool TeleportToMapPosition(Vector2 mapPositionMeters)
        {
            if (map == null || !map.TrySampleMapPosition(mapPositionMeters, out _))
                return false;

            ApplyMapPosition(mapPositionMeters);
            HasTarget = false;
            HasArrived = true;
            CurrentSpeedMetersPerSecond = 0f;
            return true;
        }

        public bool CanOccupyMapPosition(Vector2 mapPositionMeters)
        {
            if (map == null || config == null
                || !map.TrySampleMapPosition(mapPositionMeters, out _))
            {
                return false;
            }

            foreach (HeightMapObstacleFootprint footprint
                     in HeightMapObstacleFootprint.ActiveFootprints)
            {
                if (footprint == null
                    || !footprint.isActiveAndEnabled
                    || !footprint.BlocksTraversal
                    || !map.TrySampleWorldPosition(
                        footprint.transform.position,
                        out Vector2 obstaclePosition,
                        out _))
                {
                    continue;
                }

                float clearance = footprint.RadiusMeters
                                  + config.BodyRadiusMeters;
                if ((mapPositionMeters - obstaclePosition).sqrMagnitude
                    < clearance * clearance)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryChooseMovementDirection(
            Vector2 currentMapPosition,
            Vector2 directDirection,
            float step,
            out Vector2 movementDirection)
        {
            for (int index = 0; index < avoidanceAngles.Length; index++)
            {
                Vector2 candidateDirection = Rotate(
                    directDirection,
                    avoidanceAngles[index]);
                Vector2 candidatePosition = currentMapPosition
                                            + candidateDirection * step;
                if (!map.TrySampleMapPosition(candidatePosition, out _)
                    || IsObstacleSweepBlocked(
                        currentMapPosition,
                        candidatePosition))
                {
                    continue;
                }

                movementDirection = candidateDirection;
                return true;
            }

            movementDirection = Vector2.zero;
            return false;
        }

        private bool IsObstacleSweepBlocked(
            Vector2 startMapPosition,
            Vector2 endMapPosition)
        {
            foreach (HeightMapObstacleFootprint footprint
                     in HeightMapObstacleFootprint.ActiveFootprints)
            {
                if (footprint == null
                    || !footprint.isActiveAndEnabled
                    || !footprint.BlocksTraversal
                    || !map.TrySampleWorldPosition(
                        footprint.transform.position,
                        out Vector2 obstaclePosition,
                        out _))
                {
                    continue;
                }

                float clearance = footprint.RadiusMeters
                                  + config.BodyRadiusMeters;
                float clearanceSquared = clearance * clearance;
                float startDistanceSquared =
                    (startMapPosition - obstaclePosition).sqrMagnitude;
                float endDistanceSquared =
                    (endMapPosition - obstaclePosition).sqrMagnitude;

                // Allow an animal that was authored inside a footprint to move
                // outward instead of becoming permanently trapped.
                if (startDistanceSquared < clearanceSquared
                    && endDistanceSquared > startDistanceSquared)
                {
                    continue;
                }

                if (DistanceSquaredToSegment(
                        obstaclePosition,
                        startMapPosition,
                        endMapPosition) < clearanceSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyMapPosition(Vector2 mapPositionMeters)
        {
            Vector3 worldPosition = map.MapPositionToWorld(mapPositionMeters);
            worldPosition.z = transform.position.z;
            transform.position = worldPosition;
        }

        private void RotateTowardsDesiredFacing(float deltaTime)
        {
            if (desiredFacingMapDirection.sqrMagnitude <= 0.000001f)
                return;

            Vector2 worldDirection = map.MapDirectionToWorldDirection(
                desiredFacingMapDirection);
            if (worldDirection.sqrMagnitude <= 0.000001f)
                return;

            float targetAngle = Vector2.SignedAngle(Vector2.up, worldDirection);
            float currentAngle = transform.eulerAngles.z;
            float nextAngle = Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                config.TurnSpeedDegreesPerSecond * deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
            FacingMapDirection = map.WorldDirectionToMapDirection(transform.up);
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine).normalized;
        }

        private static float DistanceSquaredToSegment(
            Vector2 point,
            Vector2 segmentStart,
            Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
                return (point - segmentStart).sqrMagnitude;

            float t = Mathf.Clamp01(
                Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 closest = segmentStart + segment * t;
            return (point - closest).sqrMagnitude;
        }
    }
}
