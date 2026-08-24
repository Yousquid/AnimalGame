using UnityEngine;

namespace AnimalGame.Animals
{
    public abstract class AnimalBehaviourSet : MonoBehaviour
    {
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
    }
}
