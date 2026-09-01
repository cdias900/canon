using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Scripture;
using SheepGate.Vocation;
using UnityEngine;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// Plays one authored dialogue node at a time: queues its lines, types each one out, resolves a
    /// scripture line through <see cref="ScriptureService"/>, and applies the node's grants once the
    /// whole node has been read.
    ///
    /// The model never writes scripture. A line carries either authored text or a reference, never
    /// both, and a reference is resolved to literal text only at runtime from the generated data.
    /// </summary>
    public class DialogueSystem : MonoBehaviour
    {
        /// <summary>Typing reveal speed. Frozen by the architecture contract.</summary>
        public const float CharactersPerSecond = 40f;

        /// <summary>Vocation id exactly as authored in vocations.json.</summary>
        private const string ScribeVocationId = "escriba";
        private const int ScribeGrantPoints = 2;

        /// <summary>
        /// Guard flags so the scribe grants land once each. They are bookkeeping, not progress:
        /// nothing reads them back out to a UI.
        /// </summary>
        private const string RereadScoredFlag = "escriba_reread_scored";

        private const string AllNpcsScoredFlag = "escriba_all_npcs_scored";
        private const string ChapterOpenedFlag = "chapter_opened";
        private const string MoreTrigger = "saber_mais";

        /// <summary>Raised with the node id once its last line has been read and its grants applied.</summary>
        public event Action<string> NodeFinished;

        private DialogueUI ui;
        private DialogueNode currentNode;
        private string currentNodeId;
        private int lineIndex;
        private string currentBody = string.Empty;
        private float revealed;
        private bool typing;
        private bool playing;
        private bool placeholderNoticeLogged;
        private DialogueChoice[] pendingChoices;
        private bool awaitingChoice;
        private string queuedNodeId;

        /// <summary>
        /// What has already been said in the node on screen, oldest first.
        ///
        /// <b>Why it is kept at all.</b> A tap anywhere advances, the catcher covers the whole
        /// screen, and a line that has gone is gone: one impatient thumb and the player has skipped
        /// a sentence they cannot get back. In a game whose north-star metric depends on the text
        /// being read, a conversation with no way to look back is the one place a slip costs
        /// exactly the thing being measured.
        ///
        /// <b>Why only the current node.</b> A full transcript of a run is a reading log — it would
        /// turn what was said into a collection, and there is no version of that which is not a
        /// count of how much someone has read. One node is what a player asks to see again; the
        /// whole day is a record.
        /// </summary>
        private readonly List<DialogueRecord> spoken = new List<DialogueRecord>();

        /// <summary>One line as it was actually shown: who said it, and the words.</summary>
        public struct DialogueRecord
        {
            public string Speaker;
            public string Body;
        }

        /// <summary>The lines already read in the node on screen, oldest first.</summary>
        public IReadOnlyList<DialogueRecord> Spoken
        {
            get { return spoken; }
        }

        public bool IsPlaying
        {
            get { return playing; }
        }

        /// <summary>
        /// True while the last line has been read and the player has branches on screen. The node
        /// is still playing: the world stays locked until a branch is taken.
        /// </summary>
        public bool IsAwaitingChoice
        {
            get { return awaitingChoice; }
        }

        /// <summary>Node currently on screen, or null when nothing is playing.</summary>
        public string CurrentNodeId
        {
            get { return playing ? currentNodeId : null; }
        }

        /// <summary>Returns the dialogue system in the scene, creating one when there is none.</summary>
        public static DialogueSystem EnsureInstance()
        {
            DialogueSystem existing = FindFirstObjectByType<DialogueSystem>();
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new GameObject("DialogueSystem");
            return go.AddComponent<DialogueSystem>();
        }

        /// <summary>Starts a node. Unknown ids and re-entrant calls are logged and ignored.</summary>
        public void Play(string nodeId)
        {
            if (playing)
            {
                Debug.LogWarning("[Dialogue] Play(\"" + nodeId + "\") ignored: \"" + currentNodeId + "\" is still playing.");
                return;
            }

            DialogueNode node;
            if (!DialogueData.TryGetNode(nodeId, out node))
            {
                Debug.LogWarning("[Dialogue] Play(\"" + nodeId + "\") ignored: unknown node id.");
                return;
            }

            if (node.lines == null || node.lines.Length == 0)
            {
                Debug.LogWarning("[Dialogue] Play(\"" + nodeId + "\") ignored: node has no lines.");
                return;
            }

            EnsureUI();

            // A conversation owns the screen from here. The toast is not a modal, so nothing else
            // takes it down, and a line about a rubble pile sitting under the speech bubble is the
            // interface talking over the person.
            SheepGate.UI.Toast.Dismiss();

            currentNode = node;
            currentNodeId = nodeId;
            lineIndex = 0;
            playing = true;
            awaitingChoice = false;
            pendingChoices = null;

            // The record is per node, so it starts empty here. See the field for why it is not a
            // transcript of the whole run.
            spoken.Clear();

            if (ui != null)
            {
                ui.HideChoices();
                ui.Show();
            }

            StartLine(0);
        }

        /// <summary>
        /// One tap. While a line is still typing this completes it; once a line is fully revealed it
        /// moves on to the next line, or finishes the node. While branches are on screen a tap does
        /// nothing: the only way out of a decision is to take one.
        /// </summary>
        public void Advance()
        {
            if (!playing)
            {
                return;
            }

            if (awaitingChoice)
            {
                return;
            }

            if (typing)
            {
                CompleteTyping();
                return;
            }

            lineIndex++;
            if (currentNode != null && currentNode.lines != null && lineIndex < currentNode.lines.Length)
            {
                StartLine(lineIndex);
            }
            else
            {
                Finish();
            }
        }

        private void Awake()
        {
            ServiceLocator.Register(this);
            EnsureUI();
        }

        private void OnDestroy()
        {
            DetachUI();
        }

        private void Update()
        {
            if (!playing || !typing)
            {
                return;
            }

            // Unscaled: a paused game (the contest halts for the page) must not freeze a line mid-word.
            revealed += CharactersPerSecond * Time.unscaledDeltaTime;

            if (revealed >= currentBody.Length)
            {
                CompleteTyping();
                return;
            }

            if (ui != null)
            {
                ui.SetRevealedCharacters(Mathf.FloorToInt(revealed));
            }
        }

        private void EnsureUI()
        {
            if (ui != null)
            {
                return;
            }

            ui = DialogueUI.EnsureInstance();
            if (ui == null)
            {
                return;
            }

            ui.AdvanceRequested += OnAdvanceRequested;
            ui.ChapterRequested += OnChapterRequested;
            ui.ChoiceSelected += OnChoiceSelected;
            ui.HistoryRequested += OnHistoryRequested;
        }

        private void DetachUI()
        {
            if (ui == null)
            {
                return;
            }

            ui.AdvanceRequested -= OnAdvanceRequested;
            ui.ChapterRequested -= OnChapterRequested;
            ui.ChoiceSelected -= OnChoiceSelected;
            ui.HistoryRequested -= OnHistoryRequested;
            ui = null;
        }

        private void OnAdvanceRequested()
        {
            Advance();
        }

        /// <summary>
        /// Shows what has already been said in this node.
        ///
        /// The line still typing is included, because <see cref="BeginBody"/> records a line as it
        /// starts: a player who held the screen halfway through a sentence is asking about that
        /// sentence too, and showing it whole is the answer they wanted.
        /// </summary>
        private void OnHistoryRequested()
        {
            DialogueHistoryPanel.Show(spoken);
        }

        private void StartLine(int index)
        {
            DialogueLine line = currentNode.lines[index];
            string speaker = DialogueData.DisplayNameOf(currentNode.npc);
            string frame = line != null ? line.frame : null;

            if (line != null && !string.IsNullOrEmpty(line.verse))
            {
                StartVerseLine(line, speaker, frame);
            }
            else
            {
                string body = line != null && line.text != null ? line.text : string.Empty;
                BeginBody(body);
                if (ui != null)
                {
                    ui.BeginLine(speaker, frame, body, false, null, null, null);
                    ui.SetRevealedCharacters(0);
                }
            }

            // An empty line has nothing to type out, so no typing pass will ever complete it and
            // offer the branches. Offer them here instead.
            if (!typing)
            {
                OfferChoicesIfAny();
            }
        }

        private void StartVerseLine(DialogueLine line, string speaker, string frame)
        {
            string reference = line.verse.Trim();

            VerseEntry entry = null;
            try
            {
                entry = ScriptureService.GetVerse(reference);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Dialogue] ScriptureService.GetVerse(\"" + reference + "\") failed: " + e.Message);
            }

            NoteIfPlaceholderBuild();

            string body = entry != null ? entry.text : null;
            if (string.IsNullOrWhiteSpace(body))
            {
                // Never an empty balloon and never an invented line: show the visible marker instead.
                body = MissingTextMarker(reference);
            }

            string refDisplay = entry != null ? entry.ref_display : null;
            if (string.IsNullOrWhiteSpace(refDisplay))
            {
                refDisplay = reference;
            }

            string chapterRef = ChapterRefOf(reference);

            BeginBody(body);

            if (ui != null)
            {
                ui.BeginLine(speaker, frame, body, true, refDisplay, reference, chapterRef);
                ui.SetRevealedCharacters(0);
            }

            Dictionary<string, object> props = new Dictionary<string, object>
            {
                { "ref", reference },
                { "context", currentNodeId }
            };
            Telemetry.Track(TelemetryEvents.VerseShown, props);
        }

        private void BeginBody(string body)
        {
            currentBody = body ?? string.Empty;
            revealed = 0f;
            typing = currentBody.Length > 0;

            // Recorded as the line begins, not as it ends: a player who skipped a line by tapping
            // twice never reached its end, and that line is precisely the one they want back.
            if (currentBody.Length > 0)
            {
                spoken.Add(new DialogueRecord
                {
                    Speaker = currentNode != null ? DialogueData.DisplayNameOf(currentNode.npc) : string.Empty,
                    Body = currentBody
                });
            }
        }

        private void CompleteTyping()
        {
            revealed = currentBody.Length;
            typing = false;
            if (ui != null)
            {
                ui.SetRevealedCharacters(currentBody.Length);
            }

            OfferChoicesIfAny();
        }

        /// <summary>
        /// Puts the node's branches on screen once its last line has been fully revealed. A node
        /// with no branches, or whose branches are all gated out, is untouched by this and ends on
        /// the next tap exactly as it always did.
        /// </summary>
        private void OfferChoicesIfAny()
        {
            if (!playing || awaitingChoice || ui == null)
            {
                return;
            }

            if (currentNode == null || currentNode.lines == null)
            {
                return;
            }

            // Branches belong to the end of a node, never in the middle of one.
            if (lineIndex < currentNode.lines.Length - 1)
            {
                return;
            }

            List<DialogueChoice> available = AvailableChoices(currentNode);
            if (available.Count == 0)
            {
                return;
            }

            List<string> labels = new List<string>(available.Count);
            for (int i = 0; i < available.Count; i++)
            {
                labels.Add(available[i].text);
            }

            if (!ui.ShowChoices(labels))
            {
                // Nothing reached the screen, so the node must stay tappable: an invisible decision
                // would be a dead end with no way out of it.
                Debug.LogWarning("[Dialogue] Node \"" + currentNodeId + "\" offers branches but none could be shown.");
                return;
            }

            pendingChoices = available.ToArray();
            awaitingChoice = true;
        }

        /// <summary>
        /// The branches this node is offering right now. Gates read <see cref="GameState"/> only,
        /// so the dialogue layer decides what to show without asking the world anything.
        /// </summary>
        private List<DialogueChoice> AvailableChoices(DialogueNode node)
        {
            List<DialogueChoice> result = new List<DialogueChoice>();
            if (node == null || node.choices == null || node.choices.Length == 0)
            {
                return result;
            }

            GameState state;
            if (!ServiceLocator.TryGet(out state))
            {
                state = null;
            }

            for (int i = 0; i < node.choices.Length; i++)
            {
                DialogueChoice choice = node.choices[i];
                if (choice == null || string.IsNullOrEmpty(choice.text))
                {
                    // A button with nothing written on it is an authoring bug, not a branch.
                    continue;
                }

                if (!string.IsNullOrEmpty(choice.hidden_if_flag)
                    && state != null && state.HasFlag(choice.hidden_if_flag))
                {
                    continue;
                }

                if (choice.requires_rubble > 0 && (state == null || state.rubble < choice.requires_rubble))
                {
                    continue;
                }

                result.Add(choice);
            }

            return result;
        }

        private void OnChoiceSelected(int index)
        {
            if (!playing || !awaitingChoice)
            {
                return;
            }

            DialogueChoice[] choices = pendingChoices;
            if (choices == null || index < 0 || index >= choices.Length)
            {
                return;
            }

            DialogueChoice choice = choices[index];
            awaitingChoice = false;
            pendingChoices = null;

            if (ui != null)
            {
                ui.HideChoices();
            }

            queuedNodeId = ResolveChoiceTarget(choice);
            Finish(choice);
        }

        /// <summary>
        /// The node a branch leads to, or null when the branch simply ends the conversation. An id
        /// that is not in the authored data is reported and dropped rather than left to soft-lock.
        /// </summary>
        private string ResolveChoiceTarget(DialogueChoice choice)
        {
            if (choice == null || string.IsNullOrEmpty(choice.next))
            {
                return null;
            }

            if (DialogueData.TryGetNode(choice.next, out _))
            {
                return choice.next;
            }

            Debug.LogWarning("[Dialogue] Choice \"" + (choice.id ?? choice.text)
                             + "\" points at unknown node \"" + choice.next + "\"; the conversation just ends.");
            return null;
        }

        private void Finish(DialogueChoice choice = null)
        {
            string finishedId = currentNodeId;
            DialogueNode finishedNode = currentNode;
            string nextNodeId = queuedNodeId;

            playing = false;
            typing = false;
            awaitingChoice = false;
            pendingChoices = null;
            queuedNodeId = null;
            currentNode = null;
            currentNodeId = null;
            currentBody = string.Empty;

            if (ui != null)
            {
                ui.Hide();
            }

            GameState state;
            if (!ServiceLocator.TryGet(out state))
            {
                state = null;
            }

            int timesSeenBefore = DialogueData.TimesSeen(state, finishedId);

            // Vocation is scored the first time a conversation is read and never again: several
            // nodes are deliberately replayable, and re-reading is already paid for by the scribe
            // grant below. Flags are not progress and are applied on every playthrough.
            bool firstReading = timesSeenBefore == 0;

            ApplyGrants(finishedNode != null ? finishedNode.grants : null, state, firstReading);
            ApplyGrants(choice != null ? choice.grants : null, state, firstReading);

            DialogueData.RecordSeen(state, finishedId);
            ApplyScribeGrants(state, timesSeenBefore);

            Action<string> handler = NodeFinished;
            if (handler != null)
            {
                handler(finishedId);
            }

            // The branch's target plays only if a listener has not already started something else.
            if (!string.IsNullOrEmpty(nextNodeId) && !playing)
            {
                Play(nextNodeId);
            }
        }

        private void ApplyGrants(Grants grants, GameState state, bool allowVocation)
        {
            if (grants == null)
            {
                return;
            }

            if (allowVocation && grants.vocation != null && grants.vocation.Count > 0)
            {
                VocationTracker tracker = ResolveTracker();
                if (tracker != null)
                {
                    foreach (KeyValuePair<string, int> pair in grants.vocation)
                    {
                        if (!string.IsNullOrEmpty(pair.Key))
                        {
                            tracker.Add(pair.Key, pair.Value);
                        }
                    }
                }
            }

            bool hasFlags = (grants.flags != null && grants.flags.Count > 0)
                            || !string.IsNullOrEmpty(grants.set_flag);
            if (!hasFlags)
            {
                return;
            }

            if (state == null)
            {
                Debug.LogWarning("[Dialogue] No GameState registered; flags from the node were not applied.");
                return;
            }

            if (grants.flags != null)
            {
                foreach (KeyValuePair<string, int> pair in grants.flags)
                {
                    // Authoring writes a flag as key/value; any non-zero value means "raise it".
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != 0)
                    {
                        state.SetFlag(pair.Key);
                    }
                }
            }

            if (!string.IsNullOrEmpty(grants.set_flag))
            {
                state.SetFlag(grants.set_flag);
            }
        }

        /// <summary>
        /// Two scribe behaviours are owned by dialogue: reading a conversation a second time, and
        /// having spoken with everyone. Opening the chapter reader also scores the scribe, but that
        /// action belongs to the reader and is granted there, so it is deliberately not granted here.
        /// </summary>
        private void ApplyScribeGrants(GameState state, int timesSeenBefore)
        {
            if (state == null)
            {
                return;
            }

            if (timesSeenBefore > 0 && !state.HasFlag(RereadScoredFlag))
            {
                state.SetFlag(RereadScoredFlag);
                GrantScribe();
            }

            if (!state.HasFlag(AllNpcsScoredFlag) && DialogueData.HasSpokenWithEveryNpc(state))
            {
                state.SetFlag(AllNpcsScoredFlag);
                GrantScribe();
            }
        }

        private void GrantScribe()
        {
            VocationTracker tracker = ResolveTracker();
            if (tracker != null)
            {
                tracker.Add(ScribeVocationId, ScribeGrantPoints);
            }
        }

        private void OnChapterRequested(string chapterRef, string verseRef)
        {
            if (string.IsNullOrEmpty(chapterRef))
            {
                return;
            }

            // Report the open only when the reader was actually reached: a phantom chapter_opened
            // would corrupt the funnel that deep_read is measured against.
            if (!ChapterReaderBridge.Open(chapterRef))
            {
                return;
            }

            Dictionary<string, object> props = new Dictionary<string, object>
            {
                { "ref", chapterRef },
                { "trigger", MoreTrigger }
            };
            Telemetry.Track(TelemetryEvents.ChapterOpened, props);

            GameState state;
            if (ServiceLocator.TryGet(out state) && state != null)
            {
                state.SetFlag(ChapterOpenedFlag);
            }
        }

        private VocationTracker ResolveTracker()
        {
            VocationTracker tracker;
            if (ServiceLocator.TryGet(out tracker) && tracker != null)
            {
                return tracker;
            }

            Debug.LogWarning("[Dialogue] No VocationTracker registered; a vocation grant was skipped.");
            return null;
        }

        private void NoteIfPlaceholderBuild()
        {
            if (placeholderNoticeLogged)
            {
                return;
            }

            bool placeholder;
            try
            {
                placeholder = ScriptureService.IsPlaceholderBuild;
            }
            catch (Exception)
            {
                return;
            }

            if (!placeholder)
            {
                return;
            }

            placeholderNoticeLogged = true;
            Debug.Log("[Dialogue] Placeholder scripture build: bubbles show the unavailable-text marker with the reference.");
        }

        private static string ChapterRefOf(string verseRef)
        {
            if (string.IsNullOrEmpty(verseRef))
            {
                return null;
            }

            string chapter = null;
            try
            {
                chapter = ScriptureService.ChapterRefOf(verseRef);
            }
            catch (Exception)
            {
                chapter = null;
            }

            if (!string.IsNullOrEmpty(chapter))
            {
                return chapter;
            }

            int lastDot = verseRef.LastIndexOf('.');
            return lastDot > 0 ? verseRef.Substring(0, lastDot) : verseRef;
        }

        private static string MissingTextMarker(string reference)
        {
            return Loc.T("scripture.unavailable", reference);
        }
    }
}
