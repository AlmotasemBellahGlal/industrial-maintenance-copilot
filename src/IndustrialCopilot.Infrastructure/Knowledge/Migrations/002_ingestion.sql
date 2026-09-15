ALTER TABLE knowledge.chunks ADD COLUMN IF NOT EXISTS metadata jsonb;
ALTER TABLE knowledge.chunks ADD COLUMN IF NOT EXISTS page integer CHECK (page > 0);
ALTER TABLE knowledge.chunks ADD COLUMN IF NOT EXISTS section text;
CREATE TABLE IF NOT EXISTS knowledge.ingestion_attempts (
    id uuid PRIMARY KEY, document_id uuid NOT NULL, revision_id uuid NOT NULL,
    metadata jsonb, state integer NOT NULL CHECK (state BETWEEN 1 AND 3),
    stage integer NOT NULL CHECK (stage BETWEEN 1 AND 5), failure integer,
    started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    backend_pid integer NOT NULL, backend_start timestamptz NOT NULL,
    pages integer CHECK (pages >= 0), chunks integer CHECK (chunks >= 0)
);
CREATE INDEX IF NOT EXISTS ingestion_revision_history ON knowledge.ingestion_attempts(document_id,revision_id,started_at DESC);
