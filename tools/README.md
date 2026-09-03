# tools/ - scripture pipeline and checks

Node scripts that run **outside Unity**, plus the shell scripts that drive Unity itself. The Node
half turns a list of references into the data files the game reads at runtime and then checks that
nothing in the repository quietly copied scripture instead of referencing it.

Requirements: Node 20 or newer. No npm install, no dependencies, nothing to build.

```
tools/verses.manifest.json   the references we ship, and one translation per locale (hand edited)
tools/fetch-verses.mjs       manifest -> Assets/Resources/Data/locales/<locale>/verses.json
tools/validate-content.mjs   deterministic layer-1 validator (exit 1 on failure)
tools/list-curation.mjs      authored canonical speech awaiting a human read, every locale

tools/unity-check.sh         headless compile
tools/acceptance.sh          the product rules, asserted once per locale
tools/e2e.sh                 build a player and play the declared season in every locale, with shots
tools/ios-sim.sh             build, install, run and drive an iOS simulator - the only way we
                             touch a phone; `setup` once, then tap/press/swipe/text/key/shot
tools/ios-device.sh          the same on a real iPhone over USB; needs a signing team, which
                             lives in ProjectSettings so a Unity re-export cannot lose it
tools/tile-preview.sh        render the procedural tile art to a PNG without Unity, in about a
                             second; sheet / zoom / field <density> / check

tools/study-server.mjs       the study endpoint, run by hand during development. The game only
                             calls it when a URL is configured, so the player is offline by
                             default; the model key lives here and never in the client (rule 16)
tools/table-server.mjs       the multiplayer table (docs/multiplayer.md): seats, the trumpet, the
                             group raid. Off unless a URL is configured, like the study endpoint
tools/table-server.test.mjs  the rules that cannot be wrong, run with `node --test`
tools/table-admin.mjs        the moderator's side: the reported messages, hide one, mute a seat
tools/table-server.Dockerfile / .compose.yml   the server as a container with a volume for its db
```

`e2e.sh` deliberately does not say how many stages it plays, and neither does the runner. Coverage
is read from `Assets/Resources/Data/stages.json` at runtime: the run starts on a cold save at the
first stage and stops at the stage that declares itself `terminal`. This line said "the opening" for
a season that was three days long and "all three days" for one that had grown to nine, which is the
failure mode a written-down list has - people read it instead of the code. What is worth writing
down is the part that is a choice: **the full battery of checks (missing-string sweeps, panel
ordering, dense screenshots) runs on three stages the runner picks out of the table** - the first
one, the one that declares `reveals_page`, and the one that declares `terminal` - and every other
stage is traversed cheaply, asserting only that it was reached, that its panels were recognised and
that the day rolled over. The two chapter-reader taps on the reveal and the ending are never in the
cheap tier: those are the `deep_read` doors and they are the reason the harness exists.

The locales run **one at a time**, so the wall clock is their sum. That is deliberate and it is a
step backwards taken on purpose. They used to run concurrently — each has its own disposable data
directory, log, locale-suffixed screenshots and result file, so they share nothing but the read-only
app bundle, and the concurrency looked free. It was not: both players are launched windowed at the
same size, the second covers the first completely, and **macOS suspends rendering for a fully
occluded window**. Every step of the runner is a `yield return null`, so a player that stops getting
frames does not fail — it stops, silently, until the outer watchdog kills it fifteen minutes later.
That is the hang this harness had on 30/08, 01/09 and twice on 02/09, always just past the opening
screenshot. `--parallel` brings the old behaviour back for anyone who wants the wall clock and is
watching the run.

`Builds/e2e/` is emptied at the start of every run: screenshots from a shorter run sitting beside a
longer one read as current evidence.

**The group screen is on the gate.** Each locale's player gets a table server of its own — started
by `e2e.sh` from `tools/table-server.mjs`, in memory, on `TABLE_PORT` (default 8799, plus the
locale's index) and killed with the player — and the URL is passed as `-table-url`, the way a
developer passes it. The runner opens the drawer, makes a table, stands through one poll interval
(the redraw that once threw), sounds the trumpet, declines the call and closes the screen. Without
node, or with the port busy, the player runs with no URL and the runner **SKIPs the table visibly**
rather than passing a screen it never opened. The raid itself is not on the gate: it opens at the
trumpet's hour, two hours out, and its arithmetic is the server tests' job.

`--from-stage N` seeds a save at stage N and starts there. It is an **authoring convenience and not
the gate** - the cold run from a fresh save is the gate, because reachability from the first frame
is the whole reason this harness exists, and a run that started at stage six would have passed
happily on the season in which stages four through nine could not be reached at all.

One set of references, one translation per language. The references are language independent by
design — the model picks a reference, and each locale resolves it in its own version — so adding a
language never touches the list.

## The rule this pipeline exists to enforce

The model, the code, the specs and the JSON only ever carry a **reference** (`NEH.4.6`).
The literal text is resolved at runtime from the generated `verses.json`, which is produced
here and by nothing else. That makes a hallucinated or hand-typed verse impossible by
construction rather than by good intentions.

`Assets/Resources/Data/locales/<locale>/verses.json` is **generated** — one per language. Never edit
it by hand. Run the fetch, commit the output.

## Source of text

The **YouVersion Platform API** is the only source. There is no secondary provider and no
fallback: if the key or the version id is missing, the fetch fails loudly instead of
silently reaching somewhere else.

- Base URL: `https://api.youversion.com/v1`
- Auth header: `X-YVP-App-Key`, read from the `YOUVERSION_API_KEY` environment variable
- Chapter endpoint: `GET /bibles/{version_id}/books/{book_usfm}/chapters/{chapter}/verses`

There is no single-verse endpoint, so the script fetches whole chapters - each chapter at
most once, with a polite delay between calls and backoff on `429` - and slices out the
verses the manifest asks for.

## Translations, one per locale

`verses.manifest.json` names a version per language under `versions`:

| Locale | Version | |
|---|---|---|
| `pt-BR` | 129, NVI | All rights reserved. **This is why the repo is private.** Regenerate against BLT (3254, CC BY-SA) before making it public. |
| `en` | 206, World English Bible | Public domain, so English carries no licence obligation. |

English was chosen public-domain deliberately: NIV (111) is served by the same key and was
rejected for it. That makes `en` the locale that could ship publicly first.

**Probe entitlement by fetching a passage, never by listing versions.** The
`/bibles?language_ranges[]` listing under-reports — it omits versions that serve fine — so the
listing cannot tell you whether a version works. Ask for `NEH.4.6` and see what comes back.

Override without editing the manifest (one locale at a time):

```
node tools/fetch-verses.mjs --locale en --version-id 12
```

## Running the pipeline

### 1. Before the key lands - placeholder build

Makes the game runnable end to end with zero licensed text:

```
node tools/fetch-verses.mjs --placeholder
node tools/validate-content.mjs --allow-placeholder
```

Every text field becomes the visible pt-BR unavailable marker
(`U+27E8 texto indisponivel U+2014 NEH.4.6 U+27E9`, accents included), and the file carries
`"is_placeholder": true`. `ScriptureService.IsPlaceholderBuild` reflects that flag, and the
validator refuses such a file unless `--allow-placeholder` is passed - so a placeholder can
never reach a release build by accident.

`placeholder_chapter_verse_counts` in the manifest only tells the placeholder generator how
many stub verses to synthesize, so the chapter reader has something scrollable. It is
structural padding, never shipped text, and a real fetch replaces those entries entirely.

### 2. With a key - real build

```
export YOUVERSION_API_KEY=...        # app key from https://platform.youversion.com
node tools/fetch-verses.mjs                  # every locale; --locale en for just one
node tools/validate-content.mjs
git add Assets/Resources/Data/locales/*/verses.json
```

Run it once and commit the output. Runtime never calls the network; the file is baked into
the build and the game works in airplane mode.

### Flags

`fetch-verses.mjs`

| Flag | Effect |
|---|---|
| *(no flags)* | Fetches every locale listed in the manifest. Requires `YOUVERSION_API_KEY`. |
| `--locale <id>` | Just that one locale. |
| `--placeholder` | Structure-only output, no key needed, marks `is_placeholder: true`. |
| `--version-id <id>` | Overrides the version for one locale. Requires `--locale`. |

`YOUVERSION_VERSION_ID` in the environment is only a fallback for a locale the manifest does not
describe. It cannot override a manifest entry: one id in the environment cannot be correct for more
than one language, and when it could, it silently fetched Portuguese into the English folder.

`validate-content.mjs`

| Flag | Effect |
|---|---|
| `--allow-placeholder` | Accept a placeholder `verses.json` instead of failing on it. |
| `--root <dir>` | Validate another repository root (used for testing the validator itself). |

## What the validator checks

Checks 1-6 run once per shipped locale; 7-11 run once over the repository. The authority is the
header comment of `validate-content.mjs` itself, and this list is a copy of it - if the two ever
disagree, the script wins.

Exit 1 - build blocking:

1. A locale's `verses.json` is a placeholder build and `--allow-placeholder` was not passed.
2. A manifest reference missing from a locale's `verses.json`, or present with empty text.
3. Any file under `Assets/` or `tools/` containing a run of 8 or more consecutive words that
   also appears in that locale's `verses.json` - an accidental paraphrase or a hand-copied verse.
   Case, accents and punctuation are normalized away first, so a reformatted copy is still caught.
4. A forbidden term from that language's checklist in one of its player-facing strings. The
   lists are **curated per language, never translated**: the checklist targets a register, and a
   literal translation of the pt-BR list puts bare "purpose" on the English one, which fires on
   "picked on purpose" and teaches everyone to ignore the validator.
5. A cited verse, **or the chapter it lives in**, absent from that locale's `verses.json`. The
   first shows the unavailable-text marker inline; the second gives *Saber mais* nothing to open,
   which is the failure that once shipped with seven of nine citations carrying a dead door. This
   covers a contest's `page_verse` as well as a dialogue line's `verse`.
6. A player-facing string writing a scripture reference into its own prose. That puts
   chapter-and-verse on screen behind `ScriptureVisibility`'s back and with no way into the reader.
7. A locale missing a string the authoring locale has, a placeholder like `{0}` that survives in
   one language and not the other, or a dialogue file that disagrees with the authoring locale
   about anything that is not words — nodes, line counts, verse references, choices, grants,
   flags. Grants and flags live inside a per-language file, so this is what stops a translation
   changing what the game *does*.
8. A C# file hardcoding a string a player can read. The sinks are **derived from the method
   declarations** — any `string` parameter named `label`, `content`, `caption`, `title`… — rather
   than listed, so a literal forwarded through a helper is caught too. Fields whose *name* says
   they hold player words are checked as well: `static readonly string[] DirectionCaptions = {…}`
   is not a call argument, and it shipped untranslated once before this check existed.
9. A dialogue speaker with no authored display name in some locale.
10. C# asking `Loc.T` for a key that no `ui.json` carries.
11. A dialogue node marked `canonical_speaker` without `needs_curation`, which would route authored
    speech for a real figure past the human read rule 4 requires.

Exit 0 - reported but not blocking:

- The same 8-word overlap under `docs/`. Design documents quote on purpose; they are not
  build artifacts.

Two deliberate exemptions:

- Every locale's `verses.json` is exempt from the forbidden-term check. It is licensed
  translation text, not our authored voice, and it is not ours to rewrite. The report prints this
  exemption every run so it reads as a decision rather than a hole.
- Every locale's `verses.json` is exempt from the overlap check — one of them *is* the scripture
  being compared against, and the others are licensed text nobody is being asked to rewrite either.

The report never prints the matched text - only file, line and word count. Validating must
never become a way to spill licensed text into a log.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `No version is configured for locale "xx"` | Add it under `versions` in the manifest, or pass `--version-id` with `--locale`. |
| `Environment variable YOUVERSION_API_KEY is not set` | Export the app key, or use `--placeholder`. |
| `YouVersion rejected the app key (HTTP 401)` | Wrong key, or the key does not enable that version id. |
| `Verse X came back without usable text` | The API item shape changed. The error lists the field names it did receive; adjust `extractReference` / `extractText` in `fetch-verses.mjs`. |
| Game shows the unavailable marker on every line | A placeholder build, or `verses.json` is missing from `Assets/Resources/Data/`. |

## The curation queue

A canonical figure — someone the text records speaking — may be given authored dialogue, provided
it asserts nothing the passage does not. That is a judgement, and no script can make it, so the
nodes where it applies are marked instead of checked:

```json
"intro_gathering": {
  "npc": "governador",
  "canonical_speaker": true,
  "needs_curation": true,
  ...
}
```

List everything awaiting a human read, in every language:

```bash
node tools/list-curation.mjs
```

**A translation of a canonical figure's speech is newly authored speech in that language.** It can
drift from the passage on its own, in ways the original never did, and no automatic check can tell.
So every locale is queued, not just the authoring one: a language that has not had the read has not
had the safeguard.

Two rules still hold mechanically and are not a matter of judgement:

- **Quotation stays reference-only.** A line carries `verse` or `text`, never both, and the text of
  a quotation is resolved from `verses.json` at runtime. Authored dialogue fills the gaps *around*
  recorded speech; it never restates it in other words. `validate-content.mjs` fails the build on
  any 8+ word run shared with the scripture text, which is what stops a paraphrase drifting in.
- **God, Jesus and the Holy Spirit never speak in generated text.** No flag, no exception.

## `table-server.mjs` — multiplayer, the part that runs

```bash
node tools/table-server.mjs --port 8788 --db table.db   # :memory: by default; PORT / TABLE_DB in the env also work
node --test tools/table-server.test.mjs                  # the rules that cannot be wrong
node tools/table-admin.mjs --db table.db reports         # what people flagged; then hide <code> <id> / mute <code> <seat> <hours>
docker build -f tools/table-server.Dockerfile -t table-server . && docker run -p 8788:8788 -v table-data:/data table-server
```

Before starting one by hand, check nothing is already on the port — a server left over from an
earlier session answers `/health` as if the new one had started, and the new one dies quietly in
its own log with `EADDRINUSE`: `lsof -nP -iTCP:8788 -sTCP:LISTEN`.

The design is `docs/multiplayer.md`; this is the server it specifies. The Unity client is
`TableService` (the calls) and `TablePanel` (the screen), reached from the HUD drawer **only when a
URL is configured** — `-table-url http://127.0.0.1:8788` on the command line, or the
`sheepgate.table.url` PlayerPrefs key on iOS, where the player gets no command line. Without one
there is no button, no request, and the solo game is untouched. The device also keeps the code of
the last table it joined (`sheepgate.table.code`) and rejoins it when the screen opens.

The group mission (§06) is resolved here, not on the device: a trumpet names an hour, seats answer,
the raid opens at the hour on whoever said yes, and each turn closes when every present seat has
picked or two minutes have passed. Nothing is scheduled — a due trumpet is opened and a lapsed turn
is resolved by the first request that arrives afterwards, at the time it was due. The tuning is read
from `Assets/Resources/Data/contest.json`, the same file the game reads, so there is one copy of the
numbers. To watch a raid on the simulator without waiting two hours, sound the trumpet by hand:

```bash
curl -s localhost:8788/trumpet -X POST \
  -d '{"code":"ABCDEF","playerId":"<the device uuid>","atEpochMs":'$(( ($(date +%s) + 30) * 1000 ))'}'
```

It is here rather than in the game for the same reason `study-server.mjs` is: rules that a client
enforces are rules that live in a text editor. Two of them are rule 17 — a minor never shares a
table with an adult, and a minor table can never carry free text — and both are `CHECK` constraints
in the schema rather than validation on the way in, so a future code path that forgets gets an
exception instead of a quiet row. A third is rule 11: a committed contest move is stored but never
returned until its turn resolves.

**Free text and private tables ship switched off** (`ALLOW_FREE_TEXT=1` turns them on, for adult
tables only). That default is not timidity — `docs/persona-and-purpose.md` argues the chat
protections have to hold on both sides of the age boundary *because age is self-declared*, and
turning them off is a decision for Pedro and cybersecurity rather than a side effect of a commit.
