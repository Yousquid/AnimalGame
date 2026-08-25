using System;
using AnimalGame.Discovery;
using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>
    /// Presents an undiscovered animal as an animated static field, then
    /// reveals the authored animal renderers once discovery state changes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Discovery Visual")]
    public sealed class AnimalDiscoveryVisual : MonoBehaviour
    {
        private static readonly int SeedProperty = Shader.PropertyToID("_Seed");
        private static readonly int RevealProperty =
            Shader.PropertyToID("_RevealProgress");

        [SerializeField] private DiscoverableEntity discoverable;
        [SerializeField] private SpriteRenderer[] knownRenderers =
            Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer unknownRenderer;
        [SerializeField, Min(0.01f)] private float revealDuration = 0.4f;

        private MaterialPropertyBlock propertyBlock;

        private Color[] knownBaseColours = Array.Empty<Color>();
        private Color unknownBaseColour = Color.white;
        private float revealProgress;
        private float revealTarget;
        private float externalVisibility = 1f;
        private bool subscribed;
        private bool coloursCached;

        private void Awake()
        {
            CacheBaseColours();
            ApplyImmediateState();
        }

        private void OnEnable()
        {
            Subscribe();
            if (!coloursCached)
                CacheBaseColours();
            ApplyImmediateState();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            float duration = Mathf.Max(0.01f, revealDuration);
            revealProgress = Mathf.MoveTowards(
                revealProgress,
                revealTarget,
                Time.deltaTime / duration);
            ApplyPresentation();
        }

        private void LateUpdate()
        {
            if (unknownRenderer != null && unknownRenderer.enabled)
            {
                // The uncertainty field tracks position, but intentionally
                // conceals the animal's exact facing direction.
                unknownRenderer.transform.rotation = Quaternion.identity;
            }
        }

        public void SetExternalVisibility(float visibility)
        {
            externalVisibility = Mathf.Clamp01(visibility);
            ApplyPresentation();
        }

#if UNITY_EDITOR
        public void ConfigureEditorReferences(
            DiscoverableEntity state,
            SpriteRenderer[] authoredRenderers,
            SpriteRenderer staticRenderer,
            float transitionDuration)
        {
            discoverable = state;
            knownRenderers = authoredRenderers ?? Array.Empty<SpriteRenderer>();
            unknownRenderer = staticRenderer;
            revealDuration = Mathf.Max(0.01f, transitionDuration);
            coloursCached = false;
            CacheBaseColours();
        }
#endif

        private void OnDiscoveryChanged(bool discovered)
        {
            revealTarget = discovered ? 1f : 0f;
        }

        private void ApplyImmediateState()
        {
            bool discovered = discoverable != null && discoverable.IsDiscovered;
            revealProgress = discovered ? 1f : 0f;
            revealTarget = revealProgress;
            ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            float knownVisibility = Mathf.SmoothStep(
                0f,
                1f,
                revealProgress) * externalVisibility;
            for (int i = 0; i < knownRenderers.Length; i++)
            {
                SpriteRenderer renderer = knownRenderers[i];
                if (renderer == null)
                    continue;

                Color colour = i < knownBaseColours.Length
                    ? knownBaseColours[i]
                    : renderer.color;
                colour.a *= knownVisibility;
                renderer.color = colour;
                renderer.enabled = colour.a > 0.001f;
            }

            if (unknownRenderer == null)
                return;

            float unknownVisibility = (1f - Mathf.SmoothStep(
                0f,
                1f,
                revealProgress)) * externalVisibility;
            Color unknownColour = unknownBaseColour;
            unknownColour.a *= unknownVisibility;
            unknownRenderer.color = unknownColour;
            unknownRenderer.enabled = unknownColour.a > 0.001f;

            if (!unknownRenderer.enabled)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();
            unknownRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(SeedProperty, GetInstanceID() * 0.0137f);
            propertyBlock.SetFloat(RevealProperty, revealProgress);
            unknownRenderer.SetPropertyBlock(propertyBlock);
        }

        private void CacheBaseColours()
        {
            if (knownRenderers == null)
                knownRenderers = Array.Empty<SpriteRenderer>();

            if (knownBaseColours.Length != knownRenderers.Length)
                knownBaseColours = new Color[knownRenderers.Length];

            for (int i = 0; i < knownRenderers.Length; i++)
            {
                SpriteRenderer renderer = knownRenderers[i];
                if (renderer != null)
                    knownBaseColours[i] = renderer.color;
            }

            if (unknownRenderer != null)
                unknownBaseColour = unknownRenderer.color;
            coloursCached = true;
        }

        private void Subscribe()
        {
            if (subscribed || discoverable == null)
                return;

            discoverable.DiscoveryChanged += OnDiscoveryChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || discoverable == null)
                return;

            discoverable.DiscoveryChanged -= OnDiscoveryChanged;
            subscribed = false;
        }
    }
}
