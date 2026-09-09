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
]);

export const LOCAL_ORIGIN = "https://herddesk.terminal.local";

                                                         
                                                         
                                                        
                                                                     

                           
                  
               
                
                         
 

const BASE_FIELDS = new Set(["version", "kind", "epoch"]);
const FIELDS                                    = {
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
};

                              
                    
               
               
                
                     
                    
                 
                  
               
                        
 

export function decodeCanonicalBase64(encoded        , maxBytes        )             {
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

export function encodeCanonicalBase64(bytes            )         {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

export function parseEnvelope(value         , boundEpoch        )              {
  if (boundEpoch <= 0) {
    return reject("stale_epoch");
  }
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return reject("object_required");
  }
  const root = value                           ;
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
  const extra = FIELDS[kind];
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

function parseKind(kind        , root                         , epoch        )              {
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
  return { accepted: true, code: "allowed", kind, epoch };
}

export function classifyLink(uri                    , userGesture         )                                     {
  if (!userGesture) {
    return { allowed: false, code: "link_gesture_required" };
  }
  if (typeof uri !== "string" || uri.length === 0) {
    return { allowed: false, code: "link_scheme_denied" };
  }
  let parsed     ;
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

function reject(code        )              {
  return { accepted: false, code, kind: "", epoch: 0 };
}

function asString(value         )         {
  if (typeof value !== "string") {
    throw new Error("string_field_required");
  }
  return value;
}

function requirePositiveInt(value         )         {
  if (typeof value !== "number" || !Number.isInteger(value) || value <= 0) {
    throw new Error("malformed_web_message");
  }
  return value;
}
