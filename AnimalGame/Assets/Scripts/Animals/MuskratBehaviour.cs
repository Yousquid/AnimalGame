using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Muskrat Behaviour")]
    public sealed class MuskratBehaviour : AnimalBehaviourSet
    {
        private enum DailyPhase
        {
            None,
            Eating,
            TravellingToRoam,
            LookingAround,
            TravellingToFood,
            TravellingToDive,
            Submerging,
            Underwater,
            Surfacing,
            FallbackIdle
        }

        private enum FleePhase
        {
            None,
            TravellingToWater,
            FallbackRunning,
            Submerging
        }

        private enum HidingPhase
        {
            None,
            Hidden,
            Surfacing
        }

        private readonly List<AnimalDailyBehaviourSettings> availableBehaviours =
            new List<AnimalDailyBehaviourSettings>();

        private AnimalDailyBehaviourSettings currentDailySettings;
        private AnimalFoodSource currentFoodSource;
        private DailyPhase dailyPhase;
        private FleePhase fleePhase;
        private HidingPhase hidingPhase;
        private float actionTimer;
        private float travelTimer;
        private float transitionTimer;
        private float lookCountdown;
        private float fallbackFleeTimer;
        private float activitySoundCountdown;
        private int lookDirectionSign = 1;
        private Vector2 lookBaseDirection = Vector2.up;
        private bool hidingPrefersWater;
        private bool emergingFromWater;

        public override bool SupportsAggression => false;

        public override void EnterDaily()
        {
            CleanupDailyVisualState();
            BeginNextDailyBehaviour();
        }

        public override void TickDaily(float deltaTime)
        {
            switch (dailyPhase)
            {
                case DailyPhase.Eating:
                    TickEating(deltaTime);
                    break;
                case DailyPhase.TravellingToRoam:
                    TickTravelToRoam(deltaTime);
                    break;
                case DailyPhase.LookingAround:
                    TickLookingAround(deltaTime);
                    break;
                case DailyPhase.TravellingToFood:
                    TickTravelToFood(deltaTime);
                    break;
                case DailyPhase.TravellingToDive:
                    TickTravelToDive(deltaTime);
                    break;
                case DailyPhase.Submerging:
                    TickDailySubmerging(deltaTime);
                    break;
                case DailyPhase.Underwater:
                    TickUnderwater(deltaTime);
                    break;
                case DailyPhase.Surfacing:
                    TickSurfacing(deltaTime);
                    break;
                case DailyPhase.FallbackIdle:
                    Agent.SoundEmitter?.TickRepeated(
                        AnimalSoundKind.Idle,
                        ref activitySoundCountdown,
                        deltaTime);
                    actionTimer -= deltaTime;
                    if (actionTimer <= 0f)
                        BeginNextDailyBehaviour();
                    break;
                default:
                    BeginNextDailyBehaviour();
                    break;
            }
        }

        public override void ExitDaily()
        {
            CleanupDailyVisualState();
            dailyPhase = DailyPhase.None;
            currentDailySettings = null;
            currentFoodSource = null;
            Motor?.Stop();
        }

        public override void EnterCurious()
        {
            // Curious muskrats no longer use the daily left/right looking
            // pattern. Their direct-vision cone continuously tracks the player.
            dailyPhase = DailyPhase.None;
            currentDailySettings = null;
            currentFoodSource = null;
            lookCountdown = 0f;
            Motor.Stop();
            Agent.PlaceholderView?.RestoreVisibleAppearance();
            FacePlayerWhileCurious();
            BeginRepeatedSound(AnimalSoundKind.Curious);
        }

        public override void TickCurious(float deltaTime)
        {
            Motor.Stop();
            FacePlayerWhileCurious();
            Agent.SoundEmitter?.TickRepeated(
                AnimalSoundKind.Curious,
                ref activitySoundCountdown,
                deltaTime);
        }

        public override void ExitCurious()
        {
            Motor.Stop();
        }

        public override void EnterFleeing()
        {
            fleePhase = FleePhase.None;
            fallbackFleeTimer = 0f;
            transitionTimer = 0f;
            activitySoundCountdown = 0f;
            Motor.Stop();
            Agent.PlaceholderView?.RestoreVisibleAppearance();

            if (IsStandingInWater())
            {
                BeginFleeSubmerge();
                return;
            }

            if (TryFindNearestWaterPoint(out Vector2 waterTarget)
                && Motor.SetTarget(
                    waterTarget,
                    Config.FleeSpeedMetersPerSecond))
            {
                travelTimer = 0f;
                fleePhase = FleePhase.TravellingToWater;
                return;
            }

            BeginFallbackFlee();
        }

        public override void TickFleeing(float deltaTime)
        {
            switch (fleePhase)
            {
                case FleePhase.TravellingToWater:
                    travelTimer += deltaTime;
                    if (IsStandingInWater() || Motor.HasArrived)
                    {
                        BeginFleeSubmerge();
                    }
                    else if (travelTimer >= Config.MaximumTravelTimeSeconds)
                    {
                        BeginFallbackFlee();
                    }
                    break;
                case FleePhase.FallbackRunning:
                    fallbackFleeTimer += deltaTime;
                    if (IsOutsideCameraMargin(0.15f)
                        || fallbackFleeTimer >= 10f
                        || Motor.HasArrived)
                    {
                        hidingPrefersWater = false;
                        Agent.BeginHiding();
                    }
                    break;
                case FleePhase.Submerging:
                    transitionTimer += deltaTime;
                    float progress = transitionTimer
                                     / Config.SubmergeTransitionSeconds;
                    Agent.PlaceholderView?.SetSubmergeProgress(progress);
                    if (progress >= 1f)
                    {
                        hidingPrefersWater = true;
                        Agent.BeginHiding();
                    }
                    break;
            }
        }

        public override void ExitFleeing()
        {
            fleePhase = FleePhase.None;
            Motor?.Stop();
            Agent.PlaceholderView?.RestoreVisibleAppearance();
        }

        public override void EnterHiding()
        {
            base.EnterHiding();
            dailyPhase = DailyPhase.None;
            fleePhase = FleePhase.None;
            hidingPhase = HidingPhase.Hidden;
            emergingFromWater = false;
            transitionTimer = 0f;
            activitySoundCountdown = 0f;
            Motor.Stop();
            Agent.PlaceholderView?.SetSubmergeProgress(1f);
        }

        public override void TickHiding(float deltaTime)
        {
            Motor.Stop();
            switch (hidingPhase)
            {
                case HidingPhase.Hidden:
                    if (!ShouldAttemptSafeEmergence(deltaTime)
                        || !TryFindSafeEmergencePoint(
                            out Vector2 emergencePosition,
                            out bool isWater))
                    {
                        return;
                    }

                    if (!Motor.TeleportToMapPosition(emergencePosition))
                        return;

                    emergingFromWater = isWater;
                    transitionTimer = 0f;
                    hidingPhase = HidingPhase.Surfacing;
                    if (emergingFromWater)
                        Agent.SoundEmitter?.Emit(AnimalSoundKind.Surfacing);
                    break;

                case HidingPhase.Surfacing:
                    if (!IsEmergencePositionSafe(Motor.CurrentMapPosition))
                    {
                        Agent.PlaceholderView?.SetSubmergeProgress(1f);
                        hidingPhase = HidingPhase.Hidden;
                        transitionTimer = 0f;
                        return;
                    }

                    transitionTimer += deltaTime;
                    float progress = transitionTimer
                                     / Config.SubmergeTransitionSeconds;
                    Agent.PlaceholderView?.SetSubmergeProgress(1f - progress);
                    if (progress >= 1f)
                        Agent.CompleteHiding();
                    break;
            }
        }

        public override void ExitHiding()
        {
            hidingPhase = HidingPhase.None;
            hidingPrefersWater = false;
            emergingFromWater = false;
            transitionTimer = 0f;
            Motor?.Stop();
            Agent.PlaceholderView?.RestoreVisibleAppearance();
        }

        private void BeginNextDailyBehaviour()
        {
            CleanupDailyVisualState();
            Motor.Stop();
            currentDailySettings = null;
            currentFoodSource = null;
            activitySoundCountdown = 0f;
            availableBehaviours.Clear();

            IReadOnlyList<AnimalDailyBehaviourSettings> configuredBehaviours =
                Config.DailyBehaviours;
            if (configuredBehaviours != null)
            {
                for (int index = 0;
                     index < configuredBehaviours.Count;
                     index++)
                {
                    AnimalDailyBehaviourSettings settings =
                        configuredBehaviours[index];
                    if (settings != null
                        && settings.SelectionWeight > 0f
                        && IsDailyBehaviourAvailable(settings.Behaviour))
                    {
                        availableBehaviours.Add(settings);
                    }
                }
            }

            while (availableBehaviours.Count > 0)
            {
                AnimalDailyBehaviourSettings selected =
                    ChooseWeightedDailyBehaviour();
                if (selected != null && TryBeginDailyBehaviour(selected))
                    return;

                availableBehaviours.Remove(selected);
            }

            dailyPhase = DailyPhase.FallbackIdle;
            actionTimer = 1f;
            BeginRepeatedSound(AnimalSoundKind.Idle);
        }

        private void FacePlayerWhileCurious()
        {
            if (Agent.Perception.TryGetPlayerMapPosition(
                    out Vector2 playerMapPosition))
            {
                Motor.FaceMapPosition(playerMapPosition);
            }
        }

        private bool IsDailyBehaviourAvailable(
            AnimalDailyBehaviourKind behaviour)
        {
            switch (behaviour)
            {
                case AnimalDailyBehaviourKind.EatAtNearbyPlant:
                    return TryChooseFoodSource(true, out _, out _);
                case AnimalDailyBehaviourKind.RoamAndLook:
                    return TryFindRandomActivityPoint(out _);
                case AnimalDailyBehaviourKind.TravelToPlantAndEat:
                    return TryChooseFoodSource(false, out _, out _);
                case AnimalDailyBehaviourKind.DiveAndResurface:
                    return TryFindRandomWaterPoint(
                        Agent.HomeMapPosition,
                        Config.ActivityRadiusMeters,
                        true,
                        out _);
                default:
                    return false;
            }
        }

        private AnimalDailyBehaviourSettings ChooseWeightedDailyBehaviour()
        {
            float totalWeight = 0f;
            for (int index = 0; index < availableBehaviours.Count; index++)
                totalWeight += availableBehaviours[index].SelectionWeight;
            if (totalWeight <= 0f)
                return null;

            float selection = Random.value * totalWeight;
            for (int index = 0; index < availableBehaviours.Count; index++)
            {
                AnimalDailyBehaviourSettings settings =
                    availableBehaviours[index];
                selection -= settings.SelectionWeight;
                if (selection <= 0f)
                    return settings;
            }

            return availableBehaviours[availableBehaviours.Count - 1];
        }

        private bool TryBeginDailyBehaviour(
            AnimalDailyBehaviourSettings settings)
        {
            currentDailySettings = settings;
            actionTimer = settings.ChooseDuration();
            travelTimer = 0f;

            switch (settings.Behaviour)
            {
                case AnimalDailyBehaviourKind.EatAtNearbyPlant:
                    if (!TryChooseFoodSource(
                            true,
                            out currentFoodSource,
                            out Vector2 nearbyFoodPosition))
                    {
                        return false;
                    }

                    Motor.FaceMapPosition(nearbyFoodPosition);
                    dailyPhase = DailyPhase.Eating;
                    BeginRepeatedSound(AnimalSoundKind.Eating);
                    return true;

                case AnimalDailyBehaviourKind.RoamAndLook:
                    if (!TryFindRandomActivityPoint(out Vector2 roamTarget)
                        || !Motor.SetTarget(
                            roamTarget,
                            Config.DailyMoveSpeedMetersPerSecond))
                    {
                        return false;
                    }

                    lookBaseDirection = roamTarget - Motor.CurrentMapPosition;
                    if (lookBaseDirection.sqrMagnitude <= 0.000001f)
                        lookBaseDirection = Motor.FacingMapDirection;
                    lookBaseDirection.Normalize();
                    dailyPhase = DailyPhase.TravellingToRoam;
                    return true;

                case AnimalDailyBehaviourKind.TravelToPlantAndEat:
                    if (!TryChooseFoodSource(
                            false,
                            out currentFoodSource,
                            out Vector2 foodPosition)
                        || !TryCalculateFoodApproachPoint(
                            currentFoodSource,
                            foodPosition,
                            out Vector2 approachPoint)
                        || !Motor.SetTarget(
                            approachPoint,
                            Config.DailyMoveSpeedMetersPerSecond))
                    {
                        return false;
                    }

                    dailyPhase = DailyPhase.TravellingToFood;
                    return true;

                case AnimalDailyBehaviourKind.DiveAndResurface:
                    if (!TryFindRandomWaterPoint(
                            Agent.HomeMapPosition,
                            Config.ActivityRadiusMeters,
                            true,
                            out Vector2 diveTarget)
                        || !Motor.SetTarget(
                            diveTarget,
                            Config.DailyMoveSpeedMetersPerSecond))
                    {
                        return false;
                    }

                    dailyPhase = DailyPhase.TravellingToDive;
                    return true;
                default:
                    return false;
            }
        }

        private void TickEating(float deltaTime)
        {
            Motor.Stop();
            Agent.SoundEmitter?.TickRepeated(
                AnimalSoundKind.Eating,
                ref activitySoundCountdown,
                deltaTime);
            if (currentFoodSource != null
                && currentFoodSource.TryGetMapPosition(
                    Agent.Map,
                    out Vector2 foodPosition))
            {
                Motor.FaceMapPosition(foodPosition);
            }

            actionTimer -= deltaTime;
            if (actionTimer <= 0f)
                BeginNextDailyBehaviour();
        }

        private void TickTravelToRoam(float deltaTime)
        {
            travelTimer += deltaTime;
            if (Motor.HasArrived)
            {
                Motor.Stop();
                dailyPhase = DailyPhase.LookingAround;
                lookCountdown = 0f;
                lookDirectionSign = Random.value < 0.5f ? -1 : 1;
                BeginRepeatedSound(AnimalSoundKind.Looking);
            }
            else if (travelTimer >= Config.MaximumTravelTimeSeconds)
            {
                BeginNextDailyBehaviour();
            }
        }

        private void TickLookingAround(float deltaTime)
        {
            Motor.Stop();
            Agent.SoundEmitter?.TickRepeated(
                AnimalSoundKind.Looking,
                ref activitySoundCountdown,
                deltaTime);
            actionTimer -= deltaTime;
            lookCountdown -= deltaTime;
            if (lookCountdown <= 0f)
            {
                lookDirectionSign *= -1;
                float angle = lookDirectionSign
                              * Random.Range(0.45f, 1f)
                              * Config.LookAngleDegrees;
                Motor.FaceMapDirection(Rotate(lookBaseDirection, angle));
                lookCountdown = Config.ChooseLookInterval();
            }

            if (actionTimer <= 0f)
                BeginNextDailyBehaviour();
        }

        private void TickTravelToFood(float deltaTime)
        {
            travelTimer += deltaTime;
            if (Motor.HasArrived)
            {
                Motor.Stop();
                dailyPhase = DailyPhase.Eating;
                BeginRepeatedSound(AnimalSoundKind.Eating);
            }
            else if (travelTimer >= Config.MaximumTravelTimeSeconds
                     || currentFoodSource == null)
            {
                BeginNextDailyBehaviour();
            }
        }

        private void TickTravelToDive(float deltaTime)
        {
            travelTimer += deltaTime;
            if (Motor.HasArrived)
            {
                Motor.Stop();
                Agent.SetPerceptionSuppressed(true);
                transitionTimer = 0f;
                dailyPhase = DailyPhase.Submerging;
                Agent.SoundEmitter?.Emit(AnimalSoundKind.Submerging);
            }
            else if (travelTimer >= Config.MaximumTravelTimeSeconds)
            {
                BeginNextDailyBehaviour();
            }
        }

        private void TickDailySubmerging(float deltaTime)
        {
            Motor.Stop();
            transitionTimer += deltaTime;
            float progress = transitionTimer
                             / Config.SubmergeTransitionSeconds;
            Agent.PlaceholderView?.SetSubmergeProgress(progress);
            if (progress < 1f)
                return;

            Vector2 submergedPosition = Motor.CurrentMapPosition;
            if (TryFindRandomWaterPoint(
                    submergedPosition,
                    Config.ResurfaceRadiusMeters,
                    true,
                    out Vector2 resurfacePosition))
            {
                Motor.TeleportToMapPosition(resurfacePosition);
            }

            Agent.PlaceholderView?.SetSubmergeProgress(1f);
            dailyPhase = DailyPhase.Underwater;
        }

        private void TickUnderwater(float deltaTime)
        {
            Motor.Stop();
            actionTimer -= deltaTime;
            if (actionTimer > 0f)
                return;

            transitionTimer = 0f;
            dailyPhase = DailyPhase.Surfacing;
            Agent.SoundEmitter?.Emit(AnimalSoundKind.Surfacing);
        }

        private void TickSurfacing(float deltaTime)
        {
            Motor.Stop();
            transitionTimer += deltaTime;
            float progress = transitionTimer
                             / Config.SubmergeTransitionSeconds;
            Agent.PlaceholderView?.SetSubmergeProgress(1f - progress);
            if (progress < 1f)
                return;

            Agent.SetPerceptionSuppressed(false);
            Agent.PlaceholderView?.RestoreVisibleAppearance();
            BeginNextDailyBehaviour();
        }

        private bool TryChooseFoodSource(
            bool requireNearby,
            out AnimalFoodSource selectedSource,
            out Vector2 selectedMapPosition)
        {
            selectedSource = null;
            selectedMapPosition = Vector2.zero;
            float totalWeight = 0f;
            Vector2 currentPosition = Motor.CurrentMapPosition;

            foreach (AnimalFoodSource source in AnimalFoodSource.ActiveSources)
            {
                if (source == null
                    || !source.isActiveAndEnabled
                    || !source.TryGetMapPosition(
                        Agent.Map,
                        out Vector2 sourceMapPosition)
                    || Vector2.Distance(
                        Agent.HomeMapPosition,
                        sourceMapPosition) > Config.ActivityRadiusMeters)
                {
                    continue;
                }

                if (requireNearby
                    && Vector2.Distance(currentPosition, sourceMapPosition)
                    > Config.NearbyFoodDistanceMeters)
                {
                    continue;
                }

                float weight = source.SelectionWeight
                               * Config.GetFoodSelectionWeight(source.FoodType);
                if (weight <= 0f)
                    continue;

                totalWeight += weight;
                if (selectedSource == null
                    || Random.value * totalWeight <= weight)
                {
                    selectedSource = source;
                    selectedMapPosition = sourceMapPosition;
                }
            }

            return selectedSource != null;
        }

        private bool TryCalculateFoodApproachPoint(
            AnimalFoodSource source,
            Vector2 foodPosition,
            out Vector2 approachPoint)
        {
            approachPoint = foodPosition;
            if (source == null)
                return false;

            HeightMapObstacleFootprint obstacle =
                source.GetComponentInChildren<HeightMapObstacleFootprint>();
            if (obstacle == null || !obstacle.BlocksTraversal)
                return Agent.Map.TrySampleMapPosition(approachPoint, out _);

            float approachDistance = obstacle.RadiusMeters
                                     + Config.BodyRadiusMeters
                                     + Config.FoodApproachPaddingMeters
                                     + source.EatingApproachPaddingMeters;
            Vector2 outward = Motor.CurrentMapPosition - foodPosition;
            if (outward.sqrMagnitude <= 0.000001f)
                outward = Random.insideUnitCircle.normalized;
            if (outward.sqrMagnitude <= 0.000001f)
                outward = Vector2.up;

            for (int index = 0; index < 12; index++)
            {
                Vector2 direction = Rotate(outward.normalized, index * 30f);
                Vector2 candidate = foodPosition + direction * approachDistance;
                if (Vector2.Distance(candidate, Agent.HomeMapPosition)
                    <= Config.ActivityRadiusMeters
                    && Motor.CanOccupyMapPosition(candidate))
                {
                    approachPoint = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindRandomActivityPoint(out Vector2 mapPosition)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Vector2 candidate = Agent.HomeMapPosition
                                    + Random.insideUnitCircle
                                    * Config.ActivityRadiusMeters;
                if (Agent.Map.TrySampleMapPosition(candidate, out _)
                    && Motor.CanOccupyMapPosition(candidate))
                {
                    mapPosition = candidate;
                    return true;
                }
            }

            mapPosition = Vector2.zero;
            return false;
        }

        private bool TryFindRandomWaterPoint(
            Vector2 centre,
            float radiusMeters,
            bool constrainToHome,
            out Vector2 mapPosition)
        {
            return TryFindRandomWaterPoint(
                centre,
                radiusMeters,
                constrainToHome,
                false,
                out mapPosition);
        }

        private bool TryFindRandomWaterPoint(
            Vector2 centre,
            float radiusMeters,
            bool constrainToHome,
            bool requireSafeEmergence,
            out Vector2 mapPosition)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector2 candidate = centre
                                    + Random.insideUnitCircle * radiusMeters;
                if (constrainToHome
                    && Vector2.Distance(candidate, Agent.HomeMapPosition)
                    > Config.ActivityRadiusMeters)
                {
                    continue;
                }

                if (Agent.Map.TrySampleStaticWaterMapPosition(
                        candidate,
                        out float depthMeters)
                    && depthMeters >= Config.MinimumDiveWaterDepthMeters
                    && Motor.CanOccupyMapPosition(candidate)
                    && (!requireSafeEmergence
                        || IsEmergencePositionSafe(candidate)))
                {
                    mapPosition = candidate;
                    return true;
                }
            }

            mapPosition = Vector2.zero;
            return false;
        }

        private bool TryFindSafeEmergencePoint(
            out Vector2 mapPosition,
            out bool isWater)
        {
            Vector2 currentPosition = Motor.CurrentMapPosition;
            if (Agent.Map.TrySampleStaticWaterMapPosition(
                    currentPosition,
                    out float currentDepthMeters)
                && currentDepthMeters >= Config.MinimumDiveWaterDepthMeters
                && Motor.CanOccupyMapPosition(currentPosition)
                && IsEmergencePositionSafe(currentPosition))
            {
                mapPosition = currentPosition;
                isWater = true;
                return true;
            }

            if (TryFindRandomWaterPoint(
                    currentPosition,
                    Config.ResurfaceRadiusMeters,
                    false,
                    true,
                    out mapPosition)
                || TryFindRandomWaterPoint(
                    Agent.HomeMapPosition,
                    Config.ActivityRadiusMeters,
                    true,
                    true,
                    out mapPosition))
            {
                isWater = true;
                return true;
            }

            if (hidingPrefersWater)
            {
                isWater = true;
                return false;
            }

            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector2 candidate = Agent.HomeMapPosition
                                    + Random.insideUnitCircle
                                    * Config.ActivityRadiusMeters;
                if (Agent.Map.TrySampleMapPosition(candidate, out _)
                    && Motor.CanOccupyMapPosition(candidate)
                    && IsEmergencePositionSafe(candidate))
                {
                    mapPosition = candidate;
                    isWater = false;
                    return true;
                }
            }

            mapPosition = Vector2.zero;
            isWater = false;
            return false;
        }

        private bool TryFindNearestWaterPoint(out Vector2 mapPosition)
        {
            Vector2 currentPosition = Motor.CurrentMapPosition;
            Vector2 mapSize = Agent.Map.MapSizeMeters;
            float spacing = Mathf.Max(0.5f, Config.WaterSearchSpacingMeters);
            float bestDistanceSquared = float.PositiveInfinity;
            Vector2 bestPosition = Vector2.zero;
            bool found = false;

            for (float y = 0f; y <= mapSize.y; y += spacing)
            {
                for (float x = 0f; x <= mapSize.x; x += spacing)
                {
                    Vector2 candidate = new Vector2(x, y);
                    float distanceSquared =
                        (candidate - currentPosition).sqrMagnitude;
                    if (distanceSquared >= bestDistanceSquared
                        || !Agent.Map.TrySampleStaticWaterMapPosition(
                            candidate,
                            out float depthMeters)
                        || depthMeters < Config.MinimumDiveWaterDepthMeters
                        || !Motor.CanOccupyMapPosition(candidate))
                    {
                        continue;
                    }

                    found = true;
                    bestDistanceSquared = distanceSquared;
                    bestPosition = candidate;
                }
            }

            if (!found)
            {
                mapPosition = Vector2.zero;
                return false;
            }

            float refinementStep = spacing * 0.25f;
            for (int y = -4; y <= 4; y++)
            {
                for (int x = -4; x <= 4; x++)
                {
                    Vector2 candidate = bestPosition
                                        + new Vector2(
                                            x * refinementStep,
                                            y * refinementStep);
                    float distanceSquared =
                        (candidate - currentPosition).sqrMagnitude;
                    if (distanceSquared >= bestDistanceSquared
                        || !Agent.Map.TrySampleStaticWaterMapPosition(
                            candidate,
                            out float depthMeters)
                        || depthMeters < Config.MinimumDiveWaterDepthMeters
                        || !Motor.CanOccupyMapPosition(candidate))
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    bestPosition = candidate;
                }
            }

            mapPosition = bestPosition;
            return true;
        }

        private bool IsStandingInWater()
        {
            return Agent.Map.TrySampleStaticWaterMapPosition(
                       Motor.CurrentMapPosition,
                       out float depthMeters)
                   && depthMeters >= Config.MinimumDiveWaterDepthMeters;
        }

        private void BeginFleeSubmerge()
        {
            Motor.Stop();
            transitionTimer = 0f;
            fleePhase = FleePhase.Submerging;
            Agent.SoundEmitter?.Emit(AnimalSoundKind.Submerging);
        }

        private void BeginFallbackFlee()
        {
            Vector2 currentPosition = Motor.CurrentMapPosition;
            Vector2 awayDirection = Motor.FacingMapDirection;
            if (Agent.Perception.TryGetPlayerMapPosition(
                    out Vector2 playerPosition))
            {
                awayDirection = currentPosition - playerPosition;
            }
            if (awayDirection.sqrMagnitude <= 0.000001f)
                awayDirection = Random.insideUnitCircle.normalized;
            if (awayDirection.sqrMagnitude <= 0.000001f)
                awayDirection = Vector2.up;

            awayDirection = Rotate(
                awayDirection.normalized,
                Random.Range(-60f, 60f));
            Vector2 mapSize = Agent.Map.MapSizeMeters;
            Vector2 farTarget = currentPosition
                                + awayDirection * mapSize.magnitude * 2f;
            farTarget.x = Mathf.Clamp(farTarget.x, 0f, mapSize.x);
            farTarget.y = Mathf.Clamp(farTarget.y, 0f, mapSize.y);
            Motor.SetTarget(farTarget, Config.FleeSpeedMetersPerSecond);
            fallbackFleeTimer = 0f;
            fleePhase = FleePhase.FallbackRunning;
        }

        private bool IsOutsideCameraMargin(float margin)
        {
            Camera camera = Camera.main;
            if (camera == null)
                return false;

            Vector3 viewport = camera.WorldToViewportPoint(transform.position);
            return viewport.z <= 0f
                   || viewport.x < -margin
                   || viewport.x > 1f + margin
                   || viewport.y < -margin
                   || viewport.y > 1f + margin;
        }

        private void CleanupDailyVisualState()
        {
            Agent.SetPerceptionSuppressed(false);
            Agent.PlaceholderView?.RestoreVisibleAppearance();
            activitySoundCountdown = 0f;
        }

        private void BeginRepeatedSound(AnimalSoundKind soundKind)
        {
            AnimalSoundEmitter emitter = Agent != null
                ? Agent.SoundEmitter
                : null;
            if (emitter == null)
            {
                activitySoundCountdown = 0f;
                return;
            }

            emitter.Emit(soundKind);
            activitySoundCountdown = emitter.ChooseRepeatInterval(soundKind);
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
    }
}
