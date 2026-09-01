using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// What has been said in this conversation, for a player who tapped past it.
    ///
    /// ==================================================================================
    /// WHY
    /// ==================================================================================
    /// The tap catcher covers the whole screen and every tap advances, so a double tap costs a
    /// line and there was no way back to it. That is cheap in a game about buttons and expensive
    /// in this one: the whole build exists to find out whether a player reads, and the sentence
    /// they lost is the sentence being measured.
    ///
    /// Reached by holding the screen rather than by a button, for the reason the HUD gave for
    /// putting its own readouts away: this screen is a conversation, and a permanent control on top
    /// of it is the interface talking over the person.
    ///
    /// ==================================================================================
    /// WHAT IT IS NOT
    /// ==================================================================================
    /// <list type="bullet">
    ///   <item><b>Not a transcript of the run.</b> One node, cleared when the next one starts. A
    ///   log of everything ever said is a record of how much someone has read, and rule 19 is
    ///   clear that reading is never turned into a number.</item>
    ///   <item><b>Not a way to re-answer.</b> Choices are not shown and nothing here is tappable
    ///   except the way out. Looking back at what was said cannot change what was chosen.</item>
    ///   <item><b>Not scripture storage.</b> A verse line appears here exactly as it appeared in
    ///   the bubble, because that is the text the scripture service already resolved and put on
    ///   screen. Nothing is written down here and nothing is re-worded.</item>
    /// </list>
    /// </summary>
    public sealed class DialogueHistoryPanel : MonoBehaviour
    {
        public const string ModalId = "dialogue_history";

        bool _closed;

        /// <summary>
        /// Opens the panel over the conversation. Does nothing when there is nothing to show —
        /// a long press on the very first line has no answer, and an empty card is a worse one
        /// than none.
        /// </summary>
        public static DialogueHistoryPanel Show(IReadOnlyList<DialogueSystem.DialogueRecord> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                return null;
            }

            if (root.IsIdOpen(ModalId))
            {
                return null;
            }

            RectTransform container;
            RectTransform card = root.PushCard(ModalId, out container);
            if (card == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<DialogueHistoryPanel>();
            panel.Build(card, lines);
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

        void Build(RectTransform card, IReadOnlyList<DialogueSystem.DialogueRecord> lines)
        {
            UIKit.CreateText(card, "HistoryTitle", Loc.T("dialogue.history.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Title);

            RectTransform content;
            ScrollRect scroll = UIKit.CreateScrollView(card, "HistoryLines", out content);

            LayoutElement scrollLayout = UIKit.Layout(scroll);
            scrollLayout.minHeight = ListHeight;
            scrollLayout.preferredHeight = ListHeight;
            scrollLayout.flexibleHeight = 0f;

            // The column already has its layout group: CreateScrollView puts one on the content it
            // hands back. Adding a second is a NullReferenceException, not a warning — a layout
            // group is DisallowMultipleComponent, so AddComponent returns null and the next line
            // dereferences it. This panel shipped that bug once and it opened as an empty card.
            var column = content.GetComponent<VerticalLayoutGroup>();
            if (column != null)
            {
                column.spacing = DesignTokens.Space.S16;
                column.padding = new RectOffset();
            }

            for (int i = 0; i < lines.Count; i++)
            {
                BuildEntry(content, lines[i]);
            }

            UIKit.CreateButton(card, "HistoryClose", Loc.T("dialogue.history.close"),
                UIKit.ButtonVariant.Primary, Close);

            // Rebuilt before the scroll is placed, so the content height the jump below reads is
            // the finished one rather than the one the layout had before the rows were measured.
            UIKit.RebuildNow(content);

            // Opens at the bottom, on the most recent line. The player held the screen because of
            // something that just went past, not because of how the conversation started.
            scroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// Who said it, then what they said. The speaker is a separate line rather than a prefix so
        /// a long sentence wraps under itself instead of around a name.
        /// </summary>
        static void BuildEntry(RectTransform parent, DialogueSystem.DialogueRecord record)
        {
            RectTransform entry = UIKit.CreateRect("HistoryEntry", parent);
            UIKit.VerticalGroup(entry.gameObject, DesignTokens.Space.S4, new RectOffset());

            if (!string.IsNullOrEmpty(record.Speaker))
            {
                UIKit.CreateText(entry, "HistorySpeaker", record.Speaker,
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.BodyStrong);
            }

            UIKit.CreateText(entry, "HistoryBody", record.Body,
                DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft);

            // Nothing is measured here on purpose, and the first version of this panel got it wrong
            // twice in the same place. A Text is an ILayoutElement, so the column above already asks
            // it how tall it needs to be at the width it was handed — which is the only moment the
            // width is known. Two things break that: a ContentSizeFitter on a child whose parent
            // already controls its height, and a LayoutElement given preferredHeight read off the
            // Text before any layout pass has run, which is zero. Both produce the same symptom:
            // an empty card with rows that are there and have no height.
        }

        /// <summary>
        /// How tall the list is allowed to be.
        ///
        /// Fixed rather than fitted, because a card that grew with the conversation would be a
        /// different size on every node and would eventually be taller than the screen. Two thirds
        /// of the reference height leaves the title and the way out on screen with room to spare on
        /// the shortest phone this ships to.
        /// </summary>
        static readonly float ListHeight = UIKit.ReferenceHeight * 0.55f;
    }
}
