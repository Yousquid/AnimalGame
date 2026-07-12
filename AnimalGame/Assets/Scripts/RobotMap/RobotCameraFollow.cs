using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed class RobotCameraFollow : MonoBehaviour
    {
        [Header("Position Follow")]
        [SerializeField, Min(0f)] private float positionDamping = 0.18f;

        [Header("Rotation Follow")]
        [SerializeField, Min(0f)] private float rotationDamping = 0.22f;

        [Tooltip("Keeps the robot's forward direction at the top of the screen.")]
        [SerializeField] private bool followTargetRotation = true;

        public Transform Target { get; set; }

        private Vector3 velocity;
        private float angularVelocity;

        public void SnapToTarget()
        {
            if (Target == null)
                return;

            transform.position = new Vector3(
                Target.position.x,
                Target.position.y,
                transform.position.z);
            if (followTargetRotation)
                transform.rotation = Quaternion.Euler(0f, 0f, Target.eulerAngles.z);

            velocity = Vector3.zero;
            angularVelocity = 0f;
        }

        private void LateUpdate()
        {
            if (Target == null)
                return;

            Vector3 destination = new Vector3(Target.position.x, Target.position.y, transform.position.z);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                destination,
                ref velocity,
                positionDamping);

            if (!followTargetRotation)
                return;

            float targetAngle = Target.eulerAngles.z;
            float angle = Mathf.SmoothDampAngle(
                transform.eulerAngles.z,
                targetAngle,
                ref angularVelocity,
                rotationDamping);

            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
