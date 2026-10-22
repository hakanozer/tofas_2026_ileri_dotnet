"""
Uygulama konfigürasyonu.
Tüm ortam değişkenleri burada merkezi olarak yönetilir.
"""
from pydantic_settings import BaseSettings  # .env'den otomatik ayar okumayı sağlar
from functools import lru_cache  # Fonksiyon sonucunu önbellekler


class Settings(BaseSettings):  # .env'den otomatik okuyan ayar sınıfı
    """Pydantic BaseSettings ile .env dosyasından otomatik okuma."""

    # Uygulama
    app_name: str = "AI Commerce Assistant"
    app_env: str = "development"
    debug: bool = True
    port: int = 8000
    log_level: str = "INFO"

    # PostgreSQL
    postgres_user: str = "aicommerce"
    postgres_password: str = "changeme123"
    postgres_db: str = "aicommerce_db"
    postgres_host: str = "localhost"
    postgres_port: int = 5432
    database_url: str = "postgresql://aicommerce:changeme123@localhost:5432/aicommerce_db"

    # LLM
    ollama_base_url: str = "http://localhost:11434"
    ollama_model: str = "llama3"

    # Embedding
    embedding_model: str = "paraphrase-multilingual-mpnet-base-v2"

    # FAISS
    vector_store_path: str = "./vector_store/products.faiss"
    
    # Redis
    redis_url: str = "redis://localhost:6379/0"

    # Log forwarding (opsiyonel)
    logstash_host: str = ""
    logstash_port: int = 5514

    class Config:  # Pydantic iç yapılandırma sınıfı
        env_file = ".env"  # Ortam değişkenlerini .env'den yükler
        case_sensitive = False  # Büyük/küçük harf duyarsız okuma


@lru_cache()  # Sonucu önbellekler, bir kez çalışır
def get_settings() -> Settings:
    """
    Ayarları cache'le — her request'te .env okumayı önler.
    FastAPI dependency injection ile kullanılır.
    """
    return Settings()


# Modül düzeyinde kullanım için
settings = get_settings()  # Global ayar nesnesi