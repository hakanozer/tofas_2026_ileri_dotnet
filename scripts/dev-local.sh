#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  dev-local.sh — .NET servislerini LOKALDE (dotnet) çalıştırır, altyapıyı
#  Docker'da bırakır.
#
#  NEDEN: Her değişiklikte `docker compose up -d --build` beklemek yavaş.
#  Bu betik Postgres/Redis/Kafka/RabbitMQ/Jaeger/Graylog gibi altyapıyı
#  Docker'da tutar; Order/Inventory/AI/Gateway/worker süreçlerini lokalde
#  ayağa kaldırır. Gateway yine http://localhost:8080 ana giriş noktasıdır.
#
#  KULLANIM:
#    ./scripts/dev-local.sh                  # hepsini başlat
#    ./scripts/dev-local.sh gateway order    # sadece seçilenler
#    ./scripts/dev-local.sh --no-build       # derlemeyi atla
#    ./scripts/dev-local.sh --help
#
#  DURDURMA: Ctrl+C (tüm lokaldeki süreçler kapanır, Docker altyapı çalışmaya
#            devam eder).
#
#  ÖN KOŞUL: docker compose ile altyapı ayakta olmalı:
#    docker compose up -d postgres redis kafka kafka-init rabbitmq \
#                        jaeger otel-collector graylog
# ─────────────────────────────────────────────────────────────────────────────
set -eo pipefail

# ── Portlar ──────────────────────────────────────────────────────────────────
# Not: Bu portlar bilinçli olarak Docker portlarıyla AYNI tutulur ki Postman
# koleksiyonu ve dokümanlar değişmeden çalışsın.
GATEWAY_PORT="${GATEWAY_PORT:-8080}"
ORDER_PORT="${ORDER_PORT:-8081}"
INVENTORY_PORT="${INVENTORY_PORT:-8082}"
AI_PORT="${AI_PORT:-8083}"
# Postgres host portu: host'ta başka bir PostgreSQL (örn. Homebrew) 5432'yi
# dinliyorsa Docker'ın 5432 eşlemesi gölgelenir. docker-compose postgres'i
# host'ta 5433'e de publish eder; lokal servisler 5433'ten bağlanır.
PG_PORT="${PG_PORT:-5433}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG_DIR="$ROOT/logs"
YARP_SRC="$ROOT/src/Gateway/ApiGateway/yarp.json"
YARP_LOCAL="$ROOT/src/Gateway/ApiGateway/yarp.local.json"

C_RESET='\033[0m'; C_BOLD='\033[1m'; C_GREEN='\033[32m'; C_YELLOW='\033[33m'
C_RED='\033[31m'; C_CYAN='\033[36m'; C_DIM='\033[2m'
info() { printf "${C_CYAN}->${C_RESET} %s\n" "$*"; }
ok()   { printf "${C_GREEN}[ok]${C_RESET} %s\n" "$*"; }
warn() { printf "${C_YELLOW}[!]${C_RESET} %s\n" "$*"; }
err()  { printf "${C_RED}[x]${C_RESET} %s\n" "$*" >&2; }

usage() {
  sed -n '2,23p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# ── Argümanlar ───────────────────────────────────────────────────────────────
BUILD=1
STOP_DOCKER_APPS=1
SELECTED=()

for arg in "$@"; do
  case "$arg" in
    --no-build)          BUILD=0 ;;
    --keep-docker-apps)  STOP_DOCKER_APPS=0 ;;
    -h|--help)           usage; exit 0 ;;
    --*)                 err "Bilinmeyen bayrak: $arg"; usage; exit 2 ;;
    *)                   SELECTED+=("$arg") ;;
  esac
done

# ── Servis tabloları (paralel diziler — bash 3.2 uyumlu) ─────────────────────
NAMES=(gateway     order                 inventory                 ai                    telemetry                                     orchestrator)
DIRS=(src/Gateway/ApiGateway src/Services/Order/Order.Api src/Services/Inventory/Inventory.Api src/Services/AI/AiMcpServer src/Services/Telemetry/TelemetryStreamProcessor src/Services/Orchestrator/BackgroundOrchestrator)
DLLS=(ApiGateway.dll Order.Api.dll Inventory.Api.dll AiMcpServer.dll TelemetryStreamProcessor.dll BackgroundOrchestrator.dll)
URLS=("http://localhost:${GATEWAY_PORT}" "http://localhost:${ORDER_PORT}" "http://localhost:${INVENTORY_PORT}" "http://localhost:${AI_PORT}" "" "")
OTELS=(api-gateway order-service inventory-service ai-mcp-server telemetry-stream-processor background-orchestrator)

index_of() {
  local target="$1" i=0
  for n in "${NAMES[@]}"; do
    [ "$n" = "$target" ] && { echo "$i"; return 0; }
    i=$((i + 1))
  done
  return 1
}

if [ "${#SELECTED[@]}" -eq 0 ]; then
  SELECTED=("${NAMES[@]}")
fi

SELECTED_IDX=()
for name in "${SELECTED[@]}"; do
  if ! idx="$(index_of "$name")"; then
    err "Bilinmeyen servis: '$name' (geçerli: ${NAMES[*]})"
    exit 2
  fi
  SELECTED_IDX+=("$idx")
done

# ── 1) Ön kontroller ─────────────────────────────────────────────────────────
command -v dotnet >/dev/null 2>&1 || { err "dotnet bulunamadı (PATH)."; exit 1; }

need_docker_infra=0
for c in elp-postgres elp-redis elp-kafka elp-rabbitmq; do
  running="$(docker inspect -f '{{.State.Running}}' "$c" 2>/dev/null || echo false)"
  if [ "$running" != "true" ]; then
    warn "Altyapı konteyneri çalışmıyor: $c"
    need_docker_infra=1
  fi
done
if [ "$need_docker_infra" -eq 1 ]; then
  err "Önce altyapıyı kaldırın:"
  printf "    docker compose up -d postgres redis kafka kafka-init rabbitmq jaeger otel-collector graylog\n"
  exit 1
fi
ok "Docker altyapısı ayakta (postgres, redis, kafka, rabbitmq)."

# Host'tan erişilebilen Postgres portunu seç (5433 varsayılan; yoksa 5432).
if ! (exec 3<>/dev/tcp/127.0.0.1/${PG_PORT}) 2>/dev/null; then
  warn "Postgres 127.0.0.1:${PG_PORT} erişilemiyor; 5432'ye düşülüyor."
  PG_PORT=5432
fi
if ! (exec 3<>/dev/tcp/127.0.0.1/${PG_PORT}) 2>/dev/null; then
  err "Postgres host'tan erişilemiyor (5433 ve 5432 kapalı)."
  printf "    docker compose up -d postgres  # port eşlemelerini uygula\n"
  exit 1
fi
ok "Postgres host portu: ${PG_PORT}"

# ── 2) Portları işgal eden Docker uygulama konteynerlerini durdur ────────────
# Aynı 8080-8083 portlarını ve aynı Kafka/RabbitMQ consumer-group'larını
# kullandıkları için lokalde çalışırken kapatılmalıdırlar.
if [ "$STOP_DOCKER_APPS" -eq 1 ]; then
  to_stop=""
  for c in elp-api-gateway elp-order-service elp-inventory-service \
           elp-ai-mcp-server elp-telemetry-processor elp-background-orchestrator; do
    if [ "$(docker inspect -f '{{.State.Running}}' "$c" 2>/dev/null || echo false)" = "true" ]; then
      to_stop="$to_stop $c"
    fi
  done
  if [ -n "$to_stop" ]; then
    info "Docker uygulama konteynerleri durduruluyor (portları boşaltmak için):$to_stop"
    docker stop $to_stop >/dev/null
    ok "Konteynerler durduruldu (altyapı etkilenmedi)."
  fi
fi

# ── 3) Lokal YARP yapılandırmasını üret ─────────────────────────────────────
# yarp.json destination'ları Docker DNS adları kullanır (order-service vb.).
# Lokalde bu adlar çözülmez → hostname'leri localhost'a çeviren bir kopya üretiriz.
# Kaynak yarp.json'dur; bu dosya her çalıştırmada yeniden üretilir (drift yok).
info "Lokal YARP yapılandırması üretiliyor: yarp.local.json"
sed -e "s#http://order-service:[0-9]*#http://localhost:${ORDER_PORT}#g" \
    -e "s#http://inventory-service:[0-9]*#http://localhost:${INVENTORY_PORT}#g" \
    -e "s#http://ai-mcp-server:[0-9]*#http://localhost:${AI_PORT}#g" \
    "$YARP_SRC" > "$YARP_LOCAL"

# ── 4) Derleme ───────────────────────────────────────────────────────────────
if [ "$BUILD" -eq 1 ]; then
  info "Derleniyor (dotnet build)..."
  dotnet build "$ROOT/EvolutionaryLogistics.slnx" -c Debug --nologo -v quiet
  ok "Derleme tamam."
else
  info "Derleme atlandı (--no-build)."
fi

# ── 5) Ortak ortam değişkenleri (docker-compose x-dotnet-env karşılığı) ──────
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export OTEL_SERVICE_NAMESPACE=evolutionary-logistics
# OTel Collector host'ta 4319 -> konteynerde 4317 (Jaeger'a değil, collector'a
# gider ki tail_sampling devrede olsun).
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4319"
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=${PG_PORT};Database=evolutionary_logistics;Username=app;Password=app_secret_change_me"
export ConnectionStrings__Redis="localhost:6379,abortConnect=false"
export Kafka__BootstrapServers="localhost:29092"
export RabbitMQ__Host="localhost"
export RabbitMQ__Username="logistics"
export RabbitMQ__Password="logistics_secret"
export Sentry__Dsn="http://public@localhost:3000/1"
export Graylog__Endpoint="udp://localhost:12201"
# LLM sağlayıcısı (opsiyonel): kabukta AI_API_KEY/AI_ENDPOINT tanımlıysa geçir.
export AI__ApiKey="${AI_API_KEY:-}"
export AI__Endpoint="${AI_ENDPOINT:-}"
[ -n "${AI_CHAT_MODEL:-}" ]      && export AI__ChatModel="${AI_CHAT_MODEL}"
[ -n "${AI_EMBEDDING_MODEL:-}" ] && export AI__EmbeddingModel="${AI_EMBEDDING_MODEL}"

# ── 6) Servisleri başlat ─────────────────────────────────────────────────────
mkdir -p "$LOG_DIR"
PIDS=()
PNAMES=()
WEB_IDX=()

start_one() {
  local idx="$1" name dir dll url otel dll_path log
  name="${NAMES[$idx]}"; dir="${DIRS[$idx]}"; dll="${DLLS[$idx]}"
  url="${URLS[$idx]}"; otel="${OTELS[$idx]}"
  dll_path="$ROOT/$dir/bin/Debug/net10.0/$dll"
  log="$LOG_DIR/$name.log"

  if [ ! -f "$dll_path" ]; then
    err "$name: derlenmiş çıktı yok -> $dll_path"
    return 1
  fi

  # Content root = proje dizini olmalı; appsettings.json ve yarp.json oradan
  # okunur. `exec` sayesinde sakladığımız PID doğrudan uygulama sürecidir
  # (dotnet run'ın alt süreç bırakma sorunu yoktur).
  (
    cd "$ROOT/$dir"
    export OTEL_SERVICE_NAME="$otel"
    [ -n "$url" ] && export ASPNETCORE_URLS="$url"
    [ "$name" = "gateway" ] && export ReverseProxy__ConfigPath="yarp.local.json"
    exec dotnet "$dll_path"
  ) >"$log" 2>&1 &

  PIDS+=("$!")
  PNAMES+=("$name")
  if [ -n "$url" ]; then WEB_IDX+=("$idx"); fi
  printf "  %-12s %s ${C_DIM}(log: logs/%s.log)${C_RESET}\n" "$name" "${url:-[worker]}" "$name"
}

info "Servisler başlatılıyor:"
: > "$LOG_DIR/.started"
for idx in "${SELECTED_IDX[@]}"; do
  start_one "$idx" || exit 1
done

# ── 7) Trap: Ctrl+C'de hepsini kapat ─────────────────────────────────────────
SHUTTING_DOWN=0
shutdown() {
  [ "$SHUTTING_DOWN" -eq 1 ] && return
  SHUTTING_DOWN=1
  printf "\n"
  info "Durduruluyor (${#PIDS[@]} süreç)..."
  i=0
  for pid in "${PIDS[@]}"; do
    kill "$pid" 2>/dev/null || true
    i=$((i + 1))
  done
  wait 2>/dev/null || true
  ok "Lokal servisler kapatıldı. Docker altyapısı çalışmaya devam ediyor."
  printf "${C_DIM}Docker uygulama konteynerlerini geri almak için: docker compose start api-gateway order-service inventory-service ai-mcp-server telemetry-processor background-orchestrator${C_RESET}\n"
  exit 0
}
trap shutdown INT TERM

# ── 8) Kısa süre sonra ölen var mı? ──────────────────────────────────────────
sleep 3
i=0
for pid in "${PIDS[@]}"; do
  if ! kill -0 "$pid" 2>/dev/null; then
    err "${PNAMES[$i]} başlatılamadı. Son log satırları:"
    tail -n 15 "$LOG_DIR/${PNAMES[$i]}.log" 2>/dev/null | sed 's/^/      /' || true
    shutdown
  fi
  i=$((i + 1))
done

# ── 9) Health-check bekle ────────────────────────────────────────────────────
wait_http() {
  local url="$1" tries=0
  while [ "$tries" -lt 60 ]; do
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "$url" 2>/dev/null || echo 000)"
    [ "$code" = "200" ] && return 0
    tries=$((tries + 1))
    sleep 1
  done
  return 1
}

info "Sağlık kontrolleri bekleniyor..."
i=0
for idx in "${SELECTED_IDX[@]}"; do
  name="${NAMES[$idx]}"; url="${URLS[$idx]}"
  [ -z "$url" ] && { printf "  %-12s ${C_DIM}[worker — HTTP ucu yok]${C_RESET}\n" "$name"; continue; }
  health="$url/health"
  [ "$name" = "gateway" ] && health="$url/"
  if wait_http "$health"; then
    printf "  ${C_GREEN}up${C_RESET}   %-12s %s\n" "$name" "$health"
  else
    printf "  ${C_RED}down${C_RESET} %-12s %s ${C_DIM}(bkz. logs/%s.log)${C_RESET}\n" "$name" "$health" "$name"
  fi
  i=$((i + 1))
done

# ── 10) Özet ─────────────────────────────────────────────────────────────────
printf "\n${C_BOLD}Hazır.${C_RESET} ${C_DIM}Durdurmak için Ctrl+C${C_RESET}\n\n"
printf "  ${C_BOLD}Gateway (ana giriş):${C_RESET} http://localhost:${GATEWAY_PORT}\n"
printf "     örn: curl http://localhost:${GATEWAY_PORT}/api/orders/health\n"
printf "          curl -X POST http://localhost:${GATEWAY_PORT}/api/ai/api/assistant/ask \\\\\n"
printf "               -H 'Content-Type: application/json' -H 'X-Tenant-Id: tenant-42' \\\\\n"
printf "               -H 'X-User-Roles: ops' -d '{\"prompt\":\"SKU-1234 stok?\"}'\n\n"
printf "  ${C_DIM}Doğrudan:${C_RESET} Order :${ORDER_PORT}/health  Inventory :${INVENTORY_PORT}/health  AI :${AI_PORT}/health\n"
printf "  ${C_DIM}Gözlem:${C_RESET}   Jaeger http://localhost:16686  Grafana http://localhost:3001  Graylog http://localhost:9000  Prometheus http://localhost:9090\n"
printf "  ${C_DIM}Loglar:${C_RESET}   logs/*.log  ${C_DIM}(canlı: tail -f logs/gateway.log)${C_RESET}\n\n"

# Betik kapanana kadar bekle (trap tetiklenmesi için).
wait
