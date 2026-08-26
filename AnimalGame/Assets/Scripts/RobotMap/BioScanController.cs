using System;
using System.Collections.Generic;
using AnimalGame.Discovery;
using UnityEngine;
using UnityEngine.Serialization;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Deploys the player's biological radar and owns its radial signal points.
    /// Input gesture recognition remains in ScanChargeUI so
    /// LB has one authoritative short-tap/long-hold router.
    /// </summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMarkerView))]
    [AddComponentMenu("Animal Game/Robot/Biological Scan Controller")]
    public sealed class BioScanController : MonoBehaviour
    {
        private static readonly int ClipCenterPixelsProperty =
            Shader.PropertyToID("_ClipCenterPixels");
        private static readonly int ClipRadiusPixelsProperty =
            Shader.PropertyToID("_ClipRadiusPixels");
        private static readonly int ClipSoftnessPixelsProperty =
            Shader.PropertyToID("_ClipSoftnessPixels");
        private static readonly int PointCoreRatioProperty =
            Shader.PropertyToID("_PointCoreRatio");

        private enum ScannerPhase
        {
            Hidden,
            Charging,
            Holding,
            Retracting
        }

        private const float ArmVisibleLengthPixels = 37f;
        private const float ArmLowerPaddingPixels = 3.5f;
        private const float RadarVisibleHeightPixels = 46f;

        [Header("Scanner Artwork")]
        [Tooltip("Arts/Robot_Arm_2, used as the extending connector between the robot and radar detector.")]
        [SerializeField] private Sprite mechanicalArmSprite;
        [Tooltip("Arts/robot_bioradar, displayed at the end of the mechanical arm.")]
        [SerializeField] private Sprite biologicalRadarSprite;
        [Tooltip("Procedural point shader that clips biological signals to the fixed circular player UI.")]
        [SerializeField] private Shader biologicalSignalClipShader;
        [SerializeField] private Color scannerArtworkColor = Color.white;
        [SerializeField, Min(0.05f)] private float mechanicalArmArtworkScale = 0.9f;
        [Tooltip("Length multiplier for the biological radar arm. This shortens the arm without changing its width.")]
        [InspectorName("Mechanical Arm Length")]
        [SerializeField, Range(0.2f, 1.5f)]
        private float mechanicalArmLengthMultiplier = 0.68f;
        [SerializeField, Min(0.05f)] private float radarArtworkScale = 0.72f;

        [Header("Charge-Synchronised Preparation")]
        [Tooltip("Charge progress at which the ready points begin becoming visible around the deployed radar.")]
        [SerializeField, Range(0f, 0.95f)]
        private float signalFormationStartCharge01 = 0.62f;
        [Tooltip("Fractional radius pulse of the ready-point ring while a fully charged scan is still held.")]
        [SerializeField, Range(0f, 0.3f)]
        private float readyRingPulseAmplitude = 0.07f;
        [Tooltip("Ready-point ring pulse frequency while fully charged, in cycles per second.")]
        [SerializeField, Min(0.1f)] private float readyRingPulseFrequency = 2.5f;
        [SerializeField, Min(0f)] private float deployedHoldDuration = 0.12f;
        [SerializeField, Min(0.05f)] private float retractionDuration = 0.25f;

        [Header("Biological Signal")]
        [Tooltip("Minimum number of evenly distributed signal points in one 360-degree scan.")]
        [InspectorName("Minimum Point Count")]
        [SerializeField, Range(24, 360)] private int signalPointCount = 240;
        [Tooltip("Automatically adds enough points to keep the outer edge of the scan densely covered.")]
        [SerializeField] private bool automaticAngularCoverage = true;
        [Tooltip("Safety cap for automatically calculated point density.")]
        [SerializeField, Range(24, 720)] private int maximumPointCount = 360;
        [Tooltip("How much neighbouring points overlap at the scan's maximum radius.")]
        [SerializeField, Range(0f, 0.75f)]
        private float pointCoverageOverlap = 0.1f;
        [SerializeField, Min(0.01f)] private float formedRingRadius = 0.2f;
        [SerializeField, Min(0.1f)] private float signalSpeed = 10f;
        [SerializeField, Min(0.1f)] private float maximumSignalDistance = 12f;
        [Tooltip("Diameter of the solid leading point at the front of each biological signal.")]
        [InspectorName("Signal Head Diameter")]
        [SerializeField, Min(0.01f)] private float signalPointDiameter = 0.09f;
        [Tooltip("Fraction of the leading point occupied by its bright solid core.")]
        [SerializeField, Range(0.2f, 0.9f)]
        private float signalHeadCoreRatio = 0.5f;
        [Tooltip("Subtle scale pulse used while the ready-point ring is charging.")]
        [SerializeField, Range(0f, 0.25f)]
        private float signalHeadPulseAmount = 0.06f;
        [SerializeField, Min(0.1f)] private float signalHeadPulseFrequency = 4.5f;
        [Tooltip("Half-thickness of the invisible expanding detection wave. Points never collide or stop.")]
        [FormerlySerializedAs("signalCollisionRadius")]
        [SerializeField, Min(0.01f)] private float scanWaveHalfThickness = 0.14f;
        [Tooltip("Softness of the circular player-UI clipping edge, in screen pixels.")]
        [SerializeField, Min(0f)] private float signalClipSoftnessPixels = 1.5f;
        [SerializeField, Min(0.1f)] private float temporaryRevealDuration = 5f;

        [Tooltip("Color used by every biological signal point.")]
        [SerializeField] private Color biologicalSignalColor =
            new Color(0.055f, 0.2f, 0.72f, 1f);

        private readonly HashSet<int> revealedEntityIds = new HashSet<int>();

        private RobotMarkerView markerView;
        private ScanChargeUI scanInput;
        private Transform scannerRoot;
        private Transform armRevealRoot;
        private Transform radarTransform;
        private Transform signalOrigin;
        private Transform signalParticleRoot;
        private ParticleSystem formingPointSystem;
        private ParticleSystem emittedPointSystem;
        private ParticleSystem.Particle[] formationParticles =
            Array.Empty<ParticleSystem.Particle>();
        private Material signalPointMaterial;
        private int formingPointCount;
        private bool signalFormationActive;
        private Vector2 scanWaveOrigin;
        private float scanWaveRadius;
        private bool scanWaveActive;
        private ScannerPhase phase;
        private float phaseElapsed;
        private float currentDeployment01;
        private float retractionStartDeployment01;
        private bool subscribed;

        public bool IsScanning => phase != ScannerPhase.Hidden;
        public bool IsCharging => phase == ScannerPhase.Charging;

        private void Awake()
        {
            markerView = GetComponent<RobotMarkerView>();
            CreateSignalResources();
        }

        private void Start()
        {
            TryCreateScannerVisuals();
        }

        private void OnEnable()
        {
            SubscribeToInput();
        }

        public void Initialize(ScanChargeUI input)
        {
            if (scanInput == input)
            {
                SubscribeToInput();
                return;
            }

            UnsubscribeFromInput();
            scanInput = input;
            SubscribeToInput();
        }

        private void Update()
        {
            float visualDeltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            UpdateSignalClipMaterials();
            TickScanWave(Mathf.Max(0f, Time.deltaTime));

            if (phase == ScannerPhase.Hidden)
                return;

            if (scannerRoot == null && !TryCreateScannerVisuals())
            {
                phase = ScannerPhase.Hidden;
                return;
            }

            phaseElapsed += visualDeltaTime;
            switch (phase)
            {
                case ScannerPhase.Charging:
                {
                    float charge01 = scanInput != null
                        ? scanInput.Charge01
                        : 0f;
                    ApplyDeployment(charge01);
                    float formation01 = Mathf.InverseLerp(
                        signalFormationStartCharge01,
                        1f,
                        charge01);
                    float radiusMultiplier = charge01 >= 0.999f
                        ? 1f
                          + Mathf.Sin(
                              Time.unscaledTime
                              * Mathf.Max(0.1f, readyRingPulseFrequency)
                              * Mathf.PI
                              * 2f)
                          * readyRingPulseAmplitude
                        : 1f;
                    UpdateSignalFormation(formation01, radiusMultiplier);
                    break;
                }

                case ScannerPhase.Holding:
                    if (phaseElapsed >= Mathf.Max(0f, deployedHoldDuration))
                        BeginRetraction();
                    break;

                case ScannerPhase.Retracting:
                {
                    float progress = Mathf.Clamp01(
                        phaseElapsed / Mathf.Max(0.05f, retractionDuration));
                    ApplyDeployment(Mathf.Lerp(
                        retractionStartDeployment01,
                        0f,
                        Mathf.SmoothStep(0f, 1f, progress)));
                    if (progress >= 1f)
                    {
                        phase = ScannerPhase.Hidden;
                        scannerRoot.gameObject.SetActive(false);
                    }
                    break;
                }
            }
        }

        private void BeginBiologicalScanCharge()
        {
            if (!isActiveAndEnabled)
                return;

            if (!TryCreateScannerVisuals())
                return;

            DeactivateFormingSignals();
            scannerRoot.gameObject.SetActive(true);
            ApplyDeployment(0f);
            BeginSignalFormation();
            phase = ScannerPhase.Charging;
            phaseElapsed = 0f;
        }

        private void CancelBiologicalScanCharge()
        {
            if (phase != ScannerPhase.Charging)
                return;

            DeactivateFormingSignals();
            BeginRetraction();
        }

        private void ReleaseFullyChargedBiologicalScan()
        {
            if (!isActiveAndEnabled || !TryCreateScannerVisuals())
                return;

            scannerRoot.gameObject.SetActive(true);
            if (!signalFormationActive)
                BeginSignalFormation();
            ApplyDeployment(1f);
            UpdateSignalFormation(1f, 1f);
            LaunchFormedSignals();
            phase = ScannerPhase.Holding;
            phaseElapsed = 0f;
        }

        private void BeginRetraction()
        {
            retractionStartDeployment01 = currentDeployment01;
            phase = ScannerPhase.Retracting;
            phaseElapsed = 0f;
        }

        private bool TryCreateScannerVisuals()
        {
            if (scannerRoot != null)
                return true;

            if (markerView == null)
                markerView = GetComponent<RobotMarkerView>();
            if (markerView == null || markerView.MarkerVisualRoot == null)
                return false;
            if (mechanicalArmSprite == null || biologicalRadarSprite == null)
            {
                Debug.LogError(
                    "Biological Scan Controller is missing its arm or radar artwork.",
                    this);
                return false;
            }

            var rootObject = new GameObject("Biological Scanner Rig");
            scannerRoot = rootObject.transform;
            scannerRoot.SetParent(markerView.MarkerVisualRoot, false);
            scannerRoot.localPosition = Vector3.up
                                        * (markerView.VisualBodyDiameter * 0.48f);

            var armRevealObject = new GameObject("Mechanical Arm Reveal");
            armRevealRoot = armRevealObject.transform;
            armRevealRoot.SetParent(scannerRoot, false);

            var armObject = new GameObject("Mechanical Arm");
            Transform armTransform = armObject.transform;
            armTransform.SetParent(armRevealRoot, false);
            float armPixelsPerUnit = Mathf.Max(
                1f,
                mechanicalArmSprite.pixelsPerUnit);
            armTransform.localPosition = Vector3.up
                                         * (ArmLowerPaddingPixels
                                            / armPixelsPerUnit
                                            * mechanicalArmArtworkScale);
            armTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            armTransform.localScale = new Vector3(
                mechanicalArmArtworkScale,
                mechanicalArmArtworkScale
                * Mathf.Clamp(mechanicalArmLengthMultiplier, 0.2f, 1.5f),
                mechanicalArmArtworkScale);
            SpriteRenderer armRenderer = armObject.AddComponent<SpriteRenderer>();
            ConfigureScannerRenderer(armRenderer, mechanicalArmSprite, 1240);

            var radarObject = new GameObject("Biological Radar Detector");
            radarTransform = radarObject.transform;
            radarTransform.SetParent(scannerRoot, false);
            SpriteRenderer radarRenderer =
                radarObject.AddComponent<SpriteRenderer>();
            ConfigureScannerRenderer(radarRenderer, biologicalRadarSprite, 1241);

            var signalOriginObject = new GameObject("Biological Signal Origin");
            signalOrigin = signalOriginObject.transform;
            signalOrigin.SetParent(scannerRoot, false);

            scannerRoot.gameObject.SetActive(false);
            return true;
        }

        private void ConfigureScannerRenderer(
            SpriteRenderer renderer,
            Sprite sprite,
            int sortingOrder)
        {
            renderer.sprite = sprite;
            renderer.color = scannerArtworkColor;
            renderer.sortingOrder = sortingOrder;
            if (markerView.ForegroundSpriteMaterial != null)
                renderer.sharedMaterial = markerView.ForegroundSpriteMaterial;
        }

        private void ApplyDeployment(float deployment01)
        {
            deployment01 = Mathf.Clamp01(deployment01);
            currentDeployment01 = deployment01;
            float armProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.68f, deployment01));
            float radarProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.42f, 1f, deployment01));

            float armPixelsPerUnit = mechanicalArmSprite != null
                ? Mathf.Max(1f, mechanicalArmSprite.pixelsPerUnit)
                : 100f;
            float radarPixelsPerUnit = biologicalRadarSprite != null
                ? Mathf.Max(1f, biologicalRadarSprite.pixelsPerUnit)
                : 100f;
            float armLength = ArmVisibleLengthPixels
                              / armPixelsPerUnit
                              * mechanicalArmArtworkScale
                              * Mathf.Clamp(
                                  mechanicalArmLengthMultiplier,
                                  0.2f,
                                  1.5f);
            float radarHeight = RadarVisibleHeightPixels
                                / radarPixelsPerUnit
                                * radarArtworkScale;

            armRevealRoot.localScale = new Vector3(1f, armProgress, 1f);
            radarTransform.localPosition = Vector3.up * (armLength * armProgress);
            radarTransform.localScale = Vector3.one
                                        * (radarArtworkScale * radarProgress);
            signalOrigin.localPosition = Vector3.up
                                         * (armLength * armProgress
                                            + radarHeight
                                            * radarProgress
                                            * 0.42f);
        }

        private void BeginSignalFormation()
        {
            DeactivateFormingSignals();
            if (formingPointSystem == null)
                CreateSignalResources();

            formingPointCount = CalculateEffectivePointCount();
            if (formationParticles.Length < formingPointCount)
                Array.Resize(ref formationParticles, formingPointCount);

            signalFormationActive = formingPointSystem != null;
            if (formingPointSystem != null)
            {
                formingPointSystem.Clear(true);
                if (!formingPointSystem.isPlaying)
                    formingPointSystem.Play(true);
            }
        }

        private void UpdateSignalFormation(
            float progress,
            float radiusMultiplier)
        {
            if (!signalFormationActive || formingPointSystem == null)
                return;

            Vector2 center = GetSignalOriginWorld();
            progress = Mathf.Clamp01(progress);
            if (progress <= 0.001f)
            {
                formingPointSystem.Clear(true);
                return;
            }

            float radius = Mathf.SmoothStep(0f, 1f, progress)
                           * Mathf.Max(0.01f, formedRingRadius)
                           * Mathf.Max(0f, radiusMultiplier);
            float baseSize = Mathf.Max(0.01f, signalPointDiameter)
                             * Mathf.Lerp(0.35f, 1f, progress);
            float pulseTime = Time.unscaledTime
                              * Mathf.Max(0.1f, signalHeadPulseFrequency)
                              * Mathf.PI
                              * 2f;
            Color32 pointColor = biologicalSignalColor;
            for (int index = 0; index < formingPointCount; index++)
            {
                float angle01 = index / (float)formingPointCount;
                float angle = angle01 * Mathf.PI * 2f;
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                float pulse = 1f
                              + Mathf.Sin(pulseTime + angle)
                              * Mathf.Clamp(signalHeadPulseAmount, 0f, 0.25f);
                formationParticles[index].position = new Vector3(
                    center.x + direction.x * radius,
                    center.y + direction.y * radius,
                    -0.22f);
                formationParticles[index].velocity = Vector3.zero;
                formationParticles[index].startLifetime = 1000f;
                formationParticles[index].remainingLifetime = 1000f;
                formationParticles[index].startSize = baseSize * pulse;
                formationParticles[index].startColor = pointColor;
            }

            formingPointSystem.SetParticles(
                formationParticles,
                formingPointCount);
        }

        private void LaunchFormedSignals()
        {
            if (emittedPointSystem == null)
                CreateSignalResources();
            if (emittedPointSystem == null)
            {
                DeactivateFormingSignals();
                return;
            }

            if (!emittedPointSystem.isPlaying)
                emittedPointSystem.Play(true);

            Vector2 center = GetSignalOriginWorld();
            float initialRadius = Mathf.Max(0.01f, formedRingRadius);
            float speed = Mathf.Max(0.1f, signalSpeed);
            float lifetime = Mathf.Max(0.1f, maximumSignalDistance) / speed;
            int pointCount = signalFormationActive && formingPointCount > 0
                ? formingPointCount
                : CalculateEffectivePointCount();
            var emit = new ParticleSystem.EmitParams
            {
                startLifetime = lifetime,
                startSize = Mathf.Max(0.01f, signalPointDiameter),
                startColor = biologicalSignalColor
            };
            for (int index = 0; index < pointCount; index++)
            {
                float angle = index / (float)pointCount * Mathf.PI * 2f;
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                emit.position = new Vector3(
                    center.x + direction.x * initialRadius,
                    center.y + direction.y * initialRadius,
                    -0.22f);
                emit.velocity = new Vector3(
                    direction.x * speed,
                    direction.y * speed,
                    0f);
                emittedPointSystem.Emit(emit, 1);
            }

            scanWaveOrigin = center;
            scanWaveRadius = initialRadius;
            scanWaveActive = true;
            revealedEntityIds.Clear();
            RevealTargetsCrossedByWave(0f, initialRadius);
            DeactivateFormingSignals();
        }

        private void DeactivateFormingSignals()
        {
            signalFormationActive = false;
            formingPointCount = 0;
            if (formingPointSystem != null)
                formingPointSystem.Clear(true);
        }

        private int CalculateEffectivePointCount()
        {
            int maximum = Mathf.Clamp(maximumPointCount, 24, 720);
            int minimum = Mathf.Clamp(signalPointCount, 24, maximum);
            if (!automaticAngularCoverage)
                return minimum;

            float outerRadius = Mathf.Max(0.01f, formedRingRadius)
                                + Mathf.Max(0.1f, maximumSignalDistance);
            float spacing = Mathf.Max(0.01f, signalPointDiameter)
                            * Mathf.Max(
                                0.05f,
                                1f - Mathf.Clamp01(pointCoverageOverlap));
            int required = Mathf.CeilToInt(
                Mathf.PI * 2f * outerRadius / spacing);
            return Mathf.Clamp(Mathf.Max(minimum, required), 24, maximum);
        }

        private void TickScanWave(float deltaTime)
        {
            if (!scanWaveActive || deltaTime <= 0f)
                return;

            float previousRadius = scanWaveRadius;
            float maximumRadius = Mathf.Max(0.01f, formedRingRadius)
                                  + Mathf.Max(0.1f, maximumSignalDistance);
            scanWaveRadius = Mathf.Min(
                maximumRadius,
                scanWaveRadius + Mathf.Max(0.1f, signalSpeed) * deltaTime);
            RevealTargetsCrossedByWave(previousRadius, scanWaveRadius);
            if (scanWaveRadius >= maximumRadius - 0.0001f)
                scanWaveActive = false;
        }

        private void RevealTargetsCrossedByWave(
            float previousRadius,
            float currentRadius)
        {
            float thickness = Mathf.Max(0.01f, scanWaveHalfThickness);
            float innerRadius = Mathf.Max(0f, previousRadius - thickness);
            float outerRadius = currentRadius + thickness;
            foreach (DiscoverableEntity entity in DiscoverableEntity.Active)
            {
                if (entity == null || !entity.isActiveAndEnabled)
                    continue;
                int entityId = entity.GetInstanceID();
                if (revealedEntityIds.Contains(entityId))
                    continue;

                float targetRadius = Mathf.Max(
                    0f,
                    entity.GetBiologicalScanCollisionRadiusWorld());
                float distance = Vector2.Distance(
                    scanWaveOrigin,
                    entity.transform.position);
                if (distance + targetRadius < innerRadius
                    || distance - targetRadius > outerRadius)
                    continue;
                revealedEntityIds.Add(entityId);
                entity.RevealTemporarily(temporaryRevealDuration);
            }
        }

        private Vector2 GetSignalOriginWorld()
        {
            Transform origin = signalOrigin != null ? signalOrigin : transform;
            return origin.position;
        }

        private void CreateSignalResources()
        {
            if (formingPointSystem != null && emittedPointSystem != null)
                return;

            Shader signalShader = biologicalSignalClipShader != null
                ? biologicalSignalClipShader
                : Shader.Find("AnimalGame/Biological Signal UI Clip");
            if (signalShader == null)
            {
                Debug.LogError(
                    "Biological Scan Controller could not find its signal clipping shader.",
                    this);
                signalShader = Shader.Find("Sprites/Default");
            }
            if (signalShader != null)
            {
                signalPointMaterial = new Material(signalShader)
                {
                    name = "Runtime Biological Signal Point Material"
                };
                signalPointMaterial.SetFloat(
                    PointCoreRatioProperty,
                    Mathf.Clamp(signalHeadCoreRatio, 0.2f, 0.9f));
            }

            var rootObject = new GameObject("Biological Signal Particles");
            signalParticleRoot = rootObject.transform;
            formingPointSystem = CreatePointParticleSystem(
                "Biological Signal Ready Ring",
                Mathf.Clamp(maximumPointCount, 24, 720));
            emittedPointSystem = CreatePointParticleSystem(
                "Biological Signal Emitted Points",
                Mathf.Max(2048, Mathf.Clamp(maximumPointCount, 24, 720) * 8));
            UpdateSignalClipMaterials();
        }

        private ParticleSystem CreatePointParticleSystem(
            string systemName,
            int maxParticles)
        {
            var systemObject = new GameObject(systemName);
            systemObject.transform.SetParent(signalParticleRoot, false);
            var system = systemObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startColor = Color.white;
            main.maxParticles = Mathf.Max(24, maxParticles);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = false;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 1261;
            renderer.sharedMaterial = signalPointMaterial;
            system.Play(true);
            return system;
        }

        private void UpdateSignalClipMaterials()
        {
            Vector2 centerPixels = scanInput != null
                ? scanInput.GetUiCenterScreenPoint()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float radiusPixels = scanInput != null
                ? scanInput.GetUiRingScreenRadiusPixels()
                : Mathf.Min(Screen.width, Screen.height) * 0.5f;
            ApplySignalClip(
                signalPointMaterial,
                centerPixels,
                radiusPixels);
            if (signalPointMaterial != null)
            {
                signalPointMaterial.SetFloat(
                    PointCoreRatioProperty,
                    Mathf.Clamp(signalHeadCoreRatio, 0.2f, 0.9f));
            }
        }

        private void ApplySignalClip(
            Material material,
            Vector2 centerPixels,
            float radiusPixels)
        {
            if (material == null)
                return;

            material.SetVector(
                ClipCenterPixelsProperty,
                new Vector4(centerPixels.x, centerPixels.y, 0f, 0f));
            material.SetFloat(
                ClipRadiusPixelsProperty,
                Mathf.Max(0f, radiusPixels));
            material.SetFloat(
                ClipSoftnessPixelsProperty,
                Mathf.Max(0f, signalClipSoftnessPixels));
        }

        private void SubscribeToInput()
        {
            if (subscribed || scanInput == null || !isActiveAndEnabled)
                return;

            scanInput.BiologicalScanChargeStarted +=
                BeginBiologicalScanCharge;
            scanInput.BiologicalScanChargeCancelled +=
                CancelBiologicalScanCharge;
            scanInput.FullyChargedBiologicalScanReleased +=
                ReleaseFullyChargedBiologicalScan;
            subscribed = true;
        }

        private void UnsubscribeFromInput()
        {
            if (!subscribed || scanInput == null)
                return;

            scanInput.BiologicalScanChargeStarted -=
                BeginBiologicalScanCharge;
            scanInput.BiologicalScanChargeCancelled -=
                CancelBiologicalScanCharge;
            scanInput.FullyChargedBiologicalScanReleased -=
                ReleaseFullyChargedBiologicalScan;
            subscribed = false;
        }

        private void OnDisable()
        {
            UnsubscribeFromInput();
            phase = ScannerPhase.Hidden;
            DeactivateFormingSignals();
            if (scannerRoot != null)
                scannerRoot.gameObject.SetActive(false);
            if (emittedPointSystem != null)
                emittedPointSystem.Clear(true);
            scanWaveActive = false;
            revealedEntityIds.Clear();
        }

        private void OnDestroy()
        {
            UnsubscribeFromInput();
            if (signalParticleRoot != null)
                Destroy(signalParticleRoot.gameObject);
            if (signalPointMaterial != null)
                Destroy(signalPointMaterial);
        }

        private void OnValidate()
        {
            mechanicalArmArtworkScale = Mathf.Max(
                0.05f,
                mechanicalArmArtworkScale);
            mechanicalArmLengthMultiplier = Mathf.Clamp(
                mechanicalArmLengthMultiplier,
                0.2f,
                1.5f);
            radarArtworkScale = Mathf.Max(0.05f, radarArtworkScale);
            signalFormationStartCharge01 = Mathf.Clamp(
                signalFormationStartCharge01,
                0f,
                0.95f);
            readyRingPulseAmplitude = Mathf.Clamp(
                readyRingPulseAmplitude,
                0f,
                0.3f);
            readyRingPulseFrequency = Mathf.Max(
                0.1f,
                readyRingPulseFrequency);
            deployedHoldDuration = Mathf.Max(0f, deployedHoldDuration);
            retractionDuration = Mathf.Max(0.05f, retractionDuration);
            maximumPointCount = Mathf.Clamp(maximumPointCount, 24, 720);
            signalPointCount = Mathf.Clamp(
                signalPointCount,
                24,
                maximumPointCount);
            pointCoverageOverlap = Mathf.Clamp(
                pointCoverageOverlap,
                0f,
                0.75f);
            formedRingRadius = Mathf.Max(0.01f, formedRingRadius);
            signalSpeed = Mathf.Max(0.1f, signalSpeed);
            maximumSignalDistance = Mathf.Max(0.1f, maximumSignalDistance);
            signalPointDiameter = Mathf.Max(0.01f, signalPointDiameter);
            signalHeadCoreRatio = Mathf.Clamp(
                signalHeadCoreRatio,
                0.2f,
                0.9f);
            signalHeadPulseAmount = Mathf.Clamp(
                signalHeadPulseAmount,
                0f,
                0.25f);
            signalHeadPulseFrequency = Mathf.Max(
                0.1f,
                signalHeadPulseFrequency);
            scanWaveHalfThickness = Mathf.Max(0.01f, scanWaveHalfThickness);
            signalClipSoftnessPixels = Mathf.Max(
                0f,
                signalClipSoftnessPixels);
            temporaryRevealDuration = Mathf.Max(0.1f, temporaryRevealDuration);
        }
    }
}
