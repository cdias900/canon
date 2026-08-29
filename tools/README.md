# tools/ - scripture pipeline

Two Node scripts that run **outside Unity**. They turn a list of references into the single
data file the game reads at runtime, and then check that nothing in the repository quietly
copied scripture instead of referencing it.

Requirements: Node 20 or newer. No npm install, no dependencies, nothing to build.

```
tools/verses.manifest.json   the references we ship (input, hand edited)
tools/fetch-verses.mjs       manifest -> Assets/Resources/Data/verses.json (output, generated)
tools/validate-content.mjs   deterministic layer-1 validator (exit 1 on failure)
```

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

## OPEN DECISION: `version_id`

`version_id` in `verses.manifest.json` is deliberately **not** a real value. It reads
`PENDING_TRANSLATION_DECISION`, and the fetch refuses to run until it is an integer.

It stays open until three licence questions are answered: may we store the text, may we
fetch in bulk, and which versions the app key enables. Until then, point it at a
public-domain translation id to unblock implementation and swap the id later - nothing
else in the pipeline changes.

Override without editing the manifest:

```
node tools/fetch-verses.mjs --version-id 3034
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
| `--provider youversion` | Default. Fetches real text. Requires `YOUVERSION_API_KEY`. Any other provider name is an error. |
| `--placeholder` | Structure-only output, no key needed, marks `is_placeholder: true`. |
| `--version-id <id>` | Overrides the manifest's `version_id`. |
| `--manifest <path>` | Alternate manifest (default `tools/verses.manifest.json`). |
| `--out <path>` | Alternate output (default `Assets/Resources/Data/verses.json`). |

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
4. A forbidden term from the project checklist in a player-facing string in
   `Assets/Resources/Data/*.json`: bencao, proposito, jornada de fe, devocional,
   versiculo do dia, testemunho, "Deus tem um plano" (matched without accents).

Exit 0 - reported but not blocking:

- The same 8-word overlap under `docs/`. Design documents quote on purpose; they are not
  build artifacts.
- A `verse` reference used in `Assets/Resources/Data/*.json` that is absent from
  `verses.json`. Add it to the manifest and re-run the fetch, or the player sees the
  unavailable-text marker.

Two deliberate exemptions:

- `verses.json` is exempt from the forbidden-term check. It is licensed translation text,
  not our authored voice, and it is not ours to rewrite. The report prints this exemption
  every run so it reads as a decision rather than a hole.
- `verses.json` is exempt from the overlap check, for the obvious reason that it *is* the
  scripture the check compares against.

The report never prints the matched text - only file, line and word count. Validating must
never become a way to spill licensed text into a log.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `version_id "PENDING_TRANSLATION_DECISION" is not a YouVersion version id` | The open decision above. Pass `--version-id`, or fill the manifest. |
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

List everything awaiting a human read:

```bash
node tools/list-curation.mjs
```

Two rules still hold mechanically and are not a matter of judgement:

- **Quotation stays reference-only.** A line carries `verse` or `text`, never both, and the text of
  a quotation is resolved from `verses.json` at runtime. Authored dialogue fills the gaps *around*
  recorded speech; it never restates it in other words. `validate-content.mjs` fails the build on
  any 8+ word run shared with the scripture text, which is what stops a paraphrase drifting in.
- **God, Jesus and the Holy Spirit never speak in generated text.** No flag, no exception.
