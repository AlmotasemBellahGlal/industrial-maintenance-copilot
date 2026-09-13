CREATE SCHEMA IF NOT EXISTS operations;
CREATE TABLE IF NOT EXISTS operations.equipment (id uuid PRIMARY KEY);
CREATE TABLE IF NOT EXISTS operations.manuals (
 id uuid PRIMARY KEY, equipment_id uuid NOT NULL REFERENCES operations.equipment(id),
 UNIQUE(id,equipment_id));
CREATE TABLE IF NOT EXISTS operations.manual_revisions (
 id uuid PRIMARY KEY, manual_id uuid NOT NULL REFERENCES operations.manuals(id), UNIQUE(id,manual_id));
CREATE TABLE IF NOT EXISTS operations.runs (
 id uuid PRIMARY KEY, equipment_id uuid NOT NULL REFERENCES operations.equipment(id),
 symptom text NOT NULL, status integer NOT NULL CHECK(status BETWEEN 1 AND 7),
 cancellation_requested boolean NOT NULL, token uuid NOT NULL,
 CHECK(status<>4 OR cancellation_requested), CHECK(status<>6 OR NOT cancellation_requested));
CREATE TABLE IF NOT EXISTS operations.work_orders (
 id uuid PRIMARY KEY, token uuid NOT NULL, run_id uuid REFERENCES operations.runs(id));
CREATE TABLE IF NOT EXISTS operations.work_order_snapshots (
 id uuid NOT NULL REFERENCES operations.work_orders(id), token uuid NOT NULL,
 revision integer NOT NULL CHECK(revision>0), status integer NOT NULL CHECK(status BETWEEN 1 AND 5),
 assessment_revision integer, equipment_id uuid NOT NULL REFERENCES operations.equipment(id),
 manual_id uuid NOT NULL, manual_revision_id uuid NOT NULL,
 symptom text NOT NULL, description text NOT NULL,
 PRIMARY KEY(id,token),
 FOREIGN KEY(manual_id,equipment_id) REFERENCES operations.manuals(id,equipment_id),
 FOREIGN KEY(manual_revision_id,manual_id) REFERENCES operations.manual_revisions(id,manual_id),
 CHECK(assessment_revision IS NULL OR assessment_revision=revision));
CREATE TABLE IF NOT EXISTS operations.actions (
 id uuid NOT NULL, token uuid NOT NULL, action_order integer NOT NULL CHECK(action_order>0), instruction text NOT NULL,
 PRIMARY KEY(id,token,action_order), FOREIGN KEY(id,token) REFERENCES operations.work_order_snapshots(id,token));
CREATE TABLE IF NOT EXISTS operations.requirements (
 id uuid NOT NULL, token uuid NOT NULL, position integer NOT NULL CHECK(position>=0), requirement_id uuid NOT NULL,
 description text NOT NULL, mandatory boolean NOT NULL,
 verified_by text, verified_at text, evidence text, satisfied boolean,
 PRIMARY KEY(id,token,requirement_id), UNIQUE(id,token,position),
 FOREIGN KEY(id,token) REFERENCES operations.work_order_snapshots(id,token),
 CHECK((verified_by IS NULL AND verified_at IS NULL AND evidence IS NULL AND satisfied IS NULL)
 OR (verified_by IS NOT NULL AND verified_at IS NOT NULL AND evidence IS NOT NULL AND satisfied IS NOT NULL)));
CREATE TABLE IF NOT EXISTS operations.decisions (
 id uuid NOT NULL, token uuid NOT NULL, revision integer NOT NULL CHECK(revision>=2),
 kind integer NOT NULL CHECK(kind BETWEEN 1 AND 3), supervisor text NOT NULL, decided_at text NOT NULL,
 PRIMARY KEY(id,token,revision), FOREIGN KEY(id,token) REFERENCES operations.work_order_snapshots(id,token));
CREATE TABLE IF NOT EXISTS operations.traces (
 execution_id uuid PRIMARY KEY, correlation_id uuid NOT NULL, run_id uuid,
 version bigint NOT NULL CHECK(version>0), payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'));
CREATE INDEX IF NOT EXISTS work_orders_run ON operations.work_orders(run_id);
CREATE INDEX IF NOT EXISTS traces_correlation ON operations.traces(correlation_id);
CREATE INDEX IF NOT EXISTS traces_run ON operations.traces(run_id);
DO $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='work_orders_current_snapshot' AND conrelid='operations.work_orders'::regclass) THEN
  ALTER TABLE operations.work_orders ADD CONSTRAINT work_orders_current_snapshot
   FOREIGN KEY(id,token) REFERENCES operations.work_order_snapshots(id,token) DEFERRABLE INITIALLY DEFERRED;
 END IF;
END $$;
