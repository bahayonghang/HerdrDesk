from pathlib import Path
import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SETUP_SCRIPT = ROOT / 'scripts' / 'Invoke-HerdDeskDotnetSetup.ps1'
GLOBAL_JSON = ROOT / 'global.json'

RUNNER = r'''
param(
    [Parameter(Mandatory = $true)][string]$HerdDeskSetupScript,
    [Parameter(Mandatory = $true)][string]$HerdDeskResultPath,
    [Parameter(Mandatory = $true)][string]$HerdDeskMachineHost,
    [string]$HerdDeskExpectedSdk = '',
    [string]$HerdDeskInstall = '0',
    [string]$HerdDeskPersist = '0',
    [string]$HerdDeskCreateSdkOnInstall = '0'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$calls = New-Object System.Collections.Generic.List[object]
. $HerdDeskSetupScript
function Install-PinnedSdk {
    param(
        [Parameter(Mandatory = $true)][string]$SdkVersion,
        [Parameter(Mandatory = $true)][string]$MachineHost
    )
    $calls.Add([ordered]@{ sink = 'Install-PinnedSdk'; SdkVersion = $SdkVersion; MachineHost = $MachineHost })
    if ($HerdDeskCreateSdkOnInstall -eq '1') {
        $dir = Join-Path $MachineHost "sdk\$SdkVersion"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
}
function Set-UserDotnetEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$MachineHost
    )
    $calls.Add([ordered]@{ sink = 'Set-UserDotnetEnvironment'; MachineHost = $MachineHost })
}
$exitCode = 0
$errorMessage = ''
try {
    $invoke = @{ MachineHost = $HerdDeskMachineHost }
    if ($HerdDeskExpectedSdk) { $invoke.ExpectedSdk = $HerdDeskExpectedSdk }
    if ($HerdDeskInstall -eq '1') { $invoke.InstallPinnedSdk = $true }
    if ($HerdDeskPersist -eq '1') { $invoke.PersistUserEnvironment = $true }
    Invoke-HerdDeskDotnetSetup @invoke
} catch {
    $exitCode = 1
    $errorMessage = [string]$_.Exception.Message
}
$ndjson = @()
foreach ($call in $calls) {
    $ndjson += ($call | ConvertTo-Json -Compress -Depth 4)
}
[ordered]@{
    exit_code = $exitCode
    error = $errorMessage
    call_count = $calls.Count
    call_order = (($calls | ForEach-Object { $_.sink }) -join ',')
    calls_ndjson = ($ndjson -join "`n")
} | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $HerdDeskResultPath -Encoding utf8NoBOM
if ($exitCode -ne 0) { exit $exitCode }
'''


def _function_body(source, name):
    token = f'function {name}'
    start = source.index(token)
    rest = source[start + len(token):]
    next_fn = rest.find('\nfunction ')
    next_entry = rest.find('\nif ($script:')
    ends = [index for index in (next_fn, next_entry) if index != -1]
    end = min(ends) if ends else len(rest)
    return rest[:end]


def _pinned_sdk():
    return json.loads(GLOBAL_JSON.read_text(encoding='utf-8'))['sdk']['version']


class SetupScriptContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SETUP_SCRIPT.read_text(encoding='utf-8')
        cls.pinned = _pinned_sdk()

    def test_global_json_is_only_sdk_pin(self):
        self.assertNotRegex(self.source, r"ExpectedSdk\s*=\s*'10\.0\.400'")
        self.assertNotRegex(self.source, r"ExpectedSdk\s*=\s*\"10\.0\.400\"")
        self.assertIn("Get-Content -LiteralPath $path -Raw -Encoding utf8", self.source)
        self.assertIn('global.json', self.source)
        self.assertIn(self.pinned, json.dumps(json.loads(GLOBAL_JSON.read_text(encoding='utf-8'))))

    def test_winget_only_in_install_sink(self):
        install = _function_body(self.source, 'Install-PinnedSdk')
        user = _function_body(self.source, 'Set-UserDotnetEnvironment')
        main = _function_body(self.source, 'Invoke-HerdDeskDotnetSetup')
        self.assertIn('winget', install)
        self.assertNotIn('winget', user)
        self.assertNotIn('winget', main)
        self.assertIn('Install-PinnedSdk -SdkVersion', main)

    def test_user_environment_writes_only_in_user_sink(self):
        user = _function_body(self.source, 'Set-UserDotnetEnvironment')
        install = _function_body(self.source, 'Install-PinnedSdk')
        main = _function_body(self.source, 'Invoke-HerdDeskDotnetSetup')
        self.assertIn('[Environment]::SetEnvironmentVariable', user)
        self.assertIn("'User'", user)
        self.assertNotIn('[Environment]::SetEnvironmentVariable', install)
        self.assertNotIn('[Environment]::SetEnvironmentVariable', main)
        self.assertIn('Set-UserDotnetEnvironment -MachineHost', main)
        self.assertIn('if ($PersistUserEnvironment)', main)
        self.assertGreater(
            main.find('Set-UserDotnetEnvironment -MachineHost'),
            main.find('(& $dotnet --version)'),
        )
        setters = re.findall(r"\[Environment\]::SetEnvironmentVariable\((.*)\)", self.source)
        self.assertEqual(len(setters), 2)
        for args in setters:
            self.assertIn("'User'", args)

    def test_script_documents_parent_shell_path_limit(self):
        lowered = self.source.lower()
        self.assertIn('do not return to the parent', lowered)
        self.assertIn('just build', lowered)
        self.assertIn('InstallPinnedSdk', self.source)
        self.assertIn('PersistUserEnvironment', self.source)

    def test_direct_execution_calls_main_function(self):
        self.assertIn('HerdDeskDotnetSetupDirect', self.source)
        self.assertIn('Invoke-HerdDeskDotnetSetup @PSBoundParameters', self.source)
        self.assertIn("$MyInvocation.InvocationName -ne '.'", self.source)

    def test_justfile_setup_is_read_only(self):
        just = (ROOT / 'justfile').read_text(encoding='utf-8')
        self.assertIn('Does not install the SDK or write User environment.', just)
        self.assertRegex(
            just,
            r'(?m)^    pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup\.ps1\r?$',
        )
        self.assertNotIn('-InstallPinnedSdk', just.split('setup:', 1)[1].split('[unix]', 1)[0])
        self.assertNotIn('-PersistUserEnvironment', just.split('setup:', 1)[1].split('[unix]', 1)[0])
        self.assertNotRegex(just, r"ExpectedSdk\s*=\s*'10\.0\.400'")
        self.assertNotIn('10.0.400', just)


class SetupFlowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if os.name != 'nt':
            raise unittest.SkipTest('PowerShell setup flow tests run on Windows')
        cls.pwsh = shutil.which('pwsh')
        if not cls.pwsh:
            raise AssertionError('pwsh is missing; Windows setup tests require PowerShell 7')
        cls.pinned = _pinned_sdk()
        cls.tmpdir = tempfile.TemporaryDirectory(prefix='herddesk-setup-tests-')
        cls.runner = Path(cls.tmpdir.name) / 'run-setup-case.ps1'
        cls.runner.write_text(RUNNER.strip() + '\n', encoding='utf-8', newline='\n')
        cls.result_serial = 0

    @classmethod
    def tearDownClass(cls):
        cls.tmpdir.cleanup()

    def make_host(self, *, sdk=True, version=None):
        host = Path(tempfile.mkdtemp(prefix='herddesk-dotnet-host-', dir=self.tmpdir.name))
        if sdk:
            (host / 'sdk' / self.pinned).mkdir(parents=True)
        if version is not None:
            (host / 'dotnet.cmd').write_text(
                f'@echo off\r\necho {version}\r\nexit /b 0\r\n',
                encoding='ascii',
            )
        return host

    def run_flow(self, host, *, expected_sdk='', install=False, persist=False,
                 create_sdk_on_install=False):
        SetupFlowTests.result_serial += 1
        result_path = Path(self.tmpdir.name) / f'setup-result-{SetupFlowTests.result_serial}.json'
        cmd = [
            self.pwsh, '-NoProfile', '-NonInteractive', '-File', str(self.runner),
            '-HerdDeskSetupScript', str(SETUP_SCRIPT),
            '-HerdDeskResultPath', str(result_path),
            '-HerdDeskMachineHost', str(host),
        ]
        if expected_sdk:
            cmd += ['-HerdDeskExpectedSdk', expected_sdk]
        if install:
            cmd += ['-HerdDeskInstall', '1']
        if persist:
            cmd += ['-HerdDeskPersist', '1']
        if create_sdk_on_install:
            cmd += ['-HerdDeskCreateSdkOnInstall', '1']
        completed = subprocess.run(
            cmd, cwd=str(ROOT), capture_output=True, text=True, timeout=60,
        )
        self.assertTrue(result_path.is_file(),
                        f'setup runner wrote no result\nstdout:\n{completed.stdout}\nstderr:\n{completed.stderr}')
        data = json.loads(result_path.read_text(encoding='utf-8-sig'))
        calls = []
        raw = data.get('calls_ndjson') or ''
        if raw.strip():
            for line in raw.splitlines():
                if line.strip():
                    calls.append(json.loads(line))
        order = data.get('call_order') or ''
        if order and not isinstance(order, str):
            order = ','.join(order)
        return {
            'returncode': completed.returncode,
            'stdout': completed.stdout,
            'stderr': completed.stderr,
            'exit_code': int(data['exit_code']),
            'error': data.get('error') or '',
            'call_count': int(data['call_count']),
            'call_order': order,
            'calls': calls,
        }

    def assert_sinks_unused(self, result):
        self.assertEqual(result['call_count'], 0)
        self.assertEqual(result['calls'], [])
        self.assertEqual(result['call_order'], '')
        self.assertNotIn('winget', result['stdout'].lower())
        self.assertNotIn('winget', result['stderr'].lower())
        self.assertNotIn('Set User DOTNET_ROOT', result['stdout'])
        self.assertNotIn('[Environment]::SetEnvironmentVariable', result['stdout'])

    def test_happy_default_does_not_use_sinks(self):
        host = self.make_host(sdk=True, version=self.pinned)
        result = self.run_flow(host)
        self.assertEqual(result['exit_code'], 0, result['error'] or result['stderr'])
        self.assertEqual(result['returncode'], 0)
        self.assert_sinks_unused(result)
        self.assertIn('Read-only check complete', result['stdout'])
        self.assertIn('User environment was not written', result['stdout'])
        self.assertIn('do not return to the parent shell', result['stdout'])
        self.assertIn('just build', result['stdout'])

    def test_missing_sdk_default_exits_nonzero_without_sinks(self):
        host = self.make_host(sdk=False)
        result = self.run_flow(host)
        self.assertNotEqual(result['exit_code'], 0)
        self.assertNotEqual(result['returncode'], 0)
        self.assert_sinks_unused(result)
        self.assertIn('missing', result['error'].lower())
        self.assertIn(self.pinned, result['error'])
        self.assertIn('-InstallPinnedSdk', result['error'])

    def test_global_json_mismatch_exits_nonzero_without_sinks(self):
        host = self.make_host(sdk=True, version=self.pinned)
        result = self.run_flow(host, expected_sdk='9.0.100', install=True, persist=True)
        self.assertNotEqual(result['exit_code'], 0)
        self.assertNotEqual(result['returncode'], 0)
        self.assert_sinks_unused(result)
        self.assertIn('global.json sdk.version is', result['error'])
        self.assertIn(self.pinned, result['error'])
        self.assertIn('9.0.100', result['error'])

    def test_path_host_mismatch_exits_nonzero_without_sinks(self):
        host = self.make_host(sdk=True, version='8.0.0')
        result = self.run_flow(host)
        self.assertNotEqual(result['exit_code'], 0)
        self.assertNotEqual(result['returncode'], 0)
        self.assert_sinks_unused(result)
        self.assertIn('dotnet --version is', result['error'])
        self.assertIn('8.0.0', result['error'])
        self.assertIn(self.pinned, result['error'])

    def test_host_mismatch_ignores_persist_opt_in(self):
        host = self.make_host(sdk=True, version='8.0.0')
        result = self.run_flow(host, install=True, persist=True)
        self.assertNotEqual(result['exit_code'], 0)
        self.assertNotEqual(result['returncode'], 0)
        self.assert_sinks_unused(result)
        self.assertIn('dotnet --version is', result['error'])
        self.assertIn('8.0.0', result['error'])

    def test_precheck_failure_ignores_opt_in_switches(self):
        host = self.make_host(sdk=False)
        result = self.run_flow(host, expected_sdk='1.0.0', install=True, persist=True,
                               create_sdk_on_install=True)
        self.assertNotEqual(result['exit_code'], 0)
        self.assert_sinks_unused(result)
        self.assertIn('global.json sdk.version is', result['error'])

    def test_explicit_install_uses_install_fake_only(self):
        host = self.make_host(sdk=False, version=self.pinned)
        result = self.run_flow(host, install=True, create_sdk_on_install=True)
        self.assertEqual(result['exit_code'], 0, result['error'] or result['stderr'])
        self.assertEqual(result['call_count'], 1)
        self.assertEqual(result['call_order'], 'Install-PinnedSdk')
        self.assertEqual(result['calls'][0]['sink'], 'Install-PinnedSdk')
        self.assertEqual(result['calls'][0]['SdkVersion'], self.pinned)
        self.assertEqual(Path(result['calls'][0]['MachineHost']), host)
        self.assertNotIn('winget', result['stdout'].lower())
        self.assertNotIn('Set User DOTNET_ROOT', result['stdout'])

    def test_explicit_persist_uses_user_fake_only(self):
        host = self.make_host(sdk=True, version=self.pinned)
        result = self.run_flow(host, persist=True)
        self.assertEqual(result['exit_code'], 0, result['error'] or result['stderr'])
        self.assertEqual(result['call_count'], 1)
        self.assertEqual(result['call_order'], 'Set-UserDotnetEnvironment')
        self.assertEqual(result['calls'][0]['sink'], 'Set-UserDotnetEnvironment')
        self.assertEqual(Path(result['calls'][0]['MachineHost']), host)
        self.assertNotIn('winget', result['stdout'].lower())
        self.assertNotIn('[Environment]::SetEnvironmentVariable', result['stdout'])

    def test_explicit_opt_in_records_install_then_user(self):
        host = self.make_host(sdk=False, version=self.pinned)
        result = self.run_flow(host, install=True, persist=True, create_sdk_on_install=True)
        self.assertEqual(result['exit_code'], 0, result['error'] or result['stderr'])
        self.assertEqual(result['call_count'], 2)
        self.assertEqual(result['call_order'], 'Install-PinnedSdk,Set-UserDotnetEnvironment')
        self.assertEqual([call['sink'] for call in result['calls']],
                         ['Install-PinnedSdk', 'Set-UserDotnetEnvironment'])
        self.assertEqual(result['calls'][0]['SdkVersion'], self.pinned)
        self.assertEqual(Path(result['calls'][0]['MachineHost']), host)
        self.assertEqual(Path(result['calls'][1]['MachineHost']), host)
        self.assertNotIn('winget', result['stdout'].lower())
        self.assertNotIn('[Environment]::SetEnvironmentVariable', result['stdout'])


if __name__ == '__main__':
    unittest.main()
