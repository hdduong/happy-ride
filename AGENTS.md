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
- Do not commit secrets. `.env` files, keys, and credential files stay untracked.
- Work on a branch and open a pull request. Leave `main` as the integration branch.
- API and web commands:
  - API: `dotnet run --project src/GrabBike.Api --launch-profile http` (http://localhost:5080)
  - Web: `npm install` then `npm run dev` in `web/` (http://localhost:3000)
  - API tests: `dotnet test`
  - Browser test: `npx playwright test` in `e2e/` (starts the API and the web app)
- Mock riders and drivers are seeded in the API. Open `/rider?as=11111111-1111-1111-1111-111111111111` and `/driver?as=33333333-3333-3333-3333-333333333333` in two browsers.

## Cursor Cloud specific instructions

- No install script is configured. Dependency installation is a no-op until the project has a package manager.
- Do not start long-running services. Nothing in this repository listens on a port yet.
- After implementation, delegate to `verifier`. If no test command exists, say so and describe the check that was actually run.
