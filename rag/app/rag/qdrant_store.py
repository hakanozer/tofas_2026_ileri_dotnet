# Qdrant tabanlı ürün vector store
import logging
from qdrant_client import QdrantClient
from qdrant_client.models import PointStruct, VectorParams, Distance
from qdrant_client.http.exceptions import ResponseHandlingException
import httpx
import pandas as pd
import psycopg2
from app.core.config import settings
from app.rag.embedder import embedding_service

logger = logging.getLogger(__name__)

QDRANT_COLLECTION = "products"
QDRANT_HOST = "localhost"
QDRANT_PORT = 6333

class ProductQdrantStore:
    def __init__(self):
        # Initialize Qdrant client (no compatibility flag to avoid unexpected kw errors)
        self.client = QdrantClient(host=QDRANT_HOST, port=QDRANT_PORT)
        self.collection = QDRANT_COLLECTION

    def build(self, csv_path: str = "data/processed/products_for_embedding.csv"):
        logger.info("Qdrant index oluşturuluyor: %s", csv_path)
        df = pd.read_csv(csv_path, encoding="utf-8")
        texts = df["embedding_text"].tolist()
        # Qdrant expects numeric IDs or UUIDs — keep as integers
        product_ids = df["id"].astype(int).tolist()
        names = df["name"].tolist()
        prices = df["price"].tolist()
        categories = df.get("category", pd.Series([""] * len(df))).tolist()
        brands = df.get("brand", pd.Series([""] * len(df))).tolist()
        vectors = embedding_service.embed_batch(texts, show_progress=True)
        # attempt to enrich payloads with DB UUIDs by matching name/brand/price
        db_id_map: dict[tuple, str] = {}
        try:
            conn = psycopg2.connect(settings.database_url)
            cur = conn.cursor()
            # fetch candidate products by name
            unique_names = list(set(names))
            if unique_names:
                sql = (
                    "SELECT id, name, brand, price FROM products WHERE name = ANY(%s)"
                )
                cur.execute(sql, (unique_names,))
                for rid, rname, rbrand, rprice in cur.fetchall():
                    key = (rname, rbrand, float(rprice) if rprice is not None else None)
                    db_id_map[key] = str(rid)
            cur.close()
            conn.close()
        except Exception:
            # if DB not available, continue without DB enrichment
            db_id_map = {}

        # Koleksiyon oluştur (bağlantı hatalarını yakala)
        try:
            # ensure vector size matches embedding service
            dim = vectors.shape[1] if hasattr(vectors, "shape") else embedding_service.embedding_dim
            self.client.recreate_collection(
                collection_name=self.collection,
                vectors_config=VectorParams(size=dim, distance=Distance.COSINE),
            )
        except (ResponseHandlingException, httpx.ConnectError) as e:
            raise RuntimeError(
                "Qdrant sunucusuna bağlanılamıyor. Lütfen `docker-compose up -d qdrant` ile başlatın."
            ) from e
        # Noktaları ekle
        points = []
        for i in range(len(texts)):
            payload = {
                "name": names[i],
                "price": prices[i],
                "category": categories[i],
                "brand": brands[i],
            }
            key = (names[i], brands[i], float(prices[i]) if prices[i] is not None else None)
            if key in db_id_map:
                payload["db_id"] = db_id_map[key]

            points.append(
                PointStruct(
                    id=product_ids[i],
                    vector=vectors[i].tolist(),
                    payload=payload,
                )
            )
        self.client.upsert(collection_name=self.collection, points=points)
        logger.info("Qdrant index hazır: %d vektör", len(points))

    def search(self, query_text: str, top_k: int = 5, score_threshold: float = 0.3):
        # Get query embedding and convert to plain Python list (Qdrant expects JSON-serializable vectors)
        query_vector = embedding_service.embed_text(query_text)
        try:
            qvec = query_vector.tolist()
        except Exception:
            # if it's already a list
            qvec = list(query_vector)
        hits = self.client.search(
            collection_name=self.collection,
            query_vector=qvec,
            limit=top_k,
            with_payload=True,
            score_threshold=score_threshold,
        )
        results = []
        for hit in hits:
            meta = hit.payload.copy() if hit.payload is not None else {}
            # normalize types
            if "price" in meta:
                try:
                    meta["price"] = float(meta["price"])
                except Exception:
                    pass

            meta["score"] = float(hit.score)
            meta["id"] = hit.id
            meta["product_id"] = str(hit.id)
            results.append(meta)
        return results

    @property
    def total_products(self) -> int:
        info = self.client.get_collection(self.collection)
        return info.vectors_count

qdrant_store = ProductQdrantStore()
