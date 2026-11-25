-- Temporary anonymous visitor sessions for the public synthetic demo. Only the SHA-256 of the secret is stored.
CREATE TABLE IF NOT EXISTS operations.demo_sessions (
 actor text PRIMARY KEY CHECK(length(actor) BETWEEN 1 AND 100),
 secret_hash bytea NOT NULL UNIQUE CHECK(length(secret_hash)=32),
 client_key text NOT NULL CHECK(length(client_key) BETWEEN 1 AND 64),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 expires_at timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS demo_sessions_expiry ON operations.demo_sessions(expires_at);
CREATE INDEX IF NOT EXISTS demo_sessions_client ON operations.demo_sessions(client_key,created_at);
