#!/usr/bin/env bash
# HVTradingBot launcher for macOS (Apple Silicon and Intel). Compatible with the stock Bash 3.2.
#
#   ./run.sh          Start PostgreSQL in Docker, then run the API + worker natively. Dashboard: http://localhost:5080
#   ./run.sh docker   Run the whole stack in Docker (no .NET/Node needed on the host)
#   ./run.sh test     Run all automated tests (integration tests need Docker)
#   ./run.sh stop     Stop Docker services
#   ./run.sh reset    Stop services and DELETE the database volume (all paper-trading history)
#   ./run.sh setup-code   Show the one-time code for creating the dashboard login
#   ./run.sh reset-login  Delete the dashboard login (forgotten password); create a new one with the code it prints
#   ./run.sh migrate-keys Copy encryption keys from the old `dpkeys` Docker volume to the shared host folder
#                         (done automatically by ./run.sh docker)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"
RUN_DIR="$ROOT/.run"
# Data Protection key ring (decrypts credentials stored in PostgreSQL). Native runs use it directly; docker-compose.yml
# bind-mounts it into the api and worker containers, so both setups share one key ring.
KEYS_DIR="$HOME/Library/Application Support/HVTradingBot/keys"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

require_docker() {
  command -v docker >/dev/null 2>&1 || fail "Docker is not installed. Install Docker Desktop: https://www.docker.com/products/docker-desktop/"
  docker info >/dev/null 2>&1 || fail "Docker is installed but not running. Start Docker Desktop and try again."
}

require_dotnet() {
  command -v dotnet >/dev/null 2>&1 || fail ".NET SDK not found. Install .NET 10: brew install --cask dotnet-sdk  (or https://dotnet.microsoft.com/download)"
  dotnet --list-sdks | grep -q '^10\.' || fail ".NET 10 SDK is required (found: $(dotnet --list-sdks | awk '{print $1}' | tr '\n' ' ')). Install: brew install --cask dotnet-sdk"
}

require_node() {
  command -v node >/dev/null 2>&1 || fail "Node.js not found. Install Node 22: brew install node@22"
  local major minor
  major="$(node -p 'process.versions.node.split(".")[0]')"
  minor="$(node -p 'process.versions.node.split(".")[1]')"
  if [ "$major" -lt 20 ] || { [ "$major" -eq 20 ] && [ "$minor" -lt 19 ]; }; then
    fail "Node.js 20.19+ or 22.12+ is required (found $(node -v)). Install: brew install node@22"
  fi
}

ensure_env() {
  if [ ! -f .env ]; then
    info "Creating .env with a random database password (not committed to git)"
    local password
    password="$(openssl rand -hex 24)"
    sed "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=${password}/" .env.example > .env
    chmod 600 .env
  fi
  set -a
  # shellcheck disable=SC1091
  . ./.env
  set +a
}

# Writes KEY=value to .env without interpreting special characters in the value.
set_env() {
  local key="$1" value="$2" tmp
  tmp="$(mktemp)"
  if grep -q "^${key}=" .env; then
    KEY="$key" VALUE="$value" awk 'BEGIN { k = ENVIRON["KEY"]; v = ENVIRON["VALUE"] } index($0, k "=") == 1 { print k "=" v; next } { print }' .env > "$tmp"
  else
    cp .env "$tmp"
    printf '%s=%s\n' "$key" "$value" >> "$tmp"
  fi
  cat "$tmp" > .env
  rm -f "$tmp"
}

ensure_deriv_credentials() {
  BROKER_PROVIDER="${BROKER_PROVIDER:-Deriv}"
  MARKET_DATA_PROVIDER="${MARKET_DATA_PROVIDER:-Deriv}"
  if [ "$BROKER_PROVIDER" = "Deriv" ] && { [ -z "${DERIV_API_TOKEN:-}" ] || [ -z "${DERIV_APP_ID:-}" ]; }; then
    info "Deriv credentials are entered on the dashboard: Settings tab (stored encrypted in PostgreSQL)."
  fi
}

export_app_settings() {
  export Broker__Provider="${BROKER_PROVIDER:-Deriv}"
  export MarketData__Provider="${MARKET_DATA_PROVIDER:-Deriv}"
  export Deriv__AppId="${DERIV_APP_ID:-}"
  export Deriv__ApiToken="${DERIV_API_TOKEN:-}"
  if [ -n "${DERIV_ACCOUNT_ID:-}" ]; then export Deriv__AccountId="$DERIV_ACCOUNT_ID"; fi
}

wait_for_postgres() {
  info "Waiting for PostgreSQL"
  local i=0
  until docker compose exec -T postgres pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1; do
    i=$((i + 1))
    [ "$i" -gt 60 ] && fail "PostgreSQL did not become ready. See: docker compose logs postgres"
    sleep 1
  done
}

wait_for_api() {
  local url="$1" i=0
  until curl -fsS "$url/health/live" >/dev/null 2>&1; do
    i=$((i + 1))
    [ "$i" -gt 90 ] && fail "API did not start. See $RUN_DIR/api.log"
    sleep 1
  done
}

build_web() {
  info "Building dashboard"
  (cd web/HVTradingBot.Web && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npm run build >/dev/null)
}

run_local() {
  require_docker
  require_dotnet
  require_node
  ensure_env
  ensure_deriv_credentials
  mkdir -p "$RUN_DIR"

  info "Starting PostgreSQL (Docker, port ${POSTGRES_PORT})"
  docker compose up -d postgres >/dev/null
  wait_for_postgres

  build_web
  info "Building .NET solution"
  if ! dotnet build HVTradingBot.sln -c Release -v quiet -nologo >"$RUN_DIR/build.log" 2>&1; then
    grep -E " error " "$RUN_DIR/build.log" | sort -u >&2 || true
    fail ".NET build failed. Full output: .run/build.log"
  fi

  export ConnectionStrings__TradingDb="Host=127.0.0.1;Port=${POSTGRES_PORT};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
  export DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production

  info "Starting API (logs: .run/api.log)"
  (cd src/HVTradingBot.Api && exec dotnet run -c Release --no-build --no-launch-profile --urls "http://127.0.0.1:${API_PORT}") >"$RUN_DIR/api.log" 2>&1 &
  API_PID=$!
  info "Starting trading worker: broker ${BROKER_PROVIDER}, market data ${MARKET_DATA_PROVIDER} (logs: .run/worker.log)"
  : >"$RUN_DIR/worker.log"
  # Exit code 3 = the worker asks to be restarted (e.g. market selection changed on the Settings page).
  (
    export_app_settings
    cd src/HVTradingBot.Worker
    while true; do
      code=0
      dotnet run -c Release --no-build --no-launch-profile || code=$?
      [ "$code" -eq 3 ] || exit "$code"
      echo "[run.sh] restarting worker to apply new settings"
    done
  ) >>"$RUN_DIR/worker.log" 2>&1 &
  WORKER_PID=$!

  trap 'info "Stopping"; pkill -P $WORKER_PID 2>/dev/null; kill $API_PID $WORKER_PID 2>/dev/null; wait 2>/dev/null; info "Stopped. PostgreSQL keeps running; ./run.sh stop to stop it."' INT TERM EXIT

  local url="http://localhost:${API_PORT}"
  wait_for_api "$url"
  info "Dashboard: $url   (Ctrl+C to stop)"
  curl -fsS "$url/api/auth/me" 2>/dev/null | grep -q '"setupRequired":true' && show_setup_code
  open "$url" >/dev/null 2>&1 || true
  tail -n +1 -f "$RUN_DIR/worker.log"
}

# Creates the host key folder (so Docker does not create it as root) and copies keys from the old `dpkeys` volume
# (used before the bind mount) into it. Key files are additive and never overwritten, so this is safe to repeat.
migrate_keys() {
  require_docker
  mkdir -p "$KEYS_DIR"
  docker volume inspect hvtradingbot_dpkeys >/dev/null 2>&1 || return 0
  info "Copying encryption keys from the old Docker volume hvtradingbot_dpkeys to $KEYS_DIR"
  docker run --rm -v hvtradingbot_dpkeys:/from:ro -v "$KEYS_DIR:/to" \
    alpine sh -c 'cp -n /from/*.xml /to/ 2>/dev/null || true' \
    || fail "Copying the encryption keys failed. The old volume is untouched; see the Docker output above."
  info "Done. Once the dashboard shows your Deriv settings, the old volume can go: docker volume rm hvtradingbot_dpkeys"
}

run_docker() {
  require_docker
  ensure_env
  ensure_deriv_credentials
  migrate_keys
  info "Building and starting the full stack in Docker"
  docker compose up -d --build
  local url="http://localhost:${API_PORT}"
  wait_for_api "$url"
  info "Dashboard: $url   (logs: docker compose logs -f worker; stop: ./run.sh stop)"
  curl -fsS "$url/api/auth/me" 2>/dev/null | grep -q '"setupRequired":true' && show_setup_code
  open "$url" >/dev/null 2>&1 || true
}

# The API logs a one-time setup code while no dashboard login exists.
show_setup_code() {
  local url="http://localhost:${API_PORT:-5080}" line=""
  curl -fsS "$url/api/auth/me" 2>/dev/null | grep -q '"setupRequired":true' || { info "A dashboard login already exists. Forgot the password? ./run.sh reset-login"; return 0; }
  if [ -f "$RUN_DIR/api.log" ]; then line="$(grep -h 'setup code' "$RUN_DIR/api.log" | tail -n 1 || true)"; fi
  if [ -z "$line" ] && command -v docker >/dev/null 2>&1; then line="$(docker compose logs api 2>/dev/null | grep 'setup code' | tail -n 1 || true)"; fi
  [ -n "$line" ] || fail "No setup code found in the API log. Is the API running?"
  info "Create your dashboard login at $url with setup code: $(printf '%s' "$line" | grep -oE 'setup code [0-9A-F]{4}-[0-9A-F]{4}' | tail -n 1 | cut -d' ' -f3)"
}

reset_login() {
  require_docker
  ensure_env
  read -r -p "Delete the dashboard login? You will create a new one with a setup code. [y/N] " answer
  [ "$answer" = "y" ] || [ "$answer" = "Y" ] || exit 0
  # shellcheck disable=SC1091
  set -a; . ./.env; set +a
  docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -qc "DELETE FROM app_users;" >/dev/null
  info "Login deleted."
  show_setup_code
}

run_tests() {
  require_dotnet
  require_docker
  dotnet test HVTradingBot.sln -nologo
  require_node
  (cd web/HVTradingBot.Web && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npm run typecheck)
}

case "${1:-local}" in
  local) run_local ;;
  docker) run_docker ;;
  test) run_tests ;;
  stop) require_docker; docker compose stop ;;
  migrate-keys) migrate_keys ;;
  setup-code) show_setup_code ;;
  reset-login) reset_login ;;
  reset)
    require_docker
    read -r -p "Delete the database volume and all paper-trading history? [y/N] " answer
    [ "$answer" = "y" ] || [ "$answer" = "Y" ] || exit 0
    docker compose down -v
    ;;
  *) fail "Unknown command '$1'. Use: local | docker | test | stop | reset | setup-code | reset-login | migrate-keys" ;;
esac
