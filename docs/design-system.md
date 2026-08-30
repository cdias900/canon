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
12. **A row that has to print a sentence is full width.** The authored source specifies
    `InventoryItem` as *Grid 2 col mobile* and points one component at *resource · gear · lore*.
    That holds for a card carrying a label and a number. It does not hold for a locked wardrobe
    item, because `AGENTS.md` rule 7 makes a locked item an invitation rather than a void: it keeps
    its art, its name, its description **and its unlock condition, spelled out whole**. That is a
    sentence, and a two-column card cannot hold one at or above `Type.Minimum` without truncating
    it — which is the single thing that rule forbids. So the split is by what the row must say, not
    by what the row contains: **prose gets one full-width column; the grid is for label-plus-number
    cards.** The backpack is both at once and divides accordingly — wardrobe rows are full width,
    the materials cards keep the two-column grid. Nobody should later "correct" the wardrobe into a
    grid: the grid is what the document says, and the sentence is what the rule requires.

## Segmented tabs

Sistema Vale has no tab component. The treatment below was derived for the backpack sheet, where
five sibling panels share one modal surface, and is recorded here as the system's answer. **The next
screen that needs tabs reuses this treatment instead of inventing a second one** — the cell, the
selected state, the badge and the label-fit rule are the system's, and only the placement is a
screen's to argue.

**Two bars, and both of them are ratified.** The backpack draws three sections along the top —
Perfil, Itens, Aparência — and Aparência opens three wardrobe slots along the bottom: Cabelo, Roupa,
Extras. Five content panels sit behind the two bars, and the lower bar leaves the screen with the
wardrobe rather than standing under a list it cannot change. This is a deliberate departure from the
single bottom row this section used to prescribe, argued in code where the constants are declared in
`Assets/Scripts/UI/BackpackPanel.cs`, and the argument is the placement rule's own reason turned on
itself: *bottom, because that is where the thumb is and this is the most-tapped control* belongs to
Cabelo/Roupa/Extras, tapped over and over while a character is dressed, so they keep the bottom. A
section is chosen once per visit, so it goes up top, where it reads as the sheet's own navigation
instead of competing for the same corner. A screen that needs one bar uses the bottom one; a screen
that needs two puts the frequent one there and makes the same argument in code.

### Placement

The row is bottom-anchored **inside the card it belongs to**, never pinned to the screen:
`AnchorBottom(Space.TouchTarget, Space.S12, Space.S12, Space.S20)`. On the backpack sheet that
leaves 20 between the row and the card's own bottom edge, and that edge is itself
`Space.SafeAreaBottom` above the safe area — 42 of clearance from the home indicator. A section bar
above it is `AnchorTop` at the same `Space.S12` inset and the same `Space.TouchTarget` height, and
gets a full-width divider of its own underneath, for the same reason the lower one has a divider
above.

Bottom, because that is where the thumb is and this is the most-tapped control on its surface; a
tall sheet puts a top-anchored row in the stretch zone. The obvious objection — that a bottom row
impersonates the OS tab bar — does not survive the specifics. This row sits inside a modal card that
is already inset from both screen edges, it is **text only** and never the icon-over-label stack
that is the bottom-nav signature, and the platform's own tab-bar guidance exempts a modal from the
persistent-bar expectation to begin with.

The band is inset `Space.S12`, **not** the surrounding sheet's `Space.S20`, and is therefore
deliberately not aligned with the content above it. The inset buys 5.3 points of label width per
cell on a three-cell bar — at the reference width S20 gives 102.7 and S12 gives 108 — and that
margin is the entire reason the band is wider than the column above it. It bought more still when
this bar carried four cells, at 77 against 81. The misalignment is paid for by drawing the divider
that bounds the band at **full card width**: a rule the band sits against reads as a region
boundary, while a rule narrower than the band reads as a mistake.

### Geometry

Cells are **equal, fixed and touching**. Row width is the card minus two `Space.S12`; divide by the
cell count and pin each cell's `LayoutElement` min and preferred width to the result, with
`flexibleWidth = 0` and `childForceExpandWidth = false`. The fixed widths are load-bearing rather
than tidy: the selected label changes weight, and content-driven cells would re-measure the whole
row on every tap.

**Derive the card from `UIKit.CanvasWidth()`, never from `UIKit.ReferenceWidth`.** The canvas is
1080 units across only on a device whose aspect is exactly 1080×1920; the scaler's match-0.5 rule
gives a taller phone *fewer* units, and an iPhone 17 Pro reports about 976. A width pinned to the
reference overflows its parent there by the difference. Measured on that phone: backpack rows ran
past the scroll viewport and clipped a description mid-word, and the widest tab label spilled off
the card. **`tools/e2e.sh` cannot see this** — the macOS player is launched at exactly 1080×1920,
where the reference and the truth agree — which is why every layout change also gets a pass on
`tools/ios-sim.sh`.

So the numbers are ratios, not constants. At the reference width the backpack's row is 324 across
three cells, so 108 each; on the phone the card is about 310 points, the row is 286.5 and the cells
are 95.5. Both bars carry three cells, so one derivation serves both. What the
contract fixes is the derivation, and every width below it — row, text column, name box, material
card — comes off the same measured card.

There is no `Space.TouchGap` between cells, and this is the one place in the system where the second
half of rule 3 is deliberately not applied. The 8-point spacing exception exists for targets below
24×24; these clear the criterion outright at any width this card can have, and every platform draws
a segmented control as touching segments. The 48×48 half of the rule is exceeded, not bent.

There is **no track behind the row**, because this palette cannot draw one. Measured: parchment ink
at 8% over `Surface.Card` is 1.24:1, `Surface.Panel` is 1.11:1, `Neutral.N800` is 1.20:1. Nothing is
subtle and visible at the same time, so nothing is drawn. The row reads as one control from the
full-width divider that bounds it, three equal cells, and one of them filled.

### Selected and unselected

| | Fill | Rim | Label ink | Label role |
|---|---|---|---|---|
| Selected | `Brand.PrimaryDark` on `UiSpriteKeys.FrameMd` | `Brand.Primary` on `UiSpriteKeys.FocusRing`, stretched to the cell's own rect | `Ink.OnPrimary` | `TypeRole.BodyStrong` |
| Unselected | none | none | `Ink.Secondary` | `TypeRole.Body` |

Three channels separate the two states and only one of them is colour: the fill is present or
absent, the weight is 700 or 400, the ink changes. Rule 2 is satisfied without adding a word or an
icon. Draw the fill, the rim and the label as your own children — never recolour the kit's Label,
Border or button fill, which `VariantButton` owns and repaints on the next pointer event.

**The fill is two tokens because no single one can carry the state.** `Ink.OnPrimary` on
`Brand.Primary` is 3.82:1 and fails SC 1.4.3 — a 15.17 bold label is normal text, since the
large-text exception begins at 14 point bold, which is 18.67 in these units. `Brand.PrimaryDark` on
`Surface.Card` is 2.56:1 and fails SC 1.4.11 for a state indicator. Solving for one fill that does
both needs a relative luminance between 0.14965 and 0.16964, and no Brand token lands there
(`Primary` is 0.20889, `PrimaryDark` is 0.12062). So the fill carries the label — `Ink.OnPrimary` on
`PrimaryDark` is 5.79:1, AA — and the rim carries the boundary — `Brand.Primary` on `Surface.Card`
is 3.89:1, which clears SC 1.4.11. Pressing an already-selected cell raises its fill to
`Brand.Primary`, which is the other reason `Primary` stays in the pair.

That `Ink.OnPrimary` on `Brand.Primary` fails at all is a defect of the system rather than of this
component: **every Primary-variant button in the game has a failing label.** The fix belongs in
`UIKit.SkinFor`, where the blast radius is, and is not this component's to make.

**The selected tab is clay, not gold.** Gold means one thing — *not yet seen* — and it is spent on
the NOVO badge and on the tab's own dot, both of which clear by being looked at. Clay means *the
current one*: the tab you are on, and the piece you are wearing. Before this, gold was carrying
new, worn and focused at once, which is three meanings on one accent and the reason none of them
read. The focus ring stays gold as the single accepted exception, because rule 5 fixes it globally
and it is chrome rather than content.

### The badge

A tab whose list holds something unseen carries a **dot, never a number**: `UiSpriteKeys.IconDot` in
`Brand.Secondary` at `DesignTokens.Px(8)`, pinned to the cell's top-right corner with
`anchoredPosition = (-Space.S8, -Space.S4)` and `raycastTarget = false`. The vertical offset is the
component, not a detail — a corner dot without it lands on top of a wide label. Contrast is 8.31:1
on `Surface.Card` and 3.24:1 on `Brand.PrimaryDark`, so it survives a selected cell too.

Never a count. Three numbers on three adjacent cells read as a scoreboard to clear even when each
one is individually legal, and `AGENTS.md` rule 10 is about precisely that reading. A dot is spent
the moment its tab is looked at — including the tab that is selected on open — while an unvisited tab
keeps its dot until someone goes there. A dot that survives being looked at is the nagging version.

A cell carries a dot only when its own table names one, which is what lets a section stand in for the
lists folded inside it: Aparência carries one dot for all three wardrobe slots, because with the
wardrobe closed their cells are not on screen to carry their own. Perfil and Itens carry none —
neither has a seen-state to spend. One dot for three lists is still a dot, and still not a count.

### Focus, and the label-fit rule

The focus ring is untouched: the system's gold 2-point ring at `-UIKit.FocusRingOutset`, identical
across variants per rule 5. It extends outside the cell and so overlaps a neighbour when focused,
which is accepted because touch never produces focus. Build the fill and the rim first, then send
the kit's own `FocusRing` child back to the front.

**A tab label must fit its cell at `TypeRole.BodyStrong`, in every locale, on the narrowest device
the game ships to.** The label rect is flush to the cell — no `Space.S4` inset. The inset is what
the layout contract asked for and it was dropped for a measured reason, back when this bar carried
four cells: on the phone the cell was 71.6 points, and eight points of inset left a 63.6-point box
for a label whose widest word, *Materiais* / *Materials*, is 68.91 at Manrope Bold. Flush, the box
was the full 71.6 and that word cleared it by 2.7. The labels are centred and every other word is
far shorter, so the inset was buying nothing the centring does not already give. Three cells are
roomier and the rect stays flush anyway: the derivation is what the contract fixes, and re-adding
the inset would only put the next long word back against the wall.

**The working limit is therefore the cell width itself, and on the phone that is about 95.** That is
an acceptance criterion, not a comfort, and it is worth saying out loud in review — the row is one
re-translation away from failing, and the margin is smaller than the 348-point design frame implies.
The widest label the sheet now carries is *Aparência*, 74.6 against a 95.5 cell; it is the benchmark
a new word gets measured against, and it took that title from *Materiais* when the bars went to
three cells. The count matters as much as the word: at five cells a label would have had 57.3 points
and *Personalização* needs 112.8, which is how the sheet arrived at two bars of three rather than one
bar of five.

When it does fail, it fails loudly. The label keeps the kit's wrapping default inside a one-line
rect, so an over-limit word breaks and spills **below** the cell into the sheet's bottom padding: it
never clips silently and never ellipsises, and the e2e screenshot catches it in both locales. The
remedy is to re-author the word as a real word in both locales, and there is no second remedy. Do
not shrink the type — `Type.Minimum` would fit anything, and 12 on the most-tapped control of a
surface is the wrong trade. Do not make the cells unequal. This is why the backpack's accessory tab
has never read *Acessórios* / *Accessories*: those are 81.72 and 90.20, both wider than the cell was
at four cells, at any inset the card could offer. It read *Detalhes* / *Details* until the bars went
to three, and it now reads **Extras** in both locales — re-authored in the same pass that made
*Materiais* into *Itens* / *Items* to make room for *Aparência* on the bar above.

That word is shared, and deliberately. Character creation and the backpack both draw the three slot
names from `slot.hair` / `slot.outfit` / `slot.accessory`, one namespace adopted precisely because
two call sites is where drift lives — so creation's third tab reads *Extras* too, and always will.
Renaming a slot is one edit in two locale files and it lands on both screens at once, which is the
point of the namespace rather than a side effect of it.

### The row's name line, and the second thing a narrow card broke

A wardrobe row's name line holds three things: the name, the NOVO badge when the item is new, and a
permanently-reserved status slot for the tick or the padlock. **The gaps between them, and the
badge's own side padding, are `Space.S4` and not the `Space.S8` the layout contract asked for.**

Same cause as the tab label, one level down. The contract sized the name box off a 200-point text
column; on the phone that column is 162.6, and after the status slot and a badge at `S8` gaps the
name had 72.7 points — narrower than a single word of some item names. Legacy `Text` breaks a word
it cannot fit, so *Túnica de carregador* rendered as **"Túnica de / carregad / or"**: nothing
clipped, no pinned height was wrong, and it was simply unreadable. Sixteen points come back at `S4`
— two gaps and two paddings — putting the box at 88.7, clear of the two longest words in the
catalogue, *carregador* (77) and *ferramentas* (84).

The status slot's width stays reserved on every row whether or not it shows anything, and it is
hidden with `Image.enabled` rather than by deactivating it. A layout group drops an inactive child;
the name would widen, a one-line name could rewrap, and the height pinned at build time would then
clip — which is the one failure the whole pinning policy exists to prevent, and it would only show
on rows nobody is wearing.

### Silence

Tab switching makes **no sound**. `UIKit.CreateButton` wraps any handler it is given in
`AudioDirector.Play(AudioKeys.Confirm)`, so a segment is built with a null handler and wired with
`onClick.AddListener` afterwards. A player comparing two pieces crosses the row a dozen times, and a
cue on every crossing has stopped being feedback. Selecting a row still confirms — an equip is a
confirm — but changing tabs is not.

## What this system does not cover

The design document mocks 7 screens. The game has roughly twice that: morning report, end-of-day
assignment, vocation reveal, the daily quiz, the morale contest, settings, and the chapter reader
have **no mock at all**. Those get system-level treatment — tokens, components, the rules above —
and are not pixel-matched against anything.

The document also has no morale or night screen, because it was drawn for an explore-collect-build
loop rather than this project's day/night turn structure. Where the two conflict,
`MVP-SCOPE.md` describes the game and this describes how it looks.

**Camera:** the document specifies first-person cinematic. This project is and stays a 2D top-down
tilemap. The camera direction applies to key art and to a future production, not to this build.

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
