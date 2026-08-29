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

        private const string EscribaVocationId = "escriba";
        private const int EscribaGrantPoints = 2;

        /// <summary>
        /// Guard flags so the escriba grants land once each. They are bookkeeping, not progress:
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

        public bool IsPlaying
        {
            get { return playing; }
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

            currentNode = node;
            currentNodeId = nodeId;
            lineIndex = 0;
            playing = true;

            if (ui != null)
            {
                ui.Show();
            }

            StartLine(0);
        }

        /// <summary>
        /// One tap. While a line is still typing this completes it; once a line is fully revealed it
        /// moves on to the next line, or finishes the node.
        /// </summary>
        public void Advance()
        {
            if (!playing)
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
        }

        private void DetachUI()
        {
            if (ui == null)
            {
                return;
            }

            ui.AdvanceRequested -= OnAdvanceRequested;
            ui.ChapterRequested -= OnChapterRequested;
            ui = null;
        }

        private void OnAdvanceRequested()
        {
            Advance();
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
        }

        private void CompleteTyping()
        {
            revealed = currentBody.Length;
            typing = false;
            if (ui != null)
            {
                ui.SetRevealedCharacters(currentBody.Length);
            }
        }

        private void Finish()
        {
            string finishedId = currentNodeId;
            DialogueNode finishedNode = currentNode;

            playing = false;
            typing = false;
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

            ApplyGrants(finishedNode, state);
            DialogueData.RecordSeen(state, finishedId);
            ApplyEscribaGrants(state, timesSeenBefore);

            Action<string> handler = NodeFinished;
            if (handler != null)
            {
                handler(finishedId);
            }
        }

        private void ApplyGrants(DialogueNode node, GameState state)
        {
            if (node == null || node.grants == null)
            {
                return;
            }

            Grants grants = node.grants;

            if (grants.vocation != null && grants.vocation.Count > 0)
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
        /// Two escriba behaviours are owned by dialogue: reading a conversation a second time, and
        /// having spoken with everyone. Opening the chapter reader also scores escriba, but that
        /// action belongs to the reader and is granted there, so it is deliberately not granted here.
        /// </summary>
        private void ApplyEscribaGrants(GameState state, int timesSeenBefore)
        {
            if (state == null)
            {
                return;
            }

            if (timesSeenBefore > 0 && !state.HasFlag(RereadScoredFlag))
            {
                state.SetFlag(RereadScoredFlag);
                GrantEscriba();
            }

            if (!state.HasFlag(AllNpcsScoredFlag) && DialogueData.HasSpokenWithEveryNpc(state))
            {
                state.SetFlag(AllNpcsScoredFlag);
                GrantEscriba();
            }
        }

        private void GrantEscriba()
        {
            VocationTracker tracker = ResolveTracker();
            if (tracker != null)
            {
                tracker.Add(EscribaVocationId, EscribaGrantPoints);
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
            return "⟨texto indisponível — " + reference + "⟩";
        }
    }
}
