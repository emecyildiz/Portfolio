#!/usr/bin/env bash
set -Eeuo pipefail

umask 077

readonly env_file="${LOG_MONITOR_ENV_FILE:-/etc/emecworks/log-monitor.env}"
readonly container_name="${LOG_MONITOR_CONTAINER:-emecworks-web-1}"
readonly lookback="${LOG_MONITOR_LOOKBACK:-6m}"
readonly suppression_minutes="${LOG_MONITOR_SUPPRESSION_MINUTES:-60}"
readonly state_dir="${LOG_MONITOR_STATE_DIR:-/var/lib/emecworks/log-monitor}"
readonly max_alert_lines=20

if [[ ! "$lookback" =~ ^[1-9][0-9]*[smh]$ ]]; then
    echo "LOG_MONITOR_LOOKBACK must use a positive s, m, or h duration." >&2
    exit 1
fi

if [[ ! "$suppression_minutes" =~ ^[1-9][0-9]*$ ]]; then
    echo "LOG_MONITOR_SUPPRESSION_MINUTES must be a positive integer." >&2
    exit 1
fi

if [[ ! -r "$env_file" ]]; then
    echo "Log monitor environment file is not readable: $env_file" >&2
    exit 1
fi

read_env_value() {
    local key="$1"
    local line

    line="$(grep -m1 -E "^${key}=" "$env_file" || true)"
    if [[ -z "$line" ]]; then
        echo "${key} is required in ${env_file}." >&2
        return 1
    fi

    line="${line#*=}"
    printf '%s' "${line%$'\r'}"
}

N8N_LOG_WEBHOOK_URL="$(read_env_value N8N_LOG_WEBHOOK_URL)"
N8N_LOG_WEBHOOK_TOKEN="$(read_env_value N8N_LOG_WEBHOOK_TOKEN)"
readonly N8N_LOG_WEBHOOK_URL N8N_LOG_WEBHOOK_TOKEN

if [[ ! "$N8N_LOG_WEBHOOK_URL" =~ ^https://hooks\.emecworks\.com/webhook/[A-Za-z0-9_-]+$ ]]; then
    echo "N8N_LOG_WEBHOOK_URL must be an Emecworks production webhook URL." >&2
    exit 1
fi

if [[ ! "$N8N_LOG_WEBHOOK_TOKEN" =~ ^[A-Fa-f0-9]{64,128}$ ]]; then
    echo "N8N_LOG_WEBHOOK_TOKEN must contain 64 to 128 hexadecimal characters." >&2
    exit 1
fi

exec 9>/run/lock/emecworks-log-monitor.lock
if ! flock -n 9; then
    exit 0
fi

if ! docker inspect "$container_name" >/dev/null 2>&1; then
    echo "Portfolio web container was not found: $container_name" >&2
    exit 1
fi

install -d -m 0700 "$state_dir"
find "$state_dir" \
    -maxdepth 1 \
    -type f \
    -name '*.seen' \
    -mmin "+${suppression_minutes}" \
    -delete

raw_log="$(mktemp)"
filtered_log="$(mktemp)"
new_log="$(mktemp)"
trap 'rm -f -- "$raw_log" "$filtered_log" "$new_log"' EXIT

docker logs \
    --since "$lookback" \
    --timestamps \
    "$container_name" >"$raw_log" 2>&1

grep -E \
    '(^|[[:space:]])(fail|crit):|Unhandled exception|[A-Za-z0-9_.]+Exception(:|$)|"LogLevel":"(Error|Critical)"|"level":"(error|critical)"' \
    "$raw_log" |
    sed -E \
        -e 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[^[:space:]]+[[:space:]]+//' \
        -e 's/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/[redacted-email]/g' \
        -e 's/([0-9]{1,3}\.){3}[0-9]{1,3}/[redacted-ip]/g' \
        -e 's/([[:xdigit:]]{0,4}:){2,7}[[:xdigit:]]{0,4}/[redacted-ip]/g' \
        -e 's/((token|password|secret|authorization|api[_-]?key)[=:])[A-Za-z0-9._~+\/=-]+/\1[redacted]/Ig' |
    sed -E '/^[[:space:]]*$/d' |
    sort -u >"$filtered_log" || true

declare -a new_digests=()
while IFS= read -r line; do
    [[ -n "$line" ]] || continue

    digest="$(printf '%s' "$line" | sha256sum | awk '{print $1}')"
    if [[ -e "${state_dir}/${digest}.seen" ]]; then
        continue
    fi

    printf '%s\n' "$line" >>"$new_log"
    new_digests+=("$digest")

    if (( ${#new_digests[@]} >= max_alert_lines )); then
        break
    fi
done <"$filtered_log"

if (( ${#new_digests[@]} == 0 )); then
    exit 0
fi

observed_at="$(date -u +'%Y-%m-%dT%H:%M:%SZ')"
payload_base64="$(base64 -w 0 "$new_log")"

curl \
    --fail \
    --silent \
    --show-error \
    --connect-timeout 10 \
    --max-time 20 \
    --retry 2 \
    --retry-all-errors \
    --request POST \
    --header "Content-Type: application/json" \
    --header "X-Emecworks-Monitor-Token: ${N8N_LOG_WEBHOOK_TOKEN}" \
    --data "{\"source\":\"portfolio-web\",\"observedAt\":\"${observed_at}\",\"payloadBase64\":\"${payload_base64}\"}" \
    "$N8N_LOG_WEBHOOK_URL" >/dev/null

for digest in "${new_digests[@]}"; do
    touch "${state_dir}/${digest}.seen"
done
