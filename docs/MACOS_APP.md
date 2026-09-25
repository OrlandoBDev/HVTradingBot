# macOS App (Docker-based MVP)

Status: **planned**, on branch `feature/macos-app`. This document is the design and the implementation plan; an
agent implementing it should work through [Implementation tasks](#implementation-tasks) in order.

## Goal

A native macOS app, installed in `/Applications`, that starts the whole trading stack in Docker and shows the
dashboard. No terminal, no `dotnet run`, no `./run.sh`. This replaces `./run.sh` as the everyday way to run the bot
until the project moves to an external server with a domain.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Where the API and worker run | **Docker** (`docker-compose.yml`, already exists) | Same topology locally, on a future server and in the later Kubernetes phase. The app stays small: it orchestrates and displays. |
| Rejected: app hosts the worker and calls services in-process (no API) | Not built | Would duplicate the API's hosting, auth and SignalR inside the app, tie trading to the app being open, and diverge from the server/Kubernetes path. |
| UI technology | **.NET MAUI** (Mac Catalyst), the `maui` workload is installed | Same language as the backend; later iOS, Android and Windows builds from the same project. Flutter (new language, UI rewrite) and React Native (UI rewrite, weakest macOS support) were considered and rejected. |
| Dashboard | **Reused as is** in a `WebView` pointed at `http://localhost:${API_PORT}` | No UI rewrite. Same origin as the API, so the `SameSite=Strict` session cookie, API calls and SignalR work unchanged. |
| Native UI | Only a startup/status screen and a few controls | Everything else is the React dashboard. |

## How it works

```
HVTradingBot.app (MAUI)
 ├─ Startup screen: checks + `docker compose up -d --build` with live progress
 ├─ WebView → http://localhost:5080   (dashboard served by the api container)
 └─ Status bar: polls /health/ready (worker running / data stale / offline)

Docker (project "hvtradingbot", compose file in the repository checkout)
 ├─ postgres  (volume pgdata: all trading data, settings, login)
 ├─ api       (dashboard + REST + SignalR, 127.0.0.1:5080)
 └─ worker    (Deriv market data + trading; restart: unless-stopped also covers the exit-code-3 settings restart)
```

For the MVP the app builds the images from a repository checkout (setting, default `~/HVTradingBot`). A later step
publishes images to a registry so the app only needs a compose file (see [Later](#later-not-in-the-mvp)).

### Startup sequence (each step shown with its state on the startup screen)

1. **Find Docker.** GUI apps do not get the shell `PATH`. Look for `docker` in `/usr/local/bin`,
   `/opt/homebrew/bin` and `/Applications/Docker.app/Contents/Resources/bin`, and pass a `PATH` containing those
   directories to every child process (`docker compose` needs `docker-credential-desktop` on the `PATH`).
   Not found → "Install Docker Desktop" with a link.
2. **Docker running.** `docker info`; if it fails, `open -a Docker` and wait up to 120 s.
3. **Repository and `.env`.** The configured folder must contain `docker-compose.yml`. If `.env` is missing, create it
   from `.env.example` with a random `POSTGRES_PASSWORD` (same as `ensure_env` in `run.sh`), permissions `600`.
4. **No conflicting native run.** If a native `HVTradingBot.Worker` or `HVTradingBot.Api` process is running, or
   port `API_PORT` is held by something that is not the `api` container, stop with a clear message. Two workers would
   trade the same Deriv account.
5. **Start.** `docker compose --project-directory <repo> up -d --build`, output streamed to an expandable log view.
   The first build takes minutes; later starts take seconds.
6. **Wait for the API.** Poll `/health/live` (timeout 180 s), then show the dashboard.
7. **Keep watching.** Poll `/health/ready` every 15 s and show the worker state in a status bar.

### Controls

- **Stop trading** (`docker compose stop`), **Start**, **Restart worker** (`docker compose restart worker`).
- **Logs**: last 200 lines of `docker compose logs worker` / `api`.
- **Open in browser**.
- **Quitting the app leaves the stack running** so trading continues; the first quit explains this. A setting
  "Stop trading when the app quits" exists and is off by default.

## Shared encryption keys (no re-entering Deriv credentials)

Deriv and email credentials are stored encrypted in PostgreSQL; the Data Protection keys that decrypt them live on
disk, outside the database (`DataProtectionSetup.cs`). Native runs use
`~/Library/Application Support/HVTradingBot/keys`; Docker currently uses the `dpkeys` volume. The containers would
find the encrypted token but could not decrypt it.

Change: mount the host folder into `api` and `worker` instead of the `dpkeys` volume:

```yaml
    volumes:
      - "${HOME}/Library/Application Support/HVTradingBot/keys:/keys"
```

Both setups use the application name `HVTradingBot`, so native and Docker runs can then read each other's
encrypted values. Before switching, copy any keys already in the `dpkeys` volume into the host folder (key files are
additive; a key ring holding both sets decrypts everything):

```bash
docker run --rm -v hvtradingbot_dpkeys:/from -v "$HOME/Library/Application Support/HVTradingBot/keys:/to" \
  alpine sh -c 'cp -n /from/*.xml /to/ 2>/dev/null || true'
```

The containers run as a non-root user; verify they can read and write the bind-mounted folder on Docker Desktop.

## Never two workers on one account

Two workers on the same database and Deriv account would place duplicate orders. Guards, strongest first:

1. **Worker single-instance lock (in code).** On startup the worker opens a dedicated PostgreSQL connection and calls
   `pg_try_advisory_lock(<constant key>)`. If another worker holds it, log a warning once and retry every 10 s,
   keeping the process alive (no restart loop). The lock is released automatically if the process dies.
2. **The app** refuses to start Docker while native API/worker processes are running (startup step 4).
3. **`run.sh local`** refuses to start while the `api` or `worker` container is running, and `run.sh docker` refuses
   while native processes are running.

## Running the web dashboard and the app at the same time

| Scenario | How | Safe? |
|---|---|---|
| Browser and app both viewing the Docker stack | Open `http://localhost:5080` in a browser while the app runs. Each signs in separately (the app's WebView has its own cookies). | Yes, one worker |
| Changing the dashboard (React) | `cd web/HVTradingBot.Web && npm run dev` → `http://localhost:5173`; Vite proxies `/api`, `/hubs`, `/health` to the Docker API on 5080. | Yes, one worker |
| Changing .NET code while the app runs | New `./run.sh dev`: native API on **5081**, native worker with `MARKET_DATA_PROVIDER=Simulated`, `BROKER_PROVIDER=Paper` and a separate database `hvtradingbot_dev` (created in the same Postgres container if missing). | Yes, separate data and no Deriv orders |

`./run.sh local` (native, real database, Deriv) stays for when the app is not running.

## Project layout

```
apps/HVTradingBot.App/            MAUI head project, net10.0-maccatalyst (not in HVTradingBot.sln)
src/HVTradingBot.App.Core/        net10.0 class library: all logic, no MAUI dependency (in HVTradingBot.sln)
tests/HVTradingBot.App.Tests/     xUnit tests for App.Core (in HVTradingBot.sln)
```

- `App.Core` holds the testable logic: Docker/compose command building, locating Docker, the startup pipeline as a
  sequence of steps with states, `.env` creation, conflict detection, health response parsing. Child processes go
  through an `IProcessRunner` interface so tests use a fake.
- The MAUI project is thin: pages, bindings, WebView, `Preferences` for settings. It stays out of
  `HVTradingBot.sln` so `dotnet build`/`dotnet test` of the solution keep working on Linux and inside the Docker
  images.
- Mac Catalyst specifics: App Sandbox **off** (the app runs `docker`; not distributed through the App Store);
  `NSAppTransportSecurity` → `NSAllowsLocalNetworking = true` for `http://localhost`. `System.Diagnostics.Process`
  is supported on Mac Catalyst.
- Packaging: `apps/HVTradingBot.App/build-app.sh` runs `dotnet publish -f net10.0-maccatalyst -c Release`, ad-hoc
  signs the `.app` and copies it to `/Applications`. Unsigned by Apple for now: first launch via right-click → Open.

Follow `docs/AGENTS.md` and the existing code style (file-scoped namespaces, primary constructors,
`CancellationToken` everywhere, structured logging, no swallowed exceptions).

## Implementation tasks

Each task is one commit (or a few) on `feature/macos-app`. Keep `dotnet build HVTradingBot.sln` and
`dotnet test HVTradingBot.sln` green after every task.

1. **Shared keys.** Replace the `dpkeys` volume with the host bind mount in `docker-compose.yml`; add
   `./run.sh migrate-keys` (the copy command above) and call it from `run_docker` before `docker compose up`.
   Update the Docker notes in `README.md`.
2. **Worker single-instance lock.** Advisory lock as described above, in Infrastructure, acquired before the worker
   loads market data. Integration test: a second acquisition on the same database fails while the first is held and
   succeeds after it is released.
3. **`run.sh` guards and `dev` mode.** Conflict checks between native and Docker runs; `./run.sh dev` as described
   above; document in the `run.sh` header comment and `README.md`.
4. **`HVTradingBot.App.Core` + tests.** Docker locator, `IProcessRunner`, startup pipeline with step states,
   `.env` creation, conflict detection, `/health/ready` parsing. Unit tests for each, using fakes.
5. **MAUI app.** `apps/HVTradingBot.App`: startup screen bound to the pipeline, WebView page, status bar,
   controls, settings (repository folder, stop-on-quit), entitlements and Info.plist, `build-app.sh`.
6. **CI.** `.github/workflows/ci.yml`: an `ubuntu-latest` job (build + `dotnet test HVTradingBot.sln`; integration
   tests use Docker, which the runner has) and a `macos-latest` job (`dotnet workload install maui`, build
   `apps/HVTradingBot.App` for `net10.0-maccatalyst`). This lets agents without a Mac see whether the app builds.
7. **Docs.** `README.md` "Mac app" section (install, first run, where logs are), update `docs/ROADMAP.md` status.
   Also fix the stale README line "No user authentication yet"; the dashboard login exists.

### Manual acceptance test (on the Mac, after task 5)

- [ ] Fresh start with Docker Desktop not running: the app starts Docker, builds, shows the dashboard.
- [ ] Deriv credentials entered earlier in native runs still work (no re-entry); worker shows "running, data fresh".
- [ ] Browser at `http://localhost:5080` and the app show the same live data at the same time.
- [ ] With `dotnet run` worker running, the app refuses to start and says why.
- [ ] Quit the app: containers keep running. Stop trading: containers stop; dashboard shows the worker offline.
- [ ] Change the market selection in Settings: the worker container restarts and applies it.
- [ ] `./run.sh dev` runs alongside the app on port 5081 with simulated data.

## Notes for cloud agents

Claude Code cloud sessions run on Linux without Xcode, and usually without Docker. Tasks 1–4, 6 and 7 can be built
and unit-tested there (integration tests need Docker). Task 5 (MAUI, Mac Catalyst) cannot be built on Linux: write
it carefully, keep logic in `App.Core`, and rely on the CI `macos-latest` job from task 6 (do task 6 before task 5
if CI is needed for feedback). The owner runs the manual acceptance test on the Mac.

## Later (not in the MVP)

- Publish `api` and `worker` images to a registry (e.g. GitHub Container Registry) so the app no longer needs the
  repository checkout or a local build.
- Menu-bar status icon (needs a small native AppKit piece; Mac Catalyst cannot do it cleanly).
- Apple Developer ID signing and notarization; auto-update.
- Remote mode: point the app at an external server URL once a domain and server exist; the same MAUI project then
  targets iOS, Android and Windows.
