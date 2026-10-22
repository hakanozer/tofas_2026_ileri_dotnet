"""
Ollama REST API istemcisi.
Async HTTP ile Ollama'ya istek atar, hata yönetimi yapar.
"""
from __future__ import annotations

import json  # JSON okuma/yazma işlemleri
import httpx  # Asenkron HTTP istemcisi
from typing import AsyncIterator  # Tip ipuçları için

from app.core.config import settings


class OllamaClient:
    """
    Ollama REST API için async istemci.
    Projede singleton olarak kullanılır (lifespan'de oluşturulur).
    """

    def __init__(
        self,
        base_url: str = settings.ollama_base_url,
        model: str = settings.ollama_model,
        timeout: float = 120.0,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.model = model
        self._client = httpx.AsyncClient(timeout=timeout)

    async def generate(
        self,
        prompt: str,
        system: str | None = None,
        temperature: float = 0.1,
        stream: bool = False,
    ) -> str:
        """
        Ollama /api/generate endpoint'ine istek atar.

        Args:
            prompt: Kullanıcı girdisi veya tam prompt
            system: Sistem promptu (modelin rolünü belirler)
            temperature: 0=deterministik, 1=yaratıcı. Intent için düşük tut.
            stream: True ise token token akış döner

        Returns:
            Modelin ürettiği metin
        """
        payload: dict = {
            "model": self.model,
            "prompt": prompt,
            "stream": stream,
            "options": {
                "temperature": temperature,
                "num_predict": 512,    # Maksimum üretilecek token sayısı
                "top_p": 0.9,
                "top_k": 40,
            },
        }

        if system:
            payload["system"] = system

        response = await self._client.post(
            f"{self.base_url}/api/generate",
            json=payload,
        )
        response.raise_for_status()

        data = response.json()
        return data.get("response", "")

    async def chat(
        self,
        messages: list[dict[str, str]],
        temperature: float = 0.1,
    ) -> str:
        """
        Ollama /api/chat endpoint'ine istek atar.
        Çok turlu konuşmalar için kullanın.

        Args:
            messages: [{"role": "system"|"user"|"assistant", "content": "..."}]
            temperature: Yanıt yaratıcılığı

        Returns:
            Asistan yanıtı
        """
        payload = {
            "model": self.model,
            "messages": messages,
            "stream": False,
            "options": {"temperature": temperature},
        }

        response = await self._client.post(
            f"{self.base_url}/api/chat",
            json=payload,
        )
        response.raise_for_status()

        data = response.json()
        return data.get("message", {}).get("content", "")

    async def is_available(self) -> bool:
        """Ollama servisinin çalışıp çalışmadığını kontrol eder."""
        try:
            response = await self._client.get(
                f"{self.base_url}/api/tags",
                timeout=3.0,
            )
            return response.status_code == 200
        except Exception:
            return False

    async def close(self) -> None:
        """HTTP istemcisini kapat (lifespan sonunda çağır)."""
        await self._client.aclose()


# Uygulama genelinde paylaşılan tek instance
ollama_client = OllamaClient()