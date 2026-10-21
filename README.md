# happy-ride

[![Build](https://github.com/hdduong/happy-ride/actions/workflows/build.yml/badge.svg)](https://github.com/hdduong/happy-ride/actions/workflows/build.yml)
[![CI](https://github.com/hdduong/happy-ride/actions/workflows/ci.yml/badge.svg)](https://github.com/hdduong/happy-ride/actions/workflows/ci.yml)
[![Deployment](https://github.com/hdduong/happy-ride/actions/workflows/deploy.yml/badge.svg)](https://github.com/hdduong/happy-ride/actions/workflows/deploy.yml)
[![version](https://img.shields.io/badge/version-v1.0.67-blue?style=flat-square)](https://github.com/hdduong/happy-ride/releases/tag/v1.0.67)

A merge into `main` runs **Build** and **CI**. Build compiles the API and the web app. CI runs the API tests and the browser ride test. When both are green, CI opens a deployment request. Azure changes only after you open [Deployment](https://github.com/hdduong/happy-ride/actions/workflows/deploy.yml), click **Run workflow**, check **Deploy to Azure production**, and click the green **Run workflow** button.

That button creates or updates one Linux B1 App Service plan, `plan-grabbike`, in `rg-grabbike-dev` (shared by the API and the web). For API telemetry, it applies the optional `APPLICATIONINSIGHTS_CONNECTION_STRING` GitHub secret on every deployment; without that secret, it creates or reuses Application Insights and a Log Analytics workspace and reads the connection string from Azure. A successful deploy tags `v1.0.<commit count>` and updates the version badge. Monitoring ingestion is usage-billed; see the defaults and activation steps in [`docs/observability.md`](docs/observability.md).

Motorbike ride demo with an ASP.NET Core API and Next.js rider and driver screens. The current demo uses SQLite, seeded users, simulated driver movement, and fake places/routes.

- [`AGENTS.md`](AGENTS.md) — how the team works in this repo
- [`.cursor/agents/`](.cursor/agents/) — planner, implementer, verifier, and reviewer
- [`docs/architecture.md`](docs/architecture.md) — feature ownership, dependency rules, and current implementation limits
- [`docs/observability.md`](docs/observability.md) — structured logs, trace IDs, Azure activation, queries, and volume/retention settings
- [`docs/build-plan.md`](docs/build-plan.md) — target MVP architecture and remaining integrations

Run the API with `dotnet run --project src/GrabBike.Api --launch-profile http`. In `web/`, run `npm ci` and `npm run dev`. Open the user picker at `http://localhost:3000`, or use the seeded rider and driver links in `AGENTS.md`.

Validate with `dotnet test happy-ride.sln --configuration Release`, then `npm test` and `npm run build` in `web/`. For the two-browser ride test, run `npm ci`, `npx playwright install chromium`, and `npm test` in `e2e/`.
