using System.Text;
using System.Text.RegularExpressions;

namespace EvolutionaryLogistics.Gateway.Middleware;

/// <summary>
/// PII maskeleme + model routing. Gateway, LLM'e giden tüm trafiğin tek chokepoint'i olduğu
/// için maskeleme burada (tek yerde) yapılır — her serviste tekrar etmez.
///
/// NEDEN GATEWAY: LLM çağrısı üçüncü tarafa gider. PII'yi modele göndermeden önce
/// maskelemek hem KVKK/GDPR hem de "prompt logunda PII" riskini kapatır.
///
/// GOTCHA: Maskeleme regex-tabanlıdır; tam isim/adres gibi serbest metin PII'sini
/// yakalayamaz. Gerçek çözüm NER/model tabanlı DLP'dir. Bu katman savunma derinliğidir,
/// tek başına yeterli değildir.
/// </summary>
public static partial class PiiMasker
{
    // Sıra önemli: daha spesifik (IBAN/kart) desenler önce uygulanır ki
    // genel "11 hane" deseni onları parçalamasın.
    private static readonly (string Tag, Regex Pattern)[] Patterns =
    [
        // IBAN benzeri: 2 harf + 2 hane + 10-30 alfanumerik
        ("IBAN", new Regex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{10,30}\b", RegexOptions.Compiled)),
        // Kart no: 13-16 hane, ayraçlı olabilir
        ("CARD", new Regex(@"\b(?:\d[ -]*?){13,16}\b", RegexOptions.Compiled)),
        // E-posta
        ("EMAIL", new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled)),
        // TCKN benzeri: tam 11 hane (kart/IBAN'dan sonra)
        ("ID", new Regex(@"\b\d{11}\b", RegexOptions.Compiled))
    ];

    /// <summary>
    /// Metindeki PII'yi <c>[TAG_n]</c> token'larıyla değiştirir. Reverse map, cevapta
    /// geri-mape (opsiyonel) için döndürülür — trade-off: tablo tutmak sızıntı yüzeyi açar.
    /// </summary>
    public static string Mask(string text, out IReadOnlyDictionary<string, string> reverseMap)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = text;

        foreach (var (tag, pattern) in Patterns)
        {
            result = pattern.Replace(result, match =>
            {
                var original = match.Value;
                var token = $"[{tag}_{map.Count + 1}]";
                map[token] = original;
                return token;
            });
        }

        reverseMap = map;
        return result;
    }
}

public enum PromptComplexity
{
    Simple,
    Complex
}

/// <summary>
/// AI rotasına gelen istek gövdesindeki PII'yi maskeler ve model routing header'ı ekler.
/// </summary>
public sealed class PiiMaskingMiddleware(
    RequestDelegate next,
    ILogger<PiiMaskingMiddleware> logger)
{
    public const string ModelHeader = "X-Model";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/ai", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context);

        if (!string.IsNullOrWhiteSpace(body))
        {
            var masked = PiiMasker.Mask(body, out var reverseMap);
            if (reverseMap.Count > 0)
            {
                logger.LogInformation("PII masked request path={Path} entities={Count}", context.Request.Path, reverseMap.Count);
            }

            var bytes = Encoding.UTF8.GetBytes(masked);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;

            context.Request.Headers[ModelHeader] = ClassifyComplexity(body) switch
            {
                PromptComplexity.Complex => "gpt-4o",
                _ => "gpt-4o-mini"
            };
        }

        await next(context).ConfigureAwait(false);
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return string.Empty;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(
            context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        context.Request.Body.Position = 0;
        return body;
    }

    /// <summary>
    /// Basit karmaşıklık sınıflandırması: uzun/çok soru işaretli/yönlendirme içeren prompt → büyük model.
    /// GOTCHA: "her şeyi büyük modele gönder" en pahalı hatadır; tersini yapmak da
    /// karmaşık akıl yürütmeyi bozar. Eşik, gerçek trafikte ölçülerek ayarlanır.
    /// </summary>
    private static PromptComplexity ClassifyComplexity(string body)
    {
        var prompt = ExtractPrompt(body);
        var questionMarks = prompt.Count(c => c == '?');

        return prompt.Length > 400 || questionMarks > 1
            ? PromptComplexity.Complex
            : PromptComplexity.Simple;
    }

    private static readonly Regex PromptFieldRegex = new(
        "\"prompt\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled);

    private static string ExtractPrompt(string json)
    {
        var match = PromptFieldRegex.Match(json);
        return match.Success ? match.Groups[1].Value : json;
    }
}

public static class PiiMaskingExtensions
{
    public static IApplicationBuilder UsePiiMasking(this IApplicationBuilder app)
        => app.UseMiddleware<PiiMaskingMiddleware>();
}
