"""
AI Commerce Assistant — FastAPI giriş noktası.
Tüm route'lar, middleware ve lifecycle eventler buradan yönetilir.
"""
import structlog
from fastapi import FastAPI  # Ana uygulama sınıfı
from fastapi.middleware.cors import CORSMiddleware  # CORS politikasını yönetir
from contextlib import asynccontextmanager  # Uygulama yaşam döngüsü yönetimi

from app.core.config import settings
from app.core.logging_config import setup_logging
from app.api.routes import router
from app.core.cache import cache as cache_service
from app.core.rate_limit import setup_rate_limiting

setup_logging(
    settings.log_level,
    logstash_host=settings.logstash_host,
    logstash_port=settings.logstash_port,
)
logger = structlog.get_logger(__name__)


@asynccontextmanager  # Asenkron bağlam yöneticisi
async def lifespan(app: FastAPI):
    """Uygulama başlarken ve kapanırken çalışacak kod."""
    # Başlangıç
    print(f"🚀 {settings.app_name} başlatılıyor...")
    print(f"   Ortam  : {settings.app_env}")
    print(f"   Debug  : {settings.debug}")
    print(f"   LLM    : {settings.ollama_model} @ {settings.ollama_base_url}")
    # Redis (cache) bağlantısını kur
    try:
        await cache_service.connect()
    except Exception as e:
        logger.exception("cache_connect_failed", error=str(e))
    yield
    # Kapanış
    print("🛑 Uygulama kapatılıyor...")
    try:
        await cache_service.disconnect()
    except Exception:
        pass


app = FastAPI(  # FastAPI uygulama örneği
    title=settings.app_name,
    description="LLM + RAG + AI Agent ile akıllı e-ticaret asistanı",
    version="0.1.0",
    lifespan=lifespan,
    docs_url="/docs",       # Swagger UI
    redoc_url="/redoc",     # ReDoc UI
)

# Rate limiting (SlowAPI) must be configured before the application starts
try:
    setup_rate_limiting(app)
except Exception:
    logger.exception("rate_limit_setup_failed")

# CORS ayarları (geliştirme için tüm origin'lere izin)
app.add_middleware(  # Ara katman yazılımı ekler
    CORSMiddleware,
    allow_origins=["*"],  # Tüm kaynaklara izin ver (geliştirme)
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# API route'larını kaydet
app.include_router(router, prefix="/api/v1")  # Router'ı uygulamaya bağlar


@app.get("/", tags=["health"])  # GET endpoint tanımı
async def root() -> dict:
    """Uygulama kök endpoint'i — sağlık kontrolü."""
    return {
        "app": settings.app_name,
        "version": "0.1.0",
        "status": "running",
        "env": settings.app_env,
    }