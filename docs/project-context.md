# Project context — Happy Ride capstone

Stable goals, constraints, and decisions. Progress lives in `notes.md`; the build plan in `build-plan.md`; the source brief is `vietnam-bike-rideshare-design.pdf`.

## Goal

A working motorbike ride-hailing MVP (Happy Ride, Vietnam) that demos end to end, built as a capstone project.

## Constraints

- Team: Hien alone (owner and engineer) plus AI coding agents.
- Timeline: 3 days, starting 20 Oct 2026.
- Repository: https://github.com/hdduong/happy-ride (private, default branch `main`); CI on pull requests, deploy on `main`. Project docs are mirrored in the repo under `docs/`.

## Core MVP flow (the only thing that must work)

1. Customer searches a route: pickup place to destination.
2. Platform searches for nearby drivers.
3. One driver wins and accepts (first accept wins; a driver can never be double-assigned).
4. Customer and driver both see live, updated locations during the ride.
5. Ride completes when the driver confirms drop-off and the customer confirms arrival at the destination.

## Stack decisions

- Backend: C# / ASP.NET Core.
- Frontend: Next.js.
- Maps, geocoding, routing: real integrations with VietMap (default) and Google (second), behind one shared interface, switched by a feature flag.
- Messaging: Azure Service Bus (not RabbitMQ).
- Deployment: everything on Azure; default region Southeast Asia (Singapore), the closest to Vietnam.
- CI/CD: GitHub Actions builds, tests, and deploys; Azure is the only deploy target. Hien provides Azure access.
- Secrets live as GitHub Actions secrets: `AZURE_CREDENTIALS` (service principal JSON for `azure/login`, Contributor on the subscription); `VIETMAP_API_KEY` (Services key, backend only) and `VIETMAP_TILEMAP_KEY` (Tilemap key, public, domain-restricted); `GOOGLE_MAPS_API_KEY` (server key: Routes, Geocoding, Places) and `GOOGLE_MAPS_BROWSER_KEY` (Maps JavaScript, HTTP-referrer-restricted).
- Map keys never leave the backend except the two public ones (VietMap Tilemap, Google browser key); all search, geocoding, and routing go through the API.
- Sixth secret: `POSTGRES_ADMIN_PASSWORD` (password auth, not Entra passwordless).
- Coverage: all of Ho Chi Minh City and Hanoi (seed drivers in both; map centers on the user's location with a city picker fallback; search biased to where the user is; dispatch radius 3 km around the pickup). Service Bus stays on Basic with a single queue; no `ride-events` topic in the MVP.
- Driver location: the server-side route-replay simulator is the default and the demo path, enabled in Azure (`Sim__Enabled=true`). Real device GPS (browser geolocation) stays as an optional toggle on the driver page, not on the critical path.

## Explicitly out of scope

- Identity/auth, KYC, driver verification.
- Ledger, wallets, settlements, withholding, tax, platform take, invoices.
- Food ordering, merchants, menus, promotions.
- Admin / ops console.
