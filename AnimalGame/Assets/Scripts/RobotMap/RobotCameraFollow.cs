using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(200)]
    public sealed class RobotCameraFollow : MonoBehaviour
    {
        [Header("Position Follow")]
        [SerializeField, Min(0f)] private float positionDamping = 0.18f;

        [Header("Rotation Follow")]
        [SerializeField, Min(0f)] private float rotationDamping = 0.22f;

        [Tooltip("Keeps the robot's forward direction at the top of the screen. The target may be the robot's balance-follow point rather than the body transform itself.")]
        [SerializeField] private bool followTargetRotation = true;

        public Transform Target { get; set; }

        private Vector3 followPoint;
        private Vector3 followVelocity;
        private float followAngle;
        private float followAngularVelocity;
        private float cameraDistance = 10f;
        private bool followPoseInitialized;

        public void SnapToTarget()
        {
            if (Target == null)
                return;

            cameraDistance = Mathf.Max(
                0.1f,
                Mathf.Abs(transform.position.z - Target.position.z));
            followPoint = Target.position;
            followAngle = followTargetRotation
                ? Target.eulerAngles.z
                : transform.eulerAngles.z;
            followVelocity = Vector3.zero;
            followAngularVelocity = 0f;
            followPoseInitialized = true;
            ApplyCameraPose();
        }

        private void LateUpdate()
        {
            if (Target == null)
                return;

            if (!followPoseInitialized)
                SnapToTarget();

            followPoint = Vector3.SmoothDamp(
                followPoint,
                Target.position,
                ref followVelocity,
                Mathf.Max(0.0001f, positionDamping));

            if (followTargetRotation)
            {
                followAngle = Mathf.SmoothDampAngle(
                    followAngle,
                    Target.eulerAngles.z,
                    ref followAngularVelocity,
                    Mathf.Max(0.0001f, rotationDamping));
            }

            ApplyCameraPose();
        }

        private void ApplyCameraPose()
        {
            Quaternion cameraRotation = Quaternion.Euler(0f, 0f, followAngle);
            Vector3 cameraPosition = followPoint
                                     - cameraRotation
                                     * Vector3.forward
                                     * cameraDistance;
            transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        private void OnValidate()
        {
            positionDamping = Mathf.Max(0f, positionDamping);
            rotationDamping = Mathf.Max(0f, rotationDamping);
        }
    }
}
