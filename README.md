# Evolutionary Logistics & Telemetry Platform

İleri seviye .NET eğitimi referans kod tabanı (.NET SDK **10.0.402** hedeflenir; geliştirme makinesinde **10.0.401** ile de derlenebilir).

## Hızlı başlangıç

```bash
docker compose up -d postgres redis kafka kafka-init kafka-ui rabbitmq jaeger otel-collector prometheus grafana
# Uygulamalar (opsiyonel, build gerekir):
docker compose up -d --build api-gateway order-service inventory-service
```

| Servis | URL |
|--------|-----|
| API Gateway (YARP) | http://localhost:8080 |
| Order API | http://localhost:8081/health |
| Inventory API | http://localhost:8082/health |
| Kafbat UI | http://localhost:8085 |
| Jaeger | http://localhost:16686 |
| Grafana | http://localhost:3001 (admin/admin) |
| Graylog | http://localhost:9000 (admin/admin) |
| RabbitMQ | http://localhost:15672 |

## Solution

- `EvolutionaryLogistics.slnx` — .NET 10 XML solution
- Dizin haritası: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Eğitmen playbook: [docs/playbooks/day1-modular-monolith-es-cqrs.html](docs/playbooks/day1-modular-monolith-es-cqrs.html)

## Paket notu (YARP)

Müfredat **YARP 3.12.1** referans alır; NuGet.org’da en güncel karşılık **`Yarp.ReverseProxy` 2.3.0**’dır. `yarp.json` şeması eğitim boyunca aynı kalır; 3.x GA çıktığında yalnızca paket sürümü güncellenir.


Yapılacaklar:
docker compose up -d --build
docker compose up -d postgres redis kafka rabbitmq otel-collector jaeger prometheus grafana
dotnet run --project src/Gateway/ApiGateway/ApiGateway.csproj

docker compose up -d --build order-service

./scripts/dev-local.sh                  # hepsini başlat
./scripts/dev-local.sh gateway order    # sadece seçilenler
./scripts/dev-local.sh --no-build       # derlemeyi atla
./scripts/dev-local.sh --keep-docker-apps

python3 -m venv .venv
source .venv/bin/activate
uvicorn app.main:app --reload

ollama run
ollama run llama3

alembic upgrade head
python -m scripts.seed_database
python -m app.rag.qdrant_store
python -m scripts.build_index
http://localhost:6333/dashboard


