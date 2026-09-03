using UnityEngine;

namespace AnimalGame.Animals
{
    public abstract class AnimalBehaviourSet : MonoBehaviour
    {
        private float hidingTimeRemaining;
        private float hidingSafetyCheckCountdown;

        protected AnimalAgent Agent { get; private set; }
        protected AnimalMotor Motor => Agent != null ? Agent.Motor : null;
        protected AnimalSpeciesConfig Config =>
            Agent != null ? Agent.Config : null;

        public virtual bool SupportsAggression => false;

        public virtual void Initialize(AnimalAgent agent)
        {
            Agent = agent;
        }

        public abstract void EnterDaily();
        public abstract void TickDaily(float deltaTime);
        public abstract void ExitDaily();

        public virtual void EnterCurious()
        {
        }

        public virtual void TickCurious(float deltaTime)
        {
        }

        public virtual void ExitCurious()
        {
        }

        public abstract void EnterFleeing();
        public abstract void TickFleeing(float deltaTime);
        public abstract void ExitFleeing();

        public virtual void EnterAggressive()
        {
        }

        public virtual void TickAggressive(float deltaTime)
        {
        }

        public virtual void ExitAggressive()
        {
        }

        public virtual void EnterHiding()
        {
            BeginHidingWait();
        }

        public virtual void TickHiding(float deltaTime)
        {
        }

        public virtual void ExitHiding()
        {
        }

        protected void BeginHidingWait()
        {
            hidingTimeRemaining = Config != null
                ? Config.ChooseFrightenedHideDuration()
                : 0f;
            hidingSafetyCheckCountdown = 0f;
        }

        protected bool ShouldAttemptSafeEmergence(float deltaTime)
        {
            hidingTimeRemaining -= Mathf.Max(0f, deltaTime);
            if (hidingTimeRemaining > 0f)
                return false;

            hidingSafetyCheckCountdown -= Mathf.Max(0f, deltaTime);
            if (hidingSafetyCheckCountdown > 0f)
                return false;

            hidingSafetyCheckCountdown = Config != null
                ? Config.HideSafetyCheckIntervalSeconds
                : 0.75f;
            return true;
        }

        protected bool IsEmergencePositionSafe(Vector2 mapPosition)
        {
            if (Agent == null || Config == null || Agent.Perception == null)
                return false;
            if (!Agent.Perception.TryGetPlayerMapPosition(
                    out Vector2 playerMapPosition))
            {
                return true;
            }

            float safeDistance = Config.ReappearSafeDistanceMeters;
            return (playerMapPosition - mapPosition).sqrMagnitude
                   >= safeDistance * safeDistance;
        }
    }
}
