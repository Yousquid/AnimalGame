using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Pileated Woodpecker Behaviour")]
    public sealed class PileatedWoodpeckerBehaviour : AnimalBehaviourSet
    {
        private enum DailyPhase
        {
            None,
            Perched,
            Flying,
            Pecking,
            FallbackIdle
        }

        private enum ArrivalAction
        {
            CompleteBehaviour,
            Perch,
            Peck
        }

        private enum FleePhase
        {
            None,
            ReturningHome,
            EnteringHome
        }

        [Header("Home Tree")]
        [Tooltip("Scene tree that owns this woodpecker's fixed activity centre and escape destination. A missing reference falls back to the nearest tree at runtime.")]
        [SerializeField] private TreeHabitat birthTree;

        [Header("Tree Selection")]
        [Tooltip("Relative chance of selecting a healthy tree before choosing an individual tree in that category.")]
        [SerializeField, Min(0f)] private float healthyTreeWeight = 0.4f;
        [Tooltip("Relative chance of selecting a dead tree before choosing an individual tree in that category.")]
        [SerializeField, Min(0f)] private float deadTreeWeight = 0.6f;
        [SerializeField, Min(0f)] private float perchClearanceMeters = 0.04f;

        [Header("Pecking Motion")]
        [SerializeField] private Transform visualRoot;
        [SerializeField, Min(0f)] private float peckAmplitudeMeters = 0.08f;
        [SerializeField, Min(0.1f)] private float peckFrequencyHz = 5.5f;

        [Header("Escape Into Home Tree")]
        [SerializeField, Min(0.05f)] private float enterTreeDurationSeconds = 0.45f;

        private readonly List<AnimalDailyBehaviourSettings>
            availableBehaviours = new List<AnimalDailyBehaviourSettings>();
        private readonly List<TreeHabitat> healthyCandidates =
            new List<TreeHabitat>();
        private readonly List<TreeHabitat> deadCandidates =
            new List<TreeHabitat>();

        private AnimalDailyBehaviourSettings currentDailySettings;
        private TreeHabitat currentTree;
        private TreeHabitat targetTree;
        private TreeHabitat interruptedTargetTree;
        private ArrivalAction arrivalAction;
        private ArrivalAction interruptedArrivalAction;
        private DailyPhase dailyPhase;
        private FleePhase fleePhase;
        private float actionTimer;
        private float travelTimer;
        private float peckElapsed;
        private float enterTreeElapsed;
        private Vector2 enterTreeStartMapPosition;
        private Vector2 enterTreeTargetMapPosition;
        private Vector3 baseVisualLocalPosition;
        private bool visualPositionCached;
        private bool missingBirthTreeWarningReported;

        public override bool SupportsAggression => false;
        public TreeHabitat BirthTree => birthTree;
        public TreeHabitat CurrentTree => currentTree;
        public TreeHabitat TargetTree => targetTree;

        public override void Initialize(AnimalAgent agent)
        {
            base.Initialize(agent);
            CacheVisualPosition();
            ResolveBirthTree();
            if (birthTree != null && TrySnapToTree(birthTree))
                currentTree = birthTree;
        }

        public override void EnterDaily()
        {
            RestoreDailyVisualState();
            fleePhase = FleePhase.None;

            if (IsUsableTree(interruptedTargetTree))
            {
                TreeHabitat resumeTree = interruptedTargetTree;
                ArrivalAction resumeAction = interruptedArrivalAction;
                interruptedTargetTree = null;
                if (TryBeginFlight(resumeTree, resumeAction))
                    return;
            }

            interruptedTargetTree = null;
            if (!IsUsableTree(currentTree))
            {
                ResolveBirthTree();
                if (IsUsableTree(birthTree)
                    && TryBeginFlight(birthTree, ArrivalAction.Perch))
                {
                    return;
                }

                TreeHabitat nearest = FindNearestTree();
                if (nearest != null
                    && TryBeginFlight(nearest, ArrivalAction.Perch))
                {
                    return;
                }
            }

            BeginNextDailyBehaviour();
        }

        public override void TickDaily(float deltaTime)
        {
            switch (dailyPhase)
            {
                case DailyPhase.Perched:
                    TickPerched(deltaTime);
                    break;
                case DailyPhase.Flying:
                    TickDailyFlight(deltaTime);
                    break;
                case DailyPhase.Pecking:
                    TickPecking(deltaTime);
                    break;
                case DailyPhase.FallbackIdle:
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
            if (dailyPhase == DailyPhase.Flying && IsUsableTree(targetTree))
            {
                interruptedTargetTree = targetTree;
                interruptedArrivalAction = arrivalAction;
            }

            dailyPhase = DailyPhase.None;
            currentDailySettings = null;
            targetTree = null;
            Motor?.Stop();
            ResetVisualOffset();
        }

        public override void EnterCurious()
        {
            Motor.Stop();
            ResetVisualOffset();
            FacePlayer();
        }

        public override void TickCurious(float deltaTime)
        {
            Motor.Stop();
            FacePlayer();
        }

        public override void ExitCurious()
        {
            Motor.Stop();
        }

        public override void EnterFleeing()
        {
            dailyPhase = DailyPhase.None;
            targetTree = null;
            interruptedTargetTree = null;
            ResetVisualOffset();
            Agent.PlaceholderView?.RestoreVisibleAppearance();
            ResolveBirthTree();

            if (IsUsableTree(birthTree)
                && TryGetPerchPosition(
                    birthTree,
                    Motor.CurrentMapPosition,
                    out Vector2 homePerch)
                && Motor.SetAerialTarget(
                    homePerch,
                    Config.FleeSpeedMetersPerSecond))
            {
                targetTree = birthTree;
                fleePhase = FleePhase.ReturningHome;
                travelTimer = 0f;
                return;
            }

            targetTree = null;
            fleePhase = FleePhase.ReturningHome;
            travelTimer = 0f;
            Motor.SetAerialTarget(
                Agent.HomeMapPosition,
                Config.FleeSpeedMetersPerSecond);
        }

        public override void TickFleeing(float deltaTime)
        {
            switch (fleePhase)
            {
                case FleePhase.ReturningHome:
                    travelTimer += deltaTime;
                    if (Motor.HasArrived
                        || travelTimer >= Config.MaximumTravelTimeSeconds)
                    {
                        BeginEnteringHome();
                    }
                    break;
                case FleePhase.EnteringHome:
                    TickEnteringHome(deltaTime);
                    break;
            }
        }

        public override void ExitFleeing()
        {
            fleePhase = FleePhase.None;
            targetTree = null;
            Motor?.Stop();
            ResetVisualOffset();
        }

        public bool BindBirthTree(TreeHabitat tree, bool snapToPerch)
        {
            birthTree = tree;
            missingBirthTreeWarningReported = false;
            if (!snapToPerch || tree == null)
                return tree != null;

            bool snapped = TrySnapToTree(tree);
            if (snapped)
                currentTree = tree;
            return snapped;
        }

        public bool BindNearestTree(bool snapToPerch)
        {
            TreeHabitat nearest = FindNearestTree();
            return nearest != null && BindBirthTree(nearest, snapToPerch);
        }

        public bool SnapToBirthPerch()
        {
            if (!IsUsableTree(birthTree))
                return false;

            bool snapped = TrySnapToTree(birthTree);
            if (snapped)
                currentTree = birthTree;
            return snapped;
        }

        private void BeginNextDailyBehaviour()
        {
            RestoreDailyVisualState();
            Motor.Stop();
            currentDailySettings = null;
            targetTree = null;
            availableBehaviours.Clear();

            IReadOnlyList<AnimalDailyBehaviourSettings> settingsList =
                Config.DailyBehaviours;
            if (settingsList != null)
            {
                for (int index = 0; index < settingsList.Count; index++)
                {
                    AnimalDailyBehaviourSettings settings = settingsList[index];
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
            if (IsUsableTree(currentTree))
                FaceTree(currentTree);
        }

        private bool IsDailyBehaviourAvailable(
            AnimalDailyBehaviourKind behaviour)
        {
            switch (behaviour)
            {
                case AnimalDailyBehaviourKind.PerchAtTree:
                    return IsUsableTree(currentTree)
                           || HasAnyCandidateTree(false);
                case AnimalDailyBehaviourKind.FlyToTree:
                    return HasAnyCandidateTree(false);
                case AnimalDailyBehaviourKind.PeckAtTree:
                    return (IsUsableTree(currentTree) && currentTree.IsDead)
                           || HasAnyCandidateTree(true);
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
                case AnimalDailyBehaviourKind.PerchAtTree:
                    if (IsUsableTree(currentTree))
                    {
                        dailyPhase = DailyPhase.Perched;
                        FaceTree(currentTree);
                        return true;
                    }

                    TreeHabitat perchTree = ChooseTree(false, true);
                    return perchTree != null
                           && TryBeginFlight(perchTree, ArrivalAction.Perch);

                case AnimalDailyBehaviourKind.FlyToTree:
                    TreeHabitat flightTree = ChooseTree(false, false);
                    return flightTree != null
                           && TryBeginFlight(
                               flightTree,
                               ArrivalAction.CompleteBehaviour);

                case AnimalDailyBehaviourKind.PeckAtTree:
                    if (IsUsableTree(currentTree) && currentTree.IsDead)
                    {
                        BeginPecking();
                        return true;
                    }

                    TreeHabitat peckTree = ChooseTree(true, false);
                    return peckTree != null
                           && TryBeginFlight(peckTree, ArrivalAction.Peck);
                default:
                    return false;
            }
        }

        private bool TryBeginFlight(
            TreeHabitat tree,
            ArrivalAction actionAfterArrival)
        {
            if (!TryGetPerchPosition(
                    tree,
                    Motor.CurrentMapPosition,
                    out Vector2 perchPosition)
                || !Motor.SetAerialTarget(
                    perchPosition,
                    Config.DailyMoveSpeedMetersPerSecond))
            {
                return false;
            }

            targetTree = tree;
            currentTree = null;
            arrivalAction = actionAfterArrival;
            dailyPhase = DailyPhase.Flying;
            travelTimer = 0f;
            return true;
        }

        private void TickPerched(float deltaTime)
        {
            Motor.Stop();
            if (IsUsableTree(currentTree))
                FaceTree(currentTree);

            actionTimer -= deltaTime;
            if (actionTimer <= 0f)
                BeginNextDailyBehaviour();
        }

        private void TickDailyFlight(float deltaTime)
        {
            travelTimer += deltaTime;
            if (!Motor.HasArrived)
            {
                if (travelTimer >= Config.MaximumTravelTimeSeconds)
                {
                    Motor.Stop();
                    currentTree = null;
                    targetTree = null;
                    BeginNextDailyBehaviour();
                }

                return;
            }

            if (!IsUsableTree(targetTree))
            {
                currentTree = null;
                BeginNextDailyBehaviour();
                return;
            }

            currentTree = targetTree;
            targetTree = null;
            Motor.Stop();
            FaceTree(currentTree);
            switch (arrivalAction)
            {
                case ArrivalAction.Perch:
                    dailyPhase = DailyPhase.Perched;
                    break;
                case ArrivalAction.Peck:
                    BeginPecking();
                    break;
                default:
                    BeginNextDailyBehaviour();
                    break;
            }
        }

        private void BeginPecking()
        {
            dailyPhase = DailyPhase.Pecking;
            peckElapsed = 0f;
            Motor.Stop();
            FaceTree(currentTree);
        }

        private void TickPecking(float deltaTime)
        {
            Motor.Stop();
            if (!IsUsableTree(currentTree) || !currentTree.IsDead)
            {
                BeginNextDailyBehaviour();
                return;
            }

            FaceTree(currentTree);
            peckElapsed += deltaTime;
            actionTimer -= deltaTime;
            ApplyPeckOffset();
            if (actionTimer <= 0f)
                BeginNextDailyBehaviour();
        }

        private void ApplyPeckOffset()
        {
            if (visualRoot == null || Agent == null || Agent.Map == null)
                return;

            CacheVisualPosition();
            Vector2 direction = Motor.FacingMapDirection;
            if (direction.sqrMagnitude <= 0.000001f)
                direction = Vector2.up;
            Vector2 worldDirection = Agent.Map.MapDirectionToWorldDirection(
                direction.normalized);
            float worldAmplitude = Agent.Map.MapMetersToWorldDistance(
                worldDirection,
                peckAmplitudeMeters);
            float wave = Mathf.Sin(
                peckElapsed * peckFrequencyHz * Mathf.PI * 2f);
            visualRoot.localPosition = baseVisualLocalPosition
                                       + Vector3.up * worldAmplitude * wave;
        }

        private void FacePlayer()
        {
            if (Agent.Perception.TryGetPlayerMapPosition(
                    out Vector2 playerMapPosition))
            {
                Motor.FaceMapPosition(playerMapPosition);
            }
        }

        private void FaceTree(TreeHabitat tree)
        {
            if (tree != null
                && tree.TryGetMapPosition(
                    Agent.Map,
                    out Vector2 treeMapPosition))
            {
                Motor.FaceMapPosition(treeMapPosition);
            }
        }

        private void BeginEnteringHome()
        {
            Motor.Stop();
            enterTreeElapsed = 0f;
            enterTreeStartMapPosition = Motor.CurrentMapPosition;
            enterTreeTargetMapPosition = enterTreeStartMapPosition;
            if (IsUsableTree(targetTree))
            {
                targetTree.TryGetMapPosition(
                    Agent.Map,
                    out enterTreeTargetMapPosition);
                FaceTree(targetTree);
            }

            fleePhase = FleePhase.EnteringHome;
        }

        private void TickEnteringHome(float deltaTime)
        {
            enterTreeElapsed += deltaTime;
            float progress = Mathf.Clamp01(
                enterTreeElapsed
                / Mathf.Max(0.05f, enterTreeDurationSeconds));
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            Motor.TeleportToMapPosition(Vector2.Lerp(
                enterTreeStartMapPosition,
                enterTreeTargetMapPosition,
                easedProgress));
            Agent.PlaceholderView?.SetSubmergeProgress(easedProgress);
            if (progress >= 1f)
                Agent.Despawn();
        }

        private bool HasAnyCandidateTree(bool deadOnly)
        {
            GatherTreeCandidates(true);
            return deadOnly
                ? deadCandidates.Count > 0
                : deadCandidates.Count + healthyCandidates.Count > 0;
        }

        private TreeHabitat ChooseTree(bool deadOnly, bool allowCurrent)
        {
            GatherTreeCandidates(allowCurrent);
            if (deadOnly)
                return ChooseRandom(deadCandidates);

            float healthyWeight = healthyCandidates.Count > 0
                ? Mathf.Max(0f, healthyTreeWeight)
                : 0f;
            float deadWeight = deadCandidates.Count > 0
                ? Mathf.Max(0f, deadTreeWeight)
                : 0f;
            float totalWeight = healthyWeight + deadWeight;
            if (totalWeight <= 0f)
            {
                if (deadCandidates.Count > 0)
                    return ChooseRandom(deadCandidates);
                return ChooseRandom(healthyCandidates);
            }

            return Random.value * totalWeight < deadWeight
                ? ChooseRandom(deadCandidates)
                : ChooseRandom(healthyCandidates);
        }

        private void GatherTreeCandidates(bool allowCurrent)
        {
            healthyCandidates.Clear();
            deadCandidates.Clear();
            MapTestSceneController map = ResolveMap();
            if (map == null)
                return;

            Vector2 activityCentre = Agent != null
                ? Agent.HomeMapPosition
                : Vector2.zero;
            if (IsUsableTree(birthTree))
                birthTree.TryGetMapPosition(map, out activityCentre);
            float radiusSquared = Config != null
                ? Config.ActivityRadiusMeters * Config.ActivityRadiusMeters
                : float.PositiveInfinity;

            foreach (TreeHabitat tree in TreeHabitat.ActiveTrees)
            {
                if (!IsUsableTree(tree)
                    || !allowCurrent && tree == currentTree
                    || !tree.TryGetMapPosition(map, out Vector2 treePosition)
                    || (treePosition - activityCentre).sqrMagnitude
                    > radiusSquared)
                {
                    continue;
                }

                if (tree.IsDead)
                    deadCandidates.Add(tree);
                else
                    healthyCandidates.Add(tree);
            }

            // A single-tree habitat still needs a valid daily target.
            if (!allowCurrent
                && healthyCandidates.Count + deadCandidates.Count == 0
                && IsUsableTree(currentTree)
                && currentTree.TryGetMapPosition(map, out Vector2 currentPosition)
                && (currentPosition - activityCentre).sqrMagnitude
                <= radiusSquared)
            {
                if (currentTree.IsDead)
                    deadCandidates.Add(currentTree);
                else
                    healthyCandidates.Add(currentTree);
            }
        }

        private TreeHabitat FindNearestTree()
        {
            MapTestSceneController map = ResolveMap();
            if (map == null)
                return null;

            Vector2 origin;
            if (!map.TrySampleWorldPosition(
                    transform.position,
                    out origin,
                    out _))
            {
                origin = Agent != null ? Agent.HomeMapPosition : Vector2.zero;
            }

            TreeHabitat nearest = null;
            float nearestDistanceSquared = float.PositiveInfinity;
            foreach (TreeHabitat tree in TreeHabitat.ActiveTrees)
            {
                if (!IsUsableTree(tree)
                    || !tree.TryGetMapPosition(map, out Vector2 treePosition))
                {
                    continue;
                }

                float distanceSquared = (treePosition - origin).sqrMagnitude;
                if (distanceSquared >= nearestDistanceSquared)
                    continue;

                nearest = tree;
                nearestDistanceSquared = distanceSquared;
            }

            return nearest;
        }

        private void ResolveBirthTree()
        {
            if (IsUsableTree(birthTree))
                return;

            birthTree = FindNearestTree();
            if (birthTree != null || missingBirthTreeWarningReported)
                return;

            missingBirthTreeWarningReported = true;
            Debug.LogWarning(
                "Pileated woodpecker has no birth tree and no active tree fallback.",
                this);
        }

        private bool TrySnapToTree(TreeHabitat tree)
        {
            MapTestSceneController map = ResolveMap();
            if (tree == null || map == null)
                return false;

            Vector2 fromPosition;
            if (!map.TrySampleWorldPosition(
                    transform.position,
                    out fromPosition,
                    out _)
                && !tree.TryGetMapPosition(map, out fromPosition))
            {
                return false;
            }

            if (!TryGetPerchPosition(
                    tree,
                    fromPosition,
                    out Vector2 perchPosition))
            {
                return false;
            }

            bool moved;
            if (Motor != null && Agent != null)
            {
                moved = Motor.TeleportToMapPosition(perchPosition);
                if (moved)
                    FaceTree(tree);
            }
            else
            {
                Vector3 worldPosition = map.MapPositionToWorld(perchPosition);
                worldPosition.z = transform.position.z;
                transform.position = worldPosition;
                HeightMapPlacedObject placed =
                    GetComponent<HeightMapPlacedObject>();
                placed?.CaptureCurrentTransform();
                moved = true;
            }

            return moved;
        }

        private bool TryGetPerchPosition(
            TreeHabitat tree,
            Vector2 approachFrom,
            out Vector2 perchPosition)
        {
            perchPosition = Vector2.zero;
            MapTestSceneController map = ResolveMap();
            return tree != null
                   && map != null
                   && tree.TryGetPerchMapPosition(
                       map,
                       approachFrom,
                       (Config != null ? Config.BodyRadiusMeters : 0f)
                       + perchClearanceMeters,
                       out perchPosition);
        }

        private MapTestSceneController ResolveMap()
        {
            if (Agent != null && Agent.Map != null)
                return Agent.Map;

            HeightMapPlacedObject placed = GetComponent<HeightMapPlacedObject>();
            if (placed != null && placed.Map != null)
                return placed.Map;

            return FindObjectOfType<MapTestSceneController>();
        }

        private static TreeHabitat ChooseRandom(List<TreeHabitat> candidates)
        {
            return candidates != null && candidates.Count > 0
                ? candidates[Random.Range(0, candidates.Count)]
                : null;
        }

        private static bool IsUsableTree(TreeHabitat tree)
        {
            return tree != null
                   && tree.isActiveAndEnabled
                   && tree.gameObject.activeInHierarchy;
        }

        private void RestoreDailyVisualState()
        {
            ResetVisualOffset();
            Agent?.SetPerceptionSuppressed(false);
            Agent?.PlaceholderView?.RestoreVisibleAppearance();
        }

        private void CacheVisualPosition()
        {
            if (visualRoot == null || visualPositionCached)
                return;

            baseVisualLocalPosition = visualRoot.localPosition;
            visualPositionCached = true;
        }

        private void ResetVisualOffset()
        {
            CacheVisualPosition();
            if (visualRoot != null)
                visualRoot.localPosition = baseVisualLocalPosition;
        }

#if UNITY_EDITOR
        public void ConfigureEditorDefaults(
            TreeHabitat tree,
            Transform authoredVisualRoot)
        {
            birthTree = tree;
            visualRoot = authoredVisualRoot;
            visualPositionCached = false;
            CacheVisualPosition();
        }
#endif

        private void OnValidate()
        {
            healthyTreeWeight = Mathf.Max(0f, healthyTreeWeight);
            deadTreeWeight = Mathf.Max(0f, deadTreeWeight);
            perchClearanceMeters = Mathf.Max(0f, perchClearanceMeters);
            peckAmplitudeMeters = Mathf.Max(0f, peckAmplitudeMeters);
            peckFrequencyHz = Mathf.Max(0.1f, peckFrequencyHz);
            enterTreeDurationSeconds = Mathf.Max(
                0.05f,
                enterTreeDurationSeconds);
            visualPositionCached = false;
            CacheVisualPosition();
        }
    }
}
