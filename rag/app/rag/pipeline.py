"""
RAG Pipeline — Uçtan uca sorgu işleme.
Intent → Retrieval → LLM → Yanıt
"""
from __future__ import annotations

from dataclasses import dataclass  # Veri sınıfı tanımı için
from dataclasses import asdict

from sqlalchemy.ext.asyncio import AsyncSession
import structlog

from app.core.cache import cache
from app.llm.intent_detector import intent_detector
from app.llm.ollama_client import ollama_client
from app.rag.retriever import product_retriever, RetrievedProduct
from app.schemas.intent import IntentResult

logger = structlog.get_logger(__name__)

RAG_SYSTEM_PROMPT = """Kullanıcıya Türkçe yanıt ver, Sen AI Commerce Assistant'sın. Bir e-ticaret platformunun akıllı asistanısın.
Sana kullanıcının sorusu ve veritabanından bulunan ürün bilgileri verilecek.
Kullanıcıya nazik, samimi ve bilgilendirici bir dilde yanıt ver.
Öneri yaparken neden bu ürünü önerdiğini açıkla.
Fiyat avantajı varsa vurgula. Ürünün eksiklerini de dürüstçe belirt.
Yanıtı 3-4 paragraf ile sınırla."""


@dataclass
class RAGResponse:
    """RAG pipeline yanıtı."""
    answer: str
    intent: IntentResult
    products: list[RetrievedProduct]
    total_found: int


class RAGPipeline:
    """
    Tam RAG pipeline:
    1. Intent detection (LLM)
    2. Vector search (FAISS)
    3. DB retrieval (PostgreSQL)
    4. Response generation (LLM)
    """

    async def run(
        self,
        query: str,
        db: AsyncSession,
        top_k: int = 5,
    ) -> RAGResponse:
        """
        Kullanıcı sorgusunu uçtan uca işler.

        Args:
            query: Kullanıcının sorgusu
            db: Veritabanı oturumu
            top_k: Kaç ürün önerilsin

        Returns:
            RAGResponse: Yanıt, intent ve bulunan ürünler
        """
        # Normalize input and check cache
        logger.info("rag_pipeline_started", query=query, top_k=top_k)
        cache_prefix = "rag_response"
        normalized_query = query.strip().lower()
        try:
            cached = await cache.get(prefix=cache_prefix, query=normalized_query)
            if cached:
                logger.info("rag_cache_hit", query=normalized_query, cache_prefix=cache_prefix)
                intent = IntentResult.parse_obj(cached.get("intent"))
                products = [RetrievedProduct(**p) for p in (cached.get("products") or [])]
                return RAGResponse(
                    answer=cached.get("answer", ""),
                    intent=intent,
                    products=products,
                    total_found=int(cached.get("total_found", len(products))),
                )
        except Exception as e:
            logger.exception(
                "rag_cache_read_failed",
                query=normalized_query,
                cache_prefix=cache_prefix,
                error=str(e),
            )

        # Adım 1: Intent detection
        intent = await intent_detector.detect(query)
        logger.info("intent_detected", intent=str(intent.intent), filters=intent.filters)

        # Adım 2: Retrieval
        products = await product_retriever.retrieve(
            query_text=query,
            filters=intent.filters,
            db=db,
            top_k=top_k,
        )
        logger.info("retrieval_completed", product_count=len(products), top_k=top_k)

        # Adım 3: LLM ile yanıt üretimi
        answer = await self._generate_answer(query, products, intent)

        # Cache write
        try:
            cache_value = {
                "answer": answer,
                "intent": intent.model_dump() if hasattr(intent, "model_dump") else intent.dict(),
                "products": [asdict(p) for p in products],
                "total_found": len(products),
            }
            await cache.set(prefix=cache_prefix, query=normalized_query, value=cache_value, ttl=3600)
            logger.info("rag_cache_written", query=normalized_query, cache_prefix=cache_prefix, ttl=3600)
        except Exception as e:
            logger.exception(
                "rag_cache_write_failed",
                query=normalized_query,
                cache_prefix=cache_prefix,
                error=str(e),
            )

        return RAGResponse(
            answer=answer,
            intent=intent,
            products=products,
            total_found=len(products),
        )

    async def _generate_answer(
        self,
        query: str,
        products: list[RetrievedProduct],
        intent: IntentResult,
    ) -> str:
        """Bulunan ürünleri kullanarak LLM ile doğal dil yanıtı üretir."""
        if not products:
            return (
                "Üzgünüm, arama kriterlerinize uygun ürün bulunamadı. "
                "Filtreleri genişleterek tekrar deneyebilirsiniz."
            )

        # Ürün bilgilerini prompt'a ekle
        products_context = "\n\n".join([
            f"[Ürün {i+1}]\n{p.to_llm_context()}"
            for i, p in enumerate(products)
        ])

        user_message = (
            f"Kullanıcı sorusu: {query}\n\n"
            f"Veritabanında bulunan ürünler:\n{products_context}\n\n"
            f"Bu ürünleri değerlendirerek kullanıcıya öneri sun:"
        )

        answer = await ollama_client.chat(
            messages=[
                {"role": "system", "content": RAG_SYSTEM_PROMPT},
                {"role": "user",   "content": user_message},
            ],
            temperature=0.3,
        )

        return answer


# Singleton
rag_pipeline = RAGPipeline()