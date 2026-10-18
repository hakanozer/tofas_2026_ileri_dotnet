# Evolutionary Logistics & Telemetry Platform

## Solution layout (Bounded Contexts)

```
EvolutionaryLogistics.sln
├── src/
│   ├── Gateway/
│   │   └── ApiGateway/              # YARP 3.12.1 — edge routing, rate limits, JWT
│   ├── Services/
│   │   ├── Order/                   # Order BC
│   │   │   ├── Order.Domain/
│   │   │   ├── Order.Application/
│   │   │   ├── Order.Infrastructure/
│   │   │   ├── Order.Api/           # REST + hosts gRPC (Day 1 modular monolith slice)
│   │   │   └── Order.Contracts/     # Protobuf / shared DTOs
│   │   ├── Inventory/               # Inventory BC
│   │   │   ├── Inventory.Domain/
│   │   │   ├── Inventory.Application/
│   │   │   ├── Inventory.Infrastructure/
│   │   │   └── Inventory.Api/
│   │   ├── Telemetry/
│   │   │   └── TelemetryStreamProcessor/
│   │   ├── Orchestrator/
│   │   │   └── BackgroundOrchestrator/
│   │   └── AI/
│   │       └── AiMcpServer/
│   └── BuildingBlocks/
│       ├── Shared.Kernel/
│       ├── Shared.Grpc/             # Interceptors, metadata conventions
│       └── Shared.Observability/
├── infra/                           # Postgres init, OTEL, Prometheus, Grafana
├── docs/playbooks/                  # Trainer HTML playbooks (Day 1–4)
└── docker-compose.yml
```

## Day 1 focus

Modular monolith within **Order** and **Inventory** contexts: aggregate design, event sourcing primitives, CQRS with MediatR (expanded in playbook labs).
