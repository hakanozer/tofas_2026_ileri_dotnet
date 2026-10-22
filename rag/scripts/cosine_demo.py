import numpy as np  # Sayısal hesaplamalar için


def cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    """İki vektör arasındaki cosine similarity."""
    return float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))


# Demo: embedding vektörleri karşılaştırması
from sentence_transformers import SentenceTransformer  # Metin embedding modeli

model = SentenceTransformer("paraphrase-multilingual-mpnet-base-v2")

sorgu  = "terletmeyen spor ayakkabı"
urun1  = "Nike hafif ve nefes alan spor ayakkabı. Yaz sporları için idealdir."
urun2  = "Adidas kışlık ayakkabı terletmez. Su geçirmez ve sıcak tutar"

v_sorgu = model.encode(sorgu)
v_urun1 = model.encode(urun1)
v_urun2 = model.encode(urun2)

print(f"Sorgu ↔ Ürün 1 :   {cosine_similarity(v_sorgu, v_urun1):.4f}")
print(f"Sorgu ↔ Ürün 2 : {cosine_similarity(v_sorgu, v_urun2):.4f}")
# Beklenen: Ürün 1 skoru > Ürün 2 skoru