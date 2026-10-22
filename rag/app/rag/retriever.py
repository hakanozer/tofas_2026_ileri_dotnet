"""
Retriever servisi.
Qdrant vektör aramasını PostgreSQL sorgusuyla birleştirir.
"""
from __future__ import annotations

import logging  # Uygulama loglama
from dataclasses import dataclass, field  # Veri sınıfı tanımı için

from sqlalchemy import select  # SQLAlchemy ORM bileşenleri
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models import Product, Review
from app.rag.qdrant_store import qdrant_store
from app.schemas.intent import ProductFilters

logger = logging.getLogger(__name__)


@dataclass
class RetrievedProduct:
    """Retrieval sonucu — tek bir ürün."""
    product_id: str
    name: str
    brand: str
    category: str
    description: str
    price: float
    original_price: float
    features: list[str]
    season: str | None
    color: str | None
    gender: str | None
    rating: float
    review_count: int
    stock_quantity: int
    similarity_score: float
    top_reviews: list[str] = field(default_factory=list)

    @property  # Salt okunur özellik tanımı
    def discount_pct(self) -> float:
        if self.original_price > self.price:
            return round((self.original_price - self.price) / self.original_price * 100, 1)
        return 0.0

    def to_llm_context(self) -> str:
        """LLM prompt'una eklenecek metin formatı."""
        features = ", ".join(self.features[:4])
        reviews_text = " | ".join(self.top_reviews[:2])
        discount = f" (İndirim: %{self.discount_pct:.0f})" if self.discount_pct > 0 else ""
        return (
            f"Ürün: {self.name}\n"
            f"Marka: {self.brand} | Kategori: {self.category}\n"
            f"Fiyat: {self.price} TL{discount}\n"
            f"Özellikler: {features}\n"
            f"Sezon: {self.season or 'Genel'} | Renk: {self.color or '-'}\n"
            f"Rating: {self.rating}/5 ({self.review_count} yorum)\n"
            f"Müşteri görüşleri: {reviews_text or 'Henüz yorum yok'}\n"
        )


class ProductRetriever:
    """
    LLM intent sonucunu kullanarak en uygun ürünleri getirir.
    1. Qdrant'ta vektör araması yapar
    2. Filtre uygular (fiyat, sezon vb.)
    3. PostgreSQL'den tam ürün detayını alır
    """

    async def retrieve(
        self,
        query_text: str,
        filters: ProductFilters,
        db: AsyncSession,
        top_k: int = 5,
    ) -> list[RetrievedProduct]:
        """
        Sorgu metnine göre filtrelenmiş ürünleri getirir.
        """
        logger.info("Qdrant : 1. Aşama")
        # 1. Qdrant ile adayları bul (geniş havuz)
        candidates = qdrant_store.search(query_text, top_k=top_k * 3)
        if not candidates:
            logger.warning("Qdrant araması sonuç döndürmedi: %s", query_text)
            return []

        # 2. Kandidat meta bilgilerini topla (qdrant payload: name, brand, price, optional db_id)
        score_map: dict[tuple, float] = {}
        candidate_db_ids: list[str] = []
        candidate_names: list[str] = []
        for c in candidates:
            name = c.get("name")
            brand = c.get("brand")
            price = float(c.get("price")) if c.get("price") is not None else None
            key = (name, brand, price)
            score_map[key] = float(c.get("score", 0.0))
            dbid = c.get("db_id") or c.get("product_id")
            if dbid:
                candidate_db_ids.append(dbid)
            if name is not None:
                candidate_names.append(name)

        # 3. PostgreSQL'den detayları al
        if candidate_db_ids:
            # prefer direct db_id match if available (db_id is UUID string)
            stmt = select(Product).where(Product.id.in_(candidate_db_ids), Product.is_active == True)
        else:
            # fallback to name based matching
            stmt = select(Product).where(Product.name.in_(candidate_names), Product.is_active == True)

        # 4. Fiyat filtreleri
        if filters.max_price is not None:
            stmt = stmt.where(Product.price <= filters.max_price)
        if filters.min_price is not None:
            stmt = stmt.where(Product.price >= filters.min_price)

        # 5. Sezon filtresi
        if filters.season:
            stmt = stmt.where(
                (Product.season == filters.season) | (Product.season == "4 mevsim")
            )

        # 6. Cinsiyet filtresi
        # Treat 'unisex' as non-restrictive (allow all genders). Otherwise allow exact match or product marked 'unisex'.
        if filters.gender and filters.gender.lower() != "unisex":
            stmt = stmt.where(
                (Product.gender == filters.gender) | (Product.gender == "unisex")
            )

        result = await db.execute(stmt)
        products = result.scalars().all()

        # 7. Yorum örneklerini al ve sonuçları oluştur
        retrieved: list[RetrievedProduct] = []
        for product in products:
            top_reviews = [
                r.comment for r in (product.reviews or [])
                if r.rating >= 4
            ][:2]

            # match product to a candidate by (name, brand, price)
            key = (product.name, product.brand, float(product.price) if product.price is not None else None)
            similarity = score_map.get(key, 0.0)

            retrieved.append(
                RetrievedProduct(
                    product_id=str(product.id),
                    name=product.name,
                    brand=product.brand,
                    category=product.category,
                    description=product.description,
                    price=product.price,
                    original_price=product.original_price or product.price,
                    features=product.features or [],
                    season=product.season,
                    color=product.color,
                    gender=product.gender,
                    rating=product.rating,
                    review_count=product.review_count,
                    stock_quantity=product.stock_quantity,
                    similarity_score=similarity,
                    top_reviews=top_reviews,
                )
            )

        # 8. Benzerlik skoruna göre sırala
        retrieved.sort(key=lambda x: x.similarity_score, reverse=True)
        return retrieved[:top_k]


# Singleton
product_retriever = ProductRetriever()