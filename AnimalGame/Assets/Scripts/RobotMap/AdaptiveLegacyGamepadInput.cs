using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public enum LegacyGamepadFamily
    {
        None,
        Xbox,
        Sony,
        Generic
    }

    /// <summary>
    /// Keeps the existing legacy Input Manager while selecting an axis layout
    /// that matches the connected Xbox/XInput or native Sony HID controller.
    /// </summary>
    public static class AdaptiveLegacyGamepadInput
    {
        private const string MoveAxis = "Gamepad Move";
        private const string TurnAxis = "Gamepad Turn";
        private const string XboxTriggerThrottleAxis =
            "Gamepad Trigger Throttle";
        private const string XboxBalanceHorizontalAxis =
            "Gamepad Balance Horizontal";
        private const string XboxBalanceVerticalAxis =
            "Gamepad Balance Vertical";
        private const string SonyBalanceHorizontalAxis =
            "Gamepad Sony Balance Horizontal";
        private const string SonyBalanceVerticalAxis =
            "Gamepad Sony Balance Vertical";
        private const string SonyLeftTriggerAxis =
            "Gamepad Sony Left Trigger";
        private const string SonyRightTriggerAxis =
            "Gamepad Sony Right Trigger";

        private static readonly HashSet<string> MissingAxes = new();
        private static float nextDeviceRefreshTime;
        private static bool sonyLeftTriggerIsBipolar;
        private static bool sonyRightTriggerIsBipolar;
        private static string activeDeviceName = string.Empty;

        public static LegacyGamepadFamily ActiveFamily { get; private set; }
        public static string ActiveDeviceName => activeDeviceName;
        public static bool HasConnectedGamepad =>
            ActiveFamily != LegacyGamepadFamily.None;

        public static float ReadMove()
        {
            RefreshDeviceIfNeeded();
            return HasConnectedGamepad ? ReadAxisSafely(MoveAxis) : 0f;
        }

        public static float ReadSteering()
        {
            RefreshDeviceIfNeeded();
            return HasConnectedGamepad ? ReadAxisSafely(TurnAxis) : 0f;
        }

        public static Vector2 ReadBalance()
        {
            RefreshDeviceIfNeeded();
            if (!HasConnectedGamepad)
                return Vector2.zero;

            return ActiveFamily == LegacyGamepadFamily.Sony
                ? new Vector2(
                    ReadAxisSafely(SonyBalanceHorizontalAxis),
                    ReadAxisSafely(SonyBalanceVerticalAxis))
                : new Vector2(
                    ReadAxisSafely(XboxBalanceHorizontalAxis),
                    ReadAxisSafely(XboxBalanceVerticalAxis));
        }

        public static float ReadTriggerThrottle()
        {
            RefreshDeviceIfNeeded();
            if (!HasConnectedGamepad)
                return 0f;

            if (ActiveFamily != LegacyGamepadFamily.Sony)
                return ReadAxisSafely(XboxTriggerThrottleAxis);

            float rawLeft = ReadAxisSafely(SonyLeftTriggerAxis);
            float rawRight = ReadAxisSafely(SonyRightTriggerAxis);
            if (rawLeft < -0.25f)
                sonyLeftTriggerIsBipolar = true;
            if (rawRight < -0.25f)
                sonyRightTriggerIsBipolar = true;

            float left = NormalizeSeparateTrigger(
                rawLeft,
                sonyLeftTriggerIsBipolar);
            float right = NormalizeSeparateTrigger(
                rawRight,
                sonyRightTriggerIsBipolar);
            return Mathf.Clamp(right - left, -1f, 1f);
        }

        public static void ForceDeviceRefresh()
        {
            nextDeviceRefreshTime = 0f;
            RefreshDeviceIfNeeded(true);
        }

        private static void RefreshDeviceIfNeeded(bool force = false)
        {
            if (!force && Time.unscaledTime < nextDeviceRefreshTime)
                return;

            nextDeviceRefreshTime = Time.unscaledTime + 0.75f;
            string[] names = Input.GetJoystickNames();
            string detectedName = string.Empty;
            LegacyGamepadFamily detectedFamily = LegacyGamepadFamily.None;
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    LegacyGamepadFamily family = DetectFamily(name);
                    if (detectedFamily == LegacyGamepadFamily.None
                        || family != LegacyGamepadFamily.Generic)
                    {
                        detectedName = name.Trim();
                        detectedFamily = family;
                    }

                    if (family == LegacyGamepadFamily.Sony
                        || family == LegacyGamepadFamily.Xbox)
                    {
                        break;
                    }
                }
            }

            if (detectedFamily == ActiveFamily
                && string.Equals(
                    detectedName,
                    activeDeviceName,
                    StringComparison.Ordinal))
            {
                return;
            }

            ActiveFamily = detectedFamily;
            activeDeviceName = detectedName;
            sonyLeftTriggerIsBipolar = false;
            sonyRightTriggerIsBipolar = false;
            if (ActiveFamily == LegacyGamepadFamily.None)
            {
                Debug.Log("Adaptive gamepad input: no controller detected.");
            }
            else
            {
                Debug.Log(
                    $"Adaptive gamepad input: '{activeDeviceName}' uses "
                    + $"{ActiveFamily} legacy axis layout.");
            }
        }

        private static LegacyGamepadFamily DetectFamily(string deviceName)
        {
            string name = deviceName.ToLowerInvariant();
            if (name.Contains("dualsense")
                || name.Contains("dualshock")
                || name.Contains("wireless controller")
                || name.Contains("playstation")
                || name.Contains("sony")
                || name.Contains("ps4")
                || name.Contains("ps5"))
            {
                return LegacyGamepadFamily.Sony;
            }

            if (name.Contains("xbox")
                || name.Contains("xinput")
                || name.Contains("x-box"))
            {
                return LegacyGamepadFamily.Xbox;
            }

            // Preserve the project's previous Xbox-style mapping for unknown
            // legacy devices instead of disabling controller input entirely.
            return LegacyGamepadFamily.Generic;
        }

        private static float ReadAxisSafely(string axisName)
        {
            if (MissingAxes.Contains(axisName))
                return 0f;

            try
            {
                return Input.GetAxisRaw(axisName);
            }
            catch (ArgumentException)
            {
                MissingAxes.Add(axisName);
                Debug.LogWarning(
                    $"Adaptive gamepad input axis '{axisName}' is missing. "
                    + "Exit Play Mode and run Animal Game/Repair Gamepad Input Axes.");
                return 0f;
            }
        }

        private static float NormalizeSeparateTrigger(
            float rawValue,
            bool bipolar)
        {
            return bipolar
                ? Mathf.Clamp01((rawValue + 1f) * 0.5f)
                : Mathf.Clamp01(rawValue);
        }
    }
}
