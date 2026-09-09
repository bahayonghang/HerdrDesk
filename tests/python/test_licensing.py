from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.licensing import (
    REQUIRED_NAME_KINDS,
    REQUIRED_TEMPLATES,
    LicensingError,
    check_licensing,
    find_herdrm_copies,
    validate_licensing,
)
import validate_repository as repository


def _load(rel: str) -> dict:
    return json.loads((ROOT / rel).read_text(encoding='utf-8'))


def _bundle() -> tuple[dict, dict, str, str]:
    register = _load('docs/licensing/register.json')
    candidates = {}
    for rel in REQUIRED_TEMPLATES:
        candidates[rel] = _load(rel)
    status = (ROOT / 'LICENSE-STATUS.md').read_text(encoding='utf-8')
    markdown = (ROOT / 'docs/licensing-register.md').read_text(encoding='utf-8')
    return register, candidates, status, markdown


def _check(register=None, candidates=None, license_status_text=None,
           register_markdown=None, herdrm_copy_paths=()):
    shipped_register, shipped_candidates, shipped_status, shipped_markdown = _bundle()
    return check_licensing(
        shipped_register if register is None else register,
        shipped_candidates if candidates is None else candidates,
        license_status_text=shipped_status if license_status_text is None else license_status_text,
        register_markdown=shipped_markdown if register_markdown is None else register_markdown,
        herdrm_copy_paths=herdrm_copy_paths,
    )


def _unit(register: dict, name: str, artifact_kind: str | None = None) -> dict:
    matches = [
        item for item in register['units']
        if item['name'] == name and (artifact_kind is None or item['artifact_kind'] == artifact_kind)
    ]
    if len(matches) != 1:
        raise AssertionError('missing shipped unit ' + name)
    return matches[0]


class LicensingRegisterTests(unittest.TestCase):
    def test_shipped_register_passes_and_does_not_claim_ac02(self):
        result = validate_licensing(ROOT)
        self.assertEqual(result['licensing_validation'], 'passed')
        self.assertFalse(result['ac02_passed'])
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['herdrm_copy_present'])
        self.assertEqual(result['approved_unit_count'], 8)
        self.assertEqual(result['approved_candidate_count'], 0)
        repo = repository.validate()
        self.assertEqual(repo['structural_validation'], 'passed')
        self.assertEqual(repo['licensing_validation'], 'passed')
        self.assertFalse(repo['windows_verified'])
        self.assertFalse(repo['ac02_passed'])

    def test_shipped_names_and_markdown_match(self):
        register = _load('docs/licensing/register.json')
        markdown = (ROOT / 'docs/licensing-register.md').read_text(encoding='utf-8')
        have = {item['name']: item['kind'] for item in register['names']}
        self.assertEqual(have, dict(REQUIRED_NAME_KINDS))
        for name in REQUIRED_NAME_KINDS:
            self.assertIn(name, markdown)
        self.assertEqual(register['ac02_status'], 'not_run')
        self.assertEqual(register['ac02_child']['AC02'], 'not_run')
        self.assertEqual(register['final_review_task'], 'HD-035')
        self.assertIs(register['project_license']['public_visibility_is_license_grant'], False)
        _check()

    def test_herdr_source_binary_and_runtime_are_separate_units(self):
        register = _load('docs/licensing/register.json')
        source = _unit(register, 'herdr', 'source')
        binary = _unit(register, 'herdr', 'prebuilt_binary')
        runtime = _unit(register, 'herdr', 'runtime_download')
        self.assertEqual(source['admission'], 'pending')
        self.assertEqual(binary['admission'], 'pending')
        self.assertEqual(runtime['admission'], 'blocked')
        self.assertIsInstance(source['hash']['git_blob_sha'], dict)
        self.assertIsNone(binary['hash']['distribution_binary_sha256'])
        self.assertIsNone(runtime['hash']['runtime_binary_sha256'])
        self.assertNotEqual(source['category'], binary['category'])
        self.assertNotEqual(binary['category'], runtime['category'])

    def test_shipped_herdrm_is_blocked_and_not_vendored(self):
        register = _load('docs/licensing/register.json')
        unit = _unit(register, 'herdrm')
        self.assertEqual(unit['admission'], 'blocked')
        self.assertIs(unit['vendored'], False)
        self.assertEqual(unit['copied_files'], [])
        self.assertEqual(find_herdrm_copies(ROOT), [])
        self.assertFalse(validate_licensing(ROOT)['herdrm_copy_present'])

    def test_templates_are_not_approved_admissions(self):
        _, candidates, _, _ = _bundle()
        for rel in REQUIRED_TEMPLATES:
            doc = candidates[rel]
            self.assertIs(doc['template'], True)
            self.assertEqual(doc['admission'], 'pending')
            self.assertIs(doc['lock_allowed'], False)
            self.assertIsNone(doc['name'])
            self.assertIsNone(doc['license'])
        _check()

    def test_hd007_nuget_probes_are_pending_and_out_of_lock(self):
        register = _load('docs/licensing/register.json')
        for name in ('Microsoft.WindowsAppSDK', 'Microsoft.NET.Test.Sdk'):
            unit = _unit(register, name)
            self.assertEqual(unit['admission'], 'pending')
            self.assertIs(unit['lock_allowed'], False)
            self.assertIs(unit['enters_package_lock'], False)
            self.assertEqual(unit['owner'], 'HD-007')
        runtime = _unit(register, 'Microsoft.Web.WebView2', 'runtime_download')
        self.assertEqual(runtime['admission'], 'pending')
        self.assertIs(runtime['lock_allowed'], False)
        self.assertIs(runtime['enters_package_lock'], False)
        _check()

    def test_hd007_winui_lock_units_are_approved(self):
        register = _load('docs/licensing/register.json')
        names = (
            'Microsoft.WindowsAppSDK.WinUI',
            'Microsoft.WindowsAppSDK.Base',
            'Microsoft.WindowsAppSDK.Foundation',
            'Microsoft.WindowsAppSDK.InteractiveExperiences',
            'Microsoft.Windows.SDK.BuildTools',
            'Microsoft.Windows.SDK.BuildTools.MSIX',
        )
        for name in names:
            unit = _unit(register, name)
            self.assertEqual(unit['admission'], 'approved')
            self.assertIs(unit['lock_allowed'], True)
            self.assertIs(unit['enters_package_lock'], True)
            self.assertIs(unit['enters_msix'], False)
            self.assertEqual(unit['notice_status'], 'recorded')
            self.assertEqual(unit['final_review_task'], 'HD-035')
        webview = _unit(register, 'Microsoft.Web.WebView2', 'prebuilt_binary')
        self.assertEqual(webview['admission'], 'approved')
        self.assertIs(webview['lock_allowed'], True)
        self.assertNotEqual(webview['artifact_kind'], 'runtime_download')
        result = validate_licensing(ROOT)
        self.assertEqual(result['approved_unit_count'], 8)
        self.assertFalse(result['ac02_passed'])
        lock = json.loads((ROOT / 'src' / 'HerdDesk.App' / 'packages.lock.json').read_text(encoding='utf-8'))
        hashes = {}
        for deps in lock['dependencies'].values():
            for name, spec in deps.items():
                if spec.get('type') == 'Project' or name.lower().startswith('herddesk.'):
                    continue
                hashes[name] = (spec['resolved'], spec['contentHash'])
        self.assertEqual(len(hashes), 7)
        xterm = _unit(register, '@xterm/xterm')
        self.assertEqual(xterm['admission'], 'approved')
        self.assertEqual(xterm['version'], '6.0.0')
        self.assertEqual(xterm['license'], 'MIT')
        self.assertIs(xterm['enters_webview_bundle'], True)
        self.assertIs(xterm['enters_package_lock'], False)
        bundle = _unit(register, 'herddesk-terminal-bundle')
        self.assertEqual(bundle['admission'], 'pending')
        self.assertIs(bundle['enters_webview_bundle'], False)
        for name, (version, content) in hashes.items():
            unit = _unit(register, name, 'prebuilt_binary')
            self.assertEqual(unit['admission'], 'approved')
            self.assertEqual(unit['version'], version)
            self.assertEqual(unit['hash']['nuget_content_hash'], content)
        _check()

    def test_pending_candidate_lock_flag_is_rejected(self):
        _, candidates, _, _ = _bundle()
        candidates[REQUIRED_TEMPLATES[0]]['lock_allowed'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(candidates=candidates)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_pending_unit_release_flag_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herddesk-csharp')['enters_msix'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_blocked_unit_cannot_enter_lock(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herdrm')['enters_package_lock'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'blocked_treated_as_approved')

    def test_template_cannot_be_marked_approved(self):
        _, candidates, _, _ = _bundle()
        doc = candidates[REQUIRED_TEMPLATES[0]]
        doc['admission'] = 'approved'
        doc['license'] = 'MIT'
        doc['notice_status'] = 'recorded'
        with self.assertRaises(LicensingError) as ctx:
            _check(candidates=candidates)
        self.assertEqual(str(ctx.exception), 'template_used_as_admission_proof')

    def test_approving_unselected_first_party_license_is_rejected(self):
        register, _, _, _ = _bundle()
        unit = _unit(register, 'herddesk-csharp')
        unit['admission'] = 'approved'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_conflict_cannot_stay_pending(self):
        _, candidates, _, _ = _bundle()
        candidates[REQUIRED_TEMPLATES[1]]['conflict'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(candidates=candidates)
        self.assertEqual(str(ctx.exception), 'blocked_treated_as_approved')

    def test_herdrm_copy_claim_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herdrm')['copied_files'] = ['vendor/herdrm/icon.png']
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'herdrm_copy_present')

    def test_herdrm_vendored_flag_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herdrm')['vendored'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'herdrm_copy_present')

    def test_herdrm_path_scan_result_is_rejected(self):
        with self.assertRaises(LicensingError) as ctx:
            _check(herdrm_copy_paths=('vendor/herdrm/icon.png',))
        self.assertEqual(str(ctx.exception), 'herdrm_copy_present')

    def test_find_herdrm_copies_flags_icon_and_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'vendor' / 'herdrm').mkdir(parents=True)
            (root / 'vendor' / 'herdrm' / 'icon.png').write_bytes(b'x')
            (root / 'herdrm.ico').write_bytes(b'x')
            (root / 'docs').mkdir()
            (root / 'docs' / 'notes.md').write_text('herdrm mention', encoding='utf-8')
            found = set(find_herdrm_copies(root))
        self.assertIn('vendor/herdrm', found)
        self.assertIn('herdrm.ico', found)
        self.assertFalse(any(path.endswith('notes.md') for path in found))

    def test_public_visibility_flag_is_rejected(self):
        register, _, _, _ = _bundle()
        register['project_license']['public_visibility_is_license_grant'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'public_visibility_as_license_grant')

    def test_license_status_public_grant_sentence_is_rejected(self):
        status = (ROOT / 'LICENSE-STATUS.md').read_text(encoding='utf-8')
        status = status + '\nPublic visibility is an MIT license grant.\n'
        with self.assertRaises(LicensingError) as ctx:
            _check(license_status_text=status)
        self.assertEqual(str(ctx.exception), 'public_visibility_as_license_grant')

    def test_license_status_missing_disclaimer_is_rejected(self):
        status = (ROOT / 'LICENSE-STATUS.md').read_text(encoding='utf-8')
        status = status.replace(
            'Public visibility alone must not be treated as an MIT, Apache-2.0 or other license grant.',
            'See the project homepage for reuse terms.',
        )
        with self.assertRaises(LicensingError) as ctx:
            _check(license_status_text=status)
        self.assertEqual(str(ctx.exception), 'public_visibility_as_license_grant')

    def test_register_ac02_passed_claim_is_rejected(self):
        register, _, _, _ = _bundle()
        register['ac02_passed'] = True
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_child_ac_passed_claim_is_rejected(self):
        register, _, _, _ = _bundle()
        register['ac02_child']['AC02-C1'] = 'passed'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_final_ac02_child_passed_is_rejected(self):
        register, _, _, _ = _bundle()
        register['ac02_child']['AC02'] = 'passed'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_herdrm_pending_admission_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herdrm')['admission'] = 'pending'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'blocked_treated_as_approved')

    def test_project_spdx_while_pending_is_rejected(self):
        register, _, _, _ = _bundle()
        register['project_license']['spdx'] = 'MIT'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'public_visibility_as_license_grant')

    def test_approved_with_pending_notice_is_rejected(self):
        register, _, _, _ = _bundle()
        unit = _unit(register, 'herddesk-csharp')
        unit['admission'] = 'approved'
        unit['license'] = 'MIT'
        unit['notice_status'] = 'pending'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'pending_treated_as_approved')

    def test_unit_license_basis_public_visibility_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herddesk-csharp')['license_basis'] = 'public_visibility'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'public_visibility_as_license_grant')

    def test_missing_name_is_rejected(self):
        register, _, _, _ = _bundle()
        register['names'] = [item for item in register['names'] if item['name'] != '牧台']
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'missing_name_record')

    def test_missing_unit_field_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herddesk-csharp').pop('admission')
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_unknown_admission_value_is_rejected(self):
        register, _, _, _ = _bundle()
        _unit(register, 'herddesk-csharp')['admission'] = 'feasible'
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'unknown_admission_value')

    def test_herdr_source_cannot_stand_in_for_binary(self):
        register, _, _, _ = _bundle()
        register['units'] = [
            item for item in register['units']
            if not (item['name'] == 'herdr' and item['artifact_kind'] == 'prebuilt_binary')
        ]
        with self.assertRaises(LicensingError) as ctx:
            _check(register=register)
        self.assertEqual(str(ctx.exception), 'missing_record_field')


if __name__ == '__main__':
    unittest.main()
