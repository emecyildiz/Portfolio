# Cloudflare Turnstile

Turnstile protects the public work-request form before a ticket or confirmation
email is created. It complements the honeypot and request limits; it does not
replace them.

Turnstile is mandatory outside the `Development` environment, including
Production and Staging. Startup rejects disabled verification or missing widget
configuration before the application starts accepting requests. Production
Compose defaults `TURNSTILE_ENABLED` to `true`; explicitly setting it to `false`
does not bypass the startup check. Configure the widget before deployment.

## Production widget

Create a Managed Turnstile widget in the Cloudflare dashboard:

- Widget name: `Emecworks contact form`
- Hostname: `emecworks.com`
- Widget mode: Managed

Do not add `localhost` or `127.0.0.1` to the production widget.

Store the public site key and private secret in
`/etc/emecworks/portfolio.env`:

```dotenv
TURNSTILE_ENABLED=true
TURNSTILE_SITE_KEY=replace-with-the-production-site-key
TURNSTILE_SECRET_KEY=replace-with-the-production-secret-key
TURNSTILE_EXPECTED_HOSTNAME=emecworks.com
TURNSTILE_EXPECTED_ACTION=contact
```

The secret must never be committed, printed in logs, or exposed to client-side
code. The site key is intentionally public.

Recreate the web service after updating the environment:

```bash
sudo docker compose \
  --env-file /etc/emecworks/portfolio.env \
  -f docker-compose.prod.yml \
  up -d --force-recreate web
```

## Enforcement

The browser widget submits `cf-turnstile-response`. The server sends it to
Cloudflare Siteverify and accepts the request only when:

- validation succeeds;
- the returned hostname is `emecworks.com`;
- the returned action is `contact`.

Tokens expire after five minutes and can be used only once. Validation fails
closed: if Siteverify cannot be reached, no ticket or email job is created.

## Local testing

Leave Turnstile disabled for normal local development. For an explicit
integration test, use Cloudflare's official always-pass test site key and
secret only in the local `.env`; never use the test keys in production.

The tracked `.env.example` and `docker-compose.yml` are local Development
configuration, not production-ready settings. Use the production environment
file and `docker-compose.prod.yml` for public deployment.

## Availability limits

Successful verification is required before any public contact or confirmation
email job is stored. This prevents proof-free distributed submissions from
filling the shared ticket budget. The existing 60-ticket rolling-day, per-IP and
per-email limits remain in place; Turnstile is an abuse barrier, not sender
identity or email ownership verification. Enough senders who pass verification
can still fill the shared budget. A verified overflow channel/reserved-capacity
policy is a separate product decision; quota count-and-save concurrency is also
not made atomic by this configuration guard.
