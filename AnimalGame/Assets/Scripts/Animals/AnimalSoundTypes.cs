using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public enum AnimalSoundKind
    {
        Idle,
        Looking,
        Curious,
        Eating,
        MovingOnLand,
        MovingInWater,
        Fleeing,
        Submerging,
        Surfacing
    }

    [Serializable]
    public sealed class AnimalSoundWaveSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField, Min(0.05f)] private float maximumRadiusMeters = 0.8f;
        [SerializeField, Min(0.05f)] private float durationSeconds = 0.75f;
        [SerializeField, Range(1, 8)] private int ringCount = 5;
        [SerializeField, Range(0.05f, 0.8f)] private float innerRadiusRatio = 0.23f;
        [SerializeField, Range(0.002f, 0.08f)]
        private float lineWidthNormalized = 0.014f;
        [SerializeField, Range(0f, 1f)] private float opacity = 0.82f;
        [SerializeField] private Vector2 repeatIntervalSeconds =
            new Vector2(0.55f, 0.8f);
        [SerializeField, Range(0f, 0.35f)] private float radiusVariation = 0.08f;

        public bool Enabled => enabled;
        public float MaximumRadiusMeters => Mathf.Max(
            0.05f,
            maximumRadiusMeters);
        public float DurationSeconds => Mathf.Max(0.05f, durationSeconds);
        public int RingCount => Mathf.Clamp(ringCount, 1, 8);
        public float InnerRadiusRatio => Mathf.Clamp(
            innerRadiusRatio,
            0.05f,
            0.8f);
        public float LineWidthNormalized => Mathf.Clamp(
            lineWidthNormalized,
            0.002f,
            0.08f);
        public float Opacity => Mathf.Clamp01(opacity);

        public AnimalSoundWaveSettings()
        {
        }

        public AnimalSoundWaveSettings(
            float maximumRadiusMeters,
            float durationSeconds,
            int ringCount,
            float minimumRepeatIntervalSeconds,
            float maximumRepeatIntervalSeconds,
            float opacity = 0.82f)
        {
            this.maximumRadiusMeters = maximumRadiusMeters;
            this.durationSeconds = durationSeconds;
            this.ringCount = ringCount;
            this.opacity = opacity;
            repeatIntervalSeconds = new Vector2(
                minimumRepeatIntervalSeconds,
                maximumRepeatIntervalSeconds);
        }

        public float ChooseMaximumRadius()
        {
            float variation = Mathf.Clamp(radiusVariation, 0f, 0.35f);
            return Mathf.Max(
                0.05f,
                maximumRadiusMeters
                * UnityEngine.Random.Range(1f - variation, 1f + variation));
        }

        public float ChooseRepeatInterval()
        {
            float minimum = Mathf.Max(0.05f, repeatIntervalSeconds.x);
            float maximum = Mathf.Max(minimum, repeatIntervalSeconds.y);
            return UnityEngine.Random.Range(minimum, maximum);
        }

        public void ClampValues()
        {
            maximumRadiusMeters = Mathf.Max(0.05f, maximumRadiusMeters);
            durationSeconds = Mathf.Max(0.05f, durationSeconds);
            ringCount = Mathf.Clamp(ringCount, 1, 8);
            innerRadiusRatio = Mathf.Clamp(innerRadiusRatio, 0.05f, 0.8f);
            lineWidthNormalized = Mathf.Clamp(
                lineWidthNormalized,
                0.002f,
                0.08f);
            opacity = Mathf.Clamp01(opacity);
            repeatIntervalSeconds.x = Mathf.Max(
                0.05f,
                repeatIntervalSeconds.x);
            repeatIntervalSeconds.y = Mathf.Max(
                repeatIntervalSeconds.x,
                repeatIntervalSeconds.y);
            radiusVariation = Mathf.Clamp(radiusVariation, 0f, 0.35f);
        }
    }
}
