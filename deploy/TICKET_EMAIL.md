# Ticket confirmation email

The portfolio can send a short confirmation email after a visitor creates a
request on `/hire`. Delivery is disabled by default and uses the Resend HTTPS
API when enabled.

## Data flow

1. `HireController` writes the contact message and a `TicketEmailOutbox` row in
   the same database transaction.
2. The HTTP response succeeds without waiting for the email provider.
3. `TicketEmailDeliveryService` reads pending outbox rows in small batches.
4. `ResendTicketEmailSender` sends the confirmation with a stable idempotency
   key.
5. The outbox row records the provider ID, delivery time, attempt count, and a
   sanitized error code.

The email contains only the ticket number and tracking link. It deliberately
does not repeat the visitor's name, subject, or submitted message.

The tracking link uses a URL fragment:

```text
https://emecworks.com/hire#ticket=<ticket-number>
```

Browsers do not send the fragment to Cloudflare, Caddy, or ASP.NET in the first
HTTP request. JavaScript fills the tracking form locally and then removes the
fragment from the address bar.

## Provider and DNS setup

Use a dedicated sending subdomain so the existing root-domain email forwarding
records are not replaced:

```text
notify.emecworks.com
```

1. Add `notify.emecworks.com` as a sending domain in Resend.
2. Copy the SPF and DKIM records shown by Resend into Cloudflare DNS.
3. Keep mail-related records as **DNS only**. Do not proxy them.
4. Do not remove or replace the root `emecworks.com` MX/SPF records used by
   Namecheap email forwarding.
5. Wait until Resend reports the sending domain as verified.
6. Create a sending-only Resend API key.

Never paste the API key into chat, Git, a workflow, or a documentation file.

## Production configuration

Store these values in `/etc/emecworks/portfolio.env` on the VPS:

```dotenv
TICKET_EMAIL_ENABLED=true
TICKET_EMAIL_API_KEY=replace-with-the-secret-resend-key
TICKET_EMAIL_FROM_NAME=Emecworks
TICKET_EMAIL_FROM_ADDRESS=tickets@notify.emecworks.com
TICKET_EMAIL_REPLY_TO_ADDRESS=contact@emecworks.com
TICKET_EMAIL_PUBLIC_BASE_URL=https://emecworks.com
```

Confirm that `contact@emecworks.com` receives forwarded mail before using it as
the reply-to address.

Recreate only the web service after changing the environment:

```bash
cd /opt/emecworks/portfolio
sudo docker compose \
  --env-file /etc/emecworks/portfolio.env \
  -f docker-compose.prod.yml \
  up -d --build web
```

Do not print `/etc/emecworks/portfolio.env` or run Compose config commands that
would expand its secrets into terminal logs.

## Delivery and retry behavior

- Poll interval: 30 seconds by default.
- Batch size: 10 messages.
- Retry delays: 1 minute, 5 minutes, 15 minutes, 1 hour, 6 hours, then
  24 hours.
- Maximum attempts: 8 by default.
- HTTP 408, 429, 5xx, timeouts, network failures, and invalid provider
  responses are retried.
- Other provider 4xx responses fail permanently.
- A failed item can be queued again from the admin message detail page.
- The stable Resend idempotency key prevents duplicate provider submissions
  during uncertain retries.

When delivery is disabled, newly created outbox rows remain pending. Enabling
the service later processes those rows; they are not discarded.

## Safe production test

1. Submit one request using an email address controlled by the site owner.
2. Confirm the public success message appears immediately.
3. Wait up to one poll interval and confirm the email arrives.
4. Open the tracking link and confirm the ticket modal is populated.
5. In the admin message detail page, confirm the status is `Sent`.
6. Check logs without exposing configuration:

```bash
cd /opt/emecworks/portfolio
sudo docker compose \
  --env-file /etc/emecworks/portfolio.env \
  -f docker-compose.prod.yml \
  logs --since 10m web
```

Logs should contain only the numeric outbox ID and sanitized error code. They
must not contain the visitor email, message, ticket number, API key, or provider
response body.

## Common failure codes

- `http_401` or `http_403`: invalid key or insufficient API-key permission.
- `http_422`: sender/domain configuration rejected by Resend.
- `http_429`: provider rate limit; the service retries automatically.
- `network_error` or `request_timeout`: temporary connection problem.
- `missing_provider_id` or `invalid_provider_response`: unexpected provider
  response; the service retries.
- `unexpected_sender_error`: an unclassified sender failure; review application
  logs without exposing secrets.

After enabling the provider, update the public privacy text to identify the
email delivery provider and create a new encrypted recovery bundle.
