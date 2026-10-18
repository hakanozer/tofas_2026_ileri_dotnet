using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EvolutionaryLogistics.Services.Ai.Rag;

/// <summary>
/// Embedding önbelleği. Aynı metin için tekrar embedding üretmek pahalıdır (token + gecikme);
/// Redis'e SHA-256 tabanlı anahtarla ikili (binary) olarak yazılır. TTL 30 gündür.
/// Redis erişilemezse <b>sessizce degrade edilir</b>: retrieval yine çalışır, sadece cache ıskalanır.
/// </summary>
public sealed class EmbeddingCache(IConnectionMultiplexer redis, ILogger<EmbeddingCache> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public async Task<float[]?> TryGetAsync(string text)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(KeyFor(text));
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var bytes = (byte[]?)value;
            return bytes is null ? null : MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding cache okunamadı; önbellek atlanıyor.");
            return null;
        }
    }

    public async Task SetAsync(string text, float[] vector)
    {
        try
        {
            // Span'ı await boyunca TUTMA (CS4007) — önce diziye kopyala.
            var bytes = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
            await redis.GetDatabase().StringSetAsync(KeyFor(text), bytes, Ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding cache yazılamadı; sessizce yok sayıldı.");
        }
    }

    private static string KeyFor(string text) =>
        "embed:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
