#!/usr/bin/env bash
set -Eeuo pipefail

umask 077

readonly portfolio_env_file="/etc/emecworks/portfolio.env"
readonly n8n_db_env_file="/etc/emecworks/n8n-db.env"
readonly n8n_app_env_file="/etc/emecworks/n8n-app.env"
readonly backup_dir="/var/backups/emecworks"
readonly retention_days="${BACKUP_RETENTION_DAYS:-7}"
readonly portfolio_db_container="emecworks-db-1"
readonly n8n_db_container="emecworks-n8n-db-1"
readonly portfolio_uploads_volume="emecworks_uploads"
readonly portfolio_dataprotection_volume="emecworks_dataprotection"
readonly n8n_data_volume="emecworks-n8n_n8n_data"
readonly n8n_files_dir="/var/lib/emecworks/n8n-files"
readonly helper_image="postgres:16-alpine@sha256:57c72fd2a128e416c7fcc499958864df5301e940bca0a56f58fddf30ffc07777"

if [[ ! "$retention_days" =~ ^[0-9]+$ ]]; then
    echo "BACKUP_RETENTION_DAYS must be a non-negative integer." >&2
    exit 1
fi

for required_file in "$portfolio_env_file" "$n8n_db_env_file" "$n8n_app_env_file"; do
    if [[ ! -r "$required_file" ]]; then
        echo "Required environment file is not readable: $required_file" >&2
        exit 1
    fi
done

if [[ ! -d "$n8n_files_dir" ]]; then
    echo "n8n persistent files directory does not exist: $n8n_files_dir" >&2
    exit 1
fi

read_env_value() {
    local env_file="$1"
    local key="$2"
    local line

    line="$(grep -m1 -E "^${key}=" "$env_file" || true)"
    if [[ -z "$line" ]]; then
        echo "${key} is required in ${env_file}." >&2
        return 1
    fi

    line="${line#*=}"
    printf '%s' "${line%$'\r'}"
}

validate_postgres_identifier() {
    local value="$1"
    local label="$2"

    if [[ ! "$value" =~ ^[A-Za-z_][A-Za-z0-9_-]{0,62}$ ]]; then
        echo "${label} contains unsupported characters." >&2
        return 1
    fi
}

PORTFOLIO_POSTGRES_USER="$(read_env_value "$portfolio_env_file" POSTGRES_USER)"
PORTFOLIO_POSTGRES_DB="$(read_env_value "$portfolio_env_file" POSTGRES_DB)"
N8N_POSTGRES_USER="$(read_env_value "$n8n_db_env_file" POSTGRES_USER)"
N8N_POSTGRES_DB="$(read_env_value "$n8n_db_env_file" POSTGRES_DB)"
N8N_ENCRYPTION_KEY="$(read_env_value "$n8n_app_env_file" N8N_ENCRYPTION_KEY)"
readonly PORTFOLIO_POSTGRES_USER PORTFOLIO_POSTGRES_DB
readonly N8N_POSTGRES_USER N8N_POSTGRES_DB N8N_ENCRYPTION_KEY

validate_postgres_identifier "$PORTFOLIO_POSTGRES_USER" "Portfolio POSTGRES_USER"
validate_postgres_identifier "$PORTFOLIO_POSTGRES_DB" "Portfolio POSTGRES_DB"
validate_postgres_identifier "$N8N_POSTGRES_USER" "n8n POSTGRES_USER"
validate_postgres_identifier "$N8N_POSTGRES_DB" "n8n POSTGRES_DB"

if (( ${#N8N_ENCRYPTION_KEY} < 32 )); then
    echo "N8N_ENCRYPTION_KEY is unexpectedly short." >&2
    exit 1
fi

exec 9>/run/lock/emecworks-backup.lock
if ! flock -n 9; then
    echo "Another Emecworks backup is already running." >&2
    exit 1
fi

install -d -m 0700 "$backup_dir"
readonly timestamp="$(date -u +'%Y%m%dT%H%M%SZ')"
work_dir="$(mktemp -d "${backup_dir}/.tmp.XXXXXX")"
trap 'rm -rf -- "$work_dir"' EXIT

require_healthy_container() {
    local container="$1"
    local health

    health="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container")"
    if [[ "$health" != "healthy" ]]; then
        echo "Required container is not healthy: $container ($health)" >&2
        return 1
    fi
}

dump_database() {
    local container="$1"
    local user="$2"
    local database="$3"
    local destination="$4"

    docker exec "$container" pg_dump \
        -U "$user" \
        -d "$database" \
        -Fc >"$destination"

    if [[ ! -s "$destination" ]]; then
        echo "Database dump is empty: $destination" >&2
        return 1
    fi
}

archive_volume() {
    local volume="$1"
    local destination="$2"

    docker volume inspect "$volume" >/dev/null
    docker run --rm \
        --network none \
        --read-only \
        --cap-drop ALL \
        --cap-add DAC_READ_SEARCH \
        --security-opt no-new-privileges:true \
        -v "${volume}:/data:ro" \
        "$helper_image" \
        tar -czf - -C /data . >"$destination"
}

archive_directory() {
    local source="$1"
    local destination="$2"

    docker run --rm \
        --network none \
        --read-only \
        --cap-drop ALL \
        --cap-add DAC_READ_SEARCH \
        --security-opt no-new-privileges:true \
        -v "${source}:/data:ro" \
        "$helper_image" \
        tar -czf - -C /data . >"$destination"
}

verify_database_dump() {
    local filename="$1"

    docker run --rm \
        --network none \
        --read-only \
        --cap-drop ALL \
        --security-opt no-new-privileges:true \
        -v "${work_dir}:/backup:ro" \
        "$helper_image" \
        pg_restore -l "/backup/${filename}" >/dev/null
}

require_healthy_container "$portfolio_db_container"
require_healthy_container "$n8n_db_container"

dump_database \
    "$portfolio_db_container" \
    "$PORTFOLIO_POSTGRES_USER" \
    "$PORTFOLIO_POSTGRES_DB" \
    "${work_dir}/portfolio-database.dump"

dump_database \
    "$n8n_db_container" \
    "$N8N_POSTGRES_USER" \
    "$N8N_POSTGRES_DB" \
    "${work_dir}/n8n-database.dump"

archive_volume \
    "$portfolio_uploads_volume" \
    "${work_dir}/portfolio-uploads.tar.gz"

archive_volume \
    "$portfolio_dataprotection_volume" \
    "${work_dir}/portfolio-dataprotection.tar.gz"

archive_volume \
    "$n8n_data_volume" \
    "${work_dir}/n8n-data.tar.gz"

archive_directory \
    "$n8n_files_dir" \
    "${work_dir}/n8n-files.tar.gz"

verify_database_dump "portfolio-database.dump"
verify_database_dump "n8n-database.dump"

for archive in \
    portfolio-uploads.tar.gz \
    portfolio-dataprotection.tar.gz \
    n8n-data.tar.gz \
    n8n-files.tar.gz; do
    tar -tzf "${work_dir}/${archive}" >/dev/null
done

readonly encryption_key_fingerprint="$(
    printf '%s' "$N8N_ENCRYPTION_KEY" | sha256sum | awk '{print $1}'
)"

{
    printf 'backup_format=2\n'
    printf 'created_utc=%s\n' "$timestamp"
    printf 'portfolio_db_image=%s\n' \
        "$(docker inspect -f '{{.Config.Image}}' "$portfolio_db_container")"
    printf 'n8n_db_image=%s\n' \
        "$(docker inspect -f '{{.Config.Image}}' "$n8n_db_container")"
    printf 'n8n_image=%s\n' \
        "$(docker inspect -f '{{.Config.Image}}' emecworks-n8n-n8n-1)"
    printf 'n8n_encryption_key_sha256=%s\n' "$encryption_key_fingerprint"
} >"${work_dir}/MANIFEST"

(
    cd "$work_dir"
    sha256sum \
        portfolio-database.dump \
        n8n-database.dump \
        portfolio-uploads.tar.gz \
        portfolio-dataprotection.tar.gz \
        n8n-data.tar.gz \
        n8n-files.tar.gz \
        MANIFEST >SHA256SUMS
)

readonly final_dir="${backup_dir}/${timestamp}"
mv "$work_dir" "$final_dir"
trap - EXIT

find "$backup_dir" \
    -mindepth 1 \
    -maxdepth 1 \
    -type d \
    -name '????????T??????Z' \
    -mtime "+${retention_days}" \
    -exec rm -rf -- {} +

printf 'Backup completed: %s\n' "$final_dir"
