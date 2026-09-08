from argparse import Namespace
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import Mock, patch

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
import probe_herdr as probe


class ProbeSafetyTests(unittest.TestCase):
    def args(self,**changes):
        fields={'mode':'observe','disposable_target':False,'input_file':None,
                'allow_input':False,'target':'w1:p1','session':None,'herdr':'herdr'}
        fields.update(changes);return Namespace(**fields)

    def test_control_without_disposable_flag_never_starts_process(self):
        with patch.object(probe.subprocess,'Popen') as start:
            with self.assertRaises(ValueError):probe.stream_probe(self.args(mode='control'))
            start.assert_not_called()

    def test_observer_cannot_send_input(self):
        with patch.object(probe.subprocess,'Popen') as start:
            with self.assertRaises(ValueError):
                probe.stream_probe(self.args(input_file='test.txt',allow_input=True,disposable_target=True))
            start.assert_not_called()

    def test_control_input_requires_separate_consent(self):
        with patch.object(probe.subprocess,'Popen') as start:
            with self.assertRaises(ValueError):
                probe.stream_probe(self.args(mode='control',input_file='test.txt',disposable_target=True))
            start.assert_not_called()

    def test_invalid_target_is_not_executed(self):
        for target in ('--takeover','bad\nvalue','bad\0value',''):
            with self.subTest(target=target),patch.object(probe.subprocess,'Popen') as start:
                with self.assertRaises(ValueError):probe.stream_probe(self.args(target=target))
                start.assert_not_called()

    def test_invalid_session_rejected(self):
        with patch.object(probe,'executable',return_value='/test/herdr'):
            with self.assertRaises(ValueError):probe.base_command(self.args(session='test\nother'))

    def test_session_is_single_argument(self):
        with patch.object(probe,'executable',return_value='/test/herdr'):
            self.assertEqual(probe.base_command(self.args(session='a space;$(ignored)')),
                             ['/test/herdr','--session','a space;$(ignored)'])

    def test_stop_owned_does_not_kill_tree(self):
        process=Mock();process.poll.return_value=None
        probe.stop_owned(process)
        process.terminate.assert_called_once_with();process.wait.assert_called_once_with(timeout=1.0)
        process.kill.assert_not_called()

    def test_stop_exited_process_is_noop(self):
        process=Mock();process.poll.return_value=0
        probe.stop_owned(process);process.terminate.assert_not_called();process.kill.assert_not_called()

    def test_terminate_timeout_kills_only_owned_handle(self):
        process=Mock();process.poll.return_value=None
        process.wait.side_effect=[subprocess.TimeoutExpired('fake',1),0]
        probe.stop_owned(process);process.kill.assert_called_once_with()

    def test_probe_does_not_claim_write_ownership_or_pane_death(self):
        text=(ROOT/'scripts'/'probe_herdr.py').read_text(encoding='utf-8')
        self.assertIn('classify_stream_end',text)
        self.assertIn("report['control_verified'] = False",text)
        self.assertNotIn("report['control_verified'] = True",text)
        self.assertNotIn("report['pane_exit_verified'] = True",text)

    def test_default_report_redacts_output_and_arguments(self):
        result={'returncode':0,'timed_out':False,'overflow':False,'duration_ms':1,'errors':[],
                'stdout':b'PRIVATE_TERMINAL','stderr':b'PRIVATE_PATH','argv':['PRIVATE_ARGUMENT']}
        summary=probe.safe_summary(result)
        self.assertNotIn('PRIVATE',str(summary));self.assertNotIn('argv',summary)
        self.assertIn('PRIVATE_PATH',probe.safe_summary(result,True)['stderr_diagnostic_opt_in'])


if __name__=='__main__':unittest.main()
