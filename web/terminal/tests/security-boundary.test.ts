import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { classifyLink, parseEnvelope } from "../src/protocol.ts";
import { sourceContainsForbidden } from "../src/security.ts";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");

test("unknown exec and rpc kinds are rejected", () => {
  const epoch = 1;
  assert.equal(
    parseEnvelope({ version: 1, kind: "host.exec", epoch }, epoch).code,
    "unknown_web_message_type",
  );
  assert.equal(
    parseEnvelope({ version: 1, kind: "rpc.call", epoch }, epoch).code,
    "unknown_web_message_type",
  );
});

test("javascript and file URIs are denied", () => {
  assert.equal(classifyLink("javascript:alert(1)", true).code, "link_scheme_denied");
  assert.equal(classifyLink("file:///tmp/x", true).code, "link_scheme_denied");
  assert.equal(classifyLink("https://example.com", false).code, "link_gesture_required");
  assert.equal(classifyLink("https://example.com", true).allowed, true);
});

test("HTML injection fields are unknown", () => {
  const parsed = parseEnvelope(
    { version: 1, kind: "ready", epoch: 1, html: "<img src=x onerror=alert(1)>" },
    1,
  );
  assert.equal(parsed.code, "unknown_web_message_field");
});

test("ANSI is not concatenated into HTML in sources", () => {
  const files = [
    "src/terminal.ts",
    "src/protocol.ts",
    "src/ime.ts",
    "src/index.html",
    "scripts/build.mjs",
  ];
  for (const rel of files) {
    const text = fs.readFileSync(path.join(root, rel), "utf8");
    assert.equal(sourceContainsForbidden(text), null, rel);
    assert.equal(text.includes("GetString"), false, rel);
  }
});

test("observe path gates user input, emulator reply, and resize", () => {
  const text = fs.readFileSync(path.join(root, "src", "terminal.ts"), "utf8");
  assert.match(text, /onData\([\s\S]*?readOnly/);
  assert.match(text, /onBinary\([\s\S]*?readOnly/);
  assert.match(text, /onResize\([\s\S]*?readOnly/);
  assert.match(text, /suppressData/);
  assert.match(text, /compositionstart/);
});

test("dist host scripts are browser JavaScript", () => {
  const remnants = [
    " as {",
    ": void",
    "interface ",
    "private leftover",
    ": unknown",
    ": number",
    ": string",
    "Record<string",
  ];
  for (const rel of ["dist/terminal.js", "dist/protocol.js", "dist/utf8.js", "dist/ime.js"]) {
    const text = fs.readFileSync(path.join(root, rel), "utf8");
    for (const marker of remnants) {
      assert.equal(text.includes(marker), false, rel + " " + marker);
    }
  }
});
