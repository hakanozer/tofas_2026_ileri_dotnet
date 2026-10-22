# 📋 RAG Sistemi — Kurumsal Bilgi Yönetimi Projesi

> **Hedef:** Personellerin kendi birimlerine doküman yükleyebildiği, kategori bazlı organize edebildiği ve yapay zeka destekli RAG (Retrieval-Augmented Generation) yöntemiyle sorgulayabildiği, PDF/Word/Excel çıktısı üretebilen tam yığın bir sistem.

---

## 🧠 Sistem Mimarisi (Genel Bakış)

```
[React Frontend]
      ↓ HTTP/REST
[FastAPI Backend]
      ↓             ↓
[PostgreSQL]   [Qdrant Vector DB]
(meta, kullanıcı,  (embedding'ler,
 kategori, log)     chunk'lar)
      ↓
[LangChain Agent + Embedding Model]
      ↓
[Claude / OpenAI API]
```

**Teknoloji Yığını:**
- **Frontend:** React + TailwindCSS
- **Backend:** FastAPI (Python)
- **Veritabanı:** PostgreSQL (meta veri), Qdrant (vektör)
- **AI Katmanı:** LangChain, sentence-transformers, Claude/OpenAI
- **Dosya Formatları:** PDF, DOCX, TXT, XLSX
- **Çıktı Üretimi:** reportlab (PDF), python-docx (Word), openpyxl (Excel)
- **Altyapı:** Docker Compose (mevcut yapı korunacak)

---

## 📁 Klasör Yapısı

```
project/
├── backend/
│   ├── app/
│   │   ├── main.py                  # FastAPI giriş noktası
│   │   ├── config.py                # Ortam değişkenleri (.env okuma)
│   │   ├── database.py              # PostgreSQL bağlantısı (SQLAlchemy)
│   │   ├── vector_store.py          # Qdrant bağlantısı ve işlemleri
│   │   ├── models/                  # SQLAlchemy ORM modelleri
│   │   │   ├── user.py
│   │   │   ├── unit.py
│   │   │   ├── category.py
│   │   │   ├── document.py
│   │   │   └── query_log.py
│   │   ├── schemas/                 # Pydantic istek/yanıt şemaları
│   │   │   ├── user.py
│   │   │   ├── document.py
│   │   │   ├── category.py
│   │   │   └── query.py
│   │   ├── routers/                 # API endpoint grupları
│   │   │   ├── auth.py
│   │   │   ├── documents.py
│   │   │   ├── categories.py
│   │   │   ├── query.py
│   │   │   └── export.py
│   │   ├── services/                # İş mantığı katmanı
│   │   │   ├── document_processor.py   # Dosya parse + chunk
│   │   │   ├── embedding_service.py    # Embedding üretimi
│   │   │   ├── rag_service.py          # RAG sorgu zinciri
│   │   │   ├── agent_service.py        # LangChain Agent
│   │   │   └── export_service.py       # PDF/Word/Excel üretimi
│   │   └── utils/
│   │       ├── file_parsers.py      # PDF/DOCX/TXT/XLSX okuma
│   │       └── chunker.py           # Metin bölümleme stratejileri
│   ├── migrations/                  # Alembic migration dosyaları
│   ├── requirements.txt             # Mevcut requirements.txt + eklemeler
│   └── .env.example
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   ├── pages/
│   │   ├── hooks/
│   │   ├── services/                # API çağrı fonksiyonları
│   │   └── store/                   # Global state (Zustand/Context)
│   └── package.json
├── scripts/
│   └── init.sql                     # PostgreSQL başlangıç tabloları
├── docker-compose.yml               # Mevcut yapı korunacak
└── TASK.md
```

---

## 🚀 FAZLAR

---

## ✅ FAZ 1 — Altyapı ve Veritabanı Kurulumu

> **Amaç:** Docker ortamı çalışıyor, PostgreSQL tabloları oluşturulmuş, temel FastAPI uygulaması ayakta.

### 1.1 — Gereksinim Dosyalarını Tamamla

`requirements.txt` dosyasına aşağıdaki paketleri **ekle** (mevcut liste korunacak):

```
python-docx==1.1.2        # Word dosyası okuma ve yazma
reportlab==4.2.2          # PDF çıktısı üretme
openpyxl==3.1.2           # Excel okuma ve yazma
pypdf2==3.0.1             # PDF metin çıkarma (PyPDF2)
pdfplumber==0.11.0        # Tablo içeren PDF'ler için alternatif
python-jose[cryptography]==3.3.0   # JWT token
passlib[bcrypt]==1.7.4    # Parola hashleme
anthropic==0.28.0         # Claude API (isteğe bağlı)
openai==1.30.5            # OpenAI API (isteğe bağlı)
```

### 1.2 — `.env.example` Dosyası Oluştur

```env
# Veritabanı
DATABASE_URL=postgresql+asyncpg://aicommerce:changeme123@localhost:5432/aicommerce_db

# Qdrant
QDRANT_HOST=localhost
QDRANT_PORT=6333
QDRANT_COLLECTION_NAME=rag_documents

# AI Model
OPENAI_API_KEY=sk-...          # OpenAI kullanılacaksa
ANTHROPIC_API_KEY=sk-ant-...   # Claude kullanılacaksa
LLM_PROVIDER=openai            # "openai" veya "anthropic"
LLM_MODEL=gpt-4o               # Kullanılacak model adı

# Embedding
EMBEDDING_MODEL=sentence-transformers/paraphrase-multilingual-mpnet-base-v2
# Not: Bu model Türkçe dahil çok dilli destek sağlar

# JWT
SECRET_KEY=supersecretkeychangeme
ACCESS_TOKEN_EXPIRE_MINUTES=60

# Uygulama
MAX_FILE_SIZE_MB=50
CHUNK_SIZE=500                 # Kelime sayısı bazında bölümleme
CHUNK_OVERLAP=50               # Örtüşen kelime sayısı
TOP_K_RESULTS=5                # RAG'da döndürülecek chunk sayısı
```

### 1.3 — PostgreSQL Tablo Şemasını Oluştur

`scripts/init.sql` dosyasını oluştur. Tablolar:

```sql
-- Birimler (departmanlar)
CREATE TABLE units (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    description TEXT,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Kullanıcılar
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) NOT NULL UNIQUE,
    hashed_password TEXT NOT NULL,
    full_name VARCHAR(100),
    unit_id INTEGER REFERENCES units(id),
    role VARCHAR(20) DEFAULT 'user',  -- 'admin' veya 'user'
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Kategoriler (birim bazlı)
CREATE TABLE categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    unit_id INTEGER REFERENCES units(id) ON DELETE CASCADE,
    created_by INTEGER REFERENCES users(id),
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(name, unit_id)
);

-- Dokümanlar
CREATE TABLE documents (
    id SERIAL PRIMARY KEY,
    filename VARCHAR(255) NOT NULL,
    original_filename VARCHAR(255) NOT NULL,
    file_type VARCHAR(20) NOT NULL,  -- 'pdf', 'docx', 'txt', 'xlsx'
    file_size_bytes INTEGER,
    category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
    unit_id INTEGER REFERENCES units(id) ON DELETE CASCADE,
    uploaded_by INTEGER REFERENCES users(id),
    qdrant_collection VARCHAR(100),  -- hangi koleksiyona eklendi
    chunk_count INTEGER DEFAULT 0,   -- kaç parçaya bölündü
    status VARCHAR(20) DEFAULT 'processing',  -- 'processing', 'ready', 'error'
    error_message TEXT,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Sorgu logları
CREATE TABLE query_logs (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id),
    unit_id INTEGER REFERENCES units(id),
    question TEXT NOT NULL,
    answer TEXT,
    sources JSONB,               -- hangi dokümanlardan cevaplandı
    category_filter INTEGER,     -- filtre uygulandıysa kategori id
    response_time_ms INTEGER,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Başlangıç verisi: Örnek birim
INSERT INTO units (name, description) VALUES ('Genel', 'Genel kullanım birimi');
```

### 1.4 — FastAPI Temel Yapısını Kur

`backend/app/main.py`:

```python
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from app.routers import auth, documents, categories, query, export
from app.database import engine
from app import models

app = FastAPI(title="Kurumsal RAG Sistemi", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3000"],  # React dev server
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(auth.router, prefix="/api/auth", tags=["Auth"])
app.include_router(documents.router, prefix="/api/documents", tags=["Documents"])
app.include_router(categories.router, prefix="/api/categories", tags=["Categories"])
app.include_router(query.router, prefix="/api/query", tags=["Query"])
app.include_router(export.router, prefix="/api/export", tags=["Export"])

@app.get("/health")
async def health_check():
    return {"status": "ok"}
```

**FAZ 1 Tamamlanma Kriteri:**
- [ ] `docker-compose up` → PostgreSQL + Qdrant ayakta
- [ ] `uvicorn app.main:app --reload` → API `/health` endpoint yanıt veriyor
- [ ] `/docs` adresinde Swagger UI açılıyor

---

## ✅ FAZ 2 — Kimlik Doğrulama ve Kullanıcı Yönetimi

> **Amaç:** Kullanıcılar sisteme kayıt olabilir, giriş yapabilir, JWT token alır.

### 2.1 — Auth Router (`routers/auth.py`)

Aşağıdaki endpoint'leri oluştur:

| Metod | Endpoint | Açıklama |
|-------|----------|-----------|
| POST | `/api/auth/register` | Yeni kullanıcı kaydı |
| POST | `/api/auth/login` | Giriş → JWT token döner |
| GET | `/api/auth/me` | Mevcut kullanıcı bilgisi (token gerekli) |

**Register isteği body:**
```json
{
  "email": "ali@sirket.com",
  "password": "güçlüparola123",
  "full_name": "Ali Yılmaz",
  "unit_id": 1
}
```

**Login yanıtı:**
```json
{
  "access_token": "eyJ...",
  "token_type": "bearer",
  "user": { "id": 1, "email": "...", "full_name": "...", "unit_id": 1, "role": "user" }
}
```

### 2.2 — JWT Middleware

`utils/auth.py` dosyasında:
- `create_access_token(data: dict)` → JWT oluşturur
- `verify_token(token: str)` → token doğrular, user_id döner
- `get_current_user(token)` → FastAPI dependency olarak kullanılır

**Her korumalı endpoint'te kullanım:**
```python
@router.get("/protected")
async def protected_route(current_user = Depends(get_current_user)):
    ...
```

### 2.3 — Birim Endpoint'leri (`routers/units.py`)

| Metod | Endpoint | Kısıtlama |
|-------|----------|-----------|
| GET | `/api/units` | Herkese açık |
| POST | `/api/units` | Sadece admin |
| GET | `/api/units/{id}` | Giriş yapmış kullanıcılar |

**FAZ 2 Tamamlanma Kriteri:**
- [ ] Kullanıcı kayıt ve giriş çalışıyor
- [ ] Token ile korumalı endpoint'e erişim başarılı
- [ ] Hatalı token → 401 Unauthorized döndürüyor

---

## ✅ FAZ 3 — Doküman Yükleme ve İşleme Pipeline'ı

> **Amaç:** Kullanıcı PDF/DOCX/TXT/XLSX yüklüyor → sistem metni çıkarıyor → chunk'lara bölüyor → embedding üretiyor → Qdrant'a kaydediyor.

### 3.1 — Dosya Ayrıştırıcılar (`utils/file_parsers.py`)

Her dosya tipi için ayrı fonksiyon yaz:

```python
def parse_pdf(file_path: str) -> str:
    """
    pdfplumber kullanarak PDF'den metin çıkarır.
    Tablo içeren sayfalarda da çalışır.
    Her sayfanın metnini birleştirir.
    """

def parse_docx(file_path: str) -> str:
    """
    python-docx ile Word dosyasından metin çıkarır.
    Paragrafları ve tabloları okur.
    """

def parse_txt(file_path: str) -> str:
    """
    Düz metin dosyasını okur.
    UTF-8 encoding varsayılan, hata durumunda latin-1 dener.
    """

def parse_xlsx(file_path: str) -> str:
    """
    openpyxl ile tüm sheet'leri okur.
    Her satırı metin olarak birleştirir.
    Format: "SütunAdı: Değer | SütunAdı: Değer"
    """

def parse_file(file_path: str, file_type: str) -> str:
    """Router fonksiyon — doğru parser'ı çağırır"""
```

### 3.2 — Metin Bölümleme (`utils/chunker.py`)

```python
from langchain_text_splitters import RecursiveCharacterTextSplitter

def chunk_text(text: str, chunk_size: int = 500, overlap: int = 50) -> list[str]:
    """
    LangChain RecursiveCharacterTextSplitter kullanır.
    chunk_size: karakter sayısı (config'den gelir)
    overlap: örtüşen karakter sayısı (bağlamı korur)
    Boş chunk'ları filtreler.
    Returns: chunk string listesi
    """
```

**Neden chunk'lama?**
- LLM'lerin context window sınırı var
- Tüm dokümanı göndermek hem pahalı hem yavaş
- Sadece soruyla alakalı parçaları göndermek daha verimli

### 3.3 — Embedding Servisi (`services/embedding_service.py`)

```python
from sentence_transformers import SentenceTransformer

class EmbeddingService:
    def __init__(self, model_name: str):
        """
        Model: paraphrase-multilingual-mpnet-base-v2
        Bu model Türkçe metinlerde iyi performans verir.
        768 boyutlu vektör üretir.
        """
        self.model = SentenceTransformer(model_name)

    def embed_texts(self, texts: list[str]) -> list[list[float]]:
        """
        Bir liste metni vektöre dönüştürür.
        Returns: her metin için 768 float'lık liste
        """

    def embed_query(self, query: str) -> list[float]:
        """
        Tek sorgu metnini vektöre dönüştürür.
        """
```

### 3.4 — Qdrant Vektör Deposu (`vector_store.py`)

```python
from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams, PointStruct

class VectorStore:
    def __init__(self, host: str, port: int):
        self.client = QdrantClient(host=host, port=port)

    def ensure_collection(self, collection_name: str, vector_size: int = 768):
        """
        Koleksiyon yoksa oluşturur.
        Varsa üzerine yazmaz.
        Distance: Cosine (metin benzerliği için en uygun)
        """

    def add_documents(
        self,
        collection_name: str,
        chunks: list[str],
        embeddings: list[list[float]],
        metadata: dict  # document_id, category_id, unit_id, filename
    ) -> list[str]:
        """
        Her chunk için bir Point oluşturur.
        Point yapısı:
          - id: UUID (otomatik üretilir)
          - vector: embedding listesi
          - payload: {
              "text": chunk metni,
              "document_id": int,
              "category_id": int,
              "unit_id": int,
              "filename": str
            }
        """

    def search(
        self,
        collection_name: str,
        query_vector: list[float],
        top_k: int = 5,
        filter_unit_id: int = None,
        filter_category_id: int = None
    ) -> list[dict]:
        """
        Vektör benzerlik araması yapar.
        Filtreler: unit_id ve/veya category_id payload filter olarak uygulanır.
        Returns: [{"text": ..., "score": ..., "metadata": {...}}]
        """

    def delete_document(self, collection_name: str, document_id: int):
        """
        Belirli document_id'ye ait tüm chunk'ları siler.
        """
```

### 3.5 — Doküman İşleme Servisi (`services/document_processor.py`)

```python
async def process_document(
    document_id: int,
    file_path: str,
    file_type: str,
    metadata: dict,
    db: AsyncSession
):
    """
    Tam pipeline:
    1. parse_file() → ham metin
    2. chunk_text() → chunk listesi
    3. embedding_service.embed_texts() → vektörler
    4. vector_store.add_documents() → Qdrant'a kaydet
    5. DB'de documents tablosunu güncelle:
       - chunk_count = len(chunks)
       - status = 'ready'
    Hata durumunda status = 'error', error_message doldur
    """
```

### 3.6 — Doküman Router (`routers/documents.py`)

| Metod | Endpoint | Açıklama |
|-------|----------|-----------|
| POST | `/api/documents/upload` | Dosya yükle (multipart/form-data) |
| GET | `/api/documents` | Kullanıcının birimindeki dokümanlar |
| GET | `/api/documents/{id}` | Tek doküman detayı |
| DELETE | `/api/documents/{id}` | Dokümanı sil (DB + Qdrant) |

**Upload endpoint detayı:**
```python
@router.post("/upload")
async def upload_document(
    file: UploadFile = File(...),
    category_id: int = Form(...),
    current_user = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    # 1. Dosya boyutunu kontrol et (MAX_FILE_SIZE_MB)
    # 2. Dosya tipini kontrol et (pdf, docx, txt, xlsx)
    # 3. Dosyayı /tmp/uploads/{uuid}_{filename} konumuna kaydet
    # 4. DB'ye documents kaydı ekle (status='processing')
    # 5. BackgroundTasks ile process_document() çağır
    # 6. Hemen document kaydını döndür (işlem arka planda devam eder)
```

**FAZ 3 Tamamlanma Kriteri:**
- [ ] PDF yükleniyor → Qdrant'ta chunk'lar görünüyor
- [ ] DOCX yükleniyor → metin başarıyla çıkarılıyor
- [ ] Yükleme sonrası `status: 'ready'` oluyor
- [ ] `/api/documents` listeleme çalışıyor

---

## ✅ FAZ 4 — RAG Sorgu ve AI Agent Sistemi

> **Amaç:** Kullanıcı soru soruyor → ilgili chunk'lar getiriliyor → LLM cevap üretiyor.

### 4.1 — RAG Servisi (`services/rag_service.py`)

**RAG nasıl çalışır? (Yeni başlayanlar için):**
1. Kullanıcının sorusu embedding'e dönüştürülür
2. Qdrant'ta bu embedding'e en yakın chunk'lar bulunur (cosine similarity)
3. Bulunan chunk'lar "context" olarak LLM'e gönderilir
4. LLM, kendi bilgisini değil context'i kullanarak cevap üretir

```python
class RAGService:
    def __init__(self, vector_store, embedding_service, llm_client):
        ...

    async def query(
        self,
        question: str,
        unit_id: int,
        category_id: int = None,
        top_k: int = 5
    ) -> dict:
        """
        Tam RAG akışı:
        
        1. Soruyu embed et:
           query_vector = embedding_service.embed_query(question)
        
        2. Qdrant'ta ara:
           results = vector_store.search(
               collection_name=COLLECTION,
               query_vector=query_vector,
               top_k=top_k,
               filter_unit_id=unit_id,
               filter_category_id=category_id
           )
        
        3. Context oluştur:
           context = "\n---\n".join([r["text"] for r in results])
        
        4. Prompt hazırla:
           system_prompt = '''
           Sen bir kurumsal bilgi asistanısın.
           Sadece sana verilen bağlam (context) bilgilerini kullanarak cevap ver.
           Eğer bağlamda cevap yoksa "Bu konuda belgelerimde yeterli bilgi bulamadım." de.
           Cevabın sonunda hangi belgelerden yararlandığını belirt.
           '''
           user_prompt = f"Bağlam:\n{context}\n\nSoru: {question}"
        
        5. LLM çağır (seçilen provider'a göre)
        
        6. Sonucu döndür:
           {
             "answer": "LLM cevabı",
             "sources": [
               {"filename": "...", "score": 0.92, "chunk_preview": "..."}
             ]
           }
        """
```

### 4.2 — Agent Servisi (`services/agent_service.py`)

Agent, basit RAG'dan farklı olarak **çok adımlı düşünme** yapabilir:

```python
from langchain.agents import AgentExecutor, create_react_agent
from langchain.tools import Tool

class AgentService:
    """
    LangChain ReAct Agent kurulumu.
    
    Agent araçları (tools):
    
    Tool 1: rag_search
    - Girdi: arama sorgusu (string)
    - Çıktı: ilgili chunk'lar
    - Ne zaman kullanılır: Belgelerde bilgi aranacağında
    
    Tool 2: calculate
    - Girdi: matematiksel ifade
    - Çıktı: sonuç
    - Ne zaman kullanılır: Rakamsal hesaplamalar gerektiğinde
    
    Tool 3: get_document_list
    - Girdi: kategori filtresi (opsiyonel)
    - Çıktı: mevcut doküman listesi
    - Ne zaman kullanılır: "Hangi belgeler var?" soruları için
    
    Agent prompt şablonu (ReAct):
    "Düşün → Araç seç → Araç çalıştır → Gözlemle → Tekrar et → Cevapla"
    """

    async def run(self, question: str, unit_id: int, category_id: int = None) -> dict:
        """
        Agent'ı çalıştırır.
        Karmaşık sorular için önce RAG yapar, gerekirse hesaplar.
        Returns: {"answer": str, "steps": [...], "sources": [...]}
        """
```

### 4.3 — Sorgu Router (`routers/query.py`)

| Metod | Endpoint | Açıklama |
|-------|----------|-----------|
| POST | `/api/query/rag` | Basit RAG sorgusu |
| POST | `/api/query/agent` | Agent ile gelişmiş sorgu |
| GET | `/api/query/history` | Kullanıcının sorgu geçmişi |

**RAG sorgu isteği:**
```json
{
  "question": "Geçen ay en çok satan ürünler nelerdir?",
  "category_id": 3,
  "use_agent": false
}
```

**RAG sorgu yanıtı:**
```json
{
  "answer": "Belgelerinize göre geçen ay...",
  "sources": [
    {
      "filename": "satis_raporu.pdf",
      "score": 0.94,
      "chunk_preview": "Ocak ayı satış verileri..."
    }
  ],
  "response_time_ms": 1240
}
```

### 4.4 — Kategori Router (`routers/categories.py`)

| Metod | Endpoint | Açıklama |
|-------|----------|-----------|
| GET | `/api/categories` | Birimin kategorileri |
| POST | `/api/categories` | Yeni kategori oluştur |
| PUT | `/api/categories/{id}` | Kategori güncelle |
| DELETE | `/api/categories/{id}` | Kategori sil |

**FAZ 4 Tamamlanma Kriteri:**
- [ ] Yüklenen doküman hakkında soru sorulabiliyor
- [ ] Cevap gerçekten doküman içeriğine dayanıyor
- [ ] Kaynaklar (hangi dosyadan) gösteriliyor
- [ ] Kategori filtresi çalışıyor

---

## ✅ FAZ 5 — Dışa Aktarma (PDF / Word / Excel)

> **Amaç:** Oluşturulan cevaplar ve sorgular belge formatında indirilebilir.

### 5.1 — Export Servisi (`services/export_service.py`)

#### PDF Çıktısı (reportlab)
```python
def export_to_pdf(content: dict) -> bytes:
    """
    Içerik yapısı:
    {
      "title": "RAG Sorgu Raporu",
      "date": "2024-01-15",
      "question": "...",
      "answer": "...",
      "sources": [...]
    }
    
    PDF düzeni:
    - Başlık (büyük font)
    - Tarih
    - Soru (kalın)
    - Cevap metni
    - Kaynaklar bölümü (tablo formatında)
    - Logo/header opsiyonel
    
    Returns: bytes (PDF dosyasının binary içeriği)
    """
```

#### Word Çıktısı (python-docx)
```python
def export_to_docx(content: dict) -> bytes:
    """
    python-docx ile Word belgesi oluşturur.
    Heading 1: Başlık
    Normal: Soru ve cevap
    Tablo: Kaynaklar listesi
    Returns: bytes
    """
```

#### Excel Çıktısı (openpyxl)
```python
def export_to_xlsx(query_history: list[dict]) -> bytes:
    """
    Sorgu geçmişini Excel'e aktarır.
    Sütunlar: Tarih | Soru | Cevap | Kaynaklar | Yanıt Süresi (ms)
    Sheet adı: "Sorgu Geçmişi"
    Header satırı kalın ve arka plan renkli
    Returns: bytes
    """
```

### 5.2 — Export Router (`routers/export.py`)

| Metod | Endpoint | Açıklama |
|-------|----------|-----------|
| POST | `/api/export/pdf` | Tek cevabı PDF olarak indir |
| POST | `/api/export/docx` | Tek cevabı Word olarak indir |
| GET | `/api/export/xlsx` | Sorgu geçmişini Excel olarak indir |

**Response tipi:** `FileResponse` veya `StreamingResponse` ile binary dosya döner.

**Content-Type ve header:**
```python
return StreamingResponse(
    io.BytesIO(pdf_bytes),
    media_type="application/pdf",
    headers={"Content-Disposition": "attachment; filename=rapor.pdf"}
)
```

**FAZ 5 Tamamlanma Kriteri:**
- [ ] `/api/export/pdf` çağrısı gerçek PDF dosyası indiriyor
- [ ] `/api/export/docx` Word belgesi olarak açılabiliyor
- [ ] `/api/export/xlsx` Excel'de sütunlar düzgün görünüyor

---

## ✅ FAZ 6 — React Frontend

> **Amaç:** Tüm backend işlevleri kullanıcı dostu arayüzle erişilebilir.

### 6.1 — Proje Kurulumu

```bash
npx create-react-app frontend --template typescript
# veya
npm create vite@latest frontend -- --template react-ts

cd frontend
npm install axios react-router-dom zustand @tanstack/react-query
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init
```

### 6.2 — Sayfalar (Pages)

#### `/login` — Giriş Sayfası
- Email ve şifre alanları
- "Giriş Yap" butonu → `/api/auth/login`
- Token localStorage'a kaydedilir
- Başarılı giriş → `/dashboard`'a yönlendirme

#### `/dashboard` — Ana Panel
- Sol menü: Kategoriler listesi
- Üst bar: Kullanıcı adı + birim adı + çıkış butonu
- Ana içerik: Son sorgular özeti (son 5 sorgu kartı)
- Hızlı soru kutusu

#### `/documents` — Doküman Yönetimi
- Sürükle-bırak dosya yükleme alanı
- Kategori seçimi (dropdown)
- Yüklenen dokümanlar tablosu (ad, tip, durum, tarih, sil butonu)
- Yükleme durumu: `processing` → spinner, `ready` → yeşil, `error` → kırmızı

#### `/query` — Sorgulama Sayfası
**Bu sayfanın UI detayları:**
- Büyük soru giriş alanı (textarea)
- Kategori filtresi dropdown (opsiyonel)
- "RAG Sorgula" ve "Agent ile Sorgula" butonları
- Cevap kutusu: markdown render edilmiş metin
- Kaynaklar bölümü: her kaynak için kart (dosya adı + skor + önizleme)
- Dışa aktar: PDF / Word / Excel butonları (sağ üst köşe)
- Yükleniyor: skeleton animasyonu veya spinner

#### `/history` — Sorgu Geçmişi
- Tablo: Tarih | Soru (kısaltılmış) | Yanıt süresi
- Satıra tıklayınca sorgu detayı modal açılır
- "Excel'e Aktar" butonu

### 6.3 — API Servis Katmanı (`src/services/api.ts`)

```typescript
// Axios instance
const api = axios.create({
  baseURL: "http://localhost:8000/api",
});

// Her istekte token ekle
api.interceptors.request.use((config) => {
  const token = localStorage.getItem("token");
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// 401 durumunda login'e yönlendir
api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401) window.location.href = "/login";
    return Promise.reject(err);
  }
);

// Fonksiyonlar
export const authAPI = { login, register, getMe };
export const documentAPI = { upload, list, delete: deleteDoc };
export const categoryAPI = { list, create, update, delete: deleteCat };
export const queryAPI = { ragQuery, agentQuery, getHistory };
export const exportAPI = { toPdf, toDocx, toXlsx };
```

### 6.4 — Dosya Yükleme Bileşeni

```tsx
// DropZone component
// - react-dropzone kullanır (veya native drag-and-drop)
// - Kabul edilen tipler: .pdf, .docx, .txt, .xlsx
// - Dosya boyutu kontrolü (50MB limit)
// - Yükleme progress bar gösterir
// - Başarı/hata durumu göstergesi
```

**FAZ 6 Tamamlanma Kriteri:**
- [ ] Giriş yapılabiliyor, token saklanıyor
- [ ] Doküman yüklenebiliyor, liste görünüyor
- [ ] Soru sorulabiliyor, cevap ve kaynaklar görünüyor
- [ ] PDF/Excel export çalışıyor
- [ ] Sayfa yenilendiğinde oturum korunuyor

---

## ✅ FAZ 7 — Test ve Hata Yönetimi

### 7.1 — Backend Testleri

Her endpoint için `tests/` klasöründe test dosyaları:

```python
# tests/test_documents.py
def test_upload_pdf():
    """PDF başarıyla yüklenir, status 'processing' döner"""

def test_upload_invalid_type():
    """Geçersiz dosya tipi → 400 hatası döner"""

def test_delete_document():
    """Doküman hem DB'den hem Qdrant'tan silinir"""
```

### 7.2 — Hata Senaryoları ve Yanıtları

| Senaryo | HTTP Kodu | Mesaj |
|---------|-----------|-------|
| Desteklenmeyen dosya tipi | 400 | "Sadece PDF, DOCX, TXT, XLSX desteklenir" |
| Dosya boyutu aşımı | 413 | "Dosya 50MB sınırını aşıyor" |
| Yetki yok | 403 | "Bu işlem için yetkiniz yok" |
| Doküman bulunamadı | 404 | "Doküman bulunamadı" |
| LLM API hatası | 503 | "AI servisi şu an kullanılamıyor" |
| Qdrant bağlantı hatası | 503 | "Vektör veritabanına ulaşılamıyor" |

### 7.3 — Logging

```python
import logging

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s"
)

# Her önemli adımda log:
logger.info(f"Doküman yükleniyor: {filename}")
logger.info(f"Chunk sayısı: {len(chunks)}")
logger.error(f"Embedding hatası: {str(e)}")
```

---

## ✅ FAZ 8 — Docker Entegrasyonu ve Dağıtım

### 8.1 — Backend Dockerfile

```dockerfile
# backend/Dockerfile
FROM python:3.11-slim

WORKDIR /app

# Sistem bağımlılıkları (PDF işleme için)
RUN apt-get update && apt-get install -y \
    libgl1-mesa-glx \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
```

### 8.2 — Frontend Dockerfile

```dockerfile
# frontend/Dockerfile
FROM node:20-alpine as build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
```

### 8.3 — docker-compose.yml Güncellemesi

Mevcut `docker-compose.yml`'e eklenecekler:

```yaml
  # Backend API
  backend:
    build: ./backend
    container_name: ai_rag_backend
    restart: unless-stopped
    env_file: ./backend/.env
    ports:
      - "8000:8000"
    volumes:
      - ./uploads:/app/uploads
    depends_on:
      postgres:
        condition: service_healthy
      qdrant:
        condition: service_healthy

  # Frontend
  frontend:
    build: ./frontend
    container_name: ai_rag_frontend
    restart: unless-stopped
    ports:
      - "3000:80"
    depends_on:
      - backend
```

---

## 📌 Önemli Notlar (Yeni Başlayanlar İçin)

### RAG Nedir? (Kısa Özet)
- **R**etrieval: Soruya en yakın belge parçalarını bul
- **A**ugmented: Bu parçalarla LLM'i güçlendir
- **G**eneration: LLM, belge içeriğini kullanarak cevap üretsin

### Qdrant Nedir?
- Vektör veritabanı → yüksek boyutlu sayı dizilerini saklar ve arar
- Metin anlamı sayıya dönüştürüldükten sonra buraya kaydedilir
- "Bu soruya en çok benzeyen metinleri bul" işini Qdrant yapar

### Embedding Nedir?
- Metni anlam koruyan sayı dizisine dönüştürme işlemi
- "Elma" ve "meyve" birbirine yakın vektör üretir
- "Elma" ve "araba" çok farklı vektör üretir

### Chunk'lama Neden Gerekli?
- 100 sayfalık PDF'i direkt LLM'e gönderemezsiniz (çok büyük)
- Küçük parçalara bölünür, sadece soruyla ilgili parçalar gönderilir
- Hem maliyet hem doğruluk açısından çok daha verimli

---

## 🔁 Geliştirme Sırası (Özet)

```
FAZ 1 (Altyapı)     → docker-compose up → FastAPI ayakta
FAZ 2 (Auth)        → kullanıcı sistemi → JWT çalışıyor
FAZ 3 (Doküman)     → upload → parse → embed → Qdrant
FAZ 4 (Sorgu/RAG)   → soru sor → cevap al → kaynak gör
FAZ 5 (Export)      → PDF/Word/Excel indir
FAZ 6 (Frontend)    → React arayüzü
FAZ 7 (Test)        → hata kontrolü
FAZ 8 (Docker)      → tam containerize
```

---

## 📦 Hızlı Başlangıç Komutları

```bash
# 1. Altyapıyı başlat
docker-compose up -d postgres qdrant

# 2. Backend bağımlılıklarını kur
cd backend
pip install -r requirements.txt

# 3. Veritabanı migration'larını çalıştır
alembic upgrade head

# 4. Backend'i başlat
uvicorn app.main:app --reload --port 8000

# 5. Frontend'i başlat (ayrı terminal)
cd frontend
npm install
npm run dev

# 6. Test et
curl http://localhost:8000/health
# {"status": "ok"}
```

---

*Bu task dosyası vibecodinge uygun şekilde faz faz ilerlenecek şekilde hazırlanmıştır. Her fazı tamamladıktan sonra bir sonrakine geçin.*
