"""Exercise monitoring setup offline; every Azure command uses a local fake."""

import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "configure-observability.sh"
DEPLOY_SCRIPT = SCRIPT.with_name("deploy-azure.sh")
CONNECTION = "InstrumentationKey=test-only;IngestionEndpoint=https://example.invalid/"
WORKSPACE = "/subscriptions/test/resourceGroups/shared/providers/Microsoft.OperationalInsights/workspaces/existing"

FAKE_AZ = r'''
import json
import os
from pathlib import Path
import sys

args = sys.argv[1:]
with open(os.environ["FAKE_AZ_LOG"], "a", encoding="utf-8") as log:
    log.write(json.dumps(args) + "\n")

def value(flag):
    return args[args.index(flag) + 1]

operation = None
response = ""
scenario = os.environ["FAKE_AZ_SCENARIO"]
if args[:2] == ["webapp", "list-runtimes"]:
    operation = "list-runtimes"
    response = os.environ.get("FAKE_RUNTIMES", "DOTNETCORE|10.0\t2030-01-01\nNODE|22-lts\t2030-01-01\nNODE|20-lts\t2026-01-01")
elif args[:3] == ["appservice", "plan", "show"] or args[:2] == ["webapp", "show"]:
    if scenario == "deploy-new":
        raise SystemExit(3)
    operation = "show-app-or-plan"
    # Existing deployments may still be configured for Node 20.
    response = '{"siteConfig":{"linuxFxVersion":"NODE|20-lts"}}'
elif args[:3] == ["appservice", "plan", "create"] or args[:2] == ["webapp", "create"]:
    operation = "create-app-or-plan"
elif args[:3] == ["webapp", "config", "set"]:
    operation = "runtime-web" if "--linux-fx-version" in args else "config-api"
elif args[:2] == ["webapp", "update"]:
    operation = "update-app"
elif args[:2] == ["resource", "list"]:
    if value("--resource-type") == "Microsoft.Insights/components":
        operation = "list-insights"
        response = "/subscriptions/test/components/existing" if scenario in ("reuse", "classic", "empty-connection") else ""
    else:
        operation = "list-workspace"
        response = os.environ["FAKE_WORKSPACE"] if scenario == "existing-workspace" else ""
elif args[:3] == ["monitor", "log-analytics", "workspace"]:
    operation = "create-workspace" if args[3] == "create" else "show-workspace"
    if args[3] == "show":
        response = os.environ["FAKE_WORKSPACE"]
elif args[:2] == ["resource", "create"]:
    operation = "create-insights"
elif args[:2] == ["resource", "show"]:
    if value("--query") == "properties.WorkspaceResourceId":
        operation = "show-insights-workspace"
        response = "" if scenario == "classic" else os.environ["FAKE_WORKSPACE"]
    else:
        operation = "show-connection"
        response = "" if scenario == "empty-connection" else os.environ["FAKE_CONNECTION"]
elif args[:4] == ["webapp", "config", "appsettings", "set"]:
    operation = "settings-web" if value("--name") == os.environ.get("AZURE_WEB_APP", "app-grabbike-web") else "settings-api"
    # A real settings response contains credentials. Make an accidental missing
    # --output none visible to the test instead of silently ignoring it.
    if "--output" not in args or value("--output") != "none":
        response = os.environ["FAKE_CONNECTION"]
else:
    raise SystemExit("Unexpected Azure operation: " + repr(args))

if operation == os.environ.get("FAKE_AZ_FAIL"):
    print("Simulated Azure failure: " + operation, file=sys.stderr)
    raise SystemExit(17)
if response:
    print(response)
'''


def bash_executable():
    if os.name == "nt":
        git_bash = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"
        if git_bash.exists():
            return str(git_bash)
    executable = shutil.which("bash")
    if executable:
        return executable
    raise RuntimeError("Bash is required to test deployment scripts")


class ObservabilityDeploymentTests(unittest.TestCase):
    def run_configuration(self, scenario="reuse", supplied=False, fail="", script=SCRIPT, **overrides):
        with tempfile.TemporaryDirectory(prefix="happy-ride-observability-") as directory:
            directory = Path(directory)
            fake = directory / "fake_az.py"
            fake.write_text(FAKE_AZ, encoding="utf-8")
            log = directory / "calls.jsonl"
            executable = shlex.quote(Path(sys.executable).as_posix())
            # Both wrappers are local executables. python3 also makes this
            # suite work under Git Bash without relying on the Windows alias.
            wrappers = {
                "az": f"exec {executable} {shlex.quote(fake.as_posix())} \"$@\"\n",
                "python3": f"exec {executable} \"$@\"\n",
            }
            for name, command in wrappers.items():
                wrapper = directory / name
                wrapper.write_text("#!/usr/bin/env bash\n" + command, encoding="utf-8", newline="\n")
                wrapper.chmod(0o755)
            environment = os.environ.copy()
            for name in list(environment):
                if name.startswith("AZURE_") or name in ("APPLICATIONINSIGHTS_CONNECTION_STRING", "GITHUB_ACTIONS"):
                    environment.pop(name)
            environment.update({
                "PATH": str(directory) + os.pathsep + environment["PATH"],
                "FAKE_AZ_LOG": log.as_posix(),
                "FAKE_AZ_SCENARIO": scenario,
                "FAKE_AZ_FAIL": fail,
                "FAKE_CONNECTION": CONNECTION,
                "FAKE_WORKSPACE": WORKSPACE,
                # Azure resource IDs are not filesystem paths. Match Linux CI
                # instead of letting Git Bash rewrite them for native Python.
                "MSYS_NO_PATHCONV": "1",
            })
            if supplied:
                environment["APPLICATIONINSIGHTS_CONNECTION_STRING"] = CONNECTION
            environment.update(overrides)
            result = subprocess.run(
                [bash_executable(), "-x", script.as_posix()],
                env=environment, capture_output=True, text=True, timeout=30,
            )
            calls = [json.loads(line) for line in log.read_text(encoding="utf-8").splitlines()] if log.exists() else []
            return result, calls

    def assert_app_settings(self, calls, resource_group="rg-grabbike-dev", api="app-grabbike-api", web="app-grabbike-web"):
        settings = [call for call in calls if call[:4] == ["webapp", "config", "appsettings", "set"]]
        self.assertEqual(len(settings), 2)
        for call, app, role in zip(settings, (api, web), ("happy-ride-api", "happy-ride-web")):
            self.assertEqual(call[call.index("--resource-group") + 1], resource_group)
            self.assertEqual(call[call.index("--name") + 1], app)
            self.assertIn("APPLICATIONINSIGHTS_CONNECTION_STRING=" + CONNECTION, call)
            self.assertIn("OTEL_SERVICE_NAME=" + role, call)
            self.assertIn("ApplicationInsightsAgent_EXTENSION_VERSION=disabled", call)
            self.assertEqual(call[call.index("--output") + 1], "none")

    def assert_no_secret_output(self, result):
        self.assertNotIn(CONNECTION, result.stdout + result.stderr)

    def test_supplied_secret_configures_both_apps_without_resource_lookup(self):
        result, calls = self.run_configuration(supplied=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(calls), 2)
        self.assert_app_settings(calls)
        self.assert_no_secret_output(result)

    def test_existing_insights_and_workspace_are_reused_without_mutation(self):
        result, calls = self.run_configuration()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_app_settings(calls)
        self.assertFalse(any(call[:2] == ["resource", "create"] for call in calls))
        self.assertFalse(any(call[:2] == ["monitor", "log-analytics"] for call in calls))
        self.assert_no_secret_output(result)

    def test_missing_resources_create_one_workspace_linked_component(self):
        result, calls = self.run_configuration(scenario="new")
        self.assertEqual(result.returncode, 0, result.stderr)
        creates = [call for call in calls if call[:2] == ["resource", "create"]]
        self.assertEqual(len(creates), 1)
        properties = json.loads(creates[0][creates[0].index("--properties") + 1])
        self.assertEqual(properties["properties"]["WorkspaceResourceId"], WORKSPACE)
        self.assertEqual(properties["properties"]["IngestionMode"], "LogAnalytics")
        self.assertTrue(any(call[:4] == ["monitor", "log-analytics", "workspace", "create"] for call in calls))
        self.assert_app_settings(calls)
        self.assert_no_secret_output(result)

    def test_existing_workspace_is_reused_for_new_component(self):
        result, calls = self.run_configuration(scenario="existing-workspace")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(any(call[:4] == ["monitor", "log-analytics", "workspace", "create"] for call in calls))
        self.assert_app_settings(calls)
        self.assert_no_secret_output(result)

    def test_custom_app_and_resource_group_names_are_respected(self):
        result, calls = self.run_configuration(
            supplied=True, AZURE_RESOURCE_GROUP="rg-custom", AZURE_API_APP="api-custom", AZURE_WEB_APP="web-custom",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_app_settings(calls, resource_group="rg-custom", api="api-custom", web="web-custom")

    def test_github_mask_is_emitted_once_before_setting_either_app(self):
        connection = CONNECTION + "value=100%\r\nadditional=value"
        result, calls = self.run_configuration(
            supplied=True, GITHUB_ACTIONS="true", APPLICATIONINSIGHTS_CONNECTION_STRING=connection,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        masked = connection.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        self.assertEqual(result.stdout.count("::add-mask::"), 1)
        self.assertTrue(result.stdout.startswith("::add-mask::" + masked + "\n"))
        self.assertNotIn(connection, result.stdout + result.stderr)
        self.assertNotIn(CONNECTION, result.stderr)
        self.assertEqual(len(calls), 2)

    def test_every_azure_failure_stops_configuration(self):
        operations = (
            "list-insights", "list-workspace", "create-workspace", "show-workspace",
            "create-insights", "show-insights-workspace", "show-connection", "settings-api", "settings-web",
        )
        for operation in operations:
            with self.subTest(operation=operation):
                result, calls = self.run_configuration(scenario="new", fail=operation)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Simulated Azure failure: " + operation, result.stderr)
                self.assertNotIn("telemetry configured", result.stdout)
                self.assert_no_secret_output(result)
                if operation == "settings-api":
                    self.assertFalse(any("app-grabbike-web" in call for call in calls))

    def test_supplied_secret_still_fails_if_either_app_cannot_be_configured(self):
        for operation in ("settings-api", "settings-web"):
            with self.subTest(operation=operation):
                result, _ = self.run_configuration(supplied=True, fail=operation)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn("telemetry configured", result.stdout)
                self.assert_no_secret_output(result)

    def test_classic_or_empty_connection_resources_fail_before_app_settings(self):
        for scenario in ("classic", "empty-connection"):
            with self.subTest(scenario=scenario):
                result, calls = self.run_configuration(scenario=scenario)
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(any(call[:1] == ["webapp"] for call in calls))
                self.assert_no_secret_output(result)

    def test_existing_node_20_web_app_is_upgraded_to_node_22(self):
        result, calls = self.run_configuration(scenario="deploy-existing", supplied=True, script=DEPLOY_SCRIPT)
        self.assertEqual(result.returncode, 0, result.stderr)
        configurations = [call for call in calls if call[:3] == ["webapp", "config", "set"] and "app-grabbike-web" in call]
        self.assertEqual(len(configurations), 1)
        configuration = configurations[0]
        self.assertEqual(configuration[configuration.index("--linux-fx-version") + 1], "NODE|22-lts")
        self.assertEqual(
            configuration[configuration.index("--startup-file") + 1],
            "rm -rf .next/server/route-cache .next/cache && node server.js",
        )
        self.assertEqual(configuration[configuration.index("--always-on") + 1], "true")
        self.assertFalse(any(call[:2] == ["webapp", "create"] for call in calls))
        self.assert_no_secret_output(result)

    def test_new_web_app_uses_node_22_and_ignores_runtime_metadata(self):
        result, calls = self.run_configuration(
            scenario="deploy-new", supplied=True, script=DEPLOY_SCRIPT,
            FAKE_RUNTIMES="DOTNETCORE:10.0\t2030-01-01\nNODE:22-lts\t2030-01-01\nNODE:20-lts\t2026-01-01",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        creates = [call for call in calls if call[:2] == ["webapp", "create"] and "app-grabbike-web" in call]
        self.assertEqual(len(creates), 1)
        self.assertEqual(creates[0][creates[0].index("--runtime") + 1], "NODE|22-lts")
        self.assert_no_secret_output(result)

    def test_node_20_only_subscription_fails_before_provisioning(self):
        result, calls = self.run_configuration(
            supplied=True, script=DEPLOY_SCRIPT,
            FAKE_RUNTIMES="DOTNETCORE|10.0\t2030-01-01\nNODE|20-lts\t2026-01-01",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Linux Node.js 22 is required", result.stderr)
        self.assertTrue(calls)
        self.assertTrue(all(call[:2] == ["webapp", "list-runtimes"] for call in calls))
        self.assert_no_secret_output(result)

    def test_web_runtime_update_failure_stops_deployment(self):
        result, calls = self.run_configuration(
            scenario="deploy-existing", supplied=True, fail="runtime-web", script=DEPLOY_SCRIPT,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Simulated Azure failure: runtime-web", result.stderr)
        self.assertNotIn("Web: https://", result.stdout)
        self.assert_no_secret_output(result)


if __name__ == "__main__":
    unittest.main()
