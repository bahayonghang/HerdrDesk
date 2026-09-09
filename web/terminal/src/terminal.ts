import { Terminal } from "./xterm.mjs";
import {
  KINDS,
  LOCAL_ORIGIN,
  ORIGINS,
  SCHEMA_VERSION,
  classifyLink,
  encodeCanonicalBase64,
  parseEnvelope,
} from "./protocol.js";
import { asWriteBytes } from "./utf8.js";
import { createCompositionGate, isImeShortcut } from "./ime.js";

interface WebViewHost {
  postMessage(message: unknown): void;
  addEventListener(name: string, handler: (event: { data: unknown }) => void): void;
}

interface ChromeWebView {
  webview?: WebViewHost;
}

const chromeObj = (globalThis as { chrome?: ChromeWebView }).chrome;
const host = chromeObj?.webview;
let term: Terminal | null = null;
let boundEpoch = 0;
let readOnly = true;
let readyPosted = false;
const composition = createCompositionGate();

function post(message: Record<string, unknown>): void {
  if (!host) {
    return;
  }
  host.postMessage(message);
}

function fault(code: string): void {
  if (boundEpoch <= 0) {
    return;
  }
  post({ version: SCHEMA_VERSION, kind: KINDS.fault, epoch: boundEpoch, code });
}

function applyTheme(theme: string): void {
  document.documentElement.dataset.theme = theme === "light" ? "light" : "dark";
}

function applyDisplay(fontFamily: string, fontSize: number, zoomPercent: number): void {
  const size = Math.max(8, fontSize) * (Math.max(50, zoomPercent) / 100);
  document.documentElement.style.setProperty("--terminal-font-family", fontFamily);
  document.documentElement.style.setProperty("--terminal-font-size", `${size}px`);
  if (term) {
    term.options.fontFamily = fontFamily;
    term.options.fontSize = size;
  }
}

function resetTerminal(): void {
  term?.dispose();
  const hostEl = document.getElementById("terminal");
  if (hostEl) {
    hostEl.replaceChildren();
  }
  term = new Terminal({
    scrollback: 0,
    convertEol: false,
    disableStdin: readOnly,
    allowProposedApi: false,
    fontFamily: getComputedStyle(document.documentElement).getPropertyValue("--terminal-font-family").trim() ||
      "Consolas, Cascadia Mono, monospace",
    fontSize: 14,
    linkHandler: {
      activate(event: MouseEvent, text: string) {
        onLink(text, event.isTrusted);
      },
      allowNonHttpProtocols: false,
    },
  });
  if (hostEl) {
    term.open(hostEl);
  }
  term.onData((data: string) => {
    if (readOnly || boundEpoch <= 0) {
      return;
    }
    if (composition.suppressData(data)) {
      return;
    }
    const bytes = new TextEncoder().encode(data);
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.input,
      epoch: boundEpoch,
      origin: ORIGINS.user_key,
      bytes: encodeCanonicalBase64(bytes),
    });
  });
  term.onBinary((data: string) => {
    if (readOnly || boundEpoch <= 0) {
      return;
    }
    const bytes = new Uint8Array(data.length);
    for (let i = 0; i < data.length; i++) {
      bytes[i] = data.charCodeAt(i) & 0xff;
    }
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.input,
      epoch: boundEpoch,
      origin: ORIGINS.emulator_reply,
      bytes: encodeCanonicalBase64(bytes),
    });
  });
  term.onSelectionChange(() => {
    if (boundEpoch <= 0 || !term) {
      return;
    }
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.selectionChanged,
      epoch: boundEpoch,
      visibleText: term.getSelection(),
      shift: false,
    });
  });
  term.onResize((size: { cols: number; rows: number }) => {
    if (readOnly || boundEpoch <= 0) {
      return;
    }
    const dims = term?.options.fontSize ? [Math.round(term.options.fontSize / 2), Math.round(term.options.fontSize)] : [8, 16];
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.resize,
      epoch: boundEpoch,
      cols: size.cols,
      rows: size.rows,
      cellPx: dims,
    });
  });
}

function handleHostMessage(raw: unknown): void {
  if (boundEpoch <= 0 && isInitialize(raw)) {
    const epoch = (raw as { epoch: number }).epoch;
    const parsedInit = parseEnvelope(raw, epoch);
    if (!parsedInit.accepted) {
      return;
    }
    boundEpoch = epoch;
  }
  const parsed = parseEnvelope(raw, boundEpoch);
  if (!parsed.accepted) {
    fault(parsed.code);
    return;
  }
  if (parsed.kind === KINDS.initialize) {
    const body = raw as { theme: string; readOnly: boolean; epoch: number };
    boundEpoch = body.epoch;
    readOnly = body.readOnly === true;
    applyTheme(body.theme);
    resetTerminal();
    if (!readyPosted) {
      readyPosted = true;
      post({ version: SCHEMA_VERSION, kind: KINDS.ready, epoch: boundEpoch });
    }
    return;
  }
  if (parsed.kind === KINDS.display) {
    const body = raw as { fontFamily: string; fontSize: number; zoomPercent: number };
    applyDisplay(body.fontFamily, body.fontSize, body.zoomPercent);
    return;
  }
  if (parsed.kind === KINDS.focus) {
    term?.focus();
    return;
  }
  if (parsed.kind === KINDS.dispose) {
    term?.dispose();
    term = null;
    readyPosted = false;
    return;
  }
  if (parsed.kind === KINDS.frame) {
    if (!term || !parsed.bytes || parsed.sequence === undefined) {
      fault("terminal_stream_not_active");
      return;
    }
    const bytes = asWriteBytes(parsed.bytes);
    const seq = parsed.sequence;
    term.write(bytes, () => {
      post({
        version: SCHEMA_VERSION,
        kind: KINDS.parsed,
        epoch: boundEpoch,
        seq,
        bytesConsumed: bytes.length,
      });
    });
  }
}

function isInitialize(raw: unknown): boolean {
  return typeof raw === "object" && raw !== null && (raw as { kind?: string }).kind === KINDS.initialize;
}

function onLink(uri: string, userGesture: boolean): void {
  const decision = classifyLink(uri, userGesture);
  if (!decision.allowed || boundEpoch <= 0) {
    return;
  }
  post({
    version: SCHEMA_VERSION,
    kind: KINDS.linkRequest,
    epoch: boundEpoch,
    uri,
    userGesture,
  });
}

if (host) {
  host.addEventListener("message", (event) => {
    handleHostMessage(event.data);
  });
}

document.addEventListener("click", (event) => {
  const target = event.target;
  if (!(target instanceof HTMLAnchorElement)) {
    return;
  }
  event.preventDefault();
  onLink(target.href, event.isTrusted);
});

document.addEventListener("compositionstart", () => {
  if (boundEpoch <= 0) {
    return;
  }
  const token = composition.start();
  post({
    version: SCHEMA_VERSION,
    kind: KINDS.composition,
    epoch: boundEpoch,
    phase: "start",
    token,
  });
}, true);

document.addEventListener("compositionupdate", () => {
  if (boundEpoch <= 0 || !composition.update()) {
    return;
  }
  post({
    version: SCHEMA_VERSION,
    kind: KINDS.composition,
    epoch: boundEpoch,
    phase: "update",
    token: composition.token,
  });
}, true);

document.addEventListener("compositionend", (event: CompositionEvent) => {
  if (boundEpoch <= 0 || !composition.composing) {
    return;
  }
  const ended = composition.end(event.data || "");
  if (ended.cancel) {
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.composition,
      epoch: boundEpoch,
      phase: "cancel",
      token: ended.token,
    });
    return;
  }
  post({
    version: SCHEMA_VERSION,
    kind: KINDS.composition,
    epoch: boundEpoch,
    phase: "end",
    token: ended.token,
    text: ended.text,
  });
}, true);

document.addEventListener("keydown", (event: KeyboardEvent) => {
  if (boundEpoch <= 0) {
    return;
  }
  const ctrl = event.ctrlKey === true;
  const shift = event.shiftKey === true;
  const alt = event.altKey === true;
  if (composition.composing) {
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.key,
      epoch: boundEpoch,
      key: event.key,
      ctrl,
      shift,
      alt,
      altGr: event.getModifierState("AltGraph"),
      capsLock: event.getModifierState("CapsLock"),
    });
    if (isImeShortcut(event.key, ctrl, alt, shift)) {
      event.stopPropagation();
    }
    return;
  }
  if (ctrl && shift && event.key.toLowerCase() === "c") {
    post({
      version: SCHEMA_VERSION,
      kind: KINDS.key,
      epoch: boundEpoch,
      key: "c",
      ctrl: true,
      shift: true,
      alt: false,
      altGr: false,
      capsLock: false,
    });
    event.preventDefault();
  }
}, true);

document.addEventListener("paste", (event: ClipboardEvent) => {
  if (boundEpoch <= 0) {
    return;
  }
  event.preventDefault();
  const text = event.clipboardData ? event.clipboardData.getData("text/plain") : "";
  post({
    version: SCHEMA_VERSION,
    kind: KINDS.pasteIntent,
    epoch: boundEpoch,
    text,
  });
}, true);

document.addEventListener("wheel", (event: WheelEvent) => {
  if (boundEpoch <= 0) {
    return;
  }
  post({
    version: SCHEMA_VERSION,
    kind: KINDS.mouseIntent,
    epoch: boundEpoch,
    action: "scroll",
    delta: event.deltaY === 0 ? 0 : event.deltaY > 0 ? 1 : -1,
    shift: event.shiftKey === true,
  });
}, { capture: true, passive: true });

void LOCAL_ORIGIN;
