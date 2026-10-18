# Evolutionary Logistics & Telemetry Platform

İleri seviye .NET eğitimi referans kod tabanı (.NET SDK **10.0.402** hedeflenir; geliştirme makinesinde **10.0.401** ile de derlenebilir).

## Hızlı başlangıç

```bash
docker compose up -d postgres redis kafka kafka-init rabbitmq jaeger otel-collector prometheus grafana
# Uygulamalar (opsiyonel, build gerekir):
docker compose up -d --build api-gateway order-service inventory-service
```

| Servis | URL |
|--------|-----|
| API Gateway (YARP) | http://localhost:8080 |
| Order API | http://localhost:8081/health |
| Inventory API | http://localhost:8082/health |
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
