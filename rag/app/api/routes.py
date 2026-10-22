"""
API route'ları — Session tabanlı Agent entegrasyonu.
"""
import uuid
import structlog

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel, Field
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import text
import httpx

from app.core.config import settings
from app.db.session import get_db
from app.rag.pipeline import rag_pipeline
from app.rag.vector_store import vector_store
from app.agent.planner import commerce_agent
from app.core.rate_limit import session_rate_limiter, limiter

logger = structlog.get_logger(__name__)
router = APIRouter()


# ---------------------------------------------------------------------------
# Şemalar — RAG (mevcut)
# ---------------------------------------------------------------------------

class QueryRequest(BaseModel):
    query: str
    max_results: int = 5


class ProductResult(BaseModel):
    product_id: str
    name: str
    brand: str
    price: float
    similarity_score: float


class QueryResponse(BaseModel):
    answer: str
    intent: str
    products: list[ProductResult]
    total_found: int


# ---------------------------------------------------------------------------
# Şemalar — Agent (yeni)
# ---------------------------------------------------------------------------

class SessionStartResponse(BaseModel):
    """Yeni bir session başlatıldığında dönen yanıt."""
    session_id: str
    message: str = "Oturum başlatıldı. Sorularınızı sorabilirsiniz."


class AgentQueryRequest(BaseModel):
    """Agent'a gönderilecek mesaj."""
    session_id: str = Field(..., description="Önceki /agent/session'dan alınan oturum kimliği")
    query: str = Field(..., description="Kullanıcı sorusu")


class AgentQueryResponse(BaseModel):
    """Agent yanıtı."""
    session_id: str
    answer: str
    intent: str          # tespit edilen kullanıcı niyeti
    tools_used: list[str]
    step_count: int
    steps: list[dict]    # her adımın tool/input/output detayı


class SessionDeleteResponse(BaseModel):
    session_id: str
    deleted: bool
    message: str


# ---------------------------------------------------------------------------
# Mevcut endpoint'ler
# ---------------------------------------------------------------------------

@router.get("/health", tags=["health"])
async def health_check(db: AsyncSession = Depends(get_db)) -> dict:
    """Sistem bileşenlerinin sağlık durumunu döner."""
    llm_status = "offline"
    try:
        async with httpx.AsyncClient(timeout=3.0) as client:
            r = await client.get(f"{settings.ollama_base_url}/api/tags")
            if r.status_code == 200:
                llm_status = "online"
    except Exception:
        pass

    db_status = "disconnected"
    try:
        await db.execute(text("SELECT 1"))
        db_status = "connected"
    except Exception:
        pass

    return {
        "api": "online",
        "database": db_status,
        "llm": llm_status,
        "vector_store": "not_initialized",
        "active_agent_sessions": commerce_agent.active_session_count,
    }


@router.post("/query", response_model=QueryResponse, tags=["assistant"])
@limiter.limit("10/minute")
async def process_query(
    request: Request,
    payload: QueryRequest,
    db: AsyncSession = Depends(get_db),
) -> QueryResponse:
    """Kullanıcı sorgusunu RAG pipeline ile işler (session'sız, stateless)."""
    if not payload.query.strip():
        raise HTTPException(status_code=400, detail="Sorgu boş olamaz.")

    try:
        result = await rag_pipeline.run(
            query=payload.query,
            db=db,
            top_k=payload.max_results,
        )
        return QueryResponse(
            answer=result.answer,
            intent=result.intent.intent.value,
            products=[
                ProductResult(
                    product_id=p.product_id,
                    name=p.name,
                    brand=p.brand,
                    price=p.price,
                    similarity_score=round(p.similarity_score, 4),
                )
                for p in result.products
            ],
            total_found=result.total_found,
        )
    except Exception as e:
        logger.exception(
            "query_processing_failed",
            query=payload.query,
            max_results=payload.max_results,
            error=str(e),
        )
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/products", tags=["products"])
async def list_products(
    db: AsyncSession = Depends(get_db),
    limit: int = 10,
    offset: int = 0,
) -> dict:
    """Ürünleri sayfalı olarak listeler."""
    from sqlalchemy import select
    from app.db.models import Product

    result = await db.execute(
        select(Product).where(Product.is_active == True).limit(limit).offset(offset)
    )
    products = result.scalars().all()
    return {
        "items": [
            {"id": str(p.id), "name": p.name, "brand": p.brand,
             "price": p.price, "category": p.category}
            for p in products
        ],
        "count": len(products),
    }


# ---------------------------------------------------------------------------
# Agent endpoint'leri (yeni — session tabanlı)
# ---------------------------------------------------------------------------

@router.post("/agent/session", response_model=SessionStartResponse, tags=["agent"])
async def start_session() -> SessionStartResponse:
    """
    Yeni bir konuşma oturumu başlatır.

    Dönen session_id'yi sonraki tüm /agent/query isteklerinde kullanın.
    Her session kendi konuşma geçmişini bağımsız olarak saklar.
    Session'lar 30 dakika kullanılmazsa otomatik silinir.
    """
    
    session_id = str(uuid.uuid4())
    # Session'ı lazy oluşturuyoruz; ilk query geldiğinde kurulacak.
    # Şimdilik sadece benzersiz bir ID dönüyoruz.
    logger.info("session_id_generated", session_id=session_id)
    return SessionStartResponse(session_id=session_id)


@router.post("/agent/query", response_model=AgentQueryResponse, tags=["agent"], dependencies=[Depends(session_rate_limiter)])
async def agent_query(request: AgentQueryRequest) -> AgentQueryResponse:
    """
    Mevcut bir oturumda kullanıcı sorusunu agent'a iletir.

    Agent, bu oturuma ait konuşma geçmişini kullanarak yanıt üretir.
    Önceki sorulara atıfta bulunabilir, karşılaştırma yapabilir.

    Örnek akış:
    - Soru 1: "1000 TL altı spor ayakkabı öner"  → agent ürün önerir
    - Soru 2: "Nike ile Adidas'ı karşılaştır"    → agent önceki bağlamı hatırlar
    """
    if not request.query.strip():
        raise HTTPException(status_code=400, detail="Sorgu boş olamaz.")

    if not request.session_id.strip():
        raise HTTPException(status_code=400, detail="session_id boş olamaz.")

    try:
        response = await commerce_agent.run(
            session_id=request.session_id,
            user_input=request.query,
        )
    except Exception as e:
        logger.exception(
            "agent_query_failed",
            session_id=request.session_id,
            query=request.query,
            error=str(e),
        )
        raise HTTPException(status_code=500, detail=str(e))

    return AgentQueryResponse(
        session_id=request.session_id,
        answer=response.answer,
        intent=response.intent,
        tools_used=response.tools_used,
        step_count=len(response.steps),
        steps=response.steps,
    )


@router.delete("/agent/session/{session_id}", response_model=SessionDeleteResponse, tags=["agent"], dependencies=[Depends(session_rate_limiter)])
async def delete_session(session_id: str) -> SessionDeleteResponse:
    """
    Belirli bir oturumu ve konuşma geçmişini siler.

    Kullanıcı çıkış yaptığında veya yeni bir konuşma başlatmak istediğinde çağrılır.
    """
    deleted = commerce_agent.clear_session(session_id)
    return SessionDeleteResponse(
        session_id=session_id,
        deleted=deleted,
        message="Oturum silindi." if deleted else "Oturum bulunamadı veya zaten silinmiş.",
    )
    
    """ Robot bahçede gül kokusu fısıldar
    Role:
    Bağlam:
    Görev:
    Kısıt:
    Format:
    """