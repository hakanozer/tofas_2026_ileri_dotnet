"""
Intent Detection Motoru.
Kullanıcı sorgusunu LLM ile analiz eder, yapılandırılmış veri çıkarır.
"""
from __future__ import annotations

import json  # JSON okuma/yazma işlemleri
import logging  # Uygulama loglama
import re  # Düzenli ifadeler (regex)

from app.llm.ollama_client import OllamaClient, ollama_client
from app.schemas.intent import IntentResult, IntentType, ProductFilters

logger = logging.getLogger(__name__)

# System promptu: LLM'e ne yapacağını söyleyen talimat
INTENT_SYSTEM_PROMPT = """Sen bir e-ticaret asistanının intent analiz motorusun.
Kullanıcının yazdığı sorguyu analiz et ve YALNIZCA geçerli JSON döndür.

JSON formatı:
{
  "intent": "product_search | price_compare | stock_check | product_detail | recommendation | general_question",
  "confidence": 0.0-1.0,
  "filters": {
    "category": "ürün kategorisi veya null",
    "min_price": sayı veya null,
    "max_price": sayı veya null,
    "brand": "marka veya null",
    "features": ["özellik1", "özellik2"],
    "size": "beden veya null",
    "color": "renk veya null",
    "gender": "erkek | kadın | unisex veya null",
    "season": "yazlık | kışlık | 4 mevsim veya null"
  }
}

KURALLAR:
- SADECE JSON döndür, açıklama yazma
- Sayısal değerleri Türk Lirası cinsinden al
- "ucuz", "uygun fiyatlı" → max_price: 500
- "pahalı", "premium", "kaliteli" → min_price: 1000
- JSON dışında HİÇBİR şey yazma
"""


class IntentDetector:
    """
    LLM tabanlı intent detection servisi.
    Her sorgu için LLM'e istek atar ve JSON çıktıyı parse eder.
    """

    def __init__(self, client: OllamaClient = ollama_client) -> None:
        self._client = client

    async def detect(self, query: str) -> IntentResult:
        """
        Kullanıcı sorgusunu analiz eder.

        Args:
            query: Kullanıcının yazdığı serbest metin

        Returns:
            IntentResult: Yapılandırılmış intent ve filtreler
        """
        logger.info("Intent detection başlatıldı: %s", query)
        
        raw_response = await self._client.chat(
            messages=[
                {"role": "system", "content": INTENT_SYSTEM_PROMPT},
                {"role": "user", "content": f"Sorgu: {query}"},
            ],
            temperature=0.0,  # Deterministik — her seferinde aynı çıktı
        )
        

        logger.debug("Ham LLM yanıtı: %s", raw_response)

        parsed = self._parse_response(raw_response, query)
        logger.info(
            "Intent tespit edildi: %s (güven: %.2f)",
            parsed.intent,
            parsed.confidence,
        )
        return parsed

    def _parse_response(self, raw: str, original_query: str) -> IntentResult:
        """
        LLM'in ham metin yanıtını IntentResult'a dönüştürür.
        LLM bazen JSON dışında metin de üretebilir — savunmalı parsing yapar.
        """
        # JSON bloğunu çıkar (LLM ```json ... ``` içinde verebilir)
        clean = self._extract_json(raw)

        try:
            data = json.loads(clean)
        except json.JSONDecodeError:
            logger.warning(
                "JSON parse hatası, fallback kullanılıyor. Ham yanıt: %s", raw
            )
            return self._fallback_result(original_query, raw)

        try:
            filters_data = data.get("filters", {})
            filters = ProductFilters(**filters_data)

            return IntentResult(
                intent=IntentType(data.get("intent", "product_search")),
                confidence=float(data.get("confidence", 0.5)),
                filters=filters,
                original_query=original_query,
                raw_llm_response=raw,
            )
        except Exception as e:
            logger.warning("IntentResult oluşturulamadı: %s", e)
            return self._fallback_result(original_query, raw)

    @staticmethod  # Nesneye bağlı olmayan metot
    def _extract_json(text: str) -> str:
        """Metinden JSON bloğunu çıkarır."""
        # ```json ... ``` bloğu varsa çıkar
        match = re.search(r"```(?:json)?\s*([\s\S]+?)\s*```", text)
        if match:
            return match.group(1)

        # { ile başlayan ilk bloğu bul
        match = re.search(r"\{[\s\S]*\}", text)
        if match:
            return match.group(0)

        return text.strip()

    @staticmethod  # Nesneye bağlı olmayan metot
    def _fallback_result(query: str, raw: str) -> IntentResult:
        """LLM parse edilemezse güvenli varsayılan değer döner."""
        return IntentResult(
            intent=IntentType.PRODUCT_SEARCH,
            confidence=0.3,
            filters=ProductFilters(),
            original_query=query,
            raw_llm_response=raw,
        )


# Uygulama genelinde paylaşılan singleton
intent_detector = IntentDetector()