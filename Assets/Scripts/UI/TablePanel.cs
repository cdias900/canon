using System;
using System.Collections;
using System.Collections.Generic;
using SheepGate.Contest;
using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// Obra em grupo: the screen a person actually touches. Design in docs/multiplayer.md.
    ///
    /// <b>Three states, one panel, and the first one is the whole UX argument.</b> A player who has
    /// never done this sees two buttons and nothing else — make one, or type a code somebody gave
    /// them. No lobby, no list of strangers, no explanation of what a table is before they have
    /// one. The screen earns its complexity: the seats and the feed appear once there is a table to
    /// have them.
    ///
    /// <b>Why it opens on the seats rather than on the chat.</b> This is a building site, and the
    /// question a returning player has is "what happened while I was gone", not "who is typing".
    /// The feed answers it in the table's own vocabulary, and the composed lines sit under it as a
    /// reply — speech is a response to the work, which is also the only shape rule 17 allows for
    /// the younger half of the audience.
    ///
    /// <b>The group mission lives here and not in the contest screen.</b> docs/multiplayer.md §06
    /// says the raid at a table is the solo raid with four seats — and it is, in the DATA: the same
    /// contest.json, the same moves, the same page at the same turn. But it is not the same
    /// component. <see cref="MoraleContest"/> writes its ending into the player's own save (the
    /// resolved flag that stops stage six being fought twice, the unfinished work lost when the
    /// line breaks), and a table's raid happens whenever the trumpet says, to people at different
    /// stages of their own seasons. Running it through that class would let a table's loss tear
    /// down a player's own wall, which §04 forbids in as many words. So this panel renders what
    /// the server resolved and touches no save at all.
    /// </summary>
    public sealed class TablePanel : MonoBehaviour
    {
        const string ModalId = "table";

        /// <summary>
        /// The code of the table this device last sat at. Kept beside the player id, so that
        /// closing the screen — or the app — does not lose the table: the trumpet names an hour,
        /// and a person who comes back at that hour must find their seat, not a code field.
        /// </summary>
        const string CodePrefKey = "sheepgate.table.code";

        /// <summary>How often the feed re-reads while the panel is open. Asynchronous, not live.</summary>
        const float RefreshSeconds = 6f;

        static TablePanel _current;

        RectTransform _card;
        RectTransform _body;
        Text _status;
        InputField _codeField;
        string _code;
        string _seat;
        long _lastEventId;
        Coroutine _polling;

        /// <summary>The page opens once per raid on this screen, at the turn the tuning names.</summary>
        bool _pageShown;

        /// <summary>Whether the last snapshot had a raid in progress; drives which rows are shown.</summary>
        bool _raidOpen;

        /// <summary>A raid in progress or a call awaiting an answer: the rows that make room for them.</summary>
        bool _busy;

        public static bool Available { get { return TableService.Available; } }

        /// <summary>Opens the panel, or does nothing at all when no endpoint is configured.</summary>
        public static TablePanel Show()
        {
            if (!TableService.Available)
            {
                Debug.Log("[Table] no -table-url; the group screen stays closed.");
                return null;
            }

            if (_current != null)
            {
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<TablePanel>();
            panel.Build();
            _current = panel;
            return panel;
        }

        void Build()
        {
            var container = (RectTransform)transform;

            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.sizeDelta = new Vector2(-2f * DesignTokens.Space.Gutter, 0f);
            _card.anchoredPosition = Vector2.zero;

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20), Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S24), Mathf.RoundToInt(DesignTokens.Space.S24)));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateText(_card, "Title", Loc.T("table.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Title);

            _status = UIKit.CreateText(_card, "Status", Loc.T("table.intro"),
                DesignTokens.Type.Body, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);

            _body = UIKit.CreateRect("Body", _card);
            UIKit.VerticalGroup(_body.gameObject, DesignTokens.Space.S12, new RectOffset());
            var bodyFitter = _body.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateButton(_card, "Close", Loc.T("table.close"),
                UIKit.ButtonVariant.Ghost, Close);

            // Back to the seat you had, if you had one. The server answers a rejoin with the
            // same seat, and answers a code whose season ended with a refusal — in which case the
            // two doors are shown, the same as for somebody who never sat anywhere.
            string remembered = PlayerPrefs.GetString(CodePrefKey, string.Empty);
            if (!string.IsNullOrEmpty(remembered))
            {
                _status.text = Loc.T("table.working");
                TableService.Join(remembered, Band(), PlayerName(), answer =>
                {
                    if (answer == null || !string.IsNullOrEmpty(answer.Error))
                    {
                        PlayerPrefs.DeleteKey(CodePrefKey);
                        ShowEntry();
                        return;
                    }

                    OnJoined(answer);
                });
                return;
            }

            ShowEntry();
        }

        // ------------------------------------------------------------------ state 1: no table

        /// <summary>Two doors and nothing else. Everything a person needs to know is on the buttons.</summary>
        void ShowEntry()
        {
            Clear(_body);
            _status.text = Loc.T("table.intro");

            UIKit.CreateButton(_body, "CreateTable", Loc.T("table.create"),
                UIKit.ButtonVariant.Primary, () =>
                {
                    _status.text = Loc.T("table.working");
                    TableService.CreateTable(Band(), PlayerName(), OnJoined);
                });

            UIKit.CreateText(_body, "Or", Loc.T("table.or"),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter);

            // Upper-cased as it is typed, because the code is read off a screen and typed by
            // somebody who is not looking at their own hands.
            _codeField = UIKit.CreateInputField(_body, "CodeField", Loc.T("table.code_hint"), 6,
                value =>
                {
                    if (_codeField != null && value != value.ToUpperInvariant())
                    {
                        _codeField.text = value.ToUpperInvariant();
                    }
                });

            UIKit.CreateButton(_body, "JoinTable", Loc.T("table.join"),
                UIKit.ButtonVariant.Secondary, () =>
                {
                    string code = _codeField != null ? _codeField.text.Trim().ToUpperInvariant() : string.Empty;
                    if (code.Length != 6)
                    {
                        _status.text = Loc.T("table.code_short");
                        return;
                    }

                    _status.text = Loc.T("table.working");
                    TableService.Join(code, Band(), PlayerName(), OnJoined);
                });
        }

        void OnJoined(TableService.Snapshot answer)
        {
            if (answer == null)
            {
                _status.text = Loc.T("table.offline");
                return;
            }

            if (!string.IsNullOrEmpty(answer.Error))
            {
                // The server's refusals are sentences worth showing. "This table is not for your
                // age band" is the product working, and a generic failure message would hide the
                // one thing the person needs to understand.
                _status.text = answer.Error;
                return;
            }

            _code = answer.Code ?? (answer.Table != null ? answer.Table.Code : null);
            _seat = answer.Seat;
            PlayerPrefs.SetString(CodePrefKey, _code ?? string.Empty);
            PlayerPrefs.Save();
            ShowTable();
        }

        // ------------------------------------------------------------------ state 2: at a table

        void ShowTable()
        {
            Clear(_body);
            _status.text = string.Format(Loc.T("table.you_are"), SeatName(_seat));

            // The code, big, because the only thing anyone does on this screen in the first minute
            // is read it out loud to the person beside them.
            Text code = UIKit.CreateText(_body, "Code", _code,
                DesignTokens.Type.Display, DesignTokens.Brand.Secondary, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.Display);
            code.supportRichText = false;

            UIKit.CreateText(_body, "CodeCaption", Loc.T("table.code_caption"),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter);

            // In reading order: the call that is waiting for an answer, the fight in progress, who
            // is here, what happened. The first two are empty most of the time and take no room.
            UIKit.CreateRect("Call", _body);
            UIKit.CreateRect("Raid", _body);
            UIKit.CreateRect("Seats", _body);
            UIKit.CreateRect("Feed", _body);

            UIKit.CreateButton(_body, "Trumpet", Loc.T("table.trumpet"),
                UIKit.ButtonVariant.Primary, () =>
                {
                    bool watchPosted, acceptedInvite;
                    Preparation(out watchPosted, out acceptedInvite);
                    long inTwoHours = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
                    TableService.SoundTrumpet(_code, inTwoHours, 4, watchPosted, acceptedInvite, _ => Refresh());
                });

            BuildSayRow();

            if (_polling == null)
            {
                _polling = StartCoroutine(Poll());
            }

            Refresh();
        }

        /// <summary>
        /// The composed vocabulary, as buttons.
        ///
        /// Four of the twenty, chosen for the situation the player is most likely in rather than
        /// the first four in the file — this is where a fixed vocabulary either feels rich or feels
        /// like a phrasebook, and the difference is entirely which lines are one tap away.
        /// </summary>
        void BuildSayRow()
        {
            RectTransform row = UIKit.CreateRect("SayRow", _body);
            UIKit.VerticalGroup(row.gameObject, DesignTokens.Space.S8, new RectOffset());
            var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string[] keys =
            {
                "table.line.taking_the_exposed",
                "table.line.need_stone",
                "table.line.will_watch",
                "table.line.cannot_today"
            };

            foreach (string key in keys)
            {
                string captured = key;
                UIKit.CreateButton(row, "Say_" + key, Loc.T(key),
                    UIKit.ButtonVariant.Secondary, () =>
                    {
                        TableService.Say(_code, captured, answer =>
                        {
                            if (answer != null && !string.IsNullOrEmpty(answer.Error))
                            {
                                _status.text = answer.Error;
                                return;
                            }

                            Refresh();
                        });
                    });
            }
        }

        IEnumerator Poll()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(RefreshSeconds);
                Refresh();
            }
        }

        void Refresh()
        {
            if (string.IsNullOrEmpty(_code))
            {
                return;
            }

            TableService.Feed(_code, snapshot =>
            {
                if (snapshot == null || snapshot.Seats == null)
                {
                    return;
                }

                _raidOpen = snapshot.Raid != null && snapshot.Raid.Open;
                _busy = _raidOpen || PendingTrumpet(snapshot) != null;

                RenderCall(snapshot);
                RenderRaid(snapshot);
                RenderSeats(snapshot);
                RenderFeed(snapshot);

                // While a call is waiting for an answer or a raid is on, the code, the vocabulary
                // and the trumpet step aside; while a raid is on, so do the seats. Not for focus —
                // for the vertical budget. The first version kept everything, and on the phone the
                // title sat under the camera housing and Fechar fell off the bottom: two buttons
                // for the call, or two meters and three moves for the raid, are a screen's worth
                // on their own. The code is for inviting, and nobody invites mid-fight; the feed
                // stays, because the resolved turns arrive there.
                SetShown("Code", !_busy);
                SetShown("CodeCaption", !_busy);
                SetShown("SayRow", !_busy);
                SetShown("Trumpet", !_busy);
                SetShown("Seats", !_raidOpen);
            });
        }

        // ------------------------------------------------------------------ the call

        /// <summary>
        /// A trumpet whose hour has not come, with the two answers §05 gives: I am coming, or I
        /// cannot today — and the second is a legitimate move, in as many words. Nothing here
        /// counts down, nags, or remembers who said no.
        /// </summary>
        void RenderCall(TableService.Snapshot snapshot)
        {
            Transform host = _body.Find("Call");
            if (host == null) return;

            Column((RectTransform)host, DesignTokens.Space.S8);

            TableService.Event call = PendingTrumpet(snapshot);
            if (call == null) return;

            long at = call.Payload != null && call.Payload["at"] != null ? (long)call.Payload["at"] : 0L;

            UIKit.CreateText((RectTransform)host, "CallLine",
                string.Format(Loc.T("table.call.line"), SeatName(call.Seat), HourOf(at)),
                DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft);

            string mine = MyAnswer(snapshot, call.Id);
            if (mine != null)
            {
                UIKit.CreateText((RectTransform)host, "CallMine", Loc.T(mine),
                    DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
            }

            long trumpetId = call.Id;
            UIKit.CreateButton((RectTransform)host, "Coming", Loc.T("table.call.coming"),
                UIKit.ButtonVariant.Primary, () => Answer(trumpetId, true));
            UIKit.CreateButton((RectTransform)host, "NotToday", Loc.T("table.call.not_today"),
                UIKit.ButtonVariant.Ghost, () => Answer(trumpetId, false));
        }

        void Answer(long trumpetId, bool coming)
        {
            bool watchPosted, acceptedInvite;
            Preparation(out watchPosted, out acceptedInvite);
            TableService.AnswerTrumpet(_code, trumpetId, coming, watchPosted, acceptedInvite, answer =>
            {
                if (answer != null && !string.IsNullOrEmpty(answer.Error))
                {
                    _status.text = answer.Error;
                    return;
                }

                Refresh();
            });
        }

        /// <summary>The newest trumpet still ahead of the clock and not yet opened or skipped.</summary>
        static TableService.Event PendingTrumpet(TableService.Snapshot snapshot)
        {
            if (snapshot.Events == null) return null;

            var handled = new HashSet<long>();
            foreach (TableService.Event e in snapshot.Events)
            {
                if ((e.Kind == "raid_opened" || e.Kind == "raid_skipped") && e.Payload != null && e.Payload["trumpetId"] != null)
                {
                    handled.Add((long)e.Payload["trumpetId"]);
                }
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (int i = snapshot.Events.Count - 1; i >= 0; i--)
            {
                TableService.Event e = snapshot.Events[i];
                if (e.Kind != "trumpet" || handled.Contains(e.Id) || e.Payload == null || e.Payload["at"] == null) continue;
                if ((long)e.Payload["at"] > now) return e;
            }

            return null;
        }

        /// <summary>What this seat last said to that trumpet, as a key; null when it said nothing.</summary>
        string MyAnswer(TableService.Snapshot snapshot, long trumpetId)
        {
            if (snapshot.Events == null || string.IsNullOrEmpty(_seat)) return null;

            for (int i = snapshot.Events.Count - 1; i >= 0; i--)
            {
                TableService.Event e = snapshot.Events[i];
                if (e.Kind == "trumpet" && e.Id == trumpetId && e.Seat == _seat)
                {
                    return "table.call.you_sounded";
                }
                if (e.Kind != "answered" || e.Seat != _seat || e.Payload == null) continue;
                if (e.Payload["trumpetId"] == null || (long)e.Payload["trumpetId"] != trumpetId) continue;
                return e.Payload["coming"] != null && (bool)e.Payload["coming"]
                    ? "table.call.you_are_coming" : "table.call.you_cannot";
            }

            return null;
        }

        // ------------------------------------------------------------------ the raid

        /// <summary>
        /// The fight, as the server lets this seat see it. The two meters and the turn are the
        /// contest screen's own words; the moves are the same four, named from the same locale
        /// file. What is different is the wait: a move goes to the server and this screen shows
        /// nothing about it until the turn closes — which is rule 11 on the client side, and it
        /// is why the only thing that changes after a tap is one line saying how many have answered.
        /// </summary>
        void RenderRaid(TableService.Snapshot snapshot)
        {
            Transform host = _body.Find("Raid");
            if (host == null) return;

            Column((RectTransform)host, DesignTokens.Space.S8);

            TableService.RaidState raid = snapshot.Raid;
            if (raid == null) return;

            var rect = (RectTransform)host;

            if (!raid.Open)
            {
                // The ending stays on screen until the next trumpet replaces it — the feed carries
                // the turns, and this is the one line that says how it went.
                UIKit.CreateText(rect, "Outcome", Loc.T("table.raid.outcome." + (raid.Outcome ?? "limit")),
                    DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft);
                _pageShown = false;
                return;
            }

            UIKit.CreateText(rect, "RaidTitle", Loc.T("table.raid.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Title);
            UIKit.CreateText(rect, "Turn", Loc.T("contest.turn", raid.Turn, raid.TurnLimit),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);

            ProgressBar morale = UIKit.CreateProgress(rect, "MoraleMeter", Loc.T("contest.meter.morale"));
            morale.SetValue(raid.Morale, raid.MoraleMax);
            ProgressBar resolve = UIKit.CreateProgress(rect, "ResolveMeter", Loc.T("contest.meter.resolve"));
            resolve.SetValue(raid.Resolve, raid.ResolveMax);

            int present = raid.Present != null ? raid.Present.Count : 0;
            UIKit.CreateText(rect, "Waiting",
                string.Format(Loc.T("table.raid.waiting"), raid.CommittedCount, present),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);

            // A Página, at the turn the tuning names, for everyone at once — the same panel the
            // solo contest opens, on the same passage. Once per raid on this screen; the panel
            // itself declines to open twice in a run, and that is right too.
            if (raid.PageTurn > 0 && raid.Turn == raid.PageTurn && !_pageShown && raid.YouArePresent)
            {
                _pageShown = true;
                ThePagePanel.Show(raid.Turn, raid.PageVerse, null, () => { });
            }

            if (!raid.YouArePresent)
            {
                UIKit.CreateText(rect, "NotPresent", Loc.T("table.raid.not_present"),
                    DesignTokens.Type.Body, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
                return;
            }

            if (raid.YouCommitted)
            {
                UIKit.CreateText(rect, "Held", Loc.T("table.raid.held"),
                    DesignTokens.Type.Body, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
                return;
            }

            if (raid.Moves == null) return;

            int turn = raid.Turn;
            foreach (TableService.RaidMove move in raid.Moves)
            {
                if (!move.Open) continue;

                string moveId = move.Id;
                UIKit.CreateButton(rect, "Move_" + moveId, MoveName(moveId),
                    UIKit.ButtonVariant.Secondary, () =>
                    {
                        TableService.CommitMove(_code, turn, moveId, answer =>
                        {
                            if (answer != null && !string.IsNullOrEmpty(answer.Error))
                            {
                                _status.text = answer.Error;
                                return;
                            }

                            Refresh();
                        });
                    });
            }
        }

        /// <summary>
        /// Who is here, and — as importantly — who is not.
        ///
        /// An empty seat is drawn rather than hidden, and named for the resident who plays it. That
        /// is the honest picture: the house is holding that stretch, the wall is still going up,
        /// and there is room for one more person. A list of only the players present would make a
        /// table of two look like a failure instead of like a start.
        /// </summary>
        void RenderSeats(TableService.Snapshot snapshot)
        {
            Transform host = _body.Find("Seats");
            if (host == null) return;

            Column((RectTransform)host, DesignTokens.Space.S4);

            // People first, one line each, because those are the lines somebody reads.
            var free = new List<string>();
            foreach (TableService.Seat seat in snapshot.Seats)
            {
                if (!seat.Taken)
                {
                    free.Add(SeatName(seat.Id));
                    continue;
                }

                UIKit.CreateText((RectTransform)host, "Seat_" + seat.Id,
                    string.Format(Loc.T("table.seat_taken"), seat.Name, SeatName(seat.Id)),
                    DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft);
            }

            // The empty seats in ONE line, not five.
            //
            // Listing them one per row was the first version, and on a phone it pushed the card
            // past the safe area and put the title under the camera housing — five rows that all
            // said the same thing, to say something that is true of all of them at once. It also
            // read as five failures rather than as one fact: the house is holding those stretches,
            // and the wall is still going up.
            if (free.Count > 0)
            {
                UIKit.CreateText((RectTransform)host, "SeatsFree",
                    string.Format(Loc.T("table.seats_free"), string.Join(", ", free)),
                    DesignTokens.Type.Body, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
            }
        }

        void RenderFeed(TableService.Snapshot snapshot)
        {
            Transform host = _body.Find("Feed");
            if (host == null || snapshot.Events == null) return;

            Column((RectTransform)host, DesignTokens.Space.S4);

            // Fewer lines while the call or the raid is taking the room above.
            int limit = _busy ? 4 : 6;
            int shown = 0;
            for (int i = snapshot.Events.Count - 1; i >= 0 && shown < limit; i--)
            {
                TableService.Event e = snapshot.Events[i];
                string text = Describe(e);
                if (string.IsNullOrEmpty(text)) continue;

                UIKit.CreateText((RectTransform)host, "Event_" + e.Id, text,
                    DesignTokens.Type.Body, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
                shown++;
                _lastEventId = Math.Max(_lastEventId, e.Id);
            }
        }

        /// <summary>
        /// One line of feed.
        ///
        /// A composed line is resolved through <see cref="Loc"/> HERE, from the key the server
        /// stored — which is what lets two people at one table play in different languages and each
        /// read the conversation in their own. The server never held the words.
        /// </summary>
        string Describe(TableService.Event e)
        {
            string who = SeatName(e.Seat);

            switch (e.Kind)
            {
                case "joined":
                    return string.Format(Loc.T("table.feed_joined"), who);
                case "said":
                    if (!string.IsNullOrEmpty(e.LineKey))
                    {
                        return who + ": " + Loc.T(e.LineKey);
                    }
                    return !string.IsNullOrEmpty(e.Body) ? who + ": " + e.Body : null;
                case "trumpet":
                    return string.Format(Loc.T("table.feed_trumpet"), who);
                case "answered":
                    if (e.Payload == null || e.Payload["coming"] == null) return null;
                    return string.Format(Loc.T((bool)e.Payload["coming"]
                        ? "table.feed_coming" : "table.feed_not_today"), who);
                case "raid_opened":
                    return string.Format(Loc.T("table.feed_raid_opened"), PresentNames(e));
                case "raid_skipped":
                    return Loc.T("table.feed_raid_skipped");
                case "resolved":
                    return string.Format(Loc.T("table.feed_resolved"), e.Payload != null ? (int)e.Payload["turn"] : 0,
                        MovesOf(e));
                case "raid_finished":
                    return Loc.T("table.raid.outcome." + (e.Payload != null && e.Payload["outcome"] != null
                        ? (string)e.Payload["outcome"] : "limit"));
                default:
                    return null;
            }
        }

        /// <summary>Who came, by resident name, in one line.</summary>
        static string PresentNames(TableService.Event e)
        {
            var names = new List<string>();
            if (e.Payload != null && e.Payload["present"] != null)
            {
                foreach (var seat in e.Payload["present"])
                {
                    names.Add(SeatName((string)seat));
                }
            }
            return string.Join(", ", names);
        }

        /// <summary>
        /// "Salum: Segurar a linha, Baruque: Chamar os outros" — every move of a closed turn, all at
        /// once, which is the moment rule 11 was holding them for. A seat that did not pick is not
        /// listed: absence is not a move and is not named as one.
        /// </summary>
        static string MovesOf(TableService.Event e)
        {
            var parts = new List<string>();
            if (e.Payload != null && e.Payload["moves"] != null)
            {
                foreach (var pair in (Newtonsoft.Json.Linq.JObject)e.Payload["moves"])
                {
                    parts.Add(SeatName(pair.Key) + ": " + MoveName((string)pair.Value));
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : Loc.T("table.feed_nobody_moved");
        }

        /// <summary>The move's name from the locale's contest file, the way the contest screen reads it.</summary>
        static string MoveName(string moveId)
        {
            ContestConfig config;
            if (GameData.Contests != null && GameData.Contests.TryGetValue("raid", out config) && config != null && config.moves != null)
            {
                foreach (ContestMoveDef move in config.moves)
                {
                    if (move != null && move.id == moveId)
                    {
                        return string.IsNullOrEmpty(move.display) ? moveId : move.display;
                    }
                }
            }
            return moveId;
        }

        /// <summary>The hour a trumpet names, on this device's clock. Digits, not words, so no locale key.</summary>
        static string HourOf(long epochMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime().ToString("HH:mm");
        }

        /// <summary>
        /// What this seat brings: whether a watch stood last night, whether the invitation from
        /// outside was accepted. Read from the same flags the solo contest reads, and declared to
        /// the server, which cannot see this save.
        /// </summary>
        static void Preparation(out bool watchPosted, out bool acceptedInvite)
        {
            GameState state = WorldRuntimeState();
            watchPosted = state != null && state.HasFlag(GameFlags.WatchPostedForDay(state.day - 1));
            acceptedInvite = state != null && state.HasFlag(GameFlags.AcceptedInvite);
        }

        void SetShown(string name, bool shown)
        {
            Transform child = _body.Find(name);
            if (child != null && child.gameObject.activeSelf != shown)
            {
                child.gameObject.SetActive(shown);
            }
        }

        /// <summary>
        /// The resident's display name, which is also the seat's. Never the raw id.
        ///
        /// Read from the same place every other screen reads it — the locale's npcs.json, merged
        /// into GameData — rather than from a ui.json key. The first version of this built a
        /// speaker key out of the seat id and handed it to Loc; no such key exists, because those
        /// belong to the two adversaries who speak in cutscenes and the six residents are named in
        /// npcs.json instead. Loc answered with a LogError per seat per refresh, which the e2e
        /// promotes to a run failure.
        ///
        /// Do not write that key into this comment to explain it, either: validate-content.mjs
        /// reads comments, finds a quoted key that resolves to nothing, and fails the build — which
        /// is how this sentence came to be phrased without one.
        /// </summary>
        static string SeatName(string seatId)
        {
            if (string.IsNullOrEmpty(seatId)) return string.Empty;

            if (GameData.Npcs != null)
            {
                foreach (NpcDef npc in GameData.Npcs)
                {
                    if (npc != null && npc.id == seatId)
                    {
                        return string.IsNullOrEmpty(npc.display) ? seatId : npc.display;
                    }
                }
            }

            return seatId;
        }

        /// <summary>
        /// The band this device declares.
        ///
        /// Hard-wired to minor while there is no profile to read it from, and that is the safe
        /// direction to be wrong in: a minor table refuses free text and refuses adults, so a
        /// mis-declared adult loses a feature and a mis-declared minor loses nothing. When the
        /// profile carries an age band, this reads it — and rule 17 still says the server decides
        /// what that band may do.
        /// </summary>
        static string Band() { return "minor"; }

        static string PlayerName()
        {
            GameState state = WorldRuntimeState();
            return state != null && !string.IsNullOrEmpty(state.playerName) ? state.playerName : "?";
        }

        static GameState WorldRuntimeState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }

        /// <summary>
        /// Empties a row and makes sure it lays out as a column — once.
        ///
        /// <see cref="UIKit.VerticalGroup"/> ADDS a layout group every time it is called, and these
        /// rows are redrawn on every refresh. The second refresh asked Unity for a second group on
        /// the same object, which it refuses with a null, and the null was dereferenced inside the
        /// helper: a NullReferenceException on every poll from the moment a trumpet appeared,
        /// found on the phone and not by any gate. Children are destroyed; the component stays.
        /// </summary>
        static void Column(RectTransform host, float spacing)
        {
            Clear(host);
            if (host.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() == null)
            {
                UIKit.VerticalGroup(host.gameObject, spacing, new RectOffset());
            }
        }

        static void Clear(RectTransform rect)
        {
            if (rect == null) return;
            for (int i = rect.childCount - 1; i >= 0; i--)
            {
                Destroy(rect.GetChild(i).gameObject);
            }
        }

        public void Close()
        {
            if (_polling != null)
            {
                StopCoroutine(_polling);
                _polling = null;
            }

            _current = null;
            ModalRoot.CloseId(ModalId);
        }
    }
}
