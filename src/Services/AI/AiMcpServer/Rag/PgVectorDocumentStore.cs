using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;

namespace EvolutionaryLogistics.Services.Ai.Rag;

/// <summary>Hibrit arama sonucu: doküman satırı + harmanlanmış skor.</summary>
public sealed record RagHit(long Id, string Content, double Score);

/// <summary>
/// pgvector tabanlı doküman deposu. LLM embedding'leri 1536 boyutludur (text-embedding-3-small),
/// bu yüzden kolon <c>vector(1536)</c> olarak tanımlanır — farklı boyut sessiz bozulma yaratır.
/// Bağlantı havuzu <see cref="NpgsqlDataSource"/> ile tembel (lazy) kurulur; host PostgreSQL
/// olmadan da ayağa kalkabilir.
/// </summary>
public sealed class PgVectorDocumentStore(IConfiguration config, ILogger<PgVectorDocumentStore> logger)
{
    private readonly Lazy<NpgsqlDataSource> _dataSource = new(() =>
    {
        var connectionString = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres tanımlı değil.");

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    });

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        const string sql =
            """
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE SCHEMA IF NOT EXISTS ai;

            CREATE TABLE IF NOT EXISTS ai.documents (
              id          bigserial PRIMARY KEY,
              doc_id      uuid NOT NULL,
              chunk_index int  NOT NULL,
              content     text NOT NULL,
              embedding   vector(1536) NOT NULL,
              source      text NOT NULL,
              updated_at  timestamptz NOT NULL DEFAULT now(),
              content_tsv tsvector GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_doc_chunk ON ai.documents (doc_id, chunk_index);
            CREATE INDEX IF NOT EXISTS ix_documents_embedding ON ai.documents USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 64);
            CREATE INDEX IF NOT EXISTS ix_documents_tsv ON ai.documents USING gin (content_tsv);
            """;

        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
        logger.LogInformation("pgvector şeması hazır (ai.documents + hnsw/gin index).");
    }

    public async Task UpsertChunksAsync(
        string source,
        IReadOnlyList<(int index, string content, float[] embedding)> chunks,
        CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO ai.documents (doc_id, chunk_index, content, embedding, source)
            VALUES (@doc_id, @chunk_index, @content, @embedding, @source)
            ON CONFLICT (doc_id, chunk_index) DO UPDATE
              SET content = EXCLUDED.content,
                  embedding = EXCLUDED.embedding,
                  source = EXCLUDED.source,
                  updated_at = now();
            """;

        // doc_id, kaynak adından deterministik olarak türetilir: aynı kaynak yeniden ingest
        // edildiğinde satırlar güncellenir (kopya oluşmaz).
        var docId = DeterministicDocId(source);

        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        foreach (var (index, content, embedding) in chunks)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("doc_id", docId);
            command.Parameters.AddWithValue("chunk_index", index);
            command.Parameters.AddWithValue("content", content);
            command.Parameters.AddWithValue("embedding", new Vector(embedding));
            command.Parameters.AddWithValue("source", source);
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        logger.LogInformation("{ChunkCount} chunk '{Source}' kaynağına upsert edildi.", chunks.Count, source);
    }

    public async Task<IReadOnlyList<RagHit>> HybridSearchAsync(
        float[] queryEmbedding,
        string query,
        int topK,
        double minScore,
        CancellationToken ct)
    {
        // Hibrit harman: vektör (0.7) + anahtar kelime (0.3). FULL OUTER JOIN iki kümenin
        // birleşimini alır; sadece vektör skoru eşiği geçenler tutulur (minScore filtresi).
        const string sql =
            """
            WITH vec AS (
              SELECT id, 1 - (embedding <=> @query_embedding) AS score
              FROM ai.documents ORDER BY embedding <=> @query_embedding LIMIT 20
            ),
            kw AS (
              SELECT id, ts_rank(content_tsv, websearch_to_tsquery('simple', @query)) AS score
              FROM ai.documents WHERE content_tsv @@ websearch_to_tsquery('simple', @query) LIMIT 20
            )
            SELECT d.id, d.content, COALESCE(v.score,0)*0.7 + COALESCE(k.score,0)*0.3 AS hybrid_score
            FROM vec v FULL OUTER JOIN kw k USING (id)
            JOIN ai.documents d ON d.id = COALESCE(v.id, k.id)
            WHERE COALESCE(v.score,0) > @min_score
            ORDER BY hybrid_score DESC LIMIT @top_k;
            """;

        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("query_embedding", new Vector(queryEmbedding));
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("min_score", minScore);
        command.Parameters.AddWithValue("top_k", topK);

        var hits = new List<RagHit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt64(0);
            var content = reader.GetString(1);
            // hybrid_score numeric/double precision dönebilir → kültürden bağımsız güvenli okuma.
            var score = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
            hits.Add(new RagHit(id, content, score));
        }

        return hits;
    }

    // MD5 → 16 bayt → Guid. Deterministik ve hızlı; kriptografik amaç taşımaz.
    private static Guid DeterministicDocId(string source) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(source)));
}
