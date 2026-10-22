"""
Veri temizleme pipeline'ı.
Ham CSV'yi alır, temizler, data/processed/products_clean.csv olarak kaydeder.

Çalıştırma: python scripts/clean_data.py
"""
import json  # JSON okuma/yazma işlemleri
import pathlib
import pandas as pd
import numpy as np  # Sayısal hesaplamalar için


def load_raw(path: str = "data/raw/products.csv") -> pd.DataFrame:
    return pd.read_csv(path, encoding="utf-8")


def clean_products(df: pd.DataFrame) -> pd.DataFrame:
    print(f"Başlangıç: {len(df)} satır, {df.shape[1]} sütun")

    # 1. Tekrar eden satırları kaldır
    before = len(df)
    df = df.drop_duplicates(subset=["name", "brand"])
    print(f"Duplicate temizleme: {before - len(df)} satır kaldırıldı")

    # 2. Gerekli alanları doğrula
    required = ["name", "brand", "category", "description", "price"]
    df = df.dropna(subset=required)

    # 3. Fiyat temizleme
    df["price"] = pd.to_numeric(df["price"], errors="coerce")
    df["original_price"] = pd.to_numeric(df["original_price"], errors="coerce")
    df = df[df["price"] > 0]   # Sıfır ve negatif fiyatları kaldır
    df["original_price"] = df["original_price"].fillna(df["price"])

    # 4. Stok normalizasyon
    df["stock_quantity"] = df["stock_quantity"].fillna(0).astype(int).clip(lower=0)

    # 5. Rating temizleme (1-5 aralığı)
    df["rating"] = df["rating"].fillna(0.0).clip(lower=0.0, upper=5.0).round(1)
    df["review_count"] = df["review_count"].fillna(0).astype(int).clip(lower=0)

    # 6. Metin alanları temizle
    text_cols = ["name", "brand", "category", "description", "gender", "season", "color"]
    for col in text_cols:
        if col in df.columns:
            df[col] = df[col].fillna("").str.strip()

    # 7. JSON sütunlarını doğrula
    for col in ["features", "sizes"]:
        if col in df.columns:
            df[col] = df[col].apply(_validate_json_list)

    # 8. İskonto oranı ekle
    df["discount_pct"] = ((df["original_price"] - df["price"]) / df["original_price"] * 100).round(1)
    df["discount_pct"] = df["discount_pct"].clip(lower=0)

    # 9. Embedding metni oluştur
    df["embedding_text"] = df.apply(_build_embedding_text, axis=1)

    print(f"Temizleme tamamlandı: {len(df)} satır kaldı")
    return df.reset_index(drop=True)


def _validate_json_list(value) -> str:
    """JSON string'i doğrula; bozuksa boş liste döndür."""
    if pd.isna(value):
        return "[]"
    try:
        parsed = json.loads(value)
        if isinstance(parsed, list):
            return json.dumps(parsed)
        return "[]"
    except (json.JSONDecodeError, TypeError):
        return "[]"


def _build_embedding_text(row: pd.Series) -> str:
    """FAISS embedding için zenginleştirilmiş birleşik metin."""
    features = json.loads(row.get("features", "[]"))
    return (
        f"{row['name']}. Marka: {row['brand']}. "
        f"Kategori: {row['category']}. "
        f"{row['description']} "
        f"Özellikler: {', '.join(features)}. "
        f"Fiyat: {row['price']} TL. "
        f"Sezon: {row.get('season', '')}. "
        f"Renk: {row.get('color', '')}. "
        f"Cinsiyet: {row.get('gender', '')}."
    )


if __name__ == "__main__":
    output_dir = pathlib.Path("data/processed")
    output_dir.mkdir(parents=True, exist_ok=True)

    df_raw   = load_raw()
    df_clean = clean_products(df_raw)

    out_path = output_dir / "products_clean.csv"
    df_clean.to_csv(out_path, index=False, encoding="utf-8")
    print(f"\n✅ Temiz veri kaydedildi: {out_path}")

    # Özet istatistikler
    print("\n📊 Özet İstatistikler:")
    print(df_clean[["price", "rating", "review_count", "discount_pct"]].describe().round(2))