using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(
        fileName = "AnimalSpeciesConfig",
        menuName = "Animal Game/Animals/Animal Species Config")]
    public sealed class AnimalSpeciesConfig : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string speciesName = "Muskrat";

        [Header("Activity And Movement")]
        [SerializeField, Min(0.1f)] private float activityRadiusMeters = 10f;
        [SerializeField, Min(0f)] private float dailyMoveSpeedMetersPerSecond = 1.15f;
        [SerializeField, Min(0f)] private float fleeSpeedMetersPerSecond = 2.8f;
        [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 420f;
        [SerializeField, Min(0.01f)] private float arrivalDistanceMeters = 0.18f;
        [SerializeField, Min(0f)] private float bodyRadiusMeters = 0.16f;
        [SerializeField, Min(0.1f)] private float maximumTravelTimeSeconds = 18f;

        [Header("Daily Behaviour Weights And Durations")]
        [SerializeField] private List<AnimalDailyBehaviourSettings>
            dailyBehaviours = new List<AnimalDailyBehaviourSettings>
            {
                new AnimalDailyBehaviourSettings(
                    AnimalDailyBehaviourKind.EatAtNearbyPlant,
                    1f,
                    3f,
                    6f),
                new AnimalDailyBehaviourSettings(
                    AnimalDailyBehaviourKind.RoamAndLook,
                    1.35f,
                    3f,
                    7f),
                new AnimalDailyBehaviourSettings(
                    AnimalDailyBehaviourKind.TravelToPlantAndEat,
                    1.5f,
                    4f,
                    8f),
                new AnimalDailyBehaviourSettings(
                    AnimalDailyBehaviourKind.DiveAndResurface,
                    1f,
                    4f,
                    8f)
            };

        [Header("Food Preferences")]
        [SerializeField] private List<AnimalFoodPreference> foodPreferences =
            new List<AnimalFoodPreference>
            {
                new AnimalFoodPreference(AnimalFoodType.Bush, 1f),
                new AnimalFoodPreference(AnimalFoodType.Lotus, 1f),
                new AnimalFoodPreference(AnimalFoodType.Australis, 1f)
            };
        [SerializeField, Min(0f)] private float nearbyFoodDistanceMeters = 1.6f;
        [SerializeField, Min(0f)] private float foodApproachPaddingMeters = 0.08f;

        [Header("Idle Looking")]
        [SerializeField] private Vector2 lookIntervalSeconds = new Vector2(0.65f, 1.4f);
        [SerializeField, Range(0f, 180f)] private float lookAngleDegrees = 75f;

        [Header("Diving")]
        [SerializeField, Min(0f)] private float minimumDiveWaterDepthMeters = 0.05f;
        [SerializeField, Min(0.1f)] private float resurfaceRadiusMeters = 2.5f;
        [SerializeField, Min(0.01f)] private float submergeTransitionSeconds = 0.35f;
        [SerializeField, Min(0.1f)] private float waterSearchSpacingMeters = 1.5f;

        [Header("Alert Detection")]
        [SerializeField, Min(0.1f)] private float alertRadiusMeters = 8f;
        [SerializeField, Min(0.05f)] private float detectionIntervalSeconds = 0.5f;
        [SerializeField, Range(0f, 1f)] private float baseDetectionChancePerCheck = 0.08f;
        [SerializeField, Min(1f)] private float nearestDetectionMultiplier = 3f;
        [SerializeField, Min(0.01f)] private float playerSpeedForMaximumBonus = 3f;
        [SerializeField, Min(1f)] private float maximumPlayerSpeedDetectionMultiplier = 2f;
        [Tooltip("Full forward angle that receives the unobstructed direct-sight detection bonus. Players outside this cone can still be detected through hearing.")]
        [SerializeField, Range(0f, 360f)] private float directVisionAngleDegrees = 120f;
        [Tooltip("Additional chance multiplier when no solid map prop blocks the direct line to the player. This is a bonus only; hearing still works through cover and has no facing-angle requirement.")]
        [SerializeField, Min(1f)] private float directLineOfSightDetectionMultiplier = 1.5f;

        [Header("Curious Reactions")]
        [SerializeField, Min(0.02f)] private float reactionIntervalSeconds = 0.1f;
        [SerializeField, Range(0f, 1f)] private float baseFleeChancePerCheck = 0.05f;
        [SerializeField, Min(1f)] private float nearestFleeMultiplier = 2.5f;
        [SerializeField, Range(0f, 1f)] private float baseAggressionChancePerCheck;
        [SerializeField, Min(1f)] private float nearestAggressionMultiplier = 1f;
        [SerializeField, Min(0f)] private float curiousLostPlayerDelaySeconds = 2.5f;

        [Header("Frightened Hiding And Return")]
        [Tooltip("Random duration spent fully hidden after a frightened escape, before checking whether it is safe to return.")]
        [SerializeField] private Vector2 frightenedHideDurationSeconds =
            new Vector2(6f, 10f);
        [Tooltip("How often a hidden animal retries its safe return check after the minimum hiding time has elapsed.")]
        [SerializeField, Min(0.05f)]
        private float hideSafetyCheckIntervalSeconds = 0.75f;
        [Tooltip("Required player distance from the emergence point, expressed as a multiple of this species' alert radius.")]
        [SerializeField, Min(0f)]
        private float reappearSafeDistanceMultiplier = 1.1f;
        [Tooltip("Detection-free grace period after the animal has fully reappeared.")]
        [SerializeField, Min(0f)]
        private float postReappearGraceDurationSeconds = 1.5f;

        public string SpeciesName => speciesName;
        public float ActivityRadiusMeters => activityRadiusMeters;
        public float DailyMoveSpeedMetersPerSecond => dailyMoveSpeedMetersPerSecond;
        public float FleeSpeedMetersPerSecond => fleeSpeedMetersPerSecond;
        public float TurnSpeedDegreesPerSecond => turnSpeedDegreesPerSecond;
        public float ArrivalDistanceMeters => arrivalDistanceMeters;
        public float BodyRadiusMeters => bodyRadiusMeters;
        public float MaximumTravelTimeSeconds => maximumTravelTimeSeconds;
        public IReadOnlyList<AnimalDailyBehaviourSettings> DailyBehaviours =>
            dailyBehaviours;
        public float NearbyFoodDistanceMeters => nearbyFoodDistanceMeters;
        public float FoodApproachPaddingMeters => foodApproachPaddingMeters;
        public Vector2 LookIntervalSeconds => lookIntervalSeconds;
        public float LookAngleDegrees => lookAngleDegrees;
        public float MinimumDiveWaterDepthMeters => minimumDiveWaterDepthMeters;
        public float ResurfaceRadiusMeters => resurfaceRadiusMeters;
        public float SubmergeTransitionSeconds => submergeTransitionSeconds;
        public float WaterSearchSpacingMeters => waterSearchSpacingMeters;
        public float AlertRadiusMeters => alertRadiusMeters;
        public float DetectionIntervalSeconds => detectionIntervalSeconds;
        public float BaseDetectionChancePerCheck => baseDetectionChancePerCheck;
        public float NearestDetectionMultiplier => nearestDetectionMultiplier;
        public float PlayerSpeedForMaximumBonus => playerSpeedForMaximumBonus;
        public float MaximumPlayerSpeedDetectionMultiplier =>
            maximumPlayerSpeedDetectionMultiplier;
        public float DirectVisionAngleDegrees => directVisionAngleDegrees;
        public float DirectLineOfSightDetectionMultiplier =>
            directLineOfSightDetectionMultiplier;
        public float ReactionIntervalSeconds => reactionIntervalSeconds;
        public float BaseFleeChancePerCheck => baseFleeChancePerCheck;
        public float NearestFleeMultiplier => nearestFleeMultiplier;
        public float BaseAggressionChancePerCheck =>
            baseAggressionChancePerCheck;
        public float NearestAggressionMultiplier =>
            nearestAggressionMultiplier;
        public float CuriousLostPlayerDelaySeconds =>
            curiousLostPlayerDelaySeconds;
        public Vector2 FrightenedHideDurationSeconds =>
            frightenedHideDurationSeconds;
        public float HideSafetyCheckIntervalSeconds =>
            hideSafetyCheckIntervalSeconds;
        public float ReappearSafeDistanceMeters => AlertRadiusMeters
                                                   * reappearSafeDistanceMultiplier;
        public float PostReappearGraceDurationSeconds =>
            postReappearGraceDurationSeconds;

        public float ChooseFrightenedHideDuration()
        {
            float minimum = Mathf.Max(
                0f,
                Mathf.Min(
                    frightenedHideDurationSeconds.x,
                    frightenedHideDurationSeconds.y));
            float maximum = Mathf.Max(
                minimum,
                Mathf.Max(
                    frightenedHideDurationSeconds.x,
                    frightenedHideDurationSeconds.y));
            return Random.Range(minimum, maximum);
        }

        public float GetFoodSelectionWeight(AnimalFoodType foodType)
        {
            if (foodPreferences == null)
                return 0f;

            for (int index = 0; index < foodPreferences.Count; index++)
            {
                AnimalFoodPreference preference = foodPreferences[index];
                if (preference != null && preference.FoodType == foodType)
                    return preference.SelectionWeight;
            }

            return 0f;
        }

        public float ChooseLookInterval()
        {
            float minimum = Mathf.Max(0.05f, lookIntervalSeconds.x);
            float maximum = Mathf.Max(minimum, lookIntervalSeconds.y);
            return Random.Range(minimum, maximum);
        }

        private void OnValidate()
        {
            activityRadiusMeters = Mathf.Max(0.1f, activityRadiusMeters);
            dailyMoveSpeedMetersPerSecond = Mathf.Max(
                0f,
                dailyMoveSpeedMetersPerSecond);
            fleeSpeedMetersPerSecond = Mathf.Max(0f, fleeSpeedMetersPerSecond);
            turnSpeedDegreesPerSecond = Mathf.Max(0f, turnSpeedDegreesPerSecond);
            arrivalDistanceMeters = Mathf.Max(0.01f, arrivalDistanceMeters);
            bodyRadiusMeters = Mathf.Max(0f, bodyRadiusMeters);
            maximumTravelTimeSeconds = Mathf.Max(0.1f, maximumTravelTimeSeconds);
            nearbyFoodDistanceMeters = Mathf.Max(0f, nearbyFoodDistanceMeters);
            foodApproachPaddingMeters = Mathf.Max(0f, foodApproachPaddingMeters);
            lookIntervalSeconds.x = Mathf.Max(0.05f, lookIntervalSeconds.x);
            lookIntervalSeconds.y = Mathf.Max(
                lookIntervalSeconds.x,
                lookIntervalSeconds.y);
            minimumDiveWaterDepthMeters = Mathf.Max(
                0f,
                minimumDiveWaterDepthMeters);
            resurfaceRadiusMeters = Mathf.Max(0.1f, resurfaceRadiusMeters);
            submergeTransitionSeconds = Mathf.Max(
                0.01f,
                submergeTransitionSeconds);
            waterSearchSpacingMeters = Mathf.Max(0.1f, waterSearchSpacingMeters);
            alertRadiusMeters = Mathf.Max(0.1f, alertRadiusMeters);
            detectionIntervalSeconds = Mathf.Max(0.05f, detectionIntervalSeconds);
            baseDetectionChancePerCheck = Mathf.Clamp01(
                baseDetectionChancePerCheck);
            nearestDetectionMultiplier = Mathf.Max(
                1f,
                nearestDetectionMultiplier);
            playerSpeedForMaximumBonus = Mathf.Max(
                0.01f,
                playerSpeedForMaximumBonus);
            maximumPlayerSpeedDetectionMultiplier = Mathf.Max(
                1f,
                maximumPlayerSpeedDetectionMultiplier);
            directVisionAngleDegrees = Mathf.Clamp(
                directVisionAngleDegrees,
                0f,
                360f);
            directLineOfSightDetectionMultiplier = Mathf.Max(
                1f,
                directLineOfSightDetectionMultiplier);
            reactionIntervalSeconds = Mathf.Max(0.02f, reactionIntervalSeconds);
            baseFleeChancePerCheck = Mathf.Clamp01(baseFleeChancePerCheck);
            nearestFleeMultiplier = Mathf.Max(1f, nearestFleeMultiplier);
            baseAggressionChancePerCheck = Mathf.Clamp01(
                baseAggressionChancePerCheck);
            nearestAggressionMultiplier = Mathf.Max(
                1f,
                nearestAggressionMultiplier);
            curiousLostPlayerDelaySeconds = Mathf.Max(
                0f,
                curiousLostPlayerDelaySeconds);
            float hideMinimum = Mathf.Max(
                0f,
                Mathf.Min(
                    frightenedHideDurationSeconds.x,
                    frightenedHideDurationSeconds.y));
            float hideMaximum = Mathf.Max(
                hideMinimum,
                Mathf.Max(
                    frightenedHideDurationSeconds.x,
                    frightenedHideDurationSeconds.y));
            frightenedHideDurationSeconds = new Vector2(
                hideMinimum,
                hideMaximum);
            hideSafetyCheckIntervalSeconds = Mathf.Max(
                0.05f,
                hideSafetyCheckIntervalSeconds);
            reappearSafeDistanceMultiplier = Mathf.Max(
                0f,
                reappearSafeDistanceMultiplier);
            postReappearGraceDurationSeconds = Mathf.Max(
                0f,
                postReappearGraceDurationSeconds);

            if (dailyBehaviours != null)
            {
                for (int index = 0; index < dailyBehaviours.Count; index++)
                    dailyBehaviours[index]?.ClampValues();
            }

            if (foodPreferences != null)
            {
                for (int index = 0; index < foodPreferences.Count; index++)
                    foodPreferences[index]?.ClampValues();
            }
        }
    }
}
