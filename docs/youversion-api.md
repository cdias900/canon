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

## The translation decision — settled

**pt-BR resolves against `129` (NVI, Nova Versão Internacional); English against `206` (World
English Bible).** Every manifest reference and every cited chapter resolves under both.

English was chosen public domain deliberately — NIV (`111`) is served by the same key and was
rejected for it, which makes `en` the locale that could ship publicly first. BLT (`3254`, CC BY-SA)
was the pt-BR choice by elimination before NVI was enabled, and remains the fallback to regenerate
against if NVI is ever withdrawn or if this repository needs to go public.

Two things worth knowing, both learned the hard way:

1. **The listing endpoint lies by omission.** `GET /bibles?language_ranges[]=pt` still returns only
   BLT, even while `GET /bibles/129/passages/NEH.4.6` returns 200. Direct passage access is the
   authority on entitlement; never gate on the listing.
2. **A 403 means the version is not enabled yet — go and enable it.** 129 returned
   `403 Access denied` even with the licence showing as accepted on the account. It started
   serving only after NVI was explicitly enabled for the app in the YouVersion developer portal.
   Accepting a licence is not the same act as enabling a version on a key. Do not sit and retry a
   403: it will not clear on its own.

### The licence obligation changed with the translation

BLT was CC BY-SA. **NVI is all-rights-reserved:**

> Bíblia Sagrada, Nova Versão Internacional®, NVI® © 1993, 2000, 2011, 2023 por Biblica, Inc.
> Usado com permissão. Todos os direitos reservados mundialmente.

Consequences for the build:

- The copyright string ships in `verses.json` and **must be displayed in-game**. This is now a
  contractual requirement, not a courtesy.
- The "can we store the text?" question is answered by the YouVersion licence agreement, not by an
  open licence. Re-read the accepted terms before caching text anywhere beyond the built app.
- The dual-corpus design in `AGENTS.md` regains its point: embeddings should run over a
  public-domain text that returns only *references*, with NVI used for display only.
