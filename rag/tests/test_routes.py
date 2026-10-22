import pytest
from httpx import AsyncClient
from unittest.mock import AsyncMock, MagicMock, patch

from fastapi import FastAPI

from app.api.routes import router


# -------------------------------------------------------------------
# Test APP
# -------------------------------------------------------------------

@pytest.fixture
def app():
    app = FastAPI()
    app.include_router(router)
    return app


@pytest.fixture
async def client(app):
    async with AsyncClient(app=app, base_url="http://test") as ac:
        yield ac


# -------------------------------------------------------------------
# /health
# -------------------------------------------------------------------

@pytest.mark.asyncio
async def test_health_check_success(client):

    mock_db = AsyncMock()
    mock_db.execute.return_value = True

    with patch("app.api.routes.get_db", return_value=mock_db):

        with patch("httpx.AsyncClient.get") as mock_get:
            mock_get.return_value.status_code = 200

            response = await client.get("/health")

    assert response.status_code == 200

    data = response.json()

    assert data["api"] == "online"
    assert data["database"] == "connected"
    assert data["llm"] == "online"


# -------------------------------------------------------------------
# /query
# -------------------------------------------------------------------

@pytest.mark.asyncio
async def test_process_query_success(client):

    mock_result = MagicMock()

    mock_result.answer = "Test cevap"
    mock_result.total_found = 1

    mock_result.intent.intent.value = "product_search"

    product = MagicMock()
    product.product_id = "1"
    product.name = "Nike Air"
    product.brand = "Nike"
    product.price = 1999.99
    product.similarity_score = 0.98765

    mock_result.products = [product]

    with patch("app.api.routes.rag_pipeline.run", new_callable=AsyncMock) as mock_run:

        mock_run.return_value = mock_result

        payload = {
            "query": "Nike ayakkabı öner",
            "max_results": 5
        }

        response = await client.post("/query", json=payload)

    assert response.status_code == 200

    data = response.json()

    assert data["answer"] == "Test cevap"
    assert data["intent"] == "product_search"
    assert data["total_found"] == 1

    assert len(data["products"]) == 1
    assert data["products"][0]["name"] == "Nike Air"


@pytest.mark.asyncio
async def test_process_query_empty_query(client):

    payload = {
        "query": "   ",
        "max_results": 5
    }

    response = await client.post("/query", json=payload)

    assert response.status_code == 400
    assert response.json()["detail"] == "Sorgu boş olamaz."


@pytest.mark.asyncio
async def test_process_query_exception(client):

    with patch("app.api.routes.rag_pipeline.run", new_callable=AsyncMock) as mock_run:

        mock_run.side_effect = Exception("Pipeline error")

        payload = {
            "query": "Test",
            "max_results": 5
        }

        response = await client.post("/query", json=payload)

    assert response.status_code == 500
    assert response.json()["detail"] == "Pipeline error"


# -------------------------------------------------------------------
# /products
# -------------------------------------------------------------------

@pytest.mark.asyncio
async def test_list_products(client):

    product = MagicMock()
    product.id = "1"
    product.name = "iPhone"
    product.brand = "Apple"
    product.price = 99999
    product.category = "Telefon"

    mock_scalars = MagicMock()
    mock_scalars.all.return_value = [product]

    mock_result = MagicMock()
    mock_result.scalars.return_value = mock_scalars

    mock_db = AsyncMock()
    mock_db.execute.return_value = mock_result

    with patch("app.api.routes.get_db", return_value=mock_db):

        response = await client.get("/products")

    assert response.status_code == 200

    data = response.json()

    assert data["count"] == 1
    assert data["items"][0]["name"] == "iPhone"


# -------------------------------------------------------------------
# /agent/session
# -------------------------------------------------------------------

@pytest.mark.asyncio
async def test_start_session(client):

    response = await client.post("/agent/session")

    assert response.status_code == 200

    data = response.json()

    assert "session_id" in data
    assert data["message"] == "Oturum başlatıldı. Sorularınızı sorabilirsiniz."


# -------------------------------------------------------------------
# /agent/query
# -------------------------------------------------------------------

@pytest.mark.asyncio
async def test_agent_query_success(client):

    mock_agent_response = MagicMock()

    mock_agent_response.answer = "Nike öneriyorum"
    mock_agent_response.intent = "recommendation"
    mock_agent_response.tools_used = ["search_products"]
    mock_agent_response.steps = [
        {
            "tool": "search_products",
            "input": "Nike",
            "output": "1 ürün bulundu"
        }
    ]

    with patch(
        "app.api.routes.commerce_agent.run",
        new_callable=AsyncMock
    ) as mock_run:

        mock_run.return_value = mock_agent_response

        payload = {
            "session_id": "abc123",
            "query": "Nike öner"
        }

        response = await client.post("/agent/query", json=payload)

    assert response.status_code == 200

    data = response.json()

    assert data["session_id"] == "abc123"
    assert data["answer"] == "Nike öneriyorum"
    assert data["intent"] == "recommendation"
    assert data["step_count"] == 1


@pytest.mark.asyncio
async def test_agent_query_empty_query(client):

    payload = {
        "session_id": "abc123",
        "query": "   "
    }

    response = await client.post("/agent/query", json=payload)

    assert response.status_code == 400
    assert response.json()["detail"] == "Sorgu boş olamaz."


@pytest.mark.asyncio
async def test_agent_query_empty_session_id(client):

    payload = {
        "session_id": "   ",
        "query": "Merhaba"
    }

    response = await client.post("/agent/query", json=payload)

    assert response.status_code == 400
    assert response.json()["detail"] == "session_id boş olamaz."


@pytest.mark.asyncio
async def test_agent_query_exception(client):

    with patch(
        "app.api.routes.commerce_agent.run",
        new_callable=AsyncMock
    ) as mock_run:

        mock_run.side_effect = Exception("Agent error")

        payload = {
            "session_id": "abc123",
            "query": "Test"
        }

        response = await client.post("/agent/query", json=payload)

    assert response.status_code == 500
    assert response.json()["detail"] == "Agent error"


# -------------------------------------------------------------------
# DELETE /agent/session/{session_id}
# -------------------------------------------------------------------

@pytest.mark.asyncio
async def test_delete_session_success(client):

    with patch(
        "app.api.routes.commerce_agent.clear_session"
    ) as mock_clear:

        mock_clear.return_value = True

        response = await client.delete("/agent/session/abc123")

    assert response.status_code == 200

    data = response.json()

    assert data["deleted"] is True
    assert data["message"] == "Oturum silindi."


@pytest.mark.asyncio
async def test_delete_session_not_found(client):

    with patch(
        "app.api.routes.commerce_agent.clear_session"
    ) as mock_clear:

        mock_clear.return_value = False

        response = await client.delete("/agent/session/abc123")

    assert response.status_code == 200

    data = response.json()

    assert data["deleted"] is False