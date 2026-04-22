using UnityEngine;
using UnityEngine.InputSystem;

namespace VoxelEngine.Debugging
{
    public static class VoxelDebugSettingsUiState
    {
        private static bool _isVisible;

        public static bool IsVisible => _isVisible;

        public static bool ConsumesPointerInput => _isVisible;

        public static string ToggleHintLabel => _isVisible ? "F5 Close Settings" : "F5 Open Settings";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _isVisible = false;
        }

        public static void ToggleVisibility()
        {
            _isVisible = !_isVisible;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("VoxelEngine/Debug/Camera Planar Movement Controller")]
    public sealed class CameraPlanarMovementController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 10.0f;
        [SerializeField] private float _lookSensitivity = 0.15f;
        [SerializeField] private float _minPitch = -89.0f;
        [SerializeField] private float _maxPitch = 89.0f;
        [SerializeField] private bool _invertY;
        [SerializeField] private bool _lockCursor = true;

        private float _yaw;
        private float _pitch;
        private bool _cursorUnlockedByUser;

        private void Awake()
        {
            SyncLookAnglesFromTransform();
        }

        private void OnEnable()
        {
            SyncLookAnglesFromTransform();
            _cursorUnlockedByUser = false;
            SetCursorLock(_lockCursor && !VoxelDebugSettingsUiState.ConsumesPointerInput);
        }

        private void OnDisable()
        {
            SetCursorLock(false);
        }

        private void OnValidate()
        {
            _moveSpeed = Mathf.Max(0.0f, _moveSpeed);
            _lookSensitivity = Mathf.Max(0.0f, _lookSensitivity);
            _minPitch = Mathf.Clamp(_minPitch, -89.0f, 89.0f);
            _maxPitch = Mathf.Clamp(_maxPitch, -89.0f, 89.0f);

            if (_maxPitch < _minPitch)
            {
                _maxPitch = _minPitch;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            UpdateSettingsToggle(keyboard);
            UpdateCursorLock(keyboard, mouse);
            UpdateLook(mouse);

            if (keyboard == null)
            {
                return;
            }

            Vector3 moveDirection = GetMoveDirection(keyboard);
            if (moveDirection.sqrMagnitude <= 0.0f)
            {
                return;
            }

            transform.position += moveDirection * (_moveSpeed * Time.deltaTime);
        }

        private void UpdateSettingsToggle(Keyboard keyboard)
        {
            if (!Application.isPlaying || keyboard == null || !keyboard.f5Key.wasPressedThisFrame)
            {
                return;
            }

            VoxelDebugSettingsUiState.ToggleVisibility();
            _cursorUnlockedByUser = false;

            if (!_lockCursor)
            {
                SetCursorLock(false);
                return;
            }

            SetCursorLock(!VoxelDebugSettingsUiState.ConsumesPointerInput);
        }

        private void UpdateCursorLock(Keyboard keyboard, Mouse mouse)
        {
            if (!_lockCursor || !Application.isPlaying)
            {
                return;
            }

            if (VoxelDebugSettingsUiState.ConsumesPointerInput)
            {
                _cursorUnlockedByUser = false;
                SetCursorLock(false);
                return;
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _cursorUnlockedByUser = true;
                SetCursorLock(false);
                return;
            }

            if (_cursorUnlockedByUser)
            {
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    _cursorUnlockedByUser = false;
                    SetCursorLock(true);
                }

                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            {
                SetCursorLock(true);
            }
        }

        private void UpdateLook(Mouse mouse)
        {
            if (mouse == null)
            {
                return;
            }

            if (VoxelDebugSettingsUiState.ConsumesPointerInput)
            {
                return;
            }

            if (_lockCursor && Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 lookDelta = mouse.delta.ReadValue();
            if (lookDelta.sqrMagnitude <= 0.0f)
            {
                return;
            }

            _yaw += lookDelta.x * _lookSensitivity;
            _pitch += lookDelta.y * (_invertY ? 1.0f : -1.0f) * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
        }

        private Vector3 GetMoveDirection(Keyboard keyboard)
        {
            float forwardInput = GetAxis(keyboard.wKey.isPressed, keyboard.sKey.isPressed);
            float rightInput = GetAxis(keyboard.dKey.isPressed, keyboard.aKey.isPressed);
            float verticalInput = GetAxis(
                keyboard.spaceKey.isPressed,
                keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

            Vector3 planarForward = GetPlanarForward();
            Vector3 planarRight = Vector3.Cross(Vector3.up, planarForward);

            Vector3 moveDirection =
                (planarForward * forwardInput) +
                (planarRight * rightInput) +
                (Vector3.up * verticalInput);

            return Vector3.ClampMagnitude(moveDirection, 1.0f);
        }

        private Vector3 GetPlanarForward()
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planarForward.sqrMagnitude <= Mathf.Epsilon)
            {
                planarForward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            }

            if (planarForward.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.forward;
            }

            return planarForward.normalized;
        }

        private void SyncLookAnglesFromTransform()
        {
            Vector3 eulerAngles = transform.rotation.eulerAngles;
            _yaw = NormalizeAngle(eulerAngles.y);
            _pitch = Mathf.Clamp(NormalizeAngle(eulerAngles.x), _minPitch, _maxPitch);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
        }

        private void SetCursorLock(bool shouldLock)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

        private static float GetAxis(bool positivePressed, bool negativePressed)
        {
            if (positivePressed == negativePressed)
            {
                return 0.0f;
            }

            return positivePressed ? 1.0f : -1.0f;
        }

        private static float NormalizeAngle(float angle)
        {
            if (angle > 180.0f)
            {
                angle -= 360.0f;
            }

            return angle;
        }
    }
}
