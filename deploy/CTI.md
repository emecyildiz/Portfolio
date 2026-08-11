# CTI platform

The CTI platform is a private, n8n-driven threat-news collection system. Its
data store is separate from both the n8n application database and the public
portfolio database.

## Security boundary

- `cti-db` has no published or exposed host port.
- The `emecworks-cti` Docker network is internal.
- n8n joins that network and connects as the fixed low-privilege role
  `cti_n8n`.
- The PostgreSQL owner password is never stored in n8n.
- A future private dashboard must use its own database role; it must not reuse
  either the owner or n8n credentials.
- Feed and article URLs must be checked against each source's `allowed_hosts`
  before any HTTP request is made.

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
