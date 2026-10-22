"""
SQLAlchemy async veritabanı oturumu yönetimi.
Her HTTP isteği için bağımsız oturum sağlar.
"""
from __future__ import annotations

from contextlib import asynccontextmanager  # Uygulama yaşam döngüsü yönetimi
from typing import AsyncIterator  # Tip ipuçları için

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase  # ORM oturum ve temel sınıf

from app.core.config import settings

# Senkron DATABASE_URL'i async uyumlu hale getir
# postgresql:// → postgresql+asyncpg://
ASYNC_DATABASE_URL = settings.database_url.replace(
    "postgresql://", "postgresql+asyncpg://"
).replace("postgres://", "postgresql+asyncpg://")

# Async engine oluştur
engine = create_async_engine(
    ASYNC_DATABASE_URL,
    echo=settings.debug,      # SQL sorgularını logla (sadece dev'de)
    pool_size=10,              # Bağlantı havuzu boyutu
    max_overflow=20,           # Havuz dolunca ek bağlantı sayısı
    pool_pre_ping=True,        # Bağlantı kopukluğunu otomatik tespit et
)

# Session fabrikası
AsyncSessionLocal = async_sessionmaker(
    bind=engine,
    class_=AsyncSession,
    expire_on_commit=False,    # Commit sonrası attribute'lara erişimi koru
)


class Base(DeclarativeBase):
    """Tüm ORM modellerinin türeyeceği temel sınıf."""
    pass


async def get_db() -> AsyncIterator[AsyncSession]:
    """
    FastAPI dependency injection için veritabanı oturumu sağlar.
    Her request yeni bir session açar, kapanırken kapatır.

    Kullanım:
        @router.get("/")  # GET route endpoint
        async def endpoint(db: AsyncSession = Depends(get_db)):
            ...
    """
    async with AsyncSessionLocal() as session:
        try:
            yield session
            await session.commit()
        except Exception:
            await session.rollback()
            raise