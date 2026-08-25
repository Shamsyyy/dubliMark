#!/usr/bin/env bash
set -euo pipefail
# Apply SQL in db/init onto running VPS postgres (existing volume skips init scripts).
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
COMPOSE="${ROOT}/docker-compose.prod.yml"
USER_NAME="${POSTGRES_USER:-doublemark}"
DB_NAME="${POSTGRES_DB:-doublemark}"

if ! docker compose -f "$COMPOSE" ps postgres --status running >/dev/null 2>&1; then
  echo "Start stack first: docker compose -f docker-compose.prod.yml up -d"
  exit 1
fi

for f in "$ROOT"/db/init/*.sql; do
  echo "Applying $(basename "$f")..."
  docker compose -f "$COMPOSE" exec -T postgres \
    psql -U "$USER_NAME" -d "$DB_NAME" -v ON_ERROR_STOP=1 -f - < "$f"
done

echo "OK"
