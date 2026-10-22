"""
Temizlenmiş ürün verisini PostgreSQL'e yükler.
Çalıştırma: python scripts/seed_database.py

Ön koşul:
  - docker compose up -d (PostgreSQL çalışıyor olmalı)
  - alembic upgrade head (tablolar oluşturulmuş olmalı)
  - python scripts/clean_data.py (temiz veri hazır olmalı)
"""
import asyncio  # Asenkron programlama
import json  # JSON okuma/yazma işlemleri
import uuid  # Benzersiz kimlik üretimi
import pathlib
import pandas as pd
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession, async_sessionmaker

from app.core.config import settings
from app.db.models import Product, Review
from faker import Faker

fake = Faker("tr_TR")

ASYNC_URL = settings.database_url.replace(
    "postgresql://", "postgresql+asyncpg://"
).replace("postgres://", "postgresql+asyncpg://")


async def seed_products(session: AsyncSession, df: pd.DataFrame) -> list[Product]:
    """DataFrame'deki ürünleri veritabanına ekler."""
    products = []
    for _, row in df.iterrows():
        product = Product(
            name=row["name"],
            brand=row["brand"],
            category=row["category"],
            description=row["description"],
            price=float(row["price"]),
            original_price=float(row.get("original_price", row["price"])),
            stock_quantity=int(row.get("stock_quantity", 0)),
            gender=row.get("gender") or None,
            season=row.get("season") or None,
            color=row.get("color") or None,
            sizes=json.loads(row.get("sizes", "[]")),
            features=json.loads(row.get("features", "[]")),
            rating=float(row.get("rating", 0.0)),
            review_count=int(row.get("review_count", 0)),
            is_active=True,
        )
        session.add(product)
        products.append(product)

    await session.flush()
    return products


async def seed_reviews(session: AsyncSession, products: list[Product]) -> None:
    """Her ürün için 1-5 adet örnek yorum ekler."""
    import random  # Rastgele sayı üretimi
    random.seed(42)

    comments = [
        "Ürün çok kaliteli, beklentilerimi karşıladı.",
        "Fiyat performans açısından mükemmel.",
        "Hızlı kargo, ürün açıklamayla birebir.",
        "Biraz dar geldi, bir beden büyük alın.",
        "Mükemmel ürün, herkese tavsiye ederim.",
        "Renk fotoğraftaki gibi, çok memnun kaldım.",
    ]

    for product in products:
        n_reviews = random.randint(1, 5)
        for _ in range(n_reviews):
            review = Review(
                product_id=product.id,
                username=fake.user_name(),
                rating=random.randint(3, 5),
                comment=random.choice(comments),
                is_verified=random.random() > 0.3,
            )
            session.add(review)


async def main() -> None:
    csv_path = pathlib.Path("data/processed/products_clean.csv")
    if not csv_path.exists():
        print("❌ Temiz veri bulunamadı. Önce: python scripts/clean_data.py")
        return

    df = pd.read_csv(csv_path, encoding="utf-8")
    print(f"📂 {len(df)} ürün yüklenecek...")

    engine = create_async_engine(ASYNC_URL, echo=False)
    session_factory = async_sessionmaker(engine, expire_on_commit=False)

    async with session_factory() as session:
        async with session.begin():
            products = await seed_products(session, df)
            await seed_reviews(session, products)

    await engine.dispose()
    print(f"✅ {len(products)} ürün ve yorumlar veritabanına yüklendi.")


if __name__ == "__main__":
    asyncio.run(main())