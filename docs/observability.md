# Logs and Application Insights

The API writes structured JSON logs to standard output in every environment. When `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, its Azure Monitor OpenTelemetry integration also sends telemetry to Application Insights. Without that setting, local development and CI use console logging and do not export to Azure. Rider and driver browsers report selected activity and connection events to the API, which resolves the user and exports them through the same logger. The browser does not need an Application Insights connection string.

The Next.js Node server also exports request traces, captured server errors, and structured server events to that same resource. Its service name is `happy-ride-web`; API and browser-activity telemetry use `happy-ride-api`. The connection string is a server runtime setting on both App Services and is never a `NEXT_PUBLIC_` build variable.

## Enable Azure telemetry

1. Merge the change to `main` and wait for **Build** and **CI** to pass.
2. Ensure the deployment identity in `AZURE_CREDENTIALS` has Contributor access to `rg-grabbike-dev`, or equivalent rights to read/create `Microsoft.Insights/components`, read/create `Microsoft.OperationalInsights/workspaces`, and update both App Services' settings. Existing resources can require additional access if they belong to another resource group.
3. In **Azure portal → Subscriptions → your subscription → Resource providers**, ensure `Microsoft.Insights` and `Microsoft.OperationalInsights` are registered, alongside the existing `Microsoft.Web` provider. Registration requires subscription-level `/register/action` permission; resource-group Contributor alone cannot perform it. [Provider registration](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-providers-and-types)
4. Open GitHub **Actions → Deployment → Run workflow**, check **Deploy to Azure production**, and run it. The workflow deploys `main` and verifies its Build/CI results before preparing Azure resources.
5. In Azure, open **Application Insights → appi-grabbike-api**. Exercise the rider/driver flow and then inspect **Failures**, **Performance**, and **Logs**. Allow time for buffered telemetry ingestion.

By default, the preparation script creates a Log Analytics workspace and a workspace-based Application Insights component when missing, then applies the same `APPLICATIONINSIGHTS_CONNECTION_STRING` to the API and Next.js App Services. It sets `OTEL_SERVICE_NAME=happy-ride-api` on the API and `OTEL_SERVICE_NAME=happy-ride-web` on the web server. It uses core Azure CLI commands, so the `application-insights` extension is not required. See the [resource CLI](https://learn.microsoft.com/en-us/cli/azure/resource?view=azure-cli-latest#az-resource-create) and [component resource schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2020-02-02/components).

An optional GitHub secret can supply the connection string instead, as described below. When it is absent or empty, the script reads the component's connection string directly from Azure. Both paths mask the value in GitHub Actions and suppress app-settings output. Do not run the scripts with shell tracing or Azure CLI `--debug`. The helper sets `ApplicationInsightsAgent_EXTENSION_VERSION=disabled` on both apps to avoid running App Service's codeless agent alongside SDK instrumentation.

The helper is called by `.github/scripts/deploy-azure.sh`; editing these files or merging a PR does not provision resources. Resources and application settings change only when the manual deployment runs.

### Supply the connection string through GitHub

1. Create or select an existing workspace-based Application Insights resource in Azure. In its **Overview** page, copy the complete **Connection string**, including its endpoint fields.
2. Open this GitHub repository's **Settings → Environments → production → Environment secrets → Add environment secret**.
3. Name the secret `APPLICATIONINSIGHTS_CONNECTION_STRING`, paste the complete value, and save it. Store it as a secret, not a repository variable or a committed file. A repository Actions secret with the same name is also supported; the `production` environment secret takes precedence if both exist.
4. After **Build** and **CI** are green on `main`, run **Actions → Deployment → Run workflow** with **Deploy to Azure production** checked. The preparation step passes the secret to the script, which applies it to both server App Services with their respective service names. The Node process receives the setting at runtime; browser JavaScript receives neither the connection string nor the Azure SDK.
5. Verify telemetry in the Application Insights resource associated with that connection string, using the queries below.

A nonempty secret takes priority over Azure monitoring resource lookup and creation. The supplied Application Insights resource must already exist; this path does not create, validate, or relink monitoring resources. The monitoring variables below are ignored for this path. The deployment identity still needs permission to prepare the App Service resources and update both apps' settings, but the helper does not need monitoring-resource read/create access when a connection string is supplied.

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

## Next.js server telemetry

Both App Services use the existing Application Insights resource, normally `appi-grabbike-api` linked to `log-grabbike`. Its name does not limit it to API telemetry. Filter by service name to distinguish the two processes:

| App Service | Runtime connection string | Service name |
| --- | --- | --- |
| `app-grabbike-api` | Existing resource's full connection string | `happy-ride-api` |
| `app-grabbike-web` | The same connection string | `happy-ride-web` |

Next.js loads the server SDK through its Node-only instrumentation hook. Native Next.js spans record requests, route patterns, response status, duration, and distributed trace identifiers. The request-error hook records errors captured by Next.js during server rendering, route handlers, or server actions. Structured server events provide startup and diagnostic records. [Next.js instrumentation hooks](https://nextjs.org/docs/15/app/api-reference/file-conventions/instrumentation), [Azure Monitor Node SDK](https://learn.microsoft.com/en-us/javascript/api/overview/azure/monitor-opentelemetry-readme)

The server exports safe route templates instead of raw paths or URLs. Unknown routes are grouped as `(unmatched)`. Query strings such as `?as=...`, request headers, request bodies, and raw exception messages/stacks are excluded. Error types, digests, and sanitized frame positions remain available. Automatic HTTP/library instrumentation, arbitrary console collection, browser SDK injection, and Live Metrics are disabled; Next.js supplies the request spans. This avoids collecting the same request through two instrumentations.

The server logger and request-error hook do not infer a username or ride from browser query strings or headers. Rider/driver usernames and ride membership remain API-resolved in the activity pipeline below. Putting both servers in one resource does not automatically make a page load and a later browser-to-API command the same distributed trace; the existing GUID ride/session/activity fields join the business journey.

The web app reads `APPLICATIONINSIGHTS_CONNECTION_STRING` when its Node process starts. The Azure SDK requires Node 22 or later; deployment selects Node 22 and applies that runtime to existing web apps too. Do not set `NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING`, add the secret to `next.config.ts`, or pass it to the web build. The standalone production artifact includes the Node SDK, while browser assets do not. Local development and CI normally leave the setting unset. Missing or invalid telemetry configuration falls back safely without preventing pages from loading; telemetry export is best effort.

After deploying, load `/rider` or `/driver` and inspect web-server requests:

```kusto
requests
| where timestamp > ago(1h)
| where cloud_RoleName == "happy-ride-web"
| project timestamp, name, resultCode, duration, success, operation_Id
| order by timestamp desc
```

Find web-server events and captured errors:

```kusto
union withsource=Table traces, exceptions
| where timestamp > ago(24h)
| where cloud_RoleName == "happy-ride-web"
| project timestamp, Table, message, outerMessage, operation_Id, customDimensions
| order by timestamp desc
```

To view both services, query `requests`, `traces`, and `exceptions` with `cloud_RoleName in ("happy-ride-api", "happy-ride-web")`. In the workspace, use `AppRequests`, `AppTraces`, `AppExceptions`, `AppRoleName`, `TimeGenerated`, and `OperationId`.

Run `npm test` in `web/` for the unit regressions. After `npm run build`, run `npm run test:server-telemetry` to test the actual standalone artifact against loopback HTTP ingestion with a dummy connection string. The smoke test checks real request export, captured errors, trace correlation, sanitization, missing/invalid configuration, and exporter failures. Its isolated child process explicitly permits HTTP for the local collector and blocks nonlocal connections; production retains the SDK's default HTTPS transport. Build runs this check automatically. Offline deployment tests run with `python -m unittest discover -s .github/scripts/tests -v` from the repository root and replace every Azure CLI operation with a fake.

## Rider and driver activity

The frontend sends a small, typed record to `POST /api/telemetry/activity`. The API validates its identifiers and association with the selected persona, then writes a `ClientActivity` log through the existing Application Insights exporter. Browser activity uses the API exporter and does not expose the connection string in browser JavaScript. No additional Azure resource or browser secret is required.

| Field | Meaning |
| --- | --- |
| `RideId` | Server-generated GUID shared by both participants, HTTP commands, and background ride events. |
| `AppSessionId` | Browser-generated GUID for a tab/persona session, retained across ordinary reconnects. |
| `ActivityId` | GUID identifying one action attempt; requested/result events and the API command share it. Other lifecycle events receive their own GUID. |
| `ActorId`, `ActorRole`, `Username` | Actor GUID, rider/driver role, and display name resolved by the API. The browser does not supply these log fields. |
| `OfferId` | Existing GUID joining dispatch and driver offer activity. |
| `TraceId` | Standard W3C distributed trace ID for one request; it is not reformatted as a GUID. |
| `SignalRConnectionId` | SignalR's opaque connection identifier; it is not a GUID and can change after reconnect. Distinct from Kestrel's transport-level `ConnectionId`. |
| `Source`, `EventName`, `Action` | Distinguish browser reports from committed server events and identify the activity. |

`X-App-Session-Id` and `X-Activity-Id` carry context on API commands. A supplied ride identifier is association-checked before use in telemetry. The app still uses development persona selection, not production authentication: server lookup prevents arbitrary username text from becoming a log identity but does not turn the demo user ID into a secure login.

Known-user request completion and exception records retain the same actor context. Requests without a resolved user use `Username=anonymous`; background worker scopes use `Username=system`. Neither label represents a human account. Correlation queries use a separate database context so diagnostic lookups cannot preload stale entities into a later ride command.

Recorded browser events cover app open/background/resume/close, ride-context start/end, offer display, action requested/succeeded/failed, and connection connected/reconnecting/reconnected/closed/failed. Actions cover booking, accepting/declining offers, arriving, starting the ride, dropping off, cancelling, and confirming arrival. A booking attempt has no `RideId` until the server creates one; its `ActivityId` links the attempt, API command, and successful result. Browser success means the client received and processed its command response; committed backend events remain the source for business outcomes.

Rider ride context ends at `Completed`, `Cancelled`, or `NoDriver`. Driver detailed ride context ends at `DroppedOff` or an earlier terminal outcome. This does not change the domain: the driver remains assigned until the rider confirms arrival. Final command results retain the original ride identifier even if the screen has already cleared its active context. Driver connections remain available for subsequent rides.

Visibility changes and connection loss do not cancel rides or change driver availability. Server hub callbacks record connect/disconnect events, including a disconnect after SignalR detects abrupt network loss. HTTP polling may continue with the socket down. A disconnect is therefore not labelled an app exit or user inactivity. Browser close delivery is best effort; a killed process can lose its final event. There is no app-inactivity timer or cross-instance presence registry in this change.

Client events use a bounded in-memory queue and best-effort delivery: at most 64 pending events, three delivery attempts per event, and a three-second request timeout. The API accepts at most 4 KiB per event and 120 activity requests per minute per persona per API instance. Exceeding a limit does not rate-limit business commands. Telemetry failures must not fail a ride command. `clientOccurredAt` describes when the browser reported the event, but client clocks are untrusted; the log timestamp records API receipt. No event is generated for each GPS point, render, or reconciliation poll. A server SignalR send is still not proof that a person saw the update; per-message acknowledgements are not added here.

Find the combined rider/driver timeline in the Application Insights **Logs** view:

```kusto
let rideId = "paste-ride-guid";
traces
| where timestamp > ago(24h)
| where tostring(customDimensions.RideId) == rideId
| project timestamp,
    Source = tostring(customDimensions.Source),
    ActorRole = tostring(customDimensions.ActorRole),
    Username = tostring(customDimensions.Username),
    EventName = tostring(customDimensions.EventName),
    Action = tostring(customDimensions.Action),
    AppSessionId = tostring(customDimensions.AppSessionId),
    ActivityId = tostring(customDimensions.ActivityId),
    SignalRConnectionId = tostring(customDimensions.SignalRConnectionId),
    message, operation_Id
| order by timestamp asc
```

Find one user's activity across rides:

```kusto
traces
| where timestamp > ago(24h)
| where tostring(customDimensions.ActorId) == "paste-user-guid"
| project timestamp, message, customDimensions, operation_Id
| order by timestamp asc
```

Use `customDimensions.Username` to search by display name, or `customDimensions.AppSessionId` for one tab session. Prefer `ActorId` when names are duplicated or changed. For a failed attempt before booking succeeds, query `customDimensions.ActivityId`. In the Log Analytics workspace use `AppTraces`, `TimeGenerated`, and `Properties` instead.

## Collection, cost, and privacy

- Application code logs route templates, technical identifiers, and the user-requested `Username` field, resolved from the existing user record's `Name`. This is a display name, not a separate login name. Usernames are personal information and are included in console and Application Insights logs; apply the workspace's access and retention controls accordingly. Do not add request/response bodies, raw queries, credentials, address search text, or GPS coordinates. Avoid placing those values in exception messages. Telemetry is operational data, not a durable ride-event audit store.
- Application logs default to Information; ASP.NET Core and EF Core logs default to Warning, and hosting request-diagnostic logs are disabled to avoid raw request paths in their scopes. Request telemetry uses route templates and removes URL/query tags. No request bodies are collected by this instrumentation.
- The demo explicitly uses 100% trace sampling, disables trace-based log sampling, and disables Live Metrics. This favors complete troubleshooting records at the demo's scale, but polling still generates telemetry. It does not guarantee delivery: export failures, ingestion limits, or process shutdown can lose records. Inspect ingestion before increasing verbosity or adding per-position logs. Keep local development and CI connection strings unset.
- New workspaces use pay-as-you-go `PerGB2018` and a 1 GB/day ingestion cap. This is a safeguard, not a guaranteed spending limit: data can exceed the cap before ingestion stops, and reaching it creates monitoring gaps. Existing Application Insights limits can also affect ingestion. Inspect **Usage and estimated costs**, set a budget/alert appropriate for the project, and review [daily-cap behavior](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/daily-cap).
- The default workspace retention is explicitly set to 30 days for new workspaces. Application Insights tables have their own 90-day default retention; a workspace-level value is not a promise that all telemetry is deleted after 30 days. Configure per-table retention when a specific retention period is required. [Retention rules](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/data-retention-configure)
- Charges depend on ingestion, table settings, region, and the subscription's offer. Check current [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/); this setup does not guarantee free monitoring.

## Troubleshooting

- **Console logs but no Azure data:** verify the connection-string setting exists on the relevant App Service, the running version includes telemetry support, and the process restarted after its settings changed. Filter the resource by `happy-ride-api` or `happy-ride-web` as appropriate. Check ingestion caps and outbound connectivity, and allow time for export/ingestion. Do not print the connection string in a workflow log.
- **Preparation fails with authorization/provider errors:** confirm the deployment identity's resource-group access and the subscription's provider registrations. An identity restricted to the resource group cannot register subscription providers.
- **Requests visible, custom logs missing:** check logger category filters and the selected time range. Expected conflicts are ordinary results; investigate their request status and the client's response instead of expecting an exception record.
- **No local records:** JSON logs are written to the terminal running `dotnet run`; no file sink is configured. Azure persistence is provided by the telemetry exporter after activation.

Local tests and script checks do not verify live Azure ingestion. After the first manual deployment, complete one ride and use the queries above to confirm request/log correlation and feature identifiers.
