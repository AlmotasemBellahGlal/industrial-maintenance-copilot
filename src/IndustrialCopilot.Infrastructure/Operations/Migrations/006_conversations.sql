CREATE TABLE IF NOT EXISTS operations.conversations (
 id uuid PRIMARY KEY, actor text NOT NULL, equipment_id uuid NOT NULL,
 document_id uuid NOT NULL, revision_id uuid NOT NULL,
 culture text NOT NULL CHECK(culture IN ('en-US','ar-EG')),
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(document_id,equipment_id) REFERENCES operations.manuals(id,equipment_id),
 FOREIGN KEY(revision_id,document_id) REFERENCES operations.manual_revisions(id,manual_id));
CREATE INDEX IF NOT EXISTS conversations_owner ON operations.conversations(actor,created_at DESC,id);
CREATE TABLE IF NOT EXISTS operations.ask_turns (
 id uuid PRIMARY KEY, conversation_id uuid NOT NULL REFERENCES operations.conversations(id),
 sequence bigint GENERATED ALWAYS AS IDENTITY, question text NOT NULL CHECK(length(question)<=2000),
 answer text NOT NULL DEFAULT '' CHECK(length(answer)<=32000),
 state integer NOT NULL DEFAULT 0 CHECK(state BETWEEN 0 AND 4),
 citations jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(citations)='array'),
 correlation_id uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
 deadline timestamptz NOT NULL DEFAULT now()+interval '90 seconds',
 finished_at timestamptz, UNIQUE(conversation_id,sequence));
CREATE UNIQUE INDEX IF NOT EXISTS ask_one_active ON operations.ask_turns(conversation_id) WHERE state=0;
