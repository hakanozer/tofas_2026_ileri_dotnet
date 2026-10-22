-- İlk başlangıç: uzantıları etkinleştir
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";   -- metin benzerliği araması için

-- Veritabanı oluşturulduğunda yorum
COMMENT ON DATABASE aicommerce_db IS 'AI Commerce Assistant veritabanı';