# GrabBike Clone — 3-Day Build Plan

Spec for coding agents. Decisions come from `project-context.md` (overrides `capstone-scope-proposal.md`); the brief is `vietnam-bike-rideshare-design.pdf`. One engineer (Hien) plus agents, 3 days from 20 Oct 2026. Only the five-step core flow must work; everything in section 6's non-goals is off the table.

## 1. Architecture

### 1.1 Solution layout (one repo)

```
.github/workflows/ci.yml, deploy.yml
infra/main.bicep, infra/modules/*.bicep, infra/main.parameters.json
src/GrabBike.Api/              # single ASP.NET Core (.NET 10) project, vertical slices
  Features/Maps/        IMapProvider, VietMapProvider, GoogleMapsProvider, FakeMapProvider, MapProviderResolver, endpoints
  Features/Rides/       Ride, RideStatus, RideService (state machine), endpoints
  Features/Dispatch/    IDispatchBus (ServiceBus + InMemory), DispatchWorker, OfferService, endpoints
  Features/Drivers/     LocationService, endpoints
  Features/Realtime/    RideHub, DevUserIdProvider
  Features/Dev/         DevIdentityMiddleware, DevEndpoints, DriverSimulator, Seed
  Infrastructure/       AppDbContext (EF Core + Npgsql + NetTopologySuite), Migrations
tests/GrabBike.Api.Tests/      # xUnit + Testcontainers (Postgres)
web/                           # Next.js App Router, output: 'export'
  app/page.tsx (user picker)  app/rider/  app/driver/  app/sim/
  components/map/MapView.tsx, MapLibreCanvas.tsx (VietMap + Fake), GoogleCanvas.tsx
  lib/api.ts, lib/signalr.ts, lib/user.ts
docker-compose.yml             # postgis/postgis:16 for local dev
```

Coverage: all of Ho Chi Minh City and Hanoi. Seed driver clusters in both cities (e.g., around Bến Thành, 10.772 N 106.698 E, and Hoàn Kiếm, 21.029 N 105.852 E). The map centres on the user's geolocation, with a city picker (HCMC / Hanoi) as the fallback when permission is denied; autocomplete is biased to the map centre or user position; dispatch is always 3 km around the pickup.

### 1.2 Realtime location flow

- One SignalR hub, `RideHub` at `/hubs/ride`, hosted inside the API. Single App Service instance, so no backplane; do not scale out (that would need Azure SignalR Service).
- Identity on the socket: `?userId=<uuid>` query string → `DevUserIdProvider`, so the server addresses people with `Clients.User(id)`; no groups needed.
- Driver location: **the simulator is the default and the demo path** — the `DriverSimulator` background service (`Sim__Enabled=true`, including in Azure) calls `LocationService.IngestAsync` directly, so the whole demo runs from one laptop with no phones. Optional **Device** toggle on the driver page, off the critical path: `navigator.geolocation.watchPosition` → hub method `SendLocation(lat, lng, heading, speedKmh)` at most every 3 s (needs HTTPS, i.e. the Azure URL).
- `IngestAsync`: reject jumps > 50 m/s; `UPDATE drivers SET last_position, last_heading, last_speed_kmh, last_seen_at`; if the driver has an active ride (`Accepted`..`DroppedOff`), insert a `trip_points` row and send `DriverLocation` to the rider and the driver.
- Live driver positions live in Postgres/PostGIS, not Redis: a few drivers × one write per 3 s is nothing, it survives restarts, and dispatch's nearest-driver query runs on the same rows.

### 1.3 Where Azure Service Bus earns its keep

One place: **dispatch**. Queue `dispatch` (Basic tier supports queues and scheduled messages; topics need Standard).

| Message (`Subject`) | Body | Produced | Consumed by `DispatchWorker` |
|---|---|---|---|
| `DispatchRequested` | `{ rideId, round }` | `POST /api/rides` (round 1, after commit); the worker for rounds 2–3; `decline` when every offer in a round is declined | Load ride; if not `Searching` → done. Find up to 3 nearest free drivers within 3 km not yet offered this ride; create `ride_offers` (Pending, 15 s expiry); send `OfferReceived` to each; schedule `OfferRoundTimeout`. No candidates → `NoDriver`. |
| `OfferRoundTimeout` | `{ rideId, round }` | Scheduled by the worker with `ScheduleMessageAsync(now + 15 s)` | If ride still `Searching` and `round` is current: expire pending offers, send `OfferClosed(Expired)`; round < 3 → publish `DispatchRequested(round+1)`, else → `NoDriver`. |

Why here: offer timers are durable (an API restart mid-dispatch resumes from the queue), retries and dead-lettering come free, and ride creation returns immediately. `MessageId = "{rideId}:{round}:{Subject}"`; handlers are idempotent by re-reading state (Basic has no dedup). `ServiceBusProcessor`, `MaxConcurrentCalls = 1`, manual complete, exceptions → abandon → DLQ after 10 deliveries. `IDispatchBus` also has an `InMemoryDispatchBus` (Channel + `Task.Delay`) for local dev and tests: `Dispatch:Bus = InMemory|ServiceBus`.

Not in the MVP (decided): a `ride-events` topic carrying `RideStatusChanged { rideId, riderId, driverId?, from, to, at }` would need Standard tier and has no consumer yet; Service Bus stays Basic with the single `dispatch` queue.

### 1.4 Data model (PostgreSQL 16 + PostGIS, EF Core migrations applied at API startup)

| Table | Key columns |
|---|---|
| `users` | `id uuid PK`, `name`, `role` (`Rider`/`Driver`) — seeded personas, the identity stand-in |
| `drivers` | `user_id PK/FK`, `plate`, `status` (`Offline`/`Online`/`OnTrip`), `last_position geography(Point,4326)` (GiST index), `last_heading`, `last_speed_kmh`, `last_seen_at`, `current_ride_id uuid null` |
| `quotes` | `id uuid PK`, `provider` (`VietMap`/`Google`/`Fake`), `pickup_lat/lng/address`, `dropoff_lat/lng/address`, `distance_m`, `duration_s`, `route jsonb` (GeoJSON LineString), `fare_vnd int null` (display-only estimate, Day 3), `created_at` |
| `rides` | `id uuid PK`, `rider_id FK`, `driver_id FK null`, `quote_id FK`, `provider`, `status`, `round int`, `requested_at`, `accepted_at`, `arrived_at`, `started_at`, `dropped_off_at`, `completed_at`, `cancelled_at`, `xmin` as concurrency token. **Partial unique index** `ux_rides_active_driver ON rides(driver_id) WHERE status IN ('Accepted','DriverArrived','InProgress','DroppedOff')` — the database itself forbids double assignment |
| `ride_offers` | `id uuid PK`, `ride_id FK`, `driver_id FK`, `round`, `status` (`Pending`/`Accepted`/`Declined`/`Expired`/`Closed`), `offered_at`, `expires_at`; unique `(ride_id, driver_id)` |
| `ride_events` | `id bigserial`, `ride_id`, `type`, `actor_user_id null`, `data jsonb`, `at` — append-only timeline, written in the same transaction as every transition |
| `trip_points` | `id bigserial`, `ride_id`, `driver_id`, `lat`, `lng`, `heading`, `speed_kmh`, `at` — index `(ride_id, at)`; drawn as the actual path on the completed screen |

Nearest-driver query: `status = 'Online' AND current_ride_id IS NULL AND last_seen_at > now() - interval '60 seconds' AND ST_DWithin(last_position, :pickup, 3000) ORDER BY last_position <-> :pickup LIMIT 3` (EF: `IsWithinDistance` + `Distance`). Fallback if PostGIS gives trouble on Azure: an in-memory `DriverRegistry` with a haversine scan, same interface.

### 1.5 Azure topology (resource group `rg-grabbike-dev`, region Southeast Asia)

| Resource | SKU | ≈ USD/mo | Why |
|---|---|---|---|
| App Service Plan (Linux) + Web App `app-grabbike-api` | B1, `DOTNETCORE\|10.0`, WebSockets on, Always On, HTTPS only | 13 | Zip deploy, WebSockets toggle, app settings. Container Apps would add ACR, image builds and ingress tuning for no gain at one instance. If the .NET 10 image lags, publish self-contained. |
| Static Web App `swa-grabbike-web` | Free (resource location `eastasia`; SWA has no Southeast Asia option, content is served from the global edge) | 0 | Next.js static export, free TLS, built-in deploy action. App Service would mean a second plan for SSR we do not need. |
| Azure Database for PostgreSQL Flexible Server | Burstable B1ms, 32 GB, PG 16, `azure.extensions = POSTGIS`, public access + "Allow Azure services" firewall rule, SSL required | 17 | One store; PostGIS geosearch replaces Redis GEO; matches the brief and the EF Core path. Azure SQL has a free tier but a less familiar spatial path. |
| Service Bus namespace `sb-grabbike` | Basic, queue `dispatch` | < 1 | Section 1.3. Standard (≈ 10) only if the optional topic is added. |
| Log Analytics + Application Insights | Pay-as-you-go (5 GB/mo free) | ≈ 0 | Logs and traces while debugging the Azure deploy. |

Total ≈ $31/month. No Redis, no Key Vault, no VNet, no custom domains.

### 1.6 CI/CD, infrastructure as code, secrets

- **IaC: Bicep via `az deployment group create`**, not azd: it fits the `AZURE_CREDENTIALS` + `azure/login` flow exactly, every step is a standard, independently debuggable action, and there is no azd environment state to manage in CI. `infra/main.bicep` is idempotent: Plan, Web App (+ app settings), SWA, Postgres (+ database, firewall rule, `azure.extensions`), Service Bus (namespace, queue, auth rule), Log Analytics, App Insights.
- **`ci.yml`** on `pull_request`: `dotnet build` + `dotnet test` (Testcontainers; runners have Docker), `npm ci && npm run lint && npm run build` in `web/`. No secrets; provider contract tests use recorded JSON.
- **`deploy.yml`** on push to `main` (+ `workflow_dispatch`, `concurrency: deploy`): `azure/login@v2` (`creds: AZURE_CREDENTIALS`) → Bicep deploy with `@secure()` parameters → `dotnet publish` → `azure/webapps-deploy@v3` → `next build` with `NEXT_PUBLIC_API_BASE_URL=https://app-grabbike-api.azurewebsites.net` → `az staticwebapp secrets list --query properties.apiKey` → `Azure/static-web-apps-deploy@v1` (`skip_app_build: true`, `app_location: web/out`).
- **GitHub Actions secrets (exact names):** `AZURE_CREDENTIALS`, `VIETMAP_API_KEY`, `VIETMAP_TILEMAP_KEY`, `GOOGLE_MAPS_API_KEY`, `GOOGLE_MAPS_BROWSER_KEY`, plus the sixth, `POSTGRES_ADMIN_PASSWORD` (password auth, not Entra passwordless).
- **Where keys go.** All four map keys are Bicep `@secure()` parameters written as **Web App app settings**: `Maps__VietMap__ApiKey`, `Maps__VietMap__TileKey`, `Maps__Google__ApiKey`, `Maps__Google__BrowserKey`; alongside `Maps__Provider=VietMap`, `ConnectionStrings__Default` (built in Bicep from the server FQDN + password), `ServiceBus__ConnectionString` (Bicep `listKeys()` on the namespace auth rule — never in GitHub), `APPLICATIONINSIGHTS_CONNECTION_STRING`, `Cors__AllowedOrigins`, `Dev__Enabled=true`, `Sim__Enabled=true` (Bicep parameter `simEnabled`, default `true`; the simulator is the demo path). The Static Web App holds **no secrets**; its only build-time variable is the API URL.
- **Frontend key rule.** Only the two public keys ever reach the browser, and only via `GET /api/map-config`, which returns the active provider's config: VietMap → a style URL containing `VIETMAP_TILEMAP_KEY`; Google → `GOOGLE_MAPS_BROWSER_KEY`. `VIETMAP_API_KEY` and `GOOGLE_MAPS_API_KEY` are server-only; all autocomplete, place details, reverse geocoding and routing go through the API. Restrictions: `GOOGLE_MAPS_BROWSER_KEY` → HTTP referrers `https://<swa-host>/*` and `http://localhost:3000/*`, API restriction Maps JavaScript API only; `GOOGLE_MAPS_API_KEY` → Routes API, Places API (New), Geocoding API only; `VIETMAP_TILEMAP_KEY` → domain restriction if VietMap's console offers it. Never put a key in `NEXT_PUBLIC_*`.
- **Bootstrap (Hien, once, before Day 2):** create `rg-grabbike-dev` (Southeast Asia) and a service principal with Contributor on the subscription → `AZURE_CREDENTIALS`; VietMap account with a Services key and a tile-map key; Google Cloud project with billing and Maps JavaScript API, Places API (New), Routes API, Geocoding API enabled, two restricted keys; add the secrets to the repo.

## 2. Map provider abstraction

```csharp
public enum MapProvider { Fake, VietMap, Google }
public interface IMapProvider {
  MapProvider Id { get; }
  Task<IReadOnlyList<PlaceSuggestion>> AutocompleteAsync(string text, LatLng? near, string? sessionToken, CancellationToken ct);
  Task<PlaceDetails> GetPlaceAsync(string placeId, string? sessionToken, CancellationToken ct);  // → lat/lng + address
  Task<string> ReverseGeocodeAsync(LatLng point, CancellationToken ct);
  Task<RouteResult> GetMotorbikeRouteAsync(LatLng from, LatLng to, CancellationToken ct);         // distanceM, durationS, GeoJSON coords
  MapClientConfig ClientConfig { get; }  // what the browser gets from /api/map-config
}
```

- Registered as keyed singletons with typed `HttpClient`s (5 s timeout, one retry). `MapProviderResolver.Current` returns the active provider; quotes and rides are stamped with it, and any later call for that ride (the simulator's driver→pickup route) uses the stamped one, so a ride never mixes providers.
- **VietMap adapter** (`VIETMAP_API_KEY`): Autocomplete v3 (`text`, focus point), Place v3 (`refid` → coordinates), Reverse v3, Route with `vehicle=motorcycle&points_encoded=false`. Confirm exact paths and response shapes against VietMap's docs on Day 2 morning and save real responses as test fixtures.
- **Google adapter** (`GOOGLE_MAPS_API_KEY`): Places API (New) `places:autocomplete` (`includedRegionCodes: ["vn"]`, `languageCode: "vi"`, `locationBias` circle, session token) and `GET places/{id}` with field mask `id,displayName,formattedAddress,location`; Geocoding API for reverse; **Routes API** `directions/v2:computeRoutes` with `travelMode: TWO_WHEELER`, `regionCode: "VN"`, `polylineEncoding: GEO_JSON_LINESTRING`, field mask `routes.distanceMeters,routes.duration,routes.polyline`. Not legacy Directions/Places. One route call per quote; if `TWO_WHEELER` is rejected, fall back to `DRIVE` and log it.
- **Fake adapter** (dev only): autocomplete over ~40 seeded places, half in Ho Chi Minh City and half in Hanoi, ranked by distance from the `near` bias point; route = straight line with intermediate points, distance = haversine × 1.3, duration at 20 km/h; client config = MapLibre demo style.
- **Feature flag.** Default from config `Maps:Provider` (App Service setting `Maps__Provider`; changing it restarts the app). Demo-time override: `POST /api/dev/map-provider { provider }` sets an in-memory override and broadcasts `MapConfigChanged`. The frontend's `useMapConfig()` reads `GET /api/map-config` on load and again on `MapConfigChanged`; `MapView` then renders `GoogleCanvas` (`@vis.gl/react-google-maps`, `region="VN"`, `language="vi"`) or `MapLibreCanvas` (`maplibre-gl` with the VietMap style URL, or the demo style in Fake mode). Both take the same props: center, markers (pickup, drop-off, driver), route coordinates, click handler. Backend and frontend read the same endpoint, so map, search and routing switch together — which is also what Google's no-mixing terms require.
- `GET /api/map-config` → `{ "provider": "VietMap", "styleUrl": "…?apikey=<tile key>" }` or `{ "provider": "Google", "browserKey": "…", "region": "VN", "language": "vi" }`.

## 3. Core ride flow

Statuses: `Searching → Accepted → DriverArrived → InProgress → DroppedOff → Completed`, plus `NoDriver` and `Cancelled`. Every transition runs in one transaction with a status guard, appends a `ride_events` row, and sends SignalR after commit. Invalid transition → `409 { error: "InvalidTransition", from, to }`. All REST under `/api`, JSON, header `X-User-Id`.

**Search and quote (rider):** the page centres on `navigator.geolocation` (city picker HCMC / Hanoi if denied). `GET /api/places/autocomplete?q=&lat=&lng=&sessionToken=` (debounced 300 ms; `lat`/`lng` = map centre or user position, passed to the provider as the location bias) → `GET /api/places/{id}?sessionToken=` → coordinates; map tap → `GET /api/geocode/reverse?lat=&lng=`. `POST /api/quotes { pickup, dropoff }` → `201 { quoteId, provider, distanceM, durationS, route, fareVnd? }`; the rider page draws the route and shows distance/ETA.

| From | Trigger | Endpoint / message | To | SignalR |
|---|---|---|---|---|
| — | Rider requests | `POST /api/rides { quoteId }` → `201 { rideId }` | `Searching` | `RideStatusChanged` → rider; `DispatchRequested(1)` queued |
| `Searching` | Worker runs a round | `DispatchRequested` | `Searching` | `OfferReceived { offerId, rideId, pickup, dropoff, distanceM, durationS, expiresAt, round }` → ≤ 3 drivers |
| `Searching` | Driver accepts | `POST /api/offers/{id}/accept` → `200` ride view or `409 { reason: Taken\|Busy\|Closed }` | `Accepted` | `RideStatusChanged` (with driver name, plate, position) → rider + driver; `OfferClosed(Taken)` → the others |
| `Searching` | Driver declines | `POST /api/offers/{id}/decline` | `Searching` | all declined this round → `DispatchRequested(round+1)` immediately |
| `Searching` | Round times out | `OfferRoundTimeout` | `Searching` (round+1) or `NoDriver` after round 3 / no candidates | `OfferClosed(Expired)` → drivers; `RideStatusChanged(NoDriver)` → rider |
| `Accepted` | Driver arrives at pickup | `POST /api/rides/{id}/arrived` | `DriverArrived` | `RideStatusChanged` |
| `DriverArrived` | Driver starts trip | `POST /api/rides/{id}/start` | `InProgress` | `RideStatusChanged` |
| `InProgress` | Driver confirms drop-off | `POST /api/rides/{id}/dropoff` | `DroppedOff` | `RideStatusChanged` (rider sees "Confirm arrival") |
| `DroppedOff` | Rider confirms arrival | `POST /api/rides/{id}/confirm-arrival` | `Completed` | `RideStatusChanged`; driver → `Online`, `current_ride_id = null` |
| `Searching`..`DriverArrived` | Rider cancels | `POST /api/rides/{id}/cancel` | `Cancelled` | `RideStatusChanged`; `OfferClosed(Cancelled)`; driver freed |
| `Accepted`..`DroppedOff` | Driver GPS | hub `SendLocation` / simulator | unchanged | `DriverLocation { rideId, driverId, lat, lng, heading, speedKmh, at }` → rider + driver |

**First-accept-wins rule.** Accept is one transaction of two compare-and-set statements: `UPDATE rides SET status='Accepted', driver_id=:d, accepted_at=now() WHERE id=:r AND status='Searching' AND driver_id IS NULL AND EXISTS (offer for :r,:d)` and `UPDATE drivers SET current_ride_id=:r, status='OnTrip' WHERE user_id=:d AND current_ride_id IS NULL`. Either affecting 0 rows → rollback and `409` (`Taken` / `Busy`). A late accept after the round timed out still wins if the ride is still `Searching`; the partial unique index in 1.4 is the last line of defence against double assignment. Offers in a round go to up to 3 drivers in parallel so the race is real and demoable.

**Other endpoints:** `GET /api/rides/{id}`, `GET /api/rides/current` (rider's or driver's active ride, for reloads), `GET /api/offers/pending` (driver, for reloads), `POST /api/drivers/me/status { status, lat, lng }`, `GET /api/drivers/me`. Hub client→server: `SendLocation`. Hub server→client: `OfferReceived`, `OfferClosed`, `RideStatusChanged`, `DriverLocation`, `MapConfigChanged`.

**GPS route-replay simulator — the default and the demo path** (`DriverSimulator`, in the API, `Sim__Enabled=true` everywhere including Azure; the device-GPS toggle on `/driver` is optional): `POST /api/dev/sim/start-all` parks every seeded driver at its seed position in both cities and sets it `Online`; `POST /api/dev/sim/drivers/{id}/start { lat, lng, playbackFactor }`, `/stop`, `GET /api/dev/sim`. It ticks once per second into `LocationService.IngestAsync`. By ride state: idle → stationary; `Accepted` → fetch the driver→pickup route from the ride's provider and follow it at route speed × `playbackFactor` (default 5); `DriverArrived` → hold; `InProgress` → follow the quote route; end of route → hold at the drop-off. Humans press Arrived / Start / Drop-off on `/driver`; `autoAdvance: true` presses them for a hands-free demo. `POST /api/dev/reset` wipes rides, offers, events and trip points and re-parks drivers. `/sim` is a thin page over these endpoints plus the provider switch and an event feed.

## 4. Dev identity stand-in

- `GET /api/dev/users` lists seeded personas (2 riders and 4 drivers with plates per city, HCMC and Hanoi). The `/` page is a pick-a-user screen; the choice is kept in `localStorage` and routes to `/rider` or `/driver`. `?as=<userId>` on any page selects a user directly, so one laptop can open several tabs as different people.
- Every REST call sends `X-User-Id: <uuid>`; the hub connects with `?userId=<uuid>`. `DevIdentityMiddleware` resolves the user → `ICurrentUser { Id, Role }` (`401` if missing/unknown); driver endpoints check `Role == Driver`, rider endpoints `Role == Rider`. That is the whole security model and it is documented as such.

## 5. Day plan

**Day 1 — the whole flow, locally, on fakes.** Scaffold API and web; `docker-compose` Postgres; EF model, migrations, seed; dev identity; `IMapProvider` + `FakeMapProvider`; `/api/map-config` + `MapLibreCanvas` on demo tiles; quote → ride → `DispatchWorker` on `InMemoryDispatchBus` (3 parallel offers, 15 s, 3 rounds) → accept with the CAS rule → `RideHub` events → `DriverSimulator` moving the driver → arrived / start / drop-off / confirm-arrival. Tests: state-machine table, two concurrent accepts → one `200` one `409` (Testcontainers), dispatch rounds and timeout. **Checkpoint:** a complete ride in two localhost tabs with simulated GPS — no Azure, no keys, no phones.

**Day 2 — real providers, then Azure.** Morning: VietMap adapter with recorded fixtures, VietMap style URL in `MapLibreCanvas`; Google adapter (Places (New), Geocoding, Routes `TWO_WHEELER`) and `GoogleCanvas`; feature flag + `MapConfigChanged`; provider contract tests. Afternoon: `ServiceBusDispatchBus`; `infra/main.bicep`; `deploy.yml`; CORS for the SWA origin; App Insights; first deploy from `main`. **Checkpoint:** the Day-1 flow runs on the Azure URLs, simulator-driven, with real VietMap search, routing and tiles; flipping the flag switches map, search and routing to Google; a PR runs CI green; merging deploys.

**Day 3 — polish and demo.** Reload-safe pages (`/rides/current`, `/offers/pending`), `NoDriver` and cancel paths, offer countdown and ETA display, display-only fare estimate if wanted, `/sim` panel (park, speed factor, reset, provider switch, event feed), error toasts, README with architecture diagram and the demo script, backup screen recording, cost check. **Checkpoint:** a scripted 5-minute demo on Azure from one laptop, no phones — two riders, several simulated drivers, one race on accept, both providers, HCMC and Hanoi — with a recording as fallback.

## 6. Risks and non-goals

| Risk | Mitigation |
|---|---|
| VietMap key or docs slower than hoped; response shapes differ | Fake adapter keeps the flow working; the adapter is four calls; record real responses first thing Day 2. |
| Google `TWO_WHEELER` is an Enterprise SKU (≈ 1,000 free/month per the brief); Places (New) must be enabled explicitly; billing account required | One route call per quote, no matrix calls; fall back to `DRIVE` on error; Google stays the second provider. |
| Google terms: no mixing with non-Google maps; coordinates cached ≤ 30 days | The flag switches everything together; Google-mode rides are demo data wiped by `/api/dev/reset`; a purge job is a non-goal. |
| App Service WebSockets / SignalR | `webSocketsEnabled: true` in Bicep; one instance; check the negotiated transport in the browser console. |
| Static Web App + Next.js | `output: 'export'`, no dynamic route segments (query strings instead), `navigationFallback` in `staticwebapp.config.json`, API URL at build time, explicit CORS with credentials. |
| Service Bus Basic has no topics; no local emulator in the plan | Queue only; `InMemoryDispatchBus` locally; the Basic namespace costs cents, so local dev can also point at Azure. |
| PostGIS on Flexible Server (`azure.extensions` must list `POSTGIS` before `CREATE EXTENSION` runs at startup) | Bicep configuration resource; in-memory `DriverRegistry` fallback behind the same interface. |
| Optional device-GPS mode (off the critical path) | Only if someone tries it: needs HTTPS, screen on, no background GPS; the marker moves only if the phone moves. Not part of the demo. |
| Simulator state is in memory | `start-all` is one click after any restart; `reset` restores seed state. |
| Key leakage | Server keys only in App Service settings; browser keys only via `/api/map-config`, referrer-restricted; `@secure()` Bicep params; no `NEXT_PUBLIC_*` keys. |
| One engineer, three days | Each day's checkpoint is the stop line; nothing from the non-goals list, however "quick". |

**Non-goals:** real identity/auth (dev picker only), KYC and driver verification, payments, fare settlement, ledger, wallets, platform take, tax, invoices (a fare *estimate* is display-only and optional), food/merchants/menus/promotions, admin/ops console (the `/sim` page is a dev tool), ratings, chat, SOS, scheduled rides, cancellation fees, push notifications, native mobile apps, background GPS on phones, "Navigate" hand-off, multi-city or per-city config, surge, persistent provider caches, place index, autoscaling or SignalR backplane, Key Vault, custom domains, VNet, PDPL/compliance work, data retention jobs.

**Decided since the first draft (see `project-context.md`):** `POSTGRES_ADMIN_PASSWORD` is the sixth secret (password auth); coverage is all of Ho Chi Minh City and Hanoi; Service Bus stays Basic with the single `dispatch` queue, no `ride-events` topic; the simulator is the default and the demo path (enabled in Azure), with device GPS only as an optional toggle. No open decisions remain.
