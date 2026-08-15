\set ON_ERROR_STOP on

SELECT format('CREATE ROLE cti_n8n LOGIN PASSWORD %L', :'cti_app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cti_n8n')
\gexec

SELECT format('ALTER ROLE cti_n8n PASSWORD %L', :'cti_app_password')
\gexec

SELECT format(
    'CREATE ROLE cti_dashboard LOGIN NOINHERIT PASSWORD %L',
    :'cti_dashboard_password'
)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cti_dashboard')
\gexec

SELECT format('ALTER ROLE cti_dashboard PASSWORD %L', :'cti_dashboard_password')
\gexec

ALTER ROLE cti_dashboard SET default_transaction_read_only = on;
ALTER ROLE cti_dashboard SET statement_timeout = '10s';
ALTER ROLE cti_dashboard SET idle_in_transaction_session_timeout = '10s';

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
    last_error_at timestamptz,
    last_error_code text CHECK (
        last_error_code IS NULL OR last_error_code IN (
            'feed_read_failed',
            'feed_item_invalid',
            'feed_item_store_failed'
        )
    ),
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

CREATE INDEX IF NOT EXISTS ix_articles_simple_search
    ON cti.articles USING gin (
        to_tsvector('simple', title || ' ' || coalesce(summary_tr, ''))
    );

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

CREATE OR REPLACE FUNCTION cti.enqueue_article_analysis()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
BEGIN
    INSERT INTO cti.analysis_jobs (article_id)
    VALUES (NEW.id)
    ON CONFLICT (article_id) DO NOTHING;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_enqueue_article_analysis ON cti.articles;
CREATE TRIGGER trg_enqueue_article_analysis
AFTER INSERT ON cti.articles
FOR EACH ROW
EXECUTE FUNCTION cti.enqueue_article_analysis();

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

CREATE OR REPLACE FUNCTION cti.claim_analysis_jobs(
    batch_size_value integer DEFAULT 1,
    daily_limit_value integer DEFAULT 20,
    monthly_limit_value integer DEFAULT 400
)
RETURNS TABLE(
    article_id bigint,
    canonical_url text,
    title text,
    source_name text,
    content_selector text,
    allowed_hosts text[],
    attempt smallint
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
    utc_day_start timestamptz;
    utc_month_start timestamptz;
    used_daily_slots integer;
    used_monthly_slots integer;
    active_reservations integer;
    available_slots integer;
BEGIN
    IF batch_size_value NOT BETWEEN 1 AND 5 THEN
        RAISE EXCEPTION 'Analysis batch size must be between 1 and 5.'
            USING ERRCODE = '22023';
    END IF;

    IF daily_limit_value NOT BETWEEN 1 AND 100 THEN
        RAISE EXCEPTION 'Analysis daily limit must be between 1 and 100.'
            USING ERRCODE = '22023';
    END IF;

    IF monthly_limit_value NOT BETWEEN daily_limit_value AND 5000 THEN
        RAISE EXCEPTION 'Analysis monthly limit must be between the daily limit and 5000.'
            USING ERRCODE = '22023';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtextextended('cti.claim_analysis_jobs', 0));

    utc_day_start := date_trunc('day', reference_time AT TIME ZONE 'UTC')
        AT TIME ZONE 'UTC';
    utc_month_start := date_trunc('month', reference_time AT TIME ZONE 'UTC')
        AT TIME ZONE 'UTC';

    UPDATE cti.analysis_jobs AS job
    SET status = CASE WHEN job.attempts >= 5 THEN 'failed' ELSE 'deferred' END,
        next_attempt_at = CASE
            WHEN job.attempts >= 5 THEN job.next_attempt_at
            ELSE reference_time + interval '15 minutes'
        END,
        locked_at = NULL,
        last_error_code = 'stale_lock_recovered',
        updated_at = reference_time
    WHERE job.status = 'processing'
      AND job.locked_at < reference_time - interval '30 minutes';

    SELECT count(*)
    INTO used_daily_slots
    FROM cti.ai_usage AS usage
    WHERE usage.requested_at >= utc_day_start;

    SELECT count(*)
    INTO used_monthly_slots
    FROM cti.ai_usage AS usage
    WHERE usage.requested_at >= utc_month_start;

    SELECT count(*)
    INTO active_reservations
    FROM cti.analysis_jobs AS job
    WHERE job.status = 'processing';

    IF active_reservations > 0 THEN
        RETURN;
    END IF;

    available_slots := LEAST(
        batch_size_value,
        GREATEST(daily_limit_value - used_daily_slots, 0),
        GREATEST(monthly_limit_value - used_monthly_slots, 0)
    );

    IF available_slots = 0 THEN
        RETURN;
    END IF;

    RETURN QUERY
    WITH candidates AS MATERIALIZED (
        SELECT job.id
        FROM cti.analysis_jobs AS job
        JOIN cti.articles AS article ON article.id = job.article_id
        WHERE job.status IN ('pending', 'deferred')
          AND job.next_attempt_at <= reference_time
          AND job.attempts < 5
          AND article.analyzed_at IS NULL
        ORDER BY job.priority DESC, article.published_at DESC, job.id
        FOR UPDATE OF job SKIP LOCKED
        LIMIT available_slots
    ), claimed AS (
        UPDATE cti.analysis_jobs AS job
        SET status = 'processing',
            attempts = job.attempts + 1,
            locked_at = reference_time,
            last_error_code = NULL,
            updated_at = reference_time
        FROM candidates
        WHERE job.id = candidates.id
        RETURNING job.article_id, job.attempts
    )
    SELECT
        article.id,
        article.canonical_url,
        article.title,
        selected_source.name,
        selected_source.content_selector,
        selected_source.allowed_hosts,
        claimed.attempts
    FROM claimed
    JOIN cti.articles AS article ON article.id = claimed.article_id
    JOIN LATERAL (
        SELECT source.name, source.content_selector, source.allowed_hosts
        FROM cti.article_occurrences AS occurrence
        JOIN cti.sources AS source ON source.id = occurrence.source_id
        WHERE occurrence.article_id = article.id
          AND source.enabled = true
        ORDER BY source.trust_score DESC, occurrence.discovered_at, occurrence.id
        LIMIT 1
    ) AS selected_source ON true
    ORDER BY article.published_at DESC, article.id;
END;
$$;

CREATE OR REPLACE FUNCTION cti.complete_article_analysis(
    article_id_value bigint,
    category_value text,
    severity_value text,
    summary_tr_value text,
    cleaned_content_value text,
    confidence_value numeric,
    model_value text,
    prompt_tokens_value integer DEFAULT 0,
    output_tokens_value integer DEFAULT 0
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
BEGIN
    IF model_value IS NULL OR char_length(trim(model_value)) NOT BETWEEN 1 AND 200 THEN
        RAISE EXCEPTION 'AI model name is invalid.' USING ERRCODE = '22023';
    END IF;

    IF prompt_tokens_value < 0 OR output_tokens_value < 0 THEN
        RAISE EXCEPTION 'Token counts cannot be negative.' USING ERRCODE = '22023';
    END IF;

    PERFORM 1
    FROM cti.analysis_jobs AS job
    WHERE job.article_id = article_id_value
      AND job.status = 'processing'
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'The article does not have a claimed analysis job.'
            USING ERRCODE = '55000';
    END IF;

    UPDATE cti.articles
    SET category = category_value,
        severity = severity_value,
        summary_tr = summary_tr_value,
        cleaned_content = cleaned_content_value,
        analysis_confidence = confidence_value,
        analyzed_at = reference_time,
        updated_at = reference_time
    WHERE id = article_id_value;

    UPDATE cti.analysis_jobs
    SET status = 'completed',
        locked_at = NULL,
        last_error_code = NULL,
        updated_at = reference_time
    WHERE article_id = article_id_value;

    INSERT INTO cti.ai_usage (
        article_id,
        purpose,
        model,
        request_status,
        prompt_tokens,
        output_tokens,
        total_tokens,
        requested_at
    )
    VALUES (
        article_id_value,
        'article_analysis',
        trim(model_value),
        'success',
        prompt_tokens_value,
        output_tokens_value,
        prompt_tokens_value + output_tokens_value,
        reference_time
    );
END;
$$;

CREATE OR REPLACE FUNCTION cti.defer_article_analysis(
    article_id_value bigint,
    error_code_value text,
    ai_was_called_value boolean DEFAULT false,
    rate_limited_value boolean DEFAULT false,
    model_value text DEFAULT NULL
)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
    attempt_count smallint;
    next_status text;
    retry_delay interval;
BEGIN
    IF error_code_value IS NULL OR char_length(trim(error_code_value)) NOT BETWEEN 1 AND 100 THEN
        RAISE EXCEPTION 'Analysis error code is invalid.' USING ERRCODE = '22023';
    END IF;

    SELECT job.attempts
    INTO attempt_count
    FROM cti.analysis_jobs AS job
    WHERE job.article_id = article_id_value
      AND job.status = 'processing'
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'The article does not have a claimed analysis job.'
            USING ERRCODE = '55000';
    END IF;

    next_status := CASE WHEN attempt_count >= 5 THEN 'failed' ELSE 'deferred' END;
    retry_delay := CASE
        WHEN rate_limited_value THEN interval '2 hours'
        WHEN attempt_count >= 4 THEN interval '4 hours'
        WHEN attempt_count = 3 THEN interval '2 hours'
        WHEN attempt_count = 2 THEN interval '30 minutes'
        ELSE interval '15 minutes'
    END;

    UPDATE cti.analysis_jobs
    SET status = next_status,
        next_attempt_at = CASE
            WHEN next_status = 'failed' THEN next_attempt_at
            ELSE reference_time + retry_delay
        END,
        locked_at = NULL,
        last_error_code = trim(error_code_value),
        updated_at = reference_time
    WHERE article_id = article_id_value;

    IF ai_was_called_value THEN
        IF model_value IS NULL OR char_length(trim(model_value)) NOT BETWEEN 1 AND 200 THEN
            RAISE EXCEPTION 'AI model name is required for a recorded request.'
                USING ERRCODE = '22023';
        END IF;

        INSERT INTO cti.ai_usage (
            article_id,
            purpose,
            model,
            request_status,
            requested_at
        )
        VALUES (
            article_id_value,
            'article_analysis',
            trim(model_value),
            CASE WHEN rate_limited_value THEN 'rate_limited' ELSE 'failed' END,
            reference_time
        );
    END IF;

    RETURN next_status;
END;
$$;

INSERT INTO cti.analysis_jobs (article_id)
SELECT article.id
FROM cti.articles AS article
WHERE article.analyzed_at IS NULL
ON CONFLICT (article_id) DO NOTHING;

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
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 3),
    locked_at timestamptz,
    last_error_code text CHECK (
        last_error_code IS NULL OR char_length(last_error_code) <= 100
    ),
    CHECK ((status = 'sent' AND sent_at IS NOT NULL) OR status <> 'sent')
);

CREATE INDEX IF NOT EXISTS ix_reports_expires_at
    ON cti.reports (expires_at);

CREATE UNIQUE INDEX IF NOT EXISTS ux_reports_weekly_window
    ON cti.reports (window_start, window_end)
    WHERE report_type = 'weekly' AND category IS NULL;

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
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 3),
    locked_at timestamptz,
    attempted_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE cti.delivery_log
    ADD COLUMN IF NOT EXISTS attempts smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS locked_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'cti.delivery_log'::regclass
          AND conname = 'delivery_log_attempts_check'
    ) THEN
        ALTER TABLE cti.delivery_log
            ADD CONSTRAINT delivery_log_attempts_check
            CHECK (attempts BETWEEN 0 AND 3);
    END IF;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_log_report_channel
    ON cti.delivery_log (report_id, channel)
    WHERE report_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_delivery_log_attempted_at
    ON cti.delivery_log (attempted_at DESC);

CREATE OR REPLACE FUNCTION cti.claim_weekly_telegram_delivery()
RETURNS TABLE (
    delivery_id bigint,
    report_id bigint,
    report_title text,
    report_content text
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = cti, pg_temp
AS $$
DECLARE
    reference_time timestamptz := now();
    selected_report_id bigint;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('cti.claim_weekly_telegram_delivery', 0));

    UPDATE cti.delivery_log AS delivery
    SET status = 'failed',
        error_code = 'ambiguous:stale_lock',
        locked_at = NULL,
        attempted_at = reference_time
    WHERE delivery.channel = 'telegram'
      AND delivery.status = 'queued'
      AND (delivery.locked_at IS NULL OR delivery.locked_at < reference_time - interval '15 minutes');

    SELECT report.id
    INTO selected_report_id
    FROM cti.reports AS report
    LEFT JOIN cti.delivery_log AS delivery
      ON delivery.report_id = report.id
     AND delivery.channel = 'telegram'
    WHERE report.report_type = 'weekly'
      AND report.status = 'ready'
      AND (
          delivery.id IS NULL
          OR (
              delivery.status = 'failed'
              AND delivery.attempts < 3
              AND delivery.error_code LIKE 'retry_safe:%'
          )
      )
    ORDER BY report.window_end, report.id
    LIMIT 1
    FOR UPDATE OF report SKIP LOCKED;

    IF selected_report_id IS NULL THEN
        RETURN;
    END IF;

    UPDATE cti.delivery_log AS delivery
    SET
        status = 'queued',
        attempts = delivery.attempts + 1,
        locked_at = reference_time,
        attempted_at = reference_time,
        error_code = NULL,
        external_message_id = NULL
    WHERE delivery.report_id = selected_report_id
      AND delivery.channel = 'telegram';

    IF NOT FOUND THEN
        INSERT INTO cti.delivery_log (
            report_id, channel, status, attempts, locked_at, attempted_at
        )
        VALUES (
            selected_report_id, 'telegram', 'queued', 1, reference_time, reference_time
        );
    END IF;

    RETURN QUERY
    SELECT delivery.id, report.id, report.title, report.content
    FROM cti.delivery_log AS delivery
    JOIN cti.reports AS report ON report.id = delivery.report_id
    WHERE report.id = selected_report_id
      AND delivery.channel = 'telegram'
      AND delivery.status = 'queued';
END;
$$;

CREATE OR REPLACE FUNCTION cti.complete_weekly_telegram_delivery(
    delivery_id_value bigint,
    external_message_ids_value text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = cti, pg_temp
AS $$
DECLARE
    selected_report_id bigint;
    reference_time timestamptz := now();
    updated_reports integer;
BEGIN
    IF external_message_ids_value IS NULL
       OR char_length(trim(external_message_ids_value)) NOT BETWEEN 1 AND 1000 THEN
        RAISE EXCEPTION 'Telegram delivery receipt is invalid.' USING ERRCODE = '22023';
    END IF;

    UPDATE cti.delivery_log AS delivery
    SET status = 'sent',
        external_message_id = trim(external_message_ids_value),
        error_code = NULL,
        locked_at = NULL,
        attempted_at = reference_time
    WHERE delivery.id = delivery_id_value
      AND delivery.channel = 'telegram'
      AND delivery.status = 'queued'
    RETURNING delivery.report_id INTO selected_report_id;

    IF selected_report_id IS NULL THEN
        RAISE EXCEPTION 'Telegram delivery reservation is not active.' USING ERRCODE = '55000';
    END IF;

    UPDATE cti.reports AS report
    SET status = 'sent', sent_at = reference_time
    WHERE report.id = selected_report_id
      AND report.report_type = 'weekly'
      AND report.status = 'ready';
    GET DIAGNOSTICS updated_reports = ROW_COUNT;
    IF updated_reports <> 1 THEN
        RAISE EXCEPTION 'Weekly report is not ready for Telegram completion.' USING ERRCODE = '55000';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION cti.fail_weekly_telegram_delivery(
    delivery_id_value bigint,
    error_code_value text,
    retry_safe_value boolean DEFAULT false
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = cti, pg_temp
AS $$
DECLARE
    normalized_error text;
BEGIN
    normalized_error := regexp_replace(lower(coalesce(error_code_value, 'unknown')), '[^a-z0-9_.-]+', '_', 'g');
    normalized_error := left(trim(both '_' FROM normalized_error), 80);
    IF normalized_error = '' THEN normalized_error := 'unknown'; END IF;

    UPDATE cti.delivery_log AS delivery
    SET status = 'failed',
        error_code = (CASE WHEN retry_safe_value THEN 'retry_safe:' ELSE 'ambiguous:' END) || normalized_error,
        locked_at = NULL,
        attempted_at = now()
    WHERE delivery.id = delivery_id_value
      AND delivery.channel = 'telegram'
      AND delivery.status = 'queued';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Telegram delivery reservation is not active.' USING ERRCODE = '55000';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION cti.telegram_article_lookup(
    action_value text,
    query_value text DEFAULT NULL,
    result_limit_value integer DEFAULT 5
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = cti, pg_temp
AS $$
DECLARE
    normalized_action text := lower(trim(coalesce(action_value, '')));
    normalized_query text := lower(trim(coalesce(query_value, '')));
    result_limit integer := least(greatest(coalesce(result_limit_value, 5), 1), 5);
    article_results jsonb := '[]'::jsonb;
BEGIN
    IF normalized_action NOT IN ('menu', 'help', 'category', 'search') THEN
        RAISE EXCEPTION 'Unsupported Telegram CTI action.' USING ERRCODE = '22023';
    END IF;
    IF normalized_action = 'category'
       AND normalized_query NOT IN ('malware', 'vulnerability', 'data_breach', 'threat_intelligence', 'other') THEN
        RAISE EXCEPTION 'Unsupported Telegram CTI category.' USING ERRCODE = '22023';
    END IF;
    IF normalized_action = 'search' AND (
        char_length(normalized_query) NOT BETWEEN 2 AND 60
        OR normalized_query !~ '^[[:alnum:][:space:]._:/-]+$'
    ) THEN
        RAISE EXCEPTION 'Telegram CTI search query is invalid.' USING ERRCODE = '22023';
    END IF;

    IF normalized_action IN ('category', 'search') THEN
        SELECT coalesce(jsonb_agg(to_jsonb(selected_article) ORDER BY selected_article.rank_order), '[]'::jsonb)
        INTO article_results
        FROM (
            SELECT article.id AS article_id, left(article.title, 220) AS title,
                left(article.summary_tr, 700) AS summary_tr, article.category, article.severity,
                article.canonical_url, source.name AS source_name, article.published_at,
                row_number() OVER (ORDER BY
                    CASE article.severity WHEN 'critical' THEN 1 WHEN 'high' THEN 2
                        WHEN 'medium' THEN 3 WHEN 'low' THEN 4 ELSE 5 END,
                    article.published_at DESC, article.id DESC) AS rank_order
            FROM cti.articles AS article
            JOIN LATERAL (
                SELECT source.name
                FROM cti.article_occurrences AS occurrence
                JOIN cti.sources AS source ON source.id = occurrence.source_id
                WHERE occurrence.article_id = article.id
                ORDER BY occurrence.discovered_at, occurrence.id
                LIMIT 1
            ) AS source ON true
            WHERE article.analyzed_at IS NOT NULL AND article.summary_tr IS NOT NULL
              AND article.published_at >= now() - interval '8 days'
              AND ((normalized_action = 'category' AND article.category = normalized_query)
                OR (normalized_action = 'search'
                  AND to_tsvector('simple', article.title || ' ' || coalesce(article.summary_tr, ''))
                      @@ websearch_to_tsquery('simple', normalized_query)))
            ORDER BY CASE article.severity WHEN 'critical' THEN 1 WHEN 'high' THEN 2
                    WHEN 'medium' THEN 3 WHEN 'low' THEN 4 ELSE 5 END,
                article.published_at DESC, article.id DESC
            LIMIT result_limit
        ) AS selected_article;
    END IF;

    RETURN jsonb_build_object('action', normalized_action, 'query', nullif(normalized_query, ''),
        'articles', article_results, 'generated_at', now());
END;
$$;

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

CREATE OR REPLACE FUNCTION cti.claim_weekly_report(
    window_start_value timestamptz,
    window_end_value timestamptz,
    report_daily_limit_value integer DEFAULT 1,
    report_monthly_limit_value integer DEFAULT 8,
    provider_daily_limit_value integer DEFAULT 20,
    provider_monthly_limit_value integer DEFAULT 400,
    max_articles_value integer DEFAULT 40
)
RETURNS TABLE(
    report_id bigint,
    report_window_start timestamptz,
    report_window_end timestamptz,
    article_payload jsonb
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
    utc_day_start timestamptz;
    utc_month_start timestamptz;
    total_daily_usage integer;
    total_monthly_usage integer;
    weekly_daily_usage integer;
    weekly_monthly_usage integer;
    active_analysis_reservations integer;
    selected_report cti.reports%ROWTYPE;
    selected_payload jsonb;
BEGIN
    IF window_start_value IS NULL
       OR window_end_value IS NULL
       OR window_end_value <= window_start_value
       OR window_end_value > reference_time + interval '15 minutes'
       OR window_end_value - window_start_value NOT BETWEEN interval '6 days' AND interval '8 days' THEN
        RAISE EXCEPTION 'Weekly report window must cover approximately seven completed days.'
            USING ERRCODE = '22023';
    END IF;

    IF report_daily_limit_value NOT BETWEEN 1 AND 5
       OR report_monthly_limit_value NOT BETWEEN report_daily_limit_value AND 31
       OR provider_daily_limit_value NOT BETWEEN report_daily_limit_value AND 100
       OR provider_monthly_limit_value NOT BETWEEN provider_daily_limit_value AND 5000
       OR max_articles_value NOT BETWEEN 1 AND 80 THEN
        RAISE EXCEPTION 'Weekly report quota or article limit is invalid.'
            USING ERRCODE = '22023';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtextextended('cti.claim_weekly_report', 0));

    utc_day_start := date_trunc('day', reference_time AT TIME ZONE 'UTC')
        AT TIME ZONE 'UTC';
    utc_month_start := date_trunc('month', reference_time AT TIME ZONE 'UTC')
        AT TIME ZONE 'UTC';

    UPDATE cti.reports AS report
    SET status = 'failed',
        locked_at = NULL,
        last_error_code = 'stale_lock_recovered'
    WHERE report.report_type = 'weekly'
      AND report.status = 'draft'
      AND report.locked_at < reference_time - interval '30 minutes';

    SELECT count(*) INTO total_daily_usage
    FROM cti.ai_usage AS usage
    WHERE usage.requested_at >= utc_day_start;

    SELECT count(*) INTO total_monthly_usage
    FROM cti.ai_usage AS usage
    WHERE usage.requested_at >= utc_month_start;

    SELECT count(*) INTO weekly_daily_usage
    FROM cti.ai_usage AS usage
    WHERE usage.purpose = 'weekly_report'
      AND usage.requested_at >= utc_day_start;

    SELECT count(*) INTO weekly_monthly_usage
    FROM cti.ai_usage AS usage
    WHERE usage.purpose = 'weekly_report'
      AND usage.requested_at >= utc_month_start;

    SELECT count(*) INTO active_analysis_reservations
    FROM cti.analysis_jobs AS job
    WHERE job.status = 'processing';

    IF weekly_daily_usage >= report_daily_limit_value
       OR weekly_monthly_usage >= report_monthly_limit_value
       OR total_daily_usage + active_analysis_reservations >= provider_daily_limit_value
       OR total_monthly_usage + active_analysis_reservations >= provider_monthly_limit_value THEN
        RETURN;
    END IF;

    SELECT report.*
    INTO selected_report
    FROM cti.reports AS report
    WHERE report.report_type = 'weekly'
      AND report.category IS NULL
      AND report.window_start = window_start_value
      AND report.window_end = window_end_value
    FOR UPDATE;

    IF FOUND THEN
        IF selected_report.status <> 'failed' OR selected_report.attempts >= 3 THEN
            RETURN;
        END IF;

        UPDATE cti.reports AS report
        SET status = 'draft',
            attempts = report.attempts + 1,
            locked_at = reference_time,
            last_error_code = NULL,
            generated_at = reference_time
        WHERE report.id = selected_report.id
        RETURNING report.* INTO selected_report;
    ELSE
        INSERT INTO cti.reports (
            report_type,
            window_start,
            window_end,
            status,
            title,
            content,
            generated_at,
            expires_at,
            attempts,
            locked_at
        )
        VALUES (
            'weekly',
            window_start_value,
            window_end_value,
            'draft',
            'Pending weekly CTI report',
            'Pending AI generation.',
            reference_time,
            reference_time + interval '8 weeks',
            1,
            reference_time
        )
        RETURNING * INTO selected_report;
    END IF;

    SELECT jsonb_agg(to_jsonb(candidate) ORDER BY candidate.severity_rank, candidate.published_at DESC)
    INTO selected_payload
    FROM (
        SELECT
            article.id AS article_id,
            article.title,
            article.category,
            article.severity,
            left(article.summary_tr, 800) AS summary_tr,
            article.analysis_confidence,
            article.published_at,
            article.canonical_url,
            selected_source.name AS source_name,
            CASE article.severity
                WHEN 'critical' THEN 1
                WHEN 'high' THEN 2
                WHEN 'medium' THEN 3
                WHEN 'low' THEN 4
                ELSE 5
            END AS severity_rank
        FROM cti.articles AS article
        JOIN LATERAL (
            SELECT source.name
            FROM cti.article_occurrences AS occurrence
            JOIN cti.sources AS source ON source.id = occurrence.source_id
            WHERE occurrence.article_id = article.id
            ORDER BY source.trust_score DESC, occurrence.discovered_at, occurrence.id
            LIMIT 1
        ) AS selected_source ON true
        WHERE article.analyzed_at IS NOT NULL
          AND article.summary_tr IS NOT NULL
          AND article.published_at >= window_start_value
          AND article.published_at < window_end_value
        ORDER BY severity_rank, article.published_at DESC, article.id
        LIMIT max_articles_value
    ) AS candidate;

    IF selected_payload IS NULL THEN
        DELETE FROM cti.reports WHERE id = selected_report.id;
        RETURN;
    END IF;

    report_id := selected_report.id;
    report_window_start := selected_report.window_start;
    report_window_end := selected_report.window_end;
    article_payload := selected_payload;
    RETURN NEXT;
END;
$$;

CREATE OR REPLACE FUNCTION cti.complete_weekly_report(
    report_id_value bigint,
    title_value text,
    content_value text,
    article_ids_value bigint[],
    model_value text,
    prompt_tokens_value integer DEFAULT 0,
    output_tokens_value integer DEFAULT 0,
    total_tokens_value integer DEFAULT 0
)
RETURNS bigint
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
    selected_report cti.reports%ROWTYPE;
    valid_article_count integer;
BEGIN
    IF title_value IS NULL OR char_length(trim(title_value)) NOT BETWEEN 1 AND 500
       OR content_value IS NULL OR char_length(trim(content_value)) NOT BETWEEN 1 AND 100000
       OR article_ids_value IS NULL OR cardinality(article_ids_value) NOT BETWEEN 1 AND 80
       OR model_value IS NULL OR char_length(trim(model_value)) NOT BETWEEN 1 AND 200
       OR prompt_tokens_value < 0 OR output_tokens_value < 0
       OR total_tokens_value < prompt_tokens_value + output_tokens_value THEN
        RAISE EXCEPTION 'Completed weekly report data is invalid.' USING ERRCODE = '22023';
    END IF;

    SELECT report.* INTO selected_report
    FROM cti.reports AS report
    WHERE report.id = report_id_value
      AND report.report_type = 'weekly'
      AND report.status = 'draft'
      AND report.locked_at IS NOT NULL
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Weekly report reservation is not active.' USING ERRCODE = '55000';
    END IF;

    SELECT count(DISTINCT article.id)
    INTO valid_article_count
    FROM cti.articles AS article
    WHERE article.id = ANY(article_ids_value)
      AND article.analyzed_at IS NOT NULL
      AND article.published_at >= selected_report.window_start
      AND article.published_at < selected_report.window_end;

    IF valid_article_count <> cardinality(article_ids_value) THEN
        RAISE EXCEPTION 'Weekly report article selection is invalid.' USING ERRCODE = '22023';
    END IF;

    UPDATE cti.reports AS report
    SET status = 'ready',
        title = trim(title_value),
        content = trim(content_value),
        locked_at = NULL,
        last_error_code = NULL,
        generated_at = reference_time
    WHERE report.id = selected_report.id;

    INSERT INTO cti.report_articles (
        report_id,
        article_id,
        title_snapshot,
        url_snapshot,
        source_snapshot
    )
    SELECT
        selected_report.id,
        article.id,
        article.title,
        article.canonical_url,
        selected_source.name
    FROM cti.articles AS article
    JOIN LATERAL (
        SELECT source.name
        FROM cti.article_occurrences AS occurrence
        JOIN cti.sources AS source ON source.id = occurrence.source_id
        WHERE occurrence.article_id = article.id
        ORDER BY source.trust_score DESC, occurrence.discovered_at, occurrence.id
        LIMIT 1
    ) AS selected_source ON true
    WHERE article.id = ANY(article_ids_value)
    ON CONFLICT (report_id, url_snapshot) DO NOTHING;

    UPDATE cti.articles AS article
    SET reported_at = reference_time,
        updated_at = reference_time
    WHERE article.id = ANY(article_ids_value);

    INSERT INTO cti.ai_usage (
        purpose,
        model,
        request_status,
        prompt_tokens,
        output_tokens,
        total_tokens,
        requested_at
    )
    VALUES (
        'weekly_report',
        trim(model_value),
        'success',
        prompt_tokens_value,
        output_tokens_value,
        total_tokens_value,
        reference_time
    );

    RETURN selected_report.id;
END;
$$;

CREATE OR REPLACE FUNCTION cti.fail_weekly_report(
    report_id_value bigint,
    model_value text,
    rate_limited_value boolean,
    error_code_value text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
    updated_reports integer;
BEGIN
    IF model_value IS NULL OR char_length(trim(model_value)) NOT BETWEEN 1 AND 200
       OR error_code_value IS NULL OR char_length(trim(error_code_value)) NOT BETWEEN 1 AND 100 THEN
        RAISE EXCEPTION 'Weekly report failure data is invalid.' USING ERRCODE = '22023';
    END IF;

    UPDATE cti.reports AS report
    SET status = 'failed',
        locked_at = NULL,
        last_error_code = trim(error_code_value),
        generated_at = reference_time
    WHERE report.id = report_id_value
      AND report.report_type = 'weekly'
      AND report.status = 'draft';
    GET DIAGNOSTICS updated_reports = ROW_COUNT;

    IF updated_reports <> 1 THEN
        RAISE EXCEPTION 'Weekly report reservation is not active.' USING ERRCODE = '55000';
    END IF;

    INSERT INTO cti.ai_usage (purpose, model, request_status, requested_at)
    VALUES (
        'weekly_report',
        trim(model_value),
        CASE WHEN rate_limited_value THEN 'rate_limited' ELSE 'failed' END,
        reference_time
    );
END;
$$;

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

CREATE OR REPLACE FUNCTION cti.record_source_check(
    source_id_value bigint,
    success_value boolean,
    error_code_value text DEFAULT NULL
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, cti
AS $$
DECLARE
    reference_time timestamptz := clock_timestamp();
BEGIN
    IF success_value THEN
        IF error_code_value IS NOT NULL THEN
            RAISE EXCEPTION 'A successful source check cannot include an error code.'
                USING ERRCODE = '22023';
        END IF;

        UPDATE cti.sources
        SET last_checked_at = reference_time,
            last_success_at = reference_time,
            last_error_code = NULL,
            updated_at = reference_time
        WHERE id = source_id_value
          AND enabled = true;
    ELSE
        IF error_code_value IS NULL OR error_code_value NOT IN (
            'feed_read_failed',
            'feed_item_invalid',
            'feed_item_store_failed'
        ) THEN
            RAISE EXCEPTION 'Invalid source check error code.' USING ERRCODE = '22023';
        END IF;

        UPDATE cti.sources
        SET last_checked_at = reference_time,
            last_error_at = CASE
                WHEN last_error_code IS NOT NULL THEN last_checked_at
                ELSE last_error_at
            END,
            last_error_code = error_code_value,
            updated_at = reference_time
        WHERE id = source_id_value
          AND enabled = true;
    END IF;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Unknown or disabled CTI source.' USING ERRCODE = '22023';
    END IF;
END;
$$;

REVOKE ALL ON SCHEMA cti FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA cti FROM PUBLIC;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA cti FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.apply_retention() FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.ingest_feed_item(bigint, text, text, text, timestamptz)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.record_source_check(bigint, boolean, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.enqueue_article_analysis() FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.claim_analysis_jobs(integer, integer, integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.complete_article_analysis(
    bigint, text, text, text, text, numeric, text, integer, integer
) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.defer_article_analysis(
    bigint, text, boolean, boolean, text
) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.claim_weekly_report(
    timestamptz, timestamptz, integer, integer, integer, integer, integer
) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.complete_weekly_report(
    bigint, text, text, bigint[], text, integer, integer, integer
) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.fail_weekly_report(bigint, text, boolean, text)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.claim_weekly_telegram_delivery() FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.complete_weekly_telegram_delivery(bigint, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.fail_weekly_telegram_delivery(bigint, text, boolean)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION cti.telegram_article_lookup(text, text, integer)
    FROM PUBLIC;

GRANT CONNECT ON DATABASE cti TO cti_n8n;
GRANT USAGE ON SCHEMA cti TO cti_n8n;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA cti TO cti_n8n;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA cti TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.apply_retention() TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.ingest_feed_item(bigint, text, text, text, timestamptz)
    TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.record_source_check(bigint, boolean, text) TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.claim_analysis_jobs(integer, integer, integer) TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.complete_article_analysis(
    bigint, text, text, text, text, numeric, text, integer, integer
) TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.defer_article_analysis(
    bigint, text, boolean, boolean, text
) TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.claim_weekly_report(
    timestamptz, timestamptz, integer, integer, integer, integer, integer
) TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.complete_weekly_report(
    bigint, text, text, bigint[], text, integer, integer, integer
) TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.fail_weekly_report(bigint, text, boolean, text)
    TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.claim_weekly_telegram_delivery() TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.complete_weekly_telegram_delivery(bigint, text)
    TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.fail_weekly_telegram_delivery(bigint, text, boolean)
    TO cti_n8n;
GRANT EXECUTE ON FUNCTION cti.telegram_article_lookup(text, text, integer)
    TO cti_n8n;

ALTER DEFAULT PRIVILEGES IN SCHEMA cti
    GRANT SELECT, INSERT, UPDATE ON TABLES TO cti_n8n;
ALTER DEFAULT PRIVILEGES IN SCHEMA cti
    GRANT USAGE, SELECT ON SEQUENCES TO cti_n8n;

CREATE OR REPLACE VIEW cti.dashboard_articles
WITH (security_barrier = true)
AS
SELECT
    article.id,
    article.title,
    article.category,
    article.severity,
    article.summary_tr,
    article.canonical_url,
    article.published_at,
    article.analyzed_at,
    ARRAY(
        SELECT DISTINCT source.name
        FROM cti.article_occurrences AS occurrence
        JOIN cti.sources AS source ON source.id = occurrence.source_id
        WHERE occurrence.article_id = article.id
        ORDER BY source.name
    ) AS source_names
FROM cti.articles AS article
WHERE article.analyzed_at IS NOT NULL
  AND article.summary_tr IS NOT NULL
  AND article.category IS NOT NULL
  AND article.severity IS NOT NULL
  AND article.published_at >= now() - interval '30 days';

CREATE OR REPLACE VIEW cti.dashboard_reports
WITH (security_barrier = true)
AS
SELECT
    report.id,
    report.report_type,
    report.window_start,
    report.window_end,
    report.category,
    report.status,
    report.title,
    report.content,
    report.generated_at,
    report.sent_at,
    report.expires_at
FROM cti.reports AS report
WHERE report.status IN ('ready', 'sent')
  AND report.expires_at > now();

CREATE OR REPLACE VIEW cti.dashboard_ai_usage
WITH (security_barrier = true)
AS
WITH boundaries AS (
    SELECT
        date_trunc('day', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS utc_day_start,
        date_trunc('month', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS utc_month_start
)
SELECT
    count(usage.id) FILTER (WHERE usage.requested_at >= boundaries.utc_day_start) AS today_requests,
    COALESCE(sum(usage.total_tokens) FILTER (
        WHERE usage.requested_at >= boundaries.utc_day_start
    ), 0)::bigint AS today_tokens,
    count(usage.id) AS month_requests,
    COALESCE(sum(usage.prompt_tokens), 0)::bigint AS month_prompt_tokens,
    COALESCE(sum(usage.output_tokens), 0)::bigint AS month_output_tokens,
    COALESCE(sum(usage.total_tokens), 0)::bigint AS month_total_tokens,
    count(usage.id) FILTER (WHERE usage.purpose = 'article_analysis') AS month_article_requests,
    count(usage.id) FILTER (WHERE usage.purpose = 'weekly_report') AS month_report_requests,
    count(usage.id) FILTER (WHERE usage.request_status <> 'success') AS month_failed_requests,
    max(usage.requested_at) AS last_requested_at
FROM boundaries
LEFT JOIN cti.ai_usage AS usage
    ON usage.requested_at >= boundaries.utc_month_start;

REVOKE ALL ON ALL TABLES IN SCHEMA cti FROM cti_dashboard;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA cti FROM cti_dashboard;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cti FROM cti_dashboard;
REVOKE TEMPORARY ON DATABASE cti FROM cti_dashboard;

GRANT CONNECT ON DATABASE cti TO cti_dashboard;
GRANT USAGE ON SCHEMA cti TO cti_dashboard;
GRANT SELECT ON cti.dashboard_articles, cti.dashboard_reports, cti.dashboard_ai_usage TO cti_dashboard;

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

INSERT INTO cti.schema_versions (version)
VALUES (5)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (6)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (7)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (8)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (9)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (10)
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
    'CISA Cybersecurity Advisories',
    'https://www.cisa.gov/cybersecurity-advisories/all.xml',
    ARRAY['cisa.gov', 'www.cisa.gov'],
    '.l-page-section--rich-text .l-page-section__content',
    95,
    true
)
ON CONFLICT (name) DO UPDATE
SET feed_url = EXCLUDED.feed_url,
    allowed_hosts = EXCLUDED.allowed_hosts,
    content_selector = EXCLUDED.content_selector,
    trust_score = EXCLUDED.trust_score,
    updated_at = now();

INSERT INTO cti.schema_versions (version)
VALUES (11)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (12)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (13)
ON CONFLICT (version) DO NOTHING;

INSERT INTO cti.schema_versions (version)
VALUES (14)
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
    'Microsoft Security Blog',
    'https://www.microsoft.com/en-us/security/blog/feed/',
    ARRAY['www.microsoft.com'],
    '.entry-content',
    90,
    true
)
ON CONFLICT (name) DO UPDATE
SET feed_url = EXCLUDED.feed_url,
    allowed_hosts = EXCLUDED.allowed_hosts,
    content_selector = EXCLUDED.content_selector,
    trust_score = EXCLUDED.trust_score,
    updated_at = now();

INSERT INTO cti.schema_versions (version)
VALUES (15)
ON CONFLICT (version) DO NOTHING;
