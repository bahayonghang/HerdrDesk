from __future__ import annotations
import base64
import hashlib
import json
from pathlib import Path
import random
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.protocol import (Limits, ProtocolError, NdjsonDecoder,
    TerminalCaptureValidator, analyze_capture, strict_json_loads, validate_frame, validate_input)


def frame(seq=1, data=b'hello', full=True, **changes):
    result = {'type':'terminal.frame','seq':seq,'encoding':'ansi','width':120,
              'height':40,'full':full,'bytes':base64.b64encode(data).decode('ascii')}
    result.update(changes)
    return result


def line(obj):
    return json.dumps(obj, ensure_ascii=False).encode('utf-8') + b'\n'


class StrictJsonTests(unittest.TestCase):
    def test_duplicate_root(self):
        with self.assertRaisesRegex(ProtocolError,'duplicate_json_key'):
            strict_json_loads(b'{"type":"a","type":"b"}')

    def test_duplicate_nested(self):
        with self.assertRaises(ProtocolError):
            strict_json_loads(b'{"x":{"a":1,"a":2}}')

    def test_nonfinite(self):
        for value in ('NaN','Infinity','-Infinity'):
            with self.subTest(value=value), self.assertRaises(ProtocolError):
                strict_json_loads('{"x":'+value+'}')

    def test_invalid_utf8(self):
        with self.assertRaises(ProtocolError):strict_json_loads(b'{"x":"\xff"}')

    def test_json_error_is_redacted(self):
        secret='synthetic-private-terminal-content'
        try:strict_json_loads('{'+secret)
        except ProtocolError as exc:self.assertNotIn(secret,str(exc))
        else:self.fail('malformed JSON accepted')

    def test_extreme_nesting_is_redacted(self):
        with self.assertRaises(ProtocolError):strict_json_loads('['*3000+'0'+']'*3000)

    def test_unicode(self):
        self.assertEqual(strict_json_loads('{"x":"中文"}')['x'],'中文')


class FramerTests(unittest.TestCase):
    def test_split_record(self):
        decoder=NdjsonDecoder(100)
        self.assertEqual(list(decoder.feed(b'{"a":')),[])
        self.assertEqual(list(decoder.feed(b'1}\n')), [b'{"a":1}'])
        decoder.finish()

    def test_multiple_records_and_crlf(self):
        d=NdjsonDecoder()
        self.assertEqual(list(d.feed(b'{}\r\n\n \t\r\n[]\n')),[b'{}\r',b'[]'])
        d.finish()

    def test_byte_by_byte_utf8(self):
        data='{"text":"你好"}\n'.encode()
        d=NdjsonDecoder();lines=[]
        for value in data:lines.extend(d.feed(bytes([value])))
        d.finish();self.assertEqual(strict_json_loads(lines[0])['text'],'你好')

    def test_exact_budget(self):
        d=NdjsonDecoder(2);self.assertEqual(list(d.feed(b'{}\n')),[b'{}']);d.finish()

    def test_cross_chunk_budget_and_latch(self):
        d=NdjsonDecoder(2);list(d.feed(b'12'))
        with self.assertRaisesRegex(ProtocolError,'line_bytes_limit'):list(d.feed(b'3'))
        with self.assertRaisesRegex(ProtocolError,'decoder_not_active'):list(d.feed(b'\n'))

    def test_single_huge_chunk_is_bounded(self):
        d=NdjsonDecoder(16)
        with self.assertRaises(ProtocolError):list(d.feed(b'x'*100000))
        self.assertLessEqual(len(d.buffer),16)

    def test_eof_valid_json_without_delimiter_is_failure(self):
        d=NdjsonDecoder();list(d.feed(b'{}'))
        with self.assertRaisesRegex(ProtocolError,'truncated'):d.finish()

    def test_feed_after_finish(self):
        d=NdjsonDecoder();d.finish()
        with self.assertRaises(ProtocolError):list(d.feed(b'{}\n'))

    def test_many_lines_not_total_chunk_budget(self):
        d=NdjsonDecoder(2)
        self.assertEqual(len(list(d.feed(b'{}\n'*1000))),1000);d.finish()


class FrameTests(unittest.TestCase):
    def test_valid_frame(self):
        seq,result=validate_frame(frame())
        self.assertEqual(seq,1);self.assertEqual(result['decoded_bytes'],5)
        self.assertEqual(result['sha256'],hashlib.sha256(b'hello').hexdigest())
        self.assertNotIn('bytes',result)

    def test_schema_uses_bytes_not_data(self):
        obj=frame();obj['data']=obj.pop('bytes')
        with self.assertRaises(ProtocolError):validate_frame(obj)

    def test_invalid_dimensions(self):
        for name in ('width','height'):
            for val in (0,-1,65536,True,False,'120',1.0,None):
                with self.subTest(name=name,val=val),self.assertRaises(ProtocolError):
                    validate_frame(frame(**{name:val}))

    def test_invalid_sequences(self):
        for seq in (0,-1,2**64,True,1.0,'1',None):
            with self.subTest(seq=seq),self.assertRaises(ProtocolError):validate_frame(frame(seq=seq))

    def test_max_u64_sequence_is_integer_not_float(self):
        raw=line(frame(seq=2**64-1))
        seq,_=validate_frame(strict_json_loads(raw));self.assertEqual(seq,2**64-1)

    def test_sequence_gap_and_replay(self):
        for seq in (1,3):
            with self.subTest(seq=seq),self.assertRaises(ProtocolError):validate_frame(frame(seq),1)

    def test_first_frame_must_be_full(self):
        with self.assertRaises(ProtocolError):validate_frame(frame(full=False))

    def test_full_must_be_boolean(self):
        for full in (1,'true',None):
            with self.subTest(full=full),self.assertRaises(ProtocolError):validate_frame(frame(full=full))

    def test_invalid_base64_and_padding(self):
        for value in ('!!!','aGVs bG8=','Zh==','Zg===',1,None,'中'):
            with self.subTest(value=value),self.assertRaises(ProtocolError):validate_frame(frame(bytes=value))

    def test_decoded_budget(self):
        with self.assertRaises(ProtocolError):validate_frame(frame(data=b'1234'),limits=Limits(1024,3,16))

    def test_utf8_split_is_not_decoded(self):
        first='你'.encode()[:1];second='你'.encode()[1:]
        seq,a=validate_frame(frame(data=first))
        _,b=validate_frame(frame(2,data=second,full=False),seq)
        self.assertEqual(a['decoded_bytes']+b['decoded_bytes'],3)

    def test_unknown_fields_ignored_but_type_and_encoding_fail(self):
        validate_frame(frame(future_field={'a':1}))
        with self.assertRaises(ProtocolError):validate_frame(frame(type='terminal.granted'))
        with self.assertRaises(ProtocolError):validate_frame(frame(encoding='utf8'))

    def test_closed_reason_is_redacted(self):
        _,m=validate_frame({'type':'terminal.closed','reason':'sensitive-host'})
        self.assertEqual(m,{'type':'terminal.closed','reason_present':True})
        with self.assertRaises(ProtocolError):validate_frame({'type':'terminal.closed','reason':True})

    def test_stream_latches_failure(self):
        v=TerminalCaptureValidator();v.accept(line(frame()))
        with self.assertRaises(ProtocolError):v.accept(line(frame(3)))
        with self.assertRaises(ProtocolError):v.accept(line(frame(2)))

    def test_no_frame_after_close(self):
        v=TerminalCaptureValidator();v.accept(line(frame()))
        v.accept(line({'type':'terminal.closed','reason':'detached'}))
        with self.assertRaises(ProtocolError):v.accept(line(frame(2)))

    def test_new_connection_resets_sequence(self):
        for _ in range(2):TerminalCaptureValidator().accept(line(frame()))


class InputTests(unittest.TestCase):
    def test_valid_command_fixtures(self):
        for raw in (ROOT/'tests/fixtures/input-valid.ndjson').read_bytes().splitlines():
            validate_input(strict_json_loads(raw))

    def test_exclusive_payload(self):
        for obj in ({'type':'terminal.input'},{'type':'terminal.input','text':'x','bytes':'eA=='}):
            with self.assertRaises(ProtocolError):validate_input(obj)

    def test_empty_input(self):
        for key in ('text','bytes'):
            with self.assertRaises(ProtocolError):validate_input({'type':'terminal.input',key:''})

    def test_unicode_input_and_byte_budget(self):
        validate_input({'type':'terminal.input','text':'你好'})
        with self.assertRaises(ProtocolError):
            validate_input({'type':'terminal.input','text':'你好'},Limits(1024,256,5))

    def test_unpaired_surrogate(self):
        with self.assertRaises(ProtocolError):validate_input({'type':'terminal.input','text':'\ud800'})

    def test_scroll_coordinates(self):
        for key in ('row','column'):
            for value in (-1,65536,True,'5'):
                with self.subTest(key=key,value=value),self.assertRaises(ProtocolError):
                    validate_input({'type':'terminal.scroll','direction':'up','lines':3,key:value})
        validate_input({'type':'terminal.scroll','direction':'down','lines':1,'row':None,'column':0})

    def test_scroll_modifiers(self):
        for value in (-1,256,True,1.0):
            with self.subTest(value=value),self.assertRaises(ProtocolError):
                validate_input({'type':'terminal.scroll','direction':'up','lines':3,'modifiers':value})

    def test_scroll_defaults_and_invalid_source(self):
        validate_input({'type':'terminal.scroll','direction':'up','lines':3})
        with self.assertRaises(ProtocolError):
            validate_input({'type':'terminal.scroll','direction':'up','lines':3,'source':'mouse'})

    def test_cell_u32_validation(self):
        for value in (-1,2**32,True,'9'):
            with self.subTest(value=value),self.assertRaises(ProtocolError):
                validate_input({'type':'terminal.resize','cols':120,'rows':40,'cell_width_px':value})

    def test_limits_validation(self):
        for value in (0,-1,True,1.1,257*1024*1024):
            with self.subTest(value=value),self.assertRaises(ValueError):Limits(max_line_bytes=value)

    def test_unknown_command(self):
        with self.assertRaises(ProtocolError):validate_input({'type':'runCommand','text':'whoami'})


class CaptureTests(unittest.TestCase):
    def test_synthetic_fixture(self):
        report=analyze_capture([(ROOT/'tests/fixtures/terminal-valid.ndjson').read_bytes()])
        self.assertTrue(report['validated']);self.assertFalse(report['windows_verified'])
        self.assertFalse(report['input_execution_verified'])

    def test_random_chunk_boundaries(self):
        content=b''.join(line(frame(i,data='你好'.encode(),full=i==1)) for i in range(1,30))
        expected=analyze_capture([content]);rng=random.Random(42)
        for _ in range(100):
            parts=[];offset=0
            while offset<len(content):
                n=rng.randint(1,70);parts.append(content[offset:offset+n]);offset+=n
            self.assertEqual(analyze_capture(parts),expected)

    def test_no_closed_envelope_is_not_pane_death(self):
        report=analyze_capture([line(frame())])
        self.assertFalse(report['saw_terminal_closed']);self.assertFalse(report['daemon_or_pane_exit_verified'])

    def test_total_budget(self):
        with self.assertRaisesRegex(ProtocolError,'capture_bytes_limit'):
            analyze_capture([line(frame())],max_total_bytes=1)

    def test_no_frames(self):
        with self.assertRaisesRegex(ProtocolError,'no_terminal_frames'):analyze_capture([b'\n'])

    def test_closed_only_is_not_success(self):
        with self.assertRaises(ProtocolError):analyze_capture([line({'type':'terminal.closed'})])

    def test_cli_no_overwrite(self):
        with tempfile.TemporaryDirectory() as folder:
            output=Path(folder)/'out.json';output.write_text('keep-me')
            result=subprocess.run([sys.executable,str(ROOT/'scripts/check_capture.py'),
                str(ROOT/'tests/fixtures/terminal-valid.ndjson'),'--output',str(output)],
                capture_output=True,timeout=10)
            self.assertEqual(result.returncode,2);self.assertEqual(output.read_text(),'keep-me')

    def test_cli_missing_file_redacts_path(self):
        result=subprocess.run([sys.executable,str(ROOT/'scripts/check_capture.py'),
            'synthetic-sensitive-host/path-does-not-exist'],capture_output=True,timeout=10)
        self.assertEqual(result.returncode,2)
        self.assertNotIn(b'synthetic-sensitive-host',result.stdout+result.stderr)


if __name__=='__main__':unittest.main()
