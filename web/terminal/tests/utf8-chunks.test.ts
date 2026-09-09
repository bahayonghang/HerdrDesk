import assert from "node:assert/strict";
import test from "node:test";
import { Utf8ChunkAssembler, asWriteBytes } from "../src/utf8.ts";

test("cross-chunk CJK does not insert U+FFFD or duplicate", () => {
  const assembler = new Utf8ChunkAssembler();
  assembler.append(Uint8Array.from([0xe4]));
  assert.equal(assembler.heldIncomplete, true);
  assert.equal(assembler.text, "");
  assembler.append(Uint8Array.from([0xbd, 0xa0, 0xe5, 0xa5, 0xbd]));
  assert.equal(assembler.text, "你好");
  assert.equal(assembler.text.includes("\uFFFD"), false);
  assert.notEqual(assembler.text, "你好你好");
  assert.equal(assembler.heldIncomplete, false);
});

test("emoji split across chunks stays one scalar", () => {
  const assembler = new Utf8ChunkAssembler();
  const grin = new TextEncoder().encode("😀");
  assembler.append(grin.subarray(0, 2));
  assembler.append(grin.subarray(2));
  assert.equal(assembler.text, "😀");
  assert.equal(assembler.text.includes("\uFFFD"), false);
});

test("control bytes stay bytes and are not rewritten as text", () => {
  const written = asWriteBytes(Uint8Array.from([0x03, 0x1b]));
  assert.deepEqual(Array.from(written), [0x03, 0x1b]);
  const asText = new TextDecoder("utf-8").decode(written);
  assert.equal(asText.includes("^C"), false);
});
