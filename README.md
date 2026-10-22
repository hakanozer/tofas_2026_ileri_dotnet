# Evolutionary Logistics & Telemetry Platform

İleri seviye .NET geliştirme, mikroservis mimarisi, mesajlaşma, gözlemlenebilirlik ve yapay zekâ tabanlı RAG uygulamalarını kapsayan eğitim ve referans kod tabanı.

Proje; modern backend geliştirme pratiklerini, dağıtık sistem tasarımını, dayanıklılık mekanizmalarını, telemetri altyapısını ve yerel yapay zekâ entegrasyonlarını uygulamalı olarak incelemek amacıyla hazırlanmıştır.

## İçindekiler

- [Teknolojiler](#teknolojiler)
- [Gereksinimler](#gereksinimler)
- [Hızlı Başlangıç](#hızlı-başlangıç)
- [Servis Erişim Adresleri](#servis-erişim-adresleri)
- [Geliştirme Komutları](#geliştirme-komutları)
- [Python ve RAG Ortamı](#python-ve-rag-ortamı)
- [Solution ve Dokümantasyon](#solution-ve-dokümantasyon)
- [YARP Paket Notu](#yarp-paket-notu)

## Teknolojiler

| Alan | Teknolojiler |
|---|---|
| Backend | .NET 10, ASP.NET Core |
| API Gateway | YARP |
| Veritabanı | PostgreSQL |
| Cache | Redis |
| Mesajlaşma | Apache Kafka, RabbitMQ |
| Dağıtık izleme | OpenTelemetry, Jaeger |
| Metrik ve dashboard | Prometheus, Grafana |
| Log yönetimi | Graylog |
| Kafka yönetimi | Kafbat UI |
| Yapay zekâ | Ollama, Llama 3 |
| RAG ve vektör arama | Python, Qdrant |
| Veritabanı migration | Alembic |

## Gereksinimler

Projeyi çalıştırmadan önce aşağıdaki araçların kurulu olduğundan emin olun:

- Docker ve Docker Compose
- .NET SDK 10
- Python 3 ve `venv`
- `uvicorn`
- Ollama
- `uv` kullanılıyorsa ilgili Python bağımlılık yönetimi araçları

> **.NET SDK notu:** Proje için hedeflenen SDK sürümü `10.0.402`'dir. Geliştirme ortamında `10.0.401` sürümüyle de derleme yapılabilir.

## Hızlı Başlangıç

### 1. Altyapı servislerini başlatın

PostgreSQL, Redis, Kafka, RabbitMQ, dağıtık izleme ve metrik servislerini Docker Compose ile başlatın:

```bash
docker compose up -d postgres redis kafka kafka-init kafka-ui rabbitmq jaeger otel-collector prometheus grafana
```

Bu komut altyapı servislerini başlatır. Uygulama servisleri ayrıca derlenip çalıştırılabilir.

### 2. Uygulamaları derleyin ve başlatın

API Gateway, Order Service ve Inventory Service servislerini derleyerek başlatmak için:

```bash
docker compose up -d --build api-gateway order-service inventory-service
```

Tüm Compose servislerini derleyip başlatmak için:

```bash
docker compose up -d --build
```

> **Not:** İkinci komut, Compose dosyasında tanımlı tüm servisleri kapsar. Python uygulamalarının veya diğer servislerin ayrıca yapılandırılması gerekebilir.

### 3. API Gateway'i yerel ortamda çalıştırın

Gateway'i Docker dışında, yerel .NET geliştirme ortamında başlatmak için:

```bash
dotnet run --project src/Gateway/ApiGateway/ApiGateway.csproj
```

### 4. Servislerin durumunu kontrol edin

Aşağıdaki sağlık kontrolü adreslerinden API servislerinin erişilebilirliğini doğrulayabilirsiniz:

- Order API: `http://localhost:8081/health`
- Inventory API: `http://localhost:8082/health`

## Servis Erişim Adresleri

| Servis | Adres | Açıklama |
|---|---|---|
| API Gateway (YARP) | [http://localhost:8080](http://localhost:8080) | API isteklerinin yönlendirilmesi |
| Order API | [http://localhost:8081/health](http://localhost:8081/health) | Sipariş servisi sağlık kontrolü |
| Inventory API | [http://localhost:8082/health](http://localhost:8082/health) | Envanter servisi sağlık kontrolü |
| Kafbat UI | [http://localhost:8085](http://localhost:8085) | Kafka yönetimi |
| Jaeger | [http://localhost:16686](http://localhost:16686) | Dağıtık izlerin incelenmesi |
| Grafana | [http://localhost:3001](http://localhost:3001) | Metrik ve dashboard görüntüleme |
| Graylog | [http://localhost:9000](http://localhost:9000) | Log yönetimi |
| RabbitMQ Management | [http://localhost:15672](http://localhost:15672) | RabbitMQ yönetim arayüzü |
| Qdrant Dashboard | [http://localhost:6333/dashboard](http://localhost:6333/dashboard) | Vektör veritabanı yönetimi |

**Varsayılan kimlik bilgileri**

- Grafana: `admin / admin`
- Graylog: `admin / admin` — yapılandırmaya bağlıdır.
- RabbitMQ: Kullanıcı adı ve parola Compose yapılandırmasına bağlıdır.

> Servis adresleri ve kimlik bilgileri Docker Compose yapılandırmasına göre değişebilir. İlk çalıştırmada port eşlemelerini ve ortam değişkenlerini kontrol edin.

## Geliştirme Komutları

Proje kök dizininde bulunan `scripts/dev-local.sh` betiği, desteklenen servisleri yerel geliştirme ortamında başlatmak için kullanılabilir.

### Tüm desteklenen servisleri başlatın

```bash
./scripts/dev-local.sh
```

### Yalnızca seçilen servisleri başlatın

Örneğin Gateway ve Order servislerini başlatmak için:

```bash
./scripts/dev-local.sh gateway order
```

### Derleme adımını atlayın

```bash
./scripts/dev-local.sh --no-build
```

### Docker üzerinde çalışan uygulamaları koruyun

```bash
./scripts/dev-local.sh --keep-docker-apps
```

> Betiğin desteklediği servis adları, parametreler ve davranışlar için `scripts/dev-local.sh` dosyasını inceleyin.

### Order Service'i ayrı başlatın

```bash
docker compose up -d --build order-service
```

## Python ve RAG Ortamı

Python tabanlı uygulamayı ve RAG bileşenlerini geliştirmek için aşağıdaki adımları izleyin.

### 1. Sanal ortam oluşturun ve etkinleştirin

```bash
python3 -m venv .venv
source .venv/bin/activate
```

### 2. Python uygulamasını başlatın

`app.main` modülünün bulunduğu Python proje dizininde çalıştırın:

```bash
uvicorn app.main:app --reload
```

> Gerekli Python paketlerinin kurulu olması gerekir. Bağımlılıkları proje içerisinde tanımlanan `requirements.txt` veya `pyproject.toml` dosyasına göre yükleyin.

### 3. Ollama ile yerel modeli çalıştırın

Ollama servisinin kurulu ve çalışır durumda olduğundan emin olun.

Llama 3 modelini indirmek ve çalıştırmak için:

```bash
ollama run llama3
```

> `ollama run` komutu bir model adıyla kullanılmalıdır. Model henüz indirilmemişse Ollama gerekli indirme işlemini başlatabilir.

### 4. Veritabanı migration işlemlerini uygulayın

```bash
alembic upgrade head
```

Bu komut, Alembic tarafından tanımlanmış migration işlemlerini veritabanına uygular. Öncesinde veritabanı bağlantı ayarlarını kontrol edin.

### 5. Veritabanını örnek verilerle doldurun

```bash
python -m scripts.seed_database
```

### 6. Qdrant depolama bileşenini çalıştırın

```bash
python -m app.rag.qdrant_store
```

Bu modülün doğrudan çalıştırılabilir olması, projenin mevcut uygulamasına ve modülün giriş noktası tanımına bağlıdır.

### 7. RAG indeksini oluşturun

```bash
python -m scripts.build_index
```

Bu işlem, proje tarafından tanımlanan veri kaynaklarının indekslenerek RAG akışında kullanılmasına yönelik hazırlık adımını yürütür.

> **Önemli:** Python komutlarının doğru çalışması için komutları ilgili Python projesinin kök dizininde çalıştırın. Ayrıca PostgreSQL ve Qdrant gibi bağımlı servislerin önceden başlatılmış olması gerekir.

## Solution ve Dokümantasyon

### .NET Solution

Projenin ana solution dosyası:

- `EvolutionaryLogistics.slnx` — .NET XML tabanlı solution dosyası.

### Mimari dokümantasyon

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — Proje mimarisi ve dizin yapısı.

### Eğitmen dokümantasyonu

- [Day 1: Modular Monolith, Event Sourcing ve CQRS](docs/playbooks/day1-modular-monolith-es-cqrs.html) — İlk gün eğitim akışı ve uygulama rehberi.

## YARP Paket Notu

Eğitim müfredatında YARP `3.12.1` sürümü referans alınmıştır. Ancak kullanılan NuGet paketinin sürümü, müfredatta belirtilen sürümle aynı olmayabilir.

`Yarp.ReverseProxy` paket sürümünü ve kullanılabilir kararlı sürümleri NuGet üzerinden doğrulayın. `yarp.json` yapılandırmasının uyumluluğunu da kullanılan gerçek paket sürümüne göre test edin.

Paket sürümleri arasında farklılık varsa, eğitim dokümantasyonu ile projede kullanılan bağımlılık sürümlerini birlikte güncelleyin.

## Geliştirme Notları

- Altyapı servislerini ve uygulama servislerini ayrı başlatmak, hata ayıklamayı kolaylaştırır.
- API sağlık kontrollerini, mesajlaşma altyapısını ve telemetri dashboard'larını uygulama geliştirme sürecinde düzenli olarak kontrol edin.
- Parola, bağlantı dizesi ve API anahtarı gibi hassas bilgileri kaynak koduna veya sürüm kontrolüne eklemeyin.
- Docker Compose yapılandırmasını ve uygulama ayarlarını geliştirme ortamınıza göre düzenleyin.
