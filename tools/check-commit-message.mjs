#!/usr/bin/env node
// tools/check-commit-message.mjs
//
// Fails a commit message that is not written in English.
//
// Commit messages are read by everyone who ever runs `git log` on this repository, including
// people and tools that do not share a first language, so they follow the same rule as the rest
// of the code (see docs/development-guidelines.md). This is the check that makes the rule true
// rather than aspirational.
//
// Quoting pt-BR is explicitly allowed and is NOT a violation: a message that explains why a line
// of dialogue changed has to be able to name the line. Anything inside "double quotes", 'single
// quotes' or `backticks` is stripped before the message is judged, including across line breaks.
// Two commits already in this repository's history do exactly that, and both must keep passing.
//
//   node tools/check-commit-message.mjs .git/COMMIT_EDITMSG   one message in a file
//   node tools/check-commit-message.mjs --range origin/main..HEAD   every commit in a range
//   git log -1 --format=%B | node tools/check-commit-message.mjs    stdin
//
// Exit 0 when every message checked is English, 1 otherwise.

import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";

// Letters that effectively do not occur in English prose. A message containing one of these is
// not a borderline call, so a single occurrence is enough on its own.
const PORTUGUESE_LETTERS = /[áàâãéêíóôõúüçÁÀÂÃÉÊÍÓÔÕÚÜÇ]/;

// Words that are unambiguously Portuguese: none of them is also an English word. English
// homographs are deliberately absent - "no", "do", "a", "os" and "todo" all mean something in
// English ("todo list"), and including them would make the check fire on English messages.
const PORTUGUESE_WORDS = new Set([
  "nao", "para", "com", "que", "uma", "uns", "umas", "dos", "das",
  "voce", "voces", "este", "esta", "isto", "esse", "essa", "isso",
  "aquele", "aquela", "mais", "menos", "quando", "porque", "tambem",
  "pelo", "pela", "pelos", "pelas", "seu", "sua", "seus", "suas",
  "estao", "sao", "foi", "foram", "tem", "faz", "fazer", "sem",
  "sobre", "entre", "cada", "toda", "todos", "todas", "muito", "muita",
  "ainda", "agora", "depois", "antes", "onde", "como", "quem", "qual",
  "quais", "aqui", "ali", "ser", "ter", "vai", "vem", "pode", "deve",
  "assim", "entao", "mas", "porem", "ate", "desde", "durante", "atraves"
]);

// Verb forms a Portuguese commit subject starts with. A commit subject is an imperative, so the
// first word carries more signal than any other and one of these is enough by itself.
const PORTUGUESE_FIRST_WORDS = new Set([
  "adiciona", "adicionar", "corrige", "corrigir", "remover", "atualiza", "atualizar",
  "cria", "criar", "mover", "ajusta", "ajustar", "muda", "mudar", "arruma", "arrumar",
  "melhora", "melhorar", "implementa", "implementar", "refatora", "refatorar",
  "escreve", "escrever", "documenta", "documentar", "traduz", "traduzir",
  "renomeia", "renomear", "apaga", "apagar", "conserta", "consertar",
  "altera", "alterar", "inclui", "incluir", "permite", "permitir",
  "torna", "tornar", "deixa", "deixar", "usa", "usar", "faz", "fazer",
  "coloca", "colocar", "separa", "separar", "junta", "juntar"
]);

// Minimum distinct dictionary hits before a message is called Portuguese. One is not enough:
// "dos" turns up in "dos and don'ts", and a check that cries wolf gets switched off.
const WORD_THRESHOLD = 2;

main();

function main() {
  const argv = process.argv.slice(2);

  if (argv.includes("--help") || argv.includes("-h")) {
    printUsage();
    process.exit(0);
  }

  const rangeFlag = argv.indexOf("--range");
  const messages = rangeFlag !== -1
    ? readRange(argv[rangeFlag + 1])
    : [{ label: argv[0] ?? "(stdin)", text: readMessage(argv[0]) }];

  let failed = 0;
  for (const message of messages) {
    const verdict = judge(message.text);
    if (verdict.isEnglish) continue;

    failed += 1;
    report(message, verdict);
  }

  if (failed > 0) {
    console.error(
      `\n${failed} commit message(s) are not in English.\n\n` +
      "Commit messages follow the same rule as the code: English, so that everyone who runs\n" +
      "git log can read them. See docs/development-guidelines.md.\n\n" +
      "Quoting pt-BR is fine and is not what failed here - put the quoted content in `backticks`\n" +
      "or \"double quotes\" and it is ignored. The rule is about the sentences around it."
    );
    process.exit(1);
  }

  const noun = messages.length === 1 ? "message" : "messages";
  console.log(`Commit ${noun} in English: ${messages.length} checked, 0 problems.`);
}

// ------------------------------------------------------------------- judgement

function judge(rawText) {
  const prose = stripNonProse(rawText);

  const letters = prose.match(PORTUGUESE_LETTERS);
  if (letters) {
    return {
      isEnglish: false,
      reason: `the letter "${letters[0]}", which does not occur in English prose`
    };
  }

  const subject = firstMeaningfulLine(prose);
  const subjectWords = tokenize(subject);
  if (subjectWords.length > 0 && PORTUGUESE_FIRST_WORDS.has(subjectWords[0])) {
    return {
      isEnglish: false,
      reason: `the subject opens with "${subjectWords[0]}", a Portuguese verb`
    };
  }

  const hits = [...new Set(tokenize(prose).filter((word) => PORTUGUESE_WORDS.has(word)))];
  if (hits.length >= WORD_THRESHOLD) {
    return {
      isEnglish: false,
      reason: `the Portuguese words ${hits.map((w) => `"${w}"`).join(", ")}`
    };
  }

  return { isEnglish: true };
}

/**
 * Removes everything that is not the author's own sentences: git's comment lines, structured
 * trailers, URLs, quoted content, and anything that is an identifier rather than prose.
 *
 * Quoted content goes first and matters most. English prose about Portuguese content is correct
 * and normal in this repository - it is how a commit explains which line of dialogue changed.
 */
function stripNonProse(text) {
  return text
    // Git's own commentary in the editor template.
    .replace(/^#.*$/gm, " ")
    // Quoted spans, across line breaks: this is the sanctioned way to name pt-BR content.
    .replace(/`[^`]*`/gs, " ")
    .replace(/"[^"]*"/gs, " ")
    .replace(/'[^']*'/gs, " ")
    // Structured trailers such as Co-Authored-By and Claude-Session. The key must be hyphenated,
    // so an ordinary sentence opening with "Note:" is still judged as the prose it is.
    .replace(/^[A-Za-z]+(?:-[A-Za-z]+)+:.*$/gm, " ")
    .replace(/https?:\/\/\S+/g, " ")
    // Identifiers, paths and filenames: Locales.cs, tools/e2e.sh, wall_segments.json.
    .replace(/\S*[/\\]\S*/g, " ")
    .replace(/\b\w+\.\w[\w.]*/g, " ")
    .replace(/\b\w+_\w[\w_]*/g, " ")
    .replace(/\b[a-z]+[A-Z]\w*/g, " ");
}

function firstMeaningfulLine(prose) {
  for (const line of prose.split("\n")) {
    if (line.trim().length > 0) return line;
  }
  return "";
}

function tokenize(text) {
  return (text.toLowerCase().match(/[\p{L}]+/gu) ?? []);
}

// --------------------------------------------------------------------- input

function readMessage(path) {
  if (path && !path.startsWith("--")) {
    return readFileSync(path, "utf8");
  }
  return readFileSync(0, "utf8");
}

function readRange(range) {
  if (!range || range.startsWith("--")) {
    abort("--range expects a revision range, for example origin/main..HEAD");
  }

  // A NUL between records, because a commit body can contain anything a person can type.
  const raw = execFileSync("git", ["log", "--format=%H%x1f%B%x00", range], {
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024
  });

  return raw
    .split("\0")
    .map((record) => record.trim())
    .filter((record) => record.length > 0)
    .map((record) => {
      const separator = record.indexOf("\x1f");
      return {
        label: record.slice(0, separator).slice(0, 12),
        text: record.slice(separator + 1)
      };
    });
}

function report(message, verdict) {
  const subject = firstMeaningfulLine(message.text).trim();
  console.error(`\nNOT ENGLISH  ${message.label}`);
  console.error(`  ${subject}`);
  console.error(`  flagged by ${verdict.reason}.`);
}

function printUsage() {
  console.log([
    "check-commit-message.mjs - fail a commit message that is not in English",
    "",
    "  <path>            check the message in this file (what the commit-msg hook passes)",
    "  --range <range>   check every commit in a revision range",
    "  (no argument)     read the message from stdin",
    "",
    "Quoted content - \"double\", 'single' or `backticks` - is stripped before judging, so",
    "English prose quoting pt-BR content passes. Exit 1 on any message that is not English."
  ].join("\n"));
}

function abort(reason) {
  console.error("ERROR: " + reason);
  process.exit(2);
}
