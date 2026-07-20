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
            GameObject mapObject = InstantiateResource(MapResourcePath, "Map Test Controller");
            GameObject robotObject = InstantiateResource(RobotResourcePath, "Robot Marker");
            GameObject cameraObject = InstantiateResource(CameraResourcePath, "Robot Camera");
            GameObject traversalObject = InstantiateResource(
                TraversalResourcePath,
                "Height Map Traversal Evaluator");
            GameObject overlayObject = InstantiateResource(
                OverlayResourcePath,
                "Traversal Overlay");
            GameObject mainUiObject = InstantiateResource(
                MainUiResourcePath,
                "Main UI");
            if (mapObject == null || robotObject == null || cameraObject == null
                || traversalObject == null || overlayObject == null || mainUiObject == null)
            {
                enabled = false;
                return;
            }

            map = mapObject.GetComponent<MapTestSceneController>();
            robot = robotObject.GetComponent<RobotMover>();
            Camera camera = cameraObject.GetComponent<Camera>();
            RobotCameraFollow cameraFollow = cameraObject.GetComponent<RobotCameraFollow>();
            traversalEvaluator = traversalObject.GetComponent<HeightMapTraversalEvaluator>();
            TraversalOverlayUI traversalOverlay = overlayObject.GetComponent<TraversalOverlayUI>();

            if (map == null || robot == null || camera == null || cameraFollow == null
                || traversalEvaluator == null || traversalOverlay == null || !map.HasGeneratedMap)
            {
                Debug.LogError(
                    "HeightMapPlayerScene is missing a required prefab component or generated map.",
                    this);
                enabled = false;
                return;
            }

            robot.transform.position = map.MapPositionToWorld(playerSpawnMapPositionMeters);
            map.UseCamera(camera);
            cameraFollow.Target = robot.transform;
            cameraFollow.SnapToTarget();
            traversalEvaluator.Initialize(map);
            robot.SetTraversalEvaluator(traversalEvaluator);
            traversalOverlay.Initialize(map, traversalEvaluator, camera, robot);
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
            DrawFrameRate();

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
            state.normal.textColor = robot != null && robot.IsSlopeBlocked
                ? new Color(1f, 0.35f, 0.28f)
                : new Color(0.35f, 1f, 0.66f);
            GUI.Label(
                new Rect(left + 16f, 196f, 280f, 26f),
                robot != null && robot.IsSlopeBlocked
                    ? $"TRAVERSAL   BLOCKED ({robot.CurrentTraversalResult.BlockReason})"
                    : "TRAVERSAL   PASSABLE",
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
}
