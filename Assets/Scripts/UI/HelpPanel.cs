using System;
using SheepGate.Core;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// What to do next, and nothing else.
    ///
    /// The panel shows one instruction — the first thing the run has not done yet — with everything
    /// already done listed under it, quieter. Steps the player has not reached are not shown at all:
    /// that is what makes this open up as the game goes rather than greet a new player with a wall
    /// of things they cannot do.
    ///
    /// <b>Three things this deliberately is not</b>, and each of them is a rule this project already
    /// wrote down:
    /// <list type="bullet">
    ///   <item><b>Not a completion fraction.</b> No "3 of 7", no count, no next-unlock line. The
    ///   backpack refuses the same thing in the same words — a progress count is a task list wearing
    ///   another hat — and the reason is rule 10: the moment a player can see how many steps are
    ///   left, discovering the game turns into working through it.</item>
    ///   <item><b>Not a reward.</b> Nothing here is scored, and reading is described as what it
    ///   opens, never as what it pays. Rule 19: reading pays in understanding, never in numbers.</item>
    ///   <item><b>Not a nag.</b> It says nothing on its own. It is behind a button the player
    ///   presses when they are lost, which is the only moment advice is worth anything.</item>
    /// </list>
    ///
    /// Every step is <b>derived from the run</b> rather than remembered. Nothing is written to the
    /// save, so there is no flag that can be set at the wrong moment and no state to migrate — and
    /// a step done in a way this panel never anticipated still reads as done, because what is
    /// checked is the world, not a breadcrumb the code dropped.
    /// </summary>
    public sealed class HelpPanel : MonoBehaviour
    {
        public const string ModalId = "help";

        /// <summary>
        /// The steps, in the order the game asks for them. The first one whose test fails is the
        /// instruction on screen; everything before it is history.
        ///
        /// The tests are deliberately generous. "Gathered something" passes once the wall has any
        /// work on it at all, because material that was picked up and spent is still material that
        /// was picked up — a test that only looked at what is in hand would tell a player who did
        /// the thing to go and do it.
        /// </summary>
        static readonly Step[] Steps =
        {
            new Step("help.step.talk", state => state.Counter(NpcActor.DistinctTalkedKey) > 0),
            new Step("help.step.gather", state => Carrying(state) || WallStarted(state)),
            new Step("help.step.build", WallStarted),
            new Step("help.step.read", state => state.HasFlag(GameFlags.ChapterOpened)),
            new Step("help.step.rest", state => state.day > 1),
            new Step("help.step.split", state => state.HasFlag(GameFlags.WatchPostedD1)),
            new Step("help.step.trial", state => state.HasFlag(GameFlags.ContestResolved))
        };

        bool _closed;

        readonly struct Step
        {
            public readonly string Key;
            public readonly Func<GameState, bool> Done;

            public Step(string key, Func<GameState, bool> done)
            {
                Key = key;
                Done = done;
            }
        }

        /// <summary>Opens the panel, or does nothing when it is already up.</summary>
        public static HelpPanel Show()
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[HelpPanel] No modal root is available; help cannot open.");
                return null;
            }

            if (root.IsIdOpen(ModalId))
            {
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<HelpPanel>();
            panel.Build(container);
            return panel;
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            ModalRoot.CloseId(ModalId);
        }

        /// <summary>
        /// Title, the instruction, what is behind it, and the way out — a column that measures
        /// itself, like the settings card, so a longer instruction in one language cannot push the
        /// close button off the bottom.
        /// </summary>
        void Build(RectTransform container)
        {
            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;

            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.offsetMin = new Vector2(DesignTokens.Space.Gutter, 0f);
            cardRect.offsetMax = new Vector2(-DesignTokens.Space.Gutter, 0f);

            int pad = Mathf.RoundToInt(DesignTokens.Space.S24);
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(pad, pad, pad, pad));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateText(cardRect, "Title", Loc.T("help.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Title);

            GameState state = TryGetState();
            BuildSteps(cardRect, state);

            UIKit.CreateButton(cardRect, "Close", Loc.T("help.close"),
                UIKit.ButtonVariant.Primary, Close);
        }

        /// <summary>
        /// The instruction first and the finished steps under it, which is the order they are read
        /// in. A player who opened this is looking for one sentence; the rest is there to say that
        /// the game has been keeping up, not to be worked through.
        /// </summary>
        void BuildSteps(RectTransform cardRect, GameState state)
        {
            int current = state != null ? CurrentStep(state) : 0;

            UIKit.CreateText(cardRect, "CurrentStep",
                current < Steps.Length ? Loc.T(Steps[current].Key) : Loc.T("help.all_done"),
                DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft);

            if (state == null || current == 0)
            {
                return;
            }

            UIKit.CreateText(cardRect, "DoneLabel", Loc.T("help.done"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft);

            RectTransform doneList = UIKit.CreateRect("DoneSteps", cardRect);
            UIKit.VerticalGroup(doneList.gameObject, DesignTokens.Space.S8, new RectOffset());

            for (int i = 0; i < current; i++)
            {
                BuildDoneStep(doneList, i);
            }
        }

        /// <summary>
        /// One finished step: the check, then the same sentence it was given as, in the supporting
        /// ink. The wording does not change once it is done — a step that reworded itself in the
        /// past tense would be a second string to translate for no new information.
        /// </summary>
        static void BuildDoneStep(RectTransform parent, int index)
        {
            RectTransform row = UIKit.CreateRect("DoneStep" + index.ToString(), parent);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S8, new RectOffset(),
                                  TextAnchor.UpperLeft);

            Image check = UIKit.CreateIcon(row, "Check", UiSpriteKeys.IconCheck,
                                           DesignTokens.Ink.Secondary, DesignTokens.Type.Body);
            LayoutElement checkLayout = UIKit.Layout(check);
            checkLayout.minWidth = DesignTokens.Type.Body;
            checkLayout.preferredWidth = DesignTokens.Type.Body;

            Text line = UIKit.CreateText(row, "Line", Loc.T(Steps[index].Key),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            UIKit.Layout(line).flexibleWidth = 1f;
        }

        /// <summary>Index of the first step not yet done, or the length of the list when all are.</summary>
        static int CurrentStep(GameState state)
        {
            for (int i = 0; i < Steps.Length; i++)
            {
                if (!Steps[i].Done(state))
                {
                    return i;
                }
            }

            return Steps.Length;
        }

        static bool Carrying(GameState state)
        {
            return state.stone > 0 || state.timber > 0 || state.blocks > 0;
        }

        /// <summary>True once any segment has taken work, finished or not.</summary>
        static bool WallStarted(GameState state)
        {
            if (state.segments == null)
            {
                return false;
            }

            for (int i = 0; i < state.segments.Count; i++)
            {
                WallSegmentState segment = state.segments[i];
                if (segment != null && (segment.stage > 0 || segment.workInStage > 0))
                {
                    return true;
                }
            }

            return false;
        }

        static GameState TryGetState()
        {
            try
            {
                return ServiceLocator.Get<GameState>();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
