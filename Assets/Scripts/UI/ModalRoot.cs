using System;
using System.Collections.Generic;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The one place modal panels live. Dialogue, the morale contest, the end-of-day split and the
    /// chapter reader all push here instead of each spawning a canvas of its own, so exactly one
    /// component decides what is on top and what swallows input.
    ///
    /// PlayerController and anything else that must stand down while a panel is up
    /// polls the static <see cref="IsOpen"/>, which never allocates and never creates the root.
    ///
    /// Layering: each push gets a full-screen container with its own scrim, added as the last
    /// sibling, so a later push draws over an earlier one and blocks it.
    /// </summary>
    public sealed class ModalRoot : MonoBehaviour
    {
        /// <summary>Above the HUD, below nothing else in the POC.</summary>
        public const int CanvasSortingOrder = 300;

        static ModalRoot _instance;
        static bool _quitting;

        readonly List<Entry> _stack = new List<Entry>();

        Canvas _canvas;
        RectTransform _layer;
        bool _lockHeld;

        sealed class Entry
        {
            public string Id;
            public RectTransform Container;
        }

        /// <summary>True while at least one modal is up. Safe before anything has been built.</summary>
        public static bool IsOpen
        {
            get { return _instance != null && _instance._stack.Count > 0; }
        }

        /// <summary>How many panels are stacked. Zero when no modal root exists yet.</summary>
        public static int OpenCount
        {
            get { return _instance != null ? _instance._stack.Count : 0; }
        }

        /// <summary>Id of the topmost panel, or null when nothing is open.</summary>
        public static string TopId
        {
            get
            {
                if (_instance == null || _instance._stack.Count == 0)
                {
                    return null;
                }

                return _instance._stack[_instance._stack.Count - 1].Id;
            }
        }

        /// <summary>Fires with true on the first push and false on the last close.</summary>
        public static event Action<bool> OpenStateChanged;

        /// <summary>
        /// Find-or-create. Never called during teardown, and never persists across scenes: a modal
        /// belongs to the scene that raised it.
        /// </summary>
        public static ModalRoot Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                if (_quitting)
                {
                    return null;
                }

                ModalRoot found = FindFirstObjectByType<ModalRoot>();
                if (found != null)
                {
                    _instance = found;
                    return _instance;
                }

                var go = new GameObject("ModalRoot");
                _instance = go.AddComponent<ModalRoot>();
                return _instance;
            }
        }

        /// <summary>The canvas transform every pushed container is parented to.</summary>
        public RectTransform Layer
        {
            get
            {
                EnsureCanvas();
                return _layer;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[ModalRoot] A second modal root was created; destroying the duplicate.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureCanvas();
        }

        void OnDestroy()
        {
            // A scene change can take the root down with panels still on it. Releasing here is
            // what stops the world input staying locked into the next scene.
            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }

            _stack.Clear();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        void OnApplicationQuit()
        {
            _quitting = true;
        }

        void EnsureCanvas()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UIKit.CreateCanvas("ModalCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);
            // Panels build inside the safe area; the scrim below bleeds back out to the edges.
            _layer = UIKit.SafeArea(_canvas);
        }

        // ------------------------------------------------------------------ stack

        /// <summary>
        /// Opens a full-screen container for the given id and returns it. Pushing an id that is
        /// already open returns the existing container instead of stacking a duplicate, which is
        /// what makes a second Open() call on a screen idempotent.
        /// </summary>
        public RectTransform Push(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("[ModalRoot] A modal needs an id.");
                return null;
            }

            RectTransform existing = Find(id);
            if (existing != null)
            {
                // Already up: bring it to the front rather than opening it twice.
                existing.SetAsLastSibling();
                return existing;
            }

            EnsureCanvas();

            RectTransform container = UIKit.CreateRect("Modal_" + id, _layer);
            UIKit.Stretch(container);
            container.SetAsLastSibling();

            // The scrim is the first child, so panel content added later draws over it.
            UIKit.Bleed(UIKit.CreateScrim(container, "Scrim"));

            bool wasClosed = _stack.Count == 0;
            _stack.Add(new Entry { Id = id, Container = container });

            if (wasClosed)
            {
                SetOpenState(true);
            }

            return container;
        }

        /// <summary>Pushes an id and reparents an already-built panel into the new container.</summary>
        public RectTransform Push(string id, GameObject content)
        {
            RectTransform container = Push(id);
            if (container != null && content != null)
            {
                content.transform.SetParent(container, false);
                content.transform.SetAsLastSibling();
            }

            return container;
        }

        public bool IsIdOpen(string id)
        {
            return Find(id) != null;
        }

        /// <summary>The container of an open id, or null.</summary>
        public RectTransform Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Id == id)
                {
                    return _stack[i].Container;
                }
            }

            return null;
        }

        /// <summary>Closes one panel by id and destroys its container. Unknown ids are ignored.</summary>
        public void Close(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Id != id)
                {
                    continue;
                }

                Entry entry = _stack[i];
                _stack.RemoveAt(i);

                if (entry.Container != null)
                {
                    Destroy(entry.Container.gameObject);
                }

                if (_stack.Count == 0)
                {
                    SetOpenState(false);
                }

                return;
            }
        }

        public void CloseTop()
        {
            if (_stack.Count == 0)
            {
                return;
            }

            Close(_stack[_stack.Count - 1].Id);
        }

        public void CloseAll()
        {
            if (_stack.Count == 0)
            {
                return;
            }

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                Entry entry = _stack[i];
                if (entry.Container != null)
                {
                    Destroy(entry.Container.gameObject);
                }
            }

            _stack.Clear();
            SetOpenState(false);
        }

        /// <summary>
        /// Static convenience so a caller that only wants to shut a panel does not have to touch
        /// the instance. A missing root is not an error: there is nothing open to close.
        /// </summary>
        public static void CloseId(string id)
        {
            if (_instance != null)
            {
                _instance.Close(id);
            }
        }

        /// <summary>
        /// Runs on the empty-to-open and open-to-empty transitions only.
        ///
        /// Holding InputLock for the life of the stack is what keeps the player from walking
        /// around behind an open panel: PlayerController reads that lock, and it is the same
        /// convention the quiz and the contest already follow. The count nests, so a panel that
        /// takes the lock itself as well is not a problem.
        /// </summary>
        void SetOpenState(bool open)
        {
            if (open && !_lockHeld)
            {
                _lockHeld = true;
                InputLock.Push();
            }
            else if (!open && _lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }

            Action<bool> handler = OpenStateChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(open);
            }
            catch (Exception exception)
            {
                Debug.LogError("[ModalRoot] A listener threw while the modal state changed: " + exception.Message);
            }
        }

        // ------------------------------------------------------------------ hardware back

#if ENABLE_LEGACY_INPUT_MANAGER
        void Update()
        {
            // Android's back button arrives as Escape. Leaving a panel is always free — the chapter
            // reader in particular is only worth measuring if closing it costs nothing.
            if (_stack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseTop();
            }
        }
#endif
    }
}
