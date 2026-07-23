using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Drives the authored Scan_Idle, Scan_Hold, and Scan_Release sprite clips.
    /// Hold E or RB/R1 to charge. Releasing early cancels the charge; releasing
    /// after a full charge plays the complete release clip.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class ScanChargeUI : MonoBehaviour
    {
        private enum ScanVisualState
        {
            Idle,
            Charging,
            Charged,
            Releasing
        }

        private const string IdleClipName = "Scan_Idle";
        private const string HoldClipName = "Scan_Hold";
        private const string ReleaseClipName = "Scan_Release";

        private static readonly int IdleStateHash =
            Animator.StringToHash("Base Layer.Scan_Idle");
        private static readonly int HoldStateHash =
            Animator.StringToHash("Base Layer.Scan_Hold");
        private static readonly int ReleaseStateHash =
            Animator.StringToHash("Base Layer.Scan_Release");

        [Header("References")]
        [Tooltip("Animator containing the authored Scan_Idle, Scan_Hold, and Scan_Release sprite clips.")]
        [SerializeField] private Animator animator;

        [Header("Charge Input")]
        [Tooltip("Keyboard key held to charge a scan.")]
        [SerializeField] private KeyCode keyboardScanKey = KeyCode.E;

        [Tooltip("Legacy Input Manager fallback for RB/R1. JoystickButton5 is the right shoulder button on the supported Xbox and Sony layouts.")]
        [SerializeField] private KeyCode gamepadScanButton =
            KeyCode.JoystickButton5;

        [Tooltip("Seconds required to play from the first through the last frame of Scan_Hold. The final frame remains visible while the button stays held.")]
        [SerializeField, Min(0.05f)] private float maximumChargeDuration = 1.5f;

        private ScanVisualState state;
        private float chargeElapsed;
        private float releaseElapsed;
        private float releaseClipDuration = 0.42f;

        public float Charge01 => Mathf.Clamp01(
            chargeElapsed / Mathf.Max(0.05f, maximumChargeDuration));
        public bool IsFullyCharged => state == ScanVisualState.Charged;
        public bool IsCharging => state == ScanVisualState.Charging
                                  || state == ScanVisualState.Charged;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            CacheAuthoredClipDurations();
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
                    if (scanHeld)
                        BeginCharge();
                    break;

                case ScanVisualState.Charging:
                    if (!scanHeld)
                    {
                        // Releasing before maximum charge cancels the scan.
                        EnterIdle();
                        break;
                    }

                    chargeElapsed = Mathf.Min(
                        maximumChargeDuration,
                        chargeElapsed + deltaTime);
                    float charge01 = Charge01;
                    SampleAuthoredState(HoldStateHash, charge01);
                    if (charge01 >= 1f)
                        state = ScanVisualState.Charged;
                    break;

                case ScanVisualState.Charged:
                    // Keep the final authored Hold sprite visible until release.
                    if (!scanHeld)
                        BeginRelease();
                    break;

                case ScanVisualState.Releasing:
                    releaseElapsed = Mathf.Min(
                        releaseClipDuration,
                        releaseElapsed + deltaTime);
                    float release01 = Mathf.Clamp01(
                        releaseElapsed / releaseClipDuration);
                    SampleAuthoredState(ReleaseStateHash, release01);
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
            SampleAuthoredState(HoldStateHash, 0f);
        }

        private void BeginRelease()
        {
            state = ScanVisualState.Releasing;
            releaseElapsed = 0f;
            SampleAuthoredState(ReleaseStateHash, 0f);
        }

        private void EnterIdle()
        {
            state = ScanVisualState.Idle;
            chargeElapsed = 0f;
            releaseElapsed = 0f;
            SampleAuthoredState(IdleStateHash, 0f);
        }

        private void SampleAuthoredState(int stateHash, float normalizedTime)
        {
            if (animator == null)
                return;

            // The script owns state timing so the configurable charge duration
            // maps exactly onto the authored frames without procedural visuals.
            animator.speed = 0f;
            animator.Play(stateHash, 0, Mathf.Clamp01(normalizedTime));
            animator.Update(0f);
        }

        private void CacheAuthoredClipDurations()
        {
            RuntimeAnimatorController controller =
                animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null)
                return;

            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip != null
                    && clip.name == ReleaseClipName
                    && clip.length > 0f)
                {
                    releaseClipDuration = clip.length;
                    break;
                }
            }

            Debug.Assert(HasClip(controller, IdleClipName),
                "Scan UI controller is missing Scan_Idle.", this);
            Debug.Assert(HasClip(controller, HoldClipName),
                "Scan UI controller is missing Scan_Hold.", this);
            Debug.Assert(HasClip(controller, ReleaseClipName),
                "Scan UI controller is missing Scan_Release.", this);
        }

        private static bool HasClip(
            RuntimeAnimatorController controller,
            string clipName)
        {
            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip != null && clip.name == clipName)
                    return true;
            }

            return false;
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
                animator.speed = 0f;
        }

        private void OnValidate()
        {
            maximumChargeDuration = Mathf.Max(0.05f, maximumChargeDuration);
        }
    }
}
