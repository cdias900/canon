using System;
using UnityEngine;
using SheepGate.Core;
using SheepGate.UI;

namespace SheepGate.World
{
    /// <summary>
    /// What a pile lying in the village hands over when it is picked up.
    ///
    /// One class serves both because a pile is a pile: the only differences are the material it
    /// credits, how much of it, the label, the sprite, and the first day it exists at all.
    /// </summary>
    public enum PileMaterial
    {
        /// <summary>Burnt stone from the ruins. Lying about from the first morning.</summary>
        Stone = 0,

        /// <summary>Beams brought in for the work. Not in the village on day one; see <see cref="RubblePile.TimberFirstDay"/>.</summary>
        Timber = 1
    }

    /// <summary>
    /// A pile of material lying in the village. Picking one up turns it into what the wall is
    /// built from: stone and timber become blocks, blocks become wall.
    ///
    /// A pile taken today comes back tomorrow: the ruins keep producing stone, and a run can never
    /// dead-end for want of material. What was already built is never touched.
    ///
    /// <b>The per-day collected flag is save state.</b> A stone pile writes
    /// <c>rubble_taken_&lt;index&gt;</c>, exactly the key this class has always written, over the
    /// same index space (the order of the <c>rubble</c> array in map.json). It is preserved rather
    /// than migrated: renaming it would make every pile a mid-run save had already emptied look
    /// full again, which is the wrong side of "you can lose tomorrow, never yesterday". Timber gets
    /// its own key space, <c>timber_taken_&lt;index&gt;</c>, so the two never collide on an index.
    ///
    /// <b>Timber does not appear on day one.</b> The pacing is deliberate — the loop grows with the
    /// player instead of arriving whole on the first screen — but it is implemented as availability
    /// rather than as a spawn-time test, because <c>GameScene.Compose</c> runs once per launch and
    /// <see cref="DayCycle"/> advances the day in place. A pile spawned only on the day it is due
    /// would never show up for a player who crosses midnight without relaunching. So every pile is
    /// built at compose time and a timber pile is simply inert — invisible, no collider, its cell
    /// left walkable — until its day arrives; <see cref="Refresh"/> is already wired to
    /// <see cref="DayCycle.MorningStarted"/>, so it appears with the morning that owns it.
    /// </summary>
    public sealed class RubblePile : InteractableBase
    {
        /// <summary>
        /// Stone in one pile. Kept under its original name and value because it is public and this
        /// change may not break a caller; <see cref="DefaultStoneAmount"/> is the name to read.
        /// </summary>
        public const int DefaultAmount = 3;

        /// <summary>
        /// Stone in one pile: exactly one block's worth, at <c>ResourceSystem.StonePerBlock</c>.
        /// </summary>
        public const int DefaultStoneAmount = DefaultAmount;

        /// <summary>
        /// Timber in one pile: one block's worth, at <c>ResourceSystem.TimberPerBlock</c>. Stone is
        /// the plentiful half of the recipe and timber the scarce one — the ruins are full of stone
        /// and the beams had to be asked for — so the village carries fewer timber piles than stone
        /// ones, not smaller ones.
        /// </summary>
        public const int DefaultTimberAmount = 2;

        /// <summary>First day stone lies in the village: from the opening.</summary>
        public const int StoneFirstDay = 1;

        /// <summary>
        /// First day timber lies in the village. Day one is stone only, on purpose: the recipe is
        /// introduced one half at a time.
        /// </summary>
        public const int TimberFirstDay = 2;

        // Locale keys for the two ways a tap on a pile does nothing. The "...Key" suffix is what
        // tells the content validator these are keys rather than sentences.
        private const string NotYetKey = "toast.pile.not_yet";
        private const string TakenTodayKey = "toast.pile.taken_today";

        /// <summary>
        /// Unchanged, and it must stay unchanged: it is the prefix of a save key. See the class
        /// note for why renaming it would refill piles a run had already emptied.
        /// </summary>
        private const string StoneTakenPrefix = "rubble_taken_";

        /// <summary>Timber's own key space, so a stone pile and a timber pile can share an index.</summary>
        private const string TimberTakenPrefix = "timber_taken_";

        /// <summary>
        /// There is no timber prop in the art library — see <c>SheepGate.Art.ArtKeys</c>, which has
        /// prop_rubble, prop_well and prop_mat and nothing else. The stone pile is the closest
        /// shape to a stack of cut beams, so it is reused under a wood tint until a prop_timber
        /// exists; the mat was rejected because it already means "the place you sleep".
        /// </summary>
        private static readonly Color TimberTint = new Color(1f, 0.70f, 0.40f, 1f);

        public int Index { get; private set; }
        public int Amount { get; private set; }

        /// <summary>
        /// Which material this pile hands over. Named Kind and not Material so that nothing in
        /// this class ever has to disambiguate the member from UnityEngine.Material.
        /// </summary>
        public PileMaterial Kind { get; private set; }

        /// <summary>First day this pile exists in the village. Before it, the pile is inert.</summary>
        public int FirstDay { get; private set; }

        private DayCycle _dayCycle;
        private SpriteRenderer _renderer;

        /// <summary>
        /// Original four-argument spawn, kept so no existing caller has to change. A pile spawned
        /// without a stated material is stone, which is what every caller meant before timber
        /// existed — same index space, same save key, same amount.
        /// </summary>
        public static RubblePile Spawn(int index, Vector2Int cell, Transform parent, TilemapBuilder tilemap)
        {
            return Spawn(PileMaterial.Stone, index, cell, parent, tilemap);
        }

        /// <summary>Spawns a pile of a given material at a cell.</summary>
        public static RubblePile Spawn(PileMaterial material, int index, Vector2Int cell, Transform parent, TilemapBuilder tilemap)
        {
            GameObject pileObject = new GameObject(ObjectName(material) + "_" + index);
            if (parent != null)
            {
                pileObject.transform.SetParent(parent, false);
            }

            RubblePile pile = pileObject.AddComponent<RubblePile>();
            pile.Index = index;
            pile.Kind = material;
            pile.Amount = DefaultAmountFor(material);
            pile.FirstDay = FirstDayFor(material);
            pile.DisplayName = Loc.T(DisplayKey(material));
            pile.Place(tilemap, cell, true, 0f);
            pile.BuildSprite(tilemap != null ? tilemap.Height : 0, cell.y);
            pile.Refresh();
            return pile;
        }

        /// <summary>GameObject name, which is the handle the end-to-end tests reach for.</summary>
        private static string ObjectName(PileMaterial material)
        {
            return material == PileMaterial.Timber ? "TimberPile" : "RubblePile";
        }

        private static string DisplayKey(PileMaterial material)
        {
            return material == PileMaterial.Timber ? "world.timber" : "world.rubble";
        }

        private static int DefaultAmountFor(PileMaterial material)
        {
            return material == PileMaterial.Timber ? DefaultTimberAmount : DefaultStoneAmount;
        }

        private static int FirstDayFor(PileMaterial material)
        {
            return material == PileMaterial.Timber ? TimberFirstDay : StoneFirstDay;
        }

        private static string TakenPrefixFor(PileMaterial material)
        {
            return material == PileMaterial.Timber ? TimberTakenPrefix : StoneTakenPrefix;
        }

        /// <summary>Save key holding the day this pile was last emptied.</summary>
        private string TakenKey
        {
            get { return TakenPrefixFor(Kind) + Index; }
        }

        private void BuildSprite(int mapHeight, int cellY)
        {
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(transform, false);
            _renderer = visual.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = WorldRuntime.SortingOrderForCell(mapHeight, cellY);

            Sprite sprite = Kind == PileMaterial.Timber
                ? WorldRuntime.GetTintedSprite("prop_rubble", TimberTint)
                : WorldRuntime.GetSprite("prop_rubble");

            if (sprite == null)
            {
                Color fallback = Kind == PileMaterial.Timber
                    ? new Color(0.55f, 0.38f, 0.21f, 1f)
                    : new Color(0.44f, 0.39f, 0.33f, 1f);
                sprite = WorldRuntime.SolidSprite(fallback);
            }

            _renderer.sprite = sprite;
        }

        private void Start()
        {
            _dayCycle = FindDayCycle();
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted += OnMorningStarted;
            }

            Refresh();
        }

        protected override void OnDestroy()
        {
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted -= OnMorningStarted;
                _dayCycle = null;
            }

            base.OnDestroy();
        }

        private static DayCycle FindDayCycle()
        {
            DayCycle cycle = null;
            try
            {
                ServiceLocator.TryGet(out cycle);
            }
            catch (Exception)
            {
                cycle = null;
            }

            if (cycle == null)
            {
                cycle = FindFirstObjectByType<DayCycle>();
            }

            return cycle;
        }

        private void OnMorningStarted(int day)
        {
            Refresh();
        }

        public override bool IsAvailable
        {
            get { return isActiveAndEnabled && HasArrived() && !TakenToday(); }
        }

        /// <summary>
        /// Whether this pile's day has come. Stone's has always come; timber's comes on
        /// <see cref="TimberFirstDay"/>. Never goes backwards: a pile that has arrived stays.
        /// </summary>
        private bool HasArrived()
        {
            if (FirstDay <= StoneFirstDay)
            {
                return true;
            }

            GameState state = WorldRuntime.State;
            int day = state != null ? state.day : StoneFirstDay;
            return day >= FirstDay;
        }

        private bool TakenToday()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return false;
            }

            return state.Counter(TakenKey) == state.day;
        }

        /// <summary>Shows or hides the pile for the current day and keeps the grid in sync.</summary>
        public void Refresh()
        {
            bool available = HasArrived() && !TakenToday();

            if (_renderer != null)
            {
                _renderer.enabled = available;
            }

            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = available;
            }

            SetCellBlocking(available);
        }

        public override void Interact()
        {
            // Guarded here as well as in IsAvailable because the walk-adjacent-first contract lets
            // a caller path towards a pile and then call Interact on arrival, by which time the day
            // or the pile may have moved on.
            if (!HasArrived())
            {
                Toast.Show(Loc.T(NotYetKey));
                Debug.Log("[World] " + Kind + " pile " + Index + " is not in the village yet; it arrives on day " + FirstDay + ".");
                return;
            }

            if (TakenToday())
            {
                // The pile is hidden the moment it is taken, so this is the rarer of the two: the
                // player pathed towards a pile that emptied while they were walking. Saying so
                // beats a walk that ends in nothing.
                Toast.Show(Loc.T(TakenTodayKey));
                Debug.Log("[World] " + Kind + " pile " + Index + " was already collected today.");
                return;
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources == null)
            {
                Debug.LogWarning("[World] No ResourceSystem in the scene; the " + Kind + " could not be collected.");
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            if (Kind == PileMaterial.Timber)
            {
                resources.AddTimber(Amount);
            }
            else
            {
                resources.AddStone(Amount);
            }

            state.counters[TakenKey] = state.day;
            Refresh();
            WorldRuntime.SaveNow();
        }
    }
}
