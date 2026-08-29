using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using SheepGate.Core;
using SheepGate.Dialogue;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SheepGate.Player
{
    /// <summary>
    /// Tap-to-move avatar. A tap on open ground paths the player there; a tap on an interactable
    /// walks to an adjacent cell and then fires its interaction. Taps that land on UI, or that
    /// arrive while dialogue or a modal panel owns the screen, are ignored.
    ///
    /// Input is read defensively from both the Input System and the legacy manager so the project
    /// behaves the same whichever backend is active, and so a missing device is never a null
    /// reference.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        /// <summary>World units per second. One grid cell is one world unit.</summary>
        public const float DefaultMoveSpeed = 3.4f;

        /// <summary>How close to a tap an interactable must be, in world units, to claim it.</summary>
        public const float TapRadius = 0.9f;

        private const float ArriveEpsilon = 0.02f;
        private const float InteractableScanInterval = 1f;
        private const float DependencyRetryInterval = 0.5f;
        private const int NearestWalkableSearchRadius = 4;

        private readonly List<Vector2Int> _path = new List<Vector2Int>();
        private readonly List<Component> _interactables = new List<Component>();
        private readonly Vector2Int[] _approachCandidates = new Vector2Int[5];

        private CharacterAppearance _appearance;
        private GridPathfinder _pathfinder;
        private Camera _camera;
        private DialogueSystem _dialogue;

        private Action _onArrive;
        private bool[,] _walkableSource;
        private bool _pathfinderPinned;
        private int _pathIndex;
        private float _nextInteractableScan;
        private float _nextDialogueLookup;

        /// <summary>Fires with the cell reached whenever a walk finishes.</summary>
        public event Action<Vector2Int> Arrived;

        /// <summary>World units per second.</summary>
        public float MoveSpeed { get; set; } = DefaultMoveSpeed;

        /// <summary>Set false to suspend tap handling without touching the modal lock.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// Grid used for pathing.
        ///
        /// Resolved lazily so the component survives being created before the world composer has
        /// run, and re-pointed automatically when the tilemap publishes a new walkable array, so
        /// the player can never end up pathing over a grid that no longer matches the drawn map.
        /// The array itself is shared by reference, so wall stages rising and rubble being cleared
        /// take effect without any polling here.
        ///
        /// Assigning the property pins it: an explicitly injected pathfinder is never replaced.
        /// </summary>
        public GridPathfinder Pathfinder
        {
            get
            {
                if (!_pathfinderPinned)
                {
                    bool[,] live;
                    if (MapGrid.TryGetWorldWalkable(out live) && !ReferenceEquals(live, _walkableSource))
                    {
                        _walkableSource = live;
                        _pathfinder = new GridPathfinder(live);
                    }
                }

                if (_pathfinder == null) _pathfinder = MapGrid.CreatePathfinder();
                return _pathfinder;
            }
            set
            {
                _pathfinder = value;
                _pathfinderPinned = value != null;
                _walkableSource = null;
            }
        }

        /// <summary>The layered sprite stack. Created automatically if absent.</summary>
        public CharacterAppearance Appearance
        {
            get
            {
                if (_appearance == null) _appearance = CharacterAppearance.CreateOn(gameObject);
                return _appearance;
            }
        }

        public Vector2Int GridPosition
        {
            get { return GridPathfinder.WorldToGrid(transform.position); }
        }

        public bool IsMoving
        {
            get { return _pathIndex < _path.Count; }
        }

        /// <summary>Builds a ready-to-use player GameObject at a world position.</summary>
        public static PlayerController Spawn(Vector2 worldPosition, AppearanceState appearance)
        {
            var host = new GameObject("Player");
            var controller = host.AddComponent<PlayerController>();
            controller.Teleport(worldPosition);
            if (appearance != null) controller.Appearance.Apply(appearance);
            return controller;
        }

        private void Awake()
        {
            _appearance = CharacterAppearance.CreateOn(gameObject);
            _appearance.SetAnimation(CharacterAppearance.AnimationIdle);

            try
            {
                ServiceLocator.Register<PlayerController>(this);
            }
            catch (Exception)
            {
                // A service locator that is not ready yet is not a reason to fail to spawn.
            }
        }

        private void Update()
        {
            HandleInput();
            StepMovement(Time.deltaTime);
        }

        // ---------------------------------------------------------------- movement

        /// <summary>Paths to the tapped world position, cancelling any pending interaction.</summary>
        public void MoveTo(Vector2 worldPosition)
        {
            MoveToWorld(worldPosition, null);
        }

        /// <summary>Paths to a world position and runs a callback on arrival. False when unreachable.</summary>
        public bool MoveToWorld(Vector2 worldPosition, Action onArrive)
        {
            GridPathfinder pathfinder = Pathfinder;
            Vector2Int goal = GridPathfinder.WorldToGrid(worldPosition);

            if (!pathfinder.IsWalkable(goal))
            {
                Vector2Int nearest;
                if (!pathfinder.TryFindNearestWalkable(goal, NearestWalkableSearchRadius, out nearest)) return false;
                goal = nearest;
            }

            return MoveToCell(goal, onArrive);
        }

        /// <summary>Paths to a grid cell. False when the cell is blocked or unreachable.</summary>
        public bool MoveToCell(Vector2Int cell, Action onArrive)
        {
            GridPathfinder pathfinder = Pathfinder;
            Vector2Int start = GridPosition;

            if (start == cell)
            {
                _path.Clear();
                _pathIndex = 0;
                _onArrive = onArrive;
                FinishArrival();
                return true;
            }

            if (!pathfinder.FindPath(start, cell, _path))
            {
                // Full stop rather than a bare path clear: a tap onto a cell that became
                // unreachable mid-walk must not leave the player frozen between tiles with the
                // walk animation running and a stale arrival callback still armed.
                StopMoving();
                return false;
            }

            _pathIndex = 0;
            _onArrive = onArrive;
            Appearance.SetAnimation(CharacterAppearance.AnimationWalk);
            Appearance.SetDirectionFromDelta(GridPathfinder.GridToWorld(_path[0]) - (Vector2)transform.position);
            return true;
        }

        /// <summary>Halts immediately and drops any queued arrival callback.</summary>
        public void StopMoving()
        {
            _path.Clear();
            _pathIndex = 0;
            _onArrive = null;
            Appearance.SetAnimation(CharacterAppearance.AnimationIdle);
        }

        /// <summary>Places the player without walking. Keeps the current z.</summary>
        public void Teleport(Vector2 worldPosition)
        {
            StopMoving();
            transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
        }

        public void TeleportToCell(Vector2Int cell)
        {
            Teleport(GridPathfinder.GridToWorld(cell));
        }

        private void StepMovement(float deltaTime)
        {
            if (_pathIndex >= _path.Count) return;

            float budget = MoveSpeed * deltaTime;
            Vector2 position = transform.position;

            while (budget > 0f && _pathIndex < _path.Count)
            {
                Vector2 waypoint = GridPathfinder.GridToWorld(_path[_pathIndex]);
                Vector2 delta = waypoint - position;
                float distance = delta.magnitude;

                if (distance <= budget + ArriveEpsilon)
                {
                    position = waypoint;
                    budget -= distance;
                    _pathIndex++;

                    if (_pathIndex < _path.Count)
                    {
                        Vector2 next = GridPathfinder.GridToWorld(_path[_pathIndex]);
                        Appearance.SetDirectionFromDelta(next - position);
                    }
                }
                else
                {
                    position += delta * (budget / distance);
                    Appearance.SetDirectionFromDelta(delta);
                    budget = 0f;
                }
            }

            transform.position = new Vector3(position.x, position.y, transform.position.z);

            if (_pathIndex >= _path.Count) FinishArrival();
        }

        private void FinishArrival()
        {
            _path.Clear();
            _pathIndex = 0;
            Appearance.SetAnimation(CharacterAppearance.AnimationIdle);

            Action callback = _onArrive;
            _onArrive = null;

            Vector2Int cell = GridPosition;
            if (callback != null) callback();

            Action<Vector2Int> handler = Arrived;
            if (handler != null) handler(cell);
        }

        // ---------------------------------------------------------------- interaction

        private void HandleTap(Vector2 worldPosition)
        {
            Component target = FindInteractableNear(worldPosition);
            if (target != null)
            {
                ApproachAndInteract(target);
                return;
            }

            MoveTo(worldPosition);
        }

        private void ApproachAndInteract(Component target)
        {
            Vector2 targetWorld = target.transform.position;
            Vector2Int targetCell = GridPathfinder.WorldToGrid(targetWorld);
            Vector2Int playerCell = GridPosition;

            if (ManhattanDistance(playerCell, targetCell) <= 1)
            {
                StopMoving();
                FaceAndInteract(target);
                return;
            }

            _approachCandidates[0] = new Vector2Int(targetCell.x - 1, targetCell.y);
            _approachCandidates[1] = new Vector2Int(targetCell.x + 1, targetCell.y);
            _approachCandidates[2] = new Vector2Int(targetCell.x, targetCell.y - 1);
            _approachCandidates[3] = new Vector2Int(targetCell.x, targetCell.y + 1);
            _approachCandidates[4] = targetCell; // last resort: the interactable has no collider footprint

            SortCandidatesByDistance(playerCell);

            GridPathfinder pathfinder = Pathfinder;
            Component captured = target;

            for (int i = 0; i < _approachCandidates.Length; i++)
            {
                Vector2Int candidate = _approachCandidates[i];
                if (!pathfinder.IsWalkable(candidate)) continue;
                if (MoveToCell(candidate, () => FaceAndInteract(captured))) return;
            }

            // Nothing adjacent is reachable: get as close as the map allows, without interacting.
            MoveTo(targetWorld);
        }

        private void SortCandidatesByDistance(Vector2Int origin)
        {
            // Insertion sort over five entries: shorter than any comparer allocation.
            for (int i = 1; i < _approachCandidates.Length; i++)
            {
                Vector2Int current = _approachCandidates[i];
                int currentDistance = ManhattanDistance(origin, current);
                int j = i - 1;
                while (j >= 0 && ManhattanDistance(origin, _approachCandidates[j]) > currentDistance)
                {
                    _approachCandidates[j + 1] = _approachCandidates[j];
                    j--;
                }
                _approachCandidates[j + 1] = current;
            }
        }

        private void FaceAndInteract(Component target)
        {
            if (target == null) return;
            Appearance.SetDirectionFromDelta((Vector2)target.transform.position - (Vector2)transform.position);
            InteractBridge.Invoke(target);
        }

        private Component FindInteractableNear(Vector2 worldPosition)
        {
            RefreshInteractables(false);

            Component best = null;
            float bestDistance = TapRadius * TapRadius;

            for (int i = 0; i < _interactables.Count; i++)
            {
                Component candidate = _interactables[i];
                if (candidate == null) continue;

                Vector2 candidatePosition = candidate.transform.position;
                float distance = (candidatePosition - worldPosition).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        /// <summary>
        /// Rebuilds the interactable list by scanning behaviours for an interaction entry point.
        /// The scene holds a few dozen objects and the scan is throttled, so this stays cheaper
        /// than requiring the world module to register colliders it has no contract to provide.
        /// </summary>
        public void RefreshInteractables(bool force)
        {
            if (!force && Time.unscaledTime < _nextInteractableScan) return;
            _nextInteractableScan = Time.unscaledTime + InteractableScanInterval;

            _interactables.Clear();

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (behaviour.transform == transform) continue;
                if (behaviour.transform.IsChildOf(transform)) continue;
                if (!InteractBridge.CanInteract(behaviour)) continue;
                _interactables.Add(behaviour);
            }
        }

        private static int ManhattanDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        // ---------------------------------------------------------------- input

        private void HandleInput()
        {
            if (!InputEnabled) return;
            if (IsInputBlocked()) return;

            Vector2 screenPosition;
            bool overUI;
            if (!TryReadTap(out screenPosition, out overUI)) return;
            if (overUI) return;

            Vector2 worldPosition;
            if (!TryScreenToWorld(screenPosition, out worldPosition)) return;

            HandleTap(worldPosition);
        }

        private bool IsInputBlocked()
        {
            if (InputLock.IsLocked) return true;

            DialogueSystem dialogue = ResolveDialogue();
            if (dialogue != null && dialogue.IsPlaying) return true;

            return false;
        }

        private DialogueSystem ResolveDialogue()
        {
            if (_dialogue != null) return _dialogue;

            // Absent dialogue means "nothing is playing", never "block the player forever".
            if (Time.unscaledTime < _nextDialogueLookup) return null;
            _nextDialogueLookup = Time.unscaledTime + DependencyRetryInterval;

            try
            {
                DialogueSystem registered;
                if (ServiceLocator.TryGet<DialogueSystem>(out registered) && registered != null)
                {
                    _dialogue = registered;
                    return _dialogue;
                }
            }
            catch (Exception)
            {
                // Fall through to the scene scan.
            }

            _dialogue = FindFirstObjectByType<DialogueSystem>();
            return _dialogue;
        }

        private Camera ResolveCamera()
        {
            if (_camera != null) return _camera;
            _camera = Camera.main;
            if (_camera == null) _camera = FindFirstObjectByType<Camera>();
            return _camera;
        }

        private bool TryScreenToWorld(Vector2 screenPosition, out Vector2 worldPosition)
        {
            worldPosition = Vector2.zero;

            Camera cam = ResolveCamera();
            if (cam == null) return false;

            float depth = Mathf.Abs(cam.transform.position.z);
            if (depth < 0.0001f) depth = Mathf.Max(cam.nearClipPlane, 1f);

            Vector3 point = cam.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
            worldPosition = new Vector2(point.x, point.y);
            return true;
        }

        /// <summary>
        /// Reads a single press this frame, from whichever pointer device actually exists.
        /// Touch is checked before mouse so a touchscreen build never falls back to a simulated
        /// mouse, and every device reference is null checked.
        /// </summary>
        private bool TryReadTap(out Vector2 screenPosition, out bool overUI)
        {
            screenPosition = Vector2.zero;
            overUI = false;

#if ENABLE_INPUT_SYSTEM
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var primaryTouch = touchscreen.primaryTouch;
                if (primaryTouch != null && primaryTouch.press.wasPressedThisFrame)
                {
                    screenPosition = primaryTouch.position.ReadValue();
                    overUI = IsPointerOverUI();
                    return true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                overUI = IsPointerOverUI();
                return true;
            }

            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                screenPosition = pointer.position.ReadValue();
                overUI = IsPointerOverUI();
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.Input.touchCount > 0)
            {
                UnityEngine.Touch touch = UnityEngine.Input.GetTouch(0);
                if (touch.phase == UnityEngine.TouchPhase.Began)
                {
                    screenPosition = touch.position;
                    overUI = IsPointerOverUI(touch.fingerId);
                    return true;
                }
            }
            else if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                Vector3 mousePosition = UnityEngine.Input.mousePosition;
                screenPosition = new Vector2(mousePosition.x, mousePosition.y);
                overUI = IsPointerOverUI();
                return true;
            }
#endif

            return false;
        }

        private static bool IsPointerOverUI()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        private static bool IsPointerOverUI(int pointerId)
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject(pointerId);
        }
    }
}
