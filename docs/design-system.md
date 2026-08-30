# Sistema Vale — implementation reference

The design system for **A Cidade Quebrada**, as it exists in code. The authored source is
`Jogo Neemias.dc.html` (Sistema Vale v0.1, handoff-ready); this file is the part of it that
survived contact with a Unity uGUI project, plus the decisions taken where the two disagreed.

`Assets/Scripts/UI/DesignTokens.cs` is the source of truth for every value. This document says
what the values mean and which rules are not negotiable. When the two disagree, the code wins and
this file is stale — fix it.

## Units

The system is drawn at **390×844**; the canvas is laid out at **1080×1920**. The conversion factor
is `DesignTokens.DesignScale` (1080 / 390 = 2.769). Sizes in `DesignTokens` are **already
converted** — a reader comparing against the design document divides, never multiplies.

The reference resolution is deliberately *not* changed to 390×844. Every layout constant in this
project is written in 1080-wide units, and moving the reference would rescale all of them at once,
silently.

## Colour

Four token groups, all in `DesignTokens`:

| Group | Tokens | For |
|---|---|---|
| `Brand` | `Primary` `PrimaryLight` `PrimaryDark` `Secondary` `SecondaryLight` `SecondaryDark` `SecondaryPressed` | Clay is the action colour. Gold is the accent. |
| `Neutral` | `N50` `N100` `N300` `N500` `N700` `N800` `N900` | Sand and stone, lightest to darkest. |
| `Ambient` | `Sky` `SkyLight` `Growth` `GrowthLight` | Water and vegetation — the two colours the world gains as it heals. |
| `Feedback` | `Success` `Warning` `Error` `Info` `Disabled` | Always paired with an icon or a label. |

Two role layers sit on top, and are what screens should actually ask for:

- `DesignTokens.Surface` — `Background` `Panel` `Card` `Border` `Scroll` `SceneVeil` `Scrim`
- `DesignTokens.Ink` — `Primary` `OnScene` `Secondary` `Muted` `Faint` `OnPrimary` `OnSecondary`
  `OnScroll` `OnScrollMuted`

`UIKit.Palette` remains as a ten-name semantic alias over these so older screens keep compiling.
**New code should use the token names directly.**

### Gold

`Brand.Secondary` (`#E8B44A`) is the system accent: it marks what can be touched, what is new, and
where keyboard focus is. It is ratified against the smell checklist in `AGENTS.md` rule 13 — what
that rule bans is *luz dourada* as art direction, a devotional glow over a scene. This is an
interaction colour. **It never lights a scene and never decorates one.** A gold gradient behind a
character, a gold vignette, a gold shaft through a doorway: all still banned.

`UIKit.cs` used to carry the note *"No gold, no glow"*. That note is superseded, not forgotten.

## Type

Three families, bundled as static TrueType instances under `Assets/Resources/Fonts/`
(SIL OFL 1.1 — see `OFL-license.txt` there). Legacy `Text` takes one asset per weight, so a role
maps to a file. Ask for a role, never a file:

| `TypeRole` | File | Size token | For |
|---|---|---|---|
| `Display` | Bricolage Grotesque 800 | `Type.Display` (34) | One per screen, at most. |
| `Title` | Bricolage Grotesque 700 | `Type.Title` (21) | Card headings, names, mission titles. |
| `Body` | Manrope 400 | `Type.Body` (15) | Narrative and interface. The default. |
| `BodyStrong` | Manrope 700 | `Type.Body` | Emphasis, button labels. |
| `Mono` | IBM Plex Mono 500 | `Type.Mono` (13) | Quantities, counts, references. Tabular. |

Sizes above are the design document's numbers; `DesignTokens.Type` holds them converted.

**`Type.Minimum` (12 design px) is a floor, not a suggestion.** Nothing in the game may be smaller,
anywhere. The scale this project started from had two steps below it.

### The glyphs the fonts do not have

The design mocks use `✓ ✕ ● ≡ 🔒` as literal characters. **None of the three bundled families
carry any of them** — only `× → · —` are covered. This is checked, not assumed.

Status icons are therefore **procedural sprites** from the art module, never text. This agrees with
the design system's own iconography rule (2px stroke, 2px corners, 24 grid) and with its note that
the emoji in the mocks are placeholders for a 24-icon set. Never solve a missing glyph by
substituting a lookalike character.

## Space, radius, elevation

- `Space` — base-4 scale `S4`…`S32`, plus `Gutter` (21), `TouchTarget` (48), `TouchGap` (8),
  `SafeAreaBottom` (22).
- `Radius` — `Sm` 8, `Md` 14, `Lg` 20, `Pill`. **Buttons are never below `Md`.**
- Elevation is three steps: `Surface.Panel` (elev.0), `Surface.Card` (elev.1), and modal/toast
  (elev.2), which is `Surface.Card` over `Surface.Scrim`.

## Motion

`DesignTokens.Motion` carries the durations. All of them are short on purpose: the design system's
stated UX risk is a HUD that gets between the player and the scene, and slow motion is how that
happens without anyone deciding it should.

`Motion.ReduceMotion` suppresses parallax, pulse and shake. Fades and bars keep running — a
progress bar that does not move reads as broken, not as calm.

## Rules that are not style

These come from the design system and from `AGENTS.md`, and a screen that breaks one is wrong even
if it looks right.

1. **Progress is never a bare bar.** Always label + bar + fraction. The bar is never thinner than
   6 design px. Value transitions take `Motion.BarFill`.
2. **Never colour alone.** Every state carries an icon or a word too.
3. **Touch targets are 48×48 minimum**, with 8 of clear space between them.
4. **Text over the scene sits on a veil** of at least 72% opacity — use `Surface.SceneVeil`.
5. **Focus is a 2px `Brand.Secondary` outline with a 2px offset**, identical across every button
   variant including ghost.
6. **Disabled is opacity 0.40**, no shadow, and still carries its label.
7. **Resource icons always appear with a number.** Never an icon on its own.
8. **Quantities are mono and tabular**, with the resource's label beside them.
9. **One gold call-to-action per screen, at most.**
10. **No confetti, no loud sound on reward.** The reward is that the world changed.
11. **The world's stage is derived from wall percentage**, never set by content by hand.

## What this system does not cover

The design document mocks 7 screens. The game has roughly twice that: morning report, end-of-day
assignment, vocation reveal, the daily quiz, the morale contest, settings, and the chapter reader
have **no mock at all**. Those get system-level treatment — tokens, components, the rules above —
and are not pixel-matched against anything.

The document also has no morale or night screen, because it was drawn for an explore-collect-build
loop rather than this project's day/night turn structure. Where the two conflict,
`MVP-SCOPE.md` describes the game and this describes how it looks.

**Camera:** the document specifies first-person cinematic. This project is and stays a 2D top-down
tilemap. The camera direction applies to key art and to a future production, not to the POC.

## Text integrity, when lifting copy from the design document

The mocks contain written-out Scripture — the mission-complete screen carries
*"Vinde, e reedifiquemos o muro"*, which is `NEH.2.17`. Copying that into a locale file violates
`AGENTS.md` rule 2, and at five words it slips **under** the content validator's 8-word n-gram
threshold, so nothing catches it mechanically.

**Every quotation lifted from the design document goes through the reference pipeline**: the line
carries `verse: "NEH.2.17"`, never `text`. `NEH.2.17` is already in `tools/verses.manifest.json`.

This is not only a rule-2 technicality. The mock's wording is archaic — *Vinde… reedifiquemos* is
ARC-style — and the pt-BR corpus this project actually licenses is NVI, which renders the same
verse quite differently. Transcribing the mock would ship a translation the project neither chose
nor licensed, into a repository that is private *because* of the NVI licence. The designer picks
the reference; the pipeline fetches the text. A quotation in a mock is a stand-in for a reference.

### The found-note pattern

The mock quotes a **fragment** on a note found in the ruins, and the pipeline returns a **whole
verse**. NEH.2.17 in NVI runs through the ruined city and the burned gates before it reaches the
call to rebuild — it will not fit the treatment the mock is built around, and truncating it is not
an option anyone should reach for.

**Decided:** the note carries an **authored, non-Scriptural line**, and the verse appears in full
behind **Saber mais**.

This is the only resolution where the deferral works as designed rather than in spite of itself:

- The note reads as an in-fiction discovery, which is what the mock's own framing
  (*"ANOTAÇÃO ENCONTRADA NAS RUÍNAS · a letra é antiga; ninguém sabe de quem"*) asks for.
- `ref_display` stays hidden until the reveal, per rule 12 — and **Saber mais** is present from the
  first citation onward, so the player is one tap from the whole chapter the entire time.
- Rule 4 permits authored text in the gaps as long as it asserts nothing the passage does not
  support. The note may evoke the call to rebuild. It may not paraphrase the verse — that is what
  the validator's 8-word overlap check exists to stop, and it is a floor, not a target.

Do not solve a fragment-shaped hole by inventing a clause-level citation. The moment the codebase
can quote part of a verse, it can quote selectively, and that needs a rule nobody has written.
