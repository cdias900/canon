# YouVersion Platform API — verified facts

Verified live on 2026-08-29 with the project app key. Everything below was confirmed by an
actual request, not read from documentation.

## Endpoint surface

| | |
|---|---|
| Base URL | `https://api.youversion.com/v1` |
| Auth header | `X-YVP-App-Key: <key>` |
| List versions | `GET /bibles?language_ranges[]=pt` — the bracket suffix is **required**, a plain `language_ranges` returns HTTP 422 |
| Version metadata | `GET /bibles/{version_id}` — includes `copyright`, `title`, `abbreviation`, `books[]` |
| Chapter structure | `GET /bibles/{version_id}/books/{book_usfm}/chapters/{n}` — lists verse ids, **no text** |
| **Passage text** | `GET /bibles/{version_id}/passages/{passage_id}` — returns `{id, content, reference}` |

The passage endpoint is the only one that returns text. It accepts both a verse id (`NEH.4.6`)
and a whole chapter id (`NEH.4`) — but a chapter comes back as one unnumbered blob, so the
chapter reader fetches verse by verse to keep verse numbers.

`.../chapters/{n}/verses` returns verse *metadata* only; `?include_content=true` is ignored.

## Versions this key enables

| Language | Count | Versions |
|---|---|---|
| pt | 1 | **3254 BLT** — Bíblia Livre Para Todos |
| en | 11 | 12 ASV, 42 CPDV, 206 engWEBUS, 3034 BSB, 2660 LSV, 1932 FBV, … |
| es | 3 | 147 RVES, 3291 VBL, 3365 spaPdDpt |

## The translation decision — now answerable

`version_id = 3254` (BLT) is the only Portuguese option this key exposes, so it is the choice
by elimination rather than by preference.

Its licence resolves the three open questions in `CLAUDE.md`:

> Dr. Jonathan Gallagher. Released under Creative Commons Attribution-ShareAlike 4.0 Unported.

- **May we store it?** Yes — CC BY-SA 4.0 permits storage and redistribution.
- **May we batch fetch?** Yes, subject to ordinary rate limiting.
- **Which versions does the key enable?** The table above.

Consequence for the dual-corpus architecture: because BLT is CC BY-SA rather than
all-rights-reserved, the fallback design (embeddings over a public-domain text, display via
YouVersion) is not forced here. Attribution and share-alike still must be carried in the build —
`verses.json` keeps the `copyright` string and the game must display it.

**Open:** whether BLT is the right *reading level and register* for the audience is a product
question, not a licence one, and has not been decided. Swapping it later is a one-line change to
`tools/verses.manifest.json`.
