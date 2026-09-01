using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The name of the thing you are standing next to.
    ///
    /// ==================================================================================
    /// WHY THIS EXISTS
    /// ==================================================================================
    /// <see cref="InteractableBase.DisplayName"/> has always been filled in — the wall segment, the
    /// well, both kinds of pile, the mat, every resident — and its own summary says it is "the
    /// short label the UI may show next to the interaction prompt". There was no interaction
    /// prompt. Nothing in the game read the property, so a player learned what was tappable by
    /// tapping everything, and a thing that answers and a thing that does not look identical until
    /// you try.
    ///
    /// ==================================================================================
    /// WHY PROXIMITY, AND NOT AN OUTLINE ON EVERYTHING
    /// ==================================================================================
    /// Ringing every interactable would work and would cost the look of the village: the art here
    /// is a place drawn at a fixed palette, and a permanent overlay of markers turns it into a
    /// board of buttons. Naming the one you have walked up to is the genre's own answer, it teaches
    /// the vocabulary as the player moves, and it goes away by itself.
    ///
    /// ==================================================================================
    /// WHAT IT IS NOT
    /// ==================================================================================
    /// <list type="bullet">
    ///   <item><b>Not a count and not a hint.</b> It prints a name. It never says what the thing
    ///   will give, how many are left, or what to do next — the help panel owns advice, and rule 10
    ///   owns why none of it is a checklist.</item>
    ///   <item><b>Not tappable.</b> Every graphic here has <c>raycastTarget</c> off. The thing
    ///   itself is the target; a label that ate the tap meant for the wall behind it would be a new
    ///   bug wearing the old one's clothes.</item>
    ///   <item><b>Not on during a conversation, a modal or the opening.</b> Those own the screen,
    ///   and the village is not being played.</item>
    /// </list>
    /// </summary>
    public sealed class InteractionPrompt : MonoBehaviour
    {
        /// <summary>Above the HUD (50), below the toast (80) and dialogue (100).</summary>
        public const int CanvasSortingOrder = 60;

        /// <summary>
        /// How often the nearest interactable is looked up, in seconds.
        ///
        /// The walk is 3.4 world units a second and the reach is under two, so a fifth of a second
        /// is a fraction of a cell — the label cannot lag behind the player by anything they would
        /// notice, and the scan does not run every frame over every interactable in the scene.
        /// </summary>
        const float ScanInterval = 0.2f;

        /// <summary>How far above the thing's own position the label floats, in world units.</summary>
        const float WorldLift = 0.85f;

        static readonly float PaddingX = DesignTokens.Space.S12;
        static readonly float PlateHeight = DesignTokens.Space.S32;

        static InteractionPrompt _current;

        Canvas _canvas;
        RectTransform _plate;
        Text _label;
        CanvasGroup _group;
        Camera _camera;
        PlayerController _player;
        DialogueSystem _dialogue;
        InteractableBase _target;
        float _nextScan;

        /// <summary>The name on screen right now, or empty. Read by the e2e run.</summary>
        public static string Showing
        {
            get
            {
                if (_current == null || _current._group == null || _current._label == null)
                {
                    return string.Empty;
                }

                return _current._group.alpha > 0f ? _current._label.text : string.Empty;
            }
        }

        /// <summary>Builds the prompt, or returns the one already in the scene.</summary>
        public static InteractionPrompt Compose()
        {
            if (_current != null)
            {
                return _current;
            }

            InteractionPrompt existing = FindFirstObjectByType<InteractionPrompt>();
            if (existing != null)
            {
                _current = existing;
                return existing;
            }

            var go = new GameObject("InteractionPrompt");
            return go.AddComponent<InteractionPrompt>();
        }

        void Awake()
        {
            if (_current != null && _current != this)
            {
                Destroy(gameObject);
                return;
            }

            _current = this;
            Build();
        }

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        void Build()
        {
            _canvas = UIKit.CreateCanvas("InteractionPromptCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);

            // Parented to the canvas rather than to its safe area: the label follows a thing in the
            // world, so it is placed by arithmetic and not by anchors, and a safe-area inset under
            // it would shift the arithmetic by the size of the notch.
            Image plate = UIKit.CreateCard(_canvas.transform, "PromptPlate", UIKit.CardStyle.Glass);
            plate.raycastTarget = false;

            _plate = (RectTransform)plate.transform;
            _plate.anchorMin = new Vector2(0f, 0f);
            _plate.anchorMax = new Vector2(0f, 0f);
            _plate.pivot = new Vector2(0.5f, 0f);

            _label = UIKit.CreateText(_plate, "PromptLabel", string.Empty,
                DesignTokens.Type.Body, UIKit.InkFor(UIKit.CardStyle.Glass), TextAnchor.MiddleCenter);
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Stretch((RectTransform)_label.transform, PaddingX, PaddingX, 0f, 0f);

            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        void Update()
        {
            if (!Playing())
            {
                Hide();
                return;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                _target = FindNearest();
            }

            if (_target == null)
            {
                Hide();
                return;
            }

            Show(_target);
        }

        /// <summary>
        /// Whether the village is currently being played by the player.
        ///
        /// Every one of these owns the screen while it is up, and a name floating over the world
        /// behind a conversation is the HUD covering the scene by another route.
        /// </summary>
        static bool Playing()
        {
            if (ModalRoot.IsOpen || IntroCutscene.IsPlaying)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The nearest available interactable within its own reach, or null.
        ///
        /// The reach is the interactable's, not a number written here: the wall segment and a
        /// resident do not have to agree about how close is close, and
        /// <see cref="InteractableBase.InteractRadius"/> is already what the tap handler honours.
        /// Asking the same property keeps the label and the tap telling the same story.
        /// </summary>
        InteractableBase FindNearest()
        {
            PlayerController player = ResolvePlayer();
            if (player == null)
            {
                return null;
            }

            if (_dialogue == null)
            {
                _dialogue = WorldRuntime.FindDialogueSystem();
            }

            if (_dialogue != null && _dialogue.IsPlaying)
            {
                return null;
            }

            Vector2 origin = player.transform.position;

            InteractableBase best = null;
            float bestDistance = float.MaxValue;

            var all = InteractableBase.All;
            for (int i = 0; i < all.Count; i++)
            {
                InteractableBase candidate = all[i];
                if (candidate == null || !candidate.IsAvailable)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(candidate.DisplayName))
                {
                    continue;
                }

                float reach = candidate.InteractRadius;
                float distance = Vector2.Distance(origin, candidate.transform.position);
                if (distance > reach || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        PlayerController ResolvePlayer()
        {
            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerController>();
            }

            return _player;
        }

        void Show(InteractableBase target)
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                Hide();
                return;
            }

            _label.text = target.DisplayName;

            // Width follows the name, so a resident with a long name is a wider plate rather than a
            // truncated one — the type floor is a floor everywhere, and shrinking to fit is how a
            // label ends up under it.
            float width = _label.preferredWidth + 2f * PaddingX;
            _plate.sizeDelta = new Vector2(width, PlateHeight);

            Vector3 world = target.transform.position + new Vector3(0f, WorldLift, 0f);
            Vector3 screen = _camera.WorldToScreenPoint(world);

            // Behind the camera: nothing to point at, and WorldToScreenPoint mirrors the position
            // rather than reporting a failure.
            if (screen.z < 0f)
            {
                Hide();
                return;
            }

            // The canvas scales with the screen, so a point in pixels has to be divided by the same
            // factor the scaler applied before it means anything in canvas units.
            float scale = _canvas.scaleFactor;
            if (scale <= 0f)
            {
                scale = 1f;
            }

            _plate.anchoredPosition = new Vector2(screen.x / scale, screen.y / scale);
            _group.alpha = 1f;
        }

        void Hide()
        {
            if (_group != null)
            {
                _group.alpha = 0f;
            }

            _target = null;
        }
    }
}
