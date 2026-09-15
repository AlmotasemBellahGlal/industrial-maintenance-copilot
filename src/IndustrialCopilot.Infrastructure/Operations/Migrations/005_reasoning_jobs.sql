CREATE TABLE IF NOT EXISTS operations.reasoning_jobs (
 id uuid PRIMARY KEY,
 actor text NOT NULL CHECK(length(actor) BETWEEN 1 AND 100),
 submission_key text NOT NULL CHECK(length(submission_key) BETWEEN 1 AND 128),
 request_hash text NOT NULL,
 run_id uuid NOT NULL UNIQUE REFERENCES operations.runs(id),
 work_order_id uuid NOT NULL UNIQUE,
 equipment_id uuid NOT NULL REFERENCES operations.equipment(id),
 correlation_id uuid NOT NULL,
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
 status integer NOT NULL DEFAULT 1 CHECK(status BETWEEN 1 AND 5),
 phase integer NOT NULL DEFAULT 0 CHECK(phase BETWEEN 0 AND 10),
 cancellation_requested boolean NOT NULL DEFAULT false,
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 owner text, lease_token uuid, lease_until timestamptz,
 attempt integer NOT NULL DEFAULT 0 CHECK(attempt BETWEEN 0 AND 3),
 result jsonb, failure_code text,
 UNIQUE(actor,submission_key),
 CHECK((status=2 AND owner IS NOT NULL AND lease_token IS NOT NULL AND lease_until IS NOT NULL)
    OR (status<>2 AND owner IS NULL AND lease_token IS NULL AND lease_until IS NULL)),
 CHECK(status<>5 OR cancellation_requested)
);
CREATE INDEX IF NOT EXISTS reasoning_jobs_discovery ON operations.reasoning_jobs(available_at,created_at,id) WHERE status IN (1,2);
CREATE TABLE IF NOT EXISTS operations.reasoning_job_attempts (
 job_id uuid NOT NULL REFERENCES operations.reasoning_jobs(id),
 attempt integer NOT NULL CHECK(attempt BETWEEN 1 AND 3),
 execution_id uuid NOT NULL UNIQUE,
 started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 ended_at timestamptz, failure_code text,
 PRIMARY KEY(job_id,attempt)
);

CREATE TABLE IF NOT EXISTS operations.reasoning_job_events (
 job_id uuid NOT NULL REFERENCES operations.reasoning_jobs(id),
 sequence bigint NOT NULL CHECK(sequence>0),
 execution_id uuid NOT NULL REFERENCES operations.reasoning_job_attempts(execution_id),
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
 PRIMARY KEY(job_id,sequence)
);
