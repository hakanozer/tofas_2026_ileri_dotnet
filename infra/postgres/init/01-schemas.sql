-- Bounded context schemas (physical separation in one cluster for Day 1 monolith evolution)
CREATE SCHEMA IF NOT EXISTS orders;
CREATE SCHEMA IF NOT EXISTS inventory;
CREATE SCHEMA IF NOT EXISTS ai;

-- pgvector (Day 4 RAG): enable after `CREATE EXTENSION vector` on a pgvector-enabled image
COMMENT ON SCHEMA orders IS 'Order bounded context — event store + read models';
COMMENT ON SCHEMA inventory IS 'Inventory bounded context — stock ledger + cache projections';
