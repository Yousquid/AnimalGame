using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Placeholder View")]
    public sealed class AnimalPlaceholderView : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer bodyFill;
        [SerializeField] private SpriteRenderer bodyOutline;
        [SerializeField] private SpriteRenderer directionIndicator;

        private Vector3 baseVisualScale = Vector3.one;
        private Color baseBodyFillColor = Color.black;
        private Color baseBodyOutlineColor = Color.white;
        private Color baseIndicatorColor = Color.white;

        private void Awake()
        {
            CacheBaseAppearance();
        }

        public void ConfigureEditorReferences(
            Transform root,
            SpriteRenderer fill,
            SpriteRenderer outline,
            SpriteRenderer indicator)
        {
            visualRoot = root;
            bodyFill = fill;
            bodyOutline = outline;
            directionIndicator = indicator;
            CacheBaseAppearance();
        }

        public void SetSubmergeProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            float visibility = 1f - progress;
            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.Lerp(
                    baseVisualScale,
                    baseVisualScale * 0.58f,
                    progress);
            }

            ApplyAlpha(bodyFill, baseBodyFillColor, visibility);
            ApplyAlpha(bodyOutline, baseBodyOutlineColor, visibility);
            ApplyAlpha(directionIndicator, baseIndicatorColor, visibility);
        }

        public void RestoreVisibleAppearance()
        {
            SetSubmergeProgress(0f);
        }

        private void CacheBaseAppearance()
        {
            if (visualRoot != null)
                baseVisualScale = visualRoot.localScale;
            if (bodyFill != null)
                baseBodyFillColor = bodyFill.color;
            if (bodyOutline != null)
                baseBodyOutlineColor = bodyOutline.color;
            if (directionIndicator != null)
                baseIndicatorColor = directionIndicator.color;
        }

        private static void ApplyAlpha(
            SpriteRenderer renderer,
            Color baseColour,
            float visibility)
        {
            if (renderer == null)
                return;

            Color colour = baseColour;
            colour.a *= visibility;
            renderer.color = colour;
            renderer.enabled = colour.a > 0.001f;
        }
    }
}
