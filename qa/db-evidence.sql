-- Sprint 4 QA - delivery_db evidence on staging. Read-only.
-- Run from WSL in the QA folder:
--   psql "host=sellora-server-db.postgres.database.azure.com port=5432 dbname=delivery_db user=sellora_admin sslmode=require" -f db-evidence.sql | tee evidence/CSP-261/db/delivery-db-run.txt
\pset pager off
\echo '==== Run at' `date '+%Y-%m-%d %H:%M'`

\echo '==== Q1 Migrations applied (CSP-266 / BUG-02: expect 20261011072729_AddReturnRequests)'
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;

\echo '==== Q2 Tables and row counts (BUG-04: DeliveryConfirmations naming)'
SELECT table_name,
       (xpath('/row/c/text()',
         query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text::int AS rows
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY 1;

\echo '==== Q3 Delivery jobs (CSP-261 Q1)'
SELECT delivery_reference, order_reference, fulfilment_type, status, agency_name, shop_name, shop_owner_email,
       total, currency, assigned_rep_name, scheduled_date, delivered_at, created_at
FROM delivery_jobs ORDER BY created_at;

\echo '==== Q4 Agency email on jobs (BUG-03)'
SELECT delivery_reference, agency_name, agency_email FROM delivery_jobs ORDER BY created_at;

\echo '==== Q5 Outbox events published (CSP-261 Q1, processed_at filled = sent to Kafka)'
SELECT message_key, event_type, topic, created_at, processed_at FROM outbox_messages ORDER BY created_at;

\echo '==== Q6 Order events processed (CSP-261 Q2: more events than jobs = rejected orders skipped)'
SELECT event_id, processed_at FROM processed_delivery_events ORDER BY processed_at;

\echo '==== Q7 Orders with more than one job (CSP-261 Q4/Q6: expect 0 rows)'
SELECT order_id, count(*) FROM delivery_jobs GROUP BY order_id HAVING count(*) > 1;

\echo '==== Q8 Status history (CSP-263, rows appear once jobs move)'
SELECT * FROM delivery_status_history ORDER BY 1 LIMIT 50;
