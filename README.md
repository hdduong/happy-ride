# happy-ride

[![Build](https://github.com/hdduong/happy-ride/actions/workflows/build.yml/badge.svg)](https://github.com/hdduong/happy-ride/actions/workflows/build.yml)
[![CI](https://github.com/hdduong/happy-ride/actions/workflows/ci.yml/badge.svg)](https://github.com/hdduong/happy-ride/actions/workflows/ci.yml)
[![Deployment](https://github.com/hdduong/happy-ride/actions/workflows/deploy.yml/badge.svg)](https://github.com/hdduong/happy-ride/actions/workflows/deploy.yml)
[![version](https://img.shields.io/badge/version-not_deployed-lightgrey?style=flat-square)](https://github.com/hdduong/happy-ride/releases)

A merge into `main` runs **Build** and **CI**. Build compiles the API and the web app. CI runs the API tests and the browser ride test. When both are green, CI opens a deployment request. Azure changes only after you open [Deployment](https://github.com/hdduong/happy-ride/actions/workflows/deploy.yml), click **Run workflow**, check **Deploy to Azure production**, and click the green **Run workflow** button.

That button creates or updates one Linux B1 App Service plan, `plan-grabbike`, in `rg-grabbike-dev` (about $13 per month, shared by the API and the web). A successful deploy tags `v1.0.<commit count>` and updates the version badge.

Private repository for coordinated work by a team of Cursor agents.

- [`AGENTS.md`](AGENTS.md) — how the team works in this repo
- [`.cursor/agents/`](.cursor/agents/) — planner, implementer, verifier, and reviewer

Invoke a specialist with `/planner`, `/implementer`, `/verifier`, or `/reviewer`. Product code is not part of this initial commit.
