#!/usr/bin/env node
// tools/validate-content.mjs
//
// Deterministic layer-1 content validator. Runs outside Unity, Node 20+, zero deps.
//
// Checks 1-5 run once per shipped locale; 6 and 7 run once over the repository.
//
// Fails the build (exit 1) when:
//   1. a manifest reference is missing from a locale's verses.json, or its text is empty;
//   2. a locale's verses.json is a placeholder build and --allow-placeholder was not passed;
//   3. a file under Assets/ or tools/ repeats 8+ consecutive words that also appear in that
//      locale's verses.json - an accidental paraphrase or a hand-copied verse;
//   4. a player-facing string in that locale uses a term from its forbidden checklist;
//   5. a locale is missing a string the source locale has, or its dialogue disagrees with the
//      source locale about anything that is not words - nodes, line counts, verse references,
//      grants, flags;
//   6. a C# file passes a string literal where a player-facing string belongs. Every such string
//      goes through Loc.T so that adding a language is a content change, never a code change.
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
    console.log("");
  }

  checkLocaleParity(root, locales);
  checkNoHardcodedPlayerStrings(root);
  checkSpeakerNames(root, locales);

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

/** Every JSON file a player can read in this locale: the locale's own files plus shared content. */
function playerFacingJsonFiles(root, locale) {
  const files = [];

  for (const directory of [join(root, DATA_RELATIVE_DIR), localeDir(root, locale)]) {
    let entries;
    try {
      entries = readdirSync(directory);
    } catch (error) {
      continue;
    }
    for (const entry of entries.sort()) {
      if (extname(entry).toLowerCase() !== ".json") continue;
      if (entry === "verses.json") continue;
      files.push(join(directory, entry));
    }
  }

  return files;
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
  process.stdout.write("  [1/5] placeholder gate      ");
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
  process.stdout.write("  [2/5] manifest coverage     ");

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
  process.stdout.write("  [3/5] scripture overlap     ");

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
  process.stdout.write("  [4/5] forbidden terms       ");

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
  process.stdout.write("  [5/5] referenced verses     ");

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
      if (key !== "verse") {
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

  if (missingChapters.size > 0) {
    console.log("FAIL");
    const detail = [...missingChapters.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([chapterRef, refs]) => chapterRef + " (cited by " + [...refs].sort().join(", ") + ")")
      .join("; ");
    errors.push(
      locale + ": cited verses whose chapter is not in verses.json: " + detail +
      ". Their \"Saber mais\" opens an empty chapter. Add the chapter to the \"chapters\" list in " +
      "tools/verses.manifest.json and re-run node tools/fetch-verses.mjs."
    );
  } else {
    console.log("WARN");
  }

  if (unresolved.size > 0) {
    warnings.push(
      locale + ": referenced but absent from verses.json: " + [...unresolved].sort().join(", ") +
      ". Add them to tools/verses.manifest.json and re-run the fetch, or the player sees the " +
      "unavailable-text marker."
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
  process.stdout.write("[6/8] locale parity           ");

  const others = locales.filter((locale) => locale !== SOURCE_LOCALE);
  if (others.length === 0) {
    console.log("SKIPPED (only the source locale is present)");
    return;
  }

  const before = errors.length;

  for (const fileName of ["ui.json", "npcs.json", "vocations.json", "contest.json", "quiz.json"]) {
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
  process.stdout.write("[8/8] speaker names           ");

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
  process.stdout.write("[7/8] hardcoded strings       ");

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
