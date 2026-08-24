using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Perception")]
    public sealed class AnimalPerception : MonoBehaviour
    {
        public RobotMover Player { get; private set; }

        private MapTestSceneController map;
        private AnimalSpeciesConfig config;
        private AnimalMotor motor;
        private float detectionCountdown;
        private bool playerWasInsideAlertRange;

        public void Initialize(
            MapTestSceneController mapController,
            AnimalSpeciesConfig speciesConfig)
        {
            map = mapController;
            config = speciesConfig;
            motor = GetComponent<AnimalMotor>();
            Player = FindObjectOfType<RobotMover>();
            detectionCountdown = config != null
                ? config.DetectionIntervalSeconds
                : 0.5f;
            playerWasInsideAlertRange = false;
        }

        public bool TickDetection(float deltaTime)
        {
            if (!TryGetPlayerProximity(out float proximity, out _))
            {
                playerWasInsideAlertRange = false;
                return false;
            }

            if (!playerWasInsideAlertRange)
            {
                playerWasInsideAlertRange = true;
                detectionCountdown = config.DetectionIntervalSeconds;
                return false;
            }

            detectionCountdown -= deltaTime;
            if (detectionCountdown > 0f)
                return false;

            detectionCountdown += config.DetectionIntervalSeconds;
            float playerSpeedProgress = Mathf.Clamp01(
                Mathf.Abs(Player.CurrentSpeed)
                / config.PlayerSpeedForMaximumBonus);
            float distanceMultiplier = Mathf.Lerp(
                1f,
                config.NearestDetectionMultiplier,
                proximity);
            float speedMultiplier = Mathf.Lerp(
                1f,
                config.MaximumPlayerSpeedDetectionMultiplier,
                playerSpeedProgress);
            float directSightMultiplier = HasDirectSightOfPlayer()
                ? config.DirectLineOfSightDetectionMultiplier
                : 1f;
            float detectionChance = Mathf.Clamp01(
                config.BaseDetectionChancePerCheck
                * distanceMultiplier
                * speedMultiplier
                * directSightMultiplier);

            // Hearing is part of detection, so this deliberately has no
            // forward-view-cone requirement. Direct sight is only a bonus;
            // cover never removes the hearing-based chance. A successful roll
            // means the animal noticed the player and immediately gets Curious.
            return Random.value < detectionChance;
        }

        private bool HasDirectSightOfPlayer()
        {
            if (map == null || Player == null
                || !map.TrySampleWorldPosition(
                    transform.position,
                    out Vector2 animalMapPosition,
                    out _)
                || !map.TrySampleWorldPosition(
                    Player.transform.position,
                    out Vector2 playerMapPosition,
                    out _))
            {
                return false;
            }

            Vector2 line = playerMapPosition - animalMapPosition;
            float lineLengthSquared = line.sqrMagnitude;
            if (lineLengthSquared <= 0.0001f)
                return true;

            float fullVisionAngle = config.DirectVisionAngleDegrees;
            if (fullVisionAngle < 359.999f)
            {
                Vector2 facingDirection = motor != null
                    ? motor.FacingMapDirection
                    : map.WorldDirectionToMapDirection(transform.up);
                if (facingDirection.sqrMagnitude <= 0.000001f
                    || Vector2.Angle(facingDirection, line)
                    > fullVisionAngle * 0.5f)
                {
                    return false;
                }
            }

            foreach (HeightMapObstacleFootprint obstacle
                     in HeightMapObstacleFootprint.ActiveFootprints)
            {
                if (obstacle == null
                    || !obstacle.BlocksTraversal
                    || obstacle.RadiusMeters <= 0f
                    || !map.TrySampleWorldPosition(
                        obstacle.transform.position,
                        out Vector2 obstacleMapPosition,
                        out _))
                {
                    continue;
                }

                float progress = Mathf.Clamp01(
                    Vector2.Dot(
                        obstacleMapPosition - animalMapPosition,
                        line)
                    / lineLengthSquared);
                Vector2 closestPoint = animalMapPosition + line * progress;
                float radius = obstacle.RadiusMeters;
                if ((obstacleMapPosition - closestPoint).sqrMagnitude
                    < radius * radius)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetPlayerProximity(
            out float proximity,
            out float distanceMeters)
        {
            proximity = 0f;
            distanceMeters = float.PositiveInfinity;
            if (config == null || map == null)
                return false;

            if (Player == null)
                Player = FindObjectOfType<RobotMover>();
            if (Player == null
                || !map.TrySampleWorldPosition(
                    transform.position,
                    out Vector2 animalMapPosition,
                    out _)
                || !map.TrySampleWorldPosition(
                    Player.transform.position,
                    out Vector2 playerMapPosition,
                    out _))
            {
                return false;
            }

            distanceMeters = Vector2.Distance(
                animalMapPosition,
                playerMapPosition);
            if (distanceMeters > config.AlertRadiusMeters)
                return false;

            proximity = 1f - Mathf.Clamp01(
                distanceMeters / config.AlertRadiusMeters);
            return true;
        }

        public bool TryGetPlayerMapPosition(out Vector2 playerMapPosition)
        {
            playerMapPosition = Vector2.zero;
            if (map == null)
                return false;

            if (Player == null)
                Player = FindObjectOfType<RobotMover>();
            return Player != null
                   && map.TrySampleWorldPosition(
                       Player.transform.position,
                       out playerMapPosition,
                       out _);
        }
    }
}
