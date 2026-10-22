"""
Embedding servisi.
Metni vektöre dönüştürür. Singleton pattern ile model bir kez yüklenir.
"""
from __future__ import annotations

import logging
import numpy as np
from sentence_transformers import SentenceTransformer

from app.core.config import settings

logger = logging.getLogger(__name__)


class EmbeddingService:
    """
    sentence-transformers tabanlı embedding servisi.
    Model lazy-load edilir ve reuse edilir.
    """

    def __init__(self, model_name: str = settings.embedding_model) -> None:
        self.model_name = model_name
        self._model: SentenceTransformer | None = None
        logger.info("EmbeddingService başlatıldı: %s", model_name)

    @property
    def model(self) -> SentenceTransformer:
        """Lazy loading"""
        if self._model is None:
            logger.info("Embedding modeli yükleniyor: %s", self.model_name)
            self._model = SentenceTransformer(self.model_name)
            logger.info("Embedding modeli hazır.")
        return self._model

    def embed_text(self, text: str) -> np.ndarray:
        """
        Tek metni embedding vektörüne çevirir.
        """
        vector = self.model.encode(
            text,
            show_progress_bar=False,
            convert_to_numpy=True
        )

        # Ensure safe numeric format for downstream vector stores
        vector = np.array(vector, dtype=np.float32)
        vector = np.ascontiguousarray(vector)

        return vector

    def embed_batch(
        self,
        texts: list[str],
        batch_size: int = 64,
        show_progress: bool = True,
    ) -> np.ndarray:
        """
        Toplu embedding üretimi.
        """
        logger.info("%d metin embed ediliyor...", len(texts))

        vectors = self.model.encode(
            texts,
            batch_size=batch_size,
            show_progress_bar=show_progress,
            convert_to_numpy=True,
        )

        # 🔥 FAISS SAFE FORMAT
        vectors = np.array(vectors, dtype=np.float32)
        vectors = np.ascontiguousarray(vectors)

        return vectors

    @property
    def embedding_dim(self) -> int:
        """Embedding boyutu"""
        return self.model.get_sentence_embedding_dimension()


# Singleton instance
embedding_service = EmbeddingService()