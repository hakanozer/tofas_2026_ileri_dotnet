"""
LangChain entegrasyonu.
PromptTemplate + Ollama + OutputParser zinciri.
Modül 4 (RAG) ve Modül 5 (Agent) için temel oluşturur.
"""
from langchain_community.llms import Ollama  # Yerel LLM entegrasyonu
from langchain_core.prompts import ChatPromptTemplate  # Prompt şablonu oluşturmak için
from langchain_core.output_parsers import StrOutputParser  # LLM çıktısını ayrıştırır

from app.core.config import settings


def build_product_summary_chain():
    """
    Ürün özetleme zinciri.
    RAG'dan gelen ham ürün verilerini kullanıcı dostu metne dönüştürür.
    """
    llm = Ollama(
        base_url=settings.ollama_base_url,
        model=settings.ollama_model,
        temperature=0.3,
    )

    prompt = ChatPromptTemplate.from_messages([
        (
            "system",
            "Sen bir e-ticaret asistanısın. Kullanıcıya ürünleri samimi ve "
            "yardımsever bir dille açıkla. Teknik detayları sade bir dille anlat.",
        ),
        (
            "human",
            "Kullanıcının sorusu: {query}\n\n"
            "Bulunan ürünler:\n{products}\n\n"
            "Kullanıcıya en uygun ürünü önererek açıkla:",
        ),
    ])

    chain = prompt | llm | StrOutputParser()
    return chain


# Kullanım örneği
async def summarize_products(query: str, products: list[dict]) -> str:
    """Ürünleri kullanıcıya doğal dilde anlatan özet üretir."""
    chain = build_product_summary_chain()

    products_text = "\n".join([
        f"- {p.get('name', 'Ürün')}: {p.get('price', 0)} TL, "
        f"Özellikler: {', '.join(p.get('features', []))}"
        for p in products[:5]
    ])

    return await chain.ainvoke({
        "query": query,
        "products": products_text,
    })