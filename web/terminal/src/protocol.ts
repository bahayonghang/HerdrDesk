export const SCHEMA_VERSION = 1;

export const KINDS = Object.freeze({
  initialize: "initialize",
  frame: "frame",
  focus: "focus",
  dispose: "dispose",
  display: "display",
  ready: "ready",
  parsed: "parsed",
  input: "input",
  resize: "resize",
  linkRequest: "linkRequest",
  fault: "fault",
  composition: "composition",
  key: "key",
  pasteIntent: "pasteIntent",
  selectionChanged: "selectionChanged",
  mouseIntent: "mouseIntent",
});

export const ORIGINS = Object.freeze({
  user_key: "user_key",
  committed_text: "committed_text",
  explicit_paste: "explicit_paste",
  emulator_reply: "emulator_reply",
});

export const HOST_TO_WEB = Object.freeze([
  KINDS.initialize,
  KINDS.frame,
  KINDS.focus,
  KINDS.dispose,
  KINDS.display,
]);

export const WEB_TO_HOST = Object.freeze([
  KINDS.ready,
  KINDS.parsed,
  KINDS.input,
  KINDS.resize,
  KINDS.linkRequest,
  KINDS.fault,
  KINDS.composition,
  KINDS.key,
  KINDS.pasteIntent,
  KINDS.selectionChanged,
  KINDS.mouseIntent,
]);

export const LOCAL_ORIGIN = "https://herddesk.terminal.local";

export type HostToWebKind = (typeof HOST_TO_WEB)[number];
export type WebToHostKind = (typeof WEB_TO_HOST)[number];
export type MessageKind = HostToWebKind | WebToHostKind;
export type InputOriginName = (typeof ORIGINS)[keyof typeof ORIGINS];

export interface Envelope {
  version: number;
  kind: string;
  epoch: number;
  [key: string]: unknown;
}

const BASE_FIELDS = new Set(["version", "kind", "epoch"]);
const FIELDS: Record<string, readonly string[]> = {
  [KINDS.initialize]: ["theme", "readOnly"],
  [KINDS.frame]: ["seq", "full", "bytes"],
  [KINDS.focus]: ["token"],
  [KINDS.dispose]: [],
  [KINDS.display]: ["fontFamily", "fontSize", "zoomPercent"],
  [KINDS.ready]: [],
  [KINDS.parsed]: ["seq", "bytesConsumed"],
  [KINDS.input]: ["origin", "bytes"],
  [KINDS.resize]: ["cols", "rows", "cellPx"],
  [KINDS.linkRequest]: ["uri", "userGesture"],
  [KINDS.fault]: ["code"],
  [KINDS.composition]: ["phase", "token"],
  [KINDS.key]: ["key", "ctrl", "shift", "alt", "altGr", "capsLock"],
  [KINDS.pasteIntent]: ["text"],
  [KINDS.selectionChanged]: ["visibleText", "shift"],
  [KINDS.mouseIntent]: ["action", "delta", "shift"],
};

export interface ParseResult {
  accepted: boolean;
  code: string;
  kind: string;
  epoch: number;
  bytes?: Uint8Array;
  sequence?: number;
  full?: boolean;
  origin?: string;
  uri?: string;
  userGesture?: boolean;
}

export function decodeCanonicalBase64(encoded: string, maxBytes: number): Uint8Array {
  if (typeof encoded !== "string" || encoded.length === 0) {
    throw new Error("web_message_bytes_limit");
  }
  const binary = atob(encoded);
  if (binary.length <= 0 || binary.length > maxBytes) {
    throw new Error("web_message_bytes_limit");
  }
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  if (btoa(binary) !== encoded) {
    throw new Error("noncanonical_base64");
  }
  return bytes;
}

export function encodeCanonicalBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

export function parseEnvelope(value: unknown, boundEpoch: number): ParseResult {
  if (boundEpoch <= 0) {
    return reject("stale_epoch");
  }
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return reject("object_required");
  }
  const root = value as Record<string, unknown>;
  const names = Object.keys(root);
  if (new Set(names).size !== names.length) {
    return reject("duplicate_json_key");
  }
  if (root.version !== SCHEMA_VERSION) {
    return reject("unsupported_web_message_version");
  }
  if (typeof root.kind !== "string") {
    return reject("unknown_web_message_type");
  }
  const kind = root.kind;
  const extra = extraFields(kind, root);
  if (extra === undefined) {
    return reject("unknown_web_message_type");
  }
  if (typeof root.epoch !== "number" || !Number.isInteger(root.epoch) || root.epoch <= 0) {
    return reject("stale_epoch");
  }
  if (root.epoch !== boundEpoch) {
    return reject("stale_epoch");
  }
  for (const name of names) {
    if (!BASE_FIELDS.has(name) && !extra.includes(name)) {
      return reject("unknown_web_message_field");
    }
  }
  for (const name of extra) {
    if (!(name in root)) {
      return reject("malformed_web_message");
    }
  }
  try {
    return parseKind(kind, root, boundEpoch);
  } catch (error) {
    const code = error instanceof Error ? error.message : "malformed_web_message";
    return reject(code);
  }
}

function parseKind(kind: string, root: Record<string, unknown>, epoch: number): ParseResult {
  if (kind === KINDS.frame) {
    const seq = requirePositiveInt(root.seq);
    if (typeof root.full !== "boolean") {
      throw new Error("boolean_full_required");
    }
    const bytes = decodeCanonicalBase64(asString(root.bytes), 8 * 1024 * 1024);
    return { accepted: true, code: "allowed", kind, epoch, bytes, sequence: seq, full: root.full };
  }
  if (kind === KINDS.input) {
    const origin = asString(root.origin);
    if (!(origin in ORIGINS)) {
      throw new Error("input_origin_denied");
    }
    const bytes = decodeCanonicalBase64(asString(root.bytes), 64 * 1024);
    return { accepted: true, code: "allowed", kind, epoch, bytes, origin };
  }
  if (kind === KINDS.parsed) {
    return {
      accepted: true,
      code: "allowed",
      kind,
      epoch,
      sequence: requirePositiveInt(root.seq),
    };
  }
  if (kind === KINDS.linkRequest) {
    const uri = asString(root.uri);
    if (typeof root.userGesture !== "boolean") {
      throw new Error("malformed_web_message");
    }
    return {
      accepted: true,
      code: "allowed",
      kind,
      epoch,
      uri,
      userGesture: root.userGesture,
    };
  }
  if (kind === KINDS.fault) {
    const code = asString(root.code);
    if (!/^[a-z][a-z0-9_]{0,63}$/.test(code)) {
      throw new Error("malformed_web_message");
    }
    return { accepted: true, code: "allowed", kind, epoch };
  }
  if (kind === KINDS.composition) {
    const phase = asString(root.phase);
    if (phase !== "start" && phase !== "update" && phase !== "end" && phase !== "cancel") {
      throw new Error("malformed_web_message");
    }
    const token = asString(root.token);
    if (!/^[a-z][a-z0-9_]{0,63}$/.test(token)) {
      throw new Error("malformed_web_message");
    }
    if (phase === "end") {
      asString(root.text);
    }
    return { accepted: true, code: "allowed", kind, epoch };
  }
  if (kind === KINDS.key) {
    const key = asString(root.key);
    if (key.length === 0 || key.length > 32) {
      throw new Error("malformed_web_message");
    }
    requireBoolean(root.ctrl);
    requireBoolean(root.shift);
    requireBoolean(root.alt);
    requireBoolean(root.altGr);
    requireBoolean(root.capsLock);
    return { accepted: true, code: "allowed", kind, epoch };
  }
  if (kind === KINDS.pasteIntent) {
    asString(root.text);
    return { accepted: true, code: "allowed", kind, epoch };
  }
  if (kind === KINDS.selectionChanged) {
    asString(root.visibleText);
    requireBoolean(root.shift);
    return { accepted: true, code: "allowed", kind, epoch };
  }
  if (kind === KINDS.mouseIntent) {
    const action = asString(root.action);
    if (action !== "scroll" && action !== "drag") {
      throw new Error("malformed_web_message");
    }
    if (typeof root.delta !== "number" || !Number.isInteger(root.delta)) {
      throw new Error("malformed_web_message");
    }
    requireBoolean(root.shift);
    return { accepted: true, code: "allowed", kind, epoch };
  }
  return { accepted: true, code: "allowed", kind, epoch };
}

function extraFields(kind: string, root: Record<string, unknown>): readonly string[] | undefined {
  if (kind === KINDS.composition) {
    if (root.phase === "end") {
      return ["phase", "token", "text"];
    }
    return ["phase", "token"];
  }
  return FIELDS[kind];
}

function requireBoolean(value: unknown): boolean {
  if (value !== true && value !== false) {
    throw new Error("malformed_web_message");
  }
  return value;
}

export function classifyLink(uri: string | undefined, userGesture: boolean): { allowed: boolean; code: string } {
  if (!userGesture) {
    return { allowed: false, code: "link_gesture_required" };
  }
  if (typeof uri !== "string" || uri.length === 0) {
    return { allowed: false, code: "link_scheme_denied" };
  }
  let parsed: URL;
  try {
    parsed = new URL(uri);
  } catch {
    return { allowed: false, code: "link_scheme_denied" };
  }
  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    return { allowed: false, code: "link_scheme_denied" };
  }
  return { allowed: true, code: "allowed" };
}

function reject(code: string): ParseResult {
  return { accepted: false, code, kind: "", epoch: 0 };
}

function asString(value: unknown): string {
  if (typeof value !== "string") {
    throw new Error("string_field_required");
  }
  return value;
}

function requirePositiveInt(value: unknown): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value <= 0) {
    throw new Error("malformed_web_message");
  }
  return value;
}
