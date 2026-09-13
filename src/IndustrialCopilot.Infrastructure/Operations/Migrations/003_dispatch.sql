CREATE TABLE IF NOT EXISTS operations.human_approvals (
 id uuid NOT NULL, revision integer NOT NULL, token uuid NOT NULL,
 PRIMARY KEY(id,revision), FOREIGN KEY(id,token) REFERENCES operations.work_order_snapshots(id,token)
);
CREATE TABLE IF NOT EXISTS operations.dispatch_attempts (
 attempt_id uuid PRIMARY KEY, work_order_id uuid NOT NULL REFERENCES operations.work_orders(id),
 revision integer NOT NULL, requested_token uuid NOT NULL, reserved_token uuid NOT NULL,
 run_id uuid REFERENCES operations.runs(id), run_token uuid,
 actor_id text NOT NULL, reserved_at text NOT NULL, state integer NOT NULL CHECK(state BETWEEN 1 AND 4),
 invocation_started boolean NOT NULL DEFAULT false, external_reference text, failure_category text,
 UNIQUE(work_order_id,revision),
 FOREIGN KEY(work_order_id,reserved_token) REFERENCES operations.work_order_snapshots(id,token),
 CHECK((state=2)=(external_reference IS NOT NULL)),
 CHECK((run_id IS NULL)=(run_token IS NULL))
);
CREATE INDEX IF NOT EXISTS dispatch_attempts_run ON operations.dispatch_attempts(run_id) WHERE state IN (1,4);
CREATE UNIQUE INDEX IF NOT EXISTS dispatch_attempts_one_active_run ON operations.dispatch_attempts(run_id) WHERE state IN (1,4);
CREATE TABLE IF NOT EXISTS operations.dispatch_events (
 sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, attempt_id uuid NOT NULL REFERENCES operations.dispatch_attempts(attempt_id),
 actor_id text NOT NULL, recorded_at text NOT NULL, state integer NOT NULL CHECK(state BETWEEN 1 AND 4), category text NOT NULL
);
CREATE INDEX IF NOT EXISTS dispatch_events_attempt ON operations.dispatch_events(attempt_id,sequence);
