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
        private enum UnknownPositionRefreshPhase
        {
            Holding,
            FadingOut,
            FadingIn
        }

        private static readonly int SeedProperty = Shader.PropertyToID("_Seed");
        private static readonly int RevealProperty =
            Shader.PropertyToID("_RevealProgress");
        private static readonly int AnimationPhaseProperty =
            Shader.PropertyToID("_AnimationPhase");
        private static readonly int ShapePhaseProperty =
            Shader.PropertyToID("_ShapePhase");

        [SerializeField] private DiscoverableEntity discoverable;
        [SerializeField] private SpriteRenderer[] knownRenderers =
            Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer unknownRenderer;
        [SerializeField, Min(0.01f)] private float revealDuration = 0.4f;

        [Header("Unknown Static Animation")]
        [Tooltip("Minimum and maximum rolling-speed multipliers. Each animal changes between these speeds independently.")]
        [SerializeField] private Vector2 rollSpeedRange =
            new Vector2(0.6f, 2.5f);
        [Tooltip("Minimum and maximum seconds before selecting another rolling speed.")]
        [SerializeField] private Vector2 rollSpeedChangeInterval =
            new Vector2(0.3f, 1.2f);
        [Tooltip("How quickly the rolling speed eases toward its newly selected value.")]
        [SerializeField, Min(0.1f)] private float rollSpeedResponse = 5.5f;
        [Tooltip("Minimum and maximum seconds used to morph between consecutive moving snow-density patterns.")]
        [InspectorName("Snow Field Pattern Morph Duration")]
        [SerializeField] private Vector2 polygonMorphDuration =
            new Vector2(0.4f, 0.9f);
        [Tooltip("Runtime multiplier applied to the authored two-times-body field. 1.0 is about twice the animal body; 1.5 is about three times.")]
        [SerializeField] private Vector2 recognitionFieldScaleRange =
            new Vector2(1f, 1.5f);
        [Tooltip("Minimum and maximum seconds held at a field size before another size transition begins.")]
        [SerializeField] private Vector2 fieldScaleHoldDuration =
            new Vector2(0.7f, 1.8f);
        [Tooltip("Minimum and maximum seconds used to move smoothly to the next field size.")]
        [SerializeField] private Vector2 fieldScaleTransitionDuration =
            new Vector2(0.4f, 0.9f);

        [Header("Delayed Position Sampling")]
        [Tooltip("Seconds between world-position samples for the undiscovered snow field. The animal itself continues moving every frame.")]
        [SerializeField, Min(0.01f)]
        private float unknownPositionUpdateInterval = 0.3f;
        [Tooltip("Total seconds used by one position-refresh animation. The first half fades out and the second half fades in.")]
        [SerializeField, Min(0.02f)]
        private float unknownPositionRefreshAnimationDuration = 0.2f;

        private MaterialPropertyBlock propertyBlock;

        private Color[] knownBaseColours = Array.Empty<Color>();
        private Color unknownBaseColour = Color.white;
        private float revealProgress;
        private float revealTarget;
        private float externalVisibility = 1f;
        private bool subscribed;
        private bool coloursCached;
        private bool animationInitialized;
        private uint randomState;
        private float animationPhase;
        private float currentRollSpeed;
        private float targetRollSpeed;
        private float speedChangeRemaining;
        private int polygonShapeIndex;
        private float polygonMorphElapsed;
        private float polygonMorphCurrentDuration;
        private Vector3 unknownBaseScale = Vector3.one;
        private bool unknownBaseScaleCached;
        private float currentFieldScaleMultiplier = 1f;
        private float fieldScaleStartMultiplier = 1f;
        private float fieldScaleTargetMultiplier = 1f;
        private float fieldScaleHoldRemaining;
        private float fieldScaleTransitionElapsed;
        private float fieldScaleCurrentTransitionDuration;
        private Transform unknownAuthoredParent;
        private Vector3 unknownAuthoredLocalPosition;
        private Vector3 sampledUnknownWorldPosition;
        private Vector3 pendingUnknownWorldPosition;
        private float unknownPositionSampleRemaining;
        private float unknownPositionRefreshElapsed;
        private float unknownPositionRefreshVisibility = 1f;
        private UnknownPositionRefreshPhase unknownPositionRefreshPhase;
        private bool unknownPositionAnchorCached;
        private bool unknownPositionSampleInitialized;

        private void Awake()
        {
            CacheBaseColours();
            CacheUnknownPositionAnchor();
            InitializeUnknownAnimation();
            ApplyImmediateState();
        }

        private void OnEnable()
        {
            Subscribe();
            if (!coloursCached)
                CacheBaseColours();
            if (!animationInitialized)
                InitializeUnknownAnimation();
            ApplyImmediateState();
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreUnknownAuthoredPosition();
            ResetUnknownPositionRefresh();
        }

        private void Update()
        {
            if (unknownRenderer != null
                && (unknownRenderer.enabled || revealTarget < 1f))
            {
                UpdateUnknownAnimation(Time.deltaTime);
            }

            float duration = Mathf.Max(0.01f, revealDuration);
            revealProgress = Mathf.MoveTowards(
                revealProgress,
                revealTarget,
                Time.deltaTime / duration);
            ApplyPresentation();
        }

        private void LateUpdate()
        {
            if (unknownRenderer == null)
                return;

            if (!unknownRenderer.enabled)
            {
                RestoreUnknownAuthoredPosition();
                ResetUnknownPositionRefresh();
                return;
            }

            UpdateUnknownDisplayedPosition(Time.deltaTime);
            ApplyPresentation();

            // The uncertainty field deliberately conceals both continuous
            // movement and the animal's exact facing direction.
            unknownRenderer.transform.rotation = Quaternion.identity;
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
            unknownBaseScaleCached = false;
            unknownPositionAnchorCached = false;
            ResetUnknownPositionRefresh();
            coloursCached = false;
            CacheBaseColours();
            CacheUnknownPositionAnchor();
        }
#endif

        private void OnDiscoveryChanged(bool discovered)
        {
            revealTarget = discovered ? 1f : 0f;
            if (!discovered)
                ResetUnknownPositionRefresh();
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
            unknownColour.a *= unknownVisibility
                               * unknownPositionRefreshVisibility;
            unknownRenderer.color = unknownColour;
            // Keep the renderer enabled while a position refresh is fully
            // transparent so its world position can change invisibly.
            unknownRenderer.enabled = unknownVisibility > 0.001f;

            if (!unknownRenderer.enabled)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();
            unknownRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(SeedProperty, GetInstanceID() * 0.0137f);
            propertyBlock.SetFloat(RevealProperty, revealProgress);
            propertyBlock.SetFloat(AnimationPhaseProperty, animationPhase);
            propertyBlock.SetFloat(
                ShapePhaseProperty,
                polygonShapeIndex + Mathf.SmoothStep(
                    0f,
                    1f,
                    polygonMorphElapsed
                    / Mathf.Max(0.01f, polygonMorphCurrentDuration)));
            unknownRenderer.SetPropertyBlock(propertyBlock);
        }

        private void InitializeUnknownAnimation()
        {
            randomState = unchecked(
                (uint)GetInstanceID() * 747796405u ^ 0xA511E9B3u);
            if (randomState == 0u)
                randomState = 0x6D2B79F5u;

            animationPhase = NextRandom01() * 32f;
            currentRollSpeed = RandomRange(rollSpeedRange);
            targetRollSpeed = RandomRange(rollSpeedRange);
            speedChangeRemaining = RandomRange(rollSpeedChangeInterval);
            polygonShapeIndex = Mathf.FloorToInt(NextRandom01() * 128f);
            polygonMorphCurrentDuration = RandomRange(polygonMorphDuration);
            polygonMorphElapsed = NextRandom01()
                                  * polygonMorphCurrentDuration;
            CacheUnknownBaseScale();
            currentFieldScaleMultiplier = RandomRange(
                recognitionFieldScaleRange);
            fieldScaleStartMultiplier = currentFieldScaleMultiplier;
            fieldScaleTargetMultiplier = currentFieldScaleMultiplier;
            fieldScaleHoldRemaining = RandomRange(fieldScaleHoldDuration);
            fieldScaleCurrentTransitionDuration = RandomRange(
                fieldScaleTransitionDuration);
            fieldScaleTransitionElapsed =
                fieldScaleCurrentTransitionDuration;
            ApplyUnknownFieldScale();
            animationInitialized = true;
        }

        private void UpdateUnknownAnimation(float deltaTime)
        {
            if (!animationInitialized)
                InitializeUnknownAnimation();

            deltaTime = Mathf.Max(0f, deltaTime);
            speedChangeRemaining -= deltaTime;
            if (speedChangeRemaining <= 0f)
            {
                targetRollSpeed = RandomRange(rollSpeedRange);
                speedChangeRemaining = RandomRange(
                    rollSpeedChangeInterval);
            }

            float speedBlend = 1f - Mathf.Exp(
                -Mathf.Max(0.1f, rollSpeedResponse) * deltaTime);
            currentRollSpeed = Mathf.Lerp(
                currentRollSpeed,
                targetRollSpeed,
                speedBlend);
            animationPhase = Mathf.Repeat(
                animationPhase + currentRollSpeed * deltaTime,
                4096f);

            polygonMorphElapsed += deltaTime;
            while (polygonMorphElapsed >= polygonMorphCurrentDuration)
            {
                polygonMorphElapsed -= polygonMorphCurrentDuration;
                polygonShapeIndex = (polygonShapeIndex + 1) % 2048;
                polygonMorphCurrentDuration = RandomRange(
                    polygonMorphDuration);
            }

            UpdateUnknownFieldScale(deltaTime);
        }

        private void UpdateUnknownFieldScale(float deltaTime)
        {
            if (fieldScaleTransitionElapsed
                < fieldScaleCurrentTransitionDuration)
            {
                fieldScaleTransitionElapsed = Mathf.Min(
                    fieldScaleCurrentTransitionDuration,
                    fieldScaleTransitionElapsed + deltaTime);
                float transition01 = Mathf.Clamp01(
                    fieldScaleTransitionElapsed
                    / Mathf.Max(
                        0.01f,
                        fieldScaleCurrentTransitionDuration));
                currentFieldScaleMultiplier = Mathf.Lerp(
                    fieldScaleStartMultiplier,
                    fieldScaleTargetMultiplier,
                    Mathf.SmoothStep(0f, 1f, transition01));
            }
            else
            {
                fieldScaleHoldRemaining -= deltaTime;
                if (fieldScaleHoldRemaining <= 0f)
                {
                    fieldScaleStartMultiplier =
                        currentFieldScaleMultiplier;
                    fieldScaleTargetMultiplier = RandomRange(
                        recognitionFieldScaleRange);
                    fieldScaleCurrentTransitionDuration = RandomRange(
                        fieldScaleTransitionDuration);
                    fieldScaleTransitionElapsed = 0f;
                    fieldScaleHoldRemaining = RandomRange(
                        fieldScaleHoldDuration);
                }
            }

            ApplyUnknownFieldScale();
        }

        private void CacheUnknownBaseScale()
        {
            if (unknownBaseScaleCached || unknownRenderer == null)
                return;

            unknownBaseScale = unknownRenderer.transform.localScale;
            unknownBaseScaleCached = true;
        }

        private void ApplyUnknownFieldScale()
        {
            if (unknownRenderer == null)
                return;

            CacheUnknownBaseScale();
            unknownRenderer.transform.localScale = unknownBaseScale
                                                   * Mathf.Clamp(
                                                       currentFieldScaleMultiplier,
                                                       recognitionFieldScaleRange.x,
                                                       recognitionFieldScaleRange.y);
        }

        private void CacheUnknownPositionAnchor()
        {
            if (unknownPositionAnchorCached || unknownRenderer == null)
                return;

            Transform fieldTransform = unknownRenderer.transform;
            unknownAuthoredParent = fieldTransform.parent;
            unknownAuthoredLocalPosition = fieldTransform.localPosition;
            unknownPositionAnchorCached = true;
        }

        private void UpdateUnknownDisplayedPosition(float deltaTime)
        {
            CacheUnknownPositionAnchor();
            if (!unknownPositionAnchorCached
                || unknownAuthoredParent == null
                || unknownRenderer.transform == transform)
            {
                return;
            }

            if (!unknownPositionSampleInitialized)
            {
                CaptureUnknownWorldPosition();
            }
            else if (revealTarget < 1f)
            {
                UpdateUnknownPositionRefresh(Mathf.Max(0f, deltaTime));
            }
            else
            {
                // The discovery reveal owns opacity once scanning succeeds.
                unknownPositionRefreshVisibility = 1f;
            }

            unknownRenderer.transform.position = sampledUnknownWorldPosition;
        }

        private void CaptureUnknownWorldPosition()
        {
            sampledUnknownWorldPosition = unknownAuthoredParent.TransformPoint(
                unknownAuthoredLocalPosition);
            pendingUnknownWorldPosition = sampledUnknownWorldPosition;
            BeginUnknownPositionHold();
            unknownPositionSampleInitialized = true;
        }

        private void UpdateUnknownPositionRefresh(float deltaTime)
        {
            float halfAnimationDuration = Mathf.Max(
                0.01f,
                unknownPositionRefreshAnimationDuration * 0.5f);

            switch (unknownPositionRefreshPhase)
            {
                case UnknownPositionRefreshPhase.Holding:
                    unknownPositionSampleRemaining -= deltaTime;
                    if (unknownPositionSampleRemaining <= 0f)
                    {
                        pendingUnknownWorldPosition =
                            unknownAuthoredParent.TransformPoint(
                                unknownAuthoredLocalPosition);
                        unknownPositionRefreshElapsed = 0f;
                        unknownPositionRefreshPhase =
                            UnknownPositionRefreshPhase.FadingOut;
                    }
                    break;

                case UnknownPositionRefreshPhase.FadingOut:
                    unknownPositionRefreshElapsed += deltaTime;
                    float fadeOut01 = Mathf.Clamp01(
                        unknownPositionRefreshElapsed
                        / halfAnimationDuration);
                    unknownPositionRefreshVisibility = 1f
                        - Mathf.SmoothStep(0f, 1f, fadeOut01);
                    if (fadeOut01 >= 1f)
                    {
                        unknownPositionRefreshVisibility = 0f;
                        sampledUnknownWorldPosition =
                            pendingUnknownWorldPosition;
                        unknownPositionRefreshElapsed = 0f;
                        unknownPositionRefreshPhase =
                            UnknownPositionRefreshPhase.FadingIn;
                    }
                    break;

                case UnknownPositionRefreshPhase.FadingIn:
                    unknownPositionRefreshElapsed += deltaTime;
                    float fadeIn01 = Mathf.Clamp01(
                        unknownPositionRefreshElapsed
                        / halfAnimationDuration);
                    unknownPositionRefreshVisibility = Mathf.SmoothStep(
                        0f,
                        1f,
                        fadeIn01);
                    if (fadeIn01 >= 1f)
                        BeginUnknownPositionHold();
                    break;
            }
        }

        private void BeginUnknownPositionHold()
        {
            unknownPositionRefreshVisibility = 1f;
            unknownPositionRefreshElapsed = 0f;
            unknownPositionRefreshPhase =
                UnknownPositionRefreshPhase.Holding;
            unknownPositionSampleRemaining = Mathf.Max(
                0.01f,
                unknownPositionUpdateInterval
                - unknownPositionRefreshAnimationDuration);
        }

        private void ResetUnknownPositionRefresh()
        {
            unknownPositionSampleInitialized = false;
            unknownPositionRefreshVisibility = 1f;
            unknownPositionRefreshElapsed = 0f;
            unknownPositionRefreshPhase =
                UnknownPositionRefreshPhase.Holding;
        }

        private void RestoreUnknownAuthoredPosition()
        {
            if (!unknownPositionAnchorCached || unknownRenderer == null)
                return;

            Transform fieldTransform = unknownRenderer.transform;
            if (fieldTransform.parent == unknownAuthoredParent)
                fieldTransform.localPosition = unknownAuthoredLocalPosition;
        }

        private float RandomRange(Vector2 range)
        {
            float minimum = Mathf.Min(range.x, range.y);
            float maximum = Mathf.Max(range.x, range.y);
            return Mathf.Lerp(minimum, maximum, NextRandom01());
        }

        private float NextRandom01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00FFFFFFu) / 16777216f;
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

        private void OnValidate()
        {
            revealDuration = Mathf.Max(0.01f, revealDuration);
            rollSpeedRange = ClampPositiveRange(rollSpeedRange, 0.05f);
            rollSpeedChangeInterval = ClampPositiveRange(
                rollSpeedChangeInterval,
                0.05f);
            rollSpeedResponse = Mathf.Max(0.1f, rollSpeedResponse);
            polygonMorphDuration = ClampPositiveRange(
                polygonMorphDuration,
                0.05f);
            recognitionFieldScaleRange = ClampPositiveRange(
                recognitionFieldScaleRange,
                0.1f);
            fieldScaleHoldDuration = ClampPositiveRange(
                fieldScaleHoldDuration,
                0.05f);
            fieldScaleTransitionDuration = ClampPositiveRange(
                fieldScaleTransitionDuration,
                0.05f);
            unknownPositionUpdateInterval = Mathf.Max(
                0.01f,
                unknownPositionUpdateInterval);
            unknownPositionRefreshAnimationDuration = Mathf.Max(
                0.02f,
                unknownPositionRefreshAnimationDuration);
        }

        private static Vector2 ClampPositiveRange(
            Vector2 range,
            float minimum)
        {
            float lower = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
            float upper = Mathf.Max(lower, Mathf.Max(range.x, range.y));
            return new Vector2(lower, upper);
        }
    }
}
