import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { createRequire, stripTypeScriptTypes } from "node:module";
import { fileURLToPath } from "node:url";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const dist = path.join(root, "dist");
const require = createRequire(path.join(root, "package.json"));

const TS_REMNANTS = [
  " as {",
  ": void",
  "interface ",
  "private leftover",
  ": unknown",
  ": number",
  ": string",
  "Record<string",
];

function assertBrowserJs(file) {
  const text = fs.readFileSync(file, "utf8");
  for (const marker of TS_REMNANTS) {
    if (text.includes(marker)) {
      throw new Error("dist_typescript_forbidden: " + path.basename(file) + " " + marker);
    }
  }
  const check = spawnSync(process.execPath, ["--check", file], { encoding: "utf8" });
  if (check.status !== 0) {
    throw new Error("dist js syntax: " + path.basename(file) + "\n" + (check.stderr || check.stdout));
  }
}

function emitJs(name) {
  const source = fs.readFileSync(path.join(root, "src", name), "utf8");
  let js = stripTypeScriptTypes(source);
  js = js
    .replaceAll(' from "./protocol.ts"', ' from "./protocol.js"')
    .replaceAll(' from "./utf8.ts"', ' from "./utf8.js"')
    .replaceAll(' from "./ime.ts"', ' from "./ime.js"');
  const out = path.join(dist, name.replace(/\.ts$/, ".js"));
  fs.writeFileSync(out, js);
  assertBrowserJs(out);
}

function copyXterm() {
  const pkgPath = require.resolve("@xterm/xterm/package.json");
  const pkgDir = path.dirname(pkgPath);
  const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf8"));
  if (pkg.version !== "6.0.0") {
    throw new Error("admitted @xterm/xterm 6.0.0 missing");
  }
  const moduleRel = pkg.module || pkg.exports?.["."]?.import || "lib-headless/index.mjs";
  const candidates = [
    path.join(pkgDir, "lib-headless", "index.mjs"),
    path.join(pkgDir, "lib", "xterm.mjs"),
    path.join(pkgDir, "lib", "xterm.js"),
    path.join(pkgDir, moduleRel),
  ];
  const moduleFile = candidates.find((item) => fs.existsSync(item));
  if (!moduleFile) {
    throw new Error("xterm module file missing");
  }
  const cssFile = path.join(pkgDir, "css", "xterm.css");
  if (!fs.existsSync(cssFile)) {
    throw new Error("xterm css missing");
  }
  fs.copyFileSync(moduleFile, path.join(dist, "xterm.mjs"));
  fs.copyFileSync(cssFile, path.join(dist, "xterm.css"));
  const license = path.join(pkgDir, "LICENSE");
  if (fs.existsSync(license)) {
    fs.copyFileSync(license, path.join(dist, "LICENSE-xterm.txt"));
  }
  return pkg.version;
}

fs.rmSync(dist, { recursive: true, force: true });
fs.mkdirSync(dist, { recursive: true });
const version = copyXterm();
for (const name of ["protocol.ts", "utf8.ts", "ime.ts", "terminal.ts"]) {
  emitJs(name);
}
fs.copyFileSync(path.join(root, "src", "styles.css"), path.join(dist, "styles.css"));
fs.copyFileSync(path.join(root, "src", "index.html"), path.join(dist, "index.html"));
const html = fs.readFileSync(path.join(dist, "index.html"), "utf8");
if (html.includes("cdn") || html.includes("unpkg") || html.includes("jsdelivr")) {
  throw new Error("cdn source forbidden");
}
console.log("bundle_ok xterm=" + version + " origin=https://herddesk.terminal.local");
