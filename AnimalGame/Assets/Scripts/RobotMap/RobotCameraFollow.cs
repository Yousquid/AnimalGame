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

        [Header("Manual Imbalance Preview")]
        [Tooltip("Enables number-key camera imbalance presets. This is visual only and does not read or modify terrain traversal.")]
        [SerializeField] private bool enableManualImbalancePreview = true;

        [Tooltip("Target imbalance used by keys 1 and 4.")]
        [SerializeField, Range(0f, 1f)] private float lightImbalance = 0.3f;

        [Tooltip("Target imbalance used by keys 2 and 5.")]
        [SerializeField, Range(0f, 1f)] private float mediumImbalance = 0.62f;

        [Tooltip("Target imbalance used by keys 3 and 6.")]
        [SerializeField, Range(0f, 1f)] private float severeImbalance = 1f;

        [Tooltip("Maximum screen roll at severe imbalance. The fixed screen-space UI does not rotate.")]
        [SerializeField, Range(0f, 24f)] private float maximumRollAngle = 11f;

        [Tooltip("Maximum off-axis yaw at severe imbalance. This makes the orthographic camera observe the 2D map from the side instead of remaining perfectly perpendicular.")]
        [SerializeField, Range(0f, 30f)] private float maximumYawAngle = 16f;

        [Tooltip("Maximum camera-centre displacement in world units at severe imbalance.")]
        [SerializeField, Min(0f)] private float maximumLateralOffset = 0.84f;

        [Tooltip("Natural frequency of the imbalance spring. Lower values take longer to reach the selected preset.")]
        [SerializeField, Range(0.1f, 5f)] private float imbalanceSpringFrequency = 0.85f;

        [Tooltip("Damping ratio of the imbalance spring. Values below 1 allow a small natural overshoot before settling.")]
        [SerializeField, Range(0.1f, 1.5f)] private float imbalanceDampingRatio = 0.58f;

        [Tooltip("Very small smooth angular variation while imbalanced. This avoids a perfectly static mechanical pose without creating frame-to-frame jitter.")]
        [SerializeField, Range(0f, 4f)] private float imbalanceNoiseAngle = 0.48f;

        [Tooltip("Frequency in Hz of the smooth imbalance variation.")]
        [SerializeField, Range(0.05f, 3f)] private float imbalanceNoiseFrequency = 0.42f;

        public Transform Target { get; set; }
        public float CurrentPreviewImbalance => currentImbalance;
        public float TargetPreviewImbalance => targetImbalance;

        private Vector3 followPoint;
        private Vector3 followVelocity;
        private float followAngle;
        private float followAngularVelocity;
        private float cameraDistance = 10f;
        private float targetImbalance;
        private float currentImbalance;
        private float imbalanceVelocity;
        private float imbalanceNoiseSeed;
        private bool followPoseInitialized;

        private void Awake()
        {
            imbalanceNoiseSeed = Mathf.Abs(GetInstanceID() * 0.0137f) + 17.3f;
        }

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

            ReadManualImbalanceKeys();
            UpdateImbalanceSpring();

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

        private void ReadManualImbalanceKeys()
        {
            if (!enableManualImbalancePreview)
            {
                targetImbalance = 0f;
                return;
            }

            if (PreviewKeyDown(KeyCode.Alpha1, KeyCode.Keypad1))
                targetImbalance = -lightImbalance;
            else if (PreviewKeyDown(KeyCode.Alpha2, KeyCode.Keypad2))
                targetImbalance = -mediumImbalance;
            else if (PreviewKeyDown(KeyCode.Alpha3, KeyCode.Keypad3))
                targetImbalance = -severeImbalance;
            else if (PreviewKeyDown(KeyCode.Alpha4, KeyCode.Keypad4))
                targetImbalance = lightImbalance;
            else if (PreviewKeyDown(KeyCode.Alpha5, KeyCode.Keypad5))
                targetImbalance = mediumImbalance;
            else if (PreviewKeyDown(KeyCode.Alpha6, KeyCode.Keypad6))
                targetImbalance = severeImbalance;
            else if (PreviewKeyDown(KeyCode.Alpha0, KeyCode.Keypad0))
                targetImbalance = 0f;
        }

        private void UpdateImbalanceSpring()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            if (deltaTime <= 0f)
                return;

            float angularFrequency = 2f
                                     * Mathf.PI
                                     * Mathf.Max(0.1f, imbalanceSpringFrequency);
            float springAcceleration = angularFrequency
                                       * angularFrequency
                                       * (targetImbalance - currentImbalance);
            float dampingAcceleration = 2f
                                        * Mathf.Max(0.1f, imbalanceDampingRatio)
                                        * angularFrequency
                                        * imbalanceVelocity;
            imbalanceVelocity += (springAcceleration - dampingAcceleration)
                                 * deltaTime;
            currentImbalance += imbalanceVelocity * deltaTime;

            if (Mathf.Abs(currentImbalance) > 1.25f)
            {
                currentImbalance = Mathf.Clamp(currentImbalance, -1.25f, 1.25f);
                imbalanceVelocity *= 0.35f;
            }
        }

        private void ApplyCameraPose()
        {
            float imbalanceMagnitude = Mathf.Clamp01(Mathf.Abs(currentImbalance));
            float smoothNoise = (Mathf.PerlinNoise(
                                     imbalanceNoiseSeed,
                                     Time.time * imbalanceNoiseFrequency)
                                 * 2f
                                 - 1f)
                                * imbalanceNoiseAngle
                                * imbalanceMagnitude;
            float noiseNormalizationAngle = Mathf.Max(
                1f,
                Mathf.Max(maximumRollAngle, maximumYawAngle));
            float signedVisualImbalance = currentImbalance
                                          + smoothNoise
                                          / noiseNormalizationAngle;

            Quaternion baseRotation = Quaternion.Euler(0f, 0f, followAngle);
            Quaternion imbalanceRotation = Quaternion.Euler(
                0f,
                signedVisualImbalance * maximumYawAngle,
                signedVisualImbalance * maximumRollAngle);
            Quaternion cameraRotation = baseRotation * imbalanceRotation;

            Vector3 lateralOffset = baseRotation
                                    * Vector3.right
                                    * (signedVisualImbalance
                                       * maximumLateralOffset);
            Vector3 cameraPosition = followPoint
                                     - cameraRotation * Vector3.forward
                                     * cameraDistance
                                     + lateralOffset;
            transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        private static bool PreviewKeyDown(KeyCode alphaKey, KeyCode keypadKey)
        {
            return Input.GetKeyDown(alphaKey) || Input.GetKeyDown(keypadKey);
        }

        private void OnValidate()
        {
            positionDamping = Mathf.Max(0f, positionDamping);
            rotationDamping = Mathf.Max(0f, rotationDamping);
            lightImbalance = Mathf.Clamp01(lightImbalance);
            mediumImbalance = Mathf.Clamp01(mediumImbalance);
            severeImbalance = Mathf.Clamp01(severeImbalance);
            maximumRollAngle = Mathf.Clamp(maximumRollAngle, 0f, 24f);
            maximumYawAngle = Mathf.Clamp(maximumYawAngle, 0f, 30f);
            maximumLateralOffset = Mathf.Max(0f, maximumLateralOffset);
            imbalanceSpringFrequency = Mathf.Clamp(
                imbalanceSpringFrequency,
                0.1f,
                5f);
            imbalanceDampingRatio = Mathf.Clamp(
                imbalanceDampingRatio,
                0.1f,
                1.5f);
            imbalanceNoiseAngle = Mathf.Clamp(imbalanceNoiseAngle, 0f, 4f);
            imbalanceNoiseFrequency = Mathf.Clamp(
                imbalanceNoiseFrequency,
                0.05f,
                3f);
        }
    }
}
