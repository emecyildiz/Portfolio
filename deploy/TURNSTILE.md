# Cloudflare Turnstile

Turnstile protects the public work-request form before a ticket or confirmation
email is created. It complements the honeypot and request limits; it does not
replace them.

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
