CREATE EXTENSION IF NOT EXISTS vector;
CREATE SCHEMA IF NOT EXISTS knowledge;
CREATE TABLE IF NOT EXISTS knowledge.profiles (
    profile text PRIMARY KEY,
    binding text NOT NULL,
    dimensions integer NOT NULL CHECK (dimensions BETWEEN 1 AND 16000),
    UNIQUE (profile, dimensions)
);
CREATE TABLE IF NOT EXISTS knowledge.revisions (
    document_id uuid NOT NULL,
    revision_id uuid NOT NULL,
    profile text NOT NULL,
    dimensions integer NOT NULL,
    PRIMARY KEY (document_id, revision_id),
    UNIQUE (document_id, revision_id, profile, dimensions),
    FOREIGN KEY (profile, dimensions) REFERENCES knowledge.profiles (profile, dimensions)
);
CREATE TABLE IF NOT EXISTS knowledge.chunks (
    document_id uuid NOT NULL,
    revision_id uuid NOT NULL,
    chunk_id uuid NOT NULL,
    profile text NOT NULL,
    dimensions integer NOT NULL,
    locator text NOT NULL CHECK (length(btrim(locator)) > 0),
    content text NOT NULL CHECK (length(btrim(content)) > 0),
    embedding vector NOT NULL,
    search_text tsvector GENERATED ALWAYS AS (to_tsvector('simple'::regconfig, content)) STORED,
    PRIMARY KEY (document_id, revision_id, chunk_id),
    FOREIGN KEY (document_id, revision_id, profile, dimensions)
        REFERENCES knowledge.revisions (document_id, revision_id, profile, dimensions),
    CHECK (vector_dims(embedding) = dimensions AND vector_norm(embedding) > 0)
);
CREATE INDEX IF NOT EXISTS knowledge_chunks_text ON knowledge.chunks USING gin(search_text);
CREATE INDEX IF NOT EXISTS knowledge_chunks_profile ON knowledge.chunks(profile);
