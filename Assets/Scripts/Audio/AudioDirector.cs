using System;
using UnityEngine;
using SheepGate.Core;
using SheepGate.World;

namespace SheepGate.Audio
{
    /// <summary>
    /// The one thing that makes noise. Everything else asks it to.
    ///
    /// Two beds crossfade against the daylight rather than against a clock, so the sound follows
    /// the same number the light does: <see cref="DayCycle.NightAmount"/> is the single source of
    /// truth for how late it is, and the night bed simply is that number. Nothing here runs on a
    /// timer of its own, which keeps rule 20 intact — a player reading a chapter is not being
    /// walked toward nightfall by the soundtrack.
    ///
    /// Muting is a player setting and lives in PlayerPrefs beside the language, for the same
    /// reason: it has to survive a deleted run, because wiping a save is not a request to be
    /// shouted at on the next launch.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public const string MutedPrefKey = "sheepgate.audio.muted";

        const float BedVolume = 0.55f;
        const float CrossfadeSeconds = 1.5f;

        static AudioDirector _instance;

        static bool _suppressed = DetectAutomatedRun();

        /// <summary>
        /// True while nothing may make a sound. Defaults to whatever the command line says rather
        /// than waiting to be told: an automated run launches a real player on somebody's machine,
        /// and a suite that plays three days of footsteps out loud is a suite nobody runs twice.
        /// </summary>
        public static bool Suppressed
        {
            get { return _suppressed; }
            set
            {
                _suppressed = value;
                AudioListener.volume = value || Muted ? 0f : 1f;
            }
        }

        static bool DetectAutomatedRun()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "-e2e", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // A platform that will not hand over its arguments is not an automated run.
            }

            return Application.isBatchMode;
        }

        AudioSource _effects;
        AudioSource _day;
        AudioSource _night;
        DayCycle _cycle;
        float _nextBindAttempt;

        public static AudioDirector Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var host = new GameObject("AudioDirector");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<AudioDirector>();
                return _instance;
            }
        }

        /// <summary>Brings the director up without asking it for a sound. Safe to call twice.</summary>
        public static void Ensure()
        {
            AudioDirector unused = Instance;
        }

        public static bool Muted
        {
            get { return PlayerPrefs.GetInt(MutedPrefKey, 0) != 0; }
            set
            {
                PlayerPrefs.SetInt(MutedPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                AudioListener.volume = value || Suppressed ? 0f : 1f;
            }
        }

        /// <summary>Plays a one-shot. Safe to call before anything is set up, and safe to spam.</summary>
        public static void Play(string key)
        {
            if (Suppressed)
            {
                return;
            }

            AudioDirector director = Instance;
            if (director == null || director._effects == null)
            {
                return;
            }

            AudioClip clip = AudioLibrary.Get(key);
            if (clip != null)
            {
                director._effects.PlayOneShot(clip);
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            AudioListener.volume = Muted || Suppressed ? 0f : 1f;

            _effects = gameObject.AddComponent<AudioSource>();
            _effects.playOnAwake = false;
            _effects.spatialBlend = 0f;

            _day = CreateBed(AudioKeys.AmbienceDay);
            _night = CreateBed(AudioKeys.AmbienceNight);
        }

        AudioSource CreateBed(string key)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = AudioLibrary.Get(key);
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;

            if (source.clip != null && !Suppressed)
            {
                source.Play();
            }

            return source;
        }

        void Update()
        {
            if (_day == null || _night == null)
            {
                return;
            }

            BindCycle();

            // No cycle yet — creation, the opening — is daylight as far as the ear is concerned.
            float night = _cycle != null ? Mathf.Clamp01(_cycle.NightAmount) : 0f;
            float step = Time.unscaledDeltaTime / CrossfadeSeconds;

            _night.volume = Mathf.MoveTowards(_night.volume, night * BedVolume, step);
            _day.volume = Mathf.MoveTowards(_day.volume, (1f - night) * BedVolume, step);
        }

        void BindCycle()
        {
            if (_cycle != null || Time.unscaledTime < _nextBindAttempt)
            {
                return;
            }

            _nextBindAttempt = Time.unscaledTime + 0.5f;

            try
            {
                DayCycle found;
                ServiceLocator.TryGet(out found);
                _cycle = found;
            }
            catch (Exception)
            {
                _cycle = null;
            }
        }
    }
}
