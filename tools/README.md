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
tools/check-commit-message.mjs   fails a commit message that is not in English
tools/hooks/                 git hooks, tracked so they travel with the repository
tools/install-hooks.sh       points git at tools/hooks (once per clone)

tools/unity-check.sh         headless compile
tools/acceptance.sh          the product rules, asserted once per locale
tools/e2e.sh                 build a player and play the opening in every locale, screenshotting
tools/ios-sim.sh             build, install and run on an iOS simulator
```

One set of references, one translation per language. The references are language independent by
design — the model picks a reference, and each locale resolves it in its own version — so adding a
language never touches the list.

## The rule this pipeline exists to enforce

The model, the code, the specs and the JSON only ever carry a **reference** (`NEH.4.6`).
The literal text is resolved at runtime from the generated `verses.json`, which is produced
here and by nothing else. That makes a hallucinated or hand-typed verse impossible by
construction rather than by good intentions.

`Assets/Resources/Data/verses.json` is **generated**. Never edit it by hand. Run the fetch,
commit the output.

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
node tools/fetch-verses.mjs --provider youversion
node tools/validate-content.mjs
git add Assets/Resources/Data/verses.json
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

Exit 1 - build blocking:

1. A manifest reference missing from `verses.json`, or present with empty text.
2. `is_placeholder: true` without `--allow-placeholder`.
3. Any file under `Assets/` or `tools/` containing a run of 8 or more consecutive words that
   also appears in `verses.json` - an accidental paraphrase or a hand-copied verse. Case,
   accents and punctuation are normalized away first, so a reformatted copy is still caught.
4. A forbidden term from that language's checklist in one of its player-facing strings. The
   lists are **curated per language, never translated**: the checklist targets a register, and a
   literal translation of the pt-BR list puts bare "purpose" on the English one, which fires on
   "picked on purpose" and teaches everyone to ignore the validator.
5. A locale missing a string the authoring locale has, a placeholder like `{0}` that survives in
   one language and not the other, or a dialogue file that disagrees with the authoring locale
   about anything that is not words — nodes, line counts, verse references, choices, grants,
   flags. Grants and flags live inside a per-language file, so this is what stops a translation
   changing what the game *does*.
6. A C# file hardcoding a string a player can read. The sinks are **derived from the method
   declarations** — any `string` parameter named `label`, `content`, `caption`, `title`… — rather
   than listed, so a literal forwarded through a helper is caught too. Fields whose *name* says
   they hold player words are checked as well: `static readonly string[] DirectionCaptions = {…}`
   is not a call argument, and it shipped untranslated once before this check existed.

Exit 0 - reported but not blocking:

- The same 8-word overlap under `docs/`. Design documents quote on purpose; they are not
  build artifacts.
- A `verse` reference used in `Assets/Resources/Data/*.json` that is absent from
  `verses.json`. Add it to the manifest and re-run the fetch, or the player sees the
  unavailable-text marker.

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

## Commit messages

`git log` is read by everyone who touches this repository, so messages are English like the rest of
the code. Opt in once per clone:

```bash
tools/install-hooks.sh
```

That sets `core.hooksPath` to `tools/hooks` — git never shares `.git/hooks`, which is why the hook
is tracked here instead. CI runs the same checker on every push, so the rule still holds for a
clone that never opted in or a commit made with `--no-verify`.

**Quoting pt-BR is not a violation.** Content inside backticks, double quotes or single quotes is
stripped before the message is judged, across line breaks included, so English prose naming a line
of dialogue passes. Two commits in this history rely on that.

A message fails on a letter that does not occur in English prose, on a subject opening with a
Portuguese verb, or on two or more unambiguously Portuguese words. English homographs — `no`, `do`,
`todo`, `os` — are excluded deliberately; a check that fires on "add a todo list" gets ignored.

```bash
node tools/check-commit-message.mjs --range origin/main..HEAD   # judge a range
node tools/check-commit-message.mjs .git/COMMIT_EDITMSG         # judge one message
```

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
