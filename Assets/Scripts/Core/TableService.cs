using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SheepGate.Core
{
    /// <summary>
    /// The client half of the table (docs/multiplayer.md). Talks to tools/table-server.mjs and
    /// holds nothing the server is responsible for.
    ///
    /// <b>Configured the same way the study endpoint is, and off the same way.</b>
    ///     -table-url http://127.0.0.1:8788        on the command line, or
    ///     PlayerPrefs["sheepgate.table.url"]
    /// With no URL there is no table: the menu entry does not appear, nothing is requested, and the
    /// solo game is untouched. That is the property that lets this ship before a server exists
    /// anywhere — a build with no URL is exactly the build that shipped yesterday.
    ///
    /// <b>What this deliberately does not do.</b> It does not decide anything. Which seat you get,
    /// whether your band may join, whether a line is sayable — every one of those is answered by
    /// the server, and this class renders the answer. A Unity player is a file on somebody's
    /// machine; see the header of tools/table-server.mjs.
    /// </summary>
    public sealed class TableService : MonoBehaviour
    {
        public const string UrlPrefKey = "sheepgate.table.url";
        const string UrlArgument = "-table-url";

        /// <summary>Short: every call here is a person waiting with a screen open.</summary>
        const int TimeoutSeconds = 8;

        static TableService _instance;
        static string _url;
        static bool _urlResolved;

        // ------------------------------------------------------------------ wire types

        [Serializable]
        public sealed class Seat
        {
            [JsonProperty("seat")] public string Id;
            [JsonProperty("name")] public string Name;
            [JsonProperty("taken")] public bool Taken;
        }

        [Serializable]
        public sealed class Event
        {
            [JsonProperty("id")] public long Id;
            [JsonProperty("at")] public long At;
            [JsonProperty("seat")] public string Seat;
            [JsonProperty("kind")] public string Kind;
            [JsonProperty("lineKey")] public string LineKey;
            [JsonProperty("body")] public string Body;

            /// <summary>A held move carries only its turn: the server redacts the rest (rule 11).</summary>
            [JsonProperty("turn")] public int Turn;

            /// <summary>The rest of what happened, shaped per kind. Read, never trusted.</summary>
            [JsonProperty("payload")] public JObject Payload;
        }

        [Serializable]
        public sealed class RaidMove
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("open")] public bool Open;
        }

        [Serializable]
        public sealed class ResolvedTurn
        {
            [JsonProperty("turn")] public int Turn;
            [JsonProperty("moves")] public Dictionary<string, string> Moves;
            [JsonProperty("resolve")] public int Resolve;
            [JsonProperty("morale")] public int Morale;
        }

        /// <summary>
        /// The group mission as the server lets this player see it (docs/multiplayer.md §06): the
        /// meters, whose turn it is, whether this seat has answered — and never what anybody else
        /// answered until the turn has closed.
        /// </summary>
        [Serializable]
        public sealed class RaidState
        {
            [JsonProperty("open")] public bool Open;
            [JsonProperty("outcome")] public string Outcome;
            [JsonProperty("turn")] public int Turn;
            [JsonProperty("turnLimit")] public int TurnLimit;
            [JsonProperty("pageTurn")] public int PageTurn;
            [JsonProperty("pageVerse")] public string PageVerse;
            [JsonProperty("deadline")] public long Deadline;
            [JsonProperty("resolve")] public int Resolve;
            [JsonProperty("resolveMax")] public int ResolveMax;
            [JsonProperty("morale")] public int Morale;
            [JsonProperty("moraleMax")] public int MoraleMax;
            [JsonProperty("present")] public List<string> Present;
            [JsonProperty("youArePresent")] public bool YouArePresent;
            [JsonProperty("youCommitted")] public bool YouCommitted;
            [JsonProperty("committedCount")] public int CommittedCount;
            [JsonProperty("moves")] public List<RaidMove> Moves;
            [JsonProperty("resolvedTurns")] public List<ResolvedTurn> ResolvedTurns;
        }

        [Serializable]
        public sealed class TableInfo
        {
            [JsonProperty("code")] public string Code;
            [JsonProperty("band")] public string Band;
            [JsonProperty("freeText")] public bool FreeText;
        }

        [Serializable]
        public sealed class Snapshot
        {
            [JsonProperty("table")] public TableInfo Table;
            [JsonProperty("seats")] public List<Seat> Seats;
            [JsonProperty("events")] public List<Event> Events;
            [JsonProperty("raid")] public RaidState Raid;
            [JsonProperty("error")] public string Error;
            [JsonProperty("code")] public string Code;
            [JsonProperty("seat")] public string Seat;
            [JsonProperty("trumpetId")] public long TrumpetId;
        }

        // ------------------------------------------------------------------ identity

        /// <summary>
        /// Who this device is, to a server that has no accounts.
        ///
        /// Generated once and kept in PlayerPrefs beside the save. It proves nothing — a lost save
        /// is a lost identity and a shared device is a shared identity — and docs/multiplayer.md
        /// §03 designs around that rather than pretending otherwise.
        /// </summary>
        public static string PlayerId
        {
            get
            {
                const string key = "sheepgate.table.player";
                string existing = PlayerPrefs.GetString(key, string.Empty);
                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }

                string made = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(key, made);
                PlayerPrefs.Save();
                return made;
            }
        }

        /// <summary>Where the endpoint is, or empty when nobody configured one.</summary>
        public static string Url
        {
            get
            {
                if (_urlResolved)
                {
                    return _url;
                }

                _urlResolved = true;
                _url = ResolveUrl();
                return _url;
            }
        }

        /// <summary>Whether the table exists at all for this build. The menu asks this.</summary>
        public static bool Available { get { return !string.IsNullOrEmpty(Url); } }

        static string ResolveUrl()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == UrlArgument)
                    {
                        return args[i + 1];
                    }
                }
            }
            catch (Exception)
            {
                // iOS does not hand a player its command line. PlayerPrefs is the route there, and
                // that is not a fallback — it is the only door on a phone.
            }

            return PlayerPrefs.GetString(UrlPrefKey, string.Empty);
        }

        static TableService Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var host = new GameObject("TableService");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<TableService>();
                return _instance;
            }
        }

        // ------------------------------------------------------------------ operations

        public static void CreateTable(string band, string playerName, Action<Snapshot> done)
        {
            Post("/tables", new Dictionary<string, object>
            {
                { "band", band }, { "playerId", PlayerId }, { "playerName", playerName }
            }, done);
        }

        public static void Join(string code, string band, string playerName, Action<Snapshot> done)
        {
            Post("/join", new Dictionary<string, object>
            {
                { "code", code }, { "band", band }, { "playerId", PlayerId }, { "playerName", playerName }
            }, done);
        }

        public static void Say(string code, string lineKey, Action<Snapshot> done)
        {
            Post("/say", new Dictionary<string, object>
            {
                { "code", code }, { "playerId", PlayerId }, { "lineKey", lineKey }
            }, done);
        }

        /// <summary>
        /// Sounds the trumpet, and declares what this seat brings to the hour it names.
        ///
        /// The two preparation facts live in an offline save the server has never seen, so they
        /// are declared rather than looked up. They tune the fight and decide nothing about who may
        /// play it — see the header of the group-mission section in tools/table-server.mjs.
        /// </summary>
        public static void SoundTrumpet(string code, long atEpochMs, int seats, bool watchPosted,
                                        bool acceptedInvite, Action<Snapshot> done)
        {
            Post("/trumpet", new Dictionary<string, object>
            {
                { "code", code }, { "playerId", PlayerId }, { "atEpochMs", atEpochMs }, { "seats", seats },
                { "watchPosted", watchPosted }, { "acceptedInvite", acceptedInvite }
            }, done);
        }

        /// <summary>"Eu vou" or "Não consigo hoje". The last answer before the hour is the one that counts.</summary>
        public static void AnswerTrumpet(string code, long trumpetId, bool coming, bool watchPosted,
                                         bool acceptedInvite, Action<Snapshot> done)
        {
            Post("/answer", new Dictionary<string, object>
            {
                { "code", code }, { "playerId", PlayerId }, { "trumpetId", trumpetId }, { "coming", coming },
                { "watchPosted", watchPosted }, { "acceptedInvite", acceptedInvite }
            }, done);
        }

        /// <summary>
        /// A move, handed to the server and not applied here. Nothing changes on this screen until
        /// the turn closes and the server says what everybody did — that delay is rule 11, not lag.
        /// </summary>
        public static void CommitMove(string code, int turn, string moveId, Action<Snapshot> done)
        {
            Post("/commit", new Dictionary<string, object>
            {
                { "code", code }, { "playerId", PlayerId }, { "turn", turn }, { "move", moveId }
            }, done);
        }

        public static void Feed(string code, Action<Snapshot> done)
        {
            if (!Available)
            {
                done?.Invoke(null);
                return;
            }

            // The player id travels with the read so the answer can say whether THIS seat has
            // already answered the open turn — the one per-player fact the raid state carries.
            Instance.StartCoroutine(Instance.Get(
                "/feed?code=" + UnityWebRequest.EscapeURL(code) + "&playerId=" + UnityWebRequest.EscapeURL(PlayerId),
                done));
        }

        static void Post(string path, Dictionary<string, object> body, Action<Snapshot> done)
        {
            if (!Available)
            {
                done?.Invoke(null);
                return;
            }

            Instance.StartCoroutine(Instance.Send(path, body, done));
        }

        IEnumerator Send(string path, Dictionary<string, object> body, Action<Snapshot> done)
        {
            string json = JsonConvert.SerializeObject(body);

            using (var request = new UnityWebRequest(Url.TrimEnd('/') + path, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = TimeoutSeconds;

                yield return request.SendWebRequest();

                done?.Invoke(Parse(request));
            }
        }

        IEnumerator Get(string path, Action<Snapshot> done)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(Url.TrimEnd('/') + path))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                done?.Invoke(Parse(request));
            }
        }

        /// <summary>
        /// One answer, whatever happened.
        ///
        /// A refusal from the server is not a failure to report as one: "this table speaks in the
        /// game's own words" is the product working, and it arrives with a 403 body that is worth
        /// showing. So the body is parsed on any status that has one, and only a request that never
        /// got an answer becomes a null.
        /// </summary>
        static Snapshot Parse(UnityWebRequest request)
        {
            string text = request.downloadHandler != null ? request.downloadHandler.text : null;

            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning("[Table] " + request.url + " -> " + request.error);
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<Snapshot>(text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Table] could not read the answer: " + exception.Message);
                return null;
            }
        }
    }
}
