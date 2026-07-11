using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed class RobotMarkerView : MonoBehaviour
    {
        private LineRenderer ring;
        private LineRenderer heading;
        private LineRenderer tail;

        private void Awake()
        {
            Color white = new Color(0.92f, 0.98f, 1f, 1f);
            ring = RobotMapDemo.CreateLine(transform, "Body Ring", BuildCircle(0.34f, 48), 0.075f, white, 20, true);
            heading = RobotMapDemo.CreateLine(transform, "Heading", new[]
            {
                new Vector3(0f, 0.2f), new Vector3(0f, 0.86f),
                new Vector3(-0.12f, 0.68f), new Vector3(0f, 0.86f), new Vector3(0.12f, 0.68f)
            }, 0.065f, white, 21);
            tail = RobotMapDemo.CreateLine(transform, "Motion Tail", new[]
            {
                new Vector3(0f, -0.38f), new Vector3(0f, -0.38f)
            }, 0.05f, new Color(0.35f, 0.82f, 0.9f, 0.7f), 19);
        }

        private void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * 3.2f) * 0.035f;
            ring.transform.localScale = Vector3.one * pulse;

            RobotMover mover = GetComponent<RobotMover>();
            float tailLength = Mathf.Clamp(Mathf.Abs(mover.CurrentSpeed) * 0.2f, 0f, 1.1f);
            tail.SetPosition(1, new Vector3(0f, -0.38f - tailLength));
        }

        private static Vector3[] BuildCircle(float radius, int points)
        {
            var result = new Vector3[points];
            for (int i = 0; i < points; i++)
            {
                float angle = i / (float)points * Mathf.PI * 2f;
                result[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return result;
        }
    }
}
