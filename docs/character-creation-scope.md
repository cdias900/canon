# Character creation — scope

Align the creation screen with the catalogue, so that the items which appear in the backpack also
appear in the first wardrobe, given the same treatment. Decided by Pedro; this file is the record.

## Why now

This is not polish. **There is a contradiction in the game as it ships today.** Creation offers all
four hair variants freely; the catalogue says otherwise:

| art variant | what the catalogue says |
|---|---|
| `hair` 0 | `hair_short_crop` free · `hair_shaved_band` **locked** (`wall_stage` 1) |
| `hair` 1 | `hair_side_braid` free · `hair_work_bun` **locked** (`wall_stage` 3) |
| `hair` 2 | `hair_loose_waves` **locked** (`discovery: intro_seen`) |
| `hair` 3 | `hair_headscarf` **locked** (`day_reached` 2) |

In other words: in minute one the player picks a look that the backpack later tells them they have
not unlocked. The same goes for accessories. The two screens speak different vocabularies — creation
speaks in art indices (`appearance.hair = 2`), the backpack speaks in catalogue items
(`hair_loose_waves`) — and nobody ever introduced them.

The `Direção de Personagens` document already specifies the target: **`2 available at the start`** for
Hair, Outfit and Accessories, with all six items of each slot listed and the four locked ones showing
their condition. The declared loop is `Start → Achievement → Unlock → Personalisation → New
achievement`, closed by **"the next item is already in view"**. The document's names map one to one
onto ids that already exist in `character_catalog.json` — "Curto denso" is `hair_short_crop`, "Rolo de
corda" is `acc_rope_coil`. The catalogue was written for that spec. Only the creation screen never
knew.

## What was decided

1. **Creation becomes: choose Adar or Neriah, then personalise.** Pre-defined characters, not six
   sliders. The BASE (`build + face`) belongs to the character and is not a customisation slot.

   **1b. Amendment: skin tone remains the player's choice.** The character supplies the build, the
   face, the silhouette and the personality; skin tone stays with whoever is playing. Two fixed
   characters would mean two fixed tones, and in a game for 13-19 year olds in Brazil that narrows who
   can see themselves on screen — a cost decision 1 never intended to charge.

   The mechanical consequence, and the reason the amendment has to land early: `AppearanceState`
   **packs** build and tone into a single index (`body = build * SkinTones + tone`, with `BodyVariants`
   8 = 2 builds × 4 tones). Therefore **a `base` item declares the build only** — never a tone, never
   by implication. If the character's floor overwrote the tone, the player's choice would vanish on
   every recomposition.

2. **Two options per slot at the start.** Less choice in minute one, more across the season.
3. **The five missing ids get written** — the catalogue comes to resolve everything the presets cite.
4. **A locked item is visible from the start, visibly locked.** That is how the player understands
   there is more to come. A wardrobe that shows only what you already have promises nothing.

## What changes

**Data.** Write the five items the presets cite and the catalogue lacks:

| id | slot | why |
|---|---|---|
| `base_adar` | `base` | Adar's floor — **build 0, no tone** |
| `base_neriah` | `base` | Neriah's floor — **build 1, no tone** |
| `hair_tied_back` | `hair` | Neriah's default hair |
| `outfit_carrier_tunic` | `outfit` | Adar's default outfit |
| `outfit_surveyor_wrap` | `outfit` | Neriah's default outfit |

A `base` slot item **never appears in any wardrobe**. It exists only as the character's floor. The
backpack already ignores `CharacterSlot.Base`, and from here that becomes correct by design rather
than by accident.

**State.** `GameState` stores the chosen character, and creation seeds `equippedItems` from the
preset's `default_items`. That alone closes the three known backpack gaps:

- the backpack shows equipped rings from the first frame, instead of none;
- `Wardrobe.CharacterId` gets an answer, so the silhouette guard switches on;
- unequipping an accessory falls back to the preset's floor instead of leaving the art on the body.

**Screen.** The item row is the same component as the backpack's — art, name, description, and the
condition written out in full when locked. That is what "shown in a similar way" means.

## The real cost: art

Writing JSON does not solve it. **The catalogue promises more pieces than the art knows how to
draw.**

| slot | items | distinct visuals today | needs |
|---|---|---|---|
| hair | 6 (+1 new) | **4** | 7 → **+3 shapes** |
| accessory | 6 | **4** | 6 → **+2 shapes** |
| outfit | 6 (+2 new) | 6 | 8 → **0** (new `top`/`legs` combinations, 6 of 16 in use) |
| base | — | — | 0 (`body` already has 8 variants, `skin` 4) |

Today `hair_shaved_band` draws exactly `hair_short_crop`, and `acc_old_seal` draws exactly
`acc_ring_belt`. In a wide row with a name and a description that passes; in a wardrobe side by side,
two identical pieces with different names read as a bug.

The art is procedural (`CharacterArt`), so these are **+5 shapes in code, not commissioned assets** —
plus raising `HairVariants` 4 → 7, `AccessoryVariants` 4 → 6 and `LayerMaxVariant` with them. It is
the largest item of work in this scope and the only one that is not mechanical.

## What must not happen

- **Build and skin never become unlockable items — and skin is never taken away from the player
  either.** They are who the person is, not what they wear. Skin as a reward would be grim; skin
  removed from the choice, likewise. Build comes from the character; tone belongs to whoever is
  playing.
- **An unlock is never by score.** The mock says "Unlock with 500 points", "1,200 points", "Collect 50
  stones". `UnlockEvaluator` forbids any condition tied to a scoreboard, because rule 10 says progress
  toward something that measures the player is not shown. The catalogue uses world state —
  `wall_stage`, `day_reached`, `segments_complete`, `discovery` — and that is what stays. The mock's
  numbers are a draft.
- **No completion count.** Not "3 of 18", not a bar, not a fraction. Locked shows the condition; never
  the distance to it.
- **Appearance never evolves on its own.** No tier, no level, no automatic asset swap: only what the
  player chooses changes. An unlock is lateral, not an upgrade.

## Sequencing

**After the backpack tabs.** The tabs are settling the row treatment that creation will mirror;
building creation against the current rows means redoing it the following week. The order is: tabs →
extract the row as a shared component → art (+5 shapes) → catalogue and presets → state → creation
screen.

## Acceptance criteria

1. No art variant is selectable in creation if the catalogue considers it locked.

   **1b.** The skin-tone selector exists, is reachable, offers every tone, and no character floor
   overwrites it.

2. The five ids disappear from the "unresolved id" warning; `node tools/validate-content.mjs` passes.
3. Two free options per slot in creation; the other four visible, locked, with the condition written
   out in full.
4. A fresh save: the backpack opens already showing what the character is wearing.
5. Unequipping an accessory changes the figure.
6. Two different pieces never draw the same thing.
7. `tools/e2e.sh` covers creation in both languages: choose a character, swap a piece, and the choice
   survives the next boot.
