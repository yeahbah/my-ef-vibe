#!/usr/bin/env bash
# Convert AdventureWorks on SQL Server (source) into PostgreSQL, SQLite, and Oracle targets.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOCKER_DIR="${ROOT}/docker"
INTEGRATION_ROOT="${EFVIBE_INTEGRATION_ROOT:-/home/adiaz/Projects}"
AW_REPO="${INTEGRATION_ROOT}/AdventureWorks"
SQLITE_DIR="${AW_REPO}/Source"
SQLITE_DB="${SQLITE_DIR}/AdventureWorksLT.db"
SQLSERVER_DIR="${INTEGRATION_ROOT}/adventureworks"

# shellcheck disable=SC1091
[[ -f "${DOCKER_DIR}/.env" ]] && source "${DOCKER_DIR}/.env"

POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-Your_strong_Password123!}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_DB="${POSTGRES_DB:-postgres}"
MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-Your_strong_Password123!}"
MSSQL_PORT="${MSSQL_PORT:-1433}"
MSSQL_DATABASE="${MSSQL_DATABASE:-AdventureWorks2022}"
ORACLE_APP_PASSWORD="${ORACLE_APP_PASSWORD:-Oracle1}"
ORACLE_PASSWORD="${ORACLE_PASSWORD:-Oracle1}"

info() { printf '==> %s\n' "$*"; }
warn() { printf 'warning: %s\n' "$*" >&2; }

require_docker() {
  command -v docker >/dev/null 2>&1 || {
    echo "docker is required" >&2
    exit 1
  }
}

wait_container_healthy() {
  local name="$1"
  local attempts="${2:-60}"
  for _ in $(seq 1 "${attempts}"); do
    if docker inspect -f '{{.State.Health.Status}}' "${name}" 2>/dev/null | grep -qx healthy; then
      return 0
    fi
    sleep 2
  done
  echo "Container ${name} did not become healthy in time" >&2
  return 1
}

convert_postgresql() {
  info "Converting SQL Server -> PostgreSQL (pgloader)"

  docker exec efvibe-integration-postgres psql -U postgres -c "DROP DATABASE IF EXISTS adventureworks;" >/dev/null
  docker exec efvibe-integration-postgres psql -U postgres -c "CREATE DATABASE adventureworks;" >/dev/null
  docker exec efvibe-integration-postgres psql -U postgres -d adventureworks -c 'CREATE EXTENSION IF NOT EXISTS "uuid-ossp";' >/dev/null

  docker run --rm --add-host=host.docker.internal:host-gateway dimitri/pgloader:latest pgloader \
    "mssql://sa:${MSSQL_SA_PASSWORD}@host.docker.internal:${MSSQL_PORT}/${MSSQL_DATABASE}" \
    "postgresql://postgres:${POSTGRES_PASSWORD}@host.docker.internal:${POSTGRES_PORT}/adventureworks" \
    2>&1 | tail -5

  docker exec efvibe-integration-postgres psql -U postgres -d adventureworks -c "SELECT count(*) AS products FROM production.product;" >/dev/null
  info "PostgreSQL adventureworks database ready"
}

convert_sqlite() {
  info "Converting PostgreSQL -> SQLite (${SQLITE_DB})"
  mkdir -p "${SQLITE_DIR}"
  rm -f "${SQLITE_DB}"

  local run_sql="${ROOT}/scripts/duckdb-convert.run.sql"
  sed \
    -e "s|__POSTGRES_PASSWORD__|${POSTGRES_PASSWORD}|g" \
    -e "s|__POSTGRES_PORT__|${POSTGRES_PORT}|g" \
    -e "s|__SQLITE_PATH__|/out/AdventureWorksLT.db|g" \
    "${ROOT}/scripts/duckdb-postgres-to-sqlite.sql" >"${run_sql}"

  docker run --rm --add-host=host.docker.internal:host-gateway \
    -v "${SQLITE_DIR}:/out" \
    -v "${ROOT}/scripts:/scripts:ro" \
    duckdb/duckdb:latest duckdb -init /scripts/duckdb-convert.run.sql >/dev/null

  if [[ ! -f "${SQLITE_DB}" ]]; then
    echo "SQLite database was not created at ${SQLITE_DB}" >&2
    exit 1
  fi

  info "SQLite database ready ($(du -h "${SQLITE_DB}" | cut -f1))"
}

convert_oracle() {
  info "Converting PostgreSQL -> Oracle (AdvWorks schema)"

  local py_script="${ROOT}/scripts/postgres_to_oracle.py"
  local columns_file="${ROOT}/scripts/.sqlserver-columns.tsv"
  rm -f "${columns_file}"

  docker exec adventureworks-sql /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "${MSSQL_SA_PASSWORD}" -C -d "${MSSQL_DATABASE}" \
    -h -1 -W -s "|" -Q "
SET NOCOUNT ON;
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA IN ('HumanResources','Person','Production','Purchasing','Sales')
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;
" >"${columns_file}"

  docker run --rm --add-host=host.docker.internal:host-gateway \
    -v "${ROOT}/scripts:/scripts:ro" \
    python:3.12-slim bash -c "
      pip install -q psycopg[binary] oracledb && \
      python /scripts/postgres_to_oracle.py \
        --pg-host host.docker.internal \
        --pg-port ${POSTGRES_PORT} \
        --pg-db adventureworks \
        --pg-user postgres \
        --pg-password '${POSTGRES_PASSWORD}' \
        --oracle-dsn 'host.docker.internal:${ORACLE_PORT:-1521}/FREEPDB1' \
        --oracle-admin-password '${ORACLE_PASSWORD}' \
        --oracle-app-user AdvWorks \
        --schema-password '${ORACLE_APP_PASSWORD}' \
        --sqlserver-columns-file /scripts/.sqlserver-columns.tsv
    " 2>&1 | tail -10

  rm -f "${columns_file}"
  info "Oracle AdvWorks schema ready"
}

main() {
  require_docker

  if ! docker ps --format '{{.Names}}' | grep -qx adventureworks-sql; then
    echo "adventureworks-sql is not running. Run scripts/setup.sh first." >&2
    exit 1
  fi

  if ! docker ps --format '{{.Names}}' | grep -qx efvibe-integration-postgres; then
    echo "efvibe-integration-postgres is not running. Run scripts/setup.sh first." >&2
    exit 1
  fi

  wait_container_healthy efvibe-integration-postgres 30
  wait_container_healthy efvibe-integration-oracle 90 || warn "Oracle healthcheck pending; conversion may still work"

  convert_postgresql
  convert_sqlite
  convert_oracle
}

main "$@"
