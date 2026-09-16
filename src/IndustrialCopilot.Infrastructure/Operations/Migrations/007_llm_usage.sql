CREATE TABLE IF NOT EXISTS operations.llm_usage (
 call_id uuid PRIMARY KEY, actor text NOT NULL CHECK(length(actor) BETWEEN 1 AND 200),
 equipment_id uuid, correlation_id uuid NOT NULL, run_id uuid,
 provider text NOT NULL CHECK(length(provider) BETWEEN 1 AND 64),
 started_at timestamptz NOT NULL, status integer NOT NULL CHECK(status BETWEEN 0 AND 4),
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'));
CREATE INDEX IF NOT EXISTS llm_usage_owner ON operations.llm_usage(actor,started_at DESC,call_id);
CREATE INDEX IF NOT EXISTS llm_usage_correlation ON operations.llm_usage(actor,correlation_id);
CREATE INDEX IF NOT EXISTS llm_usage_run ON operations.llm_usage(actor,run_id);
