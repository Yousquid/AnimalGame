using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotBalanceController))]
    public sealed class RobotBalanceView : MonoBehaviour
    {
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

        [Header("Screen-space Shape")]
        [SerializeField, Min(16f)] private float controlRingDiameterPixels = 150f;
        [SerializeField, Min(0.25f)] private float ringThicknessPixels = 1f;
        [SerializeField, Min(0.25f)] private float lineThicknessPixels = 1.1f;
        [SerializeField, Min(0.5f)] private float minimumPointRadiusPixels = 2.2f;
        [SerializeField, Min(0.5f)] private float maximumPointRadiusPixels = 8.5f;
        [SerializeField, Range(1f, 1.5f)] private float displayedOverflowLimit = 1.16f;
        [SerializeField, Range(12, 128)] private int circleSegments = 64;
        [SerializeField] private int canvasSortingOrder = 40;

        private RobotBalanceController balance;
        private Camera mapCamera;
        private GameObject canvasObject;
        private Canvas canvas;
        private RobotBalanceGraphic graphic;

        private void Awake()
        {
            balance = GetComponent<RobotBalanceController>();
            CreateDisplay();
        }

        private void OnEnable()
        {
            if (canvasObject != null)
                canvasObject.SetActive(showBalanceDisplay);
        }

        private void LateUpdate()
        {
            if (!showBalanceDisplay || balance == null || graphic == null)
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
                return;

            Vector3 originScreen = mapCamera.WorldToScreenPoint(transform.position);
            if (originScreen.z <= 0f)
            {
                graphic.enabled = false;
                return;
            }

            graphic.enabled = true;
            RectTransform graphicRect = graphic.rectTransform;
            graphicRect.anchoredPosition = new Vector2(originScreen.x, originScreen.y);

            RobotBalanceState state = balance.CurrentState;
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
            float visibilityProgress = Mathf.Clamp01(state.Magnitude);
            Color displayedGuideColor = WithAlpha(
                ringColor,
                Mathf.Lerp(
                    centeredGuideAlpha,
                    edgeGuideAlpha,
                    visibilityProgress));
            Color displayedLineColor = WithAlpha(
                lineColor,
                Mathf.Lerp(
                    centeredGuideAlpha,
                    edgeGuideAlpha,
                    visibilityProgress));
            Color displayedPointColor = WithAlpha(
                pointColor,
                Mathf.Lerp(
                    centeredPointAlpha,
                    edgePointAlpha,
                    visibilityProgress));
            graphic.SetBalance(
                displayOffset,
                controlRingDiameterPixels * 0.5f,
                ringThicknessPixels,
                lineThicknessPixels,
                minimumPointRadiusPixels,
                maximumPointRadiusPixels,
                displayedOverflowLimit,
                circleSegments,
                displayedGuideColor,
                displayedLineColor,
                displayedPointColor);
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
                "Robot Balance UI",
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

            canvasObject.SetActive(showBalanceDisplay);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private void OnDisable()
        {
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
            ringThicknessPixels = Mathf.Max(0.25f, ringThicknessPixels);
            lineThicknessPixels = Mathf.Max(0.25f, lineThicknessPixels);
            minimumPointRadiusPixels = Mathf.Max(0.5f, minimumPointRadiusPixels);
            maximumPointRadiusPixels = Mathf.Max(
                minimumPointRadiusPixels,
                maximumPointRadiusPixels);
            displayedOverflowLimit = Mathf.Clamp(displayedOverflowLimit, 1f, 1.5f);
            circleSegments = Mathf.Clamp(circleSegments, 12, 128);
            centeredPointAlpha = Mathf.Clamp01(centeredPointAlpha);
            edgePointAlpha = Mathf.Clamp01(edgePointAlpha);
            centeredGuideAlpha = Mathf.Clamp01(centeredGuideAlpha);
            edgeGuideAlpha = Mathf.Clamp01(edgeGuideAlpha);
        }
    }

    [AddComponentMenu("")]
    public sealed class RobotBalanceGraphic : MaskableGraphic
    {
        private Vector2 normalizedOffset;
        private float ringRadius = 75f;
        private float ringThickness = 1f;
        private float lineThickness = 1f;
        private float minimumPointRadius = 2f;
        private float maximumPointRadius = 8f;
        private float overflowLimit = 1.16f;
        private int segments = 64;
        private Color32 ringColor;
        private Color32 lineColor;
        private Color32 pointColor;

        public void SetBalance(
            Vector2 newNormalizedOffset,
            float newRingRadius,
            float newRingThickness,
            float newLineThickness,
            float newMinimumPointRadius,
            float newMaximumPointRadius,
            float newOverflowLimit,
            int newSegments,
            Color newRingColor,
            Color newLineColor,
            Color newPointColor)
        {
            normalizedOffset = newNormalizedOffset;
            ringRadius = newRingRadius;
            ringThickness = newRingThickness;
            lineThickness = newLineThickness;
            minimumPointRadius = newMinimumPointRadius;
            maximumPointRadius = newMaximumPointRadius;
            overflowLimit = newOverflowLimit;
            segments = newSegments;
            ringColor = newRingColor;
            lineColor = newLineColor;
            pointColor = newPointColor;
            rectTransform.sizeDelta = Vector2.one
                                      * (ringRadius * 2f
                                         + maximumPointRadius * 2f
                                         + 12f);
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
                                  * ringRadius;
            float pointRadius = Mathf.Lerp(
                minimumPointRadius,
                maximumPointRadius,
                Mathf.Clamp01(magnitude));

            if ((pointCenter - center).sqrMagnitude > 0.25f)
            {
                AddLine(
                    vertexHelper,
                    center,
                    pointCenter,
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
            AddDisc(
                vertexHelper,
                pointCenter,
                pointRadius,
                Mathf.Max(12, segments / 2),
                pointColor);
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
