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
    /// Music and effects are two switches, not one, because they are two complaints. Somebody who
    /// wants the village audible but the theme off is not asking for silence, and a single switch
    /// makes them choose between the sound they like and the sound they are tired of. Effects owns
    /// the one-shots and the ambient beds — wind is a sound the world makes, not a soundtrack.
    ///
    /// Both live in PlayerPrefs beside the language, for the same reason: they have to survive a
    /// deleted run, because wiping a save is not a request to be shouted at on the next launch.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        /// <summary>
        /// The single switch this settings screen used to have. Nothing writes it any more: it is
        /// read as the <b>default</b> of the two that replaced it, so a player who had turned the
        /// game silent stays silent on the launch that splits the setting in two, without a
        /// migration step that has to run at exactly the right moment to work.
        /// </summary>
        public const string MutedPrefKey = "sheepgate.audio.muted";

        public const string MusicMutedPrefKey = "sheepgate.audio.music.muted";
        public const string EffectsMutedPrefKey = "sheepgate.audio.effects.muted";

        const float BedVolume = 0.55f;

        /// <summary>
        /// Under the beds on purpose. The theme is the thing you stop hearing after a minute; a
        /// footstep and a block going down are the ones carrying information about what just
        /// happened, and they have to survive it.
        /// </summary>
        const float MusicVolume = 0.34f;

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
                AudioListener.volume = value ? 0f : 1f;
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
        AudioSource _music;
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

        /// <summary>The theme. Off does not stop the world making noise.</summary>
        public static bool MusicMuted
        {
            get { return PlayerPrefs.GetInt(MusicMutedPrefKey, LegacyMuted) != 0; }
            set
            {
                PlayerPrefs.SetInt(MusicMutedPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>One-shots and the ambient beds: everything the world itself makes.</summary>
        public static bool EffectsMuted
        {
            get { return PlayerPrefs.GetInt(EffectsMutedPrefKey, LegacyMuted) != 0; }
            set
            {
                PlayerPrefs.SetInt(EffectsMutedPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        static int LegacyMuted
        {
            get { return PlayerPrefs.GetInt(MutedPrefKey, 0); }
        }

        /// <summary>Plays a one-shot. Safe to call before anything is set up, and safe to spam.</summary>
        public static void Play(string key)
        {
            if (Suppressed || EffectsMuted)
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
            AudioListener.volume = Suppressed ? 0f : 1f;

            _effects = gameObject.AddComponent<AudioSource>();
            _effects.playOnAwake = false;
            _effects.spatialBlend = 0f;

            _day = CreateBed(AudioKeys.AmbienceDay);
            _night = CreateBed(AudioKeys.AmbienceNight);
            _music = CreateBed(AudioKeys.MusicVillage);
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

        /// <summary>
        /// The whole mix, every frame. Each channel walks towards where it should be rather than
        /// jumping there, so a switch flipped in the settings fades out under the player's thumb
        /// instead of cutting — and so does the day handing over to the night.
        /// </summary>
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
            float world = EffectsMuted ? 0f : 1f;

            _night.volume = Mathf.MoveTowards(_night.volume, night * BedVolume * world, step);
            _day.volume = Mathf.MoveTowards(_day.volume, (1f - night) * BedVolume * world, step);

            if (_music != null)
            {
                _music.volume = Mathf.MoveTowards(_music.volume, MusicMuted ? 0f : MusicVolume, step);
            }
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
