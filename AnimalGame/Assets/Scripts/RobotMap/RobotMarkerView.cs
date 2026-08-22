using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed class RobotMarkerView : MonoBehaviour
    {
        [Header("Body")]
        [SerializeField] private Sprite robotBodySprite;
        [Tooltip("Filled silhouette drawn directly beneath robot_body. Use Arts/robot_body_fill so both sprites share the same canvas, pivot and scale.")]
        [SerializeField] private Sprite robotBodyFillSprite;
        [Tooltip("Reference chassis diameter used by existing gameplay-relative visuals and conversions. Keep this unchanged when only resizing the rendered artwork.")]
        [SerializeField, Min(0.1f)] private float bodyDiameter = 0.72f;
        [Tooltip("Rendered body diameter relative to Body Diameter. This changes only the visible chassis and its attached surface effects; gameplay dimensions remain unchanged.")]
        [SerializeField, Range(0.5f, 1.25f)]
        private float visualBodyDiameterRatio = 0.85f;
        [Tooltip("Visible ring diameter in the source robot_body sprite, excluding transparent padding.")]
        [SerializeField, Min(1f)] private float bodyArtworkVisibleDiameterPixels = 72.5f;

        [Tooltip("Visible filled-circle diameter in the source robot_body_fill sprite, excluding transparent padding. This lets the unchanged fill artwork fit a differently sized body sprite.")]
        [SerializeField, Min(1f)]
        private float bodyFillArtworkVisibleDiameterPixels = 82.5f;

        [Tooltip("Keeps the complete robot marker at a stable pixel size when build resolution or camera zoom changes. Disable to use Body Diameter as a fixed world-space size.")]
        [SerializeField] private bool keepMarkerSizeConstantOnScreen = true;

        [Tooltip("Screen-pixel size of the reference Body Diameter while Keep Marker Size Constant On Screen is enabled. The rendered chassis is this value multiplied by Visual Body Diameter Ratio.")]
        [SerializeField, Min(1f)] private float bodyScreenDiameterPixels = 45f;

        [SerializeField, Range(0.1f, 1f)] private float bodyFillDiameterRatio = 1f;
        [SerializeField] private Color bodyFillColor = new Color(0.008f, 0.011f, 0.014f, 1f);
        [Tooltip("When enabled, robot_body_fill always uses the active game camera's background color. Body Fill Color remains the fallback when no camera is active.")]
        [SerializeField] private bool matchBodyFillToCameraBackground = true;
        [SerializeField] private Color bodyOutlineColor = new Color(0.92f, 0.98f, 1f, 1f);
        [Tooltip("Camera-facing depth offset used by the complete robot visual. A small negative value places it in front of the map camera's Z=0 map plane.")]
        [SerializeField] private float visualDepthOffset = -0.1f;

        [Header("Direction Indicator")]
        [SerializeField] private Sprite indicatorSprite;
        [SerializeField, Min(0.1f)] private float indicatorScale = 1.35f;
        [SerializeField] private float indicatorRotationOffsetDegrees;
        [Tooltip("Moves the visible direction indicator inward toward the chassis, relative to the visible body diameter. This affects only artwork spacing.")]
        [SerializeField, Range(0f, 0.5f)]
        private float indicatorBodyInsetRatio = 0.2f;
        [SerializeField] private Color indicatorColor = new Color(0.92f, 0.98f, 1f, 1f);

        [Tooltip("Time used to fade the direction arrow out after entering arm-control mode.")]
        [SerializeField, Min(0.01f)]
        private float indicatorArmModeFadeOutDuration = 0.16f;

        [Tooltip("Time used to fade the direction arrow back in after leaving arm-control mode.")]
        [SerializeField, Min(0.01f)]
        private float indicatorArmModeFadeInDuration = 0.2f;

        [Header("Direction Indicator Tumble Projection")]
        [Tooltip("How far the direction arrow moves toward the falling edge while its top surface turns away, relative to the visible body diameter.")]
        [SerializeField, Range(0f, 0.5f)] private float indicatorTumbleEdgeOffsetRatio = 0.18f;

        [Tooltip("Small residual scale retained along the projected tumble axis before the arrow becomes fully hidden.")]
        [SerializeField, Range(0f, 0.25f)] private float indicatorMinimumProjectedScale = 0.04f;

        [Tooltip("Shapes how quickly the top-surface arrow fades as it turns edge-on. Values below one retain readability longer.")]
        [SerializeField, Range(0.25f, 3f)] private float indicatorTumbleFadeExponent = 0.72f;

        [Header("Photo Camera Form")]
        [Tooltip("Arts/Player_Camera/Camera_first_part, used as the extending camera stem.")]
        [SerializeField] private Sprite cameraFirstPartSprite;

        [Tooltip("Arts/Player_Camera/camera_second_part, used as the deploying camera head.")]
        [SerializeField] private Sprite cameraSecondPartSprite;

        [Tooltip("Scale shared by the two camera sprites. Both assets use the same 128 px canvas and pivot.")]
        [SerializeField, Min(0.1f)] private float photoCameraArtworkScale = 0.7f;

        [Tooltip("How far the camera stem overlaps the top of the body, relative to the visible body diameter.")]
        [SerializeField, Range(0f, 0.25f)]
        private float photoCameraBodyOverlapRatio = 0.035f;

        [Tooltip("Reveal progress at which the upper camera head starts deploying after the stem.")]
        [SerializeField, Range(0f, 0.9f)]
        private float photoCameraHeadRevealStart = 0.3f;

        [SerializeField] private Color photoCameraColor =
            new Color(0.92f, 0.98f, 1f, 1f);

        [Header("Fallen Rollover Sign")]
        [Tooltip("Arts/rollover_sign displayed over the robot only after tumbling has completely settled.")]
        [SerializeField] private Sprite rolloverSignSprite;

        [Tooltip("Diagonal endpoint span of the visible rollover sign relative to the visible robot body diameter. A value of one inscribes the X inside the circular body.")]
        [SerializeField, Min(0.1f)] private float rolloverSignDiameterRatio = 1f;

        [Tooltip("Width and height of the visible artwork in the 128 px Arts/rollover_sign source. Transparent padding is ignored and the diagonal endpoint span is calculated from this value.")]
        [SerializeField, Min(1f)] private float rolloverSignVisibleDiameterPixels = 60f;

        [SerializeField] private Color rolloverSignColor = Color.white;

        [Header("Final Settling Rock")]
        [Tooltip("Maximum sideways marker travel during the final rocking phase, relative to the visible body diameter.")]
        [SerializeField, Range(0f, 0.15f)] private float finalRockVisualOffsetRatio = 0.045f;

        [Header("Visual Drive Bob")]
        [Tooltip("Moves only the Body and Indicator visual hierarchy. Robot position, camera target, terrain queries and traversal UI remain unchanged.")]
        [SerializeField] private bool showDriveBob = true;

        [Tooltip("Maximum local fore/aft visual travel in Unity world units.")]
        [SerializeField, Min(0f)] private float driveBobAmplitude = 0.05f;

        [Tooltip("Number of mechanical push/regrip cycles completed per meter of commanded robot travel.")]
        [SerializeField, Min(0f)] private float driveBobCyclesPerMeter = 1.25f;

        [Tooltip("Commanded speed at which the visual bob reaches full strength.")]
        [SerializeField, Min(0.01f)] private float driveBobFullStrengthSpeed = 1.25f;

        [Tooltip("Time used to blend the visual bob in after the robot starts driving.")]
        [SerializeField, Min(0.01f)] private float driveBobEnterSmoothing = 0.12f;

        [Tooltip("Time used to return the visual hierarchy to its real position after drive stops.")]
        [SerializeField, Min(0.01f)] private float driveBobExitSmoothing = 0.2f;

        [Tooltip("Local-position smoothing applied to each mechanical stroke. Lower values feel sharper.")]
        [SerializeField, Min(0.005f)] private float driveBobPositionSmoothing = 0.035f;

        [Tooltip("Maximum random amplitude variation chosen once per drive cycle. A value of 0.18 produces roughly 82 to 118 percent amplitude.")]
        [SerializeField, Range(0f, 0.75f)] private float driveBobAmplitudeRandomness = 0.18f;

        [Tooltip("Maximum random cycle-frequency variation chosen once per drive cycle. This changes the travelled distance between successive pushes without producing per-frame jitter.")]
        [SerializeField, Range(0f, 0.75f)] private float driveBobFrequencyRandomness = 0.22f;

        [Tooltip("Strength of an additional smooth, non-repeating low-frequency offset, expressed as a fraction of Drive Bob Amplitude.")]
        [SerializeField, Range(0f, 1f)] private float driveBobNoiseAmplitudeRatio = 0.12f;

        [Tooltip("Frequency in Hz of the smooth secondary drive noise.")]
        [SerializeField, Min(0.01f)] private float driveBobNoiseFrequency = 0.65f;

        [Tooltip("Visual bob amplitude multiplier while travelling uphill on Level Two.")]
        [SerializeField, Min(0f)] private float levelTwoDriveBobMultiplier = 1.4f;

        [Tooltip("Visual bob amplitude multiplier during the Level Three Grip phase.")]
        [SerializeField, Min(0f)] private float levelThreeGripDriveBobMultiplier = 1.8f;

        [Header("Motion Tail")]
        [SerializeField] private bool showMotionTail = true;

        private Transform markerVisualRoot;
        private Transform bodyVisualRoot;
        private SpriteRenderer bodyFill;
        private SpriteRenderer bodyArtwork;
        private SpriteRenderer directionIndicator;
        private Transform photoCameraRoot;
        private SpriteRenderer cameraFirstPart;
        private SpriteRenderer cameraSecondPart;
        private SpriteRenderer rolloverSign;
        private LineRenderer tail;
        private Sprite generatedBodySprite;
        private Texture2D generatedBodyTexture;
        private Material foregroundSpriteMaterial;
        private Camera bodyFillBackgroundCamera;
        private Camera markerSizingCamera;
        private RobotMover mover;
        private RobotTumbleController tumble;
        private RobotArmController armController;
        private PhotoModeController photoMode;
        private float indicatorArmModeVisibility = 1f;
        private float photoCameraFormVisibility;
        private float driveBobPhase;
        private float driveBobBlend;
        private float driveBobBlendVelocity;
        private float driveBobOffset;
        private float driveBobOffsetVelocity;
        private float driveBobCycleAmplitudeMultiplier = 1f;
        private float driveBobCycleFrequencyMultiplier = 1f;
        private float driveBobNoiseSeed;
        private System.Random driveBobRandom;

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            tumble = GetComponent<RobotTumbleController>();
            armController = GetComponent<RobotArmController>();
            photoMode = GetComponent<PhotoModeController>();
            driveBobRandom = new System.Random(
                unchecked(GetInstanceID() * 397 ^ System.Environment.TickCount));
            driveBobNoiseSeed = NextDriveBobRandom(0f, 1000f);
            RandomizeDriveBobCycle();
            CreateMarkerVisualRoot();
            CreateForegroundSpriteMaterial();
            CreateBodySpriteRenderer();
            CreateDirectionIndicatorRenderer();
            CreatePhotoCameraRenderers();
            CreateRolloverSignRenderer();

            tail = RobotMapDemo.CreateLine(transform, "Motion Tail", new[]
            {
                new Vector3(0f, -0.38f), new Vector3(0f, -0.38f)
            }, 0.05f, new Color(0.35f, 0.82f, 0.9f, 0.7f), 18);
            ApplyMotionTailVisibility();
        }

        public bool ShowMotionTail => showMotionTail;
        public bool ShowDriveBob => showDriveBob;
        public Transform MarkerVisualRoot => markerVisualRoot;
        public float BodyDiameter => bodyDiameter;
        public float VisualBodyDiameter => bodyDiameter * visualBodyDiameterRatio;
        public Material ForegroundSpriteMaterial => foregroundSpriteMaterial;
        public Sprite RolloverSignSprite => rolloverSignSprite;
        public float RolloverSignVisibleDiameterPixels =>
            rolloverSignVisibleDiameterPixels;

        public float ScreenPixelsToMarkerLocalUnits(float screenPixels)
        {
            if (markerVisualRoot == null)
                return 0f;

            if (markerSizingCamera == null
                || !markerSizingCamera.isActiveAndEnabled)
            {
                markerSizingCamera = Camera.main;
            }

            if (markerSizingCamera != null && markerSizingCamera.orthographic)
            {
                float worldUnitsPerPixel = markerSizingCamera.orthographicSize
                                           * 2f
                                           / Mathf.Max(
                                               1f,
                                               markerSizingCamera.pixelHeight);
                float visualWorldScale = Mathf.Max(
                    0.0001f,
                    markerVisualRoot.lossyScale.x);
                return Mathf.Max(0f, screenPixels)
                       * worldUnitsPerPixel
                       / visualWorldScale;
            }

            return Mathf.Max(0f, screenPixels)
                   * bodyDiameter
                   / Mathf.Max(1f, bodyScreenDiameterPixels);
        }

        public void SetMotionTailVisible(bool shouldShow)
        {
            showMotionTail = shouldShow;
            ApplyMotionTailVisibility();
        }

        public void SetDriveBobVisible(bool shouldShow)
        {
            showDriveBob = shouldShow;
        }

        private void Update()
        {
            SynchronizeMarkerScreenSize();
            SynchronizeBodyFillColor();
            UpdatePhotoCameraForm();
            UpdateDirectionIndicatorSurfaceProjection();
            SynchronizeRolloverSignVisibility();

            bool movementLocked = mover != null && mover.IsMovementLocked;
            float pulse = 1f + Mathf.Sin(Time.time * 3.2f) * 0.035f;
            if (bodyVisualRoot != null)
            {
                bodyVisualRoot.localScale = movementLocked
                    ? Vector3.one
                    : Vector3.one * pulse;
            }

            UpdateDriveBob();
            if (tail != null)
                tail.enabled = showMotionTail && !movementLocked;

            if (!movementLocked && showMotionTail && tail != null && mover != null)
            {
                float tailLength = Mathf.Clamp(Mathf.Abs(mover.CurrentSpeed) * 0.2f, 0f, 1.1f);
                float visualOffset = markerVisualRoot != null
                    ? markerVisualRoot.localPosition.y
                    : 0f;
                tail.SetPosition(0, new Vector3(0f, visualOffset - 0.38f));
                tail.SetPosition(
                    1,
                    new Vector3(0f, visualOffset - 0.38f - tailLength));
            }
        }

        private void UpdateDriveBob()
        {
            if (markerVisualRoot == null)
                return;

            if (mover != null && mover.IsMovementLocked)
            {
                StopDriveBob();
                return;
            }

            float signedDriveSpeed = mover != null ? mover.CurrentSpeed : 0f;
            float absoluteDriveSpeed = Mathf.Abs(signedDriveSpeed);
            float targetBlend = showDriveBob
                ? Mathf.InverseLerp(
                    0f,
                    Mathf.Max(0.01f, driveBobFullStrengthSpeed),
                    absoluteDriveSpeed)
                : 0f;
            float blendSmoothing = targetBlend > driveBobBlend
                ? driveBobEnterSmoothing
                : driveBobExitSmoothing;
            driveBobBlend = Mathf.SmoothDamp(
                driveBobBlend,
                targetBlend,
                ref driveBobBlendVelocity,
                Mathf.Max(0.01f, blendSmoothing));

            if (absoluteDriveSpeed > 0.001f && showDriveBob)
            {
                float phaseAdvance = absoluteDriveSpeed
                                     * driveBobCyclesPerMeter
                                     * driveBobCycleFrequencyMultiplier
                                     * Time.deltaTime;
                driveBobPhase += phaseAdvance;
                while (driveBobPhase >= 1f)
                {
                    driveBobPhase -= 1f;
                    RandomizeDriveBobCycle();
                }
            }

            float directionSign = signedDriveSpeed < -0.001f ? -1f : 1f;
            float smoothNoise = Mathf.PerlinNoise(
                                    driveBobNoiseSeed,
                                    Time.time * driveBobNoiseFrequency)
                                * 2f
                                - 1f;
            float cycleOffset = EvaluateDriveBobCycle(driveBobPhase)
                                * driveBobCycleAmplitudeMultiplier;
            float targetOffset = (cycleOffset
                                  + smoothNoise * driveBobNoiseAmplitudeRatio)
                                 * driveBobAmplitude
                                 * driveBobBlend
                                 * directionSign
                                 * GetTerrainDriveBobMultiplier();
            driveBobOffset = Mathf.SmoothDamp(
                driveBobOffset,
                targetOffset,
                ref driveBobOffsetVelocity,
                Mathf.Max(0.005f, driveBobPositionSmoothing));
            markerVisualRoot.localPosition = new Vector3(
                0f,
                driveBobOffset,
                visualDepthOffset);
        }

        private void RandomizeDriveBobCycle()
        {
            driveBobCycleAmplitudeMultiplier = NextDriveBobRandom(
                1f - driveBobAmplitudeRandomness,
                1f + driveBobAmplitudeRandomness);
            driveBobCycleFrequencyMultiplier = NextDriveBobRandom(
                1f - driveBobFrequencyRandomness,
                1f + driveBobFrequencyRandomness);
        }

        private float NextDriveBobRandom(float minimum, float maximum)
        {
            if (driveBobRandom == null)
                return (minimum + maximum) * 0.5f;

            return Mathf.Lerp(
                minimum,
                maximum,
                (float)driveBobRandom.NextDouble());
        }

        private float GetTerrainDriveBobMultiplier()
        {
            if (mover == null)
                return 1f;

            if (mover.CurrentLevelThreeClimbPhase
                == LevelThreeClimbFailurePhase.Grip)
            {
                return levelThreeGripDriveBobMultiplier;
            }

            return mover.CurrentTraversalResult.HasData
                   && mover.CurrentTraversalResult.UphillLevel
                   == UphillSlopeLevel.LevelTwo
                ? levelTwoDriveBobMultiplier
                : 1f;
        }

        private static float EvaluateDriveBobCycle(float phase)
        {
            float normalizedPhase = Mathf.Repeat(phase, 1f);
            float value;
            if (normalizedPhase < 0.18f)
            {
                float progress = Mathf.SmoothStep(
                    0f,
                    1f,
                    normalizedPhase / 0.18f);
                value = Mathf.Lerp(-0.4f, 1f, progress);
            }
            else if (normalizedPhase < 0.82f)
            {
                float progress = Mathf.SmoothStep(
                    0f,
                    1f,
                    (normalizedPhase - 0.18f) / 0.64f);
                value = Mathf.Lerp(1f, -0.6f, progress);
            }
            else
            {
                float progress = Mathf.SmoothStep(
                    0f,
                    1f,
                    (normalizedPhase - 0.82f) / 0.18f);
                value = Mathf.Lerp(-0.6f, -0.4f, progress);
            }

            return value - 0.09f;
        }

        private void ApplyMotionTailVisibility()
        {
            if (tail != null)
            {
                tail.enabled = showMotionTail
                               && (mover == null || !mover.IsMovementLocked);
            }
        }

        private void CreateMarkerVisualRoot()
        {
            var visualRootObject = new GameObject("Marker Visual Root");
            visualRootObject.transform.SetParent(transform, false);
            markerVisualRoot = visualRootObject.transform;
            markerVisualRoot.localPosition = new Vector3(0f, 0f, visualDepthOffset);
        }

        private void SynchronizeMarkerScreenSize()
        {
            if (markerVisualRoot == null)
                return;

            if (!keepMarkerSizeConstantOnScreen)
            {
                markerVisualRoot.localScale = Vector3.one;
                return;
            }

            if (markerSizingCamera == null || !markerSizingCamera.isActiveAndEnabled)
                markerSizingCamera = Camera.main;

            if (markerSizingCamera == null || !markerSizingCamera.orthographic)
            {
                markerVisualRoot.localScale = Vector3.one;
                return;
            }

            float renderedHeightPixels = Mathf.Max(1f, markerSizingCamera.pixelHeight);
            float worldUnitsPerPixel =
                markerSizingCamera.orthographicSize * 2f / renderedHeightPixels;
            float desiredWorldDiameter =
                bodyScreenDiameterPixels * worldUnitsPerPixel;
            float scale = desiredWorldDiameter / Mathf.Max(0.0001f, bodyDiameter);
            markerVisualRoot.localScale = Vector3.one * scale;
        }

        private void CreateForegroundSpriteMaterial()
        {
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader == null)
            {
                Debug.LogWarning(
                    "RobotMarkerView could not find the Sprites/Default shader. " +
                    "The depth and sorting-order safeguards will still be used.",
                    this);
                return;
            }

            foregroundSpriteMaterial = new Material(spriteShader)
            {
                name = "Runtime Robot Foreground Sprite Material",
                hideFlags = HideFlags.DontSave,
                // Draw after the map's normal transparent queue. Body fill,
                // artwork and indicator still order among themselves below.
                renderQueue = 3500
            };
        }

        private void CreateBodySpriteRenderer()
        {
            var bodyVisualObject = new GameObject("Body Visual");
            bodyVisualObject.transform.SetParent(markerVisualRoot, false);
            bodyVisualRoot = bodyVisualObject.transform;

            Sprite fillSprite = robotBodyFillSprite;
            if (fillSprite == null)
            {
                generatedBodySprite = CreateCircularFillSprite(out generatedBodyTexture);
                fillSprite = generatedBodySprite;
                Debug.LogWarning(
                    "RobotMarkerView is missing Arts/robot_body_fill. " +
                    "Using the generated circular fallback fill.",
                    this);
            }

            float bodyArtworkScale = CalculateArtworkScale(
                robotBodySprite,
                bodyArtworkVisibleDiameterPixels,
                VisualBodyDiameter);
            float targetFillDiameter = VisualBodyDiameter
                                       * bodyFillDiameterRatio;
            float bodyFillArtworkScale = CalculateArtworkScale(
                fillSprite,
                bodyFillArtworkVisibleDiameterPixels,
                targetFillDiameter);

            var fillObject = new GameObject("Body Fill (robot_body_fill)");
            fillObject.transform.SetParent(bodyVisualRoot, false);
            bodyFill = fillObject.AddComponent<SpriteRenderer>();
            bodyFill.sprite = fillSprite;
            bodyFill.color = bodyFillColor;
            bodyFill.sortingOrder = 1000;
            if (foregroundSpriteMaterial != null)
                bodyFill.sharedMaterial = foregroundSpriteMaterial;
            bodyFill.transform.localScale = robotBodyFillSprite != null
                ? Vector3.one * bodyFillArtworkScale
                : Vector3.one * targetFillDiameter;

            var artworkObject = new GameObject("Body Artwork");
            artworkObject.transform.SetParent(bodyVisualRoot, false);
            bodyArtwork = artworkObject.AddComponent<SpriteRenderer>();
            bodyArtwork.sprite = robotBodySprite;
            bodyArtwork.color = bodyOutlineColor;
            bodyArtwork.sortingOrder = 1001;
            if (foregroundSpriteMaterial != null)
                bodyArtwork.sharedMaterial = foregroundSpriteMaterial;

            if (robotBodySprite != null)
            {
                bodyArtwork.transform.localScale = Vector3.one * bodyArtworkScale;
            }
            else
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing its Arts/robot_body Sprite.",
                    this);
            }
        }

        private static float CalculateArtworkScale(
            Sprite sprite,
            float visibleDiameterPixels,
            float targetDiameter)
        {
            if (sprite == null)
                return Mathf.Max(0f, targetDiameter);

            float artworkDiameter = visibleDiameterPixels /
                Mathf.Max(1f, sprite.pixelsPerUnit);
            return Mathf.Max(0f, targetDiameter)
                   / Mathf.Max(0.0001f, artworkDiameter);
        }

        private void SynchronizeBodyFillColor()
        {
            if (bodyFill == null)
                return;

            Color targetColor = bodyFillColor;
            if (matchBodyFillToCameraBackground)
            {
                if (bodyFillBackgroundCamera == null
                    || !bodyFillBackgroundCamera.isActiveAndEnabled)
                {
                    bodyFillBackgroundCamera = Camera.main;
                }

                if (bodyFillBackgroundCamera != null)
                    targetColor = bodyFillBackgroundCamera.backgroundColor;
            }

            targetColor.a = 1f;
            bodyFill.color = targetColor;
        }

        private void CreateDirectionIndicatorRenderer()
        {
            var indicatorObject = new GameObject("Direction Indicator");
            indicatorObject.transform.SetParent(markerVisualRoot, false);
            indicatorObject.transform.localRotation = Quaternion.Euler(
                0f,
                0f,
                indicatorRotationOffsetDegrees);
            indicatorObject.transform.localScale = Vector3.one * indicatorScale;

            directionIndicator = indicatorObject.AddComponent<SpriteRenderer>();
            directionIndicator.sprite = indicatorSprite;
            directionIndicator.color = indicatorColor;
            directionIndicator.sortingOrder = 1002;
            if (foregroundSpriteMaterial != null)
                directionIndicator.sharedMaterial = foregroundSpriteMaterial;

            if (indicatorSprite == null)
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing its direction Indicator Sprite.",
                    this);
            }
        }

        private void CreatePhotoCameraRenderers()
        {
            var cameraRootObject = new GameObject("Photo Camera Form");
            cameraRootObject.transform.SetParent(bodyVisualRoot, false);
            photoCameraRoot = cameraRootObject.transform;

            cameraFirstPart = CreatePhotoCameraPart(
                "Camera First Part",
                cameraFirstPartSprite,
                1003);
            cameraSecondPart = CreatePhotoCameraPart(
                "Camera Second Part",
                cameraSecondPartSprite,
                1004);

            if (cameraFirstPartSprite == null
                || cameraSecondPartSprite == null)
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing one or both "
                    + "Arts/Player_Camera camera sprites.",
                    this);
            }

            UpdatePhotoCameraForm();
        }

        private SpriteRenderer CreatePhotoCameraPart(
            string objectName,
            Sprite sprite,
            int sortingOrder)
        {
            var partObject = new GameObject(objectName);
            partObject.transform.SetParent(photoCameraRoot, false);

            SpriteRenderer renderer = partObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = WithAlpha(photoCameraColor, 0f);
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
            if (foregroundSpriteMaterial != null)
                renderer.sharedMaterial = foregroundSpriteMaterial;
            return renderer;
        }

        private void UpdatePhotoCameraForm()
        {
            bool hasCameraArtwork = cameraFirstPart != null
                                    && cameraFirstPart.sprite != null
                                    && cameraSecondPart != null
                                    && cameraSecondPart.sprite != null;
            if (!hasCameraArtwork || photoCameraRoot == null)
            {
                photoCameraFormVisibility = 0f;
                SetPhotoCameraPartVisible(cameraFirstPart, 0f);
                SetPhotoCameraPartVisible(cameraSecondPart, 0f);
                return;
            }

            if (photoMode == null)
                photoMode = GetComponent<PhotoModeController>();

            float reveal = photoMode != null
                ? Mathf.Clamp01(photoMode.Reveal01)
                : 0f;
            photoCameraFormVisibility = Mathf.SmoothStep(0f, 1f, reveal);

            const float FirstPartVisibleLowerExtentPixels = 24f;
            float firstPartLowerExtent = FirstPartVisibleLowerExtentPixels
                                         / Mathf.Max(
                                             1f,
                                             cameraFirstPart.sprite.pixelsPerUnit)
                                         * photoCameraArtworkScale;
            float cameraRootHeight = VisualBodyDiameter * 0.5f
                                     - VisualBodyDiameter
                                     * photoCameraBodyOverlapRatio
                                     + firstPartLowerExtent;
            photoCameraRoot.localPosition = new Vector3(
                0f,
                cameraRootHeight,
                0f);
            photoCameraRoot.localRotation = Quaternion.identity;

            float stemProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.72f, reveal));
            Transform stemTransform = cameraFirstPart.transform;
            stemTransform.localPosition = new Vector3(
                0f,
                Mathf.Lerp(
                    -VisualBodyDiameter * 0.09f,
                    0f,
                    stemProgress),
                0f);
            stemTransform.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Lerp(4f, 0f, stemProgress));
            stemTransform.localScale = new Vector3(
                photoCameraArtworkScale
                * Mathf.Lerp(0.86f, 1f, stemProgress),
                photoCameraArtworkScale
                * Mathf.Lerp(0.08f, 1f, stemProgress),
                1f);
            SetPhotoCameraPartVisible(cameraFirstPart, stemProgress);

            float headProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    photoCameraHeadRevealStart,
                    1f,
                    reveal));
            float headPopScale = 1f
                                 + Mathf.Sin(headProgress * Mathf.PI)
                                 * 0.1f;
            Transform headTransform = cameraSecondPart.transform;
            headTransform.localPosition = new Vector3(
                0f,
                Mathf.Lerp(
                    -VisualBodyDiameter * 0.13f,
                    0f,
                    headProgress),
                0f);
            headTransform.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Lerp(-9f, 0f, headProgress));
            headTransform.localScale = Vector3.one
                                       * photoCameraArtworkScale
                                       * Mathf.Lerp(
                                           0.55f,
                                           1f,
                                           headProgress)
                                       * headPopScale;
            SetPhotoCameraPartVisible(cameraSecondPart, headProgress);
        }

        private void SetPhotoCameraPartVisible(
            SpriteRenderer renderer,
            float visibility)
        {
            if (renderer == null)
                return;

            float alpha = Mathf.Clamp01(visibility);
            renderer.color = WithAlpha(
                photoCameraColor,
                photoCameraColor.a * alpha);
            renderer.enabled = renderer.sprite != null && alpha > 0.001f;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static Sprite CreateCircularFillSprite(out Texture2D texture)
        {
            const int Resolution = 128;
            // Keep the body interior fully opaque. Only the outermost pixels are
            // antialiased so bright contour lines cannot bleed through the centre.
            const float OuterFadeStart = 0.985f;

            texture = new Texture2D(
                Resolution,
                Resolution,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Runtime Robot Body Fill",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color[Resolution * Resolution];
            Vector2 center = Vector2.one * (Resolution * 0.5f);
            float radius = Resolution * 0.5f;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                    float normalizedDistance = Vector2.Distance(pixelCenter, center) / radius;
                    float outerCoverage = 1f - Mathf.SmoothStep(
                        OuterFadeStart,
                        1f,
                        normalizedDistance);

                    if (outerCoverage <= 0f)
                    {
                        pixels[y * Resolution + x] = Color.clear;
                        continue;
                    }

                    pixels[y * Resolution + x] = new Color(1f, 1f, 1f, outerCoverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, Resolution, Resolution),
                Vector2.one * 0.5f,
                Resolution,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "Runtime Robot Body Fill";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private void StopDriveBob()
        {
            driveBobBlend = 0f;
            driveBobBlendVelocity = 0f;
            driveBobOffset = 0f;
            driveBobOffsetVelocity = 0f;

            if (markerVisualRoot != null)
            {
                Vector2 finalRockOffset = CalculateFinalRockVisualOffset();
                markerVisualRoot.localPosition = new Vector3(
                    finalRockOffset.x,
                    finalRockOffset.y,
                    visualDepthOffset);
            }
        }

        private Vector2 CalculateFinalRockVisualOffset()
        {
            if (tumble == null
                || tumble.State != RobotTumbleState.FinalRocking)
            {
                return Vector2.zero;
            }

            Vector2 localDirection = new Vector2(
                Vector2.Dot(tumble.DirectionWorld, transform.right),
                Vector2.Dot(tumble.DirectionWorld, transform.up));
            if (localDirection.sqrMagnitude > 0.000001f)
                localDirection.Normalize();

            return localDirection
                   * VisualBodyDiameter
                   * finalRockVisualOffsetRatio
                   * tumble.FinalRockNormalizedOffset;
        }

        private void UpdateDirectionIndicatorSurfaceProjection()
        {
            if (directionIndicator == null)
                return;

            if (armController == null)
                armController = GetComponent<RobotArmController>();

            bool armModeActive = armController != null
                                 && armController.IsArmModeActive;
            float armVisibilityTarget = armModeActive ? 0f : 1f;
            float armFadeDuration = armModeActive
                ? indicatorArmModeFadeOutDuration
                : indicatorArmModeFadeInDuration;
            indicatorArmModeVisibility = Mathf.MoveTowards(
                indicatorArmModeVisibility,
                armVisibilityTarget,
                Mathf.Max(0f, Time.deltaTime)
                / Mathf.Max(0.01f, armFadeDuration));

            if (tumble == null)
                tumble = GetComponent<RobotTumbleController>();
            if (tumble == null || tumble.State == RobotTumbleState.Upright)
            {
                ApplyDirectionIndicatorProjection(
                    1f,
                    0f,
                    Vector2.zero,
                    1f);
                return;
            }

            float quarterTurnProgress = tumble.ContinuousQuarterTurnProgress;

            float surfaceAngleRadians = tumble.QuarterTurnSign
                                        * quarterTurnProgress
                                        * Mathf.PI
                                        * 0.5f;
            float signedSurfaceFacing = Mathf.Cos(surfaceAngleRadians);
            float visibility = Mathf.Pow(
                Mathf.Abs(signedSurfaceFacing),
                indicatorTumbleFadeExponent);
            float signedEdgeProjection = Mathf.Sin(surfaceAngleRadians)
                                         * tumble.QuarterTurnSign;
            float forwardDirectionSign = tumble.Axis
                                         == RobotTumbleAxis.ForwardBack
                                         && signedSurfaceFacing < 0f
                ? -1f
                : 1f;

            Vector2 localTumbleDirection = new Vector2(
                Vector2.Dot(tumble.DirectionWorld, transform.right),
                Vector2.Dot(tumble.DirectionWorld, transform.up));
            if (localTumbleDirection.sqrMagnitude > 0.000001f)
                localTumbleDirection.Normalize();

            ApplyDirectionIndicatorProjection(
                visibility,
                signedEdgeProjection,
                localTumbleDirection,
                forwardDirectionSign);
        }

        private void ApplyDirectionIndicatorProjection(
            float visibility,
            float edgeProjection,
            Vector2 localTumbleDirection,
            float forwardDirectionSign)
        {
            float safeVisibility = Mathf.Clamp01(visibility);
            float projectedScale = Mathf.Lerp(
                indicatorMinimumProjectedScale,
                1f,
                safeVisibility);
            Vector3 scale = Vector3.one * indicatorScale;
            if (tumble != null
                && tumble.State != RobotTumbleState.Upright
                && tumble.Axis == RobotTumbleAxis.ForwardBack)
            {
                scale.y *= projectedScale
                           * (forwardDirectionSign < 0f ? -1f : 1f);
            }
            else if (tumble != null
                     && tumble.State != RobotTumbleState.Upright)
            {
                scale.x *= projectedScale;
            }

            directionIndicator.transform.localScale = scale;
            Vector3 rotatedArtworkForward = Quaternion.Euler(
                0f,
                0f,
                indicatorRotationOffsetDegrees) * Vector3.up;
            Vector2 artworkForward = new Vector2(
                rotatedArtworkForward.x,
                rotatedArtworkForward.y);
            float visibleSurfaceDirectionSign = forwardDirectionSign < 0f
                ? -1f
                : 1f;
            Vector2 bodyInset = -artworkForward
                                * VisualBodyDiameter
                                * indicatorBodyInsetRatio
                                * visibleSurfaceDirectionSign;
            Vector2 tumbleEdgeOffset = localTumbleDirection
                                       * VisualBodyDiameter
                                       * indicatorTumbleEdgeOffsetRatio
                                       * Mathf.Clamp(
                                           edgeProjection,
                                           -1f,
                                           1f);
            directionIndicator.transform.localPosition =
                (Vector3)(bodyInset + tumbleEdgeOffset);
            Color projectedColor = indicatorColor;
            float combinedVisibility = safeVisibility
                                       * indicatorArmModeVisibility
                                       * GetPhotoModeIndicatorVisibility();
            projectedColor.a *= combinedVisibility;
            directionIndicator.color = projectedColor;
            directionIndicator.enabled = directionIndicator.sprite != null
                                         && combinedVisibility > 0.001f;
        }

        private float GetPhotoModeIndicatorVisibility()
        {
            if (photoMode == null)
                photoMode = GetComponent<PhotoModeController>();

            if (photoMode == null || !photoMode.IsActive)
                return 1f;

            // Entry and active photo mode hide the direction indicator in the
            // same frame as the mode switch. During exit it returns along with
            // the existing reverse camera-form animation.
            return photoMode.IsExiting
                ? 1f - photoCameraFormVisibility
                : 0f;
        }

        private void CreateRolloverSignRenderer()
        {
            var rolloverObject = new GameObject("Fallen Rollover Sign");
            rolloverObject.transform.SetParent(markerVisualRoot, false);

            rolloverSign = rolloverObject.AddComponent<SpriteRenderer>();
            rolloverSign.sprite = rolloverSignSprite;
            rolloverSign.color = rolloverSignColor;
            rolloverSign.sortingOrder = 1003;
            if (foregroundSpriteMaterial != null)
                rolloverSign.sharedMaterial = foregroundSpriteMaterial;

            if (rolloverSignSprite != null)
            {
                float visibleSpriteDiagonal = rolloverSignVisibleDiameterPixels
                                              * Mathf.Sqrt(2f)
                                              / Mathf.Max(
                                                  1f,
                                                  rolloverSignSprite.pixelsPerUnit);
                float targetDiameter = VisualBodyDiameter
                                       * rolloverSignDiameterRatio;
                rolloverObject.transform.localScale = Vector3.one
                                                     * (targetDiameter
                                                        / Mathf.Max(
                                                            0.0001f,
                                                            visibleSpriteDiagonal));
            }
            else
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing Arts/rollover_sign.",
                    this);
            }

            SynchronizeRolloverSignVisibility();
        }

        private void SynchronizeRolloverSignVisibility()
        {
            if (rolloverSign == null)
                return;

            if (tumble == null)
                tumble = GetComponent<RobotTumbleController>();
            rolloverSign.enabled = rolloverSign.sprite != null
                                   && tumble != null
                                   && tumble.State == RobotTumbleState.Fallen;
        }

        private void OnDestroy()
        {
            if (generatedBodySprite != null)
                Destroy(generatedBodySprite);

            if (generatedBodyTexture != null)
                Destroy(generatedBodyTexture);

            if (foregroundSpriteMaterial != null)
                Destroy(foregroundSpriteMaterial);
        }

        private void OnValidate()
        {
            bodyDiameter = Mathf.Max(0.1f, bodyDiameter);
            visualBodyDiameterRatio = Mathf.Clamp(
                visualBodyDiameterRatio,
                0.5f,
                1.25f);
            bodyArtworkVisibleDiameterPixels = Mathf.Max(1f, bodyArtworkVisibleDiameterPixels);
            bodyFillArtworkVisibleDiameterPixels = Mathf.Max(
                1f,
                bodyFillArtworkVisibleDiameterPixels);
            bodyScreenDiameterPixels = Mathf.Max(1f, bodyScreenDiameterPixels);
            bodyFillDiameterRatio = Mathf.Clamp(bodyFillDiameterRatio, 0.1f, 1f);
            indicatorScale = Mathf.Max(0.1f, indicatorScale);
            indicatorBodyInsetRatio = Mathf.Clamp(
                indicatorBodyInsetRatio,
                0f,
                0.5f);
            indicatorArmModeFadeOutDuration = Mathf.Max(
                0.01f,
                indicatorArmModeFadeOutDuration);
            indicatorArmModeFadeInDuration = Mathf.Max(
                0.01f,
                indicatorArmModeFadeInDuration);
            indicatorTumbleEdgeOffsetRatio = Mathf.Clamp(
                indicatorTumbleEdgeOffsetRatio,
                0f,
                0.5f);
            indicatorMinimumProjectedScale = Mathf.Clamp(
                indicatorMinimumProjectedScale,
                0f,
                0.25f);
            indicatorTumbleFadeExponent = Mathf.Clamp(
                indicatorTumbleFadeExponent,
                0.25f,
                3f);
            photoCameraArtworkScale = Mathf.Max(
                0.1f,
                photoCameraArtworkScale);
            photoCameraBodyOverlapRatio = Mathf.Clamp(
                photoCameraBodyOverlapRatio,
                0f,
                0.25f);
            photoCameraHeadRevealStart = Mathf.Clamp(
                photoCameraHeadRevealStart,
                0f,
                0.9f);
            rolloverSignDiameterRatio = Mathf.Max(
                0.1f,
                rolloverSignDiameterRatio);
            rolloverSignVisibleDiameterPixels = Mathf.Max(
                1f,
                rolloverSignVisibleDiameterPixels);
            finalRockVisualOffsetRatio = Mathf.Clamp(
                finalRockVisualOffsetRatio,
                0f,
                0.15f);
            driveBobAmplitude = Mathf.Max(0f, driveBobAmplitude);
            driveBobCyclesPerMeter = Mathf.Max(0f, driveBobCyclesPerMeter);
            driveBobFullStrengthSpeed = Mathf.Max(0.01f, driveBobFullStrengthSpeed);
            driveBobEnterSmoothing = Mathf.Max(0.01f, driveBobEnterSmoothing);
            driveBobExitSmoothing = Mathf.Max(0.01f, driveBobExitSmoothing);
            driveBobPositionSmoothing = Mathf.Max(0.005f, driveBobPositionSmoothing);
            driveBobAmplitudeRandomness = Mathf.Clamp(
                driveBobAmplitudeRandomness,
                0f,
                0.75f);
            driveBobFrequencyRandomness = Mathf.Clamp(
                driveBobFrequencyRandomness,
                0f,
                0.75f);
            driveBobNoiseAmplitudeRatio = Mathf.Clamp01(
                driveBobNoiseAmplitudeRatio);
            driveBobNoiseFrequency = Mathf.Max(0.01f, driveBobNoiseFrequency);
            levelTwoDriveBobMultiplier = Mathf.Max(0f, levelTwoDriveBobMultiplier);
            levelThreeGripDriveBobMultiplier = Mathf.Max(
                0f,
                levelThreeGripDriveBobMultiplier);
            if (!showDriveBob && markerVisualRoot != null)
                markerVisualRoot.localPosition = new Vector3(0f, 0f, visualDepthOffset);
            ApplyMotionTailVisibility();
        }
    }
}
