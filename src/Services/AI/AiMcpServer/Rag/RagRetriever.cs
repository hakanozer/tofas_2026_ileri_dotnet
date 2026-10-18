using EvolutionaryLogistics.BuildingBlocks.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;

// SK 1.81'de ITextEmbeddingGenerationService obsolete olarak işaretli (yeni yol:
// Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>). Lab sözleşmesi
// ITextEmbeddingGenerationService kullanmamızı istediği için uyarı bilinçli olarak bastırıldı.
#pragma warning disable CS0618

namespace EvolutionaryLogistics.Services.Ai.Rag;

/// <summary>
/// Hibrit RAG getirici: soruyu embedding'e çevirir (önce cache), pgvector'de vektör + anahtar
/// kelime aramasını harmanlar. Embedding servisi yoksa/başarısızsa <b>boş liste</b> döner —
/// üst katman uydurmak yerine "Bu bilgi elimde yok" demek zorundadır.
/// </summary>
public sealed class RagRetriever(
    ITextEmbeddingGenerationService embeddings,
    EmbeddingCache cache,
    PgVectorDocumentStore store,
    ILogger<RagRetriever> logger)
{
    public async Task<IReadOnlyList<RagHit>> RetrieveAsync(string question, CancellationToken ct)
    {
        try
        {
            var vector = await cache.TryGetAsync(question);
            if (vector is null)
            {
                var memory = await embeddings.GenerateEmbeddingAsync(question, cancellationToken: ct);
                vector = memory.ToArray();
                await cache.SetAsync(question, vector);
            }

            var hits = await store.HybridSearchAsync(vector, question, topK: 5, minScore: 0.35, ct);
            ObservabilityNames.RetrievalTopScore.Record(hits.Count > 0 ? hits[0].Score : 0d);
            return hits;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RAG getirimi başarısız; bağlamsız yanıt üretilecek.");
            return [];
        }
    }
}
