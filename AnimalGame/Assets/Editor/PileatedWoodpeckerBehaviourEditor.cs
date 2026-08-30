using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(PileatedWoodpeckerBehaviour))]
    public sealed class PileatedWoodpeckerBehaviourEditor
        : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var behaviour = (PileatedWoodpeckerBehaviour)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Scene Tree Binding",
                EditorStyles.boldLabel);

            if (behaviour.BirthTree == null)
            {
                EditorGUILayout.HelpBox(
                    "No birth tree is bound. At runtime the woodpecker will "
                    + "fall back to the nearest valid tree, but binding a "
                    + "scene tree makes its activity centre and escape "
                    + "destination deterministic.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Birth tree: {behaviour.BirthTree.name} "
                    + $"({behaviour.BirthTree.HealthState})\n"
                    + "The bird returns to this tree when it flees.",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bind Nearest Tree"))
                    BindNearestTree(behaviour);

                using (new EditorGUI.DisabledScope(
                           behaviour.BirthTree == null))
                {
                    if (GUILayout.Button("Snap To Birth Perch"))
                        SnapToBirthPerch(behaviour);
                }
            }

            using (new EditorGUI.DisabledScope(
                       behaviour.BirthTree == null))
            {
                if (GUILayout.Button("Select Birth Tree"))
                {
                    Selection.activeObject = behaviour.BirthTree.gameObject;
                    EditorGUIUtility.PingObject(
                        behaviour.BirthTree.gameObject);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scene gizmos\n"
                + "Green: fixed activity range around the birth tree\n"
                + "White dotted line: bird-to-birth-tree binding\n"
                + "Cyan: current perch; orange: active flight target",
                MessageType.None);
        }

        private static void BindNearestTree(
            PileatedWoodpeckerBehaviour behaviour)
        {
            Undo.RecordObject(behaviour, "Bind Nearest Woodpecker Tree");
            if (!behaviour.BindNearestTree(false))
            {
                EditorUtility.DisplayDialog(
                    "No Tree Habitat Found",
                    "Add a healthy or dead tree prefab to this scene before "
                    + "binding the woodpecker.",
                    "OK");
                return;
            }

            MarkDirty(behaviour);
        }

        private static void SnapToBirthPerch(
            PileatedWoodpeckerBehaviour behaviour)
        {
            Undo.RecordObject(
                behaviour.transform,
                "Snap Woodpecker To Birth Perch");
            Undo.RecordObject(behaviour, "Snap Woodpecker To Birth Perch");
            if (!behaviour.SnapToBirthPerch())
            {
                EditorUtility.DisplayDialog(
                    "Cannot Find A Valid Perch",
                    "The bound tree and woodpecker must belong to a generated "
                    + "map before a map-space perch can be calculated.",
                    "OK");
                return;
            }

            MarkDirty(behaviour);
            EditorUtility.SetDirty(behaviour.transform);
        }

        private static void MarkDirty(
            PileatedWoodpeckerBehaviour behaviour)
        {
            EditorUtility.SetDirty(behaviour);
            if (behaviour.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);
        }
    }

    public static class PileatedWoodpeckerBehaviourGizmoDrawer
    {
        private const int CircleSegments = 72;
        private const float PreviewWorldUnitsPerMapMeter = 0.1f;

        private static readonly Color ActivityColour =
            new Color(0.25f, 0.95f, 0.5f, 0.95f);
        private static readonly Color BindingColour =
            new Color(0.92f, 0.98f, 1f, 0.82f);
        private static readonly Color CurrentTreeColour =
            new Color(0.2f, 0.88f, 1f, 0.95f);
        private static readonly Color TargetTreeColour =
            new Color(1f, 0.62f, 0.14f, 0.95f);

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawWoodpeckerTreeState(
            PileatedWoodpeckerBehaviour behaviour,
            GizmoType gizmoType)
        {
            if (behaviour == null)
                return;

            AnimalAgent agent = behaviour.GetComponent<AnimalAgent>();
            TreeHabitat birthTree = behaviour.BirthTree;
            MapTestSceneController map = ResolveMap(behaviour, birthTree);
            bool hasMap = map != null && map.HasGeneratedMap;

            if (birthTree != null)
            {
                Vector3 centre = birthTree.TrunkWorldPosition;
                centre.z = behaviour.transform.position.z;
                Handles.color = BindingColour;
                Handles.DrawDottedLine(
                    behaviour.transform.position,
                    centre,
                    6f);

                if (agent != null && agent.Config != null)
                {
                    DrawMapCircle(
                        centre,
                        agent.Config.ActivityRadiusMeters,
                        map,
                        hasMap);
                    Vector3 labelPosition = MapOffsetToWorld(
                        centre,
                        Vector2.up * agent.Config.ActivityRadiusMeters,
                        map,
                        hasMap);
                    Handles.Label(
                        labelPosition,
                        $"  Woodpecker home / "
                        + $"{agent.Config.ActivityRadiusMeters:0.##} m",
                        CreateLabelStyle(ActivityColour));
                }

                Handles.Label(
                    centre,
                    $"  Birth tree / {birthTree.HealthState}",
                    CreateLabelStyle(BindingColour));
            }

            DrawTreeLink(
                behaviour,
                behaviour.CurrentTree,
                CurrentTreeColour,
                "Current perch");
            DrawTreeLink(
                behaviour,
                behaviour.TargetTree,
                TargetTreeColour,
                "Flight target");
        }

        private static void DrawTreeLink(
            PileatedWoodpeckerBehaviour behaviour,
            TreeHabitat tree,
            Color colour,
            string label)
        {
            if (tree == null || tree == behaviour.BirthTree)
                return;

            Vector3 target = tree.TrunkWorldPosition;
            target.z = behaviour.transform.position.z;
            Handles.color = colour;
            Handles.DrawAAPolyLine(
                3f,
                behaviour.transform.position,
                target);
            Handles.Label(target, $"  {label}", CreateLabelStyle(colour));
        }

        private static void DrawMapCircle(
            Vector3 centre,
            float radiusMeters,
            MapTestSceneController map,
            bool hasMap)
        {
            if (radiusMeters <= 0f)
                return;

            var points = new Vector3[CircleSegments + 1];
            for (int index = 0; index <= CircleSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / CircleSegments;
                Vector2 offset = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * radiusMeters;
                points[index] = MapOffsetToWorld(
                    centre,
                    offset,
                    map,
                    hasMap);
            }

            Handles.color = ActivityColour;
            Handles.DrawAAPolyLine(3f, points);
        }

        private static MapTestSceneController ResolveMap(
            PileatedWoodpeckerBehaviour behaviour,
            TreeHabitat birthTree)
        {
            HeightMapPlacedObject placedObject =
                behaviour.GetComponent<HeightMapPlacedObject>();
            if (placedObject != null && placedObject.Map != null)
                return placedObject.Map;
            if (birthTree != null && birthTree.Map != null)
                return birthTree.Map;
            return Object.FindObjectOfType<MapTestSceneController>();
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
                    0f) * PreviewWorldUnitsPerMapMeter;
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
    }
}
