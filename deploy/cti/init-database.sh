#!/usr/bin/env sh
set -eu

if [ -z "${CTI_APP_PASSWORD:-}" ]; then
    echo "CTI_APP_PASSWORD is required." >&2
    exit 1
fi

psql \
    --set=ON_ERROR_STOP=1 \
    --set=cti_app_password="$CTI_APP_PASSWORD" \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --file /opt/emecworks-cti/schema.sql
