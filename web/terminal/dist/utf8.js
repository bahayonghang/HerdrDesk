const decoder = new TextDecoder("utf-8", { fatal: false, ignoreBOM: true });

export class Utf8ChunkAssembler {
          leftover = new Uint8Array(0);
  text = "";

  get heldIncomplete()          {
    return incompleteTail(this.leftover) > 0;
  }

  append(chunk            )       {
    if (chunk.length === 0) {
      return;
    }
    const merged = new Uint8Array(this.leftover.length + chunk.length);
    merged.set(this.leftover);
    merged.set(chunk, this.leftover.length);
    const keep = incompleteTail(merged);
    const complete = merged.subarray(0, merged.length - keep);
    this.leftover = keep > 0 ? merged.subarray(merged.length - keep) : new Uint8Array(0);
    if (complete.length === 0) {
      return;
    }
    this.text += decoder.decode(complete, { stream: true });
  }
}

function incompleteTail(buffer            )         {
  if (buffer.length === 0) {
    return 0;
  }
  const max = Math.min(4, buffer.length);
  for (let i = 1; i <= max; i++) {
    const lead = buffer[buffer.length - i];
    if ((lead & 0xc0) === 0x80) {
      continue;
    }
    const need = lead < 0x80
      ? 1
      : (lead & 0xe0) === 0xc0
        ? 2
        : (lead & 0xf0) === 0xe0
          ? 3
          : (lead & 0xf8) === 0xf0
            ? 4
            : 0;
    if (need === 0) {
      return 0;
    }
    return i < need ? i : 0;
  }
  return 0;
}

export function asWriteBytes(bytes            )             {
  return bytes;
}
