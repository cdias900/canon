using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Scripture;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The codex of the gates: the ten gates of Nehemiah 3, each with who raised it and the verse
    /// that names it, opened from the HUD's drawer from the first hour of a run.
    ///
    /// The design asks for exactly this — every gate with its entry and its reference before the
    /// player has laid a stone — and the season builds only one of the ten. The other nine are
    /// not content the game plays; they are the shape of the city the player is standing in, and
    /// the one place they can be read without inventing a beat for each.
    ///
    /// This is the one surface where the reference is never deferred. Rule 12 hides chapter and
    /// verse under a quotation until the reveal because the deferral belongs to the FICTION — a
    /// stonemason reads the governor's words as the governor's words. The codex is not the
    /// fiction; it is the product being honest about what it is, which the design names in so many
    /// words: "store, title and codex are honest", and "every gate has a codex entry with its
    /// reference from the first hour". Nothing is denied, nothing is highlighted: the reference
    /// sits here for whoever comes looking, and the dialogue still does not point at it. Rule 2
    /// holds as everywhere — the verse comes from verses.json and the builders line is ours,
    /// narration about the names the text records, never a quotation.
    ///
    /// The player's own gate is marked, read off the terminal stage's gate segment rather than a
    /// segment id spelled here, so a season that moved its gate would move the mark with it.
    /// </summary>
    public sealed class CodexPanel : MonoBehaviour
    {
        public const string ModalId = "codex";

        /// <summary>Telemetry context for the verses this panel shows and the chapter it opens.</summary>
        public const string Trigger = "codex";

        static readonly float FooterHeight = DesignTokens.Space.S24 * 2f + UIKit.ButtonMinHeight;
        static readonly float HairlineHeight = Mathf.Max(2f, DesignTokens.Px(1f));

        bool _closed;

        /// <summary>Opens the codex, or does nothing when it is already up.</summary>
        public static CodexPanel Show()
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[CodexPanel] No modal root is available; the codex cannot open.");
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

            var panel = container.gameObject.AddComponent<CodexPanel>();
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

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// A full-height sheet, like the split panel: ten cards are longer than any phone, so the
        /// list scrolls and the way out is pinned under it where a thumb can reach it at any
        /// point of the list.
        /// </summary>
        void Build(RectTransform container)
        {
            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Panel);
            var cardRect = (RectTransform)card.transform;
            UIKit.Stretch(cardRect,
                          DesignTokens.Space.S16, DesignTokens.Space.S16,
                          DesignTokens.Space.S24, DesignTokens.Space.S24);

            RectTransform column;
            ScrollRect scroll = UIKit.CreateScrollView(cardRect, "Sheet", out column);
            UIKit.Stretch((RectTransform)scroll.transform, 0f, 0f, 0f, FooterHeight);
            PrepareScroll(scroll, column);

            UIKit.CreateText(column, "Title", Loc.T("codex.title"),
                DesignTokens.Type.Display, DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Display);

            UIKit.CreateText(column, "Subtitle", Loc.T("codex.subtitle"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Body);

            GateDef[] gates = GameData.Gates;
            string playersSegment = PlayersGateSegment();
            int listed = 0;
            for (int i = 0; i < gates.Length; i++)
            {
                GateDef gate = gates[i];
                if (gate == null || string.IsNullOrEmpty(gate.id))
                {
                    continue;
                }

                bool yours = !string.IsNullOrEmpty(playersSegment) && gate.segment == playersSegment;
                BuildGate(column, gate, yours);
                listed++;
            }

            if (listed == 0)
            {
                Debug.LogError("[CodexPanel] gates.json lists no gates; the codex opened on nothing.");
            }

            BuildFooter(cardRect);

            Telemetry.Track(TelemetryEvents.CodexOpened, new Dictionary<string, object>
            {
                { "gates", listed },
                { "revealed", ScriptureVisibility.ReferencesVisible() }
            });
        }

        /// <summary>
        /// The segment the season's last stage closes. That is the player's gate, by definition of
        /// the stage table, and the codex asks the table rather than remembering a segment id.
        /// </summary>
        static string PlayersGateSegment()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null)
            {
                return null;
            }

            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage != null && stage.terminal && !string.IsNullOrEmpty(stage.gate_segment))
                {
                    return stage.gate_segment;
                }
            }

            return null;
        }

        /// <summary>
        /// One gate: its name, who raised it, and the verse that records it in the same shape the
        /// morning's vigil page uses — the text in italics on the scroll card, the reference under
        /// it only after the reveal, and the way into the chapter beside the reference regardless.
        /// </summary>
        static void BuildGate(RectTransform column, GateDef gate, bool yours)
        {
            string verseRef = (gate.verse ?? string.Empty).Trim();
            VerseEntry verse = string.IsNullOrEmpty(verseRef) ? null : ScriptureService.GetVerse(verseRef);

            Image card = UIKit.CreateCard(column, "Gate_" + gate.id, UIKit.CardStyle.Scroll);
            card.raycastTarget = false;
            var cardRect = (RectTransform)card.transform;
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S12,
                                Pad(DesignTokens.Space.S16, DesignTokens.Space.S16));

            RectTransform head = UIKit.CreateRect("Head", cardRect);
            UIKit.HorizontalGroup(head.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            Text name = UIKit.CreateText(head, "GateName",
                string.IsNullOrEmpty(gate.display) ? gate.id : gate.display,
                DesignTokens.Type.Title, DesignTokens.Ink.OnScroll,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.Title);
            UIKit.Layout(name).flexibleWidth = 1f;

            if (yours)
            {
                UIKit.CreateText(head, "CodexYours", Loc.T("codex.yours"),
                    DesignTokens.Type.Minimum, DesignTokens.Ink.OnScrollMuted,
                    TextAnchor.MiddleRight, DesignTokens.TypeRole.BodyStrong);
            }

            if (!string.IsNullOrEmpty(gate.builders))
            {
                UIKit.CreateText(cardRect, "Builders", gate.builders,
                    DesignTokens.Type.Body, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
            }

            if (string.IsNullOrEmpty(verseRef))
            {
                Debug.LogWarning("[CodexPanel] gate \"" + gate.id + "\" names no verse; its card carries no citation.");
                return;
            }

            Text body = UIKit.CreateText(cardRect, "GateVerse",
                verse != null && !string.IsNullOrEmpty(verse.text) ? verse.text : Loc.T("scripture.unavailable", verseRef),
                DesignTokens.Type.Body, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
            body.fontStyle = FontStyle.Italic;

            RectTransform footer = UIKit.CreateRect("GateFooter", cardRect);
            UIKit.HorizontalGroup(footer.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            Text reference = UIKit.CreateText(footer, "GateReference_" + gate.id,
                ReferenceLabel(verse, verseRef),
                DesignTokens.Type.Mono, DesignTokens.Ink.OnScrollMuted,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.Mono);
            UIKit.Layout(reference).flexibleWidth = 1f;

            string chapterRef = ScriptureService.ChapterRefOf(verseRef);
            if (!string.IsNullOrEmpty(chapterRef))
            {
                UIKit.CreateButton(footer, "CodexReadMore_" + gate.id, Loc.T("dialogue.read_more"),
                    UIKit.ButtonVariant.Secondary,
                    () => ChapterReaderUI.Open(chapterRef, Trigger, true));
            }

            if (verse == null || string.IsNullOrEmpty(verse.text))
            {
                Debug.LogWarning("[CodexPanel] " + verseRef + " has no text in verses.json; the gate card shows the missing marker.");
            }
        }

        /// <summary>
        /// The reference under the verse, with the version — from the first hour, unlike the
        /// dialogue, A Página and the vigil, for the reason the class comment gives.
        /// </summary>
        static string ReferenceLabel(VerseEntry verse, string verseRef)
        {
            string label = verse != null && !string.IsNullOrEmpty(verse.ref_display) ? verse.ref_display : verseRef;
            VersionInfo version = ScriptureService.Version;
            if (version != null && !string.IsNullOrEmpty(version.abbrev))
            {
                label += " · " + version.abbrev;
            }

            return label;
        }

        /// <summary>
        /// The way out, pinned under the list. Named for this panel rather than "Close": the
        /// chapter reader this panel opens has a Close of its own, on top of this one, and two
        /// active controls with one name is how a harness closes the wrong screen.
        /// </summary>
        void BuildFooter(RectTransform cardRect)
        {
            RectTransform footer = UIKit.CreateRect("Footer", cardRect);
            UIKit.AnchorBottom(footer, FooterHeight, 0f, 0f, 0f);

            Image divider = UIKit.CreatePanel(footer, "Divider", DesignTokens.Surface.Border, null);
            UIKit.AnchorTop((RectTransform)divider.transform, HairlineHeight,
                            DesignTokens.Space.Gutter, DesignTokens.Space.Gutter, 0f);
            divider.raycastTarget = false;

            Button close = UIKit.CreateButton(footer, "CodexClose", Loc.T("codex.close"),
                                              UIKit.ButtonVariant.Primary, Close);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(0f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.offsetMin = new Vector2(DesignTokens.Space.Gutter, -UIKit.ButtonMinHeight * 0.5f);
            closeRect.offsetMax = new Vector2(-DesignTokens.Space.Gutter, UIKit.ButtonMinHeight * 0.5f);
        }

        /// <summary>Same retuning the split panel gives the kit's scroll view: rhythm, and a drag target.</summary>
        static void PrepareScroll(ScrollRect scroll, RectTransform column)
        {
            if (scroll != null && scroll.viewport != null && scroll.viewport.GetComponent<Graphic>() == null)
            {
                var catcher = scroll.viewport.gameObject.AddComponent<Image>();
                catcher.color = Color.clear;
                catcher.raycastTarget = true;
            }

            var group = column != null ? column.GetComponent<VerticalLayoutGroup>() : null;
            if (group == null)
            {
                return;
            }

            group.spacing = DesignTokens.Space.S16;
            group.padding = Pad(DesignTokens.Space.Gutter, DesignTokens.Space.S32);
        }

        static RectOffset Pad(float horizontal, float vertical)
        {
            int x = Mathf.RoundToInt(horizontal);
            int y = Mathf.RoundToInt(vertical);
            return new RectOffset(x, x, y, y);
        }
    }
}
