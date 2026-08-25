using System.Collections.Generic;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Deploys the player's biological radar and owns its deep-blue radial
    /// signal projectiles. Input gesture recognition remains in ScanChargeUI so
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
        [Tooltip("Screen-space shader that clips biological signal points and trails to the fixed circular player UI.")]
        [SerializeField] private Shader biologicalSignalClipShader;
        [SerializeField] private Color scannerArtworkColor = Color.white;
        [SerializeField, Min(0.05f)] private float mechanicalArmArtworkScale = 0.9f;
        [SerializeField, Min(0.05f)] private float radarArtworkScale = 0.72f;

        [Header("Charge-Synchronised Preparation")]
        [Tooltip("Charge progress at which the deep-blue ready points begin becoming visible around the deployed radar.")]
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
        [SerializeField, Range(8, 96)] private int signalPointCount = 36;
        [SerializeField, Min(0.01f)] private float formedRingRadius = 0.2f;
        [SerializeField, Min(0.1f)] private float signalSpeed = 10f;
        [SerializeField, Min(0.1f)] private float maximumSignalDistance = 12f;
        [Tooltip("Diameter of the solid leading point at the front of each biological signal.")]
        [InspectorName("Signal Head Diameter")]
        [SerializeField, Min(0.01f)] private float signalPointDiameter = 0.09f;
        [Tooltip("Fraction of the leading point occupied by its bright solid core.")]
        [SerializeField, Range(0.2f, 0.9f)]
        private float signalHeadCoreRatio = 0.5f;
        [Tooltip("Subtle scale pulse used to distinguish the leading point from its trail.")]
        [SerializeField, Range(0f, 0.25f)]
        private float signalHeadPulseAmount = 0.06f;
        [SerializeField, Min(0.1f)] private float signalHeadPulseFrequency = 4.5f;
        [SerializeField, Min(0.01f)] private float signalCollisionRadius = 0.14f;
        [Tooltip("How long emitted trail segments remain after the leading point stops.")]
        [SerializeField, Min(0.01f)] private float signalTrailDuration = 0.5f;
        [Tooltip("Trail width as a fraction of the leading point diameter.")]
        [SerializeField, Range(0.1f, 1f)] private float trailWidthRatio = 0.38f;
        [Tooltip("Visible length of one repeated trail dash, in world units.")]
        [SerializeField, Min(0.01f)] private float trailDashLength = 0.14f;
        [Tooltip("Empty gap after each trail dash, in world units.")]
        [SerializeField, Min(0.01f)] private float trailGapLength = 0.09f;
        [Tooltip("Softness of the circular player-UI clipping edge, in screen pixels.")]
        [SerializeField, Min(0f)] private float signalClipSoftnessPixels = 1.5f;
        [SerializeField, Min(0.1f)] private float temporaryRevealDuration = 5f;

        [Tooltip("Deep blue used by every biological signal point and its trail.")]
        [SerializeField] private Color biologicalSignalColor =
            new Color(0.055f, 0.2f, 0.72f, 1f);

        private readonly List<SignalProjectile> signalPool =
            new List<SignalProjectile>();
        private readonly List<SignalProjectile> formingSignals =
            new List<SignalProjectile>();

        private RobotMarkerView markerView;
        private ScanChargeUI scanInput;
        private Transform scannerRoot;
        private Transform armRevealRoot;
        private Transform radarTransform;
        private Transform signalOrigin;
        private Transform signalPoolRoot;
        private Material signalPointMaterial;
        private Material signalTrailMaterial;
        private Sprite generatedSignalSprite;
        private Texture2D generatedSignalTexture;
        private Texture2D generatedTrailTexture;
        private ScannerPhase phase;
        private float phaseElapsed;
        private float currentDeployment01;
        private float retractionStartDeployment01;
        private bool subscribed;

        public bool IsScanning => phase != ScannerPhase.Hidden;
        public bool IsCharging => phase == ScannerPhase.Charging;

        private sealed class SignalProjectile
        {
            public GameObject GameObject;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public TrailRenderer Trail;
            public Vector2 Direction;
            public Vector2 Position;
            public float DistanceTravelled;
            public bool IsActive;
            public bool IsLaunched;
            public bool IsTailFading;
            public float TailFadeRemaining;
            public float PulsePhase;
        }

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
            TickActiveSignals(Mathf.Max(0f, Time.deltaTime));

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
            if (formingSignals.Count == 0)
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
            armTransform.localScale = Vector3.one * mechanicalArmArtworkScale;
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
                              * mechanicalArmArtworkScale;
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
            Vector2 center = GetSignalOriginWorld();
            int pointCount = Mathf.Clamp(signalPointCount, 8, 96);
            for (int index = 0; index < pointCount; index++)
            {
                float angle = index / (float)pointCount * Mathf.PI * 2f;
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                SignalProjectile signal = GetSignalFromPool();
                PrepareSignalForFormation(signal);
                signal.Direction = direction;
                signal.Position = center;
                signal.DistanceTravelled = 0f;
                signal.PulsePhase = angle;
                signal.Transform.position = new Vector3(center.x, center.y, -0.22f);
                formingSignals.Add(signal);
            }
        }

        private void UpdateSignalFormation(
            float progress,
            float radiusMultiplier)
        {
            Vector2 center = GetSignalOriginWorld();
            progress = Mathf.Clamp01(progress);
            bool visible = progress > 0.001f;
            float radius = Mathf.SmoothStep(0f, 1f, progress)
                           * Mathf.Max(0.01f, formedRingRadius)
                           * Mathf.Max(0f, radiusMultiplier);
            for (int index = 0; index < formingSignals.Count; index++)
            {
                SignalProjectile signal = formingSignals[index];
                if (!signal.IsActive || signal.IsLaunched)
                    continue;

                if (signal.GameObject.activeSelf != visible)
                    signal.GameObject.SetActive(visible);
                signal.Position = center + signal.Direction * radius;
                signal.Transform.position = new Vector3(
                    signal.Position.x,
                    signal.Position.y,
                    -0.22f);
                signal.Transform.localScale = Vector3.one
                                              * (Mathf.Max(
                                                     0.01f,
                                                     signalPointDiameter)
                                                 * Mathf.Lerp(
                                                     0.35f,
                                                     1f,
                                                     progress));
            }
        }

        private void LaunchFormedSignals()
        {
            for (int index = 0; index < formingSignals.Count; index++)
            {
                SignalProjectile signal = formingSignals[index];
                if (!signal.IsActive)
                    continue;

                signal.IsLaunched = true;
                signal.IsTailFading = false;
                signal.Trail.Clear();
                signal.Trail.emitting = true;
            }
            formingSignals.Clear();
        }

        private void DeactivateFormingSignals()
        {
            for (int index = 0; index < formingSignals.Count; index++)
            {
                SignalProjectile signal = formingSignals[index];
                if (signal != null && signal.IsActive && !signal.IsLaunched)
                    RecycleSignalImmediately(signal);
            }
            formingSignals.Clear();
        }

        private void TickActiveSignals(float deltaTime)
        {
            float speed = Mathf.Max(0.1f, signalSpeed);
            for (int index = 0; index < signalPool.Count; index++)
            {
                SignalProjectile signal = signalPool[index];
                if (signal.IsTailFading)
                {
                    signal.TailFadeRemaining -= deltaTime;
                    if (signal.TailFadeRemaining <= 0f)
                        RecycleSignalImmediately(signal);
                    continue;
                }

                if (!signal.IsActive || !signal.IsLaunched)
                    continue;

                UpdateSignalHeadPulse(signal);

                Vector2 previous = signal.Position;
                float step = speed * deltaTime;
                Vector2 next = previous + signal.Direction * step;
                if (TryHitScanTarget(previous, next))
                {
                    BeginSignalTailFade(signal);
                    continue;
                }

                signal.Position = next;
                signal.DistanceTravelled += step;
                signal.Transform.position = new Vector3(next.x, next.y, -0.22f);
                if (signal.DistanceTravelled
                    >= Mathf.Max(0.1f, maximumSignalDistance))
                {
                    BeginSignalTailFade(signal);
                }
            }
        }

        private void UpdateSignalHeadPulse(SignalProjectile signal)
        {
            float pulse = 1f
                          + Mathf.Sin(
                              Time.time
                              * Mathf.Max(0.1f, signalHeadPulseFrequency)
                              * Mathf.PI
                              * 2f
                              + signal.PulsePhase)
                          * Mathf.Clamp(signalHeadPulseAmount, 0f, 0.25f);
            signal.Transform.localScale = Vector3.one
                                          * Mathf.Max(
                                              0.01f,
                                              signalPointDiameter)
                                          * pulse;
        }

        private bool TryHitScanTarget(Vector2 segmentStart, Vector2 segmentEnd)
        {
            float projectileRadius = Mathf.Max(0.01f, signalCollisionRadius);
            foreach (DiscoverableEntity entity in DiscoverableEntity.Active)
            {
                if (entity == null || !entity.isActiveAndEnabled)
                    continue;

                float targetRadius =
                    entity.GetBiologicalScanCollisionRadiusWorld();
                if (DistanceSquaredToSegment(
                        entity.transform.position,
                        segmentStart,
                        segmentEnd)
                    > Mathf.Pow(projectileRadius + targetRadius, 2f))
                {
                    continue;
                }

                entity.RevealTemporarily(temporaryRevealDuration);
                return true;
            }

            foreach (VegetationContactFade vegetation in
                     VegetationContactFade.Active)
            {
                if (vegetation == null || !vegetation.isActiveAndEnabled)
                    continue;

                float targetRadius =
                    vegetation.GetBiologicalScanCollisionRadiusWorld();
                if (DistanceSquaredToSegment(
                        vegetation.transform.position,
                        segmentStart,
                        segmentEnd)
                    <= Mathf.Pow(projectileRadius + targetRadius, 2f))
                {
                    // Plants consume the signal now; a future plant discovery
                    // presentation can be connected here without changing travel.
                    return true;
                }
            }

            return false;
        }

        private static float DistanceSquaredToSegment(
            Vector2 point,
            Vector2 segmentStart,
            Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.000001f)
                return (point - segmentStart).sqrMagnitude;

            float projection = Mathf.Clamp01(
                Vector2.Dot(point - segmentStart, segment) / denominator);
            Vector2 nearest = segmentStart + segment * projection;
            return (point - nearest).sqrMagnitude;
        }

        private Vector2 GetSignalOriginWorld()
        {
            Transform origin = signalOrigin != null ? signalOrigin : transform;
            return origin.position;
        }

        private SignalProjectile GetSignalFromPool()
        {
            for (int index = 0; index < signalPool.Count; index++)
            {
                if (!signalPool[index].IsActive)
                    return signalPool[index];
            }

            var signalObject = new GameObject(
                $"Biological Signal Point {signalPool.Count + 1}");
            signalObject.transform.SetParent(signalPoolRoot, false);
            var renderer = signalObject.AddComponent<SpriteRenderer>();
            renderer.sprite = generatedSignalSprite;
            renderer.color = biologicalSignalColor;
            renderer.sortingOrder = 1261;
            renderer.sharedMaterial = signalPointMaterial;
            renderer.transform.localScale = Vector3.one
                                            * Mathf.Max(
                                                0.01f,
                                                signalPointDiameter);

            var trail = signalObject.AddComponent<TrailRenderer>();
            trail.time = Mathf.Max(0.01f, signalTrailDuration);
            trail.minVertexDistance = 0.015f;
            trail.widthMultiplier = Mathf.Max(0.01f, signalPointDiameter)
                                    * Mathf.Clamp(trailWidthRatio, 0.1f, 1f);
            trail.sharedMaterial = signalTrailMaterial;
            trail.sortingOrder = 1259;
            trail.textureMode = LineTextureMode.Tile;
            trail.alignment = LineAlignment.View;
            trail.colorGradient = CreateTrailGradient();
            trail.emitting = false;

            var signal = new SignalProjectile
            {
                GameObject = signalObject,
                Transform = signalObject.transform,
                Renderer = renderer,
                Trail = trail
            };
            signalPool.Add(signal);
            signalObject.SetActive(false);
            return signal;
        }

        private void PrepareSignalForFormation(SignalProjectile signal)
        {
            signal.IsActive = true;
            signal.IsLaunched = false;
            signal.IsTailFading = false;
            signal.TailFadeRemaining = 0f;
            signal.Renderer.enabled = true;
            signal.Renderer.color = biologicalSignalColor;
            signal.Transform.localScale = Vector3.one
                                          * Mathf.Max(
                                              0.01f,
                                              signalPointDiameter);
            signal.Trail.time = Mathf.Max(0.01f, signalTrailDuration);
            signal.Trail.widthMultiplier = Mathf.Max(
                                               0.01f,
                                               signalPointDiameter)
                                           * Mathf.Clamp(
                                               trailWidthRatio,
                                               0.1f,
                                               1f);
            signal.Trail.colorGradient = CreateTrailGradient();
            signal.Trail.emitting = false;
            signal.Trail.Clear();
            signal.GameObject.SetActive(false);
        }

        private Gradient CreateTrailGradient()
        {
            var gradient = new Gradient();
            Color opaque = biologicalSignalColor;
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(opaque, 0f),
                    new GradientColorKey(opaque, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(opaque.a, 1f)
                });
            return gradient;
        }

        private void BeginSignalTailFade(SignalProjectile signal)
        {
            if (signal == null || !signal.IsActive)
                return;

            signal.IsLaunched = false;
            signal.IsTailFading = true;
            signal.TailFadeRemaining = Mathf.Max(
                0.01f,
                signalTrailDuration);
            if (signal.Renderer != null)
                signal.Renderer.enabled = false;
            if (signal.Trail != null)
                signal.Trail.emitting = false;
        }

        private void RecycleSignalImmediately(SignalProjectile signal)
        {
            if (signal == null)
                return;

            signal.IsActive = false;
            signal.IsLaunched = false;
            signal.IsTailFading = false;
            signal.TailFadeRemaining = 0f;
            if (signal.Renderer != null)
                signal.Renderer.enabled = true;
            if (signal.Trail != null)
            {
                signal.Trail.emitting = false;
                signal.Trail.Clear();
            }
            if (signal.GameObject != null)
                signal.GameObject.SetActive(false);
        }

        private void CreateSignalResources()
        {
            if (signalPoolRoot == null)
            {
                var poolObject = new GameObject("Biological Signal Pool");
                signalPoolRoot = poolObject.transform;
            }

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
                signalTrailMaterial = new Material(signalShader)
                {
                    name = "Runtime Biological Signal Dashed Trail Material"
                };
            }

            const int textureSize = 24;
            generatedSignalTexture = new Texture2D(
                textureSize,
                textureSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "Generated Biological Signal Point",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[textureSize * textureSize];
            Vector2 center = Vector2.one * ((textureSize - 1) * 0.5f);
            float radius = textureSize * 0.46f;
            float coreRadius = radius
                               * Mathf.Clamp(
                                   signalHeadCoreRatio,
                                   0.2f,
                                   0.9f);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        center);
                    float outerShape = 1f - Mathf.SmoothStep(
                        radius - 0.85f,
                        radius,
                        distance);
                    float core = 1f - Mathf.SmoothStep(
                        coreRadius - 0.65f,
                        coreRadius + 0.65f,
                        distance);
                    float alpha = Mathf.Max(
                        core,
                        outerShape * 0.52f);
                    float brightness = Mathf.Lerp(0.72f, 1f, core);
                    pixels[y * textureSize + x] = new Color(
                        brightness,
                        brightness,
                        brightness,
                        alpha);
                }
            }
            generatedSignalTexture.SetPixels(pixels);
            generatedSignalTexture.Apply(false, true);
            generatedSignalSprite = Sprite.Create(
                generatedSignalTexture,
                new Rect(0f, 0f, textureSize, textureSize),
                Vector2.one * 0.5f,
                textureSize);
            generatedSignalSprite.name = "Generated Biological Signal Point";

            const int dashTextureWidth = 64;
            const int dashTextureHeight = 4;
            generatedTrailTexture = new Texture2D(
                dashTextureWidth,
                dashTextureHeight,
                TextureFormat.RGBA32,
                false)
            {
                name = "Generated Biological Signal Dash Pattern",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            Color[] dashPixels = new Color[
                dashTextureWidth * dashTextureHeight];
            float dashFraction = trailDashLength
                                 / Mathf.Max(
                                     0.02f,
                                     trailDashLength + trailGapLength);
            for (int y = 0; y < dashTextureHeight; y++)
            {
                for (int x = 0; x < dashTextureWidth; x++)
                {
                    float patternPosition = (x + 0.5f) / dashTextureWidth;
                    float alpha = patternPosition <= dashFraction ? 1f : 0f;
                    dashPixels[y * dashTextureWidth + x] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }
            generatedTrailTexture.SetPixels(dashPixels);
            generatedTrailTexture.Apply(false, true);
            if (signalTrailMaterial != null)
            {
                signalTrailMaterial.mainTexture = generatedTrailTexture;
                float patternLength = Mathf.Max(
                    0.02f,
                    trailDashLength + trailGapLength);
                signalTrailMaterial.mainTextureScale = new Vector2(
                    1f / patternLength,
                    1f);
            }
            UpdateSignalClipMaterials();
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
            ApplySignalClip(
                signalTrailMaterial,
                centerPixels,
                radiusPixels);
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
            for (int index = 0; index < signalPool.Count; index++)
                RecycleSignalImmediately(signalPool[index]);
        }

        private void OnDestroy()
        {
            UnsubscribeFromInput();
            if (signalPoolRoot != null)
                Destroy(signalPoolRoot.gameObject);
            if (generatedSignalSprite != null)
                Destroy(generatedSignalSprite);
            if (generatedSignalTexture != null)
                Destroy(generatedSignalTexture);
            if (generatedTrailTexture != null)
                Destroy(generatedTrailTexture);
            if (signalPointMaterial != null)
                Destroy(signalPointMaterial);
            if (signalTrailMaterial != null)
                Destroy(signalTrailMaterial);
        }

        private void OnValidate()
        {
            mechanicalArmArtworkScale = Mathf.Max(
                0.05f,
                mechanicalArmArtworkScale);
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
            signalPointCount = Mathf.Clamp(signalPointCount, 8, 96);
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
            signalCollisionRadius = Mathf.Max(0.01f, signalCollisionRadius);
            signalTrailDuration = Mathf.Max(0.01f, signalTrailDuration);
            trailWidthRatio = Mathf.Clamp(trailWidthRatio, 0.1f, 1f);
            trailDashLength = Mathf.Max(0.01f, trailDashLength);
            trailGapLength = Mathf.Max(0.01f, trailGapLength);
            signalClipSoftnessPixels = Mathf.Max(
                0f,
                signalClipSoftnessPixels);
            temporaryRevealDuration = Mathf.Max(0.1f, temporaryRevealDuration);
        }
    }
}
