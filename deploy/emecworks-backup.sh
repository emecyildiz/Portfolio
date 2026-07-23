#!/usr/bin/env bash
set -Eeuo pipefail

umask 077

readonly env_file="/etc/emecworks/portfolio.env"
readonly backup_dir="/var/backups/emecworks"
readonly retention_days="${BACKUP_RETENTION_DAYS:-7}"
readonly db_container="emecworks-db-1"
readonly uploads_volume="emecworks_uploads"
readonly dataprotection_volume="emecworks_dataprotection"

if [[ ! "$retention_days" =~ ^[0-9]+$ ]]; then
    echo "BACKUP_RETENTION_DAYS must be a non-negative integer." >&2
    exit 1
fi

if [[ ! -r "$env_file" ]]; then
    echo "Production environment file is not readable: $env_file" >&2
    exit 1
fi

# shellcheck disable=SC1090
source "$env_file"
: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${POSTGRES_DB:?POSTGRES_DB is required}"

exec 9>/run/lock/emecworks-backup.lock
if ! flock -n 9; then
    echo "Another Emecworks backup is already running." >&2
    exit 1
fi

install -d -m 0700 "$backup_dir"
readonly timestamp="$(date -u +'%Y%m%dT%H%M%SZ')"
work_dir="$(mktemp -d "${backup_dir}/.tmp.XXXXXX")"
trap 'rm -rf -- "$work_dir"' EXIT

if [[ "$(docker inspect -f '{{.State.Health.Status}}' "$db_container")" != "healthy" ]]; then
    echo "PostgreSQL container is not healthy." >&2
    exit 1
fi

docker exec "$db_container" pg_dump \
    -U "$POSTGRES_USER" \
    -d "$POSTGRES_DB" \
    -Fc >"${work_dir}/database.dump"

docker run --rm \
    -v "${uploads_volume}:/data:ro" \
    postgres:15-alpine \
    tar -czf - -C /data . >"${work_dir}/uploads.tar.gz"

docker run --rm \
    -v "${dataprotection_volume}:/data:ro" \
    postgres:15-alpine \
    tar -czf - -C /data . >"${work_dir}/dataprotection.tar.gz"

docker run --rm \
    -v "${work_dir}:/backup:ro" \
    postgres:15-alpine \
    pg_restore -l /backup/database.dump >/dev/null

(
    cd "$work_dir"
    sha256sum database.dump uploads.tar.gz dataprotection.tar.gz >SHA256SUMS
)

readonly final_dir="${backup_dir}/${timestamp}"
mv "$work_dir" "$final_dir"
trap - EXIT

find "$backup_dir" \
    -mindepth 1 \
    -maxdepth 1 \
    -type d \
    -mtime "+${retention_days}" \
    -exec rm -rf -- {} +

printf 'Backup completed: %s\n' "$final_dir"
