# GrabBike Clone — Capstone Scope Proposal

> **Superseded:** the decided plan is [build-plan.md](build-plan.md), built on Hien's decisions in [project-context.md](project-context.md), which override this proposal wherever they differ.

Source: `docs/vietnam-bike-rideshare-design.pdf` ("Grab-clone v1 architecture", drawn 14 Sep 2026, revised 19 Oct 2026). For Hien, 20 Oct 2026.

## 1. What the brief proposes

A production v1 design for a Vietnam motorbike ride-hailing platform ("rides first, food as a second job type"), with one paragraph mapping it to a capstone.

- **Roles:** rider, driver, merchant (phase 3), ops, admin.
- **Clients:** rider and driver apps (React Native/Expo; the driver app runs background GPS and batches pings offline), ops console (React, live map, map switch), merchant PWA (phase 3).
- **Modules and features:** Identity (phone OTP via Zalo ZNS/SMS, JWT, roles); Drivers (onboarding, vendor eKYC with CCCD NFC + liveness, `VerificationStatus`, commission or daily pass); Jobs (type + legs, state machine, append-only JobEvents, cancellation rules; sole owner of job state); Dispatch (GEOSEARCH 2–3 km of online/idle/Verified drivers, ETA ranking, 12–15 s offers, `SET NX` lock + `xmin` check, re-dispatch rounds); Pricing (versioned fare tables, base + per-km + per-minute, quotes keep inputs and provider, no surcharges at launch); Geo (`IGeoProvider`: VietMap default, Google switchable, one provider per session, `/api/map-config`, provider-partitioned caches, PostGIS place index, call metering); Ledger (double-entry integer VND: fare, 10% take, state tax, driver share; cash against the driver wallet; balances computed; money never in Redis); Notifications (FCM, in-app, Zalo); Invoices & Tax (MISA/Viettel/VNPT e-invoices, withholding exports); Admin/Ops (live map, KYC queue, disputes, refunds, suspensions, fare editing, transparency report, cost dashboard, audited map switch); Orders (phase 3, two-leg food jobs).
- **Ride flow:** quote → `Requested` → shortlist → offers → one driver wins → "Navigate" opens the Google Maps app (two-wheeler) → GPS every 3–5 s over SignalR (jump rejection, GEOADD, `trip_points`, publish to `job:{id}`) → arrived/started/completed → one transaction writes `Completed` + ledger postings → receipt with the split. Settled distance comes from the platform's own trace.
- **Stack:** C#/.NET 10 ASP.NET Core modular monolith (REST, JWT, OpenTelemetry, Serilog, Hangfire) + separate SignalR realtime service + PostgreSQL on Supabase (PostGIS, JSONB caches, monthly-partitioned traces) + Redis (GEO, offers/locks/OTPs, backplane, Streams fed by a transactional outbox) + S3-compatible storage. React + TypeScript everywhere; TS client generated from OpenAPI. Docker Compose, GitHub Actions, IaC; pilot = 2 API + 2 realtime instances, Redis replica, Supabase Pro (Singapore, PITR).
- **Build order:** Phase 0 foundations (wks 1–4), Phase 1 rides pilot (wks 5–12), Phase 2 cost/quality (months 4–6), Phase 3 food (months 6–9). Capstone = "Phase 0 and most of Phase 1 in 12 weeks" with four cuts: seeded accounts + dev-only login, no CCCD check, receipts not e-invoices, PWAs not native apps. It cites a companion "capstone scope note" that is **not** in the Project store.
- **Notable:** the server-side map switch (Google's terms forbid mixing with non-Google maps; Vietnamese map rules; cost) is a first-class feature; strict money discipline; riders pay cash only — a VietQR PSP for top-ups/payouts is in the diagram but no module owns it; jobs-and-legs prepared for food; heavy Vietnam compliance (PDPL 2025, 1 July 2025 addresses, hóa đơn điện tử); the 19 Oct revision swapped SQL Server for Postgres/Supabase and dropped MongoDB and the self-hosted geo stack.
- **Silent on:** ratings, rider–driver chat, safety/SOS, scheduled rides, non-cash rider payment, minimum fare/rounding, target city, team size, grading criteria.

## 2. Recommended capstone scope

One thin slice: a rider books, one driver accepts, the trip is tracked live, the fare settles into the ledger, ops watches. Anything that needs a vendor contract is cut.

### Core MVP (must ship)

| Item | Why |
|---|---|
| Dev login + seeded rider/driver/ops accounts, JWT roles | Brief's own cut; OTP needs a vendor. |
| Rider PWA: map, place search, quote, request, live driver marker, receipt | The demo's main thread. |
| Driver PWA: online/offline, GPS pings, offer countdown, accept, arrived/started/completed, Navigate link, wallet | Other half of the slice; Navigate costs nothing. |
| Geo: `IGeoProvider` + VietMap adapter + Fake adapter, `/api/map-config`, one `MapView` | Brief's core abstraction; the Fake makes tests and offline demos key-free. |
| Pricing: one versioned fare table, quote stored with inputs and provider | Explains the settled fare — a strong defense point. |
| Jobs: state machine + JobEvents + `xmin`, ride-only single leg, basic cancel (no fees) | Brief's "only module that changes state"; a demo needs a way out of a stuck job. |
| Dispatch: GEOSEARCH shortlist, haversine × 1.3 ÷ fixed speed, sequential offers with TTL, `SET NX`, capped rounds → "no driver" | The hard, interesting part; constants replace learned speeds. |
| Realtime: SignalR hubs in the API (GPS ingest with jump rejection, `job:{id}` groups, offers); single `trip_points` table; settled distance from trace | Live tracking is the wow moment; one process, no backplane. |
| Ledger: double-entry integer-VND postings (fare, take, tax, driver share, cash collected) in the `Completed` transaction; computed balances; HTML receipt | Brief's money rules; debits = credits tests are easy wins. |
| Ops console: live map, manual verification queue, job list + timeline, fare table (read) | Replaces eKYC; shows the whole system to the examiner. |
| Docker Compose, GitHub Actions CI, seed/reset script, OpenAPI→TS client, dev GPS route-replay simulator | Reproducible demo; the simulator de-risks demo day. |
| Tests: unit (state machine, pricing, ledger), integration (two simultaneous accepts → one winner), one Playwright happy path | Defensibility. |

### Stretch (if time allows)

| Item | Why stretch |
|---|---|
| Google adapter + ops toggle + partitioned caches + contract test | Brief's signature feature, but legal/cost motivated; needs Google billing. |
| Provider call metering + cost dashboard | Cheap once two adapters exist. |
| Web push (FCM) for offers | In-app SignalR suffices while the driver screen is on. |
| Own place index (PostGIS + unaccent/pg_trgm) with curated hotspots | PostGIS showcase; provider autocomplete suffices. |
| Cancellation fees, disputes view, driver suspension | Small additions on JobEvents + Ledger. |
| Transactional outbox → Redis Stream | Swap in for in-process events; good report material. |
| Expo driver app with background GPS | Only if someone already knows React Native. |
| Receipt PDF, Hangfire jobs, OpenTelemetry, k6 load test of dispatch | Polish and defense evidence. |

### Out of scope / defer

| Item | Reason |
|---|---|
| Phone OTP (Zalo ZNS/SMS); eKYC vendor + encrypted CCCD storage; e-invoices + withholding exports | Brief's own cuts; vendor contracts, real PII, a registered entity. |
| VietQR PSP top-ups/payouts | Riders pay cash; the brief assigns it no module. |
| Food/Orders, two-leg jobs, merchant PWA | Brief phase 3. |
| Daily pass, cash caps, promotions, surcharges, Goong adapter, auto failover, learned speeds, cache re-verification | Brief phase 2 / "none at launch". |
| Separate realtime service, backplane, 2+2 instances, Supabase Pro/PITR, IaC, Vietnam-hosting review, partitions/BRIN/retention jobs | Production topology and data hygiene; describe in the report. |
| Per-city overrides, multi-city fares, Vietnamese templates | One city, one language. |
| Ratings, chat, SOS, scheduled rides | Not in the brief; don't invent. |

## 3. Suggested stack and architecture

**Adopt:** .NET 10 / ASP.NET Core modular monolith (EF Core + Npgsql, keyed `IGeoProvider` services, `xmin` as concurrency token), PostgreSQL + PostGIS, Redis (GEO, offers, locks), SignalR, React + TypeScript, OpenAPI-generated client, Docker Compose, GitHub Actions. Keep the module names as projects — Identity, Drivers, Jobs, Dispatch, Pricing, Geo, Ledger, Admin; skip Orders and Invoices; Notifications = SignalR only.

**Over-engineered for a capstone → simpler:**

- Separate realtime service + Redis backplane → hubs inside the API, single instance.
- Outbox + Redis Streams → in-process domain events after commit.
- Two providers + switch + dual-mode E2E → one real adapter + Fake; the switch is stretch.
- Supabase Pro (Singapore, PITR) → Postgres in Docker; hosted demo = one small VPS running the same compose file (Supabase Free pauses after a week idle, as the brief notes).
- Partitions / BRIN / retention → one `trip_points` table indexed on `(job_id, recorded_at)`.
- Hangfire → none; filter stale drivers by `last_seen` at query time.
- OpenTelemetry → Serilog logs.
- Learned speed/detour factor and the 1×3 provider matrix → constants (1.3, ~20 km/h), no matrix calls.
- Expo apps → three React PWAs (rider, driver, ops) in one monorepo sharing `api-client` and `map` packages; VietMap GL JS.
- eKYC / e-invoice / Zalo / FCM / VietQR → removed or faked behind the brief's adapter boundaries.

## 4. Phased milestone plan

Indicative weeks assume the brief's 12-week capstone (decision 2). Stretch starts only after M4 is demoable.

1. **M1 — Foundations (wks 1–3).** Repo, compose, CI, module skeletons, dev login + seeds, Jobs state machine + JobEvents, Ledger postings with invariant tests, `IGeoProvider` (Fake + VietMap), `/api/map-config`, `MapView`. *Demo:* log in, see a VietMap map, get a route and quote from the API.
2. **M2 — Request to accept (wks 4–6).** Rider quote/request UI; driver online + GPS over SignalR → Redis GEO; shortlist/offer/lock/accept → `Assigned`. *Demo:* two browsers — rider requests, driver gets a 15 s offer and accepts, rider sees the driver.
3. **M3 — Live trip and settlement (wks 7–8).** GPS fan-out to `job:{id}`, moving marker, arrived/started/completed, `trip_points`, settled distance, ledger postings, receipt, wallet, Navigate, GPS replay simulator. *Demo:* a full ride; receipt shows the split; balances reconcile.
4. **M4 — Ops console and hardening (wks 9–10).** Live map, verification queue, job timeline, cancel, timeout/re-dispatch/no-driver, parallel-accept test, Playwright happy path, hosted deployment, reset script. *Demo:* hosted run with ops watching; CI green including the concurrency test.
5. **M5 — Stretch and defense (wks 11–12).** One or two stretch items (Google adapter + switch first, if chosen), report, rehearsed demo, backup video.

## 5. Risks and mitigations

| Risk | Mitigation |
|---|---|
| **Maps:** VietMap key delayed; quota/cost; sovereignty rendering | Fake adapter from day one; no provider calls in the tracking loop (as the brief does); cache routes; demo-only fallback to MapLibre + public OSM tiles, with the brief's VietMap rationale kept in the report. |
| **Real-time location in a PWA:** no background GPS; poor indoor GPS | Driver screen stays on; replay simulator walks the quote polyline; re-fetch job state over REST on SignalR reconnect (Postgres is truth). |
| **Payments:** riders pay cash — nothing to integrate | Cash is a ledger posting; the wallet shows what the driver owes after cash rides; no PSP. |
| **Driver verification:** no eKYC | Ops verifies manually; seed one verified driver; optional document upload to local disk. |
| **Notifications:** no FCM/Zalo | In-app SignalR toasts + countdown; web push as stretch. |
| **Concurrency in matching:** double assignment, lost offers, stale drivers, endless re-dispatch | `SET NX` with TTL + `xmin`; idempotent accept; one dispatch coordinator per job; cap rounds (e.g., 3); Testcontainers test with simultaneous accepts; filter by `last_seen`. |
| **Scope creep and skill spread** (a startup plan; .NET + React + PostGIS + Redis + SignalR) | Freeze the MVP table at M1; spike each technology in M1; one person owns dispatch + realtime end to end. |
| **Demo day:** network, hosting, data | Hosted + local compose fallback; reset script; recorded video. |
| **Terms and data protection** | If Google is built: one provider per session, no persisted Google coordinates, app stays private. Synthetic users only; no CCCD images or real phone numbers. |

## 6. Decisions Hien needs to make

1. **Team size and roles** — solo → MVP only, web only; 3–4 people → consider the Google switch and Expo stretch.
2. **Timeline** — semester length (the brief assumes 12 weeks), demo/defense dates, mid-term checkpoint.
3. **Required deliverables** — report format, live vs recorded demo, defense, test/CI expectations, and any rubric item (e.g., "mobile app", "authentication") that would pull something back from the cut list.
4. **Target platform** — PWAs for all three clients (recommended; the brief's own cut) vs Expo driver app (the brief's production default) vs both; PWAs cannot track in the background.
5. **Real integrations vs mocks** — real VietMap (account/key, small cost) vs Fake/OSM only; Google adapter (billing); web push; OTP, eKYC, e-invoice and VietQR stay mocked or removed.
6. **Deployment target** — local compose only vs hosted (small VPS or PaaS); Supabase vs self-run Postgres; Singapore vs Vietnam hosting is moot at this scale.
7. **City, fare table, percentages** — the brief names no city and no tax rate; pick a city for seeds, confirm the 10% take and the tax %.
8. **Jobs model** — ride-only single leg (recommended) vs jobs-with-legs now for food readiness.
9. **Reconcile with the brief's companion "capstone scope note"** (not in the store) so there is one plan.
