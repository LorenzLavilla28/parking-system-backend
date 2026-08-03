#!/usr/bin/env bash
# Deploy a verified backend archive on the production EC2 instance.
# Normally invoked by the repository-root deploy.ps1 through AWS Systems Manager.
set -Eeuo pipefail

APP_DIR="${1:-/opt/parking-saas/backend}"
ARCHIVE="${2:?Pass the downloaded backend archive path}"
EXPECTED_SHA256="${3:?Pass the expected SHA-256 checksum}"
EXPECTED_APP_DIR="/opt/parking-saas/backend"
BACKUP_BUCKET="${BACKUP_BUCKET:-parking-saas-backups-prod}"
AWS_REGION_NAME="${AWS_REGION:-ap-southeast-1}"

if [[ "$(readlink -m "$APP_DIR")" != "$EXPECTED_APP_DIR" ]]; then
  echo "Refusing to deploy outside $EXPECTED_APP_DIR" >&2
  exit 1
fi

if [[ ! -f "$APP_DIR/deploy/.env.prod" ]]; then
  echo "Missing $APP_DIR/deploy/.env.prod; refusing to deploy without production configuration." >&2
  exit 1
fi

if [[ ! -f "$ARCHIVE" ]]; then
  echo "Backend archive not found: $ARCHIVE" >&2
  exit 1
fi

printf '%s  %s\n' "$EXPECTED_SHA256" "$ARCHIVE" | sha256sum -c -

COMPOSE=(
  docker compose
  -f "$APP_DIR/deploy/docker-compose.prod.yml"
  --env-file "$APP_DIR/deploy/.env.prod"
)
ROLLBACK_IMAGE="parking-saas-api:rollback"
HAS_ROLLBACK=false

if docker image inspect deploy-api >/dev/null 2>&1; then
  docker tag deploy-api "$ROLLBACK_IMAGE"
  HAS_ROLLBACK=true
fi

backup_database() {
  local stamp backup_name backup_path backup_uri
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  backup_name="parking-saas-postgres-${stamp}.dump"
  backup_path="/tmp/${backup_name}"
  backup_uri="s3://${BACKUP_BUCKET}/database/${backup_name}"

  echo "==> Backing up PostgreSQL before deployment..."
  docker exec parking-saas-postgres sh -c \
    'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > "$backup_path"

  if [[ ! -s "$backup_path" ]]; then
    echo "Database backup is empty; refusing to deploy." >&2
    rm -f -- "$backup_path"
    exit 1
  fi

  aws s3 cp "$backup_path" "$backup_uri" \
    --region "$AWS_REGION_NAME" \
    --only-show-errors
  rm -f -- "$backup_path"
  echo "Database backup uploaded to $backup_uri"
}

backup_database

echo "==> Updating backend source (production environment file is preserved)..."
rm -rf -- "$APP_DIR/src" "$APP_DIR/tests"
rm -f -- "$APP_DIR/ParkingSaaS.slnx"
tar -xzf "$ARCHIVE" -C "$APP_DIR"

echo "==> Validating production Compose configuration..."
"${COMPOSE[@]}" config --quiet

echo "==> Building API image..."
"${COMPOSE[@]}" build api

echo "==> Restarting production services..."
"${COMPOSE[@]}" up -d --no-build --remove-orphans

wait_for_health() {
  local attempts="${1:-60}"
  local delay_seconds="${2:-2}"

  printf '==> Waiting for API readiness'
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    if curl -fsS http://localhost/api/health/ready >/dev/null 2>&1; then
      printf '\n'
      return 0
    fi

    printf '.'
    sleep "$delay_seconds"
  done

  printf '\n'
  return 1
}

prune_deployment_cache() {
  echo "==> Pruning Docker deployment cache older than 7 days..."

  if ! docker builder prune --force --filter "until=168h"; then
    echo "Warning: Docker builder cache cleanup failed; deployment remains healthy." >&2
  fi

  if ! docker image prune --force --filter "until=168h"; then
    echo "Warning: dangling Docker image cleanup failed; deployment remains healthy." >&2
  fi
}

if wait_for_health 60 2; then
  "${COMPOSE[@]}" ps
  rm -f -- "$ARCHIVE"
  prune_deployment_cache
  docker system df
  echo "==> Backend deployment healthy."
  exit 0
fi

echo "API did not become ready. Recent logs:" >&2
"${COMPOSE[@]}" logs --tail=150 api >&2 || true

if [[ "$HAS_ROLLBACK" == true ]]; then
  echo "==> Attempting API image rollback..." >&2
  docker tag "$ROLLBACK_IMAGE" deploy-api
  "${COMPOSE[@]}" up -d --no-deps --no-build --force-recreate api

  if wait_for_health 30 2; then
    echo "Previous API image restored. The deployment still failed and requires investigation." >&2
  else
    echo "Rollback image also failed its readiness check." >&2
  fi
fi

exit 1
