using System;
using System.Collections;
using System.Collections.Generic;
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
    /// </summary>
    public sealed class TablePanel : MonoBehaviour
    {
        const string ModalId = "table";

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

            UIKit.CreateRect("Seats", _body);
            UIKit.CreateRect("Feed", _body);

            UIKit.CreateButton(_body, "Trumpet", Loc.T("table.trumpet"),
                UIKit.ButtonVariant.Primary, () =>
                {
                    long inTwoHours = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
                    TableService.SoundTrumpet(_code, inTwoHours, 4, _ => Refresh());
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

                RenderSeats(snapshot);
                RenderFeed(snapshot);
            });
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

            Clear((RectTransform)host);
            UIKit.VerticalGroup(host.gameObject, DesignTokens.Space.S4, new RectOffset());

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

            Clear((RectTransform)host);
            UIKit.VerticalGroup(host.gameObject, DesignTokens.Space.S4, new RectOffset());

            int shown = 0;
            for (int i = snapshot.Events.Count - 1; i >= 0 && shown < 6; i--)
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
                default:
                    return null;
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
