ALTER TABLE operations.dispatch_attempts ADD COLUMN IF NOT EXISTS next_reconciliation_at timestamptz NOT NULL DEFAULT now();
CREATE INDEX IF NOT EXISTS dispatch_attempts_reconciliation ON operations.dispatch_attempts(next_reconciliation_at,attempt_id) WHERE state IN (1,4);
