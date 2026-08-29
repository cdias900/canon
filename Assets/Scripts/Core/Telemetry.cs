using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Where events go. The whole point of the interface is that a real backend can replace the
    /// local file later without a single call site changing.
    /// </summary>
    public interface ITelemetrySink
    {
        void Track(string eventName, IDictionary<string, object> props);
        void Flush();
    }

    /// <summary>
    /// Append-only JSON Lines sink: one compact object per line, written and closed immediately so
    /// a crash keeps every line already recorded. Nothing is ever buffered between calls.
    /// </summary>
    public sealed class JsonlFileSink : ITelemetrySink
    {
        // Monotonic within the process: unaffected by the device clock being changed or by time zones.
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        static readonly Dictionary<string, object> EmptyProps = new Dictionary<string, object>();

        readonly string _path;

        public JsonlFileSink(string path)
        {
            _path = path;

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Telemetry] Could not prepare the telemetry directory: " + exception.Message);
            }
        }

        public void Track(string eventName, IDictionary<string, object> props)
        {
            if (string.IsNullOrEmpty(_path) || string.IsNullOrEmpty(eventName))
            {
                return;
            }

            var line = BuildLine(eventName, props);
            if (line == null)
            {
                return;
            }

            try
            {
                File.AppendAllText(_path, line + "\n", Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Telemetry] Could not append an event to " + _path + ": " + exception.Message);
            }
        }

        /// <summary>No-op by design: every line is already on disk when Track returns.</summary>
        public void Flush()
        {
        }

        static string BuildLine(string eventName, IDictionary<string, object> props)
        {
            var record = new Dictionary<string, object>(4)
            {
                { "event", eventName },
                { "t_ms", Clock.ElapsedMilliseconds },
                { "ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "props", props ?? (IDictionary<string, object>)EmptyProps }
            };

            try
            {
                return JsonConvert.SerializeObject(record, Settings);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Telemetry] Could not serialize the props of '" + eventName + "': " + exception.Message);
            }

            // Losing the props is acceptable; losing the event is not.
            record["props"] = EmptyProps;
            try
            {
                return JsonConvert.SerializeObject(record, Settings);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Telemetry] Dropped the event '" + eventName + "': " + exception.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// Static front door for event tracking. Never throws into game code: a telemetry failure must
    /// not be able to break a session.
    /// </summary>
    public static class Telemetry
    {
        static ITelemetrySink _sink;

        public static void Initialize(ITelemetrySink sink)
        {
            _sink = sink;
        }

        public static void Track(string eventName, IDictionary<string, object> props = null)
        {
            if (_sink == null || string.IsNullOrEmpty(eventName))
            {
                return;
            }

            try
            {
                _sink.Track(eventName, props);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Telemetry] Sink failed while tracking '" + eventName + "': " + exception.Message);
            }
        }

        public static void Flush()
        {
            if (_sink == null)
            {
                return;
            }

            try
            {
                _sink.Flush();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Telemetry] Sink failed while flushing: " + exception.Message);
            }
        }
    }

    /// <summary>Every event name the POC emits. DeepRead is the metric the POC exists to measure.</summary>
    public static class TelemetryEvents
    {
        public const string SessionStart = "session_start";
        public const string VerseShown = "verse_shown";
        public const string ChapterOpened = "chapter_opened";
        public const string DeepRead = "deep_read";
        public const string RevealShown = "reveal_shown";
        public const string NodeCompleted = "node_completed";
        public const string VocationRevealed = "vocation_revealed";
    }
}
