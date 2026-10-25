#!/usr/bin/env bash
# TenderSync Docker Compose helper (PLAN.md §15); the PowerShell twin is tendersync.ps1. Runbook: docs/runbooks/local.md.
#
#   init        write deploy/docker/.env with random secrets (JWT key, admin password, document master key)
#   up          build and start everything (init first when .env is missing), then wait until healthy
#   infra       start only PostgreSQL and Seq (for dotnet run / npm run dev on the host)
#   status      containers and URLs
#   logs [svc]  follow logs
#   ingest [n]  one-shot ingest of the last n days (default 7)
#   seed-demo   demo tenders for the dashboard e2e tests (Development only)
#   down        stop and remove containers (volumes stay)
#   reset       stop and DELETE the database, documents and logs (asks first; --force skips)
#
# --prod uses docker-compose.yml alone (Production settings; secrets from .env required).
set -euo pipefail

dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
env_file="$dir/.env"
prod=false
force=false
args=()
for a in "$@"; do
  case "$a" in
    --prod) prod=true ;;
    --force) force=true ;;
    *) args+=("$a") ;;
  esac
done
command="${args[0]:-help}"
argument="${args[1]:-}"

compose() {
  local files=(-f "$dir/docker-compose.yml")
  $prod || files+=(-f "$dir/docker-compose.override.yml")
  docker compose --project-directory "$dir" "${files[@]}" "$@"
}

secret() { head -c "$1" /dev/urandom | base64 | tr -d '\n'; }

setting() {
  local value=""
  [[ -f "$env_file" ]] && value="$(grep -E "^$1=" "$env_file" | tail -n1 | cut -d= -f2- || true)"
  echo "${value:-$2}"
}

init() {
  if [[ -f "$env_file" && "$force" != true ]]; then
    echo "$env_file already exists; leaving it alone (--force overwrites it, and a new master key makes stored documents unreadable)."
    return
  fi
  local password jwt master
  password="$(secret 18 | tr '+/' '-_')"
  jwt="$(secret 48)"
  master="$(secret 32)"
  sed -e "s|^Jwt__SigningKey=.*|Jwt__SigningKey=$jwt|" \
      -e "s|^Seed__AdminEmail=.*|Seed__AdminEmail=admin@tendersync.local|" \
      -e "s|^Seed__AdminPassword=.*|Seed__AdminPassword=$password|" \
      -e "s|^TENDERSYNC_MASTER_KEY=.*|TENDERSYNC_MASTER_KEY=$master|" \
      "$dir/.env.example" > "$env_file"
  echo "Wrote $env_file with random secrets."
  echo "  First admin: admin@tendersync.local / $password (used by the production-like stack; the dev stack signs anyone in)."
  echo "  Back up TENDERSYNC_MASTER_KEY from that file: encrypted company documents cannot be read without it."
}

wait_healthy() {
  local deadline=$((SECONDS + 300))
  for url in "http://localhost:$(setting API_PORT 5000)/health/ready" "http://localhost:$(setting WEB_PORT 3000)/login"; do
    until curl -fsS -o /dev/null --max-time 5 "$url"; do
      (( SECONDS > deadline )) && { echo "Timed out waiting for $url. See: deploy/docker/tendersync.sh logs" >&2; exit 1; }
      sleep 3
    done
    echo "OK   $url"
  done
}

urls() {
  echo
  echo "Dashboard  http://localhost:$(setting WEB_PORT 3000)"
  echo "API        http://localhost:$(setting API_PORT 5000)/scalar"
  echo "Hangfire   http://localhost:$(setting WORKER_PORT 5001)/hangfire  (Admin token: /hangfire?access_token=...)"
  echo "Seq        http://localhost:$(setting SEQ_PORT 5341)"
}

case "$command" in
  init) init ;;
  up) [[ -f "$env_file" ]] || init; compose up -d --build; wait_healthy; urls ;;
  infra) compose up -d postgres seq ;;
  status) compose ps; urls ;;
  logs) if [[ -n "$argument" ]]; then compose logs -f --tail 200 "$argument"; else compose logs -f --tail 200; fi ;;
  ingest) compose run --rm worker --ingest-days "${argument:-7}" ;;
  seed-demo) $prod && { echo "seed-demo only runs in Development (drop --prod)." >&2; exit 1; }; compose run --rm worker --seed-demo ;;
  down) compose down ;;
  reset)
    if [[ "$force" != true ]]; then
      read -r -p "This deletes the database, all documents and the logs of this stack. Type yes to continue: " answer
      [[ "$answer" == "yes" ]] || { echo "Nothing deleted."; exit 0; }
    fi
    compose down --volumes ;;
  *) sed -n '2,15p' "$0" ;;
esac
