import assert from "node:assert/strict";
import test from "node:test";
import { KINDS, parseEnvelope } from "../src/protocol.ts";
import { createCompositionGate, isImeShortcut } from "../src/ime.ts";

test("preedit messages omit composition text", () => {
  const start = parseEnvelope(
    { version: 1, kind: KINDS.composition, epoch: 1, phase: "start", token: "c1" },
    1,
  );
  assert.equal(start.accepted, true);
  const withText = parseEnvelope(
    {
      version: 1,
      kind: KINDS.composition,
      epoch: 1,
      phase: "start",
      token: "c1",
      text: "ni",
    },
    1,
  );
  assert.equal(withText.code, "unknown_web_message_field");
  const update = parseEnvelope(
    { version: 1, kind: KINDS.composition, epoch: 1, phase: "update", token: "c1" },
    1,
  );
  assert.equal(update.accepted, true);
});

test("commit token is accepted once at the web gate", () => {
  const gate = createCompositionGate();
  const token = gate.start();
  assert.equal(gate.composing, true);
  assert.equal(gate.suppressData("ni"), true);
  const ended = gate.end("你");
  assert.equal(ended.token, token);
  assert.equal(ended.cancel, false);
  assert.equal(gate.composing, false);
  assert.equal(gate.suppressData("你"), true);
  assert.equal(gate.suppressData("你"), false);
  const parsed = parseEnvelope(
    {
      version: 1,
      kind: KINDS.composition,
      epoch: 1,
      phase: "end",
      token,
      text: "你",
    },
    1,
  );
  assert.equal(parsed.accepted, true);
});

test("ctrl+k enter escape are IME shortcuts while composing", () => {
  assert.equal(isImeShortcut("k", true, false, false), true);
  assert.equal(isImeShortcut("Enter", false, false, false), true);
  assert.equal(isImeShortcut("Escape", false, false, false), true);
  assert.equal(isImeShortcut("k", true, false, true), false);
  const gate = createCompositionGate();
  gate.start();
  assert.equal(gate.suppressData("k"), true);
});
