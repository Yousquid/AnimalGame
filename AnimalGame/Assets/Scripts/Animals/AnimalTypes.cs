using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public enum AnimalState
    {
        Daily,
        Curious,
        Fleeing,
        Aggressive,
        Hiding,
        Despawned
    }

    public enum AnimalDailyBehaviourKind
    {
        EatAtNearbyPlant,
        RoamAndLook,
        TravelToPlantAndEat,
        DiveAndResurface,
        PerchAtTree,
        FlyToTree,
        PeckAtTree
    }

    public enum AnimalFoodType
    {
        Bush,
        Lotus,
        Australis
    }

    [Serializable]
    public sealed class AnimalDailyBehaviourSettings
    {
        [SerializeField] private AnimalDailyBehaviourKind behaviour;
        [SerializeField, Min(0f)] private float selectionWeight = 1f;
        [SerializeField] private Vector2 durationSeconds = new Vector2(3f, 6f);

        public AnimalDailyBehaviourKind Behaviour => behaviour;
        public float SelectionWeight => Mathf.Max(0f, selectionWeight);
        public Vector2 DurationSeconds => durationSeconds;

        public AnimalDailyBehaviourSettings(
            AnimalDailyBehaviourKind behaviour,
            float selectionWeight,
            float minimumDurationSeconds,
            float maximumDurationSeconds)
        {
            this.behaviour = behaviour;
            this.selectionWeight = Mathf.Max(0f, selectionWeight);
            durationSeconds = new Vector2(
                Mathf.Max(0.05f, minimumDurationSeconds),
                Mathf.Max(minimumDurationSeconds, maximumDurationSeconds));
        }

        public float ChooseDuration()
        {
            float minimum = Mathf.Max(0.05f, durationSeconds.x);
            float maximum = Mathf.Max(minimum, durationSeconds.y);
            return UnityEngine.Random.Range(minimum, maximum);
        }

        public void ClampValues()
        {
            selectionWeight = Mathf.Max(0f, selectionWeight);
            durationSeconds.x = Mathf.Max(0.05f, durationSeconds.x);
            durationSeconds.y = Mathf.Max(
                durationSeconds.x,
                durationSeconds.y);
        }
    }

    [Serializable]
    public sealed class AnimalFoodPreference
    {
        [SerializeField] private AnimalFoodType foodType;
        [SerializeField, Min(0f)] private float selectionWeight = 1f;

        public AnimalFoodType FoodType => foodType;
        public float SelectionWeight => Mathf.Max(0f, selectionWeight);

        public AnimalFoodPreference(
            AnimalFoodType foodType,
            float selectionWeight)
        {
            this.foodType = foodType;
            this.selectionWeight = Mathf.Max(0f, selectionWeight);
        }

        public void ClampValues()
        {
            selectionWeight = Mathf.Max(0f, selectionWeight);
        }
    }
}
