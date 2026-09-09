export interface CompositionEnd {
  token: string;
  text: string;
  cancel: boolean;
}

export function createCompositionGate() {
  let composing = false;
  let token = "";
  let serial = 0;
  let lastCommit = "";
  return {
    get composing() {
      return composing;
    },
    get token() {
      return token;
    },
    start() {
      serial += 1;
      token = "c" + String(serial);
      composing = true;
      lastCommit = "";
      return token;
    },
    update() {
      return composing;
    },
    end(text: string): CompositionEnd {
      const current = token;
      composing = false;
      token = "";
      if (!text) {
        lastCommit = "";
        return { token: current, text: "", cancel: true };
      }
      lastCommit = text;
      return { token: current, text, cancel: false };
    },
    cancel() {
      const current = token;
      composing = false;
      lastCommit = "";
      token = "";
      return current;
    },
    suppressData(data: string) {
      if (composing) {
        return true;
      }
      if (lastCommit !== "" && data === lastCommit) {
        lastCommit = "";
        return true;
      }
      return false;
    },
  };
}

export function isImeShortcut(key: string, ctrl: boolean, alt: boolean, shift: boolean): boolean {
  if (key === "Enter" || key === "Escape" || key === " " || key === "Space") {
    return true;
  }
  return ctrl && !alt && !shift && key.toLowerCase() === "k";
}
