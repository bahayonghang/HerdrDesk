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

                       
                                      
                                                                                    
 

                         
                        
 

const chromeObj = (globalThis                              ).chrome;
const host = chromeObj?.webview;
let term                  = null;
let boundEpoch = 0;
let readOnly = true;
let readyPosted = false;

function post(message                         )       {
  if (!host) {
    return;
  }
  host.postMessage(message);
}

function fault(code        )       {
  if (boundEpoch <= 0) {
    return;
  }
  post({ version: SCHEMA_VERSION, kind: KINDS.fault, epoch: boundEpoch, code });
}

function applyTheme(theme        )       {
  document.documentElement.dataset.theme = theme === "light" ? "light" : "dark";
}

function applyDisplay(fontFamily        , fontSize        , zoomPercent        )       {
  const size = Math.max(8, fontSize) * (Math.max(50, zoomPercent) / 100);
  document.documentElement.style.setProperty("--terminal-font-family", fontFamily);
  document.documentElement.style.setProperty("--terminal-font-size", `${size}px`);
  if (term) {
    term.options.fontFamily = fontFamily;
    term.options.fontSize = size;
  }
}

function resetTerminal()       {
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
      activate(event            , text        ) {
        onLink(text, event.isTrusted);
      },
      allowNonHttpProtocols: false,
    },
  });
  if (hostEl) {
    term.open(hostEl);
  }
  term.onData((data        ) => {
    if (readOnly || boundEpoch <= 0) {
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
  term.onBinary((data        ) => {
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
  term.onResize((size                                ) => {
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

function handleHostMessage(raw         )       {
  if (boundEpoch <= 0 && isInitialize(raw)) {
    const epoch = (raw                     ).epoch;
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
    const body = raw                                                       ;
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
    const body = raw                                                                 ;
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

function isInitialize(raw         )          {
  return typeof raw === "object" && raw !== null && (raw                     ).kind === KINDS.initialize;
}

function onLink(uri        , userGesture         )       {
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

void LOCAL_ORIGIN;
