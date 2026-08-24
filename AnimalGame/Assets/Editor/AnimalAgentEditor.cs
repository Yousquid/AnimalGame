using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(AnimalAgent))]
    public sealed class AnimalAgentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var agent = (AnimalAgent)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scene Gizmos\n"
                + "Green: fixed daily activity range\n"
                + "Orange: hearing / alert range around the animal\n"
                + "Cyan: direct-vision bonus angle (hearing still works outside it)",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(agent.Config == null))
            {
                if (GUILayout.Button("Select Species Config"))
                {
                    Selection.activeObject = agent.Config;
                    EditorGUIUtility.PingObject(agent.Config);
                }
            }
        }
    }

    public static class AnimalAgentGizmoDrawer
    {
        private const int CircleSegments = 72;
        private const int VisionSegments = 32;
        private const float PrefabPreviewWorldUnitsPerMapMeter = 0.1f;

        private static readonly Color ActivityColour =
            new Color(0.28f, 0.95f, 0.52f, 0.95f);
        private static readonly Color AlertColour =
            new Color(1f, 0.67f, 0.12f, 0.95f);
        private static readonly Color VisionOutlineColour =
            new Color(0.18f, 0.88f, 1f, 0.95f);
        private static readonly Color VisionFillColour =
            new Color(0.12f, 0.72f, 1f, 0.09f);

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawAnimalRanges(
            AnimalAgent agent,
            GizmoType gizmoType)
        {
            if (agent == null || agent.Config == null)
                return;

            MapTestSceneController map = ResolveMap(agent);
            bool hasMap = map != null && map.HasGeneratedMap;
            Vector3 currentCentre = agent.transform.position;
            Vector3 homeCentre = GetHomeCentre(agent, map, hasMap);
            Vector2 facingMapDirection = GetFacingMapDirection(
                agent,
                map,
                hasMap);

            DrawRangeCircle(
                homeCentre,
                agent.Config.ActivityRadiusMeters,
                ActivityColour,
                map,
                hasMap,
                false);
            DrawRangeCircle(
                currentCentre,
                agent.Config.AlertRadiusMeters,
                AlertColour,
                map,
                hasMap,
                true);
            DrawVisionSector(
                currentCentre,
                facingMapDirection,
                agent.Config.AlertRadiusMeters,
                agent.Config.DirectVisionAngleDegrees,
                map,
                hasMap);
            DrawLabels(agent, homeCentre, currentCentre, map, hasMap);
        }

        private static MapTestSceneController ResolveMap(AnimalAgent agent)
        {
            if (agent.Map != null)
                return agent.Map;

            HeightMapPlacedObject placedObject =
                agent.GetComponent<HeightMapPlacedObject>();
            if (placedObject != null && placedObject.Map != null)
                return placedObject.Map;

            return Object.FindObjectOfType<MapTestSceneController>();
        }

        private static Vector3 GetHomeCentre(
            AnimalAgent agent,
            MapTestSceneController map,
            bool hasMap)
        {
            if (!Application.isPlaying || agent.Map == null || !hasMap)
                return agent.transform.position;

            Vector3 centre = map.MapPositionToWorld(agent.HomeMapPosition);
            centre.z = agent.transform.position.z;
            return centre;
        }

        private static Vector2 GetFacingMapDirection(
            AnimalAgent agent,
            MapTestSceneController map,
            bool hasMap)
        {
            if (Application.isPlaying
                && agent.Motor != null
                && agent.Motor.FacingMapDirection.sqrMagnitude > 0.000001f)
            {
                return agent.Motor.FacingMapDirection.normalized;
            }

            Vector2 worldDirection = agent.transform.up;
            if (!hasMap)
                return worldDirection.normalized;

            Vector2 mapDirection = map.WorldDirectionToMapDirection(
                worldDirection);
            return mapDirection.sqrMagnitude > 0.000001f
                ? mapDirection.normalized
                : Vector2.up;
        }

        private static void DrawRangeCircle(
            Vector3 centre,
            float radiusMeters,
            Color colour,
            MapTestSceneController map,
            bool hasMap,
            bool dotted)
        {
            if (radiusMeters <= 0f)
                return;

            var points = new Vector3[CircleSegments + 1];
            for (int index = 0; index <= CircleSegments; index++)
            {
                float angle = index * 360f / CircleSegments;
                Vector2 mapOffset = Rotate(Vector2.up, angle) * radiusMeters;
                points[index] = MapOffsetToWorld(
                    centre,
                    mapOffset,
                    map,
                    hasMap);
            }

            Handles.color = colour;
            if (!dotted)
            {
                Handles.DrawAAPolyLine(3f, points);
                return;
            }

            for (int index = 0; index < CircleSegments; index++)
                Handles.DrawDottedLine(points[index], points[index + 1], 5f);
        }

        private static void DrawVisionSector(
            Vector3 centre,
            Vector2 facingMapDirection,
            float radiusMeters,
            float fullAngleDegrees,
            MapTestSceneController map,
            bool hasMap)
        {
            if (radiusMeters <= 0f || fullAngleDegrees <= 0f)
                return;

            float clampedAngle = Mathf.Clamp(fullAngleDegrees, 0f, 360f);
            int segmentCount = Mathf.Max(
                2,
                Mathf.CeilToInt(VisionSegments * clampedAngle / 360f));
            var arcPoints = new Vector3[segmentCount + 1];
            float halfAngle = clampedAngle * 0.5f;
            for (int index = 0; index <= segmentCount; index++)
            {
                float progress = index / (float)segmentCount;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, progress);
                Vector2 direction = Rotate(facingMapDirection, angle);
                arcPoints[index] = MapOffsetToWorld(
                    centre,
                    direction * radiusMeters,
                    map,
                    hasMap);
            }

            Handles.color = VisionFillColour;
            for (int index = 0; index < segmentCount; index++)
            {
                Handles.DrawAAConvexPolygon(
                    centre,
                    arcPoints[index],
                    arcPoints[index + 1]);
            }

            Handles.color = VisionOutlineColour;
            Handles.DrawAAPolyLine(3f, arcPoints);
            if (clampedAngle < 359.999f)
            {
                Handles.DrawAAPolyLine(3f, centre, arcPoints[0]);
                Handles.DrawAAPolyLine(
                    3f,
                    centre,
                    arcPoints[arcPoints.Length - 1]);
            }
        }

        private static void DrawLabels(
            AnimalAgent agent,
            Vector3 homeCentre,
            Vector3 currentCentre,
            MapTestSceneController map,
            bool hasMap)
        {
            AnimalSpeciesConfig config = agent.Config;
            Vector3 activityLabelPosition = MapOffsetToWorld(
                homeCentre,
                Vector2.up * config.ActivityRadiusMeters,
                map,
                hasMap);
            Vector3 alertLabelPosition = MapOffsetToWorld(
                currentCentre,
                Vector2.right * config.AlertRadiusMeters,
                map,
                hasMap);
            Vector3 visionLabelPosition = MapOffsetToWorld(
                currentCentre,
                Vector2.up * Mathf.Max(0.3f, config.BodyRadiusMeters * 2f),
                map,
                hasMap);

            Handles.Label(
                activityLabelPosition,
                $"  Daily / {config.ActivityRadiusMeters:0.##} m",
                CreateLabelStyle(ActivityColour));
            Handles.Label(
                alertLabelPosition,
                $"  Alert + hearing / {config.AlertRadiusMeters:0.##} m",
                CreateLabelStyle(AlertColour));
            Handles.Label(
                visionLabelPosition,
                $"  Direct vision / {config.DirectVisionAngleDegrees:0.#}°  "
                + $"x{config.DirectLineOfSightDetectionMultiplier:0.##}",
                CreateLabelStyle(VisionOutlineColour));
        }

        private static GUIStyle CreateLabelStyle(Color colour)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                padding = new RectOffset(3, 3, 1, 1)
            };
            style.normal.textColor = colour;
            return style;
        }

        private static Vector3 MapOffsetToWorld(
            Vector3 centre,
            Vector2 mapOffset,
            MapTestSceneController map,
            bool hasMap)
        {
            if (!hasMap)
            {
                return centre + new Vector3(
                    mapOffset.x,
                    mapOffset.y,
                    0f) * PrefabPreviewWorldUnitsPerMapMeter;
            }

            float distanceMeters = mapOffset.magnitude;
            if (distanceMeters <= 0.000001f)
                return centre;

            Vector2 worldDirection = map.MapDirectionToWorldDirection(
                mapOffset / distanceMeters);
            float worldDistance = map.MapMetersToWorldDistance(
                worldDirection,
                distanceMeters);
            return centre + new Vector3(
                worldDirection.x,
                worldDirection.y,
                0f) * worldDistance;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }
    }
}
