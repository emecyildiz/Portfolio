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
- A future private dashboard must use its own database role; it must not reuse
  either the owner or n8n credentials.
- Feed and article URLs must be checked against each source's `allowed_hosts`
  before any HTTP request is made.

## Source rollout

Sources are enabled deliberately, one at a time. The initial production source
is The Hacker News. Its RSS endpoint and permitted article hostnames are stored
in `cti.sources`; n8n must load them from the database instead of accepting an
arbitrary feed or article URL from workflow input.

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
