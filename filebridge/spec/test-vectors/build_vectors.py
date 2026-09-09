#!/usr/bin/env python3
"""Authoring helper for HD-027 golden vectors. Tests must not import this file."""
from __future__ import annotations

from pathlib import Path
import base64
import hashlib
import json
import struct

ROOT = Path(__file__).resolve().parent
MAGIC = b"HDFB"
JOB = "01234567-89ab-4def-8123-456789abcdef"
OBS = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
EMPTY_SHA = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
HELLO_SHA = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
IDENT = base64.b64encode(b"identity").decode("ascii")

KIND = {
    "RequestJson": 0x01,
    "Data": 0x02,
    "EndData": 0x03,
    "CancelJson": 0x04,
    "AcceptedJson": 0x11,
    "EntryJson": 0x12,
    "ProgressJson": 0x13,
    "CompleteJson": 0x14,
    "ErrorJson": 0x7F,
}


def b64(raw: bytes) -> str:
    return base64.b64encode(raw).decode("ascii")


def frame(kind: str, seq: int, payload: bytes, flags: int = 0, major: int = 1, minor: int = 0) -> bytes:
    header = MAGIC + bytes([major, minor, KIND[kind], flags]) + struct.pack(">II", len(payload), seq)
    return header + payload


def dumps(obj: dict) -> bytes:
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def write_vector(name: str, chunks: list[bytes]) -> tuple[str, int]:
    data = b"".join(chunks)
    path = ROOT / name
    path.write_bytes(data)
    digest = hashlib.sha256(data).hexdigest()
    return digest, len(data)


def header_only(kind: str, length: int, seq: int = 0, flags: int = 0, major: int = 1, minor: int = 0, kind_byte: int | None = None) -> bytes:
    kb = KIND[kind] if kind_byte is None else kind_byte
    return MAGIC + bytes([major, minor, kb, flags]) + struct.pack(">II", length, seq)


def main() -> None:
    vectors: list[dict] = []

    def add(vec_id: str, file_name: str, body: bytes, expect: str, frames: list[dict], **extra):
        digest = hashlib.sha256(body).hexdigest()
        (ROOT / file_name).write_bytes(body)
        offset = 0
        annotated = []
        for item in frames:
            length = item["length"]
            annotated.append({**item, "offset": offset})
            offset += length
        row = {
            "id": vec_id,
            "file": file_name,
            "sha256": digest,
            "expect": expect,
            "frames": annotated,
        }
        row.update(extra)
        vectors.append(row)

    def fr(dir_: str, kind: str, seq: int, payload: bytes, **hdr) -> tuple[bytes, dict]:
        blob = frame(kind, seq, payload, **hdr) if "kind_byte" not in hdr else (
            MAGIC + bytes([hdr.get("major", 1), hdr.get("minor", 0), hdr["kind_byte"], hdr.get("flags", 0)])
            + struct.pack(">II", len(payload), seq) + payload
        )
        return blob, {"dir": dir_, "kind": kind, "seq": seq, "length": len(blob)}

    # valid-list
    req = dumps({"protocol": "1.0", "job": JOB, "op": "list", "path": "/", "limit": 100})
    acc = dumps({"job": JOB, "op": "list", "identity": IDENT, "size": "0"})
    entry = dumps({
        "job": JOB, "name": b64(b"hello"), "display_name": "hello", "type": "file",
        "size": "5", "mtime": "1710000000", "mtime_precision": "1", "identity": IDENT,
        "symlink": False, "observation": OBS,
    })
    complete_list = dumps({
        "job": JOB, "path": "/", "length": "1", "sha256": EMPTY_SHA, "observation": OBS,
        "commit": "not_applicable", "has_more": False,
    })
    f1, m1 = fr("client", "RequestJson", 0, req)
    f2, m2 = fr("helper", "AcceptedJson", 0, acc)
    f3, m3 = fr("helper", "EntryJson", 1, entry)
    f4, m4 = fr("helper", "CompleteJson", 2, complete_list)
    add("valid-list", "valid-list.hdfb", f1 + f2 + f3 + f4, "accept",
        [m1, m2, m3, m4], outcome="success", exit_code=0, eof_after_frames=True)

    # valid-empty-write
    name_empty = b64(b"empty.txt")
    wreq = dumps({
        "protocol": "1.0", "job": JOB, "op": "write", "parent": "/", "name": name_empty,
        "mode": "create", "expected_parent_observation": OBS, "length": "0", "sha256": EMPTY_SHA,
    })
    wacc = dumps({"job": JOB, "op": "write", "identity": IDENT, "size": "0"})
    wcmp = dumps({
        "job": JOB, "path": "/" + name_empty, "length": "0", "sha256": EMPTY_SHA,
        "observation": OBS, "commit": "committed",
    })
    a, ma = fr("client", "RequestJson", 0, wreq)
    b, mb = fr("helper", "AcceptedJson", 0, wacc)
    c, mc = fr("client", "EndData", 1, b"")
    d, md = fr("helper", "CompleteJson", 1, wcmp)
    add("valid-empty-write", "valid-empty-write.hdfb", a + b + c + d, "accept",
        [ma, mb, mc, md], outcome="success", exit_code=0, eof_after_frames=True,
        exclusive_temp_deleted=False)

    # valid-stat
    sreq = dumps({"protocol": "1.0", "job": JOB, "op": "stat", "path": "/" + b64(b"hello")})
    sacc = dumps({"job": JOB, "op": "stat", "identity": IDENT, "size": "5"})
    scmp = dumps({
        "job": JOB, "path": "/" + b64(b"hello"), "length": "5", "sha256": HELLO_SHA,
        "observation": OBS, "commit": "not_applicable",
    })
    a, ma = fr("client", "RequestJson", 0, sreq)
    b, mb = fr("helper", "AcceptedJson", 0, sacc)
    c, mc = fr("helper", "CompleteJson", 1, scmp)
    add("valid-stat", "valid-stat.hdfb", a + b + c, "accept", [ma, mb, mc],
        outcome="success", exit_code=0, eof_after_frames=True)

    # valid-read
    rreq = dumps({"protocol": "1.0", "job": JOB, "op": "read", "path": "/" + b64(b"hello")})
    racc = dumps({"job": JOB, "op": "read", "identity": IDENT, "size": "5"})
    rcmp = dumps({
        "job": JOB, "path": "/" + b64(b"hello"), "length": "5", "sha256": HELLO_SHA,
        "observation": OBS, "commit": "not_applicable",
    })
    a, ma = fr("client", "RequestJson", 0, rreq)
    b, mb = fr("helper", "AcceptedJson", 0, racc)
    c, mc = fr("helper", "Data", 1, b"hello")
    d, md = fr("helper", "CompleteJson", 2, rcmp)
    add("valid-read", "valid-read.hdfb", a + b + c + d, "accept", [ma, mb, mc, md],
        outcome="success", exit_code=0, eof_after_frames=True)

    # valid-rename-create
    nreq = dumps({
        "protocol": "1.0", "job": JOB, "op": "rename",
        "source": "/" + b64(b"old"), "source_observation": OBS,
        "parent": "/", "name": b64(b"new"), "mode": "create",
        "expected_parent_observation": OBS,
    })
    nacc = dumps({"job": JOB, "op": "rename", "identity": IDENT, "size": "5"})
    ncmp = dumps({
        "job": JOB, "path": "/" + b64(b"new"), "length": "5", "sha256": HELLO_SHA,
        "observation": OBS, "commit": "committed",
    })
    a, ma = fr("client", "RequestJson", 0, nreq)
    b, mb = fr("helper", "AcceptedJson", 0, nacc)
    c, mc = fr("helper", "CompleteJson", 1, ncmp)
    add("valid-rename-create", "valid-rename-create.hdfb", a + b + c, "accept",
        [ma, mb, mc], outcome="success", exit_code=0, eof_after_frames=True)

    # unicode / space / newline component
    raw_uni = "hello world\n你好".encode("utf-8")
    uni_path = "/" + b64(raw_uni)
    ureq = dumps({"protocol": "1.0", "job": JOB, "op": "list", "path": uni_path, "limit": 1})
    uacc = dumps({"job": JOB, "op": "list", "identity": IDENT, "size": "0"})
    uent = dumps({
        "job": JOB, "name": b64(raw_uni), "display_name": "hello world\n你好", "type": "file",
        "size": "0", "mtime": "0", "mtime_precision": "1", "identity": IDENT,
        "symlink": False, "observation": OBS,
    })
    ucmp = dumps({
        "job": JOB, "path": uni_path, "length": "1", "sha256": EMPTY_SHA, "observation": OBS,
        "commit": "not_applicable", "has_more": False,
    })
    a, ma = fr("client", "RequestJson", 0, ureq)
    b, mb = fr("helper", "AcceptedJson", 0, uacc)
    c, mc = fr("helper", "EntryJson", 1, uent)
    d, md = fr("helper", "CompleteJson", 2, ucmp)
    add("valid-list-unicode-space-newline", "valid-list-unicode-space-newline.hdfb",
        a + b + c + d, "accept", [ma, mb, mc, md], outcome="success", exit_code=0,
        eof_after_frames=True)

    # non-UTF8 component
    raw_bin = b"\xff\xfe"
    bin_path = "/" + b64(raw_bin)
    breq = dumps({"protocol": "1.0", "job": JOB, "op": "list", "path": bin_path, "limit": 1})
    bacc = dumps({"job": JOB, "op": "list", "identity": IDENT, "size": "0"})
    bcmp = dumps({
        "job": JOB, "path": bin_path, "length": "0", "sha256": EMPTY_SHA, "observation": OBS,
        "commit": "not_applicable", "has_more": False,
    })
    a, ma = fr("client", "RequestJson", 0, breq)
    b, mb = fr("helper", "AcceptedJson", 0, bacc)
    c, mc = fr("helper", "CompleteJson", 1, bcmp)
    add("valid-list-non-utf8", "valid-list-non-utf8.hdfb", a + b + c, "accept",
        [ma, mb, mc], outcome="success", exit_code=0, eof_after_frames=True)

    # KeepBoth exclusive create
    kb_name = b64(b"file (1).txt")
    kreq = dumps({
        "protocol": "1.0", "job": JOB, "op": "write", "parent": "/", "name": kb_name,
        "mode": "create", "expected_parent_observation": OBS, "length": "0", "sha256": EMPTY_SHA,
    })
    kacc = dumps({"job": JOB, "op": "write", "identity": IDENT, "size": "0"})
    kcmp = dumps({
        "job": JOB, "path": "/" + kb_name, "length": "0", "sha256": EMPTY_SHA,
        "observation": OBS, "commit": "committed",
    })
    a, ma = fr("client", "RequestJson", 0, kreq)
    b, mb = fr("helper", "AcceptedJson", 0, kacc)
    c, mc = fr("client", "EndData", 1, b"")
    d, md = fr("helper", "CompleteJson", 1, kcmp)
    add("valid-keepboth-exclusive-create", "valid-keepboth-exclusive-create.hdfb",
        a + b + c + d, "accept", [ma, mb, mc, md], outcome="success", exit_code=0,
        eof_after_frames=True, keep_both_exclusive_create=True)

    # cancel before commit
    creq = dumps({
        "protocol": "1.0", "job": JOB, "op": "write", "parent": "/", "name": b64(b"x"),
        "mode": "create", "expected_parent_observation": OBS, "length": "5", "sha256": HELLO_SHA,
    })
    cacc = dumps({"job": JOB, "op": "write", "identity": IDENT, "size": "0"})
    ccan = dumps({"job": JOB, "reason": "user"})
    cerr = dumps({"job": JOB, "code": "cancelled", "stage": "cancel", "retryable": False})
    a, ma = fr("client", "RequestJson", 0, creq)
    b, mb = fr("helper", "AcceptedJson", 0, cacc)
    c, mc = fr("client", "Data", 1, b"he")
    d, md = fr("client", "CancelJson", 2, ccan)
    e, me = fr("helper", "ErrorJson", 1, cerr)
    add("valid-cancel-before-commit", "valid-cancel-before-commit.hdfb",
        a + b + c + d + e, "accept", [ma, mb, mc, md, me], outcome="cancelled",
        exit_code=1, eof_after_frames=True, exclusive_temp_deleted=True)

    # cancel after commit
    a, ma = fr("client", "RequestJson", 0, wreq)
    b, mb = fr("helper", "AcceptedJson", 0, wacc)
    c, mc = fr("client", "EndData", 1, b"")
    d, md = fr("client", "CancelJson", 2, dumps({"job": JOB, "reason": "user"}))
    e, me = fr("helper", "CompleteJson", 1, wcmp)
    add("valid-cancel-after-commit", "valid-cancel-after-commit.hdfb",
        a + b + c + d + e, "accept", [ma, mb, mc, md, me], outcome="success",
        exit_code=0, eof_after_frames=True, commit_linearized_after_frame=2,
        exclusive_temp_deleted=False)

    # replace stale target (valid ErrorJson)
    sreq = dumps({
        "protocol": "1.0", "job": JOB, "op": "write", "parent": "/", "name": b64(b"x"),
        "mode": "replace", "expected_parent_observation": OBS,
        "expected_target_observation": OBS, "length": "0", "sha256": EMPTY_SHA,
    })
    serr = dumps({"job": JOB, "code": "stale_target", "stage": "request", "retryable": False})
    a, ma = fr("client", "RequestJson", 0, sreq)
    b, mb = fr("helper", "ErrorJson", 0, serr)
    add("valid-replace-stale-target", "valid-replace-stale-target.hdfb", a + b, "accept",
        [ma, mb], outcome="failed", exit_code=1, eof_after_frames=True,
        auto_replay_forbidden=True)

    # disconnect before receipt
    a, ma = fr("client", "RequestJson", 0, wreq)
    b, mb = fr("helper", "AcceptedJson", 0, wacc)
    c, mc = fr("client", "EndData", 1, b"")
    add("valid-disconnect-before-receipt", "valid-disconnect-before-receipt.hdfb",
        a + b + c, "accept", [ma, mb, mc], outcome="unknown", exit_code=None,
        eof_after_frames=True, commit_linearized_after_frame=2,
        auto_replay_forbidden=True, skip_exit=True)

    # invalid-oversize-json (header only)
    body = header_only("RequestJson", 1048577)
    add("invalid-oversize-json", "invalid-oversize-json.hdfb", body, "reject",
        [{"dir": "client", "kind": "RequestJson", "seq": 0, "length": 16}],
        error="payload_too_large", allocate_payload=False)

    body = header_only("Data", 1048577)
    add("invalid-oversize-data", "invalid-oversize-data.hdfb", body, "reject",
        [{"dir": "helper", "kind": "Data", "seq": 0, "length": 16}],
        error="payload_too_large", allocate_payload=False,
        note="Feed as helper Data after a valid read request in unit tests if used alone; "
             "standalone header is rejected on length before kind state.")

    # sequence gap: helper Accepted seq=2
    a, ma = fr("client", "RequestJson", 0, dumps({"protocol": "1.0", "job": JOB, "op": "list", "path": "/", "limit": 1}))
    b, mb = fr("helper", "AcceptedJson", 2, acc)
    add("invalid-sequence-gap", "invalid-sequence-gap.hdfb", a + b, "reject",
        [ma, mb], error="sequence_gap")

    # unknown kind
    body = header_only("RequestJson", 0, kind_byte=0x05)
    add("invalid-unknown-kind", "invalid-unknown-kind.hdfb", body, "reject",
        [{"dir": "client", "kind": "unknown", "seq": 0, "length": 16}],
        error="unknown_kind")

    # truncated header
    full, meta = fr("client", "RequestJson", 0, req)
    add("invalid-truncated-header", "invalid-truncated-header.hdfb", full[:8], "reject",
        [{"dir": "client", "kind": "RequestJson", "seq": 0, "length": 8}],
        error="truncated_header", eof_after_frames=True)

    # truncated payload
    add("invalid-truncated-payload", "invalid-truncated-payload.hdfb", full[:20], "reject",
        [{"dir": "client", "kind": "RequestJson", "seq": 0, "length": 20}],
        error="truncated_payload", eof_after_frames=True)

    # replay: two client frames seq 0
    a, ma = fr("client", "RequestJson", 0, req)
    b, mb = fr("client", "CancelJson", 0, dumps({"job": JOB, "reason": "user"}))
    add("invalid-sequence-replay", "invalid-sequence-replay.hdfb", a + b, "reject",
        [ma, mb], error="sequence_replay")

    # wrong direction: Accepted on client stream as first frame
    a, ma = fr("client", "AcceptedJson", 0, acc)
    add("invalid-wrong-direction", "invalid-wrong-direction.hdfb", a, "reject",
        [ma], error="wrong_direction")

    # second request
    a, ma = fr("client", "RequestJson", 0, req)
    b, mb = fr("helper", "AcceptedJson", 0, acc)
    c, mc = fr("client", "RequestJson", 1, req)
    add("invalid-second-request", "invalid-second-request.hdfb", a + b + c, "reject",
        [ma, mb, mc], error="second_request")

    # data before accepted
    a, ma = fr("client", "RequestJson", 0, wreq)
    b, mb = fr("client", "Data", 1, b"x")
    add("invalid-data-before-accepted", "invalid-data-before-accepted.hdfb", a + b, "reject",
        [ma, mb], error="data_before_accepted")

    # unknown flags
    a, ma = fr("client", "RequestJson", 0, req, flags=1)
    add("invalid-unknown-flags", "invalid-unknown-flags.hdfb", a, "reject",
        [ma], error="unknown_flags")

    # unknown version
    a, ma = fr("client", "RequestJson", 0, req, minor=1)
    add("invalid-unknown-version", "invalid-unknown-version.hdfb", a, "reject",
        [ma], error="unsupported_version")

    def bad_path(vec_id: str, file_name: str, path: str, error: str):
        payload = dumps({"protocol": "1.0", "job": JOB, "op": "list", "path": path, "limit": 1})
        a, ma = fr("client", "RequestJson", 0, payload)
        add(vec_id, file_name, a, "reject", [ma], error=error)

    bad_path("invalid-dot-component", "invalid-dot-component.hdfb", "/" + b64(b"."), "invalid_component")
    bad_path("invalid-dotdot-component", "invalid-dotdot-component.hdfb", "/" + b64(b".."), "invalid_component")
    bad_path("invalid-slash-component", "invalid-slash-component.hdfb", "/" + b64(b"a/b"), "invalid_component")
    bad_path("invalid-nul-component", "invalid-nul-component.hdfb", "/" + b64(b"\x00"), "invalid_component")
    bad_path("invalid-empty-component", "invalid-empty-component.hdfb", "//", "invalid_wire_path")

    # oversize cursor
    cursor = b64(b"a" * 4097)
    payload = dumps({
        "protocol": "1.0", "job": JOB, "op": "list", "path": "/", "limit": 1, "cursor": cursor,
    })
    a, ma = fr("client", "RequestJson", 0, payload)
    add("invalid-oversize-cursor", "invalid-oversize-cursor.hdfb", a, "reject",
        [ma], error="invalid_cursor")

    # replace missing observation
    payload = dumps({
        "protocol": "1.0", "job": JOB, "op": "write", "parent": "/", "name": b64(b"x"),
        "mode": "replace", "expected_parent_observation": OBS, "length": "0", "sha256": EMPTY_SHA,
    })
    a, ma = fr("client", "RequestJson", 0, payload)
    add("invalid-replace-missing-observation", "invalid-replace-missing-observation.hdfb",
        a, "reject", [ma], error="replace_observation_required")

    # json float
    raw = (
        b'{"protocol":"1.0","job":"' + JOB.encode() +
        b'","op":"list","path":"/","limit":1.5}'
    )
    a, ma = fr("client", "RequestJson", 0, raw)
    add("invalid-json-float", "invalid-json-float.hdfb", a, "reject", [ma],
        error="json_float_rejected")

    # duplicate key
    raw = (
        b'{"protocol":"1.0","job":"' + JOB.encode() +
        b'","op":"list","op":"stat","path":"/","limit":1}'
    )
    a, ma = fr("client", "RequestJson", 0, raw)
    add("invalid-duplicate-key", "invalid-duplicate-key.hdfb", a, "reject", [ma],
        error="duplicate_json_key")

    # pollution
    add("invalid-pollution", "invalid-pollution.hdfb", b"SSH-2.0-OpenSSH_9.0\r\n", "reject",
        [{"dir": "helper", "kind": "pollution", "seq": 0, "length": 21}],
        error="protocol_pollution", eof_after_frames=True)

    # terminal result / exit mismatch: Complete then exit 1
    a, ma = fr("client", "RequestJson", 0, req)
    b, mb = fr("helper", "AcceptedJson", 0, acc)
    c, mc = fr("helper", "CompleteJson", 1, dumps({
        "job": JOB, "path": "/", "length": "0", "sha256": EMPTY_SHA, "observation": OBS,
        "commit": "not_applicable", "has_more": False,
    }))
    add("invalid-terminal-exit-mismatch", "invalid-terminal-exit-mismatch.hdfb",
        a + b + c, "accept", [ma, mb, mc], outcome="unknown", exit_code=1,
        eof_after_frames=True, error="terminal_result_mismatch")

    manifest = {
        "schema_version": 1,
        "protocol": "1.0",
        "header_bytes": 16,
        "magic": "HDFB",
        "command_not_implemented": "herddesk-filebridge serve --stdio --protocol 1.0",
        "job": JOB,
        "vectors": vectors,
    }
    (ROOT / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"wrote {len(vectors)} vectors")


if __name__ == "__main__":
    main()
