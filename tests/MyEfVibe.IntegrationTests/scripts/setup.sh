#!/usr/bin/env bash
set -euo pipefail

# Setup integration tests: AdventureWorks repo + Docker databases converted from SQL Server.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MY_EF_VIBE_ROOT="$(cd "${ROOT}/../.." && pwd)"
INTEGRATION_ROOT="${EFVIBE_INTEGRATION_ROOT:-/home/adiaz/Projects}"
DOCKER_DIR="${ROOT}/docker"
AW_REPO="${INTEGRATION_ROOT}/AdventureWorks"
CB_REPO="${INTEGRATION_ROOT}/AdventureWorksCouchBase"
MYSQL_REPO="${INTEGRATION_ROOT}/AdventureWorksMySql"
SQLSERVER_DIR="${INTEGRATION_ROOT}/adventureworks"
AW_URL="${ADVENTUREWORKS_REPO_URL:-https://github.com/theMickster/AdventureWorks.git}"
CB_URL="${ADVENTUREWORKS_COUCHBASE_REPO_URL:-}"

info() { printf '==> %s\n' "$*"; }
warn() { printf 'warning: %s\n' "$*" >&2; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required command: $1" >&2
    exit 1
  }
}

ensure_adventureworks_repo() {
  if [[ -d "${AW_REPO}/.git" ]]; then
    info "Updating AdventureWorks at ${AW_REPO}"
    git -C "${AW_REPO}" fetch origin main --quiet
    git -C "${AW_REPO}" pull --ff-only origin main --quiet 2>/dev/null || true
    return 0
  fi

  info "Cloning AdventureWorks -> ${AW_REPO}"
  git clone --branch main --depth 1 "${AW_URL}" "${AW_REPO}"
}

configure_sqlserver_secrets() {
  local api_project="${AW_REPO}/apps/api-dotnet/src/AdventureWorks.API/AdventureWorks.API.csproj"
  [[ -f "${api_project}" ]] || return 0

  # shellcheck disable=SC1091
  [[ -f "${DOCKER_DIR}/.env" ]] && source "${DOCKER_DIR}/.env"
  local password="${MSSQL_SA_PASSWORD:-Your_strong_Password123!}"

  local connection_string="Server=localhost,1433;Database=AdventureWorks2022;User Id=sa;Password=${password};Encrypt=true;TrustServerCertificate=true;Application Name=AdventureWorks.API;"
  info "Setting AdventureWorks.API user secrets (SQL Server)"
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "${connection_string}" --project "${api_project}" >/dev/null
}

setup_sqlserver() {
  if [[ ! -d "${SQLSERVER_DIR}" ]]; then
    warn "SQL Server docker project not found at ${SQLSERVER_DIR}; skipping SQL Server restore."
    return 0
  fi

  info "Ensuring SQL Server (adventureworks-sql) with AdventureWorks2022"
  if [[ ! -f "${SQLSERVER_DIR}/.env" ]]; then
    cp "${SQLSERVER_DIR}/.env.example" "${SQLSERVER_DIR}/.env"
  fi

  chmod +x "${SQLSERVER_DIR}/scripts/"*.sh 2>/dev/null || true
  "${SQLSERVER_DIR}/scripts/setup.sh"
  configure_sqlserver_secrets
}

ensure_adventureworks_couchbase_repo() {
  if [[ -d "${CB_REPO}/.git" ]]; then
    info "Updating AdventureWorksCouchBase at ${CB_REPO}"
    git -C "${CB_REPO}" fetch origin main --quiet 2>/dev/null || true
    git -C "${CB_REPO}" pull --ff-only origin main --quiet 2>/dev/null || true
    return 0
  fi

  if [[ -z "${CB_URL}" ]]; then
    warn "AdventureWorksCouchBase not found at ${CB_REPO}; clone it or set ADVENTUREWORKS_COUCHBASE_REPO_URL."
    return 0
  fi

  info "Cloning AdventureWorksCouchBase -> ${CB_REPO}"
  git clone --branch main --depth 1 "${CB_URL}" "${CB_REPO}"
}

setup_couchbase() {
  if [[ ! -f "${CB_REPO}/docker-compose.yml" ]]; then
    warn "Couchbase docker-compose not found at ${CB_REPO}; skipping Couchbase."
    return 0
  fi

  # shellcheck disable=SC1091
  [[ -f "${DOCKER_DIR}/.env" ]] && source "${DOCKER_DIR}/.env"
  local sql_password="${MSSQL_SA_PASSWORD:-Your_strong_Password123!}"

  info "Starting Couchbase (aw-couchbase) and migration"
  MIGRATION_SQL_CONNECTION_STRING="Server=host.docker.internal,1433;Database=AdventureWorks2022;User Id=sa;Password=${sql_password};TrustServerCertificate=true" \
    docker compose -f "${CB_REPO}/docker-compose.yml" up -d couchbase couchbase-migrate

  info "Waiting for Couchbase (first start can take several minutes)..."
  for _ in $(seq 1 90); do
    if docker inspect -f '{{.State.Health.Status}}' aw-couchbase 2>/dev/null | grep -qx healthy; then
      break
    fi
    sleep 5
  done
}

setup_mysql() {
  if [[ ! -f "${MYSQL_REPO}/docker-compose.yml" ]]; then
    warn "MySQL docker-compose not found at ${MYSQL_REPO}; skipping MySQL."
    return 0
  fi

  info "Starting MySQL (aw-mysql)"
  docker compose -f "${MYSQL_REPO}/docker-compose.yml" up -d mysql

  info "Waiting for MySQL..."
  for _ in $(seq 1 60); do
    if docker inspect -f '{{.State.Health.Status}}' aw-mysql 2>/dev/null | grep -qx healthy; then
      break
    fi
    sleep 2
  done
}

setup_docker_targets() {
  require_cmd docker

  if [[ ! -f "${DOCKER_DIR}/.env" ]]; then
    cp "${DOCKER_DIR}/.env.example" "${DOCKER_DIR}/.env"
    info "Created ${DOCKER_DIR}/.env"
  fi

  info "Starting PostgreSQL and Oracle containers"
  docker compose --env-file "${DOCKER_DIR}/.env" -f "${DOCKER_DIR}/docker-compose.yml" up -d

  info "Waiting for PostgreSQL..."
  for _ in $(seq 1 60); do
    if docker inspect -f '{{.State.Health.Status}}' efvibe-integration-postgres 2>/dev/null | grep -qx healthy; then
      break
    fi
    sleep 2
  done

  info "Waiting for Oracle (first start can take several minutes)..."
  for _ in $(seq 1 90); do
    if docker inspect -f '{{.State.Health.Status}}' efvibe-integration-oracle 2>/dev/null | grep -qx healthy; then
      break
    fi
    sleep 5
  done
}

main() {
  require_cmd dotnet
  require_cmd git
  require_cmd docker

  info "Integration root: ${INTEGRATION_ROOT}"
  ensure_adventureworks_repo
  ensure_adventureworks_couchbase_repo
  setup_sqlserver
  setup_docker_targets
  setup_mysql
  setup_couchbase
  "${ROOT}/scripts/convert-databases.sh"

  info "Building MyEfVibe"
  dotnet build "${MY_EF_VIBE_ROOT}/src/MyEfVibe/MyEfVibe.csproj" -c Debug --verbosity quiet

  info "Setup complete."
  echo ""
  echo "Run integration tests:"
  echo "  export EFVIBE_RUN_INTEGRATION=1"
  echo "  export EFVIBE_INTEGRATION_ROOT=${INTEGRATION_ROOT}"
  echo "  dotnet test ${ROOT}/MyEfVibe.IntegrationTests.csproj"
}

main "$@"
