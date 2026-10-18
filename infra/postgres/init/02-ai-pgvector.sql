-- Gün 4 Lab 4: RAG doküman katmanı (pgvector + HNSW + GIN).
-- pgvector/pgvector:pg18 image'ında `vector` extension ÖNDEN KURULUDUR.
CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS ai;

CREATE TABLE IF NOT EXISTS ai.documents (
  id          bigserial PRIMARY KEY,
  doc_id      uuid NOT NULL,
  chunk_index int  NOT NULL,
  content     text NOT NULL,
  -- DİKKAT: boyut embedding modeli ile BİREBİR olmalı. text-embedding-3-small = 1536.
  -- Farklı boyutlu model yazarsanız: "expected 1536 dimensions, not 3072".
  embedding   vector(1536) NOT NULL,
  source      text NOT NULL,
  updated_at  timestamptz NOT NULL DEFAULT now(),
  -- Hybrid arama için keyword vektörü (generated column — elle güncellenmez).
  content_tsv tsvector GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED
);

-- Aynı kaynak + chunk tekrar ingest edilirse çift kayıt olmasın (upsert hedefi).
CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_doc_chunk ON ai.documents (doc_id, chunk_index);

-- HNSW: yaklaşık en yakın komşu. GOTCHA: build RAM tüketir; büyük tabloda
-- SET maintenance_work_mem = '2GB'; verip oluşturun, aksi halde disk'e taşar ve saatler sürer.
-- Not: ~bin satırın altında planner seq scan'i tercih eder — bu normaldir.
CREATE INDEX IF NOT EXISTS ix_documents_embedding
  ON ai.documents USING hnsw (embedding vector_cosine_ops)
  WITH (m = 16, ef_construction = 64);

CREATE INDEX IF NOT EXISTS ix_documents_tsv ON ai.documents USING gin (content_tsv);

-- Demo bilgi tabanı (Lab 10'daki "Kış lastiği iade süresi kaç gün?" sorusu buradan cevaplanır).
-- Embedding'ler uygulama tarafında /api/rag/ingest ile üretilir; burada yalnızca şema hazırlanır.
COMMENT ON TABLE ai.documents IS 'RAG chunk store — pgvector HNSW + GIN full-text (hybrid retrieval)';
