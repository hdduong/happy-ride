# GrabBike Clone — Capstone Scope (Decided)

The detailed spec is [build-plan.md](build-plan.md); decisions of record are in [project-context.md](project-context.md); the source brief is `vietnam-bike-rideshare-design.pdf`.

## 1. What the brief proposes

A production v1 design for a Vietnam motorbike ride-hailing platform ("rides first, food as a second job type"), drawn 14 Sep 2026 and revised 19 Oct 2026, with one paragraph mapping it to a capstone.

- **Roles and clients:** rider, driver, merchant (phase 3), ops, admin; rider and driver apps in React Native/Expo (background GPS), a React ops console, a merchant PWA (phase 3).
- **Modules:** Identity (phone OTP, JWT), Drivers (vendor eKYC with CCCD + liveness), Jobs (type + legs, state machine, JobEvents), Dispatch (GEOSEARCH 2–3 km, 12–15 s offers, `SET NX` + `xmin`), Pricing (versioned fare tables), Geo (`IGeoProvider`, VietMap default / Google switchable, one provider per session), Ledger (double-entry integer VND, 10% take, cash against the driver wallet), Notifications (FCM, Zalo), Invoices & Tax (e-invoices, withholding), Admin/Ops, Orders (phase 3).
- **Ride flow:** quote → job → shortlist → offers → one driver wins → "Navigate" opens Google Maps → GPS every 3–5 s over SignalR → arrived/started/completed → ledger postings and receipt; settled distance from the platform's own trace.
- **Stack:** .NET 10 ASP.NET Core modular monolith + separate SignalR service, PostgreSQL on Supabase with PostGIS, Redis (GEO, locks, backplane, Streams fed by an outbox), Hangfire, OpenTelemetry, S3-compatible storage; React + TypeScript everywhere; Docker Compose, GitHub Actions.
- **Build order:** Phase 0 foundations, Phase 1 rides pilot, Phase 2 cost/quality, Phase 3 food. Capstone paragraph: "Phase 0 and most of Phase 1 in 12 weeks" with four cuts (seeded accounts + dev login, no CCCD check, receipts not e-invoices, PWAs not native apps); it cites a companion "capstone scope note" that was never provided.
- **Notable:** the server-side map switch (Google's terms forbid mixing with non-Google maps) is a first-class feature; strict money discipline; riders pay cash only; heavy Vietnam compliance (PDPL 2025, 1 July 2025 addresses, e-invoices). **Silent on:** ratings, chat, SOS, scheduled rides, target city, team size, grading criteria.

## 2. Decided scope

### In

- **The five-step core flow, and only that:** (1) customer searches a route, pickup to destination; (2) platform searches nearby drivers; (3) one driver wins and accepts — first accept wins, a driver is never double-assigned; (4) customer and driver both see live locations during the ride; (5) the driver confirms drop-off, then the customer confirms arrival → `Completed`. Statuses: `Searching → Accepted → DriverArrived → InProgress → DroppedOff → Completed`, plus `NoDriver` and `Cancelled` (rider cancel before the trip starts).
- **Dev identity: no login, no accounts, no JWT.** A pick-a-user page lists seeded personas (2 riders and 4 drivers per city); every API call carries `X-User-Id`, the SignalR connection carries `?userId=`. That is the entire security model.
- **Driver GPS: simulator-driven by default and in the demo.** The server-side route-replay simulator (`Sim__Enabled=true`, including in Azure) moves drivers along real routes, so the demo runs from one laptop with no phones. Browser geolocation is an optional toggle on the driver page, off the critical path.
- **Maps: VietMap default, Google second, one interface, one feature flag.** `IMapProvider` with VietMap, Google (Routes API, Places API (New), Geocoding) and a Fake adapter for dev; `/api/map-config` switches backend and frontend together. Only the two public keys (VietMap Tilemap, Google browser) reach the browser; all search, geocoding and routing go through the API.
- **Coverage: all of Ho Chi Minh City and Hanoi.** Seed driver clusters in both; the map centres on the user's geolocation with a city-picker fallback; autocomplete is biased to the map centre; dispatch radius is 3 km around the pickup.
- **Dispatch and realtime:** up to 3 parallel offers per round, 15 s, 3 rounds, then `NoDriver`; accept is a compare-and-set in one transaction plus a partial unique index; Azure Service Bus Basic queue `dispatch` carries requests and scheduled offer timeouts; one SignalR hub inside the API.
- **Azure topology (one line):** App Service B1 (API + SignalR) + Static Web Apps Free (Next.js static export) + Azure Database for PostgreSQL Flexible Server B1ms with PostGIS + Service Bus Basic + Application Insights, Southeast Asia, ≈ $31/month, Bicep + GitHub Actions.

### Out

| Item | Reason |
|---|---|
| Identity/auth, KYC, driver verification | Hien's decision; vendor-dependent and not needed for the flow — dev identity instead. |
| Ledger, wallets, payments, settlements, invoices, tax, platform take | Money is out of scope; nothing is charged in the demo. |
| Pricing engine (fare tables, surcharges, promotions) | A fare *estimate* is optional and display-only (Day 3); nothing depends on it. |
| Food, merchants, menus, promotions | Brief's phase 3. |
| Admin / ops console | Out; the `/sim` page is a dev tool, not an ops console. |
| Redis | PostGIS holds driver positions and geosearch; one API instance needs no backplane. |
| Supabase | Azure is the only deploy target; Azure Database for PostgreSQL instead. |
| Transactional outbox, Redis Streams, `ride-events` topic | One Service Bus queue is enough; there is no consumer for ride events. |
| Hangfire | No background jobs; stale drivers are filtered by `last_seen_at` at query time. |
| Native apps (React Native / Expo) | Web only (Next.js). |
| Background GPS | The simulator is the demo path; browsers do not do background GPS anyway. |
| Ratings, chat, SOS, scheduled rides, cancellation fees, push notifications, "Navigate" hand-off, place index, provider caches, autoscaling, Key Vault, custom domains, VNet, PDPL work, retention jobs | Not in the brief or not needed for a 3-day MVP. |

## 3. Stack as decided

Detail, layout and endpoints are in [build-plan.md](build-plan.md).

- **Backend:** C# / ASP.NET Core (.NET 10), one project in vertical slices; EF Core + Npgsql + NetTopologySuite; SignalR hub in-process.
- **Frontend:** Next.js (App Router, static export); `maplibre-gl` for VietMap and Fake, `@vis.gl/react-google-maps` for Google.
- **Data:** PostgreSQL 16 + PostGIS — `users`, `drivers`, `quotes`, `rides`, `ride_offers`, `ride_events`, `trip_points`.
- **Messaging:** Azure Service Bus Basic, single queue `dispatch` (`DispatchRequested`, scheduled `OfferRoundTimeout`); in-memory bus for local dev and tests.
- **Maps:** VietMap (Autocomplete, Place, Reverse, Route `vehicle=motorcycle`) and Google (Places API (New), Geocoding, Routes `TWO_WHEELER`) behind `IMapProvider`; flag `Maps__Provider`, default VietMap.
- **Hosting:** Azure, Southeast Asia (Static Web App resource in `eastasia`); App Service B1, Static Web Apps Free, PostgreSQL Flexible Server B1ms, Service Bus Basic, Log Analytics + Application Insights.
- **CI/CD and IaC:** GitHub Actions — CI on pull requests, deploy on `main`; Bicep via `az deployment group create`; repo `hdduong/happy-ride` (private), docs mirrored under `docs/`.
- **Secrets (GitHub Actions):** `AZURE_CREDENTIALS`, `VIETMAP_API_KEY`, `VIETMAP_TILEMAP_KEY`, `GOOGLE_MAPS_API_KEY`, `GOOGLE_MAPS_BROWSER_KEY`, `POSTGRES_ADMIN_PASSWORD`; the four map keys live only in App Service settings, the public two are served by `/api/map-config`.

## 4. Risks

| Risk | Mitigation |
|---|---|
| VietMap key or docs slower than hoped; response shapes differ | Fake adapter keeps the flow working; record real responses first thing Day 2. |
| Google `TWO_WHEELER` is an Enterprise SKU; Places (New) must be enabled; billing required | One route call per quote; fall back to `DRIVE` on error; Google stays second. |
| Google terms: no mixing with non-Google maps; coordinates cached ≤ 30 days | The flag switches everything together; Google-mode rides are demo data wiped by `/api/dev/reset`. |
| App Service WebSockets / SignalR | WebSockets on in Bicep; one instance; check the negotiated transport. |
| Static Web App + Next.js | Static export, no dynamic route segments, `navigationFallback`, API URL at build time, explicit CORS. |
| Service Bus Basic: no topics, no local emulator | Queue only; in-memory bus locally; the Basic namespace costs cents. |
| PostGIS enablement on Flexible Server | `azure.extensions = POSTGIS` in Bicep; in-memory driver registry as fallback behind the same interface. |
| Simulator state is in memory | `start-all` after any restart; `reset` restores seed state. |
| Optional device-GPS mode | Needs HTTPS, screen on, no background GPS; not part of the demo. |
| Key leakage | Server keys only in App Service settings; browser keys referrer-restricted and served only by `/api/map-config`; no `NEXT_PUBLIC_*` keys. |
| One engineer, three days | Each day's checkpoint is the stop line; nothing from the Out list, however small. |

## 5. Decisions made

1. **Team:** Hien alone, plus AI coding agents.
2. **Timeline:** 3 days from 20 Oct 2026 — Day 1 the whole flow locally on fakes; Day 2 real providers, then Azure; Day 3 polish and a scripted demo.
3. **Deliverables:** a working MVP that demos end to end from one laptop, plus a recorded fallback and a README with architecture diagram and demo script; report or defense format is not specified in `project-context.md`.
4. **Target platform:** web (Next.js) for rider, driver and `/sim` pages; no native apps, no background GPS.
5. **Real vs mocked:** VietMap and Google are real; a Fake adapter serves dev and tests; driver GPS is simulated by default (device GPS optional); identity, KYC, payments, invoices and notifications are removed, not mocked.
6. **Deployment:** Azure only, Southeast Asia; App Service + Static Web Apps + PostgreSQL Flexible Server + Service Bus Basic + Application Insights; Bicep and GitHub Actions; password auth for Postgres (`POSTGRES_ADMIN_PASSWORD`), not Entra passwordless.
7. **Cities and money:** Ho Chi Minh City and Hanoi; no fare tables, take rate or tax percentages — money is out.
8. **Jobs model:** ride-only, single-leg `rides` table; no legs, no food readiness.
9. **Messaging and events:** Service Bus stays Basic with the single `dispatch` queue; no `ride-events` topic.
10. **The brief's companion "capstone scope note":** never provided; `project-context.md` is the decision record instead.
