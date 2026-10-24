# Application architecture

The current application is a set of ASP.NET Core services and one Next.js application. Business features own their behavior and contracts. The services share one database so a ride command stays a single transaction. Infrastructure supplies persistence and realtime delivery.

This document describes the implemented demo. [The build plan](build-plan.md) describes the target MVP, including integrations that are still pending.

## Backend

```text
src/GrabBike.Api/        public edge. Hosts every service in one process for deploy and tests.
src/GrabBike.Rides/      ride lifecycle. Port 5081. Calls dispatch and realtime over HTTP.
src/GrabBike.Dispatch/   offers, nearest driver, round expiry. Port 5082.
src/GrabBike.Fleet/      drivers, simulation, places, and route quotes. Port 5083.
src/GrabBike.Realtime/   SignalR hub. Port 5084.
src/GrabBike.Shared/     contracts, identity, telemetry, and the shared database.
```

`dotnet run --project src/GrabBike.Api` serves the whole API at http://localhost:5080 in one process. To run four processes and keep the browser on that same address, start Rides, Dispatch, Fleet, and Realtime, then start the API with the `split` profile. That edge forwards `/api/rides`, `/api/offers`, map routes, and `/hubs/ride` to the owning service. Dev users and telemetry stay on the edge, which is the one origin the web app already uses. Production publish starts `GrabBike.Api` with no service URLs, so Azure stays one process.

Each feature keeps its models, contracts, application services, and repository interfaces together. A controller is the HTTP adapter for a feature: it validates transport input and maps application outcomes to status codes and JSON. Application services own use cases and depend on repository interfaces; they do not choose HTTP statuses or access the EF context directly.

Persistence implementations depend on the feature contracts. `Program.cs` wires implementations to interfaces. Cross-feature dependencies are explicit imports and narrow contracts. Shared code belongs in `SharedKernel` only when multiple features need the same concept; it is not a destination for unrelated models.

Driver movement and dispatch expiration have separate services and background workers with their own scopes and error boundaries. Each worker invokes its feature's application service; the ride service handles ride lifecycle use cases.

State is saved before realtime notifications are sent. Notification delivery is best effort: a delivery failure is logged and does not change a successful, committed HTTP command into an error. Clients poll to reconcile current ride and offer state. This is not durable event delivery; reliable replay would require an outbox or another persistent mechanism.

Ride writes take one Postgres advisory lock, so separate service processes cannot accept and cancel the same ride at the same time. Database creation and seeding use that same lock.

The HTTP boundary records request outcomes and handles unexpected exceptions centrally. Feature services add structured ride/offer identifiers to business logs; they do not depend on an Azure telemetry SDK. JSON console output is always available, and Azure Monitor export is enabled only when the API has `APPLICATIONINSIGHTS_CONNECTION_STRING`. The existing manual deployment applies an optional GitHub connection-string secret, or creates/reuses the monitoring resources and reads the connection string from Azure when no secret is supplied. See [observability](observability.md) for activation, queries, collection limits, and retention behavior.

## Frontend

```text
web/
  app/                Next.js routes and layout
  features/
    rider/            booking behavior and rider presentation
    driver/           driver actions, offers, and presentation
    rides/            shared ride state, polling, and realtime lifecycle
    maps/             map rendering, geometry, places, and quotes
    identity/         demo user selection and route guard
  shared/
    api/              HTTP transport and common request/error handling
    telemetry/        app session correlation and bounded activity delivery
    server-observability/ Next.js Node telemetry setup and safe server events
    ui/               reusable presentation components
```

Route files compose feature entry points. Rider and driver hooks own screen behavior; components render their state and call actions. Shared ride-session logic owns reconciliation between polling, realtime events, and command results so both screens use the same ordering rules.

A poll represents the state when its request began. If a newer event or command changes the session while that request is running, its response must not replace the newer state. Terminal rides and closed offers also need explicit handling to prevent delayed messages from bringing them back. Disposing a screen stops its connection even during connection startup or reconnect and ignores callbacks after disposal.

Feature API modules own their endpoint calls and response parsing. Shared HTTP code knows how to make a request, not how to book or accept a ride. Map components receive display inputs; they do not own ride lifecycle or dispatch decisions.

Shared telemetry plumbing owns browser session identifiers and best-effort delivery to the API. Rider/driver actions and the shared ride session supply explicit activity and ride context; telemetry transport does not infer business actions from URLs. The API resolves display names and validates ride/offer associations before exporting client reports through its existing logger. Connection lifecycle records belong to `Realtime`. Browser reports and connection loss never mutate ride state, and telemetry delivery failures never become ride-command failures. See [observability](observability.md#rider-and-driver-activity) for fields, lifecycle boundaries, and queries.

The root Next.js instrumentation hook initializes Node server observability once per process when its runtime connection string exists. The server module exports sanitized native Next.js spans and captured request errors through Azure Monitor using the `happy-ride-web` service name. It stays outside the browser dependency graph. Both servers share one Application Insights resource, but retain their own service names. Automatic browser injection and arbitrary console capture are disabled; browser business activity continues through the API. Telemetry initialization or delivery failure must not make a page unavailable.

## Review and verification

For future changes, check behavior and ownership together:

- Keep a feature's use case, contracts, and presentation discoverable in its directory.
- Keep HTTP, EF, and SignalR implementation details out of business decisions.
- Extract duplicated session behavior rather than copying it between screens.
- Preserve endpoint contracts and user-visible behavior during structural refactors.
- Add regression coverage for ordering, concurrency, or failure handling when changing those boundaries.

The checks are the API integration suite (`dotnet test`), frontend state/lifecycle tests (`npm test` in `web/`), the production web build (`npm run build`), and the Playwright rider/driver flow (`npm test` in `e2e/`).

## Remaining target work

The demo still uses development identity, fake map data unless a provider key is configured, and simulated positions. The services share one Postgres database. Durable Azure Service Bus dispatch, real device location, and a production deployment of each service on its own remain work described by the build plan. The feature structure provides places to implement these; the refactor does not claim they are already implemented.
