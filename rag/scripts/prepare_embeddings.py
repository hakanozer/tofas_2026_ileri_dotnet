"""
Embedding hazırlık scripti.
Ürünlerin embedding metinlerini products_for_embedding.csv olarak kaydeder.
Modül 4'te bu dosya vektöre dönüştürülecek.

Çalıştırma: python scripts/prepare_embeddings.py
"""
import pathlib
import pandas as pd

df = pd.read_csv("data/processed/products_clean.csv", encoding="utf-8")

# Yalnızca aktif ve stoklu ürünler
df_active = df[df["is_active"] == True].copy()

# Embedding metni uzunluğunu kontrol et
df_active["embedding_text_len"] = df_active["embedding_text"].str.len()
print(f"Ortalama embedding metni uzunluğu: {df_active['embedding_text_len'].mean():.0f} karakter")

out = pathlib.Path("data/processed/products_for_embedding.csv")
df_active[["id", "name", "brand", "category", "price", "embedding_text"]].to_csv(
    out, index=False, encoding="utf-8"
)
print(f"✅ {len(df_active)} ürün embedding için hazırlandı: {out}")