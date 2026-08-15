import httpx

from app.embeddings.provider import EmbeddingProvider


class OllamaEmbeddingProvider(EmbeddingProvider):
    """Talks to Ollama's OpenAI-compatible /v1/embeddings endpoint. Ollama runs on the
    physical host, not this container or the VM — see ADR 0011/0012 for the connectivity
    story."""

    def __init__(self, base_url: str, model: str):
        self._base_url = base_url.rstrip("/")
        self._model = model

    async def embed(self, texts: list[str]) -> tuple[list[list[float]], str]:
        async with httpx.AsyncClient(timeout=60.0) as client:
            response = await client.post(
                f"{self._base_url}/embeddings",
                json={"model": self._model, "input": texts},
            )
            response.raise_for_status()
            data = response.json()

        embeddings = [item["embedding"] for item in data["data"]]
        model_used = data.get("model") or self._model
        return embeddings, model_used
