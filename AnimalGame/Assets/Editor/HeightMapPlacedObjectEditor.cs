using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HeightMapPlacedObject))]
public sealed class HeightMapPlacedObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var placedObject = (HeightMapPlacedObject)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Fixed Map Placement", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Capture Current Position"))
            {
                Undo.RecordObject(placedObject, "Capture Height Map Position");
                placedObject.CaptureCurrentTransform();
                EditorUtility.SetDirty(placedObject);
            }

            if (GUILayout.Button("Snap To Stored Position"))
            {
                Undo.RecordObject(
                    placedObject.transform,
                    "Snap To Stored Height Map Position");
                placedObject.SnapToStoredMapPosition();
                EditorUtility.SetDirty(placedObject.transform);
            }
        }
    }

    [MenuItem("Animal Game/Level/Anchor Selected Objects To Height Map")]
    private static void AnchorSelectedObjects()
    {
        foreach (GameObject selected in Selection.gameObjects)
        {
            if (selected == null || EditorUtility.IsPersistent(selected))
                continue;

            HeightMapPlacedObject placedObject =
                selected.GetComponent<HeightMapPlacedObject>();
            if (placedObject == null)
            {
                placedObject = Undo.AddComponent<HeightMapPlacedObject>(selected);
            }

            placedObject.CaptureCurrentTransform();
            EditorUtility.SetDirty(placedObject);
        }
    }
}
