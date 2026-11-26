-- Records which actor started each run so reads of runs, work orders and traces can be owner-scoped.
CREATE TABLE IF NOT EXISTS operations.run_owners (
 run_id uuid PRIMARY KEY,
 actor text NOT NULL CHECK(length(actor) BETWEEN 1 AND 100),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp());
