"""The map switch must time out and roll back without calling Azure for real."""

import json
import os
from pathlib import Path
import signal
import stat
import subprocess
import tempfile
import textwrap
import time
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "switch-map-provider.sh"

FAKE_AZ = textwrap.dedent(
    """\
    #!/usr/bin/env python3
    import json
    import os
    import sys

    args = sys.argv[1:]
    with open(os.environ["FAKE_AZ_LOG"], "a", encoding="utf-8") as log:
        log.write(json.dumps(args) + "\\n")
    if args[:4] == ["webapp", "config", "appsettings", "list"]:
        print(json.dumps([
            {"name": "Maps__Provider", "value": "VietMap"},
            {"name": "Maps__Google__ApiKey", "value": "server-key"},
            {"name": "Maps__Google__BrowserKey", "value": "browser-key"},
            {"name": "Maps__VietMap__ApiKey", "value": "viet-key"},
            {"name": "Maps__VietMap__TileKey", "value": "tile-key"},
        ]))
    elif args[:4] != ["webapp", "config", "appsettings", "set"]:
        sys.stderr.write("unexpected az command\\n")
        sys.exit(2)
    """
)

FAKE_CURL = textwrap.dedent(
    """\
    #!/usr/bin/env bash
    mode="${FAKE_CURL_MODE:-google}"
    if [ "$mode" = "hang" ]; then
      sleep 120
    elif [ "$mode" = "old" ]; then
      printf '%s' '{"provider":"VietMap","browserKey":"browser-key"}'
    else
      printf '%s' '{"provider":"Google","browserKey":"browser-key"}'
    fi
    """
)


def providers(log_path: Path) -> list[str]:
    values = []
    if not log_path.exists():
        return values
    for line in log_path.read_text(encoding="utf-8").splitlines():
        args = json.loads(line)
        if args[:4] == ["webapp", "config", "appsettings", "set"]:
            setting = args[args.index("--settings") + 1]
            values.append(setting.removeprefix("Maps__Provider="))
    return values


class SwitchMapProviderTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        root = Path(self.directory.name)
        self.log = root / "az.log"
        bin_dir = root / "bin"
        bin_dir.mkdir()
        for name, body in (("az", FAKE_AZ), ("curl", FAKE_CURL)):
            path = bin_dir / name
            path.write_text(body, encoding="utf-8")
            path.chmod(path.stat().st_mode | stat.S_IEXEC)
        self.env = os.environ.copy()
        self.env.update(
            {
                "PATH": f"{bin_dir}{os.pathsep}{self.env.get('PATH', '')}",
                "FAKE_AZ_LOG": str(self.log),
                "PROVIDER": "Google",
                "AZURE_RESOURCE_GROUP": "rg-test",
                "AZURE_API_APP": "app-test",
                "WEB_BASE_URL": "https://web.example",
                "MAP_SWITCH_ATTEMPTS": "1",
                "MAP_SWITCH_POLL_SECONDS": "0",
            }
        )

    def tearDown(self):
        self.directory.cleanup()

    def run_script(self, mode: str) -> subprocess.CompletedProcess[str]:
        env = self.env.copy()
        env["FAKE_CURL_MODE"] = mode
        return subprocess.run(
            ["bash", str(SCRIPT)],
            env=env,
            text=True,
            capture_output=True,
            timeout=20,
            check=False,
        )

    def test_switch_keeps_google_when_the_site_reports_it(self):
        result = self.run_script("google")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(providers(self.log), ["Google"])
        self.assertNotIn("server-key", result.stdout + result.stderr)
        self.assertNotIn("browser-key", result.stdout + result.stderr)

    def test_mismatch_restores_the_previous_provider(self):
        result = self.run_script("old")
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(providers(self.log), ["Google", "VietMap"])
        self.assertNotIn("browser-key", result.stdout + result.stderr)

    def test_stalled_poll_restores_the_previous_provider_on_termination(self):
        env = self.env.copy()
        env["FAKE_CURL_MODE"] = "hang"
        env["MAP_SWITCH_ATTEMPTS"] = "5"
        process = subprocess.Popen(
            ["bash", str(SCRIPT)],
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            start_new_session=True,
        )
        deadline = time.time() + 15
        while time.time() < deadline and providers(self.log) != ["Google"]:
            time.sleep(0.05)
        self.assertEqual(providers(self.log), ["Google"])
        os.killpg(process.pid, signal.SIGTERM)
        stdout, stderr = process.communicate(timeout=15)
        self.assertNotEqual(process.returncode, 0)
        self.assertEqual(providers(self.log), ["Google", "VietMap"])
        self.assertNotIn("browser-key", f"{stdout}{stderr}")


if __name__ == "__main__":
    unittest.main()
