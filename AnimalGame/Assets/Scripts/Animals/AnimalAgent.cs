using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeightMapPlacedObject))]
    [RequireComponent(typeof(AnimalMotor))]
    [RequireComponent(typeof(AnimalPerception))]
    [AddComponentMenu("Animal Game/Animals/Animal Agent")]
    public sealed class AnimalAgent : MonoBehaviour
    {
        [SerializeField] private AnimalSpeciesConfig config;
        [SerializeField] private AnimalBehaviourSet behaviourSet;
        [SerializeField] private AnimalPlaceholderView placeholderView;

        public AnimalSpeciesConfig Config => config;
        public AnimalMotor Motor { get; private set; }
        public AnimalPerception Perception { get; private set; }
        public AnimalPlaceholderView PlaceholderView => placeholderView;
        public MapTestSceneController Map { get; private set; }
        public Vector2 HomeMapPosition { get; private set; }
        public AnimalState CurrentState { get; private set; } = AnimalState.Daily;
        public bool PerceptionSuppressed { get; private set; }

        private bool initialized;
        private float reactionCountdown;
        private float outsideAlertTimer;

        private void Start()
        {
            TryInitialize();
        }

        private void Update()
        {
            if (!initialized && !TryInitialize())
                return;

            float deltaTime = Time.deltaTime;
            if (CurrentState == AnimalState.Daily
                && !PerceptionSuppressed
                && Perception.TickDetection(deltaTime))
            {
                EnterCurious();
            }

            switch (CurrentState)
            {
                case AnimalState.Daily:
                    behaviourSet.TickDaily(deltaTime);
                    break;
                case AnimalState.Curious:
                    TickCurious(deltaTime);
                    break;
                case AnimalState.Fleeing:
                    behaviourSet.TickFleeing(deltaTime);
                    break;
                case AnimalState.Aggressive:
                    behaviourSet.TickAggressive(deltaTime);
                    break;
            }

            if (CurrentState != AnimalState.Despawned)
                Motor.Tick(deltaTime);
        }

        public void ConfigureEditorDefaults(
            AnimalSpeciesConfig speciesConfig,
            AnimalBehaviourSet speciesBehaviour,
            AnimalPlaceholderView view)
        {
            config = speciesConfig;
            behaviourSet = speciesBehaviour;
            placeholderView = view;
        }

        public void SetPerceptionSuppressed(bool suppressed)
        {
            PerceptionSuppressed = suppressed;
        }

        public void ReturnToDaily()
        {
            if (!initialized || CurrentState == AnimalState.Despawned)
                return;

            ExitCurrentState();
            CurrentState = AnimalState.Daily;
            PerceptionSuppressed = false;
            outsideAlertTimer = 0f;
            behaviourSet.EnterDaily();
        }

        public void Despawn()
        {
            if (CurrentState == AnimalState.Despawned)
                return;

            ExitCurrentState();
            CurrentState = AnimalState.Despawned;
            Motor.Stop();
            gameObject.SetActive(false);
        }

        private bool TryInitialize()
        {
            if (initialized)
                return true;
            if (config == null)
            {
                Debug.LogError("Animal Agent has no species configuration.", this);
                enabled = false;
                return false;
            }

            HeightMapPlacedObject placedObject =
                GetComponent<HeightMapPlacedObject>();
            Map = placedObject != null ? placedObject.Map : null;
            if (Map == null)
                Map = FindObjectOfType<MapTestSceneController>();
            if (Map == null || !Map.HasGeneratedMap
                || !Map.TrySampleWorldPosition(
                    transform.position,
                    out Vector2 homePosition,
                    out _))
            {
                return false;
            }

            Motor = GetComponent<AnimalMotor>();
            Perception = GetComponent<AnimalPerception>();
            if (behaviourSet == null)
                behaviourSet = GetComponent<AnimalBehaviourSet>();
            if (placeholderView == null)
                placeholderView = GetComponent<AnimalPlaceholderView>();
            if (Motor == null || Perception == null || behaviourSet == null)
            {
                Debug.LogError(
                    "Animal Agent is missing its motor, perception, or behaviour set.",
                    this);
                enabled = false;
                return false;
            }

            HomeMapPosition = homePosition;
            Motor.Initialize(Map, config);
            Perception.Initialize(Map, config);
            behaviourSet.Initialize(this);
            placeholderView?.RestoreVisibleAppearance();
            initialized = true;
            CurrentState = AnimalState.Daily;
            behaviourSet.EnterDaily();
            return true;
        }

        private void EnterCurious()
        {
            if (CurrentState != AnimalState.Daily)
                return;

            behaviourSet.ExitDaily();
            CurrentState = AnimalState.Curious;
            PerceptionSuppressed = false;
            outsideAlertTimer = 0f;
            reactionCountdown = config.ReactionIntervalSeconds;
            Motor.Stop();
            behaviourSet.EnterCurious();
        }

        private void TickCurious(float deltaTime)
        {
            Motor.Stop();
            behaviourSet.TickCurious(deltaTime);

            if (!Perception.TryGetPlayerProximity(
                    out float proximity,
                    out _))
            {
                outsideAlertTimer += deltaTime;
                if (outsideAlertTimer >= config.CuriousLostPlayerDelaySeconds)
                    ReturnToDaily();
                return;
            }

            outsideAlertTimer = 0f;
            reactionCountdown -= deltaTime;
            if (reactionCountdown > 0f)
                return;

            reactionCountdown += config.ReactionIntervalSeconds;
            float fleeChance = Mathf.Clamp01(
                config.BaseFleeChancePerCheck
                * Mathf.Lerp(1f, config.NearestFleeMultiplier, proximity));
            float aggressionChance = behaviourSet.SupportsAggression
                ? Mathf.Clamp01(
                    config.BaseAggressionChancePerCheck
                    * Mathf.Lerp(
                        1f,
                        config.NearestAggressionMultiplier,
                        proximity))
                : 0f;
            float roll = Random.value;
            if (roll < aggressionChance)
            {
                EnterAggressive();
            }
            else if (roll < aggressionChance + fleeChance)
            {
                EnterFleeing();
            }
        }

        private void EnterFleeing()
        {
            behaviourSet.ExitCurious();
            CurrentState = AnimalState.Fleeing;
            PerceptionSuppressed = true;
            Motor.Stop();
            behaviourSet.EnterFleeing();
        }

        private void EnterAggressive()
        {
            behaviourSet.ExitCurious();
            CurrentState = AnimalState.Aggressive;
            PerceptionSuppressed = true;
            Motor.Stop();
            behaviourSet.EnterAggressive();
        }

        private void ExitCurrentState()
        {
            switch (CurrentState)
            {
                case AnimalState.Daily:
                    behaviourSet?.ExitDaily();
                    break;
                case AnimalState.Curious:
                    behaviourSet?.ExitCurious();
                    break;
                case AnimalState.Fleeing:
                    behaviourSet?.ExitFleeing();
                    break;
                case AnimalState.Aggressive:
                    behaviourSet?.ExitAggressive();
                    break;
            }
        }

    }
}
