using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    [RequireComponent(typeof(RobotMarkerView))]
    public sealed class RobotArmController : MonoBehaviour
    {
        private const float SourceCanvasCenterPixels = 63.5f;
        private const float ArmTwoBaseImageY = 96f;
        private const float ArmTwoOuterImageY = 60f;

        [Header("Input")]
        [Tooltip("Keyboard alternative to holding the left-stick button (L3). WASD becomes the arm target while this key is held.")]
        [SerializeField] private KeyCode keyboardArmKey = KeyCode.CapsLock;

        [Tooltip("Radial dead zone applied to the left-stick target while the arm button is held.")]
        [SerializeField, Range(0f, 0.9f)] private float leftStickDeadZone = 0.15f;

        [Header("Artwork")]
        [SerializeField] private Sprite robotArmOneSprite;
        [SerializeField] private Sprite robotArmTwoSprite;
        [SerializeField] private Sprite robotHandSprite;
        [SerializeField] private Color armColor = Color.white;

        [Tooltip("Uniform scale of the three source sprites. This determines the shortest complete arm length without stretching Arm 1 or the hand.")]
        [SerializeField, Min(0.01f)] private float artworkScale = 0.7f;

        [Tooltip("Horizontal thickness multiplier for Arm 1, Arm 2 and the hand. This does not change arm length or reach.")]
        [SerializeField, Min(0.1f)] private float artworkThicknessScale = 2.57f;

        [Header("Reach")]
        [Tooltip("Distance of each arm socket from the robot centre, relative to the visible robot-body radius.")]
        [SerializeField, Range(0f, 1.2f)] private float socketRadiusOfBody = 0.88f;

        [Tooltip("Maximum hand-centre reach from the robot centre, relative to the visible balance-ring radius.")]
        [SerializeField, Range(0.1f, 1.5f)] private float maximumReachOfBalanceRadius = 0.75f;

        [Tooltip("Additional fine adjustment applied to the computed maximum extension in marker-local units.")]
        [SerializeField] private float maximumExtensionOffset;

        [Tooltip("Maximum telescoping travel of the existing outer Arm 2, relative to the visible robot-body diameter. This caps the practical cargo-pulling stroke independently of the balance ring.")]
        [SerializeField, Range(0.05f, 1.5f)]
        private float maximumExtensionOfBodyDiameter = 0.5f;

        [Header("Body Connector")]
        [Tooltip("Length of the new Robot_Arm_2 segment linking each body socket to the existing outer arm, relative to the visible robot-body diameter.")]
        [SerializeField, Range(0.05f, 1.5f)]
        private float connectorLengthOfBodyDiameter = 0.2f;

        [Tooltip("Mirrored angular offset from the outward body-surface normal. Zero makes each connector emerge perpendicularly from the circular body.")]
        [SerializeField, Range(-180f, 180f)]
        private float connectorDirectionDegrees;

        [Tooltip("Resting angle between the connector and the existing outer arm. The two sides mirror this value.")]
        [SerializeField, Range(15f, 165f)]
        private float perpendicularAngleDegrees = 90f;

        [Tooltip("Horizontal thickness multiplier used only by the new Robot_Arm_2 connector segment.")]
        [SerializeField, Min(0.1f)] private float connectorThicknessScale = 2.57f;

        [Tooltip("Time used by the body connector to extend from inside the robot.")]
        [SerializeField, Min(0.01f)] private float connectorExtendDuration = 0.24f;

        [Tooltip("Time used by the body connector to retract into the robot.")]
        [SerializeField, Min(0.01f)] private float connectorRetractDuration = 0.2f;

        [Tooltip("Connector extension progress at which the existing outer arm begins growing perpendicularly from its end.")]
        [SerializeField, Range(0f, 1f)]
        private float outerArmExtendStartNormalized = 0.55f;

        [Tooltip("During retraction, the connector starts folding away once the outer arm has contracted to this progress.")]
        [SerializeField, Range(0f, 1f)]
        private float connectorRetractStartOuterNormalized = 0.4f;

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float extendDuration = 0.3f;
        [SerializeField, Min(0.01f)] private float retractDuration = 0.25f;

        [Tooltip("Small delay before the second arm begins extending or retracting.")]
        [SerializeField, Range(0f, 0.2f)] private float secondArmDelay = 0.03f;

        [Tooltip("Time used by the complete rigid arm to rotate toward the current stick target.")]
        [SerializeField, Min(0.001f)] private float aimSmoothingTime = 0.2f;

        [Tooltip("Time used by visible arms to slide between the four body sockets when the rollover direction becomes their reference.")]
        [SerializeField, Min(0.001f)]
        private float socketPositionSmoothingTime = 0.12f;

        [Tooltip("Remapped stick magnitude at which arm aiming becomes active. This is applied after the radial dead zone.")]
        [SerializeField, Range(0f, 1f)] private float aimEnterMagnitude = 0.12f;

        [Tooltip("Lower remapped magnitude at which aiming returns to its default radial pose. Keeping this below Aim Enter Magnitude prevents centre jitter.")]
        [SerializeField, Range(0f, 1f)] private float aimExitMagnitude = 0.06f;

        [Tooltip("Remapped stick magnitude at which both rigid arms fully converge on the requested target point.")]
        [SerializeField, Range(0.01f, 1f)] private float fullAimMagnitude = 0.7f;

        [Tooltip("Shapes the transition from the default radial pose into exact converging aim. Values above one make small stick movement gentler.")]
        [SerializeField, Range(0.5f, 4f)] private float aimBlendExponent = 1.6f;

        [Tooltip("Maximum rotation speed of the complete rigid arm in degrees per second.")]
        [SerializeField, Min(1f)] private float maximumAimSpeedDegreesPerSecond = 240f;

        [Tooltip("Time used by Arm 2 to follow changes in requested extension length.")]
        [SerializeField, Min(0.001f)] private float lengthSmoothingTime = 0.07f;

        public bool IsArmModeActive { get; private set; }
        public Vector2 CurrentTargetLocal { get; private set; }
        public float CurrentInputMagnitude { get; private set; }
        public float VisibleDeployment01 => leftArm != null && rightArm != null
            ? Mathf.Min(
                Mathf.Max(leftArm.ConnectorDeployment, leftArm.OuterDeployment),
                Mathf.Max(rightArm.ConnectorDeployment, rightArm.OuterDeployment))
            : 0f;

        private RobotMover mover;
        private RobotMarkerView markerView;
        private RobotBalanceView balanceView;
        private RobotTumbleController tumble;
        private ArmVisual leftArm;
        private ArmVisual rightArm;
        private float modeElapsed;
        private float releaseElapsed;
        private bool previousArmModeActive;
        private bool aimTargetActive;

        private sealed class ArmVisual
        {
            public GameObject RootObject;
            public Transform ConnectorPivot;
            public Transform ConnectorArtworkRoot;
            public Transform ConnectorArmTwo;
            public Transform OuterPivot;
            public Transform OuterRevealRoot;
            public Transform ArtworkRoot;
            public Transform ArmTwo;
            public Transform DistalAssembly;
            public SpriteRenderer ConnectorRenderer;
            public SpriteRenderer ArmTwoRenderer;
            public SpriteRenderer ArmOneRenderer;
            public SpriteRenderer HandRenderer;
            public float SideSign;
            public Vector2 BodySocketDirection;
            public Vector2 ConnectorDirection;
            public Vector2 DefaultOuterDirection;
            public float SocketAngle;
            public float SocketAngleVelocity;
            public float ConnectorAngle;
            public float ConnectorDeployment;
            public float OuterDeployment;
            public float AimAngle;
            public float AimVelocity;
            public float Extension;
            public float ExtensionVelocity;
        }

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            markerView = GetComponent<RobotMarkerView>();
            balanceView = GetComponent<RobotBalanceView>();
            tumble = GetComponent<RobotTumbleController>();
        }

        private void Start()
        {
            TryCreateArmVisuals();
        }

        private void Update()
        {
            if (leftArm == null || rightArm == null)
                TryCreateArmVisuals();

            bool tumbleArmControlAvailable = tumble != null
                                             && tumble.State
                                             != RobotTumbleState.Upright;
            bool canOperate = mover != null
                              && (mover.MovementMode
                                  == RobotMovementMode.Driven
                                  || tumbleArmControlAvailable);
            bool held = canOperate && IsArmInputHeld();
            IsArmModeActive = held;
            mover?.SetArmInputCaptured(held);

            if (held != previousArmModeActive)
            {
                if (held)
                    modeElapsed = 0f;
                else
                    releaseElapsed = 0f;
                previousArmModeActive = held;
            }

            if (held)
            {
                modeElapsed += Mathf.Max(0f, Time.deltaTime);
                ReadArmTargetInput();
            }
            else
            {
                releaseElapsed += Mathf.Max(0f, Time.deltaTime);
                CurrentTargetLocal = Vector2.zero;
                CurrentInputMagnitude = 0f;
                aimTargetActive = false;
            }

            if (leftArm == null || rightArm == null)
                return;

            UpdateArm(leftArm, 0f, held);
            UpdateArm(rightArm, secondArmDelay, held);
        }

        private void ReadArmTargetInput()
        {
            Vector2 keyboard = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            keyboard = Vector2.ClampMagnitude(keyboard, 1f);
            Vector2 gamepad = AdaptiveLegacyGamepadInput.ReadLeftStick();
            Vector2 raw = keyboard.sqrMagnitude >= gamepad.sqrMagnitude
                ? keyboard
                : Vector2.ClampMagnitude(gamepad, 1f);
            CurrentTargetLocal = ApplyRadialDeadZone(
                raw,
                leftStickDeadZone,
                out float magnitude);
            CurrentInputMagnitude = magnitude;
            if (aimTargetActive)
            {
                if (CurrentInputMagnitude <= aimExitMagnitude)
                    aimTargetActive = false;
            }
            else if (CurrentInputMagnitude >= aimEnterMagnitude)
            {
                aimTargetActive = true;
            }
        }

        private void UpdateArm(ArmVisual arm, float delay, bool extending)
        {
            float deltaTime = Mathf.Max(0f, Time.deltaTime);
            UpdateArmSocketFrame(arm, deltaTime);
            bool armDelayElapsed = extending
                ? modeElapsed >= delay
                : releaseElapsed >= delay;
            if (armDelayElapsed)
            {
                if (extending)
                {
                    arm.ConnectorDeployment = Mathf.MoveTowards(
                        arm.ConnectorDeployment,
                        1f,
                        deltaTime / Mathf.Max(0.01f, connectorExtendDuration));

                    if (arm.ConnectorDeployment
                        >= outerArmExtendStartNormalized)
                    {
                        arm.OuterDeployment = Mathf.MoveTowards(
                            arm.OuterDeployment,
                            1f,
                            deltaTime / Mathf.Max(0.01f, extendDuration));
                    }
                }
                else
                {
                    arm.OuterDeployment = Mathf.MoveTowards(
                        arm.OuterDeployment,
                        0f,
                        deltaTime / Mathf.Max(0.01f, retractDuration));

                    if (arm.OuterDeployment
                        <= connectorRetractStartOuterNormalized)
                    {
                        arm.ConnectorDeployment = Mathf.MoveTowards(
                            arm.ConnectorDeployment,
                            0f,
                            deltaTime
                            / Mathf.Max(0.01f, connectorRetractDuration));
                    }
                }
            }

            float connectorDeploymentVisual = Mathf.SmoothStep(
                0f,
                1f,
                arm.ConnectorDeployment);
            float outerDeploymentVisual = Mathf.SmoothStep(
                0f,
                1f,
                arm.OuterDeployment);
            if (arm.RootObject != null)
            {
                bool visible = arm.ConnectorDeployment > 0.0001f
                               || arm.OuterDeployment > 0.0001f
                               || extending;
                if (arm.RootObject.activeSelf != visible)
                    arm.RootObject.SetActive(visible);
            }

            if (arm.ConnectorPivot == null || arm.OuterPivot == null)
                return;

            float socketRadius = markerView.BodyDiameter
                                 * 0.5f
                                 * socketRadiusOfBody;
            Vector2 socketPosition = arm.BodySocketDirection * socketRadius;
            arm.ConnectorPivot.localPosition = new Vector3(
                socketPosition.x,
                socketPosition.y,
                0f);
            arm.ConnectorPivot.localRotation = Quaternion.Euler(
                0f,
                0f,
                arm.ConnectorAngle);

            float connectorLength = markerView.BodyDiameter
                                    * connectorLengthOfBodyDiameter;
            float visibleConnectorLength = connectorLength
                                           * connectorDeploymentVisual;
            ApplyConnectorLength(arm, visibleConnectorLength);
            arm.OuterPivot.localPosition = Vector3.up
                                           * visibleConnectorLength;
            if (arm.ConnectorRenderer != null)
            {
                arm.ConnectorRenderer.enabled = visibleConnectorLength
                                                > 0.0001f;
            }
            if (arm.OuterRevealRoot != null)
            {
                arm.OuterRevealRoot.localScale = new Vector3(
                    1f,
                    outerDeploymentVisual,
                    1f);
                arm.OuterRevealRoot.gameObject.SetActive(
                    outerDeploymentVisual > 0.0001f);
            }

            Vector2 aimDirection = arm.DefaultOuterDirection;
            float aimBlend = 0f;
            if (extending && aimTargetActive)
            {
                float ringRadius = GetBalanceRingRadiusLocal();
                Vector2 targetPoint = CurrentTargetLocal * ringRadius;
                Vector2 outerJointPosition = socketPosition
                                             + arm.ConnectorDirection
                                             * visibleConnectorLength;
                Vector2 fromJoint = targetPoint - outerJointPosition;
                if (fromJoint.sqrMagnitude > 0.000001f)
                    aimDirection = fromJoint.normalized;

                float linearBlend = Mathf.InverseLerp(
                    aimExitMagnitude,
                    Mathf.Max(aimEnterMagnitude + 0.0001f, fullAimMagnitude),
                    CurrentInputMagnitude);
                aimBlend = Mathf.Pow(
                    Mathf.Clamp01(linearBlend),
                    aimBlendExponent);
            }

            float defaultAngle = Vector2.SignedAngle(
                Vector2.up,
                arm.DefaultOuterDirection);
            float exactAimAngle = Vector2.SignedAngle(
                Vector2.up,
                aimDirection);
            float targetAngle = Mathf.LerpAngle(
                defaultAngle,
                exactAimAngle,
                aimBlend);
            arm.AimAngle = Mathf.SmoothDampAngle(
                arm.AimAngle,
                targetAngle,
                ref arm.AimVelocity,
                aimSmoothingTime,
                maximumAimSpeedDegreesPerSecond,
                deltaTime);
            arm.OuterPivot.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.DeltaAngle(arm.ConnectorAngle, arm.AimAngle));
            Vector2 currentOuterDirection = RotateVector(
                Vector2.up,
                arm.AimAngle).normalized;

            float requestedExtension = extending
                ? CalculateMaximumExtension(
                      socketPosition
                      + arm.ConnectorDirection * visibleConnectorLength,
                      currentOuterDirection)
                  * CurrentInputMagnitude
                  * outerDeploymentVisual
                : 0f;
            arm.Extension = Mathf.SmoothDamp(
                arm.Extension,
                requestedExtension,
                ref arm.ExtensionVelocity,
                lengthSmoothingTime,
                Mathf.Infinity,
                deltaTime);
            ApplyExtension(arm, Mathf.Max(0f, arm.Extension));
        }

        private void ApplyConnectorLength(ArmVisual arm, float length)
        {
            if (arm.ConnectorArmTwo == null)
                return;

            float pixelsPerUnit = GetSharedPixelsPerUnit();
            float baseY = (SourceCanvasCenterPixels - ArmTwoBaseImageY)
                          / pixelsPerUnit;
            float visibleNaturalLength = (ArmTwoBaseImageY - ArmTwoOuterImageY)
                                         / pixelsPerUnit
                                         * artworkScale;
            float scaleY = length
                           / Mathf.Max(0.0001f, visibleNaturalLength);
            arm.ConnectorArmTwo.localScale = new Vector3(1f, scaleY, 1f);
            arm.ConnectorArmTwo.localPosition = new Vector3(
                0f,
                baseY * (1f - scaleY),
                0f);
        }

        private float CalculateMaximumExtension(
            Vector2 outerJointPosition,
            Vector2 outerDirection)
        {
            float pixelsPerUnit = GetSharedPixelsPerUnit();
            float baseToHandCenterPixels = ArmTwoBaseImageY - 35f;
            float shortestOuterArmLength = baseToHandCenterPixels
                                           / pixelsPerUnit
                                           * artworkScale;
            float requestedMaximumRadius = Mathf.Max(
                0.0001f,
                GetBalanceRingRadiusLocal()
                * maximumReachOfBalanceRadius);
            Vector2 direction = outerDirection.sqrMagnitude > 0.000001f
                ? outerDirection.normalized
                : Vector2.up;
            float projectedJoint = Vector2.Dot(
                outerJointPosition,
                direction);
            float discriminant = projectedJoint * projectedJoint
                                 + requestedMaximumRadius
                                 * requestedMaximumRadius
                                 - outerJointPosition.sqrMagnitude;
            if (discriminant <= 0f)
                return Mathf.Max(0f, maximumExtensionOffset);

            float distanceFromJointToReachCircle = -projectedJoint
                                                   + Mathf.Sqrt(
                                                       discriminant);
            float reachLimitedExtension = distanceFromJointToReachCircle
                                          - shortestOuterArmLength
                                          + maximumExtensionOffset;
            float cargoPullStrokeLimit = markerView.BodyDiameter
                                         * maximumExtensionOfBodyDiameter;
            return Mathf.Max(
                0f,
                Mathf.Min(
                    reachLimitedExtension,
                    cargoPullStrokeLimit));
        }

        private void ApplyExtension(ArmVisual arm, float extraLength)
        {
            float pixelsPerUnit = GetSharedPixelsPerUnit();
            float baseY = (SourceCanvasCenterPixels - ArmTwoBaseImageY)
                          / pixelsPerUnit;
            float armTwoVisibleLength = (ArmTwoBaseImageY - ArmTwoOuterImageY)
                                        / pixelsPerUnit;
            float scaleY = 1f
                           + extraLength
                           / Mathf.Max(
                               0.0001f,
                               armTwoVisibleLength * artworkScale);
            arm.ArmTwo.localScale = new Vector3(1f, scaleY, 1f);
            arm.ArmTwo.localPosition = new Vector3(
                0f,
                baseY * (1f - scaleY),
                0f);
            arm.DistalAssembly.localPosition = new Vector3(
                0f,
                extraLength / Mathf.Max(0.0001f, artworkScale),
                0f);
        }

        private float GetBalanceRingRadiusLocal()
        {
            if (balanceView != null)
            {
                float converted = markerView.ScreenPixelsToMarkerLocalUnits(
                    balanceView.ControlRingRadiusPixels);
                if (converted > 0.0001f)
                    return converted;
            }

            return markerView.BodyDiameter * 1.65f;
        }

        private bool IsArmInputHeld()
        {
            return Input.GetKey(keyboardArmKey)
                   || AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
        }

        private void TryCreateArmVisuals()
        {
            if (leftArm != null
                || markerView == null
                || markerView.MarkerVisualRoot == null)
            {
                return;
            }

            leftArm = CreateArm("Left Mechanical Arm", 1f);
            rightArm = CreateArm("Right Mechanical Arm", -1f);
        }

        private void UpdateArmSocketFrame(ArmVisual arm, float deltaTime)
        {
            Vector2 forwardReference = GetArmForwardReferenceLocal();
            Vector2 targetSocketDirection;
            if (ShouldUseTumbleSocketSelection())
            {
                GetNearestTumbleSocketPair(
                    forwardReference,
                    out Vector2 leftSocketDirection,
                    out Vector2 rightSocketDirection);
                targetSocketDirection = arm.SideSign > 0f
                    ? leftSocketDirection
                    : rightSocketDirection;
            }
            else
            {
                targetSocketDirection = RotateVector(
                    forwardReference,
                    90f * arm.SideSign).normalized;
            }
            float targetSocketAngle = Vector2.SignedAngle(
                Vector2.up,
                targetSocketDirection);
            bool armIsVisible = arm.ConnectorDeployment > 0.0001f
                                || arm.OuterDeployment > 0.0001f;
            if (!armIsVisible)
            {
                arm.SocketAngle = targetSocketAngle;
                arm.SocketAngleVelocity = 0f;
            }
            else
            {
                arm.SocketAngle = Mathf.SmoothDampAngle(
                    arm.SocketAngle,
                    targetSocketAngle,
                    ref arm.SocketAngleVelocity,
                    socketPositionSmoothingTime,
                    Mathf.Infinity,
                    deltaTime);
            }

            arm.BodySocketDirection = RotateVector(
                Vector2.up,
                arm.SocketAngle).normalized;
            arm.ConnectorDirection = RotateVector(
                arm.BodySocketDirection,
                connectorDirectionDegrees * arm.SideSign).normalized;
            arm.DefaultOuterDirection = RotateVector(
                arm.ConnectorDirection,
                -perpendicularAngleDegrees * arm.SideSign).normalized;
            arm.ConnectorAngle = Vector2.SignedAngle(
                Vector2.up,
                arm.ConnectorDirection);
        }

        private bool ShouldUseTumbleSocketSelection()
        {
            return tumble != null
                   && tumble.State != RobotTumbleState.Upright
                   && tumble.DirectionWorld.sqrMagnitude > 0.000001f;
        }

        private Vector2 GetArmForwardReferenceLocal()
        {
            if (tumble == null
                || tumble.State == RobotTumbleState.Upright
                || tumble.DirectionWorld.sqrMagnitude <= 0.000001f)
            {
                return Vector2.up;
            }

            Vector2 directionWorld = tumble.DirectionWorld.normalized;
            Vector3 directionLocal = transform.InverseTransformDirection(
                new Vector3(directionWorld.x, directionWorld.y, 0f));
            Vector2 localPlanarDirection = new Vector2(
                directionLocal.x,
                directionLocal.y);
            return localPlanarDirection.sqrMagnitude > 0.000001f
                ? localPlanarDirection.normalized
                : Vector2.up;
        }

        private static void GetNearestTumbleSocketPair(
            Vector2 direction,
            out Vector2 leftSocketDirection,
            out Vector2 rightSocketDirection)
        {
            Vector2 primaryDirection;
            Vector2 secondaryDirection;
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            {
                primaryDirection = direction.x >= 0f
                    ? Vector2.right
                    : Vector2.left;
                secondaryDirection = direction.y >= 0f
                    ? Vector2.up
                    : Vector2.down;
            }
            else
            {
                primaryDirection = direction.y >= 0f
                    ? Vector2.up
                    : Vector2.down;
                secondaryDirection = direction.x >= 0f
                    ? Vector2.right
                    : Vector2.left;
            }

            float primarySideAngle = Vector2.SignedAngle(
                direction,
                primaryDirection);
            float secondarySideAngle = Vector2.SignedAngle(
                direction,
                secondaryDirection);
            if (primarySideAngle >= secondarySideAngle)
            {
                leftSocketDirection = primaryDirection;
                rightSocketDirection = secondaryDirection;
            }
            else
            {
                leftSocketDirection = secondaryDirection;
                rightSocketDirection = primaryDirection;
            }
        }

        private ArmVisual CreateArm(string objectName, float sideSign)
        {
            Vector2 socketDirection = RotateVector(
                Vector2.up,
                90f * sideSign).normalized;
            Vector2 connectorDirection = RotateVector(
                socketDirection,
                connectorDirectionDegrees * sideSign).normalized;
            Vector2 defaultOuterDirection = RotateVector(
                connectorDirection,
                -perpendicularAngleDegrees * sideSign).normalized;
            var arm = new ArmVisual
            {
                SideSign = sideSign,
                BodySocketDirection = socketDirection,
                ConnectorDirection = connectorDirection,
                DefaultOuterDirection = defaultOuterDirection,
                SocketAngle = Vector2.SignedAngle(
                    Vector2.up,
                    socketDirection),
                ConnectorAngle = Vector2.SignedAngle(
                    Vector2.up,
                    connectorDirection)
            };
            arm.RootObject = new GameObject(objectName);
            arm.ConnectorPivot = arm.RootObject.transform;
            arm.ConnectorPivot.SetParent(markerView.MarkerVisualRoot, false);

            var connectorArtworkObject = new GameObject(
                "Body Connector Artwork Root");
            arm.ConnectorArtworkRoot = connectorArtworkObject.transform;
            arm.ConnectorArtworkRoot.SetParent(arm.ConnectorPivot, false);
            float connectorBaseOffset = (ArmTwoBaseImageY
                                         - SourceCanvasCenterPixels)
                                        / GetSharedPixelsPerUnit()
                                        * artworkScale;
            arm.ConnectorArtworkRoot.localPosition = Vector3.up
                                                       * connectorBaseOffset;
            arm.ConnectorArtworkRoot.localScale = new Vector3(
                artworkScale * connectorThicknessScale,
                artworkScale,
                1f);
            arm.ConnectorRenderer = CreateSpriteRenderer(
                "Body Connector Robot_Arm_2",
                arm.ConnectorArtworkRoot,
                robotArmTwoSprite,
                996,
                out arm.ConnectorArmTwo);

            var outerPivotObject = new GameObject("Outer Arm Joint");
            arm.OuterPivot = outerPivotObject.transform;
            arm.OuterPivot.SetParent(arm.ConnectorPivot, false);
            arm.AimAngle = Vector2.SignedAngle(
                Vector2.up,
                defaultOuterDirection);
            arm.OuterPivot.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.DeltaAngle(arm.ConnectorAngle, arm.AimAngle));

            var outerRevealObject = new GameObject("Outer Arm Reveal Root");
            arm.OuterRevealRoot = outerRevealObject.transform;
            arm.OuterRevealRoot.SetParent(arm.OuterPivot, false);

            var artworkRootObject = new GameObject("Arm Artwork Root");
            arm.ArtworkRoot = artworkRootObject.transform;
            arm.ArtworkRoot.SetParent(arm.OuterRevealRoot, false);
            float baseOffset = (ArmTwoBaseImageY - SourceCanvasCenterPixels)
                               / GetSharedPixelsPerUnit()
                               * artworkScale;
            arm.ArtworkRoot.localPosition = Vector3.up * baseOffset;
            arm.ArtworkRoot.localScale = new Vector3(
                artworkScale * artworkThicknessScale,
                artworkScale,
                1f);

            arm.ArmTwoRenderer = CreateSpriteRenderer(
                "Robot_Arm_2",
                arm.ArtworkRoot,
                robotArmTwoSprite,
                997,
                out arm.ArmTwo);

            var distalObject = new GameObject("Arm 2 End Joint");
            arm.DistalAssembly = distalObject.transform;
            arm.DistalAssembly.SetParent(arm.ArtworkRoot, false);

            var armOneAssembly = new GameObject("Arm 1 Fixed Assembly").transform;
            armOneAssembly.SetParent(arm.DistalAssembly, false);
            arm.ArmOneRenderer = CreateSpriteRenderer(
                "Robot_Arm_1",
                armOneAssembly,
                robotArmOneSprite,
                998,
                out _);

            var handJoint = new GameObject("Fixed Hand Joint").transform;
            handJoint.SetParent(armOneAssembly, false);
            arm.HandRenderer = CreateSpriteRenderer(
                "Robot_Hand",
                handJoint,
                robotHandSprite,
                999,
                out _);

            arm.RootObject.SetActive(false);
            return arm;
        }

        private static Vector2 RotateVector(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sine = Mathf.Sin(radians);
            float cosine = Mathf.Cos(radians);
            return new Vector2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }

        private SpriteRenderer CreateSpriteRenderer(
            string objectName,
            Transform parent,
            Sprite sprite,
            int sortingOrder,
            out Transform spriteTransform)
        {
            var spriteObject = new GameObject(objectName);
            spriteTransform = spriteObject.transform;
            spriteTransform.SetParent(parent, false);
            var renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = armColor;
            renderer.sortingOrder = sortingOrder;
            if (markerView.ForegroundSpriteMaterial != null)
                renderer.sharedMaterial = markerView.ForegroundSpriteMaterial;
            return renderer;
        }

        private float GetSharedPixelsPerUnit()
        {
            if (robotArmTwoSprite != null)
                return Mathf.Max(1f, robotArmTwoSprite.pixelsPerUnit);
            if (robotArmOneSprite != null)
                return Mathf.Max(1f, robotArmOneSprite.pixelsPerUnit);
            if (robotHandSprite != null)
                return Mathf.Max(1f, robotHandSprite.pixelsPerUnit);
            return 100f;
        }

        private static Vector2 ApplyRadialDeadZone(
            Vector2 input,
            float deadZone,
            out float remappedMagnitude)
        {
            float magnitude = Mathf.Clamp01(input.magnitude);
            if (magnitude <= deadZone)
            {
                remappedMagnitude = 0f;
                return Vector2.zero;
            }

            remappedMagnitude = Mathf.InverseLerp(deadZone, 1f, magnitude);
            return input.normalized * remappedMagnitude;
        }

        private void OnDisable()
        {
            IsArmModeActive = false;
            mover?.SetArmInputCaptured(false);
            if (leftArm?.RootObject != null)
                leftArm.RootObject.SetActive(false);
            if (rightArm?.RootObject != null)
                rightArm.RootObject.SetActive(false);
        }

        private void OnValidate()
        {
            leftStickDeadZone = Mathf.Clamp(leftStickDeadZone, 0f, 0.9f);
            artworkScale = Mathf.Max(0.01f, artworkScale);
            artworkThicknessScale = Mathf.Max(0.1f, artworkThicknessScale);
            socketRadiusOfBody = Mathf.Clamp(socketRadiusOfBody, 0f, 1.2f);
            maximumExtensionOfBodyDiameter = Mathf.Clamp(
                maximumExtensionOfBodyDiameter,
                0.05f,
                1.5f);
            connectorLengthOfBodyDiameter = Mathf.Clamp(
                connectorLengthOfBodyDiameter,
                0.05f,
                1.5f);
            connectorDirectionDegrees = Mathf.Clamp(
                connectorDirectionDegrees,
                -180f,
                180f);
            perpendicularAngleDegrees = Mathf.Clamp(
                perpendicularAngleDegrees,
                15f,
                165f);
            connectorThicknessScale = Mathf.Max(0.1f, connectorThicknessScale);
            connectorExtendDuration = Mathf.Max(0.01f, connectorExtendDuration);
            connectorRetractDuration = Mathf.Max(
                0.01f,
                connectorRetractDuration);
            outerArmExtendStartNormalized = Mathf.Clamp01(
                outerArmExtendStartNormalized);
            connectorRetractStartOuterNormalized = Mathf.Clamp01(
                connectorRetractStartOuterNormalized);
            maximumReachOfBalanceRadius = Mathf.Clamp(
                maximumReachOfBalanceRadius,
                0.1f,
                1.5f);
            extendDuration = Mathf.Max(0.01f, extendDuration);
            retractDuration = Mathf.Max(0.01f, retractDuration);
            secondArmDelay = Mathf.Clamp(secondArmDelay, 0f, 0.2f);
            aimSmoothingTime = Mathf.Max(0.001f, aimSmoothingTime);
            socketPositionSmoothingTime = Mathf.Max(
                0.001f,
                socketPositionSmoothingTime);
            aimEnterMagnitude = Mathf.Clamp01(aimEnterMagnitude);
            aimExitMagnitude = Mathf.Clamp(
                aimExitMagnitude,
                0f,
                aimEnterMagnitude);
            fullAimMagnitude = Mathf.Clamp(
                fullAimMagnitude,
                Mathf.Max(0.01f, aimEnterMagnitude),
                1f);
            aimBlendExponent = Mathf.Clamp(aimBlendExponent, 0.5f, 4f);
            maximumAimSpeedDegreesPerSecond = Mathf.Max(
                1f,
                maximumAimSpeedDegreesPerSecond);
            lengthSmoothingTime = Mathf.Max(0.001f, lengthSmoothingTime);
        }
    }
}
