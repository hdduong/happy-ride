# Application architecture

The current application is a feature-oriented modular monolith: one ASP.NET Core API and one Next.js application. Business features own their behavior and contracts. Infrastructure supplies persistence and realtime delivery. This uses Clean Architecture's dependency direction without adding a separate project for every layer.

This document describes the implemented demo. [The build plan](build-plan.md) describes the target MVP, including integrations that are still pending.

## Backend

```text
src/GrabBike.Api/
  Features/
    Rides/       ride lifecycle, command outcomes, queries, HTTP endpoints
    Dispatch/    offers, driver selection, round expiration
    Drivers/     driver state and simulated movement
    Maps/        places, route quotes, fake map behavior
    Dev/         seeded user identity and development endpoints
    Realtime/    notification contracts and realtime integration
  Infrastructure/
    Persistence/ EF context, repository implementations, write serialization
  SharedKernel/  common geographic values and persistence ports
  Program.cs     dependency injection and application startup
```

Each feature keeps its models, contracts, application services, and repository interfaces together. A controller is the HTTP adapter for a feature: it validates transport input and maps application outcomes to status codes and JSON. Application services own use cases and depend on repository interfaces; they do not choose HTTP statuses or access the EF context directly.

Persistence implementations depend on the feature contracts. `Program.cs` wires implementations to interfaces. Cross-feature dependencies are explicit imports and narrow contracts. Shared code belongs in `SharedKernel` only when multiple features need the same concept; it is not a destination for unrelated models.

Driver movement and dispatch expiration have separate services and background workers with their own scopes and error boundaries. Each worker invokes its feature's application service; the ride service handles ride lifecycle use cases.

State is saved before realtime notifications are sent. Notification delivery is best effort: a delivery failure is logged and does not change a successful, committed HTTP command into an error. Clients poll to reconcile current ride and offer state. This is not durable event delivery; reliable replay would require an outbox or another persistent mechanism.

The SQLite demo serializes writes with a process-local lock. This supports the existing single-process tests, including concurrent offer acceptance. Multiple API instances require database-level concurrency controls and coordination; moving files into features does not provide those guarantees.

The HTTP boundary records request outcomes and handles unexpected exceptions centrally. Feature services add structured ride/offer identifiers to business logs; they do not depend on an Azure telemetry SDK. JSON console output is always available, and Azure Monitor export is enabled only when the API has `APPLICATIONINSIGHTS_CONNECTION_STRING`. The existing manual deployment creates or reuses the monitoring resources. See [observability](observability.md) for activation, queries, collection limits, and retention behavior.

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
    ui/               reusable presentation components
```

Route files compose feature entry points. Rider and driver hooks own screen behavior; components render their state and call actions. Shared ride-session logic owns reconciliation between polling, realtime events, and command results so both screens use the same ordering rules.

A poll represents the state when its request began. If a newer event or command changes the session while that request is running, its response must not replace the newer state. Terminal rides and closed offers also need explicit handling to prevent delayed messages from bringing them back. Disposing a screen stops its connection even during connection startup or reconnect and ignores callbacks after disposal.

Feature API modules own their endpoint calls and response parsing. Shared HTTP code knows how to make a request, not how to book or accept a ride. Map components receive display inputs; they do not own ride lifecycle or dispatch decisions.

## Review and verification

For future changes, check behavior and ownership together:

- Keep a feature's use case, contracts, and presentation discoverable in its directory.
- Keep HTTP, EF, and SignalR implementation details out of business decisions.
- Extract duplicated session behavior rather than copying it between screens.
- Preserve endpoint contracts and user-visible behavior during structural refactors.
- Add regression coverage for ordering, concurrency, or failure handling when changing those boundaries.

The checks are the API integration suite (`dotnet test`), frontend state/lifecycle tests (`npm test` in `web/`), the production web build (`npm run build`), and the Playwright rider/driver flow (`npm test` in `e2e/`).

## Remaining target work

The demo still uses development identity, SQLite, fake map data, and simulated positions. PostgreSQL/PostGIS, durable Azure Service Bus dispatch, real map providers, and device location ingestion remain work described by the build plan. The feature structure provides places to implement these; the refactor does not claim they are already implemented.
