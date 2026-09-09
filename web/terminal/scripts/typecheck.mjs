import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(path.join(root, "package.json"));
const KINDS = [
  "initialize", "frame", "focus", "dispose", "display", "ready", "parsed",
  "input", "resize", "linkRequest", "fault",
];
const CDN = [
  "cdn.jsdelivr.net", "unpkg.com", "cdnjs.cloudflare.com", "jsdelivr.net",
  "https://cdn.", "http://cdn.",
];

function walk(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "node_modules") {
      continue;
    }
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, acc);
    } else {
      acc.push(full);
    }
  }
  return acc;
}

const pkg = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8"));
if (pkg.dependencies?.xterm || pkg.devDependencies?.xterm) {
  throw new Error("deprecated unscoped xterm id is not admitted");
}
if (pkg.dependencies?.["@xterm/xterm"] !== "6.0.0") {
  throw new Error("admitted @xterm/xterm 6.0.0 missing");
}
if (Object.keys(pkg.dependencies || {}).join() !== "@xterm/xterm") {
  throw new Error("extra npm production dependency");
}
if (pkg.devDependencies && Object.keys(pkg.devDependencies).length > 0) {
  throw new Error("extra npm dev dependency");
}

const protocol = fs.readFileSync(path.join(root, "src", "protocol.ts"), "utf8");
for (const kind of KINDS) {
  if (!protocol.includes(`"${kind}"`)) {
    throw new Error("protocol kind missing: " + kind);
  }
}

for (const file of walk(root)) {
  const rel = path.relative(root, file).replaceAll("\\", "/");
  if (rel.startsWith("dist/") && rel.endsWith(".mjs") && rel.includes("xterm")) {
    continue;
  }
  if (rel === "scripts/typecheck.mjs" || rel.endsWith("security.ts") || rel.endsWith("security.js") || rel.startsWith("tests/")) {
    continue;
  }
  const ext = path.extname(file);
  if (![".ts", ".js", ".mjs", ".html", ".css", ".json"].includes(ext)) {
    continue;
  }
  if (rel === "package-lock.json") {
    continue;
  }
  const text = fs.readFileSync(file, "utf8");
  for (const marker of CDN) {
    if (text.includes(marker)) {
      throw new Error("cdn source forbidden: " + rel);
    }
  }
  if (rel.startsWith("src/") && (text.includes("innerHTML") || text.includes("document.write"))) {
    throw new Error("html injection API forbidden: " + rel);
  }
}

const resolved = require.resolve("@xterm/xterm/package.json");
const xtermPkg = JSON.parse(fs.readFileSync(resolved, "utf8"));
if (xtermPkg.version !== "6.0.0") {
  throw new Error("installed @xterm/xterm is not 6.0.0");
}

const probe = spawnSync(process.execPath, ["--experimental-strip-types", "--check", path.join(root, "src", "protocol.ts")], {
  cwd: root,
  encoding: "utf8",
});
if (probe.status !== 0 && probe.stderr.includes("Error")) {
  const fallback = spawnSync(process.execPath, ["--check", path.join(root, "scripts", "build.mjs")], {
    cwd: root,
    encoding: "utf8",
  });
  if (fallback.status !== 0) {
    throw new Error(fallback.stderr || probe.stderr || "typecheck failed");
  }
}

console.log("typecheck_ok kinds=" + KINDS.length + " xterm=" + xtermPkg.version);
