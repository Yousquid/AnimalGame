using System.Collections.Generic;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.MapTest
{
    public sealed class HeightMapPlayerSceneBootstrap : MonoBehaviour
    {
        private const string MapResourcePath = "MapTest/MapTestController";
        private const string RobotResourcePath = "Robot/RobotMarker";
        private const string CameraResourcePath = "Camera/RobotCamera";
        private const string TraversalResourcePath = "Traversal/HeightMapTraversalEvaluator";
        private const string OverlayResourcePath = "Traversal/TraversalOverlay";
        private const string ScanOverlayResourcePath = "Traversal/TraversalScanOverlay";
        private const string MainUiResourcePath = "UI/MainUI";

        [Header("Player Spawn")]
        [Tooltip("Initial player position in logical map meters. Values outside the map are clamped to its edges.")]
        [SerializeField] private Vector2 playerSpawnMapPositionMeters = new Vector2(50f, 50f);

        [Header("Performance Display")]
        [SerializeField] private bool showFrameRate = true;
        [SerializeField, Min(0.05f)] private float frameRateRefreshInterval = 0.25f;

        private MapTestSceneController map;
        private RobotMover robot;
        private HeightMapTraversalEvaluator traversalEvaluator;
        private Vector2 playerMapPosition;
        private float playerHeight;
        private bool playerInsideMap;
        private float smoothedUnscaledDeltaTime;
        private float displayedFramesPerSecond;
        private float nextFrameRateRefreshTime;

        private void Awake()
        {
            map = FindObjectOfType<MapTestSceneController>();
            GameObject mapObject = map != null
                ? map.gameObject
                : InstantiateResource(MapResourcePath, "Map Test Controller");
            GameObject robotObject = InstantiateResource(RobotResourcePath, "Robot Marker");
            GameObject cameraObject = InstantiateResource(CameraResourcePath, "Robot Camera");
            GameObject traversalObject = InstantiateResource(
                TraversalResourcePath,
                "Height Map Traversal Evaluator");
            GameObject overlayObject = InstantiateResource(
                OverlayResourcePath,
                "Debug Traversal Overlay");
            GameObject scanOverlayObject = InstantiateResource(
                ScanOverlayResourcePath,
                "Scanned Traversal Overlay");
            GameObject mainUiObject = InstantiateResource(
                MainUiResourcePath,
                "Main UI");
            if (mapObject == null || robotObject == null || cameraObject == null
                || traversalObject == null || overlayObject == null
                || scanOverlayObject == null || mainUiObject == null)
            {
                enabled = false;
                return;
            }

            if (map == null)
                map = mapObject.GetComponent<MapTestSceneController>();
            robot = robotObject.GetComponent<RobotMover>();
            RobotBalanceController balance =
                robotObject.GetComponent<RobotBalanceController>();
            if (balance == null)
                balance = robotObject.AddComponent<RobotBalanceController>();
            RobotTumbleController tumble =
                robotObject.GetComponent<RobotTumbleController>();
            if (tumble == null)
                tumble = robotObject.AddComponent<RobotTumbleController>();
            RobotHeightMotionDetector heightMotion =
                robotObject.GetComponent<RobotHeightMotionDetector>();
            if (heightMotion == null)
                heightMotion = robotObject.AddComponent<RobotHeightMotionDetector>();
            if (robotObject.GetComponent<RobotBalanceView>() == null)
                robotObject.AddComponent<RobotBalanceView>();
            if (robotObject.GetComponent<RobotArmController>() == null)
                robotObject.AddComponent<RobotArmController>();
            if (robotObject.GetComponent<RobotSelfRightingController>() == null)
                robotObject.AddComponent<RobotSelfRightingController>();
            Camera camera = cameraObject.GetComponent<Camera>();
            RobotCameraFollow cameraFollow = cameraObject.GetComponent<RobotCameraFollow>();
            RobotCameraShake cameraShake =
                cameraObject.GetComponent<RobotCameraShake>();
            if (cameraShake == null)
                cameraShake = cameraObject.AddComponent<RobotCameraShake>();
            traversalEvaluator = traversalObject.GetComponent<HeightMapTraversalEvaluator>();
            TraversalOverlayUI traversalOverlay = overlayObject.GetComponent<TraversalOverlayUI>();
            TraversalScanOverlayUI scanOverlay =
                scanOverlayObject.GetComponent<TraversalScanOverlayUI>();
            ScanChargeUI scanChargeUi =
                mainUiObject.GetComponentInChildren<ScanChargeUI>(true);

            if (map == null || robot == null || camera == null || cameraFollow == null
                || traversalEvaluator == null || traversalOverlay == null
                || scanOverlay == null || scanChargeUi == null
                || !map.HasGeneratedMap)
            {
                Debug.LogError(
                    "HeightMapPlayerScene is missing a required prefab component or generated map.",
                    this);
                enabled = false;
                return;
            }

            robot.transform.position = map.MapPositionToWorld(playerSpawnMapPositionMeters);
            map.UseCamera(camera);
            cameraFollow.FollowBalanceTarget(balance);
            cameraFollow.SnapToTarget();
            traversalEvaluator.Initialize(map);
            robot.SetTraversalEvaluator(traversalEvaluator);
            tumble.Initialize(traversalEvaluator);
            heightMotion.Initialize(map);
            cameraShake.Initialize(robot, balance, heightMotion);
            RobotTumbleUiRotation uiRotation =
                mainUiObject.GetComponent<RobotTumbleUiRotation>();
            if (uiRotation == null)
                uiRotation = mainUiObject.AddComponent<RobotTumbleUiRotation>();
            uiRotation.Initialize(tumble, camera);
            traversalOverlay.Initialize(map, traversalEvaluator, camera, robot);
            scanOverlay.Initialize(
                map,
                traversalEvaluator,
                camera,
                robot,
                scanChargeUi);
            UpdatePlayerHeight();
        }

        private void Update()
        {
            UpdateFrameRate();
            UpdatePlayerHeight();
        }

        private void LateUpdate()
        {
            if (map == null || robot == null)
                return;

            Bounds bounds = map.WorldBounds;
            Vector3 position = robot.transform.position;
            position.x = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
            position.y = Mathf.Clamp(position.y, bounds.min.y, bounds.max.y);
            robot.transform.position = position;
        }

        private void UpdatePlayerHeight()
        {
            if (map == null || robot == null)
                return;

            playerInsideMap = map.TrySampleWorldPosition(
                robot.transform.position,
                out playerMapPosition,
                out playerHeight);
        }

        private void UpdateFrameRate()
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0.000001f)
                return;

            if (smoothedUnscaledDeltaTime <= 0f)
            {
                smoothedUnscaledDeltaTime = deltaTime;
            }
            else
            {
                // Smooth quickly enough to show real performance changes without making
                // the number unreadable by changing to a completely new value every frame.
                float smoothing = 1f - Mathf.Exp(-8f * deltaTime);
                smoothedUnscaledDeltaTime = Mathf.Lerp(
                    smoothedUnscaledDeltaTime,
                    deltaTime,
                    smoothing);
            }

            if (Time.unscaledTime < nextFrameRateRefreshTime)
                return;

            displayedFramesPerSecond = 1f / Mathf.Max(0.000001f, smoothedUnscaledDeltaTime);
            nextFrameRateRefreshTime = Time.unscaledTime
                                       + Mathf.Max(0.05f, frameRateRefreshInterval);
        }

        private static GameObject InstantiateResource(string path, string instanceName)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"Missing Resources prefab: {path}");
                return null;
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = instanceName;
            return instance;
        }

        private void OnGUI()
        {
            Matrix4x4 previousGuiMatrix =
                RobotTumbleUiRotation.BeginImmediateModeGuiRotation();
            try
            {
                DrawFrameRate();
            }
            finally
            {
                GUI.matrix = previousGuiMatrix;
            }

            DrawRobotTerrainData();
        }

        private void DrawRobotTerrainData()
        {
            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.9f, 0.97f, 1f);
            GUIStyle data = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            data.normal.textColor = new Color(0.75f, 0.9f, 0.93f);

            float left = Screen.width - 330f;
            GUI.Box(new Rect(left, 18f, 312f, 250f), GUIContent.none);
            GUI.Label(new Rect(left + 16f, 28f, 280f, 26f), "ROBOT TERRAIN DATA", title);
            if (!playerInsideMap)
            {
                GUI.Label(new Rect(left + 16f, 64f, 280f, 24f), "OUTSIDE MAP", data);
                return;
            }

            GUI.Label(new Rect(left + 16f, 61f, 280f, 24f),
                $"POSITION   X {playerMapPosition.x:F1}m   Y {playerMapPosition.y:F1}m", data);
            GUI.Label(new Rect(left + 16f, 88f, 280f, 26f), $"CURRENT HEIGHT   {playerHeight:F1}m", data);
            string slopeText = robot != null && robot.CurrentTraversalResult.HasData
                ? $"FORWARD SLOPE   {robot.CurrentTraversalResult.SignedSlopeAngle:+0.0;-0.0;0.0} deg"
                : "FORWARD SLOPE   NO DATA";
            GUI.Label(new Rect(left + 16f, 115f, 280f, 26f), slopeText, data);
            string surfaceText = robot != null && robot.CurrentTraversalResult.HasData
                ? $"SURFACE MAX     {robot.CurrentTraversalResult.MaximumSurfaceSlopeAngle:F1} deg"
                : "SURFACE MAX     NO DATA";
            GUI.Label(new Rect(left + 16f, 142f, 280f, 26f), surfaceText, data);
            string stepText = robot != null && robot.CurrentTraversalResult.HasData
                ? $"STEP RESIDUAL   {robot.CurrentTraversalResult.MaximumStepHeight:F2}m"
                : "STEP RESIDUAL   NO DATA";
            GUI.Label(new Rect(left + 16f, 169f, 280f, 26f), stepText, data);
            GUIStyle state = new GUIStyle(data);
            string traversalText = "TRAVERSAL   NO DATA";
            state.normal.textColor = new Color(0.75f, 0.9f, 0.93f);
            if (robot != null && robot.CurrentTraversalResult.HasData)
            {
                if (robot.IsSlopeBlocked)
                {
                    traversalText =
                        $"TRAVERSAL   BLOCKED ({robot.CurrentTraversalResult.BlockReason})";
                    state.normal.textColor = new Color(1f, 0.35f, 0.28f);
                }
                else if (robot.CurrentLevelThreeClimbPhase
                         != LevelThreeClimbFailurePhase.None)
                {
                    traversalText = robot.CurrentLevelThreeClimbPhase switch
                    {
                        LevelThreeClimbFailurePhase.Grip =>
                            "TRAVERSAL   LEVEL III / GRIP",
                        LevelThreeClimbFailurePhase.Strain =>
                            "TRAVERSAL   LEVEL III / STRAIN",
                        LevelThreeClimbFailurePhase.Slip =>
                            "TRAVERSAL   LEVEL III / SLIP",
                        _ => "TRAVERSAL   SLOPE LEVEL III"
                    };
                    state.normal.textColor = new Color(1f, 0.55f, 0.2f);
                }
                else if (robot.IsLevelThreeUnstable)
                {
                    traversalText = "TRAVERSAL   LEVEL III / UNSTABLE";
                    state.normal.textColor = new Color(1f, 0.55f, 0.2f);
                }
                else if (robot.IsDownhillBoosted)
                {
                    traversalText = "TRAVERSAL   DOWNHILL BOOST";
                    state.normal.textColor = new Color(0.3f, 0.8f, 1f);
                }
                else
                {
                    UphillSlopeLevel surfaceLevel = traversalEvaluator != null
                        ? traversalEvaluator.ClassifyUphillSlope(
                            robot.CurrentTraversalResult.MaximumSurfaceSlopeAngle)
                        : UphillSlopeLevel.LevelOne;
                    traversalText = surfaceLevel switch
                    {
                        UphillSlopeLevel.LevelTwo => "TRAVERSAL   SLOPE LEVEL II",
                        UphillSlopeLevel.LevelThree => "TRAVERSAL   SLOPE LEVEL III",
                        _ => "TRAVERSAL   SLOPE LEVEL I"
                    };
                    state.normal.textColor = surfaceLevel == UphillSlopeLevel.LevelOne
                        ? new Color(0.35f, 1f, 0.66f)
                        : new Color(1f, 0.83f, 0.27f);
                }
            }
            GUI.Label(
                new Rect(left + 16f, 196f, 280f, 26f),
                traversalText,
                state);
        }

        private void DrawFrameRate()
        {
            if (!showFrameRate)
                return;

            float width = 144f;
            float left = Screen.width - width - 18f;
            var panelRect = new Rect(left, 278f, width, 46f);
            GUI.Box(panelRect, GUIContent.none);

            GUIStyle frameRateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            frameRateStyle.normal.textColor = displayedFramesPerSecond >= 55f
                ? new Color(0.35f, 1f, 0.66f)
                : displayedFramesPerSecond >= 30f
                    ? new Color(1f, 0.83f, 0.27f)
                    : new Color(1f, 0.35f, 0.28f);

            GUI.Label(
                new Rect(left + 6f, 283f, width - 12f, 34f),
                $"FPS  {displayedFramesPerSecond:F1}",
                frameRateStyle);
        }

        private void OnValidate()
        {
            frameRateRefreshInterval = Mathf.Max(0.05f, frameRateRefreshInterval);
        }
    }

    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class RobotTumbleUiRotation : MonoBehaviour
    {
        public static float ActiveRotationDegrees { get; private set; }

        [Tooltip("How often newly created root canvases are added to the rotating UI layer.")]
        [SerializeField, Min(0.05f)] private float canvasRefreshInterval = 0.5f;

        private readonly Dictionary<Canvas, RectTransform> canvasPivots = new();
        private RobotTumbleController tumble;
        private Camera mapCamera;
        private float currentRotationDegrees;
        private float nextCanvasRefreshTime;
        private int screenQuarterTurnSign = 1;
        private bool tumbleDirectionLocked;

        public void Initialize(RobotTumbleController tumbleController, Camera camera)
        {
            tumble = tumbleController;
            mapCamera = camera;
            RefreshCanvasRoots();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextCanvasRefreshTime)
                RefreshCanvasRoots();

            currentRotationDegrees = CalculateTargetRotation();
            ActiveRotationDegrees = currentRotationDegrees;
            ApplyRotationToCanvasPivots();
        }

        public static Matrix4x4 BeginImmediateModeGuiRotation()
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            if (Mathf.Abs(ActiveRotationDegrees) > 0.0001f)
            {
                GUIUtility.RotateAroundPivot(
                    ActiveRotationDegrees,
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            }

            return previousMatrix;
        }

        private float CalculateTargetRotation()
        {
            if (tumble == null || tumble.State == RobotTumbleState.Upright)
            {
                tumbleDirectionLocked = false;
                return 0f;
            }

            if (!tumbleDirectionLocked)
                LockScreenQuarterTurnSign();

            float quarterTurnProgress = tumble.ContinuousQuarterTurnProgress;
            return screenQuarterTurnSign * quarterTurnProgress * 90f;
        }

        private void LockScreenQuarterTurnSign()
        {
            screenQuarterTurnSign = tumble != null
                ? tumble.QuarterTurnSign
                : 1;
            if (tumble != null
                && mapCamera != null
                && tumble.DirectionWorld.sqrMagnitude > 0.000001f)
            {
                Vector3 originScreen = mapCamera.WorldToScreenPoint(
                    tumble.transform.position);
                Vector3 directionScreen = mapCamera.WorldToScreenPoint(
                    tumble.transform.position + (Vector3)tumble.DirectionWorld);
                float screenDirectionX = directionScreen.x - originScreen.x;
                if (Mathf.Abs(screenDirectionX) > 0.001f)
                    screenQuarterTurnSign = screenDirectionX > 0f ? -1 : 1;
            }

            tumbleDirectionLocked = true;
        }

        private void RefreshCanvasRoots()
        {
            nextCanvasRefreshTime = Time.unscaledTime
                                    + Mathf.Max(0.05f, canvasRefreshInterval);
            Canvas[] canvases = FindObjectsOfType<Canvas>(true);
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null
                    || !canvas.isRootCanvas
                    || canvas.renderMode == RenderMode.WorldSpace)
                {
                    continue;
                }

                if (canvas.gameObject.name == RobotBalanceView.BalanceCanvasName)
                {
                    if (canvasPivots.TryGetValue(
                            canvas,
                            out RectTransform excludedPivot)
                        && excludedPivot != null)
                    {
                        excludedPivot.localRotation = Quaternion.identity;
                    }

                    canvasPivots.Remove(canvas);
                    continue;
                }

                if (!canvasPivots.TryGetValue(canvas, out RectTransform pivot)
                    || pivot == null)
                {
                    pivot = FindOrCreateRotationPivot(canvas);
                    canvasPivots[canvas] = pivot;
                }

                MoveDirectCanvasChildrenUnderPivot(canvas, pivot);
            }
        }

        private static RectTransform FindOrCreateRotationPivot(Canvas canvas)
        {
            const string PivotName = "Tumble UI Rotation Pivot";
            Transform canvasTransform = canvas.transform;
            for (int i = 0; i < canvasTransform.childCount; i++)
            {
                Transform child = canvasTransform.GetChild(i);
                if (child.name == PivotName && child is RectTransform existingPivot)
                    return existingPivot;
            }

            var pivotObject = new GameObject(PivotName, typeof(RectTransform));
            pivotObject.layer = canvas.gameObject.layer;
            RectTransform pivot = pivotObject.GetComponent<RectTransform>();
            pivot.SetParent(canvasTransform, false);
            pivot.anchorMin = Vector2.zero;
            pivot.anchorMax = Vector2.one;
            pivot.offsetMin = Vector2.zero;
            pivot.offsetMax = Vector2.zero;
            pivot.pivot = Vector2.one * 0.5f;
            return pivot;
        }

        private static void MoveDirectCanvasChildrenUnderPivot(
            Canvas canvas,
            RectTransform pivot)
        {
            Transform canvasTransform = canvas.transform;
            for (int i = canvasTransform.childCount - 1; i >= 0; i--)
            {
                Transform child = canvasTransform.GetChild(i);
                if (child == pivot)
                    continue;

                child.SetParent(pivot, false);
            }
        }

        private void ApplyRotationToCanvasPivots()
        {
            var missingCanvases = new List<Canvas>();
            foreach (KeyValuePair<Canvas, RectTransform> canvasPivot in canvasPivots)
            {
                if (canvasPivot.Key == null || canvasPivot.Value == null)
                {
                    missingCanvases.Add(canvasPivot.Key);
                    continue;
                }

                canvasPivot.Value.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    currentRotationDegrees);
            }

            foreach (Canvas missingCanvas in missingCanvases)
                canvasPivots.Remove(missingCanvas);
        }

        private void RestoreCanvasRotations()
        {
            foreach (KeyValuePair<Canvas, RectTransform> canvasPivot in canvasPivots)
            {
                if (canvasPivot.Value != null)
                    canvasPivot.Value.localRotation = Quaternion.identity;
            }

            currentRotationDegrees = 0f;
            ActiveRotationDegrees = 0f;
        }

        private void OnDisable()
        {
            RestoreCanvasRotations();
        }

        private void OnDestroy()
        {
            RestoreCanvasRotations();
        }

        private void OnValidate()
        {
            canvasRefreshInterval = Mathf.Max(0.05f, canvasRefreshInterval);
        }
    }
}
