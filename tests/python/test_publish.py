import contextlib
import io
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
import publish_github as publisher


class PublisherTests(unittest.TestCase):
    def manifest(self,root,files):
        (root/publisher.MANIFEST).write_text(json.dumps({'repository':'bahayonghang/HerdrDesk',
            'visibility':'public','files':files}),encoding='utf-8')

    def test_valid_manifest(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'README.md').write_bytes(b'hello')
            self.manifest(root,{'README.md':hashlib.sha256(b'hello').hexdigest()})
            self.assertEqual(publisher.validate_manifest(root),['README.md',publisher.MANIFEST])

    def test_changed_file_stops_publication(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'README.md').write_bytes(b'changed')
            self.manifest(root,{'README.md':hashlib.sha256(b'hello').hexdigest()})
            with self.assertRaises(publisher.PublishError) as error:
                publisher.validate_manifest(root)
            message = str(error.exception)
            self.assertIn('Publication file changed: README.md', message)
            self.assertIn('historical first-import bundle', message)
            self.assertIn('daily gate is just ci', message)

    def test_traversal_rejected(self):
        for path in ('../secret','/absolute/secret','..\\secret','-option'):
            with self.subTest(path=path),tempfile.TemporaryDirectory() as d:
                root=Path(d);self.manifest(root,{path:'abc'})
                with self.assertRaises(publisher.PublishError):publisher.validate_manifest(root)

    def test_unlisted_secrets_not_staged(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'README.md').write_bytes(b'hello');(root/'.env').write_text('LOCAL_ONLY')
            self.manifest(root,{'README.md':hashlib.sha256(b'hello').hexdigest()})
            self.assertNotIn('.env',publisher.validate_manifest(root))

    def test_wrong_identity_rejected(self):
        for profile in ({'login':'other','id':123},{'login':'bahayonghang','id':True},None):
            with self.subTest(profile=profile),self.assertRaises(publisher.PublishError):
                publisher.validate_identity(profile)

    def test_correct_identity(self):
        self.assertEqual(publisher.validate_identity({'login':'bahayonghang','id':85470879}),('bahayonghang',85470879))

    def test_create_is_public_and_not_forceful(self):
        command=publisher.create_command(Path('/test/source'))
        self.assertIn('--public',command);self.assertIn('--push',command)
        self.assertNotIn('--force',command);self.assertNotIn('--private',command)
        self.assertEqual(command[3],'bahayonghang/HerdrDesk')

    def test_dry_run_never_invokes_network(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'a').write_bytes(b'a');self.manifest(root,{'a':hashlib.sha256(b'a').hexdigest()})
            stdout=io.StringIO()
            with patch.object(publisher,'ROOT',root),patch.object(publisher,'publish') as pub:
                with contextlib.redirect_stdout(stdout):
                    self.assertEqual(publisher.main([]),0)
                pub.assert_not_called()
            payload=json.loads(stdout.getvalue())
            self.assertEqual(payload['mode'],'offline_dry_run')
            self.assertEqual(payload['kind'],'historical_bundle_audit')
            self.assertEqual(payload['daily_gate'],'just ci')
            self.assertFalse(payload['github_changed'])
            self.assertTrue(payload['ok'])

    def test_dry_run_hash_mismatch_exits_2_without_publish(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'README.md').write_bytes(b'changed')
            self.manifest(root,{'README.md':hashlib.sha256(b'hello').hexdigest()})
            stdout=io.StringIO();stderr=io.StringIO()
            with patch.object(publisher,'ROOT',root),patch.object(publisher,'publish') as pub:
                with contextlib.redirect_stdout(stdout),contextlib.redirect_stderr(stderr):
                    self.assertEqual(publisher.main([]),2)
                pub.assert_not_called()
            payload=json.loads(stdout.getvalue())
            self.assertEqual(payload['kind'],'historical_bundle_audit')
            self.assertEqual(payload['daily_gate'],'just ci')
            self.assertFalse(payload['github_changed'])
            self.assertFalse(payload['ok'])
            self.assertIn('Publication file changed: README.md',payload['error'])
            self.assertIn('just ci',payload['error'])
            self.assertIn('Publish stopped:',stderr.getvalue())
            self.assertIn('just ci',stderr.getvalue())

    def test_help_states_historical_bundle_and_daily_gate(self):
        stdout=io.StringIO()
        with contextlib.redirect_stdout(stdout):
            with self.assertRaises(SystemExit) as exit_status:
                publisher.main(['--help'])
        self.assertEqual(exit_status.exception.code,0)
        text=stdout.getvalue()
        self.assertIn('historical',text.lower())
        self.assertIn('just ci',text)
        self.assertIn('Refuses an existing repository',text)

    def test_publish_flag_is_the_only_write_path(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'a').write_bytes(b'a');self.manifest(root,{'a':hashlib.sha256(b'a').hexdigest()})
            result={'repository':'bahayonghang/HerdrDesk','github_changed':True}
            with patch.object(publisher,'ROOT',root),patch.object(publisher,'publish',return_value=result) as pub:
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(publisher.main([]),0)
                pub.assert_not_called()
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(publisher.main(['--publish']),0)
                pub.assert_called_once()

    def test_repo_root_default_offline_is_historical_audit(self):
        stdout=io.StringIO();stderr=io.StringIO()
        with patch.object(publisher,'publish') as pub:
            with contextlib.redirect_stdout(stdout),contextlib.redirect_stderr(stderr):
                code=publisher.main([])
        pub.assert_not_called()
        self.assertIn(code,(0,2))
        payload=json.loads(stdout.getvalue())
        self.assertEqual(payload['kind'],'historical_bundle_audit')
        self.assertEqual(payload['daily_gate'],'just ci')
        self.assertFalse(payload['github_changed'])
        if code==0:
            self.assertTrue(payload['ok'])
        else:
            self.assertFalse(payload['ok'])
            self.assertIn('Publish stopped:',stderr.getvalue())
            self.assertIn('just ci',stderr.getvalue())

    def test_preexisting_checkout_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'.git').mkdir()
            with patch.object(publisher.shutil,'which',return_value='/tool'),patch.object(publisher,'checked') as command:
                with self.assertRaises(publisher.PublishError):publisher.publish(root,[])
                command.assert_not_called()

    def test_existing_github_repository_refused(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            if any((parent/'.git').exists() for parent in [root,*root.parents]):
                self.skipTest('temporary directory is inside a Git checkout')
            view=type('View',(),{'returncode':0,'stdout':'','stderr':''})()
            with patch.object(publisher.shutil,'which',return_value='/tool'):
                with patch.object(publisher,'checked',return_value='{"login":"bahayonghang","id":85470879}') as command:
                    with patch.object(publisher.subprocess,'run',return_value=view) as run:
                        with self.assertRaises(publisher.PublishError) as error:
                            publisher.publish(root,[])
                        self.assertIn('already exists',str(error.exception))
                        command.assert_called()
                        run.assert_called()


if __name__=='__main__':unittest.main()
