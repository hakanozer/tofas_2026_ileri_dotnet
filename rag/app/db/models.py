"""
SQLAlchemy ORM modelleri.
Projemizin veritabanı tabloları burada tanımlanır.
"""
from __future__ import annotations

import uuid  # Benzersiz kimlik üretimi
from datetime import datetime  # Tarih/saat işlemleri

from sqlalchemy import (  # SQLAlchemy ORM bileşenleri
    Boolean, DateTime, Float, ForeignKey, Integer,
    String, Text, func, ARRAY,
)
from sqlalchemy.dialects.postgresql import UUID, JSONB
from sqlalchemy.orm import Mapped, mapped_column, relationship  # ORM oturum ve temel sınıf

from app.db.session import Base


class Product(Base):
    """Ürün tablosu — e-ticaret kataloğu."""
    __tablename__ = "products"

    id: Mapped[uuid.UUID] = mapped_column(
        UUID(as_uuid=True), primary_key=True, default=uuid.uuid4
    )
    name: Mapped[str]          = mapped_column(String(255), nullable=False, index=True)
    brand: Mapped[str]         = mapped_column(String(100), nullable=False, index=True)
    category: Mapped[str]      = mapped_column(String(100), nullable=False, index=True)
    subcategory: Mapped[str | None] = mapped_column(String(100))
    description: Mapped[str]   = mapped_column(Text, nullable=False)
    price: Mapped[float]       = mapped_column(Float, nullable=False, index=True)
    original_price: Mapped[float | None] = mapped_column(Float)
    stock_quantity: Mapped[int] = mapped_column(Integer, default=0)
    is_active: Mapped[bool]    = mapped_column(Boolean, default=True)
    gender: Mapped[str | None] = mapped_column(String(20))   # erkek, kadın, unisex
    season: Mapped[str | None] = mapped_column(String(20))   # yazlık, kışlık, 4 mevsim
    color: Mapped[str | None]  = mapped_column(String(50))
    sizes: Mapped[list | None] = mapped_column(JSONB)         # ["38","39","40","41"]
    features: Mapped[list | None] = mapped_column(JSONB)      # ["su geçirmez","hafif"]
    image_url: Mapped[str | None] = mapped_column(String(500))
    rating: Mapped[float]      = mapped_column(Float, default=0.0)
    review_count: Mapped[int]  = mapped_column(Integer, default=0)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=func.now()
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=func.now(), onupdate=func.now()
    )

    # İlişkiler
    reviews: Mapped[list["Review"]] = relationship(
        "Review", back_populates="product", lazy="selectin"
    )

    def __repr__(self) -> str:
        return f""

    @property  # Salt okunur özellik tanımı
    def embedding_text(self) -> str:
        """
        FAISS embedding için kullanılacak birleşik metin.
        Modül 4'te bu metin vektöre dönüştürülecek.
        """
        features_str = ", ".join(self.features or [])
        return (
            f"{self.name}. {self.brand} markası. {self.category} kategorisi. "
            f"{self.description}. Özellikler: {features_str}. "
            f"Fiyat: {self.price} TL. Sezon: {self.season or 'belirsiz'}. "
            f"Renk: {self.color or 'belirsiz'}."
        )


class Review(Base):
    """Ürün yorumları tablosu."""
    __tablename__ = "reviews"

    id: Mapped[uuid.UUID] = mapped_column(
        UUID(as_uuid=True), primary_key=True, default=uuid.uuid4
    )
    product_id: Mapped[uuid.UUID] = mapped_column(
        UUID(as_uuid=True), ForeignKey("products.id", ondelete="CASCADE"), index=True
    )
    username: Mapped[str]  = mapped_column(String(100))
    rating: Mapped[int]    = mapped_column(Integer)        # 1-5
    comment: Mapped[str]   = mapped_column(Text)
    is_verified: Mapped[bool] = mapped_column(Boolean, default=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=func.now()
    )

    product: Mapped["Product"] = relationship("Product", back_populates="reviews")