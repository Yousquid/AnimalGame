using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed class RobotMapDemo : MonoBehaviour
    {
        private RobotMover mover;
        private Camera demoCamera;

        private void Awake()
        {
            CreateGrid();
            CreateContourBackdrop();
            CreateRobot();
            CreateCamera();
        }

        private void CreateCamera()
        {
            Camera sceneCamera = Camera.main;
            GameObject cameraPrefab = Resources.Load<GameObject>("Camera/RobotCamera");

            if (cameraPrefab != null)
            {
                GameObject cameraObject = Instantiate(cameraPrefab);
                cameraObject.name = "Robot Camera";
                demoCamera = cameraObject.GetComponent<Camera>();
            }
            else
            {
                var cameraObject = new GameObject("Robot Camera");
                cameraObject.tag = "MainCamera";
                demoCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<RobotCameraFollow>();
                ConfigureFallbackCamera(demoCamera);
            }

            if (sceneCamera != null && sceneCamera != demoCamera)
                sceneCamera.gameObject.SetActive(false);

            demoCamera.GetComponent<RobotCameraFollow>().Target = mover.transform;
        }

        private static void ConfigureFallbackCamera(Camera camera)
        {
            camera.orthographic = true;
            camera.orthographicSize = 14f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.008f, 0.011f, 0.014f);
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

        private void CreateGrid()
        {
            var grid = new GameObject("Sensor Grid");
            grid.transform.SetParent(transform);

            Color gridColor = new Color(0.12f, 0.17f, 0.19f, 0.42f);
            for (int i = -30; i <= 30; i++)
            {
                CreateLine(grid.transform, $"Grid V {i}", new[]
                {
                    new Vector3(i, -30f), new Vector3(i, 30f)
                }, 0.018f, gridColor, -10);

                CreateLine(grid.transform, $"Grid H {i}", new[]
                {
                    new Vector3(-30f, i), new Vector3(30f, i)
                }, 0.018f, gridColor, -10);
            }
        }

        private void CreateContourBackdrop()
        {
            var contours = new GameObject("Contour Backdrop");
            contours.transform.SetParent(transform);
            Color contourColor = new Color(0.5f, 0.57f, 0.58f, 0.58f);

            CreateContourFamily(contours.transform, new Vector2(-8f, 4f), new Vector2(8f, 5.3f), 4, contourColor);
            CreateContourFamily(contours.transform, new Vector2(9f, 7f), new Vector2(4.5f, 3.5f), 3, contourColor);
            CreateContourFamily(contours.transform, new Vector2(8f, -8f), new Vector2(6f, 4.2f), 3, contourColor);
            CreateContourFamily(contours.transform, new Vector2(-10f, -9f), new Vector2(5.5f, 4.5f), 2, contourColor);
        }

        private void CreateContourFamily(Transform parent, Vector2 center, Vector2 radius, int count, Color color)
        {
            for (int layer = 0; layer < count; layer++)
            {
                const int pointCount = 96;
                var points = new Vector3[pointCount];
                float inset = layer * 0.72f;

                for (int i = 0; i < pointCount; i++)
                {
                    float angle = i / (float)(pointCount - 1) * Mathf.PI * 2f;
                    float irregularity = Mathf.Sin(angle * 3f + layer) * 0.24f
                                        + Mathf.Sin(angle * 7f - layer * 0.4f) * 0.1f;
                    float x = center.x + Mathf.Cos(angle) * (radius.x - inset + irregularity);
                    float y = center.y + Mathf.Sin(angle) * (radius.y - inset + irregularity);
                    points[i] = new Vector3(x, y, 0f);
                }

                CreateLine(parent, $"Contour {center} {layer}", points, 0.045f, color, -5, true);
            }
        }

        private void CreateRobot()
        {
            GameObject robotPrefab = Resources.Load<GameObject>("Robot/RobotMarker");
            GameObject robot = robotPrefab != null
                ? Instantiate(robotPrefab)
                : new GameObject("Robot Marker");

            robot.name = "Robot Marker";
            robot.transform.SetParent(transform);

            mover = robot.GetComponent<RobotMover>();
            if (mover == null)
                mover = robot.AddComponent<RobotMover>();

            if (robot.GetComponent<RobotMarkerView>() == null)
                robot.AddComponent<RobotMarkerView>();
        }

        internal static LineRenderer CreateLine(Transform parent, string name, IReadOnlyList<Vector3> points,
            float width, Color color, int sortingOrder, bool loop = false)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
                line.SetPosition(i, points[i]);

            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.sortingOrder = sortingOrder;
            line.material = new Material(Shader.Find("Sprites/Default"));
            return line;
        }
    }
}
