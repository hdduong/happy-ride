# happy-ride

Private workspace for a team of Cursor agents. The parent agent coordinates. Specialists in `.cursor/agents/` do the focused work.

## Team

| Agent | Role |
| --- | --- |
| `planner` | Turn a request into a small ordered plan. Read only. |
| `implementer` | Implement one planned slice and leave the tree in a working state. |
| `verifier` | Run the project's checks and report what passed, failed, or is still missing. |
| `reviewer` | Review the diff for correctness, security, and maintainability. Read only. |

Built-in Explore and Browser subagents stay available for search and UI checks.

Delegate to `planner` before a multi-step change, to `implementer` for the code, then to `verifier` and `reviewer` before calling the work done.

## Working agreement

- Read this file before changing the repository.
- Stay inside the requested task. Do not invent a product, stack, or architecture.
- Follow the feature boundaries and dependency rules in [`docs/architecture.md`](docs/architecture.md). Review backend and frontend ownership as well as functional correctness.
- Do not commit secrets. `.env` files, keys, and credential files stay untracked.
- Work on a branch and open a pull request. Leave `main` as the integration branch.
- API and web commands:
  - Database: `docker compose up -d` starts local PostGIS. The API connection string is `Host=localhost;Port=5432;Database=happyride;Username=happyride;Password=happyride`.
  - API: `dotnet run --project src/GrabBike.Api --launch-profile http` (http://localhost:5080). Reference docs: http://localhost:5080/scalar/v1 and http://localhost:5080/swagger. `/` and `/scalar` redirect to Scalar. Both UIs read the OpenAPI document generated from the controllers. Map search stays on the API. The browser only receives the public tile style or Google browser key from `GET /api/map-config`. `Maps:Provider` is `Fake` unless VietMap or Google keys are set.
  - Web: `npm install` then `npm run dev` in `web/` (http://localhost:3000)
  - API tests: `dotnet test`
  - Frontend state and connection tests: `npm test` in `web/`
  - Browser test: `npx playwright test` in `e2e/` (starts the API and the web app)
  - GitHub Actions: `Build` compiles the API and web, `CI` runs both test suites, and `Deployment` publishes to Azure only after the Run workflow button
- Mock riders and drivers are seeded in the API. Open `/rider?as=11111111-1111-1111-1111-111111111111` and `/driver?as=33333333-3333-3333-3333-333333333333` in two browsers.

## Cursor Cloud specific instructions

- Install locked JavaScript dependencies with `npm ci` in `web/` and `e2e/`.
- Do not leave long-running services behind. Playwright starts and stops its API and web servers. Set `E2E_API_PORT` and `E2E_WEB_PORT` when 5080 or 3000 are already in use; those overrides start new servers instead of reusing the dev processes.
- After implementation, delegate to `verifier`. If no test command exists, say so and describe the check that was actually run.
