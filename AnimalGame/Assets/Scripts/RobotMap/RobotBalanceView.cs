using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotBalanceController))]
    public sealed class RobotBalanceView : MonoBehaviour
    {
        public const string BalanceCanvasName = "Robot Balance UI";
        public float ControlRingRadiusPixels => controlRingDiameterPixels * 0.5f;

        [Header("Visibility")]
        [SerializeField] private bool showBalanceDisplay = true;
        [SerializeField] private Color ringColor = new Color(0.92f, 0.98f, 1f, 1f);
        [SerializeField] private Color lineColor = new Color(0.92f, 0.98f, 1f, 1f);
        [SerializeField] private Color pointColor = new Color(0.92f, 0.98f, 1f, 1f);

        [Tooltip("Point alpha while the centre of mass is at the robot centre.")]
        [SerializeField, Range(0f, 1f)] private float centeredPointAlpha = 0.35f;

        [Tooltip("Point alpha when the centre of mass reaches the outer control ring.")]
        [SerializeField, Range(0f, 1f)] private float edgePointAlpha = 0.8f;

        [Tooltip("Ring and direction-line alpha while the centre of mass is at the robot centre.")]
        [SerializeField, Range(0f, 1f)] private float centeredGuideAlpha = 0.08f;

        [Tooltip("Ring and direction-line alpha when the centre of mass reaches the outer control ring.")]
        [SerializeField, Range(0f, 1f)] private float edgeGuideAlpha = 0.35f;

        [Header("Screen-space Ranges")]
        [Tooltip("Diameter of the visible balance-control ring in screen pixels. This changes only the UI ring, not the physical balance simulation.")]
        [SerializeField, Min(16f)] private float controlRingDiameterPixels = 150f;

        [Tooltip("Maximum screen-space distance from the robot centre to the centre-of-mass point when balance magnitude is 100%. Set this equal to half the ring diameter for the point to reach the ring at 100%.")]
        [SerializeField, Min(1f)] private float pointTravelRangePixels = 75f;

        [Tooltip("How far the connection line starts away from the exact robot centre, in screen pixels.")]
        [SerializeField, Min(0f)] private float lineStartInsetPixels;

        [Tooltip("How far the connection line stops before the centre-of-mass point, in screen pixels.")]
        [SerializeField, Min(0f)] private float lineEndInsetPixels;

        [Tooltip("Maximum displayed point/line distance as a multiple of Point Travel Range. Values above one allow the point to visibly leave the ring.")]
        [SerializeField, Range(1f, 2f)] private float displayedOverflowLimit = 1.9f;

        [Header("Screen-space Thickness and Size")]
        [Tooltip("Thickness of the balance range ring in screen pixels.")]
        [SerializeField, Min(0.25f)] private float ringThicknessPixels = 1f;

        [Tooltip("Thickness of the centre-to-balance-point connection line in screen pixels.")]
        [SerializeField, Min(0.25f)] private float lineThicknessPixels = 1.1f;

        [Tooltip("Radius of the centre-of-mass point while balance is centred, in screen pixels.")]
        [SerializeField, Min(0.5f)] private float minimumPointRadiusPixels = 2.2f;

        [Tooltip("Radius of the centre-of-mass point at 100% balance displacement, in screen pixels.")]
        [SerializeField, Min(0.5f)] private float maximumPointRadiusPixels = 8.5f;

        [Tooltip("Visible diagonal size of the fallen centre-of-mass cross relative to the normal point diameter.")]
        [SerializeField, Range(0.5f, 2f)]
        private float fallenPointCrossSizeOfPointDiameter = 1.6f;

        [Tooltip("Additional same-colour UI outline used to make the fallen centre-of-mass cross heavier.")]
        [SerializeField, Range(0f, 3f)]
        private float fallenPointCrossOutlinePixels = 1.25f;

        [Tooltip("Alpha multiplier applied to the complete balance display while tumble balance presentation is active.")]
        [SerializeField, Range(1f, 2f)]
        private float tumbleBalanceOpacityMultiplier = 1.2f;

        [Header("Self Righting")]
        [Tooltip("Colour of the solid centre-of-mass point after the arms establish enough self-righting support.")]
        [SerializeField] private Color selfRightingBalancePointColor =
            new Color(1f, 0.78f, 0.12f, 1f);

        [Tooltip("Number of segments used to draw the range ring. Higher values make it rounder.")]
        [SerializeField, Range(12, 128)] private int circleSegments = 64;

        [Tooltip("Screen-space canvas sorting order of the complete balance display.")]
        [SerializeField] private int canvasSortingOrder = 40;

        [Header("Tumble Motion Trail")]
        [SerializeField] private bool showTumbleBalanceTrail = true;

        [Tooltip("Number of recent centre-of-mass positions retained during fast tumble motion.")]
        [SerializeField, Range(2, 8)] private int tumbleBalanceTrailCount = 5;

        [Tooltip("Lifetime in seconds of the oldest tumble balance afterimage.")]
        [SerializeField, Range(0.05f, 0.4f)] private float tumbleBalanceTrailLifetime = 0.16f;

        [Tooltip("Alpha multiplier of the newest afterimage relative to the live centre-of-mass point.")]
        [SerializeField, Range(0f, 1f)] private float tumbleBalanceTrailAlpha = 0.38f;

        [Tooltip("Size of the oldest afterimage relative to the live centre-of-mass point.")]
        [SerializeField, Range(0.2f, 1f)] private float tumbleBalanceTrailEndScale = 0.55f;

        private RobotBalanceController balance;
        private RobotTumbleController tumble;
        private RobotSelfRightingController selfRighting;
        private RobotMarkerView markerView;
        private Camera mapCamera;
        private GameObject canvasObject;
        private Canvas canvas;
        private RobotBalanceGraphic graphic;
        private Image fallenPointCrossImage;
        private Outline fallenPointCrossOutline;
        private readonly List<BalanceTrailSample> tumbleTrail =
            new List<BalanceTrailSample>(8);
        private readonly Vector2[] tumbleTrailOffsets = new Vector2[8];
        private readonly float[] tumbleTrailAges01 = new float[8];
        private float tumbleTrailSampleElapsed;
        private bool tumbleTrailWasActive;

        private struct BalanceTrailSample
        {
            public Vector2 NormalizedOffset;
            public float Age;

            public BalanceTrailSample(Vector2 normalizedOffset)
            {
                NormalizedOffset = normalizedOffset;
                Age = 0f;
            }
        }

        private void Awake()
        {
            balance = GetComponent<RobotBalanceController>();
            tumble = GetComponent<RobotTumbleController>();
            selfRighting = GetComponent<RobotSelfRightingController>();
            markerView = GetComponent<RobotMarkerView>();
            CreateDisplay();
        }

        private void OnEnable()
        {
            if (canvasObject != null)
                canvasObject.SetActive(showBalanceDisplay);
        }

        private void LateUpdate()
        {
            if (!showBalanceDisplay
                || balance == null
                || graphic == null)
            {
                if (canvasObject != null)
                    canvasObject.SetActive(false);
                return;
            }

            if (!canvasObject.activeSelf)
                canvasObject.SetActive(true);

            if (mapCamera == null || !mapCamera.isActiveAndEnabled)
                mapCamera = Camera.main;
            if (mapCamera == null)
            {
                SetFallenPointCrossVisible(false);
                return;
            }

            Vector3 originScreen = mapCamera.WorldToScreenPoint(
                transform.position);
            if (originScreen.z <= 0f)
            {
                graphic.enabled = false;
                SetFallenPointCrossVisible(false);
                return;
            }

            graphic.enabled = true;
            if (selfRighting == null)
                selfRighting = GetComponent<RobotSelfRightingController>();
            RectTransform graphicRect = graphic.rectTransform;
            graphicRect.anchoredPosition = new Vector2(originScreen.x, originScreen.y);

            RobotBalanceState state = selfRighting != null
                                      && selfRighting.HasBalancePresentation
                ? selfRighting.DisplayedBalanceState
                : tumble != null && tumble.HasTumbleBalanceState
                    ? tumble.TumbleBalanceState
                    : balance.CurrentState;
            Vector2 screenDirection = Vector2.zero;
            if (state.NormalizedWorldOffset.sqrMagnitude > 0.000001f)
            {
                Vector3 directionScreen = mapCamera.WorldToScreenPoint(
                    transform.position
                    + (Vector3)state.NormalizedWorldOffset.normalized);
                screenDirection = ((Vector2)directionScreen - (Vector2)originScreen)
                    .normalized;
            }

            Vector2 displayOffset = screenDirection * state.Magnitude;
            float tumbleOpacityMultiplier = tumble != null
                                             && tumble.HasTumbleBalanceState
                ? tumbleBalanceOpacityMultiplier
                : 1f;
            bool showFallenPointCross = (selfRighting != null
                                         && selfRighting.HasBalancePresentation
                    ? selfRighting.ShowBalancePointAsCross
                    : tumble != null
                      && tumble.State == RobotTumbleState.Fallen)
                                        && fallenPointCrossImage != null
                                        && fallenPointCrossImage.sprite != null;
            float visibilityProgress = Mathf.Clamp01(state.Magnitude);
            Color displayedGuideColor = WithAlpha(
                ringColor,
                Mathf.Lerp(
                    centeredGuideAlpha,
                    edgeGuideAlpha,
                    visibilityProgress)
                * tumbleOpacityMultiplier);
            Color displayedLineColor = WithAlpha(
                lineColor,
                Mathf.Lerp(
                    centeredGuideAlpha,
                    edgeGuideAlpha,
                    visibilityProgress)
                * tumbleOpacityMultiplier);
            Color activePointColor = selfRighting != null
                                     && selfRighting.UseRecoveryBalancePointColor
                ? selfRightingBalancePointColor
                : pointColor;
            Color displayedPointColor = WithAlpha(
                activePointColor,
                Mathf.Lerp(
                    centeredPointAlpha,
                    edgePointAlpha,
                    visibilityProgress)
                * tumbleOpacityMultiplier);
            int trailSampleCount = UpdateTumbleBalanceTrail(displayOffset);
            graphic.SetBalance(
                displayOffset,
                controlRingDiameterPixels * 0.5f,
                pointTravelRangePixels,
                lineStartInsetPixels,
                lineEndInsetPixels,
                ringThicknessPixels,
                lineThicknessPixels,
                minimumPointRadiusPixels,
                maximumPointRadiusPixels,
                !showFallenPointCross,
                displayedOverflowLimit,
                circleSegments,
                displayedGuideColor,
                displayedLineColor,
                displayedPointColor);
            graphic.SetTumbleTrail(
                tumbleTrailOffsets,
                tumbleTrailAges01,
                trailSampleCount,
                tumbleBalanceTrailAlpha,
                tumbleBalanceTrailEndScale,
                displayedPointColor);
            UpdateFallenPointCross(
                showFallenPointCross,
                new Vector2(originScreen.x, originScreen.y),
                displayOffset,
                displayedPointColor);
        }

        private int UpdateTumbleBalanceTrail(Vector2 displayOffset)
        {
            float deltaTime = Mathf.Min(Mathf.Max(0f, Time.deltaTime), 0.05f);
            for (int i = tumbleTrail.Count - 1; i >= 0; i--)
            {
                BalanceTrailSample sample = tumbleTrail[i];
                sample.Age += deltaTime;
                if (sample.Age >= tumbleBalanceTrailLifetime)
                    tumbleTrail.RemoveAt(i);
                else
                    tumbleTrail[i] = sample;
            }

            bool trailActive = showTumbleBalanceTrail
                               && tumble != null
                               && tumble.State == RobotTumbleState.Tumbling;
            if (trailActive && !tumbleTrailWasActive)
            {
                tumbleTrail.Clear();
                tumbleTrailSampleElapsed = 0f;
                AddTumbleTrailSample(displayOffset);
            }

            if (trailActive)
            {
                tumbleTrailSampleElapsed += deltaTime;
                float sampleInterval = tumbleBalanceTrailLifetime
                                       / Mathf.Max(2, tumbleBalanceTrailCount);
                while (tumbleTrailSampleElapsed >= sampleInterval)
                {
                    tumbleTrailSampleElapsed -= sampleInterval;
                    AddTumbleTrailSample(displayOffset);
                }
            }
            else if (!showTumbleBalanceTrail)
            {
                tumbleTrail.Clear();
            }

            tumbleTrailWasActive = trailActive;
            int sampleCount = Mathf.Min(
                tumbleTrail.Count,
                Mathf.Min(8, tumbleBalanceTrailCount));
            int firstSample = tumbleTrail.Count - sampleCount;
            for (int i = 0; i < sampleCount; i++)
            {
                BalanceTrailSample sample = tumbleTrail[firstSample + i];
                tumbleTrailOffsets[i] = sample.NormalizedOffset;
                tumbleTrailAges01[i] = Mathf.Clamp01(
                    sample.Age
                    / Mathf.Max(0.05f, tumbleBalanceTrailLifetime));
            }

            return sampleCount;
        }

        private void AddTumbleTrailSample(Vector2 displayOffset)
        {
            tumbleTrail.Add(new BalanceTrailSample(displayOffset));
            int maximumSamples = Mathf.Min(8, tumbleBalanceTrailCount);
            while (tumbleTrail.Count > maximumSamples)
                tumbleTrail.RemoveAt(0);
        }

        public void SetBalanceDisplayVisible(bool visible)
        {
            showBalanceDisplay = visible;
            if (canvasObject != null)
                canvasObject.SetActive(visible);
        }

        private void CreateDisplay()
        {
            if (canvasObject != null)
                return;

            canvasObject = new GameObject(
                BalanceCanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");

            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var graphicObject = new GameObject(
                "Balance Ring, Direction and Point",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            graphicObject.layer = canvasObject.layer;
            graphicObject.transform.SetParent(canvasObject.transform, false);
            graphic = graphicObject.AddComponent<RobotBalanceGraphic>();
            graphic.raycastTarget = false;
            RectTransform rect = graphic.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = Vector2.one * (controlRingDiameterPixels + 40f);

            CreateFallenPointCrossImage();

            canvasObject.SetActive(showBalanceDisplay);
        }

        private void CreateFallenPointCrossImage()
        {
            var crossObject = new GameObject(
                "Fallen Balance Point Cross",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            crossObject.layer = canvasObject.layer;
            crossObject.transform.SetParent(canvasObject.transform, false);
            fallenPointCrossImage = crossObject.AddComponent<Image>();
            fallenPointCrossImage.raycastTarget = false;
            fallenPointCrossImage.preserveAspect = true;
            fallenPointCrossImage.sprite = markerView != null
                ? markerView.RolloverSignSprite
                : null;

            RectTransform crossRect = fallenPointCrossImage.rectTransform;
            crossRect.anchorMin = Vector2.zero;
            crossRect.anchorMax = Vector2.zero;
            crossRect.pivot = Vector2.one * 0.5f;
            if (fallenPointCrossImage.sprite != null)
            {
                float sourceVisibleDiagonalPixels = Mathf.Max(
                    1f,
                    markerView.RolloverSignVisibleDiameterPixels)
                    * Mathf.Sqrt(2f);
                float targetVisibleDiagonalPixels = maximumPointRadiusPixels
                                                    * 2f
                                                    * fallenPointCrossSizeOfPointDiameter;
                float artworkScale = targetVisibleDiagonalPixels
                                     / sourceVisibleDiagonalPixels;
                Rect spriteRect = fallenPointCrossImage.sprite.rect;
                crossRect.sizeDelta = new Vector2(
                    spriteRect.width * artworkScale,
                    spriteRect.height * artworkScale);
            }
            else
            {
                crossRect.sizeDelta = Vector2.one
                                      * maximumPointRadiusPixels
                                      * 2f;
            }

            fallenPointCrossOutline = crossObject.AddComponent<Outline>();
            fallenPointCrossOutline.useGraphicAlpha = false;
            fallenPointCrossOutline.effectDistance = new Vector2(
                fallenPointCrossOutlinePixels,
                -fallenPointCrossOutlinePixels);

            fallenPointCrossImage.enabled = false;
        }

        private void UpdateFallenPointCross(
            bool visible,
            Vector2 originScreen,
            Vector2 displayOffset,
            Color displayedColor)
        {
            if (fallenPointCrossImage == null)
                return;

            bool canDisplay = visible
                              && fallenPointCrossImage.sprite != null;
            fallenPointCrossImage.enabled = canDisplay;
            if (!canDisplay)
                return;

            Vector2 clampedOffset = Vector2.ClampMagnitude(
                displayOffset,
                displayedOverflowLimit);
            fallenPointCrossImage.rectTransform.anchoredPosition =
                originScreen + clampedOffset * pointTravelRangePixels;
            fallenPointCrossImage.color = displayedColor;
            if (fallenPointCrossOutline != null)
                fallenPointCrossOutline.effectColor = displayedColor;
        }

        private void SetFallenPointCrossVisible(bool visible)
        {
            if (fallenPointCrossImage != null)
                fallenPointCrossImage.enabled = visible;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private void OnDisable()
        {
            tumbleTrail.Clear();
            tumbleTrailWasActive = false;
            tumbleTrailSampleElapsed = 0f;
            if (canvasObject != null)
                canvasObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvasObject != null)
                Destroy(canvasObject);
        }

        private void OnValidate()
        {
            controlRingDiameterPixels = Mathf.Max(16f, controlRingDiameterPixels);
            pointTravelRangePixels = Mathf.Max(1f, pointTravelRangePixels);
            lineStartInsetPixels = Mathf.Max(0f, lineStartInsetPixels);
            lineEndInsetPixels = Mathf.Max(0f, lineEndInsetPixels);
            ringThicknessPixels = Mathf.Max(0.25f, ringThicknessPixels);
            lineThicknessPixels = Mathf.Max(0.25f, lineThicknessPixels);
            minimumPointRadiusPixels = Mathf.Max(0.5f, minimumPointRadiusPixels);
            maximumPointRadiusPixels = Mathf.Max(
                minimumPointRadiusPixels,
                maximumPointRadiusPixels);
            fallenPointCrossSizeOfPointDiameter = Mathf.Clamp(
                fallenPointCrossSizeOfPointDiameter,
                0.5f,
                2f);
            fallenPointCrossOutlinePixels = Mathf.Clamp(
                fallenPointCrossOutlinePixels,
                0f,
                3f);
            tumbleBalanceOpacityMultiplier = Mathf.Clamp(
                tumbleBalanceOpacityMultiplier,
                1f,
                2f);
            displayedOverflowLimit = Mathf.Clamp(displayedOverflowLimit, 1f, 2f);
            circleSegments = Mathf.Clamp(circleSegments, 12, 128);
            centeredPointAlpha = Mathf.Clamp01(centeredPointAlpha);
            edgePointAlpha = Mathf.Clamp01(edgePointAlpha);
            centeredGuideAlpha = Mathf.Clamp01(centeredGuideAlpha);
            edgeGuideAlpha = Mathf.Clamp01(edgeGuideAlpha);
            tumbleBalanceTrailCount = Mathf.Clamp(
                tumbleBalanceTrailCount,
                2,
                8);
            tumbleBalanceTrailLifetime = Mathf.Clamp(
                tumbleBalanceTrailLifetime,
                0.05f,
                0.4f);
            tumbleBalanceTrailAlpha = Mathf.Clamp01(tumbleBalanceTrailAlpha);
            tumbleBalanceTrailEndScale = Mathf.Clamp(
                tumbleBalanceTrailEndScale,
                0.2f,
                1f);
        }
    }

    [AddComponentMenu("")]
    public sealed class RobotBalanceGraphic : MaskableGraphic
    {
        private Vector2 normalizedOffset;
        private float ringRadius = 75f;
        private float pointTravelRange = 75f;
        private float lineStartInset;
        private float lineEndInset;
        private float ringThickness = 1f;
        private float lineThickness = 1f;
        private float minimumPointRadius = 2f;
        private float maximumPointRadius = 8f;
        private bool pointVisible = true;
        private float overflowLimit = 1.16f;
        private int segments = 64;
        private Color32 ringColor;
        private Color32 lineColor;
        private Color32 pointColor;
        private readonly Vector2[] tumbleTrailOffsets = new Vector2[8];
        private readonly float[] tumbleTrailAges01 = new float[8];
        private int tumbleTrailCount;
        private float tumbleTrailAlpha;
        private float tumbleTrailEndScale = 0.55f;
        private Color32 tumbleTrailColor;

        public void SetBalance(
            Vector2 newNormalizedOffset,
            float newRingRadius,
            float newPointTravelRange,
            float newLineStartInset,
            float newLineEndInset,
            float newRingThickness,
            float newLineThickness,
            float newMinimumPointRadius,
            float newMaximumPointRadius,
            bool newPointVisible,
            float newOverflowLimit,
            int newSegments,
            Color newRingColor,
            Color newLineColor,
            Color newPointColor)
        {
            normalizedOffset = newNormalizedOffset;
            ringRadius = newRingRadius;
            pointTravelRange = newPointTravelRange;
            lineStartInset = newLineStartInset;
            lineEndInset = newLineEndInset;
            ringThickness = newRingThickness;
            lineThickness = newLineThickness;
            minimumPointRadius = newMinimumPointRadius;
            maximumPointRadius = newMaximumPointRadius;
            pointVisible = newPointVisible;
            overflowLimit = newOverflowLimit;
            segments = newSegments;
            ringColor = newRingColor;
            lineColor = newLineColor;
            pointColor = newPointColor;
            rectTransform.sizeDelta = Vector2.one
                                      * (Mathf.Max(
                                             ringRadius,
                                             pointTravelRange * overflowLimit)
                                         * 2f
                                         + maximumPointRadius * 2f
                                         + 12f);
            SetVerticesDirty();
        }

        public void SetTumbleTrail(
            Vector2[] offsets,
            float[] ages01,
            int count,
            float alpha,
            float endScale,
            Color color)
        {
            tumbleTrailCount = Mathf.Clamp(count, 0, 8);
            for (int i = 0; i < tumbleTrailCount; i++)
            {
                tumbleTrailOffsets[i] = offsets[i];
                tumbleTrailAges01[i] = Mathf.Clamp01(ages01[i]);
            }

            tumbleTrailAlpha = Mathf.Clamp01(alpha);
            tumbleTrailEndScale = Mathf.Clamp(endScale, 0.2f, 1f);
            tumbleTrailColor = color;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Vector2 center = rectTransform.rect.center;
            float magnitude = normalizedOffset.magnitude;
            Vector2 direction = magnitude > 0.0001f
                ? normalizedOffset / magnitude
                : Vector2.zero;
            float displayedMagnitude = Mathf.Min(magnitude, overflowLimit);
            Vector2 pointCenter = center
                                  + direction
                                  * displayedMagnitude
                                  * pointTravelRange;
            float pointRadius = Mathf.Lerp(
                minimumPointRadius,
                maximumPointRadius,
                Mathf.Clamp01(magnitude));

            float connectionDistance = Vector2.Distance(center, pointCenter);
            float safeStartInset = Mathf.Min(
                Mathf.Max(0f, lineStartInset),
                connectionDistance);
            float safeEndInset = Mathf.Min(
                Mathf.Max(0f, lineEndInset),
                Mathf.Max(0f, connectionDistance - safeStartInset));
            Vector2 lineStart = center + direction * safeStartInset;
            Vector2 lineEnd = pointCenter - direction * safeEndInset;
            if ((lineEnd - lineStart).sqrMagnitude > 0.25f)
            {
                AddLine(
                    vertexHelper,
                    lineStart,
                    lineEnd,
                    lineThickness,
                    lineColor);
            }

            AddRing(
                vertexHelper,
                center,
                ringRadius,
                ringThickness,
                segments,
                ringColor);
            AddTumbleTrailDiscs(vertexHelper, center);
            if (pointVisible)
            {
                AddDisc(
                    vertexHelper,
                    pointCenter,
                    pointRadius,
                    Mathf.Max(12, segments / 2),
                    pointColor);
            }
        }

        private void AddTumbleTrailDiscs(
            VertexHelper vertexHelper,
            Vector2 center)
        {
            for (int i = 0; i < tumbleTrailCount; i++)
            {
                Vector2 offset = tumbleTrailOffsets[i];
                float magnitude = offset.magnitude;
                Vector2 direction = magnitude > 0.0001f
                    ? offset / magnitude
                    : Vector2.zero;
                float displayedMagnitude = Mathf.Min(magnitude, overflowLimit);
                Vector2 trailCenter = center
                                      + direction
                                      * displayedMagnitude
                                      * pointTravelRange;
                float freshness = 1f - tumbleTrailAges01[i];
                float baseRadius = Mathf.Lerp(
                    minimumPointRadius,
                    maximumPointRadius,
                    Mathf.Clamp01(magnitude));
                float radiusScale = Mathf.Lerp(
                    tumbleTrailEndScale,
                    0.9f,
                    freshness);
                Color32 color = tumbleTrailColor;
                color.a = (byte)Mathf.RoundToInt(
                    color.a
                    * tumbleTrailAlpha
                    * freshness
                    * freshness);
                if (color.a == 0)
                    continue;

                AddDisc(
                    vertexHelper,
                    trailCenter,
                    baseRadius * radiusScale,
                    Mathf.Max(10, segments / 3),
                    color);
            }
        }

        private static void AddLine(
            VertexHelper vh,
            Vector2 start,
            Vector2 end,
            float width,
            Color32 color)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector2 normal = new Vector2(-direction.y, direction.x).normalized
                             * width
                             * 0.5f;
            int index = vh.currentVertCount;
            AddVertex(vh, start - normal, color);
            AddVertex(vh, start + normal, color);
            AddVertex(vh, end + normal, color);
            AddVertex(vh, end - normal, color);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private static void AddRing(
            VertexHelper vh,
            Vector2 center,
            float radius,
            float thickness,
            int segmentCount,
            Color32 color)
        {
            int startIndex = vh.currentVertCount;
            float innerRadius = Mathf.Max(0f, radius - thickness * 0.5f);
            float outerRadius = radius + thickness * 0.5f;
            for (int i = 0; i <= segmentCount; i++)
            {
                float angle = i / (float)segmentCount * Mathf.PI * 2f;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddVertex(vh, center + radial * innerRadius, color);
                AddVertex(vh, center + radial * outerRadius, color);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int index = startIndex + i * 2;
                vh.AddTriangle(index, index + 1, index + 3);
                vh.AddTriangle(index, index + 3, index + 2);
            }
        }

        private static void AddDisc(
            VertexHelper vh,
            Vector2 center,
            float radius,
            int segmentCount,
            Color32 color)
        {
            int centerIndex = vh.currentVertCount;
            AddVertex(vh, center, color);
            for (int i = 0; i <= segmentCount; i++)
            {
                float angle = i / (float)segmentCount * Mathf.PI * 2f;
                AddVertex(
                    vh,
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    color);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                vh.AddTriangle(centerIndex, centerIndex + i + 1, centerIndex + i + 2);
            }
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Color32 color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = Vector2.zero;
            vh.AddVert(vertex);
        }
    }
}
