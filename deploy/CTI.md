# CTI platform

The CTI platform is a private, n8n-driven threat-news collection system. Its
data store is separate from both the n8n application database and the public
portfolio database.

## Security boundary

- `cti-db` has no published or exposed host port.
- The Compose service uses the unique name `cti-db`; a generic `db` service
  name would collide with n8n's own PostgreSQL DNS alias on the shared network.
- The `emecworks-cti` Docker network is internal.
- n8n joins that network and connects as the fixed low-privilege role
  `cti_n8n`.
- The PostgreSQL owner password is never stored in n8n.
- The private dashboard uses the dedicated `cti_dashboard` role and can select
  only from two restricted views. It cannot read base tables, call workflow
  functions, or write CTI data.
- Feed and article URLs must be checked against each source's `allowed_hosts`
  before any HTTP request is made.

## Source rollout

Sources are enabled deliberately, one at a time. The initial production source
is The Hacker News. The second reviewed source is CISA Cybersecurity
Advisories, using CISA's official all-advisories RSS feed and a selector scoped
to the advisory body. RSS endpoints, permitted article hostnames, and content
selectors are stored in `cti.sources`; n8n must load them from the database
instead of accepting an arbitrary feed or article URL from workflow input.

Every new source must be added through a reviewed migration, tested manually,
and observed for at least one collection cycle before another source is
enabled. This prevents a broken or unexpectedly large feed from exhausting AI
or Telegram quotas.

`deploy/n8n/workflows/cti-source-collection.json` is the first production
workflow. It runs every six hours, but its repository copy is intentionally
inactive. It loads enabled sources from PostgreSQL, reads their RSS feeds,
normalizes tracking parameters, enforces each source's HTTPS hostname
allowlist, and passes metadata to the parameterized `cti.ingest_feed_item()`
function. It does not fetch article pages, call Gemini, or send Telegram
messages.

## Analysis queue and quota gate

Every newly inserted article receives one `analysis_jobs` row through a
database trigger. `cti.claim_analysis_jobs(batch_size, daily_limit,
monthly_limit)` is the
only supported way for an n8n analysis workflow to reserve work. It enforces a
maximum batch size of five, caller-provided daily and monthly ceilings,
serialized claims, only one in-flight AI analysis, stale-lock recovery, and no
more than five attempts per article. The production workflow starts with one
article per execution, 20 AI requests per UTC day, and 400 per UTC month.

Successful calls must finish through `cti.complete_article_analysis()` so
token usage and the analyzed article are committed together. HTTP or parsing
failures, which do not consume AI quota, and actual AI/rate-limit failures are
distinguished by `cti.defer_article_analysis()`.

`deploy/n8n/workflows/cti-article-analysis.json` runs at most once every 30
minutes and claims one article. It repeats the HTTPS host allowlist check,
disables HTTP redirects, applies a 15-second fetch timeout, extracts only the
reviewed CSS selector, removes data URIs and links deterministically, and caps
the AI input at 16,000 characters. Gemini receives untrusted article text as
data, returns only classification metadata and a short English summary, and
its output is validated against the database enums and length limits before
storage. Every known failure path releases or defers the claimed job.

## Retention

`cti.apply_retention()` implements the bounded server-side retention policy:

- analyzed article text is cleared after 8 days, but only after it was included
  in a successfully delivered report and a 24-hour safety delay elapsed;
- URL and content fingerprints expire after 30 days;
- article metadata expires after 30 days;
- weekly and other generated reports expire after 8 weeks;
- delivery and AI usage records expire after 14 days;
- informational workflow logs expire after 7 days and all remaining workflow
  logs after 14 days.

The cleanup function returns JSON row counts. A scheduled n8n maintenance
workflow will call it once per night and send an alert if it fails.

## Initial installation order

1. Copy `deploy/cti-db.env.example` to `/etc/emecworks/cti-db.env` on the VPS.
2. Replace both placeholder passwords with different random values and set the
   file owner to `root:root` and its mode to `0600`.
3. Start `deploy/cti.compose.yml` before restarting n8n. The first database
   initialization creates the schema and the restricted `cti_n8n` role.
4. Restart n8n so it joins `emecworks-cti`.
5. Create an n8n PostgreSQL credential using host `cti-db`, port `5432`,
   database `cti`, user `cti_n8n`, and the `CTI_APP_PASSWORD` value.

The Emecworks backup and recovery scripts include `cti-database.dump` and
`/etc/emecworks/cti-db.env`. Deploy the database and the matching backup files
as one change so the scheduled backup never runs against a partial setup.

## Private dashboard

`deploy/cti-dashboard` is a separate ASP.NET Core service. It is not part of
the public portfolio application, does not publish a host port, and joins only
the internal `emecworks-cti` network. The Cloudflare Tunnel container also
joins this network so it can reach the panel; the panel itself receives no
general-purpose egress network.

The first version is deliberately read-only:

- analyzed articles from the current 30-day metadata window;
- title and English-summary search;
- category and severity filters;
- individual article records with original-source links;
- ready or sent report archives that have not expired.

The service sends `noindex`, private-cache, CSP, framing, referrer, and MIME
hardening headers. In production it rejects every non-health request that does
not contain Cloudflare Access's authenticated-user header. This application
check is defense in depth; Cloudflare Access must still protect the hostname.

### Dashboard installation

1. Generate a third random database password. Add it as
   `CTI_DASHBOARD_PASSWORD` to `/etc/emecworks/cti-db.env`.
2. Create `/etc/emecworks/cti-dashboard.env` containing only the same
   `CTI_DASHBOARD_PASSWORD` value plus `CTI_ACCESS_EMAIL`, which must match the
   single email allowed by Cloudflare Access. Set both files to `root:root`
   and `0600`.
3. Apply `deploy/cti/migrations/010-private-dashboard.sql` as the CTI owner,
   passing the password through the psql variable `cti_dashboard_password`.
4. Build and start `cti-dashboard` through `deploy/cti.compose.yml`.
5. Add the tunnel route `cti.emecworks.com` to
   `http://cti-dashboard:8080`.
6. Create a Cloudflare Access self-hosted application for
   `cti.emecworks.com`. Its Allow policy must contain only the owner's email;
   use One-time PIN as the required login method and keep the session short.
7. Verify that an unauthenticated private-browser request shows Cloudflare
   Access, an authorized request loads the dashboard, and
   `https://cti.emecworks.com/health/ready` is healthy through the tunnel.
8. Export a new encrypted recovery bundle because the new environment file is
   required for disaster recovery.

Do not add dashboard notes or administrative writes by expanding this role.
If a write feature is ever justified, it must use a separate narrowly scoped
function and a new reviewed migration.
