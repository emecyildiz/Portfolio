\set ON_ERROR_STOP on

SELECT format('CREATE ROLE cti_n8n LOGIN PASSWORD %L', :'cti_app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cti_n8n')
\gexec

SELECT format('ALTER ROLE cti_n8n PASSWORD %L', :'cti_app_password')
\gexec

CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS cti AUTHORIZATION CURRENT_USER;

CREATE TABLE IF NOT EXISTS cti.schema_versions (
    version integer PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS cti.sources (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE,
    feed_url text NOT NULL UNIQUE CHECK (feed_url ~ '^https://'),
    allowed_hosts text[] NOT NULL CHECK (cardinality(allowed_hosts) > 0),
    content_selector text,
    trust_score smallint NOT NULL DEFAULT 50 CHECK (trust_score BETWEEN 0 AND 100),
    enabled boolean NOT NULL DEFAULT true,
    last_checked_at timestamptz,
    last_success_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS cti.articles (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    canonical_url text NOT NULL CHECK (canonical_url ~ '^https://'),
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 500),
    normalized_title text NOT NULL CHECK (char_length(normalized_title) BETWEEN 1 AND 500),
    title_hash char(64) NOT NULL CHECK (title_hash ~ '^[0-9a-f]{64}$'),
    content_hash char(64) CHECK (content_hash ~ '^[0-9a-f]{64}$'),
    cleaned_content text CHECK (cleaned_content IS NULL OR char_length(cleaned_content) <= 20000),
    category text CHECK (category IN (
        'malware',
        'vulnerability',
        'data_breach',
        'threat_intelligence',
        'other'
    )),
    severity text CHECK (severity IN ('critical', 'high', 'medium', 'low', 'unknown')),
    summary_tr text CHECK (summary_tr IS NULL OR char_length(summary_tr) <= 4000),
    analysis_confidence numeric(4,3) CHECK (
        analysis_confidence IS NULL OR analysis_confidence BETWEEN 0 AND 1
    ),
    published_at timestamptz NOT NULL,
    fetched_at timestamptz NOT NULL DEFAULT now(),
    analyzed_at timestamptz,
    reported_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (canonical_url)
);

CREATE INDEX IF NOT EXISTS ix_articles_published_at
    ON cti.articles (published_at DESC);
CREATE INDEX IF NOT EXISTS ix_articles_category_published_at
    ON cti.articles (category, published_at DESC);
CREATE INDEX IF NOT EXISTS ix_articles_normalized_title_trgm
    ON cti.articles USING gin (normalized_title gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_articles_content_hash
    ON cti.articles (content_hash) WHERE content_hash IS NOT NULL;

CREATE TABLE IF NOT EXISTS cti.article_occurrences (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    article_id bigint NOT NULL REFERENCES cti.articles(id) ON DELETE CASCADE,
    source_id bigint NOT NULL REFERENCES cti.sources(id) ON DELETE RESTRICT,
    original_url text NOT NULL CHECK (
        original_url ~ '^https://' AND char_length(original_url) <= 4000
    ),
    source_title text NOT NULL CHECK (char_length(source_title) BETWEEN 1 AND 500),
    feed_guid text CHECK (feed_guid IS NULL OR char_length(feed_guid) <= 1000),
    source_published_at timestamptz NOT NULL,
    discovered_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, original_url)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_article_occurrences_source_guid
    ON cti.article_occurrences (source_id, feed_guid)
    WHERE feed_guid IS NOT NULL AND feed_guid <> '';

CREATE TABLE IF NOT EXISTS cti.fingerprints (
    kind text NOT NULL CHECK (kind IN ('url', 'content')),
    fingerprint char(64) NOT NULL CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
    article_id bigint NOT NULL REFERENCES cti.articles(id) ON DELETE CASCADE,
    first_seen_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL DEFAULT (now() + interval '30 days'),
    PRIMARY KEY (kind, fingerprint)
);

CREATE INDEX IF NOT EXISTS ix_fingerprints_expires_at
    ON cti.fingerprints (expires_at);

CREATE TABLE IF NOT EXISTS cti.analysis_jobs (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    article_id bigint NOT NULL UNIQUE REFERENCES cti.articles(id) ON DELETE CASCADE,
    status text NOT NULL DEFAULT 'pending' CHECK (
        status IN ('pending', 'processing', 'completed', 'deferred', 'failed')
    ),
    priority smallint NOT NULL DEFAULT 50 CHECK (priority BETWEEN 0 AND 100),
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 20),
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    locked_at timestamptz,
    last_error_code text CHECK (last_error_code IS NULL OR char_length(last_error_code) <= 100),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_analysis_jobs_ready
    ON cti.analysis_jobs (priority DESC, next_attempt_at, id)
    WHERE status IN ('pending', 'deferred');

CREATE TABLE IF NOT EXISTS cti.ai_usage (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    article_id bigint REFERENCES cti.articles(id) ON DELETE SET NULL,
    purpose text NOT NULL CHECK (purpose IN ('article_analysis', 'weekly_report', 'manual_summary')),
    model text NOT NULL CHECK (char_length(model) BETWEEN 1 AND 200),
    request_status text NOT NULL CHECK (request_status IN ('success', 'rate_limited', 'failed')),
    prompt_tokens integer NOT NULL DEFAULT 0 CHECK (prompt_tokens >= 0),
    output_tokens integer NOT NULL DEFAULT 0 CHECK (output_tokens >= 0),
    total_tokens integer NOT NULL DEFAULT 0 CHECK (
        total_tokens >= prompt_tokens + output_tokens
    ),
    requested_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_ai_usage_requested_at
    ON cti.ai_usage (requested_at DESC);

CREATE TABLE IF NOT EXISTS cti.reports (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    report_type text NOT NULL CHECK (report_type IN ('daily', 'weekly', 'on_demand')),
    window_start timestamptz NOT NULL,
    window_end timestamptz NOT NULL CHECK (window_end > window_start),
    category text,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'ready', 'sent', 'failed')),
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 500),
    content text NOT NULL CHECK (char_length(content) BETWEEN 1 AND 100000),
    generated_at timestamptz NOT NULL DEFAULT now(),
    sent_at timestamptz,
    expires_at timestamptz NOT NULL DEFAULT (now() + interval '8 weeks'),
    CHECK ((status = 'sent' AND sent_at IS NOT NULL) OR status <> 'sent')
);

CREATE INDEX IF NOT EXISTS ix_reports_expires_at
    ON cti.reports (expires_at);

CREATE TABLE IF NOT EXISTS cti.report_articles (
    report_id bigint NOT NULL REFERENCES cti.reports(id) ON DELETE CASCADE,
    article_id bigint REFERENCES cti.articles(id) ON DELETE SET NULL,
    title_snapshot text NOT NULL CHECK (char_length(title_snapshot) BETWEEN 1 AND 500),
    url_snapshot text NOT NULL CHECK (url_snapshot ~ '^https://'),
    source_snapshot text NOT NULL CHECK (char_length(source_snapshot) BETWEEN 1 AND 200),
    PRIMARY KEY (report_id, url_snapshot)
);

CREATE TABLE IF NOT EXISTS cti.delivery_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    report_id bigint REFERENCES cti.reports(id) ON DELETE SET NULL,
    channel text NOT NULL CHECK (channel IN ('telegram', 'panel')),
    status text NOT NULL CHECK (status IN ('queued', 'sent', 'failed')),
    external_message_id text,
    error_code text CHECK (error_code IS NULL OR char_length(error_code) <= 100),
    attempted_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_delivery_log_attempted_at
    ON cti.delivery_log (attempted_at DESC);

CREATE TABLE IF NOT EXISTS cti.workflow_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    workflow_name text NOT NULL CHECK (char_length(workflow_name) BETWEEN 1 AND 200),
    level text NOT NULL CHECK (level IN ('info', 'warning', 'error')),
    event_code text NOT NULL CHECK (char_length(event_code) BETWEEN 1 AND 100),
    details jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_workflow_log_occurred_at
    ON cti.workflow_log (occurred_at DESC);

CREATE OR REPLACE FUNCTION cti.apply_retention()
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
    cleaned_articles integer;
    deleted_fingerprints integer;
    deleted_reports integer;
    deleted_deliveries integer;
    deleted_ai_usage integer;
    deleted_info_logs integer;
    deleted_old_logs integer;
    deleted_articles integer;
BEGIN
    UPDATE cti.articles
    SET cleaned_content = NULL,
        updated_at = reference_time
    WHERE cleaned_content IS NOT NULL
      AND fetched_at < reference_time - interval '8 days'
      AND reported_at IS NOT NULL
      AND reported_at < reference_time - interval '24 hours';
    GET DIAGNOSTICS cleaned_articles = ROW_COUNT;

    DELETE FROM cti.fingerprints
    WHERE expires_at <= reference_time;
    GET DIAGNOSTICS deleted_fingerprints = ROW_COUNT;

    DELETE FROM cti.reports
    WHERE expires_at <= reference_time;
    GET DIAGNOSTICS deleted_reports = ROW_COUNT;

    DELETE FROM cti.delivery_log
    WHERE attempted_at < reference_time - interval '14 days';
    GET DIAGNOSTICS deleted_deliveries = ROW_COUNT;

    DELETE FROM cti.ai_usage
    WHERE requested_at < reference_time - interval '14 days';
    GET DIAGNOSTICS deleted_ai_usage = ROW_COUNT;

    DELETE FROM cti.workflow_log
    WHERE level = 'info'
      AND occurred_at < reference_time - interval '7 days';
    GET DIAGNOSTICS deleted_info_logs = ROW_COUNT;

    DELETE FROM cti.workflow_log
    WHERE occurred_at < reference_time - interval '14 days';
    GET DIAGNOSTICS deleted_old_logs = ROW_COUNT;

    DELETE FROM cti.articles
    WHERE fetched_at < reference_time - interval '30 days';
    GET DIAGNOSTICS deleted_articles = ROW_COUNT;

    RETURN jsonb_build_object(
        'cleaned_articles', cleaned_articles,
        'deleted_fingerprints', deleted_fingerprints,
        'deleted_reports', deleted_reports,
        'deleted_deliveries', deleted_deliveries,
        'deleted_ai_usage', deleted_ai_usage,
        'deleted_info_logs', deleted_info_logs,
        'deleted_old_logs', deleted_old_logs,
        'deleted_articles', deleted_articles
    );
END;
$$;

CREATE OR REPLACE FUNCTION cti.ingest_feed_item(
    source_id_value bigint,
    original_url_value text,
    title_value text,
    feed_guid_value text,
    published_at_value timestamptz
)
RETURNS TABLE(article_id bigint, is_new boolean, occurrence_added boolean)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    source_record cti.sources%ROWTYPE;
    canonical_url_value text;
    hostname_value text;
    port_value text;
    normalized_title_value text;
    normalized_feed_guid_value text;
    title_hash_value char(64);
    url_hash_value char(64);
    existing_article_id bigint;
    inserted_occurrences integer;
    reference_time timestamptz := clock_timestamp();
BEGIN
    SELECT *
    INTO source_record
    FROM cti.sources AS source
    WHERE source.id = source_id_value
      AND source.enabled = true;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Unknown or disabled CTI source.' USING ERRCODE = '22023';
    END IF;

    canonical_url_value := split_part(trim(original_url_value), '#', 1);
    hostname_value := lower(substring(canonical_url_value FROM '^https://([^/:?#]+)'));
    port_value := substring(canonical_url_value FROM '^https://[^/:?#@]+:([0-9]+)');

    IF canonical_url_value = ''
       OR char_length(canonical_url_value) > 4000
       OR hostname_value IS NULL
       OR canonical_url_value ~ '^https://[^/?#]*@'
       OR (port_value IS NOT NULL AND port_value <> '443')
       OR NOT (hostname_value = ANY(source_record.allowed_hosts)) THEN
        RAISE EXCEPTION 'Article URL is not allowed for this CTI source.'
            USING ERRCODE = '22023';
    END IF;

    IF published_at_value IS NULL THEN
        RAISE EXCEPTION 'Article publication time is required.' USING ERRCODE = '22023';
    END IF;

    IF published_at_value < reference_time - interval '30 hours'
       OR published_at_value > reference_time + interval '15 minutes' THEN
        RETURN;
    END IF;

    IF title_value IS NULL OR char_length(trim(title_value)) NOT BETWEEN 1 AND 500 THEN
        RAISE EXCEPTION 'Article title length is invalid.' USING ERRCODE = '22023';
    END IF;

    normalized_feed_guid_value := NULLIF(trim(feed_guid_value), '');

    IF normalized_feed_guid_value IS NOT NULL
       AND char_length(normalized_feed_guid_value) > 1000 THEN
        RAISE EXCEPTION 'Feed GUID length is invalid.' USING ERRCODE = '22023';
    END IF;

    normalized_title_value := trim(regexp_replace(
        lower(title_value),
        '[^[:alnum:]]+',
        ' ',
        'g'
    ));

    IF normalized_title_value = '' THEN
        RAISE EXCEPTION 'Article title becomes empty after normalization.'
            USING ERRCODE = '22023';
    END IF;

    title_hash_value := encode(public.digest(normalized_title_value, 'sha256'), 'hex');
    url_hash_value := encode(public.digest(canonical_url_value, 'sha256'), 'hex');

    PERFORM pg_advisory_xact_lock(hashtextextended('url:' || url_hash_value, 0));
    PERFORM pg_advisory_xact_lock(hashtextextended('title:' || title_hash_value, 0));
    IF normalized_feed_guid_value IS NOT NULL THEN
        PERFORM pg_advisory_xact_lock(hashtextextended(
            'guid:' || source_id_value::text || ':' || normalized_feed_guid_value,
            0
        ));
    END IF;

    SELECT occurrence.article_id
    INTO existing_article_id
    FROM cti.article_occurrences AS occurrence
    WHERE occurrence.source_id = source_id_value
      AND (
          occurrence.original_url = canonical_url_value
          OR (
              normalized_feed_guid_value IS NOT NULL
              AND occurrence.feed_guid = normalized_feed_guid_value
          )
      )
    ORDER BY occurrence.id
    LIMIT 1;

    IF existing_article_id IS NULL THEN
        SELECT fingerprint.article_id
        INTO existing_article_id
        FROM cti.fingerprints AS fingerprint
        WHERE fingerprint.kind = 'url'
          AND fingerprint.fingerprint = url_hash_value
          AND fingerprint.expires_at > reference_time;
    END IF;

    IF existing_article_id IS NULL THEN
        SELECT article.id
        INTO existing_article_id
        FROM cti.articles AS article
        WHERE article.canonical_url = canonical_url_value;
    END IF;

    IF existing_article_id IS NULL THEN
        SELECT article.id
        INTO existing_article_id
        FROM cti.articles AS article
        WHERE article.title_hash = title_hash_value
          AND article.published_at BETWEEN
              published_at_value - interval '2 days'
              AND published_at_value + interval '2 days'
        ORDER BY article.fetched_at
        LIMIT 1;
    END IF;

    IF existing_article_id IS NULL THEN
        INSERT INTO cti.articles (
            canonical_url,
            title,
            normalized_title,
            title_hash,
            published_at
        )
        VALUES (
            canonical_url_value,
            trim(title_value),
            normalized_title_value,
            title_hash_value,
            published_at_value
        )
        ON CONFLICT (canonical_url) DO UPDATE
        SET updated_at = reference_time
        RETURNING id INTO existing_article_id;

        is_new := true;
    ELSE
        is_new := false;
    END IF;

    INSERT INTO cti.fingerprints (kind, fingerprint, article_id, expires_at)
    VALUES ('url', url_hash_value, existing_article_id, reference_time + interval '30 days')
    ON CONFLICT (kind, fingerprint) DO UPDATE
    SET article_id = EXCLUDED.article_id,
        expires_at = GREATEST(cti.fingerprints.expires_at, EXCLUDED.expires_at);

    INSERT INTO cti.article_occurrences (
        article_id,
        source_id,
        original_url,
        source_title,
        feed_guid,
        source_published_at
    )
    VALUES (
        existing_article_id,
        source_id_value,
        canonical_url_value,
        trim(title_value),
        normalized_feed_guid_value,
        published_at_value
    )
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS inserted_occurrences = ROW_COUNT;

    article_id := existing_article_id;
    occurrence_added := inserted_occurrences = 1;
    RETURN NEXT;
END;
$$;

REVOKE ALL ON SCHEMA cti FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA cti FROM PUBLIC;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA cti FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.apply_retention() FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.ingest_feed_item(bigint, text, text, text, timestamptz)
    FROM PUBLIC;

GRANT CONNECT ON DATABASE cti TO cti_n8n;
GRANT USAGE ON SCHEMA cti TO cti_n8n;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA cti TO cti_n8n;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA cti TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.apply_retention() TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.ingest_feed_item(bigint, text, text, text, timestamptz)
    TO cti_n8n;

ALTER DEFAULT PRIVILEGES IN SCHEMA cti
    GRANT SELECT, INSERT, UPDATE ON TABLES TO cti_n8n;
ALTER DEFAULT PRIVILEGES IN SCHEMA cti
    GRANT USAGE, SELECT ON SEQUENCES TO cti_n8n;

INSERT INTO cti.schema_versions (version)
VALUES (1)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (2)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.sources (
    name,
    feed_url,
    allowed_hosts,
    content_selector,
    trust_score,
    enabled
)
VALUES (
    'The Hacker News',
    'https://feeds.feedburner.com/TheHackersNews',
    ARRAY['thehackernews.com', 'www.thehackernews.com'],
    '#articlebody',
    80,
    true
)
ON CONFLICT (name) DO UPDATE
SET feed_url = EXCLUDED.feed_url,
    allowed_hosts = EXCLUDED.allowed_hosts,
    content_selector = EXCLUDED.content_selector,
    trust_score = EXCLUDED.trust_score,
    updated_at = now();

INSERT INTO cti.schema_versions (version)
VALUES (3)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (4)
ON CONFLICT (version) DO NOTHING;
