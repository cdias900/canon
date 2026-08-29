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


## Update — NVI access granted

`version_id` is now **129 (NVI, Nova Versão Internacional)**, in Portuguese, which is what the
game ships against. All ten manifest references plus the whole of NEH.4 resolve under it.

Two things worth knowing, both learned the hard way:

1. **The listing endpoint lies by omission.** `GET /bibles?language_ranges[]=pt` still returns only
   BLT, even while `GET /bibles/129/passages/NEH.4.6` returns 200. Direct passage access is the
   authority on entitlement; never gate on the listing.
2. **Entitlement propagation is not instant.** 129 returned `403 Access denied` for a while after
   the licence was accepted, then began serving without any change on our side. A 403 here is not
   necessarily permanent — retry before concluding a version is unavailable.

### The licence obligation changed with the translation

BLT was CC BY-SA. **NVI is all-rights-reserved:**

> Bíblia Sagrada, Nova Versão Internacional®, NVI® © 1993, 2000, 2011, 2023 por Biblica, Inc.
> Usado com permissão. Todos os direitos reservados mundialmente.

Consequences for the build:

- The copyright string ships in `verses.json` and **must be displayed in-game**. This is now a
  contractual requirement, not a courtesy.
- The "can we store the text?" question is answered by the YouVersion licence agreement, not by an
  open licence. Re-read the accepted terms before caching text anywhere beyond the built app.
- The dual-corpus design in `CLAUDE.md` regains its point: embeddings should run over a
  public-domain text that returns only *references*, with NVI used for display only.
