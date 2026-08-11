\set ON_ERROR_STOP on

INSERT INTO cti.sources (
    name,
    feed_url,
    allowed_hosts,
    content_selector,
    trust_score
)
VALUES (
    'Test Source',
    'https://feeds.example.test/security.xml',
    ARRAY['news.example.test'],
    'article',
    80
);

DO $$
DECLARE
    source_id_value bigint;
    result_record record;
    result_count integer;
BEGIN
    SELECT id INTO source_id_value
    FROM cti.sources
    WHERE name = 'Test Source';

    SELECT * INTO result_record
    FROM cti.ingest_feed_item(
        source_id_value,
        'https://news.example.test/advisories/one#details',
        'Critical Product Vulnerability',
        'guid-one',
        now() - interval '1 hour'
    );

    IF result_record.is_new IS DISTINCT FROM true
       OR result_record.occurrence_added IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'First ingestion did not create an article and occurrence.';
    END IF;

    SELECT * INTO result_record
    FROM cti.ingest_feed_item(
        source_id_value,
        'https://news.example.test/advisories/one',
        'Critical Product Vulnerability',
        'guid-one',
        now() - interval '1 hour'
    );

    IF result_record.is_new IS DISTINCT FROM false
       OR result_record.occurrence_added IS DISTINCT FROM false THEN
        RAISE EXCEPTION 'Repeated URL was not deduplicated.';
    END IF;

    SELECT * INTO result_record
    FROM cti.ingest_feed_item(
        source_id_value,
        'https://news.example.test/advisories/syndicated-copy',
        'Critical Product Vulnerability',
        'guid-two',
        now() - interval '50 minutes'
    );

    IF result_record.is_new IS DISTINCT FROM false
       OR result_record.occurrence_added IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Exact-title syndicated occurrence was not merged.';
    END IF;

    SELECT * INTO result_record
    FROM cti.ingest_feed_item(
        source_id_value,
        'https://news.example.test/advisories/guid-moved',
        'A Changed Feed Title',
        'guid-two',
        now() - interval '40 minutes'
    );

    IF result_record.is_new IS DISTINCT FROM false
       OR result_record.occurrence_added IS DISTINCT FROM false THEN
        RAISE EXCEPTION 'Repeated feed GUID was not deduplicated.';
    END IF;

    SELECT count(*) INTO result_count
    FROM cti.ingest_feed_item(
        source_id_value,
        'https://news.example.test/advisories/old',
        'Old Security Story',
        'guid-old',
        now() - interval '31 hours'
    );

    IF result_count <> 0 THEN
        RAISE EXCEPTION 'An article outside the 30-hour window was accepted.';
    END IF;

    BEGIN
        PERFORM * FROM cti.ingest_feed_item(
            source_id_value,
            'https://attacker.example/advisories/one',
            'Disallowed Host Story',
            'guid-host',
            now()
        );
        RAISE EXCEPTION 'A disallowed article host was accepted.';
    EXCEPTION
        WHEN SQLSTATE '22023' THEN
            NULL;
    END;

    IF (SELECT count(*) FROM cti.articles) <> 1 THEN
        RAISE EXCEPTION 'Deduplication left an unexpected article count.';
    END IF;

    IF (SELECT count(*) FROM cti.article_occurrences) <> 2 THEN
        RAISE EXCEPTION 'Deduplication left an unexpected occurrence count.';
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
END;
$$;

SELECT 'CTI ingestion tests passed.' AS result;
