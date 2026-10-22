"""
Veri analizi ve görselleştirme scripti.
Çalıştırma: python scripts/analyze_data.py
"""
import pathlib
import matplotlib  # Veri görselleştirme
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # Grafik çizimi için
import seaborn as sns
import pandas as pd

# start time
startTimer = pd.Timestamp.now()
sns.set_theme(style="whitegrid", palette="muted")

df = pd.read_csv("data/processed/products_clean.csv", encoding="utf-8")
output_dir = pathlib.Path("data/processed/charts")
output_dir.mkdir(parents=True, exist_ok=True)

fig, axes = plt.subplots(2, 2, figsize=(14, 10))
fig.suptitle("Ürün Veri Seti Analizi", fontsize=16, fontweight="bold")

# 1. Fiyat dağılımı
axes[0, 0].hist(df["price"], bins=30, color="#198754", alpha=0.8)
axes[0, 0].set_title("Fiyat Dağılımı (TL)")
axes[0, 0].set_xlabel("Fiyat (TL)")
axes[0, 0].set_ylabel("Ürün Sayısı")

# 2. Kategori pasta grafiği
cat_counts = df["category"].value_counts()
axes[0, 1].pie(cat_counts.values, labels=cat_counts.index, autopct="%1.0f%%", startangle=90)
axes[0, 1].set_title("Kategori Dağılımı")

# 3. Marka bazında ortalama fiyat
brand_price = df.groupby("brand")["price"].mean().sort_values(ascending=False).head(10)
axes[1, 0].barh(brand_price.index, brand_price.values, color="#0d6efd", alpha=0.8)
axes[1, 0].set_title("Marka Bazında Ortalama Fiyat")
axes[1, 0].set_xlabel("Ortalama Fiyat (TL)")

# 4. Rating dağılımı
axes[1, 1].hist(df["rating"], bins=20, color="#ffc107", alpha=0.8)
axes[1, 1].set_title("Rating Dağılımı (1-5)")
axes[1, 1].set_xlabel("Rating")

plt.tight_layout()
out = output_dir / "analysis_overview.png"
plt.savefig(out, dpi=100, bbox_inches="tight")
print(f"✅ Grafik kaydedildi: {out}")

# Temel istatistikler
print("\n📊 Temel İstatistikler:")
print(df[["price", "rating", "stock_quantity"]].describe().round(2))
print(f"\nEksik değer sayısı:\n{df.isnull().sum()}")

# end time
endTimer = pd.Timestamp.now()
print(f"\n⏱️ Analiz süresi: {endTimer - startTimer}")