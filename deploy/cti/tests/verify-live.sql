\set ON_ERROR_STOP on

DO $$
BEGIN
    IF COALESCE((SELECT max(version) FROM cti.schema_versions), 0) < 10 THEN
        RAISE EXCEPTION 'CTI schema version 10 is not installed.';
    END IF;

    IF has_table_privilege('cti_n8n', 'cti.articles', 'DELETE') THEN
        RAISE EXCEPTION 'The n8n role unexpectedly has direct DELETE permission.';
    END IF;

    IF NOT has_function_privilege(
        'cti_n8n',
        'cti.ingest_feed_item(bigint,text,text,text,timestamp with time zone)',
        'EXECUTE'
    ) THEN
        RAISE EXCEPTION 'The n8n role cannot execute the ingestion function.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM cti.sources
        WHERE name = 'The Hacker News'
          AND enabled = true
          AND feed_url = 'https://feeds.feedburner.com/TheHackersNews'
          AND allowed_hosts @> ARRAY['thehackernews.com', 'www.thehackernews.com']
    ) THEN
        RAISE EXCEPTION 'The initial CTI source is missing or invalid.';
    END IF;

    IF NOT has_function_privilege(
        'cti_n8n',
        'cti.claim_analysis_jobs(integer,integer,integer)',
        'EXECUTE'
    ) THEN
        RAISE EXCEPTION 'The n8n role cannot claim analysis jobs.';
    END IF;

    IF has_table_privilege('cti_dashboard', 'cti.articles', 'SELECT') THEN
        RAISE EXCEPTION 'The dashboard role unexpectedly reads the articles table directly.';
    END IF;

    IF NOT has_table_privilege('cti_dashboard', 'cti.dashboard_articles', 'SELECT') OR
       NOT has_table_privilege('cti_dashboard', 'cti.dashboard_reports', 'SELECT') THEN
        RAISE EXCEPTION 'The dashboard role cannot read its restricted views.';
    END IF;

    IF has_table_privilege('cti_dashboard', 'cti.dashboard_articles', 'UPDATE') THEN
        RAISE EXCEPTION 'The dashboard role unexpectedly has write access.';
    END IF;
END;
$$;

SELECT
    max(version) AS schema_version,
    has_table_privilege('cti_n8n', 'cti.articles', 'DELETE') AS n8n_can_delete,
    has_function_privilege(
        'cti_n8n',
        'cti.ingest_feed_item(bigint,text,text,text,timestamp with time zone)',
        'EXECUTE'
    ) AS n8n_can_ingest,
    has_table_privilege(
        'cti_dashboard', 'cti.dashboard_articles', 'SELECT'
    ) AS dashboard_can_read,
    has_table_privilege(
        'cti_dashboard', 'cti.articles', 'SELECT'
    ) AS dashboard_can_read_base_table,
    (SELECT count(*) FROM cti.sources WHERE enabled = true) AS enabled_sources
FROM cti.schema_versions;
