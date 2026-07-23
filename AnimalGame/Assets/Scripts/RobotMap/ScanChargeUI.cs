using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Drives the two scan activation markers that surround the main display.
    /// Hold E or the current gamepad's right shoulder button to charge. A
    /// completed charge plays the release state when the button is released.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScanChargeUI : MonoBehaviour
    {
        private enum ScanVisualState
        {
            Idle,
            Charging,
            Charged,
            Releasing
        }

        private static readonly int IdleStateHash =
            Animator.StringToHash("Base Layer.Scan_Idle");
        private static readonly int HoldStateHash =
            Animator.StringToHash("Base Layer.Scan_Hold");
        private static readonly int ReleaseStateHash =
            Animator.StringToHash("Base Layer.Scan_Release");

        [Header("References")]
        [Tooltip("Animator containing the Scan_Idle, Scan_Hold, and Scan_Release states.")]
        [SerializeField] private Animator animator;

        [Tooltip("Full-screen RectTransform containing only the two scan activation markers.")]
        [SerializeField] private RectTransform markerLayer;

        [Tooltip("CanvasGroup used to animate the visibility of both scan activation markers together.")]
        [SerializeField] private CanvasGroup markerCanvasGroup;

        [Header("Charge Input")]
        [Tooltip("Keyboard key held to charge a scan.")]
        [SerializeField] private KeyCode keyboardScanKey = KeyCode.E;

        [Tooltip("Legacy Input Manager fallback for RB/R1. JoystickButton5 is the right shoulder button on the supported Xbox and Sony layouts.")]
        [SerializeField] private KeyCode gamepadScanButton =
            KeyCode.JoystickButton5;

        [Tooltip("Seconds the scan button must remain held before the charge is complete. The Scan_Hold state is sampled from start to finish across exactly this duration.")]
        [SerializeField, Min(0.05f)] private float maximumChargeDuration = 1.5f;

        [Header("Marker Animation")]
        [Tooltip("Marker opacity while the scan system is idle.")]
        [SerializeField, Range(0f, 1f)] private float idleAlpha = 0.72f;

        [Tooltip("Marker opacity at maximum scan charge.")]
        [SerializeField, Range(0f, 1f)] private float fullyChargedAlpha = 1f;

        [Tooltip("Marker-layer scale at maximum charge. Values below one pull both activation keys inward toward the display centre.")]
        [SerializeField, Range(0.75f, 1.1f)] private float fullyChargedScale = 0.9f;

        [Tooltip("Small scale pulse used while idle so the activation keys remain alive without appearing active.")]
        [SerializeField, Range(0f, 0.03f)] private float idlePulseAmplitude = 0.0025f;

        [Tooltip("Idle marker pulse frequency in cycles per second.")]
        [SerializeField, Min(0f)] private float idlePulseFrequency = 0.55f;

        [Tooltip("Pulse amplitude used after the charge reaches maximum while the button is still held.")]
        [SerializeField, Range(0f, 0.04f)] private float chargedPulseAmplitude = 0.006f;

        [Tooltip("Charged marker pulse frequency in cycles per second.")]
        [SerializeField, Min(0f)] private float chargedPulseFrequency = 3.5f;

        [Tooltip("Duration of the Scan_Release animation before returning to Scan_Idle.")]
        [SerializeField, Min(0.05f)] private float releaseDuration = 0.42f;

        [Tooltip("Outward overshoot reached near the beginning of Scan_Release.")]
        [SerializeField, Range(1f, 1.2f)] private float releaseOvershootScale = 1.045f;

        [Tooltip("Lowest marker opacity reached during the release flash.")]
        [SerializeField, Range(0f, 1f)] private float releaseFlashAlpha = 0.48f;

        private ScanVisualState state;
        private Vector3 baseScale = Vector3.one;
        private float chargeElapsed;
        private float releaseElapsed;

        public float Charge01 => Mathf.Clamp01(
            chargeElapsed / Mathf.Max(0.05f, maximumChargeDuration));
        public bool IsFullyCharged => state == ScanVisualState.Charged;
        public bool IsCharging => state == ScanVisualState.Charging
                                  || state == ScanVisualState.Charged;

        private void Awake()
        {
            if (markerLayer == null)
                markerLayer = transform as RectTransform;
            if (markerCanvasGroup == null)
                markerCanvasGroup = GetComponent<CanvasGroup>();
            if (animator == null)
                animator = GetComponent<Animator>();

            if (markerLayer != null)
                baseScale = markerLayer.localScale;
        }

        private void OnEnable()
        {
            EnterIdle();
        }

        private void Update()
        {
            bool scanHeld = IsScanInputHeld();
            float deltaTime = Time.unscaledDeltaTime;

            switch (state)
            {
                case ScanVisualState.Idle:
                    UpdateIdleVisual();
                    if (scanHeld)
                        BeginCharge();
                    break;

                case ScanVisualState.Charging:
                    if (!scanHeld)
                    {
                        // Releasing before the maximum charge cancels the scan.
                        EnterIdle();
                        break;
                    }

                    chargeElapsed = Mathf.Min(
                        maximumChargeDuration,
                        chargeElapsed + deltaTime);
                    float charge01 = Charge01;
                    SampleAnimatorState(HoldStateHash, charge01);
                    ApplyChargingVisual(charge01);
                    if (charge01 >= 1f)
                        state = ScanVisualState.Charged;
                    break;

                case ScanVisualState.Charged:
                    ApplyChargedVisual();
                    if (!scanHeld)
                        BeginRelease();
                    break;

                case ScanVisualState.Releasing:
                    releaseElapsed += deltaTime;
                    float release01 = Mathf.Clamp01(
                        releaseElapsed / Mathf.Max(0.05f, releaseDuration));
                    SampleAnimatorState(ReleaseStateHash, release01);
                    ApplyReleaseVisual(release01);
                    if (release01 >= 1f)
                        EnterIdle();
                    break;
            }
        }

        private void BeginCharge()
        {
            state = ScanVisualState.Charging;
            chargeElapsed = 0f;
            releaseElapsed = 0f;
            SampleAnimatorState(HoldStateHash, 0f);
            ApplyChargingVisual(0f);
        }

        private void BeginRelease()
        {
            state = ScanVisualState.Releasing;
            releaseElapsed = 0f;
            SampleAnimatorState(ReleaseStateHash, 0f);
            ApplyReleaseVisual(0f);
        }

        private void EnterIdle()
        {
            state = ScanVisualState.Idle;
            chargeElapsed = 0f;
            releaseElapsed = 0f;
            if (animator != null)
            {
                animator.speed = 1f;
                animator.Play(IdleStateHash, 0, 0f);
            }

            SetMarkerVisual(1f, idleAlpha);
        }

        private void UpdateIdleVisual()
        {
            float pulse = Mathf.Sin(
                Time.unscaledTime * idlePulseFrequency * Mathf.PI * 2f);
            SetMarkerVisual(
                1f + pulse * idlePulseAmplitude,
                idleAlpha);
        }

        private void ApplyChargingVisual(float charge01)
        {
            float eased = charge01 * charge01 * (3f - 2f * charge01);
            SetMarkerVisual(
                Mathf.Lerp(1f, fullyChargedScale, eased),
                Mathf.Lerp(idleAlpha, fullyChargedAlpha, eased));
        }

        private void ApplyChargedVisual()
        {
            float pulse = Mathf.Sin(
                Time.unscaledTime * chargedPulseFrequency * Mathf.PI * 2f);
            SetMarkerVisual(
                fullyChargedScale + pulse * chargedPulseAmplitude,
                fullyChargedAlpha);
        }

        private void ApplyReleaseVisual(float release01)
        {
            float scale;
            float alpha;
            if (release01 < 0.35f)
            {
                float rise = Mathf.SmoothStep(0f, 1f, release01 / 0.35f);
                scale = Mathf.Lerp(
                    fullyChargedScale,
                    releaseOvershootScale,
                    rise);
                alpha = Mathf.Lerp(
                    fullyChargedAlpha,
                    releaseFlashAlpha,
                    rise);
            }
            else
            {
                float settle = Mathf.SmoothStep(
                    0f,
                    1f,
                    (release01 - 0.35f) / 0.65f);
                scale = Mathf.Lerp(releaseOvershootScale, 1f, settle);
                alpha = Mathf.Lerp(releaseFlashAlpha, idleAlpha, settle);
            }

            SetMarkerVisual(scale, alpha);
        }

        private void SetMarkerVisual(float uniformScale, float alpha)
        {
            if (markerLayer != null)
                markerLayer.localScale = baseScale * uniformScale;
            if (markerCanvasGroup != null)
                markerCanvasGroup.alpha = Mathf.Clamp01(alpha);
        }

        private void SampleAnimatorState(int stateHash, float normalizedTime)
        {
            if (animator == null)
                return;

            animator.speed = 0f;
            animator.Play(stateHash, 0, Mathf.Clamp01(normalizedTime));
        }

        private bool IsScanInputHeld()
        {
            if (Input.GetKey(keyboardScanKey)
                || Input.GetKey(gamepadScanButton))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad != null
                    && gamepad.added
                    && gamepad.rightShoulder.isPressed)
                {
                    return true;
                }
            }
#endif
            return false;
        }

        private void OnDisable()
        {
            if (animator != null)
                animator.speed = 1f;
            SetMarkerVisual(1f, idleAlpha);
        }

        private void OnValidate()
        {
            maximumChargeDuration = Mathf.Max(0.05f, maximumChargeDuration);
            idleAlpha = Mathf.Clamp01(idleAlpha);
            fullyChargedAlpha = Mathf.Clamp01(fullyChargedAlpha);
            fullyChargedScale = Mathf.Clamp(fullyChargedScale, 0.75f, 1.1f);
            idlePulseAmplitude = Mathf.Clamp(idlePulseAmplitude, 0f, 0.03f);
            idlePulseFrequency = Mathf.Max(0f, idlePulseFrequency);
            chargedPulseAmplitude = Mathf.Clamp(
                chargedPulseAmplitude,
                0f,
                0.04f);
            chargedPulseFrequency = Mathf.Max(0f, chargedPulseFrequency);
            releaseDuration = Mathf.Max(0.05f, releaseDuration);
            releaseOvershootScale = Mathf.Clamp(
                releaseOvershootScale,
                1f,
                1.2f);
            releaseFlashAlpha = Mathf.Clamp01(releaseFlashAlpha);
        }
    }
}
