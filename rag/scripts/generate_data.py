"""
Demo ürün veri seti oluşturucu.
500 adet spor ürünü üretir ve data/raw/products.csv olarak kaydeder.

Çalıştırma: python scripts/generate_data.py
"""
import random  # Rastgele sayı üretimi
import json  # JSON okuma/yazma işlemleri
import pandas as pd
from faker import Faker

fake = Faker("tr_TR")
random.seed(42)

# ===== SABİT VERİ HAVUZLARI =====

BRANDS = [
    "Nike", "Adidas", "Puma", "New Balance", "Asics",
    "Under Armour", "Reebok", "Skechers", "Columbia", "Salomon",
]

CATEGORIES = {
    "Spor Ayakkabı": {
        "features_pool": [
            "terletmeyen", "hafif", "su geçirmez", "yüksek taban",
            "ortopedik", "kaymaz taban", "nefes alan", "amortisörlü",
        ],
        "seasons": ["yazlık", "kışlık", "4 mevsim"],
        "genders": ["erkek", "kadın", "unisex"],
        "price_range": (299, 3499),
    },
    "Spor Tişört": {
        "features_pool": [
            "nem emici", "hızlı kuruyan", "UV korumalı", "elastik",
            "reflektif bant", "antimikrobiyal", "hafif",
        ],
        "seasons": ["yazlık", "4 mevsim"],
        "genders": ["erkek", "kadın", "unisex"],
        "price_range": (149, 999),
    },
    "Spor Şort": {
        "features_pool": [
            "esnek bel", "fermuarlı cep", "hızlı kuruyan", "hafif",
            "geniş kesim", "dar kesim",
        ],
        "seasons": ["yazlık"],
        "genders": ["erkek", "kadın", "unisex"],
        "price_range": (99, 799),
    },
    "Koşu Ayakkabısı": {
        "features_pool": [
            "yastıklı taban", "karbon plaka", "hafif", "nefes alan",
            "amortisörlü", "geniş burun", "yüksek koşu verimi",
        ],
        "seasons": ["yazlık", "4 mevsim"],
        "genders": ["erkek", "kadın", "unisex"],
        "price_range": (499, 4999),
    },
}

COLORS = [
    "Siyah", "Beyaz", "Lacivert", "Kırmızı", "Gri",
    "Mavi", "Yeşil", "Turuncu", "Mor", "Sarı",
]

SIZES = {
    "Spor Ayakkabı":  ["36", "37", "38", "39", "40", "41", "42", "43", "44", "45"],
    "Koşu Ayakkabısı": ["36", "37", "38", "39", "40", "41", "42", "43", "44", "45"],
    "Spor Tişört":    ["XS", "S", "M", "L", "XL", "XXL"],
    "Spor Şort":      ["XS", "S", "M", "L", "XL", "XXL"],
}

ADJECTIVES = ["Profesyonel", "Ultra", "Pro", "Elite", "Speed", "Comfort", "Air"]
SERIES = ["X", "V2", "3.0", "Plus", "Max", "Lite", "Boost"]


def generate_product_name(brand: str, category: str) -> str:
    adj = random.choice(ADJECTIVES)
    ser = random.choice(SERIES)
    return f"{brand} {adj} {category.split()[0]} {ser}"


def generate_description(name: str, features: list[str], brand: str) -> str:
    feat_text = ", ".join(features[:3])
    return (
        f"{name}, {brand} tarafından üretilen yüksek performanslı bir üründür. "
        f"Öne çıkan özellikleri: {feat_text}. "
        f"Sporseverler ve günlük kullanım için idealdir. "
        f"Premium malzeme kullanımıyla uzun ömürlü dayanıklılık sunar."
    )


def generate_products(n: int = 500) -> list[dict]:
    products = []
    category_names = list(CATEGORIES.keys())

    for i in range(n):
        category = random.choice(category_names)
        cat_data = CATEGORIES[category]

        brand     = random.choice(BRANDS)
        name      = generate_product_name(brand, category)
        features  = random.sample(cat_data["features_pool"], k=random.randint(2, 5))
        season    = random.choice(cat_data["seasons"])
        gender    = random.choice(cat_data["genders"])
        color     = random.choice(COLORS)
        price_min, price_max = cat_data["price_range"]
        price     = round(random.uniform(price_min, price_max), -1)  # 10'un katı
        orig_price = price if random.random() > 0.3 else round(price * random.uniform(1.1, 1.5), -1)
        stock     = random.randint(0, 200)
        rating    = round(random.uniform(3.0, 5.0), 1)
        reviews   = random.randint(0, 500)
        sizes     = random.sample(SIZES[category], k=random.randint(4, len(SIZES[category])))

        products.append({
            "id": str(i + 1),
            "name": name,
            "brand": brand,
            "category": category,
            "description": generate_description(name, features, brand),
            "price": price,
            "original_price": orig_price,
            "stock_quantity": stock,
            "gender": gender,
            "season": season,
            "color": color,
            "sizes": json.dumps(sorted(sizes)),
            "features": json.dumps(features),
            "rating": rating,
            "review_count": reviews,
            "is_active": True,
        })

    return products


if __name__ == "__main__":
    import pathlib

    output_dir = pathlib.Path("data/raw")
    output_dir.mkdir(parents=True, exist_ok=True)

    print("Ürün verisi üretiliyor...")
    products = generate_products(500)
    df = pd.DataFrame(products)
    output_path = output_dir / "products.csv"
    df.to_csv(output_path, index=False, encoding="utf-8")

    print(f"✅ {len(df)} ürün kaydedildi: {output_path}")
    print(df.head(3).to_string())
    print(f"\nKategori dağılımı:\n{df['category'].value_counts()}")