using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(HeightMapPlayerSceneBootstrap))]
    public sealed class HeightMapPlayerSceneBootstrapEditor : UnityEditor.Editor
    {
        private const float MarkerRadiusMeters = 2f;
        private SerializedProperty playerSpawnMapPositionMeters;

        private void OnEnable()
        {
            playerSpawnMapPositionMeters = serializedObject.FindProperty(
                "playerSpawnMapPositionMeters");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var bootstrap = (HeightMapPlayerSceneBootstrap)target;
            MapTestSceneController map = ResolveMap(bootstrap);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The player spawn is shown as a target in the Scene view. "
                + "Select this object and drag the centre handle to place it "
                + "directly on the map.",
                MessageType.Info);

            if (map == null || !map.HasGeneratedMap)
            {
                EditorGUILayout.HelpBox(
                    "No generated height-map controller is available in this scene.",
                    MessageType.Warning);
                return;
            }

            Vector2 authoredPosition = bootstrap.PlayerSpawnMapPositionMeters;
            Vector2 actualPosition = ClampToMap(authoredPosition, map);
            bool insideRectangle = (actualPosition - authoredPosition).sqrMagnitude
                                   <= 0.000001f;
            bool playable = map.TrySampleMapPosition(
                actualPosition,
                out float heightMeters);

            if (!insideRectangle)
            {
                EditorGUILayout.HelpBox(
                    $"The authored position is outside the map. Runtime will clamp it to "
                    + $"X {actualPosition.x:0.0}m, Y {actualPosition.y:0.0}m.",
                    MessageType.Warning);
            }
            else if (!playable)
            {
                EditorGUILayout.HelpBox(
                    "The spawn is inside the rectangular map bounds but outside the "
                    + "playable height-map silhouette.",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Playable spawn · X {actualPosition.x:0.0}m · "
                    + $"Y {actualPosition.y:0.0}m · Height {heightMeters:0.0}m",
                    MessageType.None);
            }

            if (GUILayout.Button("Frame Player Spawn In Scene"))
                FrameSpawnInScene(map, actualPosition);
        }

        private void OnSceneGUI()
        {
            var bootstrap = (HeightMapPlayerSceneBootstrap)target;
            MapTestSceneController map = ResolveMap(bootstrap);
            if (map == null || !map.HasGeneratedMap
                || playerSpawnMapPositionMeters == null)
            {
                return;
            }

            serializedObject.Update();
            Vector2 actualPosition = ClampToMap(
                playerSpawnMapPositionMeters.vector2Value,
                map);
            Vector3 worldPosition = GetMarkerWorldPosition(map, actualPosition);
            float handleSize = HandleUtility.GetHandleSize(worldPosition) * 0.1f;

            EditorGUI.BeginChangeCheck();
            Handles.color = map.TrySampleMapPosition(actualPosition, out _)
                ? SpawnGizmoDrawer.ValidColour
                : SpawnGizmoDrawer.InvalidColour;
            Vector3 movedWorldPosition = Handles.FreeMoveHandle(
                worldPosition,
                handleSize,
                Vector3.zero,
                Handles.CircleHandleCap);
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(bootstrap, "Move Player Spawn");
            playerSpawnMapPositionMeters.vector2Value =
                WorldToMapPosition(map, movedWorldPosition);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(bootstrap);
            SceneView.RepaintAll();
        }

        private static void FrameSpawnInScene(
            MapTestSceneController map,
            Vector2 mapPosition)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return;

            Vector3 worldPosition = GetMarkerWorldPosition(map, mapPosition);
            sceneView.pivot = worldPosition;
            sceneView.size = Mathf.Max(
                1f,
                map.MapMetersToWorldDistance(Vector2.right, 12f));
            sceneView.Repaint();
        }

        internal static MapTestSceneController ResolveMap(
            HeightMapPlayerSceneBootstrap bootstrap)
        {
            if (bootstrap == null)
                return null;

            MapTestSceneController[] maps =
                Object.FindObjectsOfType<MapTestSceneController>();
            for (int index = 0; index < maps.Length; index++)
            {
                MapTestSceneController candidate = maps[index];
                if (candidate != null
                    && candidate.gameObject.scene == bootstrap.gameObject.scene)
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static Vector2 ClampToMap(
            Vector2 mapPosition,
            MapTestSceneController map)
        {
            Vector2 size = map.MapSizeMeters;
            return new Vector2(
                Mathf.Clamp(mapPosition.x, 0f, size.x),
                Mathf.Clamp(mapPosition.y, 0f, size.y));
        }

        internal static Vector3 GetMarkerWorldPosition(
            MapTestSceneController map,
            Vector2 mapPosition)
        {
            Vector3 worldPosition = map.MapPositionToWorld(mapPosition);
            worldPosition.z = map.WorldBounds.center.z - 0.2f;
            return worldPosition;
        }

        private static Vector2 WorldToMapPosition(
            MapTestSceneController map,
            Vector3 worldPosition)
        {
            Bounds bounds = map.WorldBounds;
            Vector2 size = map.MapSizeMeters;
            return new Vector2(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x)
                * size.x,
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, worldPosition.y)
                * size.y);
        }

        internal static Vector2 GetMarkerWorldRadii(
            MapTestSceneController map,
            Vector3 worldPosition)
        {
            Bounds bounds = map.WorldBounds;
            Vector2 mapSize = map.MapSizeMeters;
            float minimumScreenRadius =
                HandleUtility.GetHandleSize(worldPosition) * 0.065f;
            return new Vector2(
                Mathf.Max(
                    minimumScreenRadius,
                    MarkerRadiusMeters * bounds.size.x
                    / Mathf.Max(0.0001f, mapSize.x)),
                Mathf.Max(
                    minimumScreenRadius,
                    MarkerRadiusMeters * bounds.size.y
                    / Mathf.Max(0.0001f, mapSize.y)));
        }
    }

    public static class SpawnGizmoDrawer
    {
        private const int CircleSegments = 48;
        internal static readonly Color ValidColour =
            new Color(0.2f, 1f, 0.72f, 0.98f);
        internal static readonly Color InvalidColour =
            new Color(1f, 0.25f, 0.2f, 0.98f);

        private static GUIStyle labelStyle;

        [DrawGizmo(
            GizmoType.Selected
            | GizmoType.NonSelected
            | GizmoType.Active)]
        private static void DrawPlayerSpawn(
            HeightMapPlayerSceneBootstrap bootstrap,
            GizmoType gizmoType)
        {
            MapTestSceneController map =
                HeightMapPlayerSceneBootstrapEditor.ResolveMap(bootstrap);
            if (map == null || !map.HasGeneratedMap)
                return;

            Vector2 authoredPosition = bootstrap.PlayerSpawnMapPositionMeters;
            Vector2 actualPosition =
                HeightMapPlayerSceneBootstrapEditor.ClampToMap(
                    authoredPosition,
                    map);
            bool insideRectangle = (actualPosition - authoredPosition).sqrMagnitude
                                   <= 0.000001f;
            float heightMeters = 0f;
            bool playable = insideRectangle
                            && map.TrySampleMapPosition(
                                actualPosition,
                                out heightMeters);
            Vector3 worldPosition =
                HeightMapPlayerSceneBootstrapEditor.GetMarkerWorldPosition(
                    map,
                    actualPosition);
            Vector2 radii =
                HeightMapPlayerSceneBootstrapEditor.GetMarkerWorldRadii(
                    map,
                    worldPosition);
            Color colour = playable ? ValidColour : InvalidColour;

            DrawTarget(worldPosition, radii, colour);

            if ((gizmoType & GizmoType.Selected) == 0)
                return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 12
                };
            }

            labelStyle.normal.textColor = colour;
            string heightText = playable
                ? $" · H {heightMeters:0.0}m"
                : " · INVALID";
            Vector3 labelPosition = worldPosition
                                    + Vector3.up * radii.y * 1.4f;
            Handles.Label(
                labelPosition,
                $"PLAYER SPAWN\nX {actualPosition.x:0.0}m · "
                + $"Y {actualPosition.y:0.0}m{heightText}",
                labelStyle);
        }

        private static void DrawTarget(
            Vector3 centre,
            Vector2 radii,
            Color colour)
        {
            var ring = new Vector3[CircleSegments + 1];
            for (int index = 0; index <= CircleSegments; index++)
            {
                float angle = index / (float)CircleSegments
                              * Mathf.PI * 2f;
                ring[index] = centre + new Vector3(
                    Mathf.Cos(angle) * radii.x,
                    Mathf.Sin(angle) * radii.y,
                    0f);
            }

            Color fill = colour;
            fill.a = 0.13f;
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(ring);
            Handles.color = colour;
            Handles.DrawAAPolyLine(3f, ring);
            Handles.DrawAAPolyLine(
                2f,
                centre + Vector3.left * radii.x * 1.25f,
                centre + Vector3.right * radii.x * 1.25f);
            Handles.DrawAAPolyLine(
                2f,
                centre + Vector3.down * radii.y * 1.25f,
                centre + Vector3.up * radii.y * 1.25f);
            Handles.DrawSolidDisc(
                centre,
                Vector3.forward,
                Mathf.Min(radii.x, radii.y) * 0.16f);
        }
    }
}
