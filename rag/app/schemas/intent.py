"""
Intent ve filtre şemaları.
LLM çıktısı bu şemalara parse edilir.
"""
from __future__ import annotations

from enum import Enum  # Sabit değer kümeleri için
from pydantic import BaseModel, Field  # Veri doğrulama ve şema tanımı


class IntentType(str, Enum):
    """Desteklenen intent türleri."""
    PRODUCT_SEARCH   = "product_search"    # Ürün arama
    PRICE_COMPARE    = "price_compare"     # Fiyat karşılaştırma
    STOCK_CHECK      = "stock_check"       # Stok sorgulama
    PRODUCT_DETAIL   = "product_detail"    # Ürün detayı
    RECOMMENDATION   = "recommendation"   # Genel öneri
    GENERAL_QUESTION = "general_question"  # Genel soru


class ProductFilters(BaseModel):
    """Kullanıcı sorgusundan çıkarılan ürün filtreleri."""
    category: str | None = Field(None, description="Ürün kategorisi: spor ayakkabı, tişört, vb.")
    min_price: float | None = Field(None, ge=0, description="Minimum fiyat (TL)")
    max_price: float | None = Field(None, ge=0, description="Maksimum fiyat (TL)")
    brand: str | None = Field(None, description="Marka adı")
    features: list[str] = Field(default_factory=list, description="İstenen özellikler")
    size: str | None = Field(None, description="Beden bilgisi")
    color: str | None = Field(None, description="Renk tercihi")
    gender: str | None = Field(None, description="Cinsiyet: erkek, kadın, unisex")
    season: str | None = Field(None, description="Sezon: yazlık, kışlık, 4 mevsim")


class IntentResult(BaseModel):  # Intent analizi sonuç şeması
    """LLM intent analizi sonucu."""
    intent: IntentType
    confidence: float = Field(ge=0.0, le=1.0, description="Güven skoru (0-1)")
    filters: ProductFilters = Field(default_factory=ProductFilters)
    original_query: str
    language: str = Field(default="tr", description="Sorgu dili")
    raw_llm_response: str | None = Field(None, description="Ham LLM yanıtı (debug için)")