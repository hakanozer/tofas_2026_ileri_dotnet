using EvolutionaryLogistics.BuildingBlocks.Observability;
using EvolutionaryLogistics.Gateway.Auth;
using EvolutionaryLogistics.Gateway.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Gün 3: Structured logging (Console Compact JSON + GELF→Graylog) + Sentry + OTel ──
// Gateway tüm trafiği gördüğü için trace'in kökü burada üretilir; sampler kararı
// ParentBased ile downstream'e taşınır.
builder.AddEvolutionarySerilog("api-gateway");
builder.AddEvolutionarySentry("api-gateway", "api-gateway@1.0.0");
builder.Services.AddEvolutionaryObservability("api-gateway");

// ── Gün 4: LLM token kotası için Redis (kaynak-of-truth — replika başına bellek DEĞİL) ──
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<ITenantQuotaStore, RedisTenantQuotaStore>();

builder.Services.AddGatewayAuth(builder.Configuration);

var yarpPath = builder.Configuration["ReverseProxy:ConfigPath"] ?? "yarp.json";
builder.Configuration.AddJsonFile(yarpPath, optional: false, reloadOnChange: true);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// TLS sonlandıran reverse proxy arkasında Request.IsHttps doğru olsun diye (yalnızca açıkça etkinse).
if (builder.Configuration.GetValue<bool>("Auth:TrustForwardedHeaders"))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

await app.MigrateAuthDatabaseAsync();

app.UseSerilogRequestLogging();
app.UseTraceIdResponse();
app.UseAuthentication();
app.UseAuthorization();
app.UseCorrelationAndClaimInjection();
// ── Gün 2: Rate limiting proxy pipeline'a kaydedilir. GOTCHA: IReverseProxyFeature
// yalnızca proxy endpoint'i içinde doldurulur; bu yüzden burada (ana pipeline'da)
// route metadata okunamaz — middleware MapReverseProxy pipeline'ında çalışmalı. ──
// ── Gün 4: PII maskeleme → token bütçesi → proxy. Sıra önemli: maskeleme proxy'den
// ÖNCE olmalı, aksi halde ham PII downstream'e (ve LLM'e) gider. ──
app.UsePiiMasking();
app.UseLlmTokenBudget();
app.MapReverseProxy(pipeline =>
{
    pipeline.UseMiddleware<GatewayRateLimitingMiddleware>();
});

app.MapAuthEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "Evolutionary Logistics API Gateway",
    yarp = "3.12.1",
    routes = new[] { "/api/orders", "/api/inventory", "/api/ai", "/api/auth" }
}));

app.Run();
