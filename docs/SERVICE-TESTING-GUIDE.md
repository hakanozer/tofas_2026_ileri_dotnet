# Servis Test & Tetikleme Rehberi

> **Evolutionary Logistics & Telemetry Platform** — Gün 3 (Performans & Gözlemlenebilirlik) + Gün 4 (AI Entegrasyonu)
>
> Bu doküman, sistemdeki HTTP servislerinin **nasıl tetikleneceğini**, **hangi tür veriyi aldığını** ve **ne tür veri döndürdüğünü** açıklar. Yanında çalışan Postman koleksiyonu: [`postman/EvolutionaryLogistics.postman_collection.json`](postman/EvolutionaryLogistics.postman_collection.json)

---

## 1. Ön koşul — Servisleri ayağa kaldırma

```bash
docker compose up -d --build
docker compose ps          # 17 konteyner ayakta olmalı
```

Container durumları `healthy` olana kadar bekleyin (özellikle `elp-postgres`, `elp-redis`, `elp-kafka`). Ardından Postman'de koleksiyonu **Import** edin.

**Koleksiyon değişkenleri** (Import sonrası host'unuza göre düzenleyin):

| Değişken | Varsayılan | Açıklama |
|---|---|---|
| `gateway` | `http://localhost:8080` | API Gateway (YARP) |
| `orderDirect` | `http://localhost:8081` | Order servisi (doğrudan) |
| `inventoryDirect` | `http://localhost:8082` | Inventory servisi (doğrudan) |
| `aiDirect` | `http://localhost:8083` | AI MCP sunucusu (doğrudan) |
| `tenantId` | `tenant-42` | LLM token kotası için tenant kimliği |
| `sku` | `SKU-1234` | Örnek stok kodu |
| `orderId` | `ORD-10023` | Örnek sipariş numarası |

---

## 2. Port haritası

| Servis | Port | HTTP ucu var mı? | Rol |
|---|---|---|---|
| ApiGateway | 8080 | ✅ `/`, proxy rotaları | Reverse proxy + middleware chokepoint |
| Order.Api | 8081 | ✅ `/health`, `/` | Sipariş servisi (Gün 1 lab'ları buraya eklenir) |
| Inventory.Api | 8082 | ✅ `/health` | Envanter servisi (gRPC pipeline kayıtlı) |
| AiMcpServer | 8083 | ✅ `/health`, `/api/assistant/ask`, `/api/rag/ingest`, `/mcp` | SK + MCP + pgvector RAG |
| TelemetryStreamProcessor | — | ❌ | Kafka consumer (Gün 3) |
| BackgroundOrchestrator | — | ❌ | RabbitMQ/MassTransit worker |

---

## 3. İki test katmanı

| | Doğrudan servis | Gateway üzerinden |
|---|---|---|
| Hedef port | 8081 / 8082 / 8083 | 8080 |
| Middleware | ❌ yok | ✅ rate limit + PII maskeleme + token kotası + correlation |
| Kullanım | İzole davranış testi, sorun ayıklama | Gerçek üretim yolu, uçtan uca test |
| Auth | yok | Proxy rotalarında zorunlu değil (aşağıya bak) |

---

## 4. Gateway (ApiGateway · :8080)

### 4.1 Kendi endpoint'i

`GET /` → Gateway process'inin kimliğini döner.

```json
{
  "service": "Evolutionary Logistics API Gateway",
  "yarp": "3.12.1",
  "routes": ["/api/orders", "/api/inventory", "/api/ai"]
}
```

### 4.2 YARP route tablosu

| Route | Gelen yol | Transform | Cluster / LB | Rate limit policy | AuthRequired |
|---|---|---|---|---|---|
| `orders-api` | `/api/orders/{**}` | `PathRemovePrefix /api/orders` | order-cluster / PowerOfTwoChoices | `write-heavy` | true |
| `orders-read` | `/api/read/orders/{**}` | remove `/api/read/orders` + prefix `/read` | order-cluster | `read-heavy` | true |
| `inventory-api` | `/api/inventory/{**}` | `PathRemovePrefix /api/inventory` | inventory-cluster / RoundRobin | `read-heavy` | true |
| `inventory-grpc-bridge` | `/grpc/inventory/{**}` | `PathRemovePrefix /grpc/inventory` | inventory-cluster | `write-heavy` | true |
| `ai-api` | `/api/ai/{**}` | remove `/api/ai` + ekler `X-LLM-Gateway: enabled` | ai-cluster / LeastRequests | `ai-token-budget` | true |
| `health-aggregate` | `/health/{**}` | `PathRemovePrefix /health` | order-cluster | `anonymous-burst` | false |

### 4.3 Gateway'den geçen çağrı örnekleri

| İstek | Downstream'e giden | Beklenen yanıt |
|---|---|---|
| `GET /api/orders/health` | Order `GET /health` | `{ "status":"healthy", "context":"orders" }` |
| `GET /api/read/orders/health` | Order `GET /read/health` | **404** — Order'da `/read/*` ucu henüz yok (route doğru eşleşir) |
| `GET /api/inventory/health` | Inventory `GET /health` | `{ "status":"healthy", "context":"inventory" }` |
| `GET /api/ai/health` *(X-Tenant-Id zorunlu)* | AI `GET /health` | `{ "status":"healthy", "service":"ai-mcp-server" }` |
| `GET /health/health` | Order `GET /health` | `{ "status":"healthy", "context":"orders" }` |
| `POST /grpc/inventory/*` | Inventory `/grpc` yoluna | **404** — `MapGrpcService` hâlâ yorumda (Gün 2 labı) |

> **Gotcha — `health-aggregate`:** `{**catch-all}` sıfır segmenti de eşleştirir. Bu yüzden `GET /health` (tek segment) isteği prefix silindikten sonra Order servisinin **köküne** (`/`) düşer ve `{ "message": "Order service …" }` döner. Gerçek sağlık gövdesini almak için `/health/health` kullanın.

> **Gotcha — AI yolunda çift `api` öneki:** `ai-api` route'u `PathRemovePrefix /api/ai` uygular; yani **gateway yolu = `/api/ai` + AI servisinin kendi yolu**. AI servisindeki uçların hepsi `/api/...` ile başladığı için gateway'den erişirken yol iki kez `api` içerir:
>
> | AI servisindeki uç | Gateway'den çağrı |
> |---|---|
> | `GET /health` | `GET /api/ai/health` |
> | `POST /api/assistant/ask` | `POST /api/ai/api/assistant/ask` |
> | `POST /api/rag/ingest` | `POST /api/ai/api/rag/ingest` |
>
> `/api/ai/assistant/ask` yazarsanız downstream'e `/assistant/ask` gider ve `404` döner. Bu tutarsızlığı gidermek için ya AI uçlarını `/assistant/ask` biçimine çekin ya da `ai-api` route'undan `PathRemovePrefix` dönüşümünü kaldırın.

### 4.4 Middleware zinciri (sıra önemli)

```
UseSerilogRequestLogging
  → UseAuthentication
  → UseAuthorization
  → UseCorrelationAndClaimInjection   // correlation id üretir + JWT claim'lerini header'a çevirir
  → UsePiiMasking                     // /api/ai gövdesindeki PII'yi maskeler, X-Model ekler
  → UseLlmTokenBudget                 // /api/ai için X-Tenant-Id zorunlu + kota
  → MapReverseProxy
       └─ GatewayRateLimitingMiddleware  // route metadata → policy
```

**PII maskeleme (`PiiMaskingMiddleware`)** — yalnızca `/api/ai/*` yolunda çalışır. Şu desenleri `[TAG_n]` token'larıyla değiştirir: IBAN, kredi kartı, e-posta, 11 haneli TCKN. Prompt uzunluğuna göre `X-Model` header'ı ekler (`gpt-4o-mini` / `gpt-4o`).

**LLM token kotası (`LlmTokenBudgetMiddleware`)** — `/api/ai/*` yolunda:
- `X-Tenant-Id` yoksa → `400 Bad Request` (`X-Tenant-Id header zorunlu.`)
- Kota bittiyse → `429 Too Many Requests` + `Retry-After: 60` (model hiç çağrılmaz)
- Yanıt gövdesi byte'ları sayılır (`chars/4` tahmini) ve Redis'teki günlük sayaçtan düşülür

**Rate limiting policy'leri** (partition = tenant header → JWT claim → remote IP):

| Policy | Token limiti | Dolum | Kuyruk | Kullanan rotalar |
|---|---|---|---|---|
| `read-heavy` | 500 | 500/sn | 100 | inventory-api, orders-read |
| `write-heavy` | 120 | 120/sn | 30 | orders-api, grpc-bridge |
| `ai-token-budget` | 30 | 30/sn | 5 | ai-api |
| `anonymous-burst` | 60 | 60/sn | 0 | health-aggregate |

Limit aşımı → `429` + `Retry-After: 1`. Başarılı yanıtta `X-RateLimit-Policy` header'ı döner.

### 4.5 Auth notu

Proxy rotalarında `RequireAuthorization()` **yok**; token göndermeden test edilebilir. JWT yapılandırması (`Jwt__Authority`) örnek bir Keycloak adresine bakar.

> **Güvenlik gözlemi (eğitim notu):** `CorrelationAndClaimInjectionMiddleware`, `X-User-Roles` / `X-Tenant-Id` gibi header'ları yalnızca **authenticated** isteklerde claim'den üzerine yazar. Anonim isteklerde istemcinin gönderdiği değer downstream'e **olduğu gibi** geçer. Üretimde bu header'lar gateway'de koşulsuz temizlenmelidir (header spoofing).

---

## 5. Order Service (Order.Api · :8081)

| Endpoint | Girdi | Çıktı |
|---|---|---|
| `GET /health` | — | `{ "status":"healthy", "context":"orders" }` |
| `GET /` | — | `{ "message":"Order service — Day 1 ES/CQRS labs land here" }` |

Order servisi bugün yalnızca iskelet uçları barındırır. ES/CQRS (event sourcing, aggregate, projection) uçları **Gün 1 laboratuvarlarında** buraya eklenir; gateway'deki `orders-read` rotası o zaman anlam kazanır.

---

## 6. Inventory Service (Inventory.Api · :8082)

| Endpoint | Girdi | Çıktı |
|---|---|---|
| `GET /health` | — | `{ "status":"healthy", "context":"inventory" }` |

gRPC server/client pipeline'ı (`AddEvolutionaryGrpcServerPipeline` / `AddEvolutionaryGrpcClientPipeline`) kayıtlıdır; ancak `MapGrpcService<InventoryGrpcService>()` henüz yorumdadır. Bu yüzden `/grpc/inventory/*` yolları şu an `404` döner.

---

## 7. AI MCP Server (AiMcpServer · :8083)

### 7.1 GET `/health`

```json
{ "status": "healthy", "service": "ai-mcp-server" }
```

`AI__ApiKey` boş olsa bile servis ayakta kalır — LLM servisleri kayıt edilmez ve `/api/assistant/ask` `503` döner (uydurma yanıt üretilmez).

### 7.2 POST `/api/assistant/ask`

**Girdi:**
```json
{ "prompt": "SKU-1234 kodlu ürünün stok durumu nedir?" }
```

**Çıktı (200):**
```json
{ "answer": "SKU-1234 için güncel stok 142 adet, IST-01 deposunda." }
```

**İşleyiş:**
1. `RagRetriever` pgvector'dan ilgili doküman parçalarını getirir.
2. Prompt kurulur; bağlam `<untrusted_document>` etiketiyle işaretlenir (prompt injection savunması).
3. LLM `FunctionChoiceBehavior.Auto()` ile tool'ları kendiliğinden çağırır (`get_inventory_level`, `forecast_stockout`, `get_order_status`).
4. Her tool çağrısı `ToolAuthorizationFilter` guardrail'inden geçer (rol kontrolü + çıktı 8000 karakter kırpma).
5. Token kullanımı `llm.tokens` metriğine yazılır (`model` + `direction` bounded label).

**Header'lar:** `X-User-Id`, `X-Tenant-Id`, `X-User-Roles` (virgülle ayrık). Roller olmadan tool çağrısı **fail-closed** reddedilir → `KernelException` → `503`.

**Hata yanıtı:** `503 Service Unavailable` (ProblemDetails) — LLM sağlayıcısı yok/erişilemez.

### 7.3 POST `/api/rag/ingest`

**Girdi:**
```json
{ "source": "depo-politikasi.md", "content": "İzmir depo (IZM-03) iade süreci: ..." }
```

**Çıktı:**
```json
{ "chunks": 1 }
```

İçerik `DocumentChunker` ile parçalanır → her parça embedding'e çevrilir → `ai.documents` (pgvector) tablosuna upsert edilir. `source` alanı aynı kaynağın yeniden yüklenmesinde üzerine yazmayı sağlar.

> **Ön koşul:** Embedding için `AI__ApiKey` gerekir; yoksa `500` döner.

### 7.4 MCP yüzeyi — POST `/` (kök yol)

> **Gotcha — adres:** `app.MapMcp()` desen verilmeden çağrıldığı için MCP ucu **kök yolda** (`http://localhost:8083/`) yayınlanır, `/mcp` **değil**. `POST /mcp` → `404`.

JSON-RPC 2.0, Streamable HTTP transport. Tool'lar `[McpServerToolType]` + `[McpServerTool]` ile keşfedilir; aynı metotlar Semantic Kernel tarafında `[KernelFunction]` ile auto-function-calling'e girer.

**Header'lar:** `Content-Type: application/json`, `Accept: application/json, text/event-stream`

**Akış:**

| Adım | Method | Gövde (özet) | Dönen |
|---|---|---|---|
| 1 | `initialize` | `{ protocolVersion, capabilities, clientInfo }` | `serverInfo` + `Mcp-Session-Id` header |
| 2 | `tools/list` | — | 3 tool + `inputSchema` |
| 3 | `tools/call` | `{ name, arguments }` | `result.content[].text` (JSON string) |

**Tool'lar:**

| Tool | Girdi | Doğrulama | Yetkili roller |
|---|---|---|---|
| `get_inventory_level` | `sku` | `^[A-Z0-9\-]{3,32}$` | `reader`, `ops`, `admin` |
| `forecast_stockout` | `sku` | `^[A-Z0-9\-]{3,32}$` | `reader`, `ops`, `admin` |
| `get_order_status` | `orderId` | `^[A-Za-z0-9\-]{4,40}$` | `support`, `ops`, `admin` |

**Örnek `tools/call` gövdesi:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_inventory_level",
    "arguments": { "sku": "SKU-1234" }
  }
}
```

**Dönen (MCP):**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [
      { "type": "text", "text": "{\"sku\":\"SKU-1234\",\"quantity\":142,\"warehouse\":\"IST-01\"}" }
    ]
  }
}
```

**Tool çıktı şemaları (typed, SQL'siz sözleşmeler):**

| Record | Alanlar |
|---|---|
| `InventoryLevel` | `Sku`, `Quantity` (int), `Warehouse` |
| `StockoutForecast` | `Sku`, `EstimatedStockoutDate` (date), `DailyVelocity` (double) |
| `OrderStatus` | `OrderId`, `Status`, `CustomerId` |

Demo veri deterministic'tir (FNV-1a hash): aynı SKU/sipariş her zaman aynı sonucu verir.

> **Postman notu:** MCP yanıtları SSE olarak akabilir; Postman gövdeyi ham gösterir. Protokolü gezinmek için `npx @modelcontextprotocol/inspector` daha rahattır.

---

## 8. Arka plan servisleri (HTTP ucu yok)

| Servis | Tetikleyici | Girdi | Çıktı / etki |
|---|---|---|---|
| TelemetryStreamProcessor | Kafka `telemetry.raw` topic | Telemetri olayları | İşlenmiş metrikler → OTel |
| BackgroundOrchestrator | RabbitMQ (MassTransit) | Mesajlar / zamanlanmış işler | Outbox, CDC işleri |

Bu servisler HTTP ile tetiklenmez. Denemek için:

```bash
# Kafka topic'lerini listele
docker exec -it elp-kafka kafka-topics --bootstrap-server localhost:9092 --list

# telemetry.raw topic'ine elle mesaj bas
docker exec -it elp-kafka kafka-console-producer \
  --bootstrap-server localhost:9092 --topic telemetry.raw
```

---

## 9. Hızlı cURL karşılıkları

```bash
# Gateway sağlık zinciri
curl -s http://localhost:8080/ | jq
curl -s http://localhost:8080/api/orders/health | jq
curl -s http://localhost:8080/api/inventory/health | jq

# AI (gateway üzerinden — tenant header zorunlu, çift `api` önekine dikkat)
curl -s -X POST http://localhost:8080/api/ai/api/assistant/ask \
  -H 'Content-Type: application/json' \
  -H 'X-Tenant-Id: tenant-42' \
  -H 'X-User-Roles: ops,reader' \
  -d '{"prompt":"SKU-1234 stok durumu nedir?"}' | jq

# AI doğrudan
curl -s http://localhost:8083/health | jq
curl -s -X POST http://localhost:8083/api/assistant/ask \
  -H 'Content-Type: application/json' \
  -H 'X-User-Roles: ops' \
  -d '{"prompt":"SKU-1234 stok durumu nedir?"}' | jq

# RAG ingest (doğrudan)
curl -s -X POST http://localhost:8083/api/rag/ingest \
  -H 'Content-Type: application/json' \
  -d '{"source":"depo-politikasi.md","content":"İzmir depo iade süreci 14 gündür."}' | jq

# MCP tools/list (kok yol!)
curl -s -X POST http://localhost:8083/ \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
```

---

## 10. Sık karşılaşılan durumlar

| Kod | Nerede | Anlamı | Çözüm |
|---|---|---|---|
| `400` | Gateway `/api/ai/*` | `X-Tenant-Id` header'ı eksik | Header'ı ekleyin |
| `429` | Gateway `/api/ai/*` | Günlük 100.000 token kotası tükendi | Kota UTC gece yarısı sıfırlanır; `Retry-After` bekleyin |
| `429` | Gateway (tüm rotalar) | Rate limit aşımı | `Retry-After: 1` sonra tekrar deneyin |
| `503` | AI `/api/assistant/ask` | LLM sağlayıcısı yapılandırılmamış | `AI__ApiKey` (+ gerekirse `AI__Endpoint`) verin |
| `500` | AI `/api/rag/ingest` | Embedding üretilemedi (API key yok) | `AI__ApiKey` verin |
| `404` | `/api/read/orders/*`, `/grpc/inventory/*` | Downstream uç henüz yazılmadı | Gün 1 / Gün 2 lab'ları tamamlanınca aktifleşir |
| `404` | Gateway `/api/ai/assistant/ask` | Çift `api` öneki unutulmuş | `/api/ai/api/assistant/ask` kullanın |
| `404` | AI `POST /mcp` | MCP ucu kök yolda yayınlanıyor | `POST {{aiDirect}}/` kullanın |

---

## 11. Gözlemlenebilirlik uçları

| Araç | Adres | Not |
|---|---|---|
| Jaeger UI | http://localhost:16686 | Trace arama; `service=api-gateway` |
| Prometheus | http://localhost:9090 | `/-/healthy`; scrape hedefi `otel-collector:8889` |
| Grafana | http://localhost:3001 | Giriş `admin` / `admin` |
| Graylog | http://localhost:9000 | Giriş `admin` / `admin`; **GELF UDP (12201) input manuel tanımlanmalı** |
| RabbitMQ Management | http://localhost:15672 | Giriş `logistics` / `logistics_secret` |
