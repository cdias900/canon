using System;
using System.Collections;
using SheepGate.Core;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// What the player sees when the wall grows.
    ///
    /// The wall stands on the north row of the map, fourteen cells from where the village lives,
    /// and the close view is fifteen cells tall: for most of a day the one thing the season is
    /// about is off screen. <see cref="WallSystem"/> raised <c>SegmentCompleted</c> for a whole
    /// season with nobody listening. This is the listener, and it does three things, all of them
    /// inside the design system's rule that the reward is the world changing and never confetti:
    ///
    ///   - every course says so, in a toast, with its number;
    ///   - a finished segment is looked at — the camera goes to it for a moment and comes back —
    ///     unless the player asked for reduced motion, in which case the toast is the whole beat;
    ///   - the world's stage follows the wall (design system rule 11): the ruin lying outside the
    ///     city thins with every course, through <see cref="TilemapBuilder.ApplyWallProgress"/>.
    ///
    /// Nothing here runs while the night is resolving or a panel is up. The night crew lays its
    /// courses in the dark and the morning report is where those are read; a toast over the split
    /// would be noise, and a camera move under a modal would be a bug.
    /// </summary>
    public sealed class WallBeats : MonoBehaviour
    {
        /// <summary>How long the camera rests on a finished segment before coming back.</summary>
        public const float LookSeconds = 1.6f;

        /// <summary>How long the camera takes to get there and back.</summary>
        public const float TravelSeconds = 0.7f;

        private WallSystem _wall;
        private DayCycle _cycle;
        private TilemapBuilder _tilemap;
        private CameraRig _rig;
        private Coroutine _look;

        public void Configure(WallSystem wall, DayCycle cycle, TilemapBuilder tilemap, CameraRig rig)
        {
            _wall = wall;
            _cycle = cycle;
            _tilemap = tilemap;
            _rig = rig;

            if (_wall != null)
            {
                _wall.SegmentStageChanged += OnStageChanged;
                _wall.SegmentCompleted += OnSegmentCompleted;
            }

            if (_cycle != null)
            {
                _cycle.MorningStarted += OnMorningStarted;
            }

            ApplyWorldStage();
        }

        private void OnDestroy()
        {
            if (_wall != null)
            {
                _wall.SegmentStageChanged -= OnStageChanged;
                _wall.SegmentCompleted -= OnSegmentCompleted;
                _wall = null;
            }

            if (_cycle != null)
            {
                _cycle.MorningStarted -= OnMorningStarted;
                _cycle = null;
            }
        }

        private void OnMorningStarted(int day)
        {
            ApplyWorldStage();
        }

        private void OnStageChanged(string id, int stage)
        {
            ApplyWorldStage();

            if (stage >= WallSystem.StagesPerSegment || !PlayerIsWatching())
            {
                return;
            }

            Toast.Show(Loc.T("toast.wall.course", stage, WallSystem.StagesPerSegment));
        }

        private void OnSegmentCompleted(string id)
        {
            ApplyWorldStage();

            if (!PlayerIsWatching())
            {
                return;
            }

            bool yours = _wall != null && _wall.IsExposed(id);
            Toast.Show(Loc.T(yours ? "toast.wall.segment_done.yours" : "toast.wall.segment_done"));

            Vector3 where;
            if (_look == null && !DesignTokens.Motion.ReduceMotion && _rig != null && GameScene.Player != null
                && _wall != null && _wall.TryGetWorldPosition(id, out where))
            {
                _look = StartCoroutine(LookAt(where));
            }
        }

        /// <summary>
        /// True when a beat on the wall would land on a player who is looking at the village: no
        /// night resolving, no panel up, no cutscene or conversation holding the controls.
        /// </summary>
        private bool PlayerIsWatching()
        {
            if (_cycle != null && _cycle.IsResolving)
            {
                return false;
            }

            return !ModalRoot.IsOpen && !InputLock.IsLocked;
        }

        private IEnumerator LookAt(Vector3 where)
        {
            try
            {
                _rig.FrameCutscene(where, CameraRig.CloseSize, TravelSeconds);
                yield return new WaitForSeconds(TravelSeconds + LookSeconds);
            }
            finally
            {
                if (_rig != null && GameScene.Player != null)
                {
                    _rig.SetTarget(GameScene.Player.transform);
                }

                _look = null;
            }
        }

        private void ApplyWorldStage()
        {
            if (_tilemap == null || _wall == null)
            {
                return;
            }

            try
            {
                _tilemap.ApplyWallProgress(_wall.Fraction);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Repainting the ruin for the wall's progress failed: " + exception.Message);
            }
        }
    }
}
