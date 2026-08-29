#!/usr/bin/env node
// tools/validate-content.mjs
//
// Deterministic layer-1 content validator. Runs outside Unity, Node 20+, zero deps.
//
// Fails the build (exit 1) when:
//   1. a manifest reference is missing from verses.json, or its text is empty;
//   2. verses.json is a placeholder build and --allow-placeholder was not passed;
//   3. a file under Assets/ or tools/ repeats 8+ consecutive words that also appear in
//      verses.json - an accidental paraphrase or a hand-copied verse;
//   4. a player-facing string in Assets/Resources/Data/*.json uses a forbidden term.
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

const VERSES_RELATIVE_PATH = join("Assets", "Resources", "Data", "verses.json");
const MANIFEST_RELATIVE_PATH = join("tools", "verses.manifest.json");
const DATA_RELATIVE_DIR = join("Assets", "Resources", "Data");

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

// Checklist from CLAUDE.md section 13, stored deaccented and lowercase because the
// comparison runs on normalized tokens. Plurals included: the rule is about the word,
// not the spelling.
const FORBIDDEN_TERMS = [
  "bencao", "bencaos",
  "proposito", "propositos",
  "jornada de fe",
  "devocional", "devocionais",
  "versiculo do dia", "versiculos do dia",
  "testemunho", "testemunhos",
  "deus tem um plano"
];

// JSON keys whose values are identifiers, not prose shown to the player.
const IDENTIFIER_KEYS = new Set(["id", "npc", "verse", "source_ref", "palette", "set_flag"]);

const REFERENCE_PATTERN = /^[A-Z0-9]{3}\.\d+(\.\d+)?$/;

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

  const versesPath = join(root, VERSES_RELATIVE_PATH);
  const manifestPath = join(root, MANIFEST_RELATIVE_PATH);

  const scripture = loadScripture(versesPath);
  const manifest = loadManifest(manifestPath);

  checkPlaceholderGate(scripture, options.allowPlaceholder);
  checkManifestCoverage(scripture, manifest);
  checkScriptureOverlap(root, scripture);
  checkForbiddenTerms(root);
  checkDialogueReferences(root, scripture);

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

// -------------------------------------------------------------------- loading

function loadScripture(versesPath) {
  if (!existsSync(versesPath)) {
    abort(
      "verses.json not found at " + versesPath + ". " +
      "Generate it first: node tools/fetch-verses.mjs --placeholder"
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

function checkPlaceholderGate(scripture, allowPlaceholder) {
  process.stdout.write("[1/5] placeholder gate        ");
  if (scripture.isPlaceholder && !allowPlaceholder) {
    console.log("FAIL");
    errors.push(
      "verses.json is a placeholder build (is_placeholder: true) and carries no scripture text. " +
      "Run: node tools/fetch-verses.mjs --provider youversion, or pass --allow-placeholder " +
      "if you are knowingly validating a pre-licence build."
    );
    return;
  }
  console.log(scripture.isPlaceholder ? "OK (placeholder allowed)" : "OK");
}

function checkManifestCoverage(scripture, manifest) {
  process.stdout.write("[2/5] manifest coverage       ");

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
    errors.push("Missing from verses.json: " + missing.join(", "));
  }
  if (empty.length > 0) {
    errors.push("Empty text in verses.json: " + empty.join(", "));
  }
}

function checkScriptureOverlap(root, scripture) {
  process.stdout.write("[3/5] scripture overlap       ");

  const scriptureNgrams = buildScriptureNgrams(scripture);
  if (scriptureNgrams.size === 0) {
    console.log("SKIPPED (no scripture long enough to form " + NGRAM_SIZE + "-word runs)");
    return;
  }

  const failHits = [];
  for (const directory of FAIL_SCAN_DIRS) {
    collectOverlapHits(join(root, directory), root, scriptureNgrams, scripture.path, failHits);
  }

  const warnHits = [];
  for (const directory of WARN_SCAN_DIRS) {
    collectOverlapHits(join(root, directory), root, scriptureNgrams, scripture.path, warnHits);
  }

  if (failHits.length === 0) {
    console.log("OK (" + scriptureNgrams.size + " scripture runs indexed)");
  } else {
    console.log("FAIL");
    for (const hit of failHits) {
      errors.push(
        "Scripture-length overlap in " + hit.file + ":" + hit.line + " - " + hit.words +
        " consecutive words also present in verses.json. Reference the verse instead of copying it."
      );
    }
  }

  for (const hit of warnHits) {
    warnings.push(
      "Scripture-length overlap in " + hit.file + ":" + hit.line + " - " + hit.words +
      " consecutive words also present in verses.json. Design prose may quote; build artifacts may not."
    );
  }
}

function checkForbiddenTerms(root) {
  process.stdout.write("[4/5] forbidden terms         ");

  const dataDirectory = join(root, DATA_RELATIVE_DIR);
  if (!existsSync(dataDirectory)) {
    console.log("SKIPPED (no " + DATA_RELATIVE_DIR + ")");
    return;
  }

  const hits = [];
  for (const entry of readdirSync(dataDirectory)) {
    if (extname(entry).toLowerCase() !== ".json" || entry === "verses.json") {
      continue;
    }

    const filePath = join(dataDirectory, entry);
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
      for (const term of FORBIDDEN_TERMS) {
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
        "Forbidden term \"" + hit.term + "\" in a player-facing string: " +
        hit.file + " at " + (hit.path || "(root)") + "."
      );
    }
  }
  console.log("      note: verses.json is exempt from this check - it is licensed translation");
  console.log("      text, not our authored voice, and is not ours to rewrite.");
}

function checkDialogueReferences(root, scripture) {
  process.stdout.write("[5/5] referenced verses       ");

  const dataDirectory = join(root, DATA_RELATIVE_DIR);
  if (!existsSync(dataDirectory)) {
    console.log("SKIPPED (no " + DATA_RELATIVE_DIR + ")");
    return;
  }

  const unresolved = new Set();
  for (const entry of readdirSync(dataDirectory)) {
    if (extname(entry).toLowerCase() !== ".json" || entry === "verses.json") {
      continue;
    }

    const filePath = join(dataDirectory, entry);
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
    });
  }

  if (unresolved.size === 0) {
    console.log("OK");
    return;
  }

  console.log("WARN");
  warnings.push(
    "Referenced but absent from verses.json: " + [...unresolved].sort().join(", ") +
    ". Add them to tools/verses.manifest.json and re-run the fetch, or the player sees the " +
    "unavailable-text marker."
  );
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

function collectOverlapHits(directory, root, scriptureNgrams, versesPath, output) {
  for (const filePath of walkFiles(directory)) {
    if (filePath === versesPath) {
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
