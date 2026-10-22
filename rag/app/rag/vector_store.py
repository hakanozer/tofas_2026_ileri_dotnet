"""
FAISS vector store yönetimi.
Ürün embedding'lerini indexler ve benzerlik araması yapar.
"""
from __future__ import annotations

import logging
import pathlib
import pickle

import faiss
import numpy as np
import pandas as pd

from app.rag.embedder import embedding_service

logger = logging.getLogger(__name__)

INDEX_PATH = pathlib.Path("vector_store/products.faiss")
META_PATH = pathlib.Path("vector_store/products_meta.pkl")


class ProductVectorStore:
    """
    FAISS tabanlı ürün vector store.
    Cosine similarity = IndexFlatIP + normalize vectors
    """

    def __init__(self) -> None:
        self._index: faiss.IndexFlatIP | None = None
        self._metadata: list[dict] = []
        self._is_loaded = False

    def build(self, csv_path: str = "data/processed/products_for_embedding.csv") -> None:
        logger.info("FAISS index oluşturuluyor: %s", csv_path)

        df = pd.read_csv(csv_path, encoding="utf-8")

        texts = df["embedding_text"].tolist()
        product_ids = df["id"].astype(str).tolist()
        names = df["name"].tolist()
        prices = df["price"].tolist()
        categories = df.get("category", pd.Series([""] * len(df))).tolist()
        brands = df.get("brand", pd.Series([""] * len(df))).tolist()

        # =========================
        # 🔥 EMBEDDING
        # =========================
        vectors = embedding_service.embed_batch(texts, show_progress=True)

        # =========================
        # 🔥 FAISS SAFE FORMAT FIX
        # =========================
        vectors = np.array(vectors, dtype=np.float32)
        vectors = np.ascontiguousarray(vectors)

        # cosine similarity için normalize
        faiss.normalize_L2(vectors)

        # =========================
        # 🔥 INDEX
        # =========================
        dim = vectors.shape[1]
        self._index = faiss.IndexFlatIP(dim)
        self._index.add(vectors)

        # =========================
        # METADATA
        # =========================
        self._metadata = [
            {
                "idx": i,
                "product_id": product_ids[i],
                "name": names[i],
                "price": prices[i],
                "category": categories[i],
                "brand": brands[i],
            }
            for i in range(len(texts))
        ]

        # =========================
        # SAVE
        # =========================
        INDEX_PATH.parent.mkdir(parents=True, exist_ok=True)

        faiss.write_index(self._index, str(INDEX_PATH))

        with open(META_PATH, "wb") as f:
            pickle.dump(self._metadata, f)

        self._is_loaded = True

        logger.info(
            "FAISS index hazır: %d vektör, dim=%d",
            self._index.ntotal,
            dim,
        )

    def load(self) -> None:
        if not INDEX_PATH.exists():
            logger.warning("Index yok: build() çalıştırılmalı")
            return

        self._index = faiss.read_index(str(INDEX_PATH))

        with open(META_PATH, "rb") as f:
            self._metadata = pickle.load(f)

        self._is_loaded = True

        logger.info("FAISS index yüklendi: %d vektör", self._index.ntotal)

    def search(
        self,
        query_text: str,
        top_k: int = 5,
        score_threshold: float = 0.3,
    ) -> list[dict]:

        if not self._is_loaded or self._index is None:
            self.load()

        if self._index is None or self._index.ntotal == 0:
            logger.error("Index boş")
            return []

        # =========================
        # QUERY EMBEDDING FIX
        # =========================
        query_vector = embedding_service.embed_text(query_text)

        query_vector = np.array(query_vector, dtype=np.float32)
        query_vector = np.ascontiguousarray(query_vector)
        query_vector = query_vector.reshape(1, -1)

        faiss.normalize_L2(query_vector)

        scores, indices = self._index.search(query_vector, top_k)

        results = []

        for score, idx in zip(scores[0], indices[0]):
            if idx < 0:
                continue
            if score < score_threshold:
                continue

            meta = self._metadata[idx].copy()
            meta["score"] = float(score)
            results.append(meta)

        return results

    @property
    def total_products(self) -> int:
        if self._index is None:
            return 0
        return self._index.ntotal


vector_store = ProductVectorStore()