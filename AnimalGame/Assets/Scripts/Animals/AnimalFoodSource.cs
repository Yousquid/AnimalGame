using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Food Source")]
    public sealed class AnimalFoodSource : MonoBehaviour
    {
        private static readonly HashSet<AnimalFoodSource> activeSources =
            new HashSet<AnimalFoodSource>();

        [SerializeField] private AnimalFoodType foodType;
        [Tooltip("Multiplies the animal species' preference weight. Use this to make one specific plant more or less attractive.")]
        [SerializeField, Min(0f)] private float selectionWeight = 1f;
        [Tooltip("Additional distance kept between an eating animal and this plant's solid footprint.")]
        [SerializeField, Min(0f)] private float eatingApproachPaddingMeters = 0.05f;

        public static IEnumerable<AnimalFoodSource> ActiveSources =>
            activeSources;
        public AnimalFoodType FoodType => foodType;
        public float SelectionWeight => Mathf.Max(0f, selectionWeight);
        public float EatingApproachPaddingMeters =>
            Mathf.Max(0f, eatingApproachPaddingMeters);

        private void OnEnable()
        {
            if (gameObject.scene.IsValid())
                activeSources.Add(this);
        }

        private void OnDisable()
        {
            activeSources.Remove(this);
        }

        private void OnDestroy()
        {
            activeSources.Remove(this);
        }

        public bool TryGetMapPosition(
            MapTestSceneController map,
            out Vector2 mapPositionMeters)
        {
            mapPositionMeters = Vector2.zero;
            if (map == null || !map.HasGeneratedMap)
                return false;

            return map.TrySampleWorldPosition(
                transform.position,
                out mapPositionMeters,
                out _);
        }

        public void ConfigureEditorDefaults(
            AnimalFoodType type,
            float weight,
            float approachPaddingMeters)
        {
            foodType = type;
            selectionWeight = Mathf.Max(0f, weight);
            eatingApproachPaddingMeters = Mathf.Max(
                0f,
                approachPaddingMeters);
        }

        private void OnValidate()
        {
            selectionWeight = Mathf.Max(0f, selectionWeight);
            eatingApproachPaddingMeters = Mathf.Max(
                0f,
                eatingApproachPaddingMeters);
        }
    }
}
