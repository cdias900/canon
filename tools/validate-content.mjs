#!/usr/bin/env node
// tools/validate-content.mjs
//
// Deterministic layer-1 content validator. Runs outside Unity, Node 20+, zero deps.
//
// Checks 1-6 run once per shipped locale; 7-11 run once over the repository.
//
// Fails the build (exit 1) when:
//   1. a locale's verses.json is a placeholder build and --allow-placeholder was not passed;
//   2. a manifest reference is missing from a locale's verses.json, or its text is empty;
//   3. a file under Assets/ or tools/ repeats 8+ consecutive words that also appear in that
//      locale's verses.json - an accidental paraphrase or a hand-copied verse;
//   4. a player-facing string in that locale uses a term from its forbidden checklist;
//   5. a cited verse, or the chapter it lives in, is absent from that locale's verses.json - the
//      first shows the unavailable-text marker inline, the second gives "Saber mais" nothing to
//      open;
//   6. a player-facing string in that locale writes a scripture reference into its own prose,
//      which puts chapter-and-verse on screen behind ScriptureVisibility's back and with no way
//      into the reader;
//   7. a locale is missing a string the source locale has, or its dialogue disagrees with the
//      source locale about anything that is not words - nodes, line counts, verse references,
//      grants, flags;
//   8. a C# file passes a string literal where a player-facing string belongs. Every such string
//      goes through Loc.T so that adding a language is a content change, never a code change;
//   9. a dialogue speaker has no authored display name in some locale;
//  10. C# asks Loc.T for a key that no ui.json carries;
//  11. a dialogue node is marked canonical_speaker without needs_curation, which would route
//      authored speech for a real figure past the human read rule 4 requires.
//
// Warns (exit 0) for the same 8+ word overlap under docs/, where quoting in design
// prose is expected and is not a build artifact.
//
// Matched scripture is never printed: the report gives file, line and word count only,
// so running the validator can never become a way to leak licensed text into a log.

import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, extname, isAbsolute, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const DEFAULT_ROOT = resolve(SCRIPT_DIR, "..");

const NGRAM_SIZE = 8;
const MAX_REPORTED_MATCHES_PER_FILE = 3;

const MANIFEST_RELATIVE_PATH = join("tools", "verses.manifest.json");
const DATA_RELATIVE_DIR = join("Assets", "Resources", "Data");
const LOCALES_RELATIVE_DIR = join(DATA_RELATIVE_DIR, "locales");

// The locale content is authored in. Every other locale is checked against this one, and this
// one is checked against nothing: it is the authority, not a translation.
const SOURCE_LOCALE = "pt-BR";

const localeDir = (root, locale) => join(root, LOCALES_RELATIVE_DIR, locale);
const versesPathFor = (root, locale) => join(localeDir(root, locale), "verses.json");

// Scripture is licensed translation text, not our authored voice. No locale's verses file is
// ever scanned for overlap or forbidden terms, including another locale's.
const isVersesFile = (filePath) => filePath.split(/[\\/]/).pop() === "verses.json";

const FAIL_SCAN_DIRS = ["Assets", "tools"];
const WARN_SCAN_DIRS = ["docs"];

const TEXT_EXTENSIONS = new Set([
  ".cs", ".js", ".mjs", ".cjs", ".json", ".md", ".txt",
  ".yaml", ".yml", ".unity", ".asmdef", ".shader", ".prefab", ".asset"
]);

const SKIP_DIRECTORIES = new Set([
  ".git", "node_modules", "Library", "Temp", "Logs", "obj", "bin",
  "Build", "Builds", ".vs", ".idea", ".vscode"
]);

// Checklist from AGENTS.md section 13, stored deaccented and lowercase because the
// comparison runs on normalized tokens. Plurals included: the rule is about the word,
// not the spelling.
//
// The lists are curated per language, never transliterated. The checklist targets a register,
// and the register is carried by different words in each language: a straight translation of the
// pt-BR list would put bare "purpose" on the English one, which fires on an ordinary sentence
// like "picked on purpose" and teaches everyone to ignore the validator.
const FORBIDDEN_TERMS_BY_LOCALE = {
  "pt-BR": [
    "bencao", "bencaos",
    "proposito", "propositos",
    "jornada de fe",
    "devocional", "devocionais",
    "versiculo do dia", "versiculos do dia",
    "testemunho", "testemunhos",
    "deus tem um plano"
  ],
  en: [
    "blessing", "blessings",
    "faith journey", "journey of faith",
    "devotional", "devotionals",
    "verse of the day", "verses of the day",
    "testimony", "testimonies",
    "quiet time",
    "god has a plan", "god has a purpose",
    "gods plan for your life"
  ]
};

// JSON keys whose values are identifiers, not prose shown to the player.
const IDENTIFIER_KEYS = new Set(["id", "npc", "verse", "source_ref", "palette", "set_flag"]);

const REFERENCE_PATTERN = /^[A-Z0-9]{3}\.\d+(\.\d+)?$/;

/**
 * Keys whose value IS a reference the game resolves, renders as a citation and opens a reader on.
 * Each one owes a verse that resolves and a chapter that was fetched.
 *
 * source_ref is deliberately absent. It records which verse a resident's NAME comes from; it is
 * never rendered, never given a "Saber mais" and never opened, so demanding its chapter would make
 * the build ask for licensed text the game does not show - the exact thing the manifest's own note
 * says not to fetch.
 */
// vigil_verse is the page a night's vigil returns (stages.json), rendered by the morning report
// with the same reference gate and the same way into the reader as a dialogue line's verse.
const CITATION_KEYS = new Set(["verse", "page_verse", "vigil_verse"]);

/**
 * A scripture reference as this repository spells one, found anywhere inside a string rather than
 * anchored to the whole of it: an OSIS book code (three characters, optionally opening with a digit
 * for 1CO), a chapter, and optionally a verse.
 *
 * Deliberately narrower than REFERENCE_PATTERN, whose [A-Z0-9]{3} is safe only because that pattern
 * is anchored. Unanchored, the same alphabet would fire on "123.45" in the middle of a sentence,
 * and a check that cries wolf on ordinary prose is a check people learn to route around.
 */
const EMBEDDED_REFERENCE_PATTERN = /(?:[1-9][A-Z]{2}|[A-Z]{3})\.\d{1,3}(?:\.\d{1,3})?/;

/** Fields of a dialogue node that are structure, not words. These must be identical everywhere. */
const DIALOGUE_STRUCTURE_FIELDS = ["npc", "day", "cutscene", "reliable", "canonical_speaker", "needs_curation"];
const CHOICE_STRUCTURE_FIELDS = ["id", "next", "requires_rubble", "hidden_if_flag"];

/**
 * Parameter names that mean "words a player will read". Any method declaring a string parameter
 * with one of these names becomes a sink, and a literal passed there fails the build.
 *
 * Sinks are DERIVED from the declarations rather than listed, because a listed set only covers the
 * calls someone remembered to list. "Obra" and "Guarda" reached the screen through a local helper
 * that forwarded them to CreateText, and a list of direct UI calls could not see them.
 *
 * A GameObject name is deliberately not in this set: those are English identifiers, never read.
 */
const PLAYER_TEXT_PARAMETER_NAMES = new Set([
  "label", "content", "placeholder", "caption", "title", "prompt", "hint", "message", "body"
]);

/**
 * Fields whose name says they hold words a player reads, initialised from literals.
 *
 * This exists because of a real escape: `static readonly string[] DirectionCaptions = { "frente",
 * ... }` shipped in the English build. It is not a call argument, so a check on call sites could
 * not see it, and it took a screenshot to find. The name is the signal that a check can use.
 */
const PLAYER_STRING_FIELD = /\b(?:const|readonly)\s+string\s*(\[\s*\])?\s+([A-Za-z_]\w*)\s*=\s*(\{[^;]*?\}|"(?:[^"\\]|\\.)*")\s*;/g;

/** Assignments whose right-hand side is read straight off the screen. */
const PLAYER_STRING_ASSIGNMENTS = [
  { pattern: /\.text\s*=\s*("(?:[^"\\]|\\.)*")/g, note: "the text of a label" },
  { pattern: /\bDisplayName\s*=\s*("(?:[^"\\]|\\.)*")/g, note: "the name of a thing in the world" }
];

const errors = [];
const warnings = [];

main();

// ---------------------------------------------------------------- entry point

function main() {
  const options = parseArgs(process.argv.slice(2));

  if (options.help) {
    printUsage();
    process.exit(0);
  }

  const root = options.root;
  console.log("Sheep Gate content validation");
  console.log("  root : " + root);
  console.log("");

  const manifest = loadManifest(join(root, MANIFEST_RELATIVE_PATH));
  const locales = discoverLocales(root);
  console.log("  locales: " + locales.join(", "));
  console.log("");

  for (const locale of locales) {
    console.log("-- " + locale);
    const scripture = loadScripture(versesPathFor(root, locale), locale);

    checkPlaceholderGate(scripture, options.allowPlaceholder, locale);
    checkManifestCoverage(scripture, manifest, locale);
    checkScriptureOverlap(root, scripture, locale);
    checkForbiddenTerms(root, locale);
    checkDialogueReferences(root, scripture, locale);
    checkBareReferences(root, locale);
    console.log("");
  }

  checkLocaleParity(root, locales);
  checkNoHardcodedPlayerStrings(root);
  checkSpeakerNames(root, locales);
  checkLocaleKeysResolve(root, locales);
  checkCurationFlags(root, locales);

  console.log("");
  for (const warning of warnings) {
    console.log("WARN  " + warning);
  }
  for (const error of errors) {
    console.log("FAIL  " + error);
  }

  console.log("");
  console.log(
    errors.length === 0
      ? "PASSED with " + warnings.length + " warning(s)."
      : "FAILED with " + errors.length + " error(s) and " + warnings.length + " warning(s)."
  );

  process.exit(errors.length === 0 ? 0 : 1);
}

// ----------------------------------------------------------------------- args

function parseArgs(argv) {
  const options = {
    allowPlaceholder: false,
    root: DEFAULT_ROOT,
    help: false
  };

  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    switch (argument) {
      case "--allow-placeholder":
        options.allowPlaceholder = true;
        break;
      case "--root": {
        const value = argv[++index];
        if (value === undefined || value.startsWith("--")) {
          abort("Flag --root expects a directory.");
        }
        options.root = isAbsolute(value) ? value : resolve(process.cwd(), value);
        break;
      }
      case "--help":
      case "-h":
        options.help = true;
        break;
      default:
        abort("Unknown argument \"" + argument + "\". Run with --help for usage.");
    }
  }

  return options;
}

function printUsage() {
  const lines = [
    "validate-content.mjs - deterministic layer-1 content validator",
    "",
    "  --allow-placeholder   accept a verses.json generated with --placeholder.",
    "  --root <dir>          repository root to validate (default: the repo this file lives in).",
    "  --help                this text.",
    "",
    "Exit 0 when every check passes (warnings do not fail). Exit 1 on any failure."
  ];
  console.log(lines.join("\n"));
}

// ------------------------------------------------------------------ discovery

/**
 * The shipped locales are the directories under Resources/Data/locales. Discovering them rather
 * than listing them here means adding a language cannot forget to add it to the validator - the
 * check that would have caught the mistake would have been the one nobody updated.
 */
function discoverLocales(root) {
  const directory = join(root, LOCALES_RELATIVE_DIR);
  if (!existsSync(directory)) {
    abort("No locales directory at " + directory + ".");
  }

  const locales = readdirSync(directory)
    .filter((entry) => {
      try {
        return statSync(join(directory, entry)).isDirectory();
      } catch (error) {
        return false;
      }
    })
    .sort();

  if (locales.length === 0) {
    abort("No locales found under " + directory + ".");
  }
  if (!locales.includes(SOURCE_LOCALE)) {
    abort("The source locale " + SOURCE_LOCALE + " is missing from " + directory + ".");
  }

  // Source first, so every later locale can be compared against an already-loaded authority.
  return [SOURCE_LOCALE, ...locales.filter((locale) => locale !== SOURCE_LOCALE)];
}

/**
 * Every JSON file a player can read in this locale: the shared content under Resources/Data plus
 * this locale's own files, walked to the bottom of the tree.
 *
 * RECURSIVE MATTERS MORE THAN IT LOOKS. This one list feeds three checks - forbidden terms, verse
 * resolution and chapter access - so the readdirSync of a single directory level that used to stand
 * here meant that grouping content into a subfolder would drop it out of all three at once,
 * silently, and silently as a PASS. Nine stages add files. The day someone tidies them into a
 * folder must not be the day the checks quietly stop looking.
 *
 * The other locales' directories are skipped rather than walked. Each locale's prose is measured
 * against its own forbidden-term checklist and its own verses.json, so reading pt-BR while
 * validating en would test Portuguese against an English list and resolve its references in an
 * English scripture file: noise in one direction, false confidence in the other. A stray file
 * directly under locales/ belongs to no locale and is skipped by the same rule.
 */
function playerFacingJsonFiles(root, locale) {
  const localesRoot = join(root, LOCALES_RELATIVE_DIR);
  const thisLocale = localeDir(root, locale);

  const files = [];
  for (const filePath of walkFiles(join(root, DATA_RELATIVE_DIR))) {
    if (extname(filePath).toLowerCase() !== ".json") continue;
    if (isVersesFile(filePath)) continue;
    if (isInside(filePath, localesRoot) && !isInside(filePath, thisLocale)) continue;
    files.push(filePath);
  }

  return files.sort();
}

/** True when a path sits inside a directory, rather than merely starting with the same letters. */
function isInside(filePath, directory) {
  const rel = relative(directory, filePath);
  return rel.length > 0 && !rel.startsWith("..") && !isAbsolute(rel);
}

// -------------------------------------------------------------------- loading

function loadScripture(versesPath, locale) {
  if (!existsSync(versesPath)) {
    abort(
      "verses.json not found at " + versesPath + ". " +
      "Generate it first: node tools/fetch-verses.mjs --locale " + locale + " --placeholder"
    );
  }

  let document;
  try {
    document = JSON.parse(readFileSync(versesPath, "utf8"));
  } catch (error) {
    abort("verses.json is not valid JSON - " + error.message);
  }

  return {
    path: versesPath,
    locale,
    isPlaceholder: document.is_placeholder === true,
    version: document.version || {},
    verses: document.verses && typeof document.verses === "object" ? document.verses : {},
    chapters: document.chapters && typeof document.chapters === "object" ? document.chapters : {}
  };
}

function loadManifest(manifestPath) {
  if (!existsSync(manifestPath)) {
    abort("verses.manifest.json not found at " + manifestPath + ".");
  }

  let document;
  try {
    document = JSON.parse(readFileSync(manifestPath, "utf8"));
  } catch (error) {
    abort("verses.manifest.json is not valid JSON - " + error.message);
  }

  return {
    verses: Array.isArray(document.verses) ? document.verses.map(normalizeReference) : [],
    chapters: Array.isArray(document.chapters) ? document.chapters.map(normalizeReference) : []
  };
}

function plural(count, noun) {
  return count + " " + noun + (count === 1 ? "" : "s");
}

function normalizeReference(value) {
  return String(value).trim().toUpperCase();
}

// ------------------------------------------------------------------- checks

function checkPlaceholderGate(scripture, allowPlaceholder, locale) {
  process.stdout.write("  [1/6] placeholder gate      ");
  if (scripture.isPlaceholder && !allowPlaceholder) {
    console.log("FAIL");
    errors.push(
      locale + ": verses.json is a placeholder build (is_placeholder: true) and carries no " +
      "scripture text. Run: node tools/fetch-verses.mjs --locale " + locale + ", or pass " +
      "--allow-placeholder if you are knowingly validating a pre-licence build."
    );
    return;
  }
  console.log(scripture.isPlaceholder ? "OK (placeholder allowed)" : "OK");
}

function checkManifestCoverage(scripture, manifest, locale) {
  process.stdout.write("  [2/6] manifest coverage     ");

  const missing = [];
  const empty = [];

  for (const reference of manifest.verses) {
    const entry = scripture.verses[reference];
    if (!entry) {
      missing.push(reference);
    } else if (typeof entry.text !== "string" || entry.text.trim().length === 0) {
      empty.push(reference);
    }
  }

  for (const chapterRef of manifest.chapters) {
    const chapter = scripture.chapters[chapterRef];
    if (!chapter || !Array.isArray(chapter.verses) || chapter.verses.length === 0) {
      missing.push(chapterRef);
      continue;
    }
    for (const verse of chapter.verses) {
      if (!verse || typeof verse.text !== "string" || verse.text.trim().length === 0) {
        empty.push(chapterRef + "." + (verse && verse.n !== undefined ? verse.n : "?"));
      }
    }
  }

  if (missing.length === 0 && empty.length === 0) {
    console.log(
      "OK (" + plural(manifest.verses.length, "verse") + ", " +
      plural(manifest.chapters.length, "chapter") + ")"
    );
    return;
  }

  console.log("FAIL");
  if (missing.length > 0) {
    errors.push(locale + ": missing from verses.json: " + missing.join(", "));
  }
  if (empty.length > 0) {
    errors.push(locale + ": empty text in verses.json: " + empty.join(", "));
  }
}

function checkScriptureOverlap(root, scripture, locale) {
  process.stdout.write("  [3/6] scripture overlap     ");

  const scriptureNgrams = buildScriptureNgrams(scripture);
  if (scriptureNgrams.size === 0) {
    console.log("SKIPPED (no scripture long enough to form " + NGRAM_SIZE + "-word runs)");
    return;
  }

  const failHits = [];
  for (const directory of FAIL_SCAN_DIRS) {
    collectOverlapHits(join(root, directory), root, scriptureNgrams, failHits);
  }

  const warnHits = [];
  for (const directory of WARN_SCAN_DIRS) {
    collectOverlapHits(join(root, directory), root, scriptureNgrams, warnHits);
  }

  if (failHits.length === 0) {
    console.log("OK (" + scriptureNgrams.size + " scripture runs indexed)");
  } else {
    console.log("FAIL");
    for (const hit of failHits) {
      errors.push(
        locale + ": scripture-length overlap in " + hit.file + ":" + hit.line + " - " + hit.words +
        " consecutive words also present in that locale's verses.json. Reference the verse " +
        "instead of copying it."
      );
    }
  }

  for (const hit of warnHits) {
    warnings.push(
      locale + ": scripture-length overlap in " + hit.file + ":" + hit.line + " - " + hit.words +
      " consecutive words also present in that locale's verses.json. Design prose may quote; " +
      "build artifacts may not."
    );
  }
}

function checkForbiddenTerms(root, locale) {
  process.stdout.write("  [4/6] forbidden terms       ");

  const terms = FORBIDDEN_TERMS_BY_LOCALE[locale];
  if (!terms) {
    console.log("FAIL");
    errors.push(
      "No forbidden-term checklist for locale \"" + locale + "\". Add one to " +
      "FORBIDDEN_TERMS_BY_LOCALE in tools/validate-content.mjs, curated for that language " +
      "rather than translated from another."
    );
    return;
  }

  const hits = [];
  for (const filePath of playerFacingJsonFiles(root, locale)) {
    let document;
    try {
      document = JSON.parse(readFileSync(filePath, "utf8"));
    } catch (error) {
      errors.push(relative(root, filePath) + " is not valid JSON - " + error.message);
      continue;
    }

    walkStrings(document, "", (path, key, value) => {
      if (IDENTIFIER_KEYS.has(key) || REFERENCE_PATTERN.test(value.trim())) {
        return;
      }
      const words = tokenize(value).map((token) => token.word);
      for (const term of terms) {
        if (containsSequence(words, term.split(" "))) {
          hits.push({ file: relative(root, filePath), path, term });
        }
      }
    });
  }

  if (hits.length === 0) {
    console.log("OK");
  } else {
    console.log("FAIL");
    for (const hit of hits) {
      errors.push(
        locale + ": forbidden term \"" + hit.term + "\" in a player-facing string: " +
        hit.file + " at " + (hit.path || "(root)") + "."
      );
    }
  }
  console.log("      note: verses.json is exempt from this check - it is licensed translation");
  console.log("      text, not our authored voice, and is not ours to rewrite.");
}

function checkDialogueReferences(root, scripture, locale) {
  process.stdout.write("  [5/6] referenced verses     ");

  const unresolved = new Set();

  // Every citation carries a "Saber mais" button, and that button opens the whole CHAPTER the
  // verse lives in - not the verse. So a chapter absent from verses.json is a dead button.
  //
  // This is an error and not a warning, unlike a missing verse, because of how it fails: a missing
  // verse shows the unavailable-text marker inline, where anyone reading the line sees it. A
  // missing chapter renders the citation perfectly and breaks only for the player who taps. It
  // shipped that way - only NEH.4 was ever fetched, so seven of the game's nine citations had a
  // dead button, including the first scripture a player ever sees.
  //
  // Two rules ride on this, which is why it stops the build. CLAUDE.md rule 12: the citation may
  // be deferred, the access never. And deep_read - the north-star metric the whole product exists
  // to move - can only fire from inside the reader this button opens.
  const missingChapters = new Map();

  for (const filePath of playerFacingJsonFiles(root, locale)) {
    let document;
    try {
      document = JSON.parse(readFileSync(filePath, "utf8"));
    } catch (error) {
      continue; // already reported by the forbidden-term pass
    }

    walkStrings(document, "", (path, key, value) => {
      // Every key that renders as a citation, not just the dialogue line's. A Pagina carries its
      // reference under page_verse, and that panel is where the reveal happens and where the first
      // "Saber mais" of the run is tapped - the single door the north-star metric fires through.
      // Checking only "verse" left the most load-bearing citation in the build unchecked.
      if (!CITATION_KEYS.has(key)) {
        return;
      }
      const reference = normalizeReference(value);
      if (!scripture.verses[reference]) {
        unresolved.add(reference);
      }

      // Mirrors SheepGate.Scripture.ScriptureService.ChapterRefOf: book and chapter, dropping
      // the verse. If the two ever disagree the reader looks somewhere this check does not.
      const parts = reference.split(".");
      if (parts.length < 3) {
        return;
      }
      const chapterRef = parts[0] + "." + parts[1];
      const chapter = scripture.chapters && scripture.chapters[chapterRef];
      if (!chapter || !Array.isArray(chapter.verses) || chapter.verses.length === 0) {
        if (!missingChapters.has(chapterRef)) {
          missingChapters.set(chapterRef, new Set());
        }
        missingChapters.get(chapterRef).add(reference);
      }
    });
  }

  if (unresolved.size === 0 && missingChapters.size === 0) {
    console.log("OK");
    return;
  }

  console.log("FAIL");

  if (missingChapters.size > 0) {
    const detail = [...missingChapters.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([chapterRef, refs]) => chapterRef + " (cited by " + [...refs].sort().join(", ") + ")")
      .join("; ");
    errors.push(
      locale + ": cited verses whose chapter is not in verses.json: " + detail +
      ". Their \"Saber mais\" opens an empty chapter. Add the chapter to the \"chapters\" list in " +
      "tools/verses.manifest.json and re-run node tools/fetch-verses.mjs."
    );
  }

  // A missing verse used to be a warning, and at nine citations that was defensible: the marker is
  // visible inline, so a run of the game would show it to whoever looked. A season of thirty-five
  // citations is not something anybody looks at end to end, and a warning in a passing build is a
  // line of console output nobody reads twice. The two halves of one citation - the verse and the
  // chapter behind its button - now fail the same way, which is also the honest description of
  // what they are.
  if (unresolved.size > 0) {
    errors.push(
      locale + ": referenced but absent from verses.json: " + [...unresolved].sort().join(", ") +
      ". Add them to the \"verses\" list in tools/verses.manifest.json and re-run node " +
      "tools/fetch-verses.mjs, or the player reads the unavailable-text marker where the " +
      "quotation should be."
    );
  }
}

/**
 * Fails when a player-facing string writes a scripture reference into its own prose.
 *
 * Chapter and verse reach the screen exactly one way: a citation resolves the reference out of
 * verses.json, ScriptureVisibility decides whether the ref_display footer under it is drawn yet,
 * and a "Saber mais" beside it opens the whole chapter in the internal reader. A "NEH.4.2." typed
 * into a sentence goes round all three. It shows chapter-and-verse whether the reveal has happened
 * or not, and it offers nothing to tap - which is the one half of rule 12 that has no exception:
 * the citation may be deferred, the ACCESS never.
 *
 * This is not hypothetical. Two day-1 quiz notes ended in a bare reference, in both locales,
 * printing chapter-and-verse on the first stage of the run, five stages before the reveal, with no
 * way into the reader from there. Every other check passed the whole time it was on screen.
 *
 * The check keys off the KEY, not the file, because a file has no opinion: quiz.json is all prose
 * and dialogue.json is both. A value under one of CITATION_KEYS is a reference doing its job.
 * A reference under any other key is one that got out.
 *
 * WHAT IT DOES NOT CATCH, stated so nobody mistakes it for a complete gate: this finds the code
 * form, NEH.4.2. A book name written out in the locale's own language followed by numbers is the
 * same leak and is invisible here, because the book names are per-language prose and matching them
 * would fire on any sentence that mentions the man. That one stays a human judgement, and the
 * curation read is where it gets made.
 */
function checkBareReferences(root, locale) {
  process.stdout.write("  [6/6] bare references       ");

  const hits = [];
  for (const filePath of playerFacingJsonFiles(root, locale)) {
    let document;
    try {
      document = JSON.parse(readFileSync(filePath, "utf8"));
    } catch (error) {
      continue; // already reported by the forbidden-term pass
    }

    walkStrings(document, "", (path, key, value) => {
      if (CITATION_KEYS.has(key) || IDENTIFIER_KEYS.has(key)) {
        return;
      }
      const match = EMBEDDED_REFERENCE_PATTERN.exec(value);
      if (match !== null) {
        hits.push({ file: relative(root, filePath), path, reference: match[0] });
      }
    });
  }

  if (hits.length === 0) {
    console.log("OK");
    return;
  }

  console.log("FAIL");
  for (const hit of hits) {
    errors.push(
      locale + ": the reference " + hit.reference + " is written into a player-facing string - " +
      hit.file + " at " + (hit.path || "(root)") + ". A citation is gated by ScriptureVisibility " +
      "and carries a way into the reader; prose that names a reference does neither. Take it out " +
      "of the sentence, or - if this key genuinely holds a reference the game resolves - add the " +
      "key to CITATION_KEYS in tools/validate-content.mjs so it is checked as one."
    );
  }
}

// ------------------------------------------------------------- locale parity

/**
 * Every locale owes the same strings as the source locale, and its dialogue must agree with the
 * source about everything that is not words.
 *
 * The reason this matters more than tidiness: grants, flags and verse references live inside
 * dialogue.json, and dialogue.json is copied per language. A translator who drops a choice or
 * renumbers a line changes what the game *does* in that language, silently. Structure is compared
 * field by field so that can only be a build failure, never a shipped one.
 */
function checkLocaleParity(root, locales) {
  process.stdout.write("[7/11] locale parity          ");

  const others = locales.filter((locale) => locale !== SOURCE_LOCALE);
  if (others.length === 0) {
    console.log("SKIPPED (only the source locale is present)");
    return;
  }

  const before = errors.length;

  // WHICH FILES ARE COMPARED IS DERIVED, NEVER LISTED. The list that used to sit here named five
  // files and the locale directory held seven, so catalog.json - every wardrobe string in the game -
  // had no parity gate at all: a missing English translation shipped as a bare key on a panel, with
  // nothing but a console line behind it. A list is a second place to remember, and this one was
  // already behind. Whatever the authoring locale carries is what the other locales owe.
  //
  // Two exclusions, each for its own reason. verses.json is generated per locale from one shared
  // manifest and is licensed text, not a translation of ours. dialogue.json is excluded here only
  // because compareDialogue below reads it far more strictly than key equality ever could.
  const parityFiles = localeFileNames(localeDir(root, SOURCE_LOCALE))
    .filter((name) => name !== "verses.json" && name !== "dialogue.json");

  if (parityFiles.length === 0) {
    console.log("FAIL");
    errors.push(
      "Nothing to compare in " + relative(root, localeDir(root, SOURCE_LOCALE)) + ". This check " +
      "derives its file list from that directory, so an empty one is not a pass - it is the check " +
      "covering nothing."
    );
    return;
  }

  for (const fileName of parityFiles) {
    const source = readJson(root, join(localeDir(root, SOURCE_LOCALE), fileName));
    if (source === null) continue;

    for (const locale of others) {
      const target = readJson(root, join(localeDir(root, locale), fileName));
      if (target === null) {
        errors.push(locale + ": missing " + fileName + ".");
        continue;
      }
      compareKeys(fileName, locale, source, target, "");
    }
  }

  const sourceDialogue = readJson(root, join(localeDir(root, SOURCE_LOCALE), "dialogue.json"));
  if (sourceDialogue !== null) {
    for (const locale of others) {
      const target = readJson(root, join(localeDir(root, locale), "dialogue.json"));
      if (target === null) {
        errors.push(locale + ": missing dialogue.json.");
        continue;
      }
      compareDialogue(locale, sourceDialogue, target);
    }
  }

  console.log(errors.length === before ? "OK" : "FAIL");
}

/** Recursively asserts that two locale files describe the same keys and the same shapes. */
function compareKeys(fileName, locale, source, target, path) {
  const where = (key) => fileName + " at " + (path ? path + "." + key : key);

  if (Array.isArray(source)) {
    if (!Array.isArray(target) || target.length !== source.length) {
      errors.push(
        locale + ": " + fileName + " at " + (path || "(root)") + " has " +
        (Array.isArray(target) ? target.length : "no") + " entries, source has " + source.length + "."
      );
      return;
    }
    source.forEach((child, index) => {
      if (child !== null && typeof child === "object") {
        compareKeys(fileName, locale, child, target[index], path + "[" + index + "]");
      }
    });
    return;
  }

  if (source === null || typeof source !== "object") return;

  for (const [key, value] of Object.entries(source)) {
    if (!(key in target)) {
      errors.push(locale + ": missing " + where(key) + ".");
      continue;
    }
    const translated = target[key];
    if (typeof value === "string") {
      if (typeof translated !== "string" || translated.trim().length === 0) {
        errors.push(locale + ": empty or non-string " + where(key) + ".");
        continue;
      }
      // A {0} that survives in one language and not the other is a crash waiting for the screen
      // that formats it, so placeholders are part of the contract, not part of the prose.
      const sourcePlaceholders = placeholdersIn(value);
      const targetPlaceholders = placeholdersIn(translated);
      if (sourcePlaceholders !== targetPlaceholders) {
        errors.push(
          locale + ": placeholders differ in " + where(key) + " - source has [" +
          (sourcePlaceholders || "none") + "], translation has [" + (targetPlaceholders || "none") + "]."
        );
      }
      continue;
    }
    if (value !== null && typeof value === "object") {
      compareKeys(fileName, locale, value, translated, path ? path + "." + key : key);
    }
  }

  for (const key of Object.keys(target)) {
    if (!(key in source)) {
      errors.push(locale + ": unknown " + where(key) + " (not in " + SOURCE_LOCALE + ").");
    }
  }
}

function placeholdersIn(text) {
  return (String(text).match(/\{\d+\}/g) || []).sort().join(",");
}

function compareDialogue(locale, source, target) {
  for (const id of Object.keys(source)) {
    if (!(id in target)) {
      errors.push(locale + ": dialogue.json is missing node \"" + id + "\".");
      continue;
    }

    const a = source[id];
    const b = target[id];

    for (const field of DIALOGUE_STRUCTURE_FIELDS) {
      if (JSON.stringify(a[field]) !== JSON.stringify(b[field])) {
        errors.push(
          locale + ": dialogue.json node \"" + id + "\" disagrees on " + field +
          " (" + JSON.stringify(b[field]) + " vs " + JSON.stringify(a[field]) + ")."
        );
      }
    }

    // Grants are points and flags. A translation must never be able to change what a node awards.
    if (JSON.stringify(a.grants ?? null) !== JSON.stringify(b.grants ?? null)) {
      errors.push(locale + ": dialogue.json node \"" + id + "\" has different grants from " + SOURCE_LOCALE + ".");
    }

    // The hour a node costs is a rule, not a word: a translation must not price a beat differently.
    if ((a.spend_work ?? 0) !== (b.spend_work ?? 0)) {
      errors.push(locale + ": dialogue.json node \"" + id + "\" has a different spend_work from " + SOURCE_LOCALE + ".");
    }

    const aLines = a.lines ?? [];
    const bLines = b.lines ?? [];
    if (aLines.length !== bLines.length) {
      errors.push(
        locale + ": dialogue.json node \"" + id + "\" has " + bLines.length +
        " line(s), source has " + aLines.length + "."
      );
    } else {
      aLines.forEach((line, index) => {
        const other = bLines[index];
        const at = "node \"" + id + "\" line " + index;
        // A line carries text OR a verse. Which one it is decides whether the player is reading
        // authored prose or scripture, so it may not change between languages.
        if (("verse" in line) !== ("verse" in other) || line.verse !== other.verse) {
          errors.push(locale + ": dialogue.json " + at + " has a different verse reference.");
        }
        if (("text" in line) !== ("text" in other)) {
          errors.push(locale + ": dialogue.json " + at + " swaps authored text for a quotation, or back.");
        }
        if ("text" in other && String(other.text).trim().length === 0) {
          errors.push(locale + ": dialogue.json " + at + " is empty.");
        }
        if ("frame" in line && (!("frame" in other) || String(other.frame).trim().length === 0)) {
          errors.push(locale + ": dialogue.json " + at + " is missing the frame around its quotation.");
        }
      });
    }

    const aChoices = a.choices ?? [];
    const bChoices = b.choices ?? [];
    if (aChoices.length !== bChoices.length) {
      errors.push(
        locale + ": dialogue.json node \"" + id + "\" has " + bChoices.length +
        " choice(s), source has " + aChoices.length + "."
      );
      continue;
    }
    aChoices.forEach((choice, index) => {
      const other = bChoices[index];
      for (const field of CHOICE_STRUCTURE_FIELDS) {
        if (JSON.stringify(choice[field]) !== JSON.stringify(other[field])) {
          errors.push(
            locale + ": dialogue.json node \"" + id + "\" choice " + index + " disagrees on " + field + "."
          );
        }
      }
      if (JSON.stringify(choice.grants ?? null) !== JSON.stringify(other.grants ?? null)) {
        errors.push(locale + ": dialogue.json node \"" + id + "\" choice " + index + " has different grants.");
      }
      if (typeof other.text !== "string" || other.text.trim().length === 0) {
        errors.push(locale + ": dialogue.json node \"" + id + "\" choice " + index + " has no text.");
      }
    });
  }

  for (const id of Object.keys(target)) {
    if (!(id in source)) {
      errors.push(locale + ": dialogue.json has node \"" + id + "\", which " + SOURCE_LOCALE + " does not.");
    }
  }
}

/**
 * Fails on a dialogue speaker with no authored display name in any locale.
 *
 * A speaker id resolves one of two ways: npcs.json, for the builders who exist in the world, or
 * DialogueData.SpeakerStringKeys, for the ones who do not. Neither path covered the narrator, the
 * neighbour, the man from the capital or the crowd, so the raw id reached the bubble and an
 * English build printed "vizinho". Every other check passed while that was on screen, which is
 * why this one exists: the parity check compares locales against each other and cannot see a
 * string that is missing from all of them.
 */
function checkSpeakerNames(root, locales) {
  process.stdout.write("[9/11] speaker names          ");

  const dialoguePath = join(localeDir(root, SOURCE_LOCALE), "dialogue.json");
  const dialogue = readJson(root, dialoguePath);
  if (dialogue === null) {
    console.log("SKIPPED (no dialogue.json for " + SOURCE_LOCALE + ")");
    return;
  }

  const speakers = new Set();
  for (const node of Object.values(dialogue)) {
    // An empty npc is authored on purpose - the bubble hides the row - so it needs no name.
    if (node && typeof node.npc === "string" && node.npc !== "") speakers.add(node.npc);
  }

  const stringKeys = speakerStringKeys(root);
  if (stringKeys === null) {
    console.log("FAIL");
    errors.push(
      "Could not read SpeakerStringKeys from Assets/Scripts/Dialogue/DialogueData.cs. The map " +
      "moved or was renamed; this check cannot pass silently without it."
    );
    return;
  }

  const hits = [];
  for (const speaker of [...speakers].sort()) {
    for (const locale of locales) {
      const names = readJson(root, join(localeDir(root, locale), "npcs.json")) ?? {};
      if (typeof names[speaker] === "string" && names[speaker] !== "") continue;

      const key = stringKeys.get(speaker);
      if (key === undefined) {
        hits.push(speaker + " (" + locale + ") has no entry in npcs.json and no SpeakerStringKeys mapping");
        continue;
      }

      const ui = readJson(root, join(localeDir(root, locale), "ui.json")) ?? {};
      if (typeof ui[key] !== "string" || ui[key] === "") {
        hits.push(speaker + " (" + locale + ") maps to " + key + ", which ui.json does not define");
      }
    }
  }

  if (hits.length === 0) {
    console.log("OK (" + speakers.size + " speaker(s), " + stringKeys.size + " mapped to the string table)");
    return;
  }

  console.log("FAIL");
  for (const hit of hits) {
    errors.push(
      "Dialogue speaker without a display name: " + hit + ". The player would read the raw id, " +
      "which is a word in whichever language it was typed in."
    );
  }
}

/**
 * The id -> string-key map out of DialogueData.cs. Returns null when the initialiser cannot be
 * found at all, so a rename fails the build instead of emptying the check.
 */
function speakerStringKeys(root) {
  const path = join(root, "Assets", "Scripts", "Dialogue", "DialogueData.cs");
  if (!existsSync(path)) return null;

  const content = readFileSync(path, "utf8");
  const block = /SpeakerStringKeys\s*=\s*new\s+Dictionary<string,\s*string>\s*\{([\s\S]*?)\}\s*;/.exec(content);
  if (block === null) return null;

  const found = new Map();
  const entry = /\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}/g;
  let match = entry.exec(block[1]);
  while (match !== null) {
    found.set(match[1], match[2]);
    match = entry.exec(block[1]);
  }

  return found.size === 0 ? null : found;
}

/**
 * Every JSON file in a locale directory, as paths relative to it so a subdirectory still counts.
 * Relative paths rather than bare names because the same string then addresses the file in any
 * other locale, and names the file in an error a person has to go and fix.
 */
function localeFileNames(directory) {
  const names = [];
  for (const filePath of walkFiles(directory)) {
    if (extname(filePath).toLowerCase() !== ".json") continue;
    names.push(relative(directory, filePath));
  }
  return names.sort();
}

function readJson(root, filePath) {
  if (!existsSync(filePath)) return null;
  try {
    return JSON.parse(readFileSync(filePath, "utf8"));
  } catch (error) {
    errors.push(relative(root, filePath) + " is not valid JSON - " + error.message);
    return null;
  }
}

// -------------------------------------------------- hardcoded player strings

/**
 * Fails on any string literal reaching a player-facing sink from C#.
 *
 * This is the check that keeps the rule true over time. An accent-based grep would pass a build
 * whose hardcoded strings happen to be English, which is exactly the state this codebase is one
 * careless edit away from now that it ships a second language.
 */
function checkNoHardcodedPlayerStrings(root) {
  process.stdout.write("[8/11] hardcoded strings      ");

  const sources = [];
  for (const filePath of walkFiles(join(root, "Assets"))) {
    if (extname(filePath).toLowerCase() !== ".cs") continue;
    try {
      sources.push({ file: relative(root, filePath), content: readFileSync(filePath, "utf8") });
    } catch (error) {
      continue;
    }
  }

  // Pass 1: every method that takes words a player reads, and where in its argument list.
  const sinks = new Map();
  for (const source of sources) {
    for (const [name, indices] of findPlayerTextParameters(source.content)) {
      const existing = sinks.get(name) ?? new Set();
      for (const index of indices) existing.add(index);
      sinks.set(name, existing);
    }
  }

  // Pass 2: literals arriving at any of them, plus assignments straight onto a screen.
  const hits = [];
  for (const { file, content } of sources) {
    const callPattern = /\b([A-Za-z_]\w*)\s*\(/g;
    let match = callPattern.exec(content);
    while (match !== null) {
      const indices = sinks.get(match[1]);
      if (indices) {
        const open = match.index + match[0].length - 1;
        const args = splitCallArguments(content, open);
        if (args && !looksLikeDeclaration(args)) {
          for (const index of indices) {
            const argument = args[index];
            if (argument !== undefined && isStringLiteral(argument)) {
              hits.push({
                file,
                line: lineOf(content, open),
                note: "argument " + index + " of " + match[1] + "()",
                value: argument.trim()
              });
            }
          }
        }
      }
      match = callPattern.exec(content);
    }

    for (const assignment of PLAYER_STRING_ASSIGNMENTS) {
      assignment.pattern.lastIndex = 0;
      let hit = assignment.pattern.exec(content);
      while (hit !== null) {
        hits.push({ file, line: lineOf(content, hit.index), note: assignment.note, value: hit[1] });
        hit = assignment.pattern.exec(content);
      }
    }

    PLAYER_STRING_FIELD.lastIndex = 0;
    let field = PLAYER_STRING_FIELD.exec(content);
    while (field !== null) {
      const name = field[2];
      const initialiser = field[3];
      if (namesPlayerText(name) && /"/.test(initialiser)) {
        hits.push({
          file,
          line: lineOf(content, field.index),
          note: "the field " + name + ", which holds words a player reads",
          value: initialiser.replace(/\s+/g, " ").slice(0, 60)
        });
      }
      field = PLAYER_STRING_FIELD.exec(content);
    }
  }

  if (hits.length === 0) {
    console.log("OK (" + sinks.size + " sink(s) derived from declarations)");
    return;
  }

  console.log("FAIL");
  for (const hit of hits) {
    errors.push(
      "Hardcoded player-facing string in " + hit.file + ":" + hit.line + " - " + hit.value +
      " is " + hit.note + ". Move it to Resources/Data/locales/*/ui.json and read it with Loc.T."
    );
  }
}

/**
 * Finds declarations with a string parameter named like player-visible words, and returns
 * method name -> argument positions. Works on the shape of a parameter list rather than on a
 * full C# parse: a declaration is the call-looking thing whose every argument is "type name".
 */
function findPlayerTextParameters(content) {
  const found = new Map();
  const pattern = /\b([A-Za-z_]\w*)\s*\(/g;

  let match = pattern.exec(content);
  while (match !== null) {
    const args = splitCallArguments(content, match.index + match[0].length - 1);
    if (args && looksLikeDeclaration(args)) {
      const indices = new Set();
      args.forEach((parameter, index) => {
        const parts = parameter.trim().split("=")[0].trim().split(/\s+/);
        if (parts.length < 2) return;
        const name = parts[parts.length - 1];
        const type = parts[parts.length - 2];
        if (type === "string" && PLAYER_TEXT_PARAMETER_NAMES.has(name)) indices.add(index);
      });
      if (indices.size > 0) found.set(match[1], indices);
    }
    match = pattern.exec(content);
  }

  return found;
}

/** True when every argument reads as "Type name" - i.e. this is a declaration, not a call. */
function looksLikeDeclaration(args) {
  const meaningful = args.map((a) => a.trim()).filter((a) => a.length > 0);
  if (meaningful.length === 0) return false;
  return meaningful.every((a) => /^(?:this\s+|params\s+|ref\s+|out\s+)?[\w<>\[\],\.\?]+\s+[A-Za-z_]\w*(\s*=\s*[^,]+)?$/.test(a));
}

/**
 * Splits a C# argument list into top-level arguments, given the index of its opening paren.
 * Nesting and string literals are respected, so a call spread over ten lines with a lambda in it
 * still yields the right argument in the content position. Returns null on an unbalanced call.
 */
function splitCallArguments(content, openParenIndex) {
  const args = [];
  let depth = 0;
  let start = openParenIndex + 1;
  let inString = false;
  let inChar = false;
  let escaped = false;

  for (let index = openParenIndex; index < content.length; index += 1) {
    const character = content[index];

    if (escaped) { escaped = false; continue; }
    if (inString || inChar) {
      if (character === "\\") escaped = true;
      else if (inString && character === "\"") inString = false;
      else if (inChar && character === "'") inChar = false;
      continue;
    }

    if (character === "\"") { inString = true; continue; }
    if (character === "'") { inChar = true; continue; }

    if (character === "(" || character === "[" || character === "{") {
      depth += 1;
      continue;
    }
    if (character === ")" || character === "]" || character === "}") {
      depth -= 1;
      if (depth === 0) {
        args.push(content.slice(start, index));
        return args;
      }
      continue;
    }
    if (character === "," && depth === 1) {
      args.push(content.slice(start, index));
      start = index + 1;
    }
  }

  return null;
}

/**
 * True when an identifier reads as player-visible words: Caption, DirectionCaptions, BodyLabel.
 * A trailing "Key" or "Keys" is the opposite signal and is deliberately excluded - a field of
 * locale keys is the fix, not the bug.
 */
function namesPlayerText(name) {
  if (/keys?$/i.test(name)) return false;
  const lower = name.toLowerCase();
  for (const noun of PLAYER_TEXT_PARAMETER_NAMES) {
    if (lower.endsWith(noun) || lower.endsWith(noun + "s")) return true;
  }
  return false;
}

/** True when an argument is nothing but a literal string - "x", or "x" + "y". */
function isStringLiteral(argument) {
  const trimmed = argument.trim();
  if (!trimmed.startsWith("\"")) return false;
  return /^"(?:[^"\\]|\\.)*"(\s*\+\s*"(?:[^"\\]|\\.)*")*$/.test(trimmed);
}

// -------------------------------------------------------- overlap machinery

function buildScriptureNgrams(scripture) {
  const ngrams = new Set();

  // One text unit at a time: concatenating verses would invent runs that
  // span a verse boundary and never actually appear in the translation.
  for (const entry of Object.values(scripture.verses)) {
    addNgrams(ngrams, entry && entry.text);
  }
  for (const chapter of Object.values(scripture.chapters)) {
    if (!chapter || !Array.isArray(chapter.verses)) {
      continue;
    }
    for (const verse of chapter.verses) {
      addNgrams(ngrams, verse && verse.text);
    }
  }

  return ngrams;
}

function addNgrams(target, text) {
  if (typeof text !== "string") {
    return;
  }
  const words = tokenize(text).map((token) => token.word);
  for (let index = 0; index + NGRAM_SIZE <= words.length; index += 1) {
    target.add(words.slice(index, index + NGRAM_SIZE).join(" "));
  }
}

function collectOverlapHits(directory, root, scriptureNgrams, output) {
  for (const filePath of walkFiles(directory)) {
    // Every locale's verses file, not just this locale's: one is the source of the runs being
    // searched for, and the others are licensed text nobody is being asked to rewrite either.
    if (isVersesFile(filePath)) {
      continue;
    }
    if (!TEXT_EXTENSIONS.has(extname(filePath).toLowerCase())) {
      continue;
    }

    let content;
    try {
      content = readFileSync(filePath, "utf8");
    } catch (error) {
      continue;
    }

    for (const hit of findOverlaps(content, scriptureNgrams)) {
      output.push({
        file: relative(root, filePath),
        line: hit.line,
        words: hit.words
      });
    }
  }
}

function findOverlaps(content, scriptureNgrams) {
  const tokens = tokenize(content);
  const hits = [];
  if (tokens.length < NGRAM_SIZE) {
    return hits;
  }

  let index = 0;
  while (index + NGRAM_SIZE <= tokens.length && hits.length < MAX_REPORTED_MATCHES_PER_FILE) {
    const gram = gramAt(tokens, index);
    if (!scriptureNgrams.has(gram)) {
      index += 1;
      continue;
    }

    // Grow the run as far as the overlap continues, so the report says how bad it is.
    let length = NGRAM_SIZE;
    while (
      index + length < tokens.length &&
      scriptureNgrams.has(gramAt(tokens, index + length - NGRAM_SIZE + 1))
    ) {
      length += 1;
    }

    hits.push({
      line: lineOf(content, tokens[index].index),
      words: length
    });
    index += length;
  }

  return hits;
}

function gramAt(tokens, start) {
  const parts = [];
  for (let offset = 0; offset < NGRAM_SIZE; offset += 1) {
    parts.push(tokens[start + offset].word);
  }
  return parts.join(" ");
}

function lineOf(content, characterIndex) {
  let line = 1;
  for (let index = 0; index < characterIndex && index < content.length; index += 1) {
    if (content.charCodeAt(index) === 10) {
      line += 1;
    }
  }
  return line;
}

// --------------------------------------------------------------- text helpers

// Case, accents and punctuation are all normalized away before comparing, so a
// reformatted or unaccented copy of a verse is still recognized as a copy.
function tokenize(text) {
  const tokens = [];
  const pattern = /[\p{L}\p{N}]+/gu;
  let match = pattern.exec(text);
  while (match !== null) {
    tokens.push({ word: normalizeWord(match[0]), index: match.index });
    match = pattern.exec(text);
  }
  return tokens;
}

function normalizeWord(word) {
  return word.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
}

function containsSequence(words, sequence) {
  if (sequence.length === 0 || words.length < sequence.length) {
    return false;
  }
  for (let index = 0; index + sequence.length <= words.length; index += 1) {
    let matched = true;
    for (let offset = 0; offset < sequence.length; offset += 1) {
      if (words[index + offset] !== sequence[offset]) {
        matched = false;
        break;
      }
    }
    if (matched) {
      return true;
    }
  }
  return false;
}

function walkStrings(node, path, visit) {
  if (typeof node === "string") {
    return;
  }
  if (Array.isArray(node)) {
    node.forEach((child, position) => {
      const childPath = path + "[" + position + "]";
      if (typeof child === "string") {
        visit(childPath, lastKeyOf(path), child);
      } else {
        walkStrings(child, childPath, visit);
      }
    });
    return;
  }
  if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const childPath = path.length === 0 ? key : path + "." + key;
      if (typeof value === "string") {
        visit(childPath, key, value);
      } else {
        walkStrings(value, childPath, visit);
      }
    }
  }
}

function lastKeyOf(path) {
  const withoutIndex = path.replace(/\[\d+\]$/, "");
  const parts = withoutIndex.split(".");
  return parts[parts.length - 1] || "";
}

// ------------------------------------------------------- locale keys resolve

/**
 * Fails when C# asks Loc.T for a key that no ui.json actually carries.
 *
 * This is the check that was missing when the backpack shipped seventeen keys that existed only
 * in the code. Nothing caught it: parity compares the locales against each other, and two tables
 * missing the same key are in perfect agreement. The hardcoded-strings check is the mirror image
 * of this one — it catches words that never became a key — and between them the two say that a
 * key and its text always exist together.
 *
 * A miss here is not cosmetic. Loc.T renders an unknown key as the key itself, so the failure
 * mode is a player reading "backpack.slot.hair" off a panel, in every language at once.
 */
function checkLocaleKeysResolve(root, locales) {
  process.stdout.write("[10/11] locale keys resolve   ");

  // A key, and nothing else: lowercase segments joined by dots. Sprite keys carry no dot, file
  // names do not start with a known namespace, and scripture references are uppercase, so none of
  // the three reach this check.
  const KEY_SHAPE = /^[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+$/;

  const sources = [];
  for (const filePath of walkFiles(join(root, "Assets"))) {
    if (extname(filePath).toLowerCase() !== ".cs") continue;
    try {
      sources.push({ file: relative(root, filePath), content: readFileSync(filePath, "utf8") });
    } catch (error) {
      continue;
    }
  }

  // Pass 1: string constants, including the ones built by concatenating another constant with a
  // suffix. Wardrobe spells its refusals that way — KeyRefusalPrefix + "locked" — and reading only
  // bare literals would see "backpack.refusal." and "locked" and never the key itself.
  const constants = new Map();
  const CONST_LITERAL = /\bconst\s+string\s+(\w+)\s*=\s*"((?:[^"\\]|\\.)*)"\s*;/g;
  const CONST_CONCAT = /\bconst\s+string\s+(\w+)\s*=\s*(\w+)\s*\+\s*"((?:[^"\\]|\\.)*)"\s*;/g;
  for (const { content } of sources) {
    CONST_LITERAL.lastIndex = 0;
    let hit = CONST_LITERAL.exec(content);
    while (hit !== null) {
      constants.set(hit[1], { value: hit[2] });
      hit = CONST_LITERAL.exec(content);
    }
    CONST_CONCAT.lastIndex = 0;
    hit = CONST_CONCAT.exec(content);
    while (hit !== null) {
      constants.set(hit[1], { base: hit[2], suffix: hit[3] });
      hit = CONST_CONCAT.exec(content);
    }
  }
  const resolveConstant = (name, depth) => {
    if (depth > 8) return null;
    const entry = constants.get(name);
    if (!entry) return null;
    if (entry.value !== undefined) return entry.value;
    const base = resolveConstant(entry.base, depth + 1);
    return base === null ? null : base + entry.suffix;
  };

  // Pass 2: every key-shaped literal in the tree, plus every key-shaped constant, with the line
  // it sits on so a failure names somewhere to go.
  const referenced = new Map();
  const remember = (key, file, line) => {
    if (!KEY_SHAPE.test(key) || referenced.has(key)) return;
    referenced.set(key, { file, line });
  };
  for (const { file, content } of sources) {
    const LITERAL = /"((?:[^"\\\n]|\\.)*)"/g;
    let hit = LITERAL.exec(content);
    while (hit !== null) {
      remember(hit[1], file, lineOf(content, hit.index));
      hit = LITERAL.exec(content);
    }
    CONST_CONCAT.lastIndex = 0;
    hit = CONST_CONCAT.exec(content);
    while (hit !== null) {
      const resolved = resolveConstant(hit[1], 0);
      if (resolved !== null) remember(resolved, file, lineOf(content, hit.index));
      hit = CONST_CONCAT.exec(content);
    }
  }

  // Only keys whose namespace the table already uses. A key-shaped literal that shares no first
  // segment with anything in ui.json is something else wearing the same punctuation, and guessing
  // otherwise would fail the build over an asset path.
  const tables = new Map();
  const namespaces = new Set();
  for (const locale of locales) {
    const table = readJson(root, "Assets/Resources/Data/locales/" + locale + "/ui.json");
    if (!table) continue;
    tables.set(locale, table);
    for (const key of Object.keys(table)) namespaces.add(key.split(".")[0]);
  }

  if (tables.size === 0) {
    console.log("SKIP (no ui.json found)");
    return;
  }

  // Loc.Plural is handed a stem and appends ".one" or ".other" itself, so the stem is never a key
  // and both of its branches always are. Checking the stem would fail every plural in the game;
  // checking neither branch would let a half-translated plural through.
  const pluralStems = new Map();
  const PLURAL_CALL = /\bLoc\.Plural\s*\(\s*"((?:[^"\\\n]|\\.)*)"/g;
  for (const { file, content } of sources) {
    PLURAL_CALL.lastIndex = 0;
    let hit = PLURAL_CALL.exec(content);
    while (hit !== null) {
      if (!pluralStems.has(hit[1])) {
        pluralStems.set(hit[1], { file, line: lineOf(content, hit.index) });
      }
      hit = PLURAL_CALL.exec(content);
    }
  }

  const missing = [];
  const check = (key, where) => {
    const absent = [];
    for (const [locale, table] of tables) {
      if (!Object.prototype.hasOwnProperty.call(table, key)) absent.push(locale);
    }
    if (absent.length > 0) missing.push({ key, where, absent });
  };

  for (const [key, where] of referenced) {
    if (!namespaces.has(key.split(".")[0])) continue;
    if (pluralStems.has(key)) continue;
    check(key, where);
  }
  for (const [stem, where] of pluralStems) {
    if (!namespaces.has(stem.split(".")[0])) continue;
    check(stem + ".one", where);
    check(stem + ".other", where);
  }

  if (missing.length === 0) {
    console.log("OK (" + plural(referenced.size, "key") + " referenced from C#, " +
                plural(pluralStems.size, "plural") + ")");
    return;
  }

  console.log("FAIL");
  for (const { key, where, absent } of missing) {
    errors.push(
      'ui.json has no "' + key + '", asked for at ' + where.file + ":" + where.line +
      " (missing in " + absent.join(", ") + "). Loc.T renders an unknown key as the key itself, " +
      "so this reaches the player as raw text."
    );
  }
}

/**
 * Fails on a dialogue node marked canonical_speaker without needs_curation.
 *
 * Rule 4 lets a figure the text actually names - Sanballat, Tobiah, the governor - be given
 * authored words, on one condition: a person reads those words against the passage first and
 * decides whether they assert an event, a motive or a claim it does not carry. No script can make
 * that judgement, and this one does not try. What a script CAN do is guarantee the judgement was
 * queued, and tools/list-curation.mjs builds its queue from needs_curation and nothing else.
 *
 * So the hole this closes is a quiet one, and quiet in the worst direction. A node flagged
 * canonical_speaker and nothing else is invisible to the queue; the queue then comes back short,
 * which reads exactly like a queue that has been worked through; and a named figure ships saying
 * something nobody weighed. Nine stages put authored speech in three canonical mouths, so one
 * forgotten flag is the entire safeguard.
 *
 * Every locale is checked, not only the authoring one. A translation of a canonical figure's
 * speech is newly authored speech in that language and owes its own read.
 */
function checkCurationFlags(root, locales) {
  process.stdout.write("[11/11] curation flags        ");

  const hits = [];
  let queued = 0;

  for (const locale of locales) {
    // A missing dialogue.json is the parity check's failure to report, not this one's. Saying it
    // twice would only make the real message harder to find.
    const dialogue = readJson(root, join(localeDir(root, locale), "dialogue.json"));
    if (dialogue === null) continue;

    for (const [id, node] of Object.entries(dialogue)) {
      if (!node || node.canonical_speaker !== true) continue;
      if (node.needs_curation === true) {
        queued += 1;
        continue;
      }
      hits.push(locale + " node \"" + id + "\" (speaker: " + (node.npc || "(none)") + ")");
    }
  }

  if (hits.length === 0) {
    console.log("OK (" + plural(queued, "node") + " queued for a human read)");
    return;
  }

  console.log("FAIL");
  for (const hit of hits) {
    errors.push(
      "canonical_speaker without needs_curation: " + hit + ". A figure the text names is being " +
      "given words, and tools/list-curation.mjs queues by needs_curation alone, so this node would " +
      "never reach the read rule 4 requires - and the short queue would read as a finished one. " +
      "Add \"needs_curation\": true in every locale."
    );
  }
}

function* walkFiles(directory) {
  let entries;
  try {
    entries = readdirSync(directory);
  } catch (error) {
    return;
  }

  for (const entry of entries.sort()) {
    if (SKIP_DIRECTORIES.has(entry)) {
      continue;
    }
    const fullPath = join(directory, entry);
    let stats;
    try {
      stats = statSync(fullPath);
    } catch (error) {
      continue;
    }
    if (stats.isDirectory()) {
      yield* walkFiles(fullPath);
    } else if (stats.isFile()) {
      yield fullPath;
    }
  }
}

function abort(message) {
  console.error("ERROR: " + message);
  process.exit(1);
}
