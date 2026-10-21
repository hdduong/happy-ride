# Logs and Application Insights

The API writes structured JSON logs to standard output in every environment. When `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, its Azure Monitor OpenTelemetry integration also sends server telemetry to Application Insights. Without that setting, local development and CI use console logging and do not export to Azure. The Next.js app is not instrumented by this change.

## Enable Azure telemetry

1. Merge the change to `main` and wait for **Build** and **CI** to pass.
2. Ensure the deployment identity in `AZURE_CREDENTIALS` has Contributor access to `rg-grabbike-dev`, or equivalent rights to read/create `Microsoft.Insights/components`, read/create `Microsoft.OperationalInsights/workspaces`, and update the API App Service settings. Existing resources can require additional access if they belong to another resource group.
3. In **Azure portal → Subscriptions → your subscription → Resource providers**, ensure `Microsoft.Insights` and `Microsoft.OperationalInsights` are registered, alongside the existing `Microsoft.Web` provider. Registration requires subscription-level `/register/action` permission; resource-group Contributor alone cannot perform it. [Provider registration](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-providers-and-types)
4. Open GitHub **Actions → Deployment → Run workflow**, check **Deploy to Azure production**, and run it. The workflow deploys `main` and verifies its Build/CI results before preparing Azure resources.
5. In Azure, open **Application Insights → appi-grabbike-api**. Exercise the rider/driver flow and then inspect **Failures**, **Performance**, and **Logs**. Allow time for buffered telemetry ingestion.

By default, the preparation script creates a Log Analytics workspace and a workspace-based Application Insights component when missing, then sets the API's `APPLICATIONINSIGHTS_CONNECTION_STRING` and `OTEL_SERVICE_NAME=happy-ride-api`. It uses core Azure CLI commands, so the `application-insights` extension is not required. See the [resource CLI](https://learn.microsoft.com/en-us/cli/azure/resource?view=azure-cli-latest#az-resource-create) and [component resource schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2020-02-02/components).

An optional GitHub secret can supply the connection string instead, as described below. When it is absent or empty, the script reads the component's connection string directly from Azure. Both paths mask the value in GitHub Actions and suppress app-settings output. Do not run the scripts with shell tracing or Azure CLI `--debug`. Do not enable App Service's separate codeless Application Insights agent alongside the API's SDK instrumentation.

The helper is called by `.github/scripts/deploy-azure.sh`; editing these files or merging a PR does not provision resources. Resources and application settings change only when the manual deployment runs.

### Supply the connection string through GitHub

1. Create or select an existing workspace-based Application Insights resource in Azure. In its **Overview** page, copy the complete **Connection string**, including its endpoint fields.
2. Open this GitHub repository's **Settings → Environments → production → Environment secrets → Add environment secret**.
3. Name the secret `APPLICATIONINSIGHTS_CONNECTION_STRING`, paste the complete value, and save it. Store it as a secret, not a repository variable or a committed file. A repository Actions secret with the same name is also supported; the `production` environment secret takes precedence if both exist.
4. After **Build** and **CI** are green on `main`, run **Actions → Deployment → Run workflow** with **Deploy to Azure production** checked. The preparation step passes the secret to the script, which applies it to the backend App Service along with `OTEL_SERVICE_NAME=happy-ride-api`. The frontend receives neither setting.
5. Verify telemetry in the Application Insights resource associated with that connection string, using the queries below.

A nonempty secret takes priority over Azure monitoring resource lookup and creation. The supplied Application Insights resource must already exist; this path does not create, validate, or relink monitoring resources. The monitoring variables below are ignored for this path. The deployment identity still needs permission to prepare the App Service resources and update the API settings, but the helper does not need monitoring-resource read/create access when a connection string is supplied.

Every deployment reapplies the current secret. To rotate or replace it, update the GitHub secret and run the deployment again; changing the secret alone does not update a running app. Removing the secret (including a same-named repository secret, if present) restores the default Azure lookup/provisioning path on the next deployment. It does not disable telemetry or clear the existing App Service setting immediately.

## Resource names and reuse

When no connection-string secret is supplied, optional GitHub repository/environment **Variables** configure monitoring:

| Variable | Default | Applied when |
| --- | --- | --- |
| `AZURE_APPLICATION_INSIGHTS` | `appi-grabbike-api` | Selecting or creating the component |
| `AZURE_LOG_ANALYTICS_WORKSPACE` | `log-grabbike` | Selecting/creating a workspace for a new component |
| `AZURE_LOG_RETENTION_DAYS` | `30` | Creating a new workspace |
| `AZURE_LOG_DAILY_CAP_GB` | `1` | Creating a new workspace |

The resources use the deployment script's resource group and region (`rg-grabbike-dev`, `southeastasia` by default). An existing component keeps its workspace link, even if the workspace variable names a different workspace. An existing workspace keeps its retention and cap. Changing variables does not rewrite those existing settings. To adjust retention or volume limits, use the workspace's **Usage and estimated costs** settings deliberately.

If an existing Application Insights component has no workspace link, preparation stops with a clear error instead of silently converting or relinking it. Select a workspace-based component or link the existing component before retrying. Resource lookup failures also stop preparation; they are not treated as a missing resource.

## Find a failed request or ride

The API returns `X-Trace-Id` for request correlation. Unexpected failures return a generic `500` Problem Details response with a trace ID; exception details stay in server telemetry. Expected validation, permission, not-found, and conflict responses keep their existing status codes and are not logged as unexpected exceptions.

Request completion records use `Method`, `Route`, `StatusCode`, `ElapsedMs`, `TraceId`, and `Aborted` fields (event 1000, `RequestCompleted`). Unexpected request exceptions use event 1001, `UnhandledRequestException`. Ride and offer events add identifiers such as `RideId` and `OfferId` where available. Background-worker failures and best-effort notification failures also appear in logs. A delivered SignalR send is not a client acknowledgement; clients still reconcile via polling.

Run these queries in the Application Insights resource's **Logs** view. `operation_Id` correlates request telemetry and logs; it is the distributed trace ID. [Telemetry table names and model](https://learn.microsoft.com/en-us/azure/azure-monitor/app/data-model-complete)

```kusto
requests
| where timestamp > ago(1h)
| where cloud_RoleName == "happy-ride-api"
| project timestamp, name, resultCode, duration, success, operation_Id
| order by timestamp desc
```

Copy a response's `X-Trace-Id` into this query:

```kusto
let traceId = "paste-trace-id";
union withsource=Table requests, traces, exceptions
| where timestamp > ago(24h)
| where operation_Id == traceId or tostring(customDimensions.TraceId) == traceId
| order by timestamp asc
```

Find application records for one ride, including commands handled by different requests:

```kusto
let rideId = "paste-ride-id";
union withsource=Table traces, exceptions
| where timestamp > ago(24h)
| where tostring(customDimensions.RideId) == rideId
| project timestamp, Table, severityLevel, message, outerMessage, operation_Id, customDimensions
| order by timestamp asc
```

When querying from the **Log Analytics workspace** instead, use `AppRequests`, `AppTraces`, and `AppExceptions`. Their corresponding fields are `TimeGenerated`, `OperationId`, and `Properties`, rather than `timestamp`, `operation_Id`, and `customDimensions`. For example:

```kusto
AppTraces
| where TimeGenerated > ago(24h)
| where tostring(Properties.RideId) == "paste-ride-id"
| project TimeGenerated, Message, SeverityLevel, OperationId, Properties
| order by TimeGenerated asc
```

## Collection, cost, and privacy

- Application code logs route templates and technical identifiers. Do not add request/response bodies, raw queries, credentials, address search text, rider/driver names, or GPS coordinates to diagnostic logs. Avoid placing those values in exception messages. Telemetry is operational data, not a durable ride-event audit store.
- Application logs default to Information; ASP.NET Core and EF Core logs default to Warning, and hosting request-diagnostic logs are disabled to avoid raw request paths in their scopes. Request telemetry uses route templates and removes URL/query tags. No request bodies are collected by this instrumentation.
- The demo explicitly uses 100% trace sampling, disables trace-based log sampling, and disables Live Metrics. This favors complete troubleshooting records at the demo's scale, but polling still generates telemetry. It does not guarantee delivery: export failures, ingestion limits, or process shutdown can lose records. Inspect ingestion before increasing verbosity or adding per-position logs. Keep local development and CI connection strings unset.
- New workspaces use pay-as-you-go `PerGB2018` and a 1 GB/day ingestion cap. This is a safeguard, not a guaranteed spending limit: data can exceed the cap before ingestion stops, and reaching it creates monitoring gaps. Existing Application Insights limits can also affect ingestion. Inspect **Usage and estimated costs**, set a budget/alert appropriate for the project, and review [daily-cap behavior](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/daily-cap).
- The default workspace retention is explicitly set to 30 days for new workspaces. Application Insights tables have their own 90-day default retention; a workspace-level value is not a promise that all telemetry is deleted after 30 days. Configure per-table retention when a specific retention period is required. [Retention rules](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/data-retention-configure)
- Charges depend on ingestion, table settings, region, and the subscription's offer. Check current [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/); this setup does not guarantee free monitoring.

## Troubleshooting

- **Console logs but no Azure data:** verify the connection-string setting exists on the API App Service, the running version includes telemetry support, and the process restarted after its settings changed. Check ingestion caps and outbound connectivity, and allow time for export/ingestion. Do not print the connection string in a workflow log.
- **Preparation fails with authorization/provider errors:** confirm the deployment identity's resource-group access and the subscription's provider registrations. An identity restricted to the resource group cannot register subscription providers.
- **Requests visible, custom logs missing:** check logger category filters and the selected time range. Expected conflicts are ordinary results; investigate their request status and the client's response instead of expecting an exception record.
- **No local records:** JSON logs are written to the terminal running `dotnet run`; no file sink is configured. Azure persistence is provided by the telemetry exporter after activation.

Local tests and script checks do not verify live Azure ingestion. After the first manual deployment, complete one ride and use the queries above to confirm request/log correlation and feature identifiers.
