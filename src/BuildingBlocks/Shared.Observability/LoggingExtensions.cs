using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Graylog;
using Serilog.Sinks.Graylog.Core.Transport;

namespace EvolutionaryLogistics.BuildingBlocks.Observability;

/// <summary>
/// Serilog + Sentry bootstrap'i. Gün 3 Lab 7/8: her log satırında W3C <c>trace_id</c>
/// olur → Graylog'da aynı trace_id ile Jaeger span'i eşleştirilebilir (cross-signal).
///
/// Kullanım (web ve worker aynı API):
/// <code>
/// builder.AddEvolutionarySerilog("inventory-service");   // Console (Compact JSON) + GELF
/// builder.AddEvolutionarySentry("inventory-service", "inventory@1.4.2");
/// </code>
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Serilog'u kurar: structured JSON console + opsiyonel GELF (Graylog).
    /// <c>Graylog:Endpoint</c> tanımlı değilse GELF sink atlanır (yerel geliştirme gürültüsüz).
    /// </summary>
    public static IHostApplicationBuilder AddEvolutionarySerilog(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        var config = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            // W3C TraceId/SpanId → her log satırına. YOKSA cross-signal korelasyon imkânsız.
            .Enrich.WithSpan()
            .WriteTo.Console(new CompactJsonFormatter());

        var graylogEndpoint = builder.Configuration["Graylog:Endpoint"];
        if (Uri.TryCreate(graylogEndpoint, UriKind.Absolute, out var gelfUri))
        {
            config.WriteTo.Graylog(new GraylogSinkOptions
            {
                HostnameOrAddress = gelfUri.Host,
                Port = gelfUri.Port,
                TransportType = TransportType.Udp,
                Facility = serviceName
            });
        }

        Log.Logger = config.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }

    /// <summary>
    /// Sentry'yi (Relay'e) bağlar. <c>Sentry:Dsn</c> yoksa no-op.
    /// GOTCHA: default PII taşır (request body, user). <c>SetBeforeSend</c> ile scrub edilir.
    /// Her 4xx'i göndermeyin — Sentry gürültüye boğulur, gerçek regresyon gizlenir.
    /// </summary>
    public static IHostApplicationBuilder AddEvolutionarySentry(
        this IHostApplicationBuilder builder,
        string serviceName,
        string release)
    {
        var dsn = builder.Configuration["Sentry:Dsn"];
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return builder;
        }

        var handle = SentrySdk.Init(options =>
        {
            options.Dsn = dsn;
            options.Environment = builder.Environment.EnvironmentName;
            options.Release = release;

            // Perf trace örnekleme — Jaeger'a ek ikinci sinyal. Düşük tutun (maliyet + hacim).
            options.TracesSampleRate = 0.10;
            options.SendDefaultPii = false;

            options.SetBeforeSend(e =>
            {
                // PII scrub — Sentry event'ine kişisel veri sızmasın.
                e.ServerName = null!;
                e.User = null!;
                return e;
            });
        });

        // Host kapanışında Sentry'yi flush et — aksi halde son event'ler kaybolur.
        builder.Services.AddSingleton(handle);

        return builder;
    }
}
