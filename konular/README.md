# Kurumsal .NET ileri seviye eğitim materyali

Bu katalog, uzman .NET katılımcılarına yönelik 20 bağımsız HTML oturumundan oluşur. Önerilen sıra platform ve servis sınırlarından başlayıp domain tasarımı, dağıtık mesajlaşma, performans/operasyon ve AI güvenliğiyle tamamlanır. Her sayfa sahnede izlenebilir anlatım akışı, mimari karar gerekçesi, trade-off'lar, proje bağlantıları ve **Tartışma** bölümü içerir; konuya göre kod, akış, hata senaryosu ve üretim notları eklenmiştir. Dakika bazlı süre etiketleri ve quiz/sınav bölümleri kullanılmaz.

Tüm sayfalar açık renk tema kullanır. `egitim.css` yerel ve haricî runtime bağımlılığı olmadan yüklenir; HTML'de çevrimdışı kullanımı engelleyen JavaScript veya CDN yoktur. Üst/alt gezinme bağlantıları yerel dosyalara göredir. Kod parçaları bağlamına göre “kavramsal”, “repoda mevcut” veya “çalıştırılabilir” olarak okunmalıdır; tamamı bağımsız derlenebilir örnek değildir. Sunumda kaynak dosyaya gitmeden önce kodun proje içindeki güncel halini kontrol edin.

- [01-dotnet10-csharp14-aspnetcore10.html](./01-dotnet10-csharp14-aspnetcore10.html), .NET SDK/Runtime/deployment ayrımını açıklamalı SVG ile gösterir ve kullanıcının talep ettiği 10 C# 14 özelliğini ayrı, problem-odaklı alt başlıklarda ele alır. Derleyici sözdizimi örnekleri eğitim snippet'idir; solution build bu bağımsız snippet'lerin hepsini derlemez.
- [02-mikroservisler-sinirlar.html](./02-mikroservisler-sinirlar.html), gateway, servisler, RabbitMQ/MassTransit Saga, Kafka telemetry, PostgreSQL/pgvector, AI ve observability bağlantılarını SVG üzerinde gösterir. Dolu ok yapılandırılmış/kodlanmış bağlantıyı, kesik turuncu ok tamamlanmamış entegrasyonu gösterir; her ürünün rolü ve sınırı şeklin altında açıklanır.
- 03–20 arasındaki oturumların her birinde **Kod yürüyüşü** bölümü bulunur. Bölüm, ilgili repository dosyasından kod/konfigürasyon kesitiyle problemi ve veri/istek akışını anlatır; eksik entegrasyon, demo-only kod ve temsili laboratuvar örneklerini çalışan üretim davranışından açıkça ayırır. Yan menüdeki bağlantı doğrudan bu bölüme gider.

## Sıra ve kapsam

| # | Dosya | Kapsam | Repodaki karşılık |
|---|---|---|---|
| 01 | [01-dotnet10-csharp14-aspnetcore10.html](./01-dotnet10-csharp14-aspnetcore10.html) | .NET 10, Runtime/SDK/TFM, C# 14, ASP.NET Core 10 | `Directory.Build.props`, .NET 10 solution |
| 02 | [02-mikroservisler-sinirlar.html](./02-mikroservisler-sinirlar.html) | Microservices, servis ayrıştırma, bounded context, strangler | Order/Inventory ve servis topolojisi |
| 03 | [03-katmanli-mimari.html](./03-katmanli-mimari.html) | Katmanlı/clean/hexagonal mimari, dependency rule, DI | Order/Inventory Domain/Application/Infrastructure/API |
| 04 | [04-ddd-event-sourcing.html](./04-ddd-event-sourcing.html) | DDD, aggregate, domain/integration event, Event Sourcing | Inventory event sözleşmesi; tam Event Store yok |
| 05 | [05-cqrs.html](./05-cqrs.html) | CQRS, query/read model, MediatR pipeline, cache consistency | Inventory query ve CacheAsideBehavior |
| 06 | [06-http-grpc-yarp.html](./06-http-grpc-yarp.html) | HTTP API, gRPC, protobuf, YARP, deadline/retry | API Gateway, gRPC shared pipeline |
| 07 | [07-mesajlasma-brokerleri.html](./07-mesajlasma-brokerleri.html) | Event-driven architecture, RabbitMQ, MassTransit, Azure Service Bus, Kafka | RabbitMQ/MassTransit Orchestrator ve Kafka telemetry; Azure Service Bus yok |
| 08 | [08-outbox-idempotency.html](./08-outbox-idempotency.html) | Outbox/Inbox, at-least-once, idempotent consumer, dual-write | MassTransit EF Outbox, telemetry upsert |
| 09 | [09-saga-process-manager.html](./09-saga-process-manager.html) | Saga orchestration/choreography, Process Manager, compensation | OrderFulfillmentSaga state machine |
| 10 | [10-dagitik-dayaniklilik.html](./10-dagitik-dayaniklilik.html) | Timeout, retry, circuit breaker, bulkhead, dağıtık işlem yönetimi | gRPC ve MassTransit retry; tam circuit-breaker yok |
| 11 | [11-dotnet-performans-memory-async.html](./11-dotnet-performans-memory-async.html) | Memory/GC, Span/Memory, async/await, Channel, ArrayPool | Kafka telemetry consumer/processor |
| 12 | [12-efcore-veritabani.html](./12-efcore-veritabani.html) | EF Core 10 query/transaction/concurrency/migration optimizasyonu | OutboxDbContext, Npgsql telemetry store |
| 13 | [13-redis-caching.html](./13-redis-caching.html) | Cache-aside, Redis, invalidation, stampede, quota | Shared cache behavior, Redis gateway quota |
| 14 | [14-observability-logging-tracing.html](./14-observability-logging-tracing.html) | Structured logging, Serilog, Graylog, Sentry, OpenTelemetry tracing | Shared.Observability, Collector, Jaeger/Graylog compose |
| 15 | [15-metrik-slo-aspire.html](./15-metrik-slo-aspire.html) | Metrics, health checks, SLI/SLO, error budget, .NET Aspire | Prometheus/Grafana/OTel; Aspire AppHost yok |
| 16 | [16-profiling-benchmark-yuk-testi.html](./16-profiling-benchmark-yuk-testi.html) | Profiling, BenchmarkDotNet, load/stress/spike/soak testing | Telemetry ölçüm noktaları; benchmark/test projesi yok |
| 17 | [17-ai-llm-platform.html](./17-ai-llm-platform.html) | Microsoft.Extensions.AI, Semantic Kernel, LLM gateway, model routing | Semantic Kernel AI server, YARP AI middleware |
| 18 | [18-rag-vektor-arama.html](./18-rag-vektor-arama.html) | RAG ingestion, chunking, embeddings, pgvector, hybrid/vector search | PgVectorDocumentStore, RagRetriever |
| 19 | [19-mcp-agent-mimarisi.html](./19-mcp-agent-mimarisi.html) | MCP, tools, multi-agent trade-off ve yetkilendirme | AiMcpServer, MCP tools ve guardrail filter |
| 20 | [20-ai-guvenlik-kvkk-kalite-maliyet.html](./20-ai-guvenlik-kvkk-kalite-maliyet.html) | Prompt architecture, AI quality/evaluation, security, KVKK/GDPR, cost | PII masking, token quota, tool authorization |

## Proje ve örneklerin durumu

- Mevcut solution `.NET 10` (`net10.0`) ve `LangVersion=14` hedefliyor. Bu dokümantasyon mevcut projeyi .NET 6'dan yükseltmez; hedef platform anlatımını mevcut çözümle uyumlu tutar.
- Makine üzerinde `.NET SDK 10.0.401`, ASP.NET Core/.NET runtime `10.0.12` kurulu olarak görüldü. Kök [README.md](../README.md) SDK 10.0.402 hedeflediğini yazıyor ve `global.json` bulunmuyor; SDK pin'i ile yazılı hedef aynı değildir.
- Solution build doğrulaması: `dotnet build EvolutionaryLogistics.slnx --no-restore` — bu revizyon sonrasında başarılı, 0 uyarı, 0 hata. Solution build, bağımsız HTML snippet'lerini derleyen kontrol değildir; container servisleri, dış AI provider, mesaj teslimatı, yük testi veya üretim hazır olma kanıtı da değildir.
- Bu revizyondaki statik HTML kontrolü 20 sayfa, 2 açıklamalı SVG ve 306 yerel dosya/anchor linkini kapsar. 03–20 sayfalarında 18 adet `Kod yürüyüşü` bağlantısı doğrulandı; bozuk hedef, eksik anchor, yinelenen ID, eksik `Tartışma` başlığı veya dakika etiketi bulunmadı. 02 servis topolojisi ve 13 kod anlatımı tarayıcıda görsel olarak kontrol edildi.
- C# 14 dil örnekleri geçici bir `net10.0` / `LangVersion=14.0` smoke-test projesinde derlendi: başarılı, 0 uyarı, 0 hata. File-based örnek `dotnet run --file ...` ile çalıştırıldı ve beklenen çıktıyı verdi. Geçici test projesi teslimattan önce silindi. Bu kontroller tüm HTML kod bloklarının derlendiği veya repository'nin iş akışlarının doğrulandığı anlamına gelmez; `#:include`/paket restore senaryoları ayrıca sınanmadı.
- Bu doğrulama turunda `dotnet test` çalıştırılmadı; solution içinde test projesi listelenmiyor. Kaynak kodun derlenmesi test senaryosu yürütüldüğü anlamına gelmez.
- Yerel broker/veritabanı laboratuvarları için Docker Compose gerekir. Ana altyapı başlangıç komutu kök README'de verilmiştir; servisler için gerekli secret/config ve container health kontrol edilmelidir. RabbitMQ/Kafka/PostgreSQL/Redis/observability topolojisi eğitim amaçlı single-node/yerel ayarlara sahiptir; production HA değildir.
- AI modeli çalıştırmak için `AI:ApiKey` ve opsiyonel `AI:Endpoint`/model ayarları gerekir. Dış sağlayıcı hesabı/ağ erişimi olmadan gerçek LLM/embedding çağrıları doğrulanmamıştır. AI query servislerindeki bazı order/inventory verileri in-memory demo verisidir.
- RAG için pgvector uzantılı PostgreSQL image'i, embedding provider ve proje config'i gerekir. Retrieval kalite metriği, evaluation dataset'i veya model kalitesi bu build sırasında doğrulanmamıştır.
- Azure Service Bus ve .NET Aspire AppHost bu solution'da yoktur. Bu başlıklar mimari rehber/laboratuvar yönlendirmesidir; mevcut entegrasyon gibi sunulmaz. BenchmarkDotNet ve load test projesi de eklenmemiştir.
- Repo README'si YARP 3.12.1 eğitim hedefinden söz ederken [ApiGateway.csproj](../src/Gateway/ApiGateway/ApiGateway.csproj) `Yarp.ReverseProxy` 2.3.0 pin'liyor. Eğitim sırasında package file gerçek paket sürümü için kaynaktır; ikisi aynı sürüm iddiası değildir.
- Kod incelemesi bazı üretim risklerini de işaretler: Kafka consumer offset'i kalıcı Npgsql batch yazımından önce commit ettiği için process/store failure veri kaybı yaratabilir ve mevcut “at-least-once” yorumu bu akışta garanti değildir; AI gateway tenant quota okuma/yazma işlemi atomik rezervasyon değildir ve Redis hatasında fail-open olur; AI PII masking regex kapsamı sınırlıdır; RAG retrieval hatası boş sonuç olarak degrade olur; Orchestrator migration'ı startup'ta çalıştırır. İlgili oturumlarda bu davranışlar örnek ve tasarım riski olarak anlatılmıştır, üretim garantisi sayılmamıştır.

## Çalıştırma ve kullanım

1. Önce solution kökünde `dotnet build EvolutionaryLogistics.slnx` çalıştırın.
2. İlgili oturumun proje bağlantılarını ve “repoda olmayan” notlarını dersten önce açıp gerçek kodla karşılaştırın.
3. Broker/veritabanı örneklerinde README'deki Compose komutlarını ve servis health durumunu kullanın; port/secret çakışmalarını yerel ortama göre yönetin.
4. AI/RAG uygulamasında secret'ları kaynak koda koymayın; user data veya production traffic'i eğitim denemelerine taşımayın.
5. Altyapı gerektiren örneği yerel compose, test provider, dış API veya preview SDK'dan hangisine bağlıysa önceden açıkça belirtin. Doğrulanmamış bir kod parçasını “derlenmiş/çalışır” diye tanıtmayın.

## Sürüm kaynakları

Sürüm duyarlı platform bilgileri 18 Ekim 2026 tarihinde resmi Microsoft dokümantasyonuyla kontrol edildi. Paket sürümleri için repository `.csproj` dosyaları, gelecekteki güncellemelerde ise resmî release notes/NuGet uyumluluk matrisi izlenmelidir.

- [.NET 10 overview](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)
- [C# 14 what's new](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14)
- [ASP.NET Core 10 release notes](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0)
- [EF Core 10 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
- [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/get-started/what-is-aspire)
- [MCP specification](https://modelcontextprotocol.io/specification/)
