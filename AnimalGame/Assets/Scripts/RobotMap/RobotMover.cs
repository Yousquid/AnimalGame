using UnityEngine;
using AnimalGame.MapTest;

namespace AnimalGame.RobotMap
{
    public sealed class RobotMover : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float forwardSpeed = 5f;
        [SerializeField, Min(0f)] private float reverseSpeed = 3f;
        [SerializeField, Min(0f)] private float turnSpeed = 100f;

        [Header("Turning Feel")]
        [Tooltip("How quickly angular speed builds while holding a turn input, in degrees per second squared.")]
        [SerializeField, Min(0f)] private float turnAcceleration = 180f;

        [Tooltip("How quickly angular speed returns to zero after releasing the turn input.")]
        [SerializeField, Min(0f)] private float turnDeceleration = 240f;

        [Header("Input")]
        [Tooltip("Ignores small gamepad stick movement while keeping keyboard input unchanged.")]
        [SerializeField, Range(0f, 0.9f)] private float stickDeadZone = 0.15f;

        [Header("Acceleration Feel")]
        [Tooltip("Acceleration from rest. Lower values make the initial launch feel heavier.")]
        [SerializeField, Min(0f)] private float launchAcceleration = 2.2f;

        [Tooltip("Acceleration after the robot has started moving.")]
        [SerializeField, Min(0f)] private float runningAcceleration = 4.8f;

        [Tooltip("Natural deceleration after releasing the movement input.")]
        [SerializeField, Min(0f)] private float coastDeceleration = 2.6f;

        [Tooltip("Deceleration when pressing the opposite movement direction.")]
        [SerializeField, Min(0f)] private float brakingDeceleration = 8f;

        public float CurrentSpeed { get; private set; }
        public float CurrentTurnSpeed { get; private set; }
        public Vector2 MapPosition => transform.position;
        public Vector2 Forward => transform.up;
        public bool IsSlopeBlocked { get; private set; }
        public SlopeTraversalResult CurrentTraversalResult { get; private set; }

        private HeightMapTraversalEvaluator traversalEvaluator;

        public void SetTraversalEvaluator(HeightMapTraversalEvaluator evaluator)
        {
            traversalEvaluator = evaluator;
            IsSlopeBlocked = false;
            CurrentTraversalResult = SlopeTraversalResult.NoData;
        }

        private void Update()
        {
            float keyboardThrottle = ReadKeyboardAxis(KeyCode.S, KeyCode.DownArrow, KeyCode.W, KeyCode.UpArrow);
            float keyboardSteering = ReadKeyboardAxis(KeyCode.A, KeyCode.LeftArrow, KeyCode.D, KeyCode.RightArrow);
            float gamepadThrottle = ApplyDeadZone(Input.GetAxisRaw("Gamepad Move"));
            float gamepadSteering = ApplyDeadZone(Input.GetAxisRaw("Gamepad Turn"));

            float throttle = SelectStrongerInput(keyboardThrottle, gamepadThrottle);
            float steering = SelectStrongerInput(keyboardSteering, gamepadSteering);

            float targetSpeed = throttle >= 0f
                ? throttle * forwardSpeed
                : throttle * reverseSpeed;

            float speedChangeRate = GetSpeedChangeRate(throttle, targetSpeed);
            CurrentSpeed = Mathf.MoveTowards(
                CurrentSpeed,
                targetSpeed,
                speedChangeRate * Time.deltaTime);

            float targetTurnSpeed = steering * turnSpeed;
            float turnChangeRate = Mathf.Approximately(steering, 0f)
                ? turnDeceleration
                : turnAcceleration;
            CurrentTurnSpeed = Mathf.MoveTowards(
                CurrentTurnSpeed,
                targetTurnSpeed,
                turnChangeRate * Time.deltaTime);

            float reverseDirection = CurrentSpeed < -0.05f ? -1f : 1f;
            transform.Rotate(0f, 0f, -CurrentTurnSpeed * reverseDirection * Time.deltaTime);

            if (Mathf.Abs(CurrentSpeed) > 0.001f && traversalEvaluator != null)
            {
                Vector2 movementDirection = (Vector2)transform.up * Mathf.Sign(CurrentSpeed);
                CurrentTraversalResult = traversalEvaluator.EvaluateMovement(
                    transform.position,
                    movementDirection);
                IsSlopeBlocked = CurrentTraversalResult.HasData
                                 && !CurrentTraversalResult.IsPassable;

                if (IsSlopeBlocked)
                {
                    CurrentSpeed = Mathf.MoveTowards(
                        CurrentSpeed,
                        0f,
                        traversalEvaluator.BlockedBraking * Time.deltaTime);
                    return;
                }
            }
            else if (traversalEvaluator != null)
            {
                // Keep the forward terrain readout current while the robot is
                // stationary, including while the player rotates in place.
                CurrentTraversalResult = traversalEvaluator.EvaluateMovement(
                    transform.position,
                    transform.up);
                IsSlopeBlocked = false;
            }
            else if (traversalEvaluator == null)
            {
                IsSlopeBlocked = false;
                CurrentTraversalResult = SlopeTraversalResult.NoData;
            }

            transform.position += transform.up * (CurrentSpeed * Time.deltaTime);
        }

        private static float ReadKeyboardAxis(
            KeyCode negativeKey,
            KeyCode negativeAlternate,
            KeyCode positiveKey,
            KeyCode positiveAlternate)
        {
            bool negative = Input.GetKey(negativeKey) || Input.GetKey(negativeAlternate);
            bool positive = Input.GetKey(positiveKey) || Input.GetKey(positiveAlternate);
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }

        private static float SelectStrongerInput(float first, float second)
        {
            return Mathf.Abs(first) >= Mathf.Abs(second) ? first : second;
        }

        private float ApplyDeadZone(float value)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= stickDeadZone)
                return 0f;

            float remappedMagnitude = Mathf.InverseLerp(stickDeadZone, 1f, magnitude);
            return Mathf.Sign(value) * remappedMagnitude;
        }

        private float GetSpeedChangeRate(float throttle, float targetSpeed)
        {
            if (Mathf.Approximately(throttle, 0f))
                return coastDeceleration;

            bool isBraking = !Mathf.Approximately(CurrentSpeed, 0f)
                             && Mathf.Sign(targetSpeed) != Mathf.Sign(CurrentSpeed);
            if (isBraking)
                return brakingDeceleration;

            float relevantTopSpeed = targetSpeed >= 0f ? forwardSpeed : reverseSpeed;
            float normalizedSpeed = relevantTopSpeed > 0f
                ? Mathf.Clamp01(Mathf.Abs(CurrentSpeed) / relevantTopSpeed)
                : 0f;

            return Mathf.Lerp(launchAcceleration, runningAcceleration, normalizedSpeed);
        }
    }
}
