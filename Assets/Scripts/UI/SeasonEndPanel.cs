using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Scripture;
using SheepGate.Vocation;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The screen after the reveal: the whole wall, with the names on it.
    ///
    /// The season used to end on the vocation's name and a button back to a village where
    /// nothing could happen any more — no ending, no record, no way to start again except a row
    /// in the settings. This is the ending. It shows the wall the run built, segment by segment,
    /// with who repaired each stretch, which is the one thing Nehemiah 3 does with every name it
    /// lists; the six vocations the game could have named, now that one has been; a way into the
    /// chapter that comes after the season, which nothing in the game asked for or pays for; and
    /// the way to a new work.
    ///
    /// <b>What it never shows.</b> No score, no second place, no distance to another vocation
    /// (rule 10): the six are names, and the only mark on the list is which one this run got. The
    /// numbers on this screen are the wall's and the nights', which were on the HUD and in the
    /// morning reports all season. No button shares anything: the card is drawn to be
    /// screenshotted, and the reading stays inside the app.
    ///
    /// Reachable twice: the director opens it once after the reveal, and the HUD's drawer carries
    /// a row for it from then on, so the ending can be looked at again.
    /// </summary>
    public sealed class SeasonEndPanel : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "season_end";

        /// <summary>
        /// The chapter the invitation opens: the first one the season never played, where the
        /// letters come from and the wall is finished. Not fetched for Ezra — the manifest has no
        /// Ezra — and not a stage's chapter, which is the point: nothing in the game hangs off it.
        /// </summary>
        public const string ReadOnChapterRef = "NEH.6";

        static readonly float FooterHeight = DesignTokens.Space.S24 * 2f + UIKit.ButtonMinHeight;
        static readonly float HairlineHeight = Mathf.Max(2f, DesignTokens.Px(1f));
        static readonly float RecordBarHeight = DesignTokens.Px(8f);
        static readonly float RecordBarWidth = DesignTokens.Px(56f);

        static SeasonEndPanel _current;

        Action _onClosed;
        bool _built;
        bool _closed;

        public static bool IsOpen
        {
            get { return _current != null && !_current._closed; }
        }

        /// <summary>Shows the ending. Calls back once it goes away, however it goes away.</summary>
        public static SeasonEndPanel Show(Action onClosed)
        {
            if (_current != null && !_current._closed)
            {
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[SeasonEndPanel] No modal root is available; the ending cannot open.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<SeasonEndPanel>();
            panel._onClosed = onClosed;
            panel.Build();
            _current = panel;
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

        void Build()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            var container = (RectTransform)transform;
            GameState state = TryGetState();

            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;
            UIKit.Stretch(cardRect, DesignTokens.Space.S16, DesignTokens.Space.S16,
                          DesignTokens.Space.S24, DesignTokens.Space.S24);

            RectTransform column;
            ScrollRect scroll = UIKit.CreateScrollView(cardRect, "Ending", out column);
            UIKit.Stretch((RectTransform)scroll.transform, 0f, 0f, 0f, FooterHeight);
            PrepareScroll(scroll, column);

            BuildHeader(column, state);
            BuildRecord(column, state);
            BuildVocations(column, state);
            BuildReadOn(column);
            BuildRestart(column);
            BuildFooter(cardRect);
        }

        static void PrepareScroll(ScrollRect scroll, RectTransform column)
        {
            if (scroll != null && scroll.viewport != null && scroll.viewport.GetComponent<Graphic>() == null)
            {
                var catcher = scroll.viewport.gameObject.AddComponent<Image>();
                catcher.color = Color.clear;
                catcher.raycastTarget = true;
            }

            var group = column != null ? column.GetComponent<VerticalLayoutGroup>() : null;
            if (group != null)
            {
                group.spacing = DesignTokens.Space.S16;
                group.padding = Pad(DesignTokens.Space.Gutter, DesignTokens.Space.S32);
            }
        }

        /// <summary>The eyebrow, the sentence, and the three numbers the season was made of.</summary>
        static void BuildHeader(RectTransform column, GameState state)
        {
            UIKit.CreateText(column, "Kicker", Loc.T("season_end.kicker"),
                DesignTokens.Type.Mono, DesignTokens.Brand.Secondary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            UIKit.CreateText(column, "Title", Loc.T("season_end.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Title);

            int days = DayCycle.FinalDay;
            int courses = 0;
            if (state != null && state.segments != null)
            {
                for (int i = 0; i < state.segments.Count; i++)
                {
                    WallSegmentState segment = state.segments[i];
                    if (segment != null)
                    {
                        courses += Mathf.Clamp(segment.stage, 0, WallSystem.StagesPerSegment);
                    }
                }
            }

            int nights = 0;
            int watched = 0;
            StageDef[] stages = GameData.Stages;
            if (stages != null)
            {
                for (int i = 0; i < stages.Length; i++)
                {
                    StageDef stage = stages[i];
                    if (stage == null || stage.terminal)
                    {
                        continue;
                    }

                    nights++;
                    if (state != null && state.HasFlag(GameFlags.WatchPostedForDay(stage.day)))
                    {
                        watched++;
                    }
                }
            }

            UIKit.CreateText(column, "Stats", Loc.T("season_end.stats", days, courses, watched, nights),
                DesignTokens.Type.Mono, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Mono);
        }

        /// <summary>
        /// The wall, one row per segment in the order it runs, with who repaired it. The player's
        /// segment carries the player's name; the others carry the builders npcs.json puts on
        /// them. The reference under the list is where the naming of builders comes from — a
        /// reference, never the text.
        /// </summary>
        static void BuildRecord(RectTransform column, GameState state)
        {
            UIKit.CreateText(column, "RecordHeading", Loc.T("season_end.record.heading"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            Image plate = UIKit.CreatePanel(column, "Record", DesignTokens.Surface.Panel, UiSpriteKeys.FrameSm);
            plate.raycastTarget = false;
            var plateRect = (RectTransform)plate.transform;
            UIKit.VerticalGroup(plate.gameObject, DesignTokens.Space.S12,
                Pad(DesignTokens.Space.S20, DesignTokens.Space.S16));

            WallSegmentDef[] defs = GameData.WallSegments ?? new WallSegmentDef[0];
            string playerName = BuilderName(state);

            for (int i = 0; i < defs.Length; i++)
            {
                WallSegmentDef def = defs[i];
                if (def == null || string.IsNullOrEmpty(def.id))
                {
                    continue;
                }

                WallSegmentState segment = state != null ? state.Segment(def.id) : null;
                int stage = segment != null ? Mathf.Clamp(segment.stage, 0, WallSystem.StagesPerSegment) : 0;

                string names;
                if (def.exposed)
                {
                    names = playerName != null
                        ? Loc.T("season_end.record.yours", playerName)
                        : Loc.T("season_end.record.you");
                }
                else
                {
                    names = BuildersOf(def.id);
                }

                BuildRecordRow(plateRect, "Stretch_" + def.id, stage, names, def.exposed);
            }

            UIKit.CreateText(plateRect, "Reference", ScriptureService.ChapterDisplay(GateRecordRef),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Mono);
        }

        /// <summary>Where the naming of builders comes from. Shown as a reference, never as text.</summary>
        const string GateRecordRef = "NEH.3";

        static void BuildRecordRow(RectTransform parent, string name, int stage, string names, bool yours)
        {
            RectTransform row = UIKit.CreateRect(name, parent);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            Image track = UIKit.CreatePanel(row, "Bar", DesignTokens.Surface.Card, UiSpriteKeys.BarTrack);
            track.raycastTarget = false;
            LayoutElement trackLayout = UIKit.Layout(track);
            trackLayout.minWidth = RecordBarWidth;
            trackLayout.preferredWidth = RecordBarWidth;
            trackLayout.minHeight = RecordBarHeight;
            trackLayout.preferredHeight = RecordBarHeight;

            Image fill = UIKit.CreatePanel(track.transform, "Fill", DesignTokens.Brand.Primary, UiSpriteKeys.BarFill);
            fill.raycastTarget = false;
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(stage / (float)WallSystem.StagesPerSegment, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            Text text = UIKit.CreateText(row, "Names", names, DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft,
                yours ? DesignTokens.TypeRole.BodyStrong : DesignTokens.TypeRole.Body);
            UIKit.Layout(text).flexibleWidth = 1f;
        }

        /// <summary>The builders npcs.json puts on a segment, joined the way the locale joins names.</summary>
        static string BuildersOf(string segmentId)
        {
            var names = new List<string>();
            NpcDef[] npcs = GameData.Npcs;
            if (npcs != null)
            {
                for (int i = 0; i < npcs.Length; i++)
                {
                    NpcDef npc = npcs[i];
                    if (npc != null && npc.segment == segmentId && !string.IsNullOrEmpty(npc.display))
                    {
                        names.Add(npc.display);
                    }
                }
            }

            return names.Count > 0 ? string.Join(Loc.T("season_end.names_join"), names.ToArray()) : string.Empty;
        }

        /// <summary>
        /// The six names, now that one has been said. Names only: no score, no order by score, no
        /// distance. The one this run got is marked, in words, and that is the whole disclosure.
        /// </summary>
        static void BuildVocations(RectTransform column, GameState state)
        {
            UIKit.CreateText(column, "VocationsHeading", Loc.T("season_end.vocations.heading"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            string yours = RevealedVocationId(state);

            RectTransform list = UIKit.CreateRect("Vocations", column);
            UIKit.VerticalGroup(list.gameObject, DesignTokens.Space.S8, new RectOffset());

            VocationDef[] vocations = GameData.Vocations ?? new VocationDef[0];
            for (int i = 0; i < vocations.Length; i++)
            {
                VocationDef vocation = vocations[i];
                if (vocation == null || string.IsNullOrEmpty(vocation.id))
                {
                    continue;
                }

                bool mine = vocation.id == yours;
                string label = string.IsNullOrEmpty(vocation.display) ? vocation.id : vocation.display;

                RectTransform row = UIKit.CreateRect("Vocation_" + vocation.id, list);
                UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);
                UIKit.CreateIcon(row, "Icon", mine ? UiSpriteKeys.IconCheck : UiSpriteKeys.IconDot,
                                 mine ? DesignTokens.Brand.Secondary : DesignTokens.Ink.Muted, UIKit.IconSize);
                Text text = UIKit.CreateText(row, "Label", mine ? Loc.T("season_end.vocations.yours", label) : label,
                    DesignTokens.Type.Body, mine ? DesignTokens.Ink.Primary : DesignTokens.Ink.Secondary,
                    TextAnchor.MiddleLeft, mine ? DesignTokens.TypeRole.BodyStrong : DesignTokens.TypeRole.Body);
                UIKit.Layout(text).flexibleWidth = 1f;
            }
        }

        /// <summary>
        /// The id the tracker already said. Only asked once the reveal has happened — the flag is
        /// what opens this screen — so nothing is resolved here that was not resolved before.
        /// </summary>
        static string RevealedVocationId(GameState state)
        {
            if (state == null || !state.HasFlag(GameFlags.VocationRevealed))
            {
                return null;
            }

            try
            {
                VocationTracker tracker = VocationTracker.EnsureRegistered();
                return tracker != null ? tracker.Resolve() : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SeasonEndPanel] Resolving the revealed vocation failed: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// The invitation into the chapter after the season. Secondary and narrow, like every way
        /// into the reader: the number this exists to produce is only worth anything if nobody is
        /// pushed. The caption says out loud that it pays nothing.
        /// </summary>
        static void BuildReadOn(RectTransform column)
        {
            UIKit.CreateText(column, "ReadOnCaption",
                Loc.T("season_end.read_on.caption", ScriptureService.ChapterDisplay(ReadOnChapterRef)),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);

            RectTransform row = UIKit.CreateRect("ReadOnRow", column);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S16, new RectOffset(), TextAnchor.MiddleLeft);
            SetFixedHeight(row, DesignTokens.Space.TouchTarget);

            Button readOn = UIKit.CreateButton(row, "ReadOn", Loc.T("season_end.read_on"),
                UIKit.ButtonVariant.Secondary, OnReadOnClicked);
            LayoutElement layout = UIKit.Layout(readOn);
            if (layout != null)
            {
                layout.minWidth = DesignTokens.Px(200f);
                layout.preferredWidth = DesignTokens.Px(200f);
            }
        }

        static void OnReadOnClicked()
        {
            // Prompted, in the funnel's sense: this button is a door the game drew. The deep read
            // it may lead to is reported as ungamed_read by the reader, off the trigger.
            ChapterReaderUI.Open(ReadOnChapterRef, ChapterReaderUI.UngamedTrigger, true);
        }

        /// <summary>A new work. The question it asks is the settings' own, unchanged.</summary>
        static void BuildRestart(RectTransform column)
        {
            RectTransform row = UIKit.CreateRect("RestartRow", column);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S16, new RectOffset(), TextAnchor.MiddleLeft);
            SetFixedHeight(row, DesignTokens.Space.TouchTarget);

            Button restart = UIKit.CreateButton(row, "SeasonEndRestart", Loc.T("season_end.restart"),
                UIKit.ButtonVariant.Secondary, () => RestartConfirmModal.Show());
            LayoutElement layout = UIKit.Layout(restart);
            if (layout != null)
            {
                layout.minWidth = DesignTokens.Px(200f);
                layout.preferredWidth = DesignTokens.Px(200f);
            }
        }

        void BuildFooter(RectTransform cardRect)
        {
            RectTransform footer = UIKit.CreateRect("Footer", cardRect);
            UIKit.AnchorBottom(footer, FooterHeight, 0f, 0f, 0f);

            Image divider = UIKit.CreatePanel(footer, "Divider", DesignTokens.Surface.Border, null);
            UIKit.AnchorTop((RectTransform)divider.transform, HairlineHeight,
                            DesignTokens.Space.Gutter, DesignTokens.Space.Gutter, 0f);
            divider.raycastTarget = false;

            Button close = UIKit.CreateButton(footer, "SeasonEndClose", Loc.T("season_end.close"),
                                              UIKit.ButtonVariant.Primary, Close);
            var rect = (RectTransform)close.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(DesignTokens.Space.Gutter, DesignTokens.Space.S24);
            rect.offsetMax = new Vector2(-DesignTokens.Space.Gutter,
                                         DesignTokens.Space.S24 + UIKit.ButtonMinHeight);
        }

        // ------------------------------------------------------------------ helpers

        static string BuilderName(GameState state)
        {
            string name = state != null ? state.playerName : null;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            name = name.Trim();
            return name.Length > 0 ? name : null;
        }

        static void SetFixedHeight(Component target, float height)
        {
            LayoutElement layout = UIKit.Layout(target);
            if (layout == null)
            {
                return;
            }

            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        static RectOffset Pad(float horizontal, float vertical)
        {
            int x = Mathf.RoundToInt(horizontal);
            int y = Mathf.RoundToInt(vertical);
            return new RectOffset(x, x, y, y);
        }

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }

        // ------------------------------------------------------------------ teardown

        void OnDestroy()
        {
            _closed = true;

            if (_current == this)
            {
                _current = null;
            }

            Action callback = _onClosed;
            _onClosed = null;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Debug.LogError("[SeasonEndPanel] A listener threw while the ending closed: " + exception.Message);
            }
        }
    }
}
