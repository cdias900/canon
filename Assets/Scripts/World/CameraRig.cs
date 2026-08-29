using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// Two views on one map.
    ///
    /// The default view is close and portrait, following the player with SmoothDamp and clamped to
    /// the map. The patrol view — the button the player sees is labelled "Ronda" — pulls back to
    /// <see cref="PatrolSize"/> and hands the player horizontal dragging, which is the only view
    /// where the whole wall is visible.
    ///
    /// Entering the patrol view scores the exile vocation in silence. The count is never exposed by
    /// this class and never reaches any UI.
    ///
    /// The seam is <see cref="SetPatrolView"/>: the HUD owns the button and pushes the state in
    /// here. Setting a state the rig already has does nothing, so two callers pushing the same
    /// value for one transition still count it once. Anything forwarding a "the view changed to X"
    /// event must pass that X through <see cref="SetPatrolView"/> — never through
    /// <see cref="TogglePatrolView"/>, which would flip the rig straight back and leave the button
    /// disagreeing with the camera. <see cref="TogglePatrolView"/> is for a caller that owns the
    /// decision itself and has no state to push.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public const float CloseSize = 7.5f;
        public const float PatrolSize = 20f;
        public const float FollowSmoothTime = 0.22f;
        public const float ZoomSmoothTime = 0.35f;
        public const float CameraZ = -10f;

        // Persisted counter keys. The strings live in save files that already exist, so they stay
        // exactly as authored even though the identifiers around them are English: renaming a key
        // would silently reset a player's progress.
        private const string PatrolUsesKey = "ronda_uses";
        private const string ExileAwardedKey = "exile_ronda_awarded";
        private const int PatrolUsesForVocation = 3;

        public bool IsPatrolView { get; private set; }

        public Transform Target { get; private set; }

        private Camera _camera;
        private TilemapBuilder _tilemap;
        private Vector3 _followVelocity;
        private float _sizeVelocity;
        private float _targetSize = CloseSize;
        private float _patrolX;
        private bool _dragging;
        private float _lastPointerX;

        public void Configure(TilemapBuilder tilemap, Transform target)
        {
            _tilemap = tilemap;
            Target = target;

            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                Debug.LogWarning("[World] CameraRig found no camera; the view will not follow the player.");
                return;
            }

            _camera.orthographic = true;
            _camera.orthographicSize = CloseSize;
            _targetSize = CloseSize;

            Vector3 start = Target != null
                ? new Vector3(Target.position.x, Target.position.y, CameraZ)
                : new Vector3(transform.position.x, transform.position.y, CameraZ);

            _patrolX = start.x;
            transform.position = ClampToBounds(start);
        }

        public void SetTarget(Transform target)
        {
            Target = target;
        }

        /// <summary>Switches between the close follow view and the wide patrol view.</summary>
        public void TogglePatrolView()
        {
            SetPatrolView(!IsPatrolView);
        }

        /// <summary>
        /// Enters or leaves the wide patrol view. Setting the state it already has does nothing at
        /// all, which is what lets more than one caller drive this without double counting.
        /// </summary>
        public void SetPatrolView(bool patrolView)
        {
            if (IsPatrolView == patrolView)
            {
                return;
            }

            IsPatrolView = patrolView;
            _targetSize = IsPatrolView ? PatrolSize : CloseSize;
            _dragging = false;

            if (!IsPatrolView)
            {
                return;
            }

            _patrolX = Target != null ? Target.position.x : transform.position.x;
            CountPatrolUse();
        }

        private void CountPatrolUse()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            state.Bump(PatrolUsesKey);
            if (state.Counter(PatrolUsesKey) >= PatrolUsesForVocation)
            {
                WorldRuntime.AwardOnce(ExileAwardedKey, WorldRuntime.VocationExile, 2);
            }

            WorldRuntime.SaveNow();
        }

        /// <summary>Horizontal pan in world units, for a drag driven by the UI layer.</summary>
        public void PanBy(float worldDeltaX)
        {
            if (!IsPatrolView)
            {
                return;
            }

            _patrolX += worldDeltaX;
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize, _targetSize, ref _sizeVelocity, ZoomSmoothTime);

            Vector3 desired;
            float smoothTime;

            if (IsPatrolView)
            {
                HandleDrag();
                float wallY = _tilemap != null
                    ? _tilemap.CellToWorldCenter(0, _tilemap.WallRowY).y
                    : transform.position.y;
                desired = new Vector3(_patrolX, wallY, CameraZ);
                smoothTime = _dragging ? 0.05f : 0.30f;
            }
            else
            {
                desired = Target != null
                    ? new Vector3(Target.position.x, Target.position.y, CameraZ)
                    : new Vector3(transform.position.x, transform.position.y, CameraZ);
                smoothTime = FollowSmoothTime;
            }

            Vector3 clamped = ClampToBounds(desired);
            Vector3 next = Vector3.SmoothDamp(transform.position, clamped, ref _followVelocity, smoothTime);
            next.z = CameraZ;
            transform.position = next;

            if (IsPatrolView)
            {
                _patrolX = clamped.x;
            }
        }

        private void HandleDrag()
        {
            Vector2 screenPosition;
            bool pressed;

            if (!PointerInput.TryRead(out screenPosition, out pressed) || !pressed)
            {
                _dragging = false;
                return;
            }

            if (IsPointerOverUserInterface())
            {
                _dragging = false;
                return;
            }

            if (!_dragging)
            {
                _dragging = true;
                _lastPointerX = screenPosition.x;
                return;
            }

            float screenWidth = Mathf.Max(1, Screen.width);
            float worldPerPixel = (2f * _camera.orthographicSize * _camera.aspect) / screenWidth;
            float delta = (screenPosition.x - _lastPointerX) * worldPerPixel;
            _lastPointerX = screenPosition.x;
            _patrolX -= delta;
        }

        private static bool IsPointerOverUserInterface()
        {
            try
            {
                EventSystem current = EventSystem.current;
                return current != null && current.IsPointerOverGameObject();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Vector3 ClampToBounds(Vector3 position)
        {
            position.z = CameraZ;

            if (_camera == null || _tilemap == null)
            {
                return position;
            }

            Bounds bounds = _tilemap.WorldBounds;
            if (bounds.size.x <= 0.01f || bounds.size.y <= 0.01f)
            {
                return position;
            }

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;

            float minX = bounds.min.x + halfWidth;
            float maxX = bounds.max.x - halfWidth;
            position.x = minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : bounds.center.x;

            float minY = bounds.min.y + halfHeight;
            float maxY = bounds.max.y - halfHeight;
            position.y = minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : bounds.center.y;

            return position;
        }

        /// <summary>
        /// Reads a pointer without knowing which input backend the project ships with. The legacy
        /// UnityEngine.Input path is tried first and disabled for good if the project runs on the
        /// Input System backend, where it throws; the Input System is then read reflectively so
        /// this file compiles whether or not the package is installed.
        /// </summary>
        private static class PointerInput
        {
            private static bool _legacyAvailable = true;
            private static bool _inputSystemAvailable = true;
            private static bool _probed;

            private static PropertyInfo _mouseCurrent;
            private static PropertyInfo _touchscreenCurrent;
            private static readonly Dictionary<string, PropertyInfo> PropertyCache = new Dictionary<string, PropertyInfo>();
            private static readonly Dictionary<Type, MethodInfo> ReadValueCache = new Dictionary<Type, MethodInfo>();

            public static bool TryRead(out Vector2 position, out bool pressed)
            {
                if (_legacyAvailable && TryReadLegacy(out position, out pressed))
                {
                    return true;
                }

                return TryReadInputSystem(out position, out pressed);
            }

            private static bool TryReadLegacy(out Vector2 position, out bool pressed)
            {
                position = Vector2.zero;
                pressed = false;

                try
                {
                    if (Input.touchCount > 0)
                    {
                        Touch touch = Input.GetTouch(0);
                        position = touch.position;
                        pressed = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
                        return true;
                    }

                    Vector3 mouse = Input.mousePosition;
                    position = new Vector2(mouse.x, mouse.y);
                    pressed = Input.GetMouseButton(0);
                    return true;
                }
                catch (Exception)
                {
                    // The project runs on the Input System backend; stop asking the legacy class.
                    _legacyAvailable = false;
                    position = Vector2.zero;
                    pressed = false;
                    return false;
                }
            }

            private static bool TryReadInputSystem(out Vector2 position, out bool pressed)
            {
                position = Vector2.zero;
                pressed = false;

                if (!_inputSystemAvailable)
                {
                    return false;
                }

                Probe();

                try
                {
                    if (_touchscreenCurrent != null)
                    {
                        object touchscreen = _touchscreenCurrent.GetValue(null, null);
                        if (touchscreen != null)
                        {
                            object touch = ReadProperty(touchscreen, "primaryTouch");
                            if (touch != null)
                            {
                                object press = ReadProperty(touch, "press");
                                if (press != null && ReadBool(press, "isPressed"))
                                {
                                    object touchPosition = ReadProperty(touch, "position");
                                    if (ReadVector2(touchPosition, out position))
                                    {
                                        pressed = true;
                                        return true;
                                    }
                                }
                            }
                        }
                    }

                    if (_mouseCurrent != null)
                    {
                        object mouse = _mouseCurrent.GetValue(null, null);
                        if (mouse != null)
                        {
                            object mousePosition = ReadProperty(mouse, "position");
                            if (ReadVector2(mousePosition, out position))
                            {
                                object leftButton = ReadProperty(mouse, "leftButton");
                                pressed = leftButton != null && ReadBool(leftButton, "isPressed");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    _inputSystemAvailable = false;
                    Debug.LogWarning("[World] Reading the Input System failed; the patrol drag is disabled: " + exception.Message);
                }

                return false;
            }

            private static void Probe()
            {
                if (_probed)
                {
                    return;
                }

                _probed = true;

                Type mouseType = TypeBridge.Find("UnityEngine.InputSystem.Mouse");
                if (mouseType != null)
                {
                    _mouseCurrent = mouseType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                }

                Type touchscreenType = TypeBridge.Find("UnityEngine.InputSystem.Touchscreen");
                if (touchscreenType != null)
                {
                    _touchscreenCurrent = touchscreenType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                }

                if (_mouseCurrent == null && _touchscreenCurrent == null)
                {
                    _inputSystemAvailable = false;
                }
            }

            private static object ReadProperty(object target, string propertyName)
            {
                if (target == null)
                {
                    return null;
                }

                Type type = target.GetType();
                string cacheKey = type.FullName + "." + propertyName;

                PropertyInfo property;
                if (!PropertyCache.TryGetValue(cacheKey, out property))
                {
                    property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    PropertyCache[cacheKey] = property;
                }

                if (property == null)
                {
                    return null;
                }

                return property.GetValue(target, null);
            }

            private static bool ReadBool(object control, string propertyName)
            {
                object value = ReadProperty(control, propertyName);
                return value is bool && (bool)value;
            }

            private static bool ReadVector2(object control, out Vector2 value)
            {
                value = Vector2.zero;
                if (control == null)
                {
                    return false;
                }

                Type type = control.GetType();
                MethodInfo readValue;
                if (!ReadValueCache.TryGetValue(type, out readValue) || readValue == null)
                {
                    readValue = type.GetMethod("ReadValue", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null);
                    ReadValueCache[type] = readValue;
                }

                if (readValue == null)
                {
                    return false;
                }

                object result = readValue.Invoke(control, null);
                if (result is Vector2)
                {
                    value = (Vector2)result;
                    return true;
                }

                return false;
            }
        }
    }
}
