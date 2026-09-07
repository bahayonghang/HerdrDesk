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
        (root/publisher.MANIFEST).write_text(json.dumps({'repository':'bahayonghang/HerdDesk',
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
            with self.assertRaises(publisher.PublishError):publisher.validate_manifest(root)

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
        self.assertEqual(command[3],'bahayonghang/HerdDesk')

    def test_dry_run_never_invokes_network(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'a').write_bytes(b'a');self.manifest(root,{'a':hashlib.sha256(b'a').hexdigest()})
            with patch.object(publisher,'ROOT',root),patch.object(publisher,'publish') as pub:
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(publisher.main([]),0)
                pub.assert_not_called()

    def test_preexisting_checkout_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'.git').mkdir()
            with patch.object(publisher.shutil,'which',return_value='/tool'),patch.object(publisher,'checked') as command:
                with self.assertRaises(publisher.PublishError):publisher.publish(root,[])
                command.assert_not_called()


if __name__=='__main__':unittest.main()
