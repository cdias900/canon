using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace SheepGate.Core
{
    /// <summary>
    /// Asks the study endpoint what this player should read next, and hands the answer back.
    ///
    /// ==================================================================================
    /// WHERE THE MODEL IS, AND WHY IT IS NOT HERE
    /// ==================================================================================
    /// Rule 16: no AI key reaches the client. This class knows a URL and nothing else — no key, no
    /// prompt, no model name. Everything that could be read out of a shipped player is on the other
    /// side of that URL, in <c>tools/study-server.mjs</c>.
    ///
    /// ==================================================================================
    /// OFF BY DEFAULT, AND THAT IS THE DESIGN
    /// ==================================================================================
    /// <c>MVP-SCOPE.md</c> puts LLM calls at runtime out of scope, and the game is offline by
    /// design. So this does nothing at all unless somebody configures a URL — with no URL there is
    /// no request, no timeout and no waiting, and the profile shows its own authored table exactly
    /// as it did before. Turning it on is a deliberate act:
    ///
    ///     -study-url http://127.0.0.1:8787      on the command line, or
    ///     PlayerPrefs "sheepgate.studies.url"   set once on a device
    ///
    /// ==================================================================================
    /// THE ANSWER IS NEVER WAITED FOR
    /// ==================================================================================
    /// The screen draws the offline suggestions immediately and swaps in the model's when they
    /// arrive. A player on a slow connection sees a complete screen that gets better, never a
    /// spinner — and a player whose server is down never learns that one exists.
    /// </summary>
    public sealed class StudyService : MonoBehaviour
    {
        public const string UrlPrefKey = "sheepgate.studies.url";
        const string UrlArgument = "-study-url";

        /// <summary>Long enough for a model to think, short enough that nobody waits on it.</summary>
        const int TimeoutSeconds = 20;

        static StudyService _instance;
        static string _url;
        static bool _urlResolved;

        /// <summary>One suggested study, as it comes back over the wire.</summary>
        [Serializable]
        public sealed class RemoteStudy
        {
            [JsonProperty("title")] public string Title;
            [JsonProperty("line")] public string Line;
            [JsonProperty("reference")] public string Reference;
        }

        sealed class Answer
        {
            [JsonProperty("studies")] public List<RemoteStudy> Studies;
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

        /// <summary>True when a request is worth making at all.</summary>
        public static bool IsConfigured
        {
            get { return !string.IsNullOrEmpty(Url); }
        }

        /// <summary>
        /// Asks for suggestions. <paramref name="onAnswer"/> runs only on success and only with at
        /// least one study; every failure is silent by design, because the caller already has a
        /// complete screen and a suggestion nobody asked for is not worth an error message.
        /// </summary>
        public static void Request(GameState state, Action<IReadOnlyList<RemoteStudy>> onAnswer)
        {
            if (!IsConfigured || state == null || onAnswer == null)
            {
                return;
            }

            Instance.StartCoroutine(Instance.Fetch(state, onAnswer));
        }

        static StudyService Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var host = new GameObject("StudyService");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<StudyService>();
                return _instance;
            }
        }

        IEnumerator Fetch(GameState state, Action<IReadOnlyList<RemoteStudy>> onAnswer)
        {
            string body = JsonConvert.SerializeObject(Signals(state));

            using (var request = new UnityWebRequest(Url.TrimEnd('/') + "/studies", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = TimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[Studies] " + request.error + " — keeping the authored list.");
                    yield break;
                }

                List<RemoteStudy> studies = null;
                try
                {
                    Answer answer = JsonConvert.DeserializeObject<Answer>(request.downloadHandler.text);
                    studies = answer != null ? answer.Studies : null;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Studies] unreadable answer: " + exception.Message);
                    yield break;
                }

                // A study with no reference has nothing to open, and one with no words has nothing
                // to read. Dropping them here means no screen has to think about half-answers.
                var usable = new List<RemoteStudy>();
                for (int i = 0; studies != null && i < studies.Count; i++)
                {
                    RemoteStudy study = studies[i];
                    if (study != null && !string.IsNullOrEmpty(study.Reference) &&
                        !string.IsNullOrEmpty(study.Title) && !string.IsNullOrEmpty(study.Line))
                    {
                        usable.Add(study);
                    }
                }

                if (usable.Count == 0)
                {
                    yield break;
                }

                onAnswer(usable);
            }
        }

        /// <summary>
        /// What the endpoint is told: what the player has done, and nothing about who they are.
        ///
        /// No name, no id, no device, no save. The whole point of the profile is that a record of
        /// somebody's choices is a moral dossier (rule 15), and the six numbers below are the least
        /// that can answer "what should this person read next".
        /// </summary>
        static Dictionary<string, object> Signals(GameState state)
        {
            StudyDesk.Signals signals = StudyDesk.Read(state);

            return new Dictionary<string, object>
            {
                { "conversations", signals.Conversations },
                { "wallStages", signals.WallStages },
                { "read", signals.Read },
                { "wentDownTheValley", signals.WentDownTheValley },
                { "stoodTheTrial", signals.StoodTheTrial },
                { "day", state.day }
            };
        }

        static string ResolveUrl()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], UrlArgument, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i + 1];
                    }
                }
            }
            catch (Exception)
            {
                // A platform that will not hand over its arguments simply has none to give.
            }

            return PlayerPrefs.GetString(UrlPrefKey, string.Empty);
        }
    }
}
