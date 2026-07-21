using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed class RobotMarkerView : MonoBehaviour
    {
        [Header("Body")]
        [SerializeField] private Sprite robotBodySprite;
        [SerializeField, Min(0.1f)] private float bodyDiameter = 0.72f;
        [Tooltip("Visible ring diameter in the source robot_body sprite, excluding transparent padding.")]
        [SerializeField, Min(1f)] private float bodyArtworkVisibleDiameterPixels = 85.4f;
        [SerializeField, Range(0.1f, 1f)] private float bodyFillDiameterRatio = 0.92f;
        [SerializeField] private Color bodyFillColor = new Color(0.008f, 0.011f, 0.014f, 0.94f);
        [SerializeField] private Color bodyOutlineColor = new Color(0.92f, 0.98f, 1f, 1f);

        [Header("Direction Indicator")]
        [SerializeField] private Sprite indicatorSprite;
        [SerializeField, Min(0.1f)] private float indicatorScale = 1.35f;
        [SerializeField] private float indicatorRotationOffsetDegrees;
        [SerializeField] private Color indicatorColor = new Color(0.92f, 0.98f, 1f, 1f);

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
        private LineRenderer tail;
        private Sprite generatedBodySprite;
        private Texture2D generatedBodyTexture;
        private RobotMover mover;
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
            driveBobRandom = new System.Random(
                unchecked(GetInstanceID() * 397 ^ System.Environment.TickCount));
            driveBobNoiseSeed = NextDriveBobRandom(0f, 1000f);
            RandomizeDriveBobCycle();
            CreateMarkerVisualRoot();
            CreateBodySpriteRenderer();
            CreateDirectionIndicatorRenderer();

            tail = RobotMapDemo.CreateLine(transform, "Motion Tail", new[]
            {
                new Vector3(0f, -0.38f), new Vector3(0f, -0.38f)
            }, 0.05f, new Color(0.35f, 0.82f, 0.9f, 0.7f), 18);
            ApplyMotionTailVisibility();
        }

        public bool ShowMotionTail => showMotionTail;
        public bool ShowDriveBob => showDriveBob;

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
            float pulse = 1f + Mathf.Sin(Time.time * 3.2f) * 0.035f;
            if (bodyVisualRoot != null)
                bodyVisualRoot.localScale = Vector3.one * pulse;

            UpdateDriveBob();
            if (showMotionTail && tail != null && mover != null)
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
            markerVisualRoot.localPosition = Vector3.up * driveBobOffset;
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
                tail.enabled = showMotionTail;
        }

        private void CreateMarkerVisualRoot()
        {
            var visualRootObject = new GameObject("Marker Visual Root");
            visualRootObject.transform.SetParent(transform, false);
            markerVisualRoot = visualRootObject.transform;
        }

        private void CreateBodySpriteRenderer()
        {
            var bodyVisualObject = new GameObject("Body Visual");
            bodyVisualObject.transform.SetParent(markerVisualRoot, false);
            bodyVisualRoot = bodyVisualObject.transform;

            generatedBodySprite = CreateCircularFillSprite(out generatedBodyTexture);

            var fillObject = new GameObject("Body Fill");
            fillObject.transform.SetParent(bodyVisualRoot, false);
            bodyFill = fillObject.AddComponent<SpriteRenderer>();
            bodyFill.sprite = generatedBodySprite;
            bodyFill.color = bodyFillColor;
            bodyFill.sortingOrder = 19;
            bodyFill.transform.localScale = Vector3.one * (bodyDiameter * bodyFillDiameterRatio);

            var artworkObject = new GameObject("Body Artwork");
            artworkObject.transform.SetParent(bodyVisualRoot, false);
            bodyArtwork = artworkObject.AddComponent<SpriteRenderer>();
            bodyArtwork.sprite = robotBodySprite;
            bodyArtwork.color = bodyOutlineColor;
            bodyArtwork.sortingOrder = 20;

            if (robotBodySprite != null)
            {
                float artworkDiameter = bodyArtworkVisibleDiameterPixels /
                    Mathf.Max(1f, robotBodySprite.pixelsPerUnit);
                float artworkScale = bodyDiameter / artworkDiameter;
                bodyArtwork.transform.localScale = Vector3.one * artworkScale;
            }
            else
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing its Arts/robot_body Sprite.",
                    this);
            }
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
            directionIndicator.sortingOrder = 21;

            if (indicatorSprite == null)
            {
                Debug.LogWarning(
                    "RobotMarkerView is missing its direction Indicator Sprite.",
                    this);
            }
        }

        private static Sprite CreateCircularFillSprite(out Texture2D texture)
        {
            const int Resolution = 128;
            const float OuterFadeStart = 0.94f;

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

        private void OnDestroy()
        {
            if (generatedBodySprite != null)
                Destroy(generatedBodySprite);

            if (generatedBodyTexture != null)
                Destroy(generatedBodyTexture);
        }

        private void OnValidate()
        {
            bodyDiameter = Mathf.Max(0.1f, bodyDiameter);
            bodyArtworkVisibleDiameterPixels = Mathf.Max(1f, bodyArtworkVisibleDiameterPixels);
            bodyFillDiameterRatio = Mathf.Clamp(bodyFillDiameterRatio, 0.1f, 1f);
            indicatorScale = Mathf.Max(0.1f, indicatorScale);
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
                markerVisualRoot.localPosition = Vector3.zero;
            ApplyMotionTailVisibility();
        }
    }
}
