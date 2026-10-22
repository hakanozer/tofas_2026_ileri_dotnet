"""
Agent test scripti.
Çalıştırma: python scripts/test_agent.py
"""
import asyncio  # Asenkron programlama
from app.agent.planner import commerce_agent


async def main():
    test_queries = [
        "1000 TL altı terletmeyen spor ayakkabı öner",
        "Nike ile Adidas spor ayakkabılarını karşılaştır",
        "Stokta olan yazlık koşu ayakkabısı var mı?",
        "Tüm sonuçları pdf olarak yazdır."
    ]

    for query in test_queries:
        print(f"\n{'='*60}")
        print(f"SORGU: {query}")
        print("="*60)

        response = await commerce_agent.run(query)

        print(f"\nYANIT:\n{response.answer}")
        print(f"\nKullanılan araçlar: {response.tools_used}")
        print(f"Adım sayısı: {len(response.steps)}")


if __name__ == "__main__":
    asyncio.run(main())