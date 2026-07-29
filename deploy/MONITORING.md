# Emecworks monitoring

The production monitoring stack has independent layers:

1. Cloudflare Tunnel Health sends an external email when the tunnel changes
   health state.
2. The `Emecworks Health Monitor` n8n workflow checks portfolio and database
   readiness through the private Docker network.
3. The `n8n Workflow Error Alerts` workflow sends sanitized workflow failures
   to Telegram.
4. The host log monitor scans only the portfolio web container for new error
   signatures and sends a sanitized payload to an authenticated n8n webhook.
5. The `Emecworks Weekly Analytics Report` n8n workflow sends a privacy-safe
   summary of the last seven complete UTC days to Telegram every Monday.

The n8n container is not given access to `/var/run/docker.sock`. The host-side
log monitor remains narrowly scoped to `emecworks-web-1`.

## n8n log webhook

Create a workflow named `Emecworks Application Log Alerts`:

1. Add a `Webhook` node using `POST` and the path `portfolio-log-alert`.
2. Select `Header Auth` and store a 64-character hexadecimal token under the
   header `X-Emecworks-Monitor-Token`.
3. Decode `body.payloadBase64`, validate `body.source`, cap the decoded text,
   and format a plain-text Telegram notification.
4. Send the notification with the dedicated Telegram credential and chat ID.
5. End with `Respond to Webhook` and an HTTP 202 response.
6. Select `n8n Workflow Error Alerts` as this workflow's error workflow.
7. Publish the workflow.

Generate the shared token on the VPS:

```bash
openssl rand -hex 32
```

Never commit the generated value.

## Weekly analytics report

The portfolio exposes `GET /internal/analytics/weekly` only through the
`emecworks-internal` Caddy hostname on the private Docker network. Requests to
the same path through `emecworks.com` return HTTP 404, including requests that
contain the correct token.

Store a 64-character hexadecimal value in the production portfolio environment
file:

```text
WEEKLY_REPORT_TOKEN=<generated value>
```

Store the same value in an n8n `Header Auth` credential named
`Emecworks Weekly Report Header Auth`:

```text
X-Emecworks-Report-Token: <generated value>
```

The n8n workflow uses:

- a weekly Schedule Trigger for Monday at 09:00 in `Europe/Istanbul`;
- an HTTP Request to
  `http://emecworks-gateway:8080/internal/analytics/weekly`;
- a `Host: emecworks-internal` request header;
- the Header Auth credential above;
- a Code node that labels the result as daily unique visits, not verified
  individual people;
- the existing Telegram credential and `n8n Workflow Error Alerts` workflow.

The endpoint never selects or returns the visitor hash or IP address. Countries
and entry paths are shown only when a bucket contains at least two visits.
Lower-volume values are combined into aggregate counts. The response uses
`Cache-Control: no-store` and `X-Robots-Tag: noindex, nofollow, noarchive`.

The proxy access log deletes the `X-Emecworks-Report-Token` header before
serializing a request. Do not remove that filter from `deploy/Caddyfile`.

## VPS installation

Install the script and systemd units from the production checkout:

```bash
sudo install -m 0750 \
  /opt/emecworks/portfolio/deploy/emecworks-log-monitor.sh \
  /usr/local/sbin/emecworks-log-monitor

sudo install -m 0644 \
  /opt/emecworks/portfolio/deploy/emecworks-log-monitor.service \
  /etc/systemd/system/emecworks-log-monitor.service

sudo install -m 0644 \
  /opt/emecworks/portfolio/deploy/emecworks-log-monitor.timer \
  /etc/systemd/system/emecworks-log-monitor.timer

sudo install -m 0600 \
  /opt/emecworks/portfolio/deploy/log-monitor.env.example \
  /etc/emecworks/log-monitor.env
```

Replace both placeholder values in `/etc/emecworks/log-monitor.env`. Use the
production webhook URL shown by n8n and the same token stored in its Header Auth
credential.

Load and enable the timer:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now emecworks-log-monitor.timer
```

## Validation

Verify the unit and timer:

```bash
sudo systemd-analyze verify \
  /etc/systemd/system/emecworks-log-monitor.service \
  /etc/systemd/system/emecworks-log-monitor.timer

sudo systemctl start emecworks-log-monitor.service
sudo systemctl status emecworks-log-monitor.service --no-pager
sudo systemctl list-timers emecworks-log-monitor.timer --no-pager
```

The service exits without sending a message when no new error signatures exist.
The timer scans a six-minute window every five minutes. Identical sanitized
signatures are suppressed for 60 minutes.

## Removal

```bash
sudo systemctl disable --now emecworks-log-monitor.timer
sudo rm /etc/systemd/system/emecworks-log-monitor.timer
sudo rm /etc/systemd/system/emecworks-log-monitor.service
sudo rm /usr/local/sbin/emecworks-log-monitor
sudo systemctl daemon-reload
```

Keep `/etc/emecworks/log-monitor.env` only if the webhook will be reinstalled.
Otherwise delete the credential in n8n and remove the file from the VPS.
