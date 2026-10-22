"""
AI Agent araçları (tools).
Her araç, agent'ın bir görevi gerçekleştirmek için çağırabileceği bir fonksiyondur.
LangChain @tool dekoratörü ile tanımlanır.
"""
from __future__ import annotations

from sqlalchemy.ext.asyncio import AsyncSession

import json  # JSON okuma/yazma işlemleri
import logging  # Uygulama loglama
from typing import TYPE_CHECKING  # Tip ipuçları için

from langchain_core.tools import tool

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession

logger = logging.getLogger(__name__)


# ─── Tool 1: Ürün Arama ────────────────────────────────────────────────────
@tool("search_products")
async def search_products(
    query: str
) -> str:
    """
    Ürün arama aracı. Veritabanında sorgu ve filtrelere göre ürün arar.
    Args:
        query: Kullanıcının serbest metin sorgusu
        filters: Intent'ten gelen filtreler (kategori, fiyat aralığı, marka vb.)
        db: Veritabanı oturumu

    Returns:
        JSON string formatında bulunan ürünler
    """
    # Örnek: Veritabanında sorgu ve filtreleme işlemleri yapılır
    # Bu kısımda gerçek veritabanı sorgusu yapılmalıdır
    products = [
        {"id": 1, "name": "Spor Ayakkabı", "price": 699, "brand": "Nike"},
        {"id": 2, "name": "Koşu Ayakkabısı", "price": 650, "brand": "Adidas"},
    ]
    logger.info(f"search_products tool called with query: {query} and filters:")
    return json.dumps(products)


# ─── Tool 2: Fiyat Sorgulama ───────────────────────────────────────────────
@tool("check_price")
async def check_price(input: str) -> str:
    """
    Fiyat sorgulama aracı. Belirli bir ürünün fiyatını döner.
    Args:
        product_id: Ürün ID'si

    Returns:
        Ürünün fiyatı
    """
    # Örnek: Veritabanından ürün fiyatı sorgulanır
    # Bu kısımda gerçek veritabanı sorgusu yapılmalıdır
    price = 699  # Örnek fiyat
    logger.info(f"check_price tool called with product_id:")
    return str(price)

# ─── Tool 3: Stok Kontrolü ─────────────────────────────────────────────────
@tool("check_stock")
async def check_stock(input: str) -> str:
    """
    Stok kontrol aracı. Belirli bir ürünün stok durumunu döner.
    Args:
        product_id: Ürün ID'si
        db: Veritabanı oturumu

    Returns:
        Ürünün stok durumu (örneğin: "In Stock", "Out of Stock")
    """
    # Örnek: Veritabanından ürün stok durumu sorgulanır
    # Bu kısımda gerçek veritabanı sorgusu yapılmalıdır
    stock_status = "In Stock"  # Örnek stok durumu
    logger.info(f"check_stock tool called with product_id: {input}")
    return stock_status


# ─── Tool 4: Ürün Karşılaştırma ────────────────────────────────────────────
@tool("compare_products")
async def compare_products(input: str) -> str:
    """
    Ürün karşılaştırma aracı. Birden fazla ürünün özelliklerini karşılaştırır.
    Args:
        product_ids: Karşılaştırılacak ürün ID'leri

    Returns:
        JSON string formatında karşılaştırma sonuçları
    """
    # Örnek: Veritabanından ürün detayları sorgulanır ve karşılaştırılır
    # Bu kısımda gerçek veritabanı sorgusu yapılmalıdır
    comparison = [
        {"id": 1, "name": "Spor Ayakkabı", "price": 699, "brand": "Nike", "stock": "In Stock"},
        {"id": 2, "name": "Koşu Ayakkabısı", "price": 650, "brand": "Adidas", "stock": "In Stock"},
    ]
    logger.info(f"compare_products tool called with product_ids: {input}")
    return json.dumps(comparison)


# pdf yazdırma aracı gibi ek araçlar da benzer şekilde tanımlanabilir
@tool("print_pdf")
async def print_pdf(input: str) -> str:
    """
    PDF yazdırma aracı. Verilen içeriği PDF formatında kaydeder.
    Args:
        content: PDF'e yazılacak içerik

    Returns:
        PDF dosyasının kaydedildiği yol
    """
    # Örnek: İçeriği PDF formatında kaydetme işlemi yapılır
    # Bu kısımda gerçek PDF oluşturma işlemi yapılmalıdır
    pdf_path = "/path/to/generated.pdf"  # Örnek PDF yolu
    logger.info(f"print_pdf tool called with content: {input}")
    return pdf_path

# Tüm araçlar listesi (agent'a verilecek)
ALL_TOOLS = [search_products, check_price, check_stock, compare_products, print_pdf]
